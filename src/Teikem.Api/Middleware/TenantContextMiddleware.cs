using System.Security.Claims;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Middleware;

/// <summary>
/// Llena TenantContext desde el principal (JWT): TenantId del claim `tid`, NUNCA del request. Fija CorrelationId
/// (X-Correlation-Id o nuevo), IP, User-Agent e idioma (Accept-Language / X-Lang).
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        var correlation = http.Request.Headers.TryGetValue(CorrelationHeader, out var h) && Guid.TryParse(h.FirstOrDefault(), out var g) ? g : Guid.NewGuid();
        tenant.CorrelationId = correlation;
        http.Response.Headers[CorrelationHeader] = correlation.ToString();
        tenant.IpAddress = http.Connection.RemoteIpAddress?.ToString();
        tenant.UserAgent = http.Request.Headers.UserAgent.FirstOrDefault();
        tenant.Lang = ResolveLang(http);

        var user = http.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            tenant.IsAuthenticated = true;
            if (int.TryParse(user.FindFirst(TeikemClaims.Subject)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) tenant.UserId = uid;
            if (int.TryParse(user.FindFirst(TeikemClaims.TenantId)?.Value, out var tid)) tenant.TenantId = tid;
            tenant.IsPlatformAdmin = user.FindFirst(TeikemClaims.PlatformAdmin)?.Value == "1";
            if (long.TryParse(user.FindFirst(TeikemClaims.Aal2At)?.Value, out var aal2)) tenant.Aal2VerifiedAtUtc = DateTimeOffset.FromUnixTimeSeconds(aal2).UtcDateTime;
        }
        await next(http);
    }

    private static string ResolveLang(HttpContext http)
    {
        var explicitLang = http.Request.Headers["X-Lang"].FirstOrDefault() ?? http.Request.Query["lang"].FirstOrDefault();
        var raw = explicitLang ?? http.Request.Headers.AcceptLanguage.FirstOrDefault()?.Split(',').FirstOrDefault()?.Split(';').FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return "es";
        var l = raw.Trim().ToLowerInvariant();
        return l.StartsWith("en") ? "en" : "es";
    }
}

/// <summary>Traduce excepciones de dominio a ProblemDetails; lo demás es 500 con CorrelationId para rastrear.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        try { await next(http); }
        catch (Teikem.Infrastructure.Exceptions.TeikemException ex)
        {
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = ex.StatusCode;
            await http.Response.WriteAsJsonAsync(new
            {
                type = "https://teikem.app/errors/" + ex.Code, title = ex.Message, status = ex.StatusCode, code = ex.Code,
                errors = ex.Errors, correlationId = tenant.CorrelationId,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error no controlado. Correlation {Correlation}", tenant.CorrelationId);
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = 500;
            await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/internal", title = "Error interno.", status = 500, code = "internal", correlationId = tenant.CorrelationId });
        }
    }
}
