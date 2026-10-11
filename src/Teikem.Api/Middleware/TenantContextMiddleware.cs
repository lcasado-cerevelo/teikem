using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Middleware;

/// <summary>
/// Llena TenantContext desde el principal (JWT): TenantId del claim `tid`, NUNCA del request. Fija CorrelationId
/// (X-Correlation-Id o nuevo), IP, User-Agent e idioma (Accept-Language / X-Lang).
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http, TenantContext tenant, TeikemDbContext db)
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
            await LoadDataScopeAsync(tenant, db, http.RequestAborted);
        }
        await next(http);
    }

    /// <summary>
    /// Alcance de datos (UserDataScope): lee del usuario sus almacenes y clientes permitidos y los deja en el contexto, para que el filtro
    /// global de TeikemDbContext los aplique. Sin filas de un tipo = sin restricción de ese tipo. Nadie se exenta (ni administradores).
    /// </summary>
    private static async Task LoadDataScopeAsync(TenantContext tenant, TeikemDbContext db, CancellationToken ct)
    {
        if (tenant.TenantId is null || tenant.UserId is null || tenant.IsPlatformAdmin) return;
        var uid = tenant.UserId.Value;
        var rows = await db.UserDataScopes.AsNoTracking().Where(s => s.UserId == uid)
            .Select(s => new { Entity = s.ScopeEntity!.InternalCode, s.ScopeId }).ToListAsync(ct);
        if (rows.Count == 0) return;
        var warehouses = rows.Where(r => r.Entity == Teikem.Domain.Constants.EntityTypes.Warehouse).Select(r => r.ScopeId).ToList();
        var clients = rows.Where(r => r.Entity == Teikem.Domain.Constants.EntityTypes.Client).Select(r => r.ScopeId).ToList();
        if (warehouses.Count > 0) tenant.ScopedWarehouseIds = warehouses;
        if (clients.Count > 0) tenant.ScopedClientIds = clients;
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
            await http.Response.WriteAsJsonAsync(ProblemBody(ex, tenant.CorrelationId));
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // el cuerpo superó el máximo del endpoint (p. ej. subida de logo sin Content-Length): 413 con el formato de siempre
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/payload_too_large", title = "El archivo supera el tamaño máximo permitido.", status = 413, code = "payload_too_large", correlationId = tenant.CorrelationId });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error no controlado. Correlation {Correlation}", tenant.CorrelationId);
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = 500;
            await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/internal", title = "Error interno.", status = 500, code = "internal", correlationId = tenant.CorrelationId });
        }
    }

    /// <summary>Cuerpo ProblemDetails de una excepción de dominio (lo reutiliza IdempotencyMiddleware para guardar la respuesta).</summary>
    public static object ProblemBody(Teikem.Infrastructure.Exceptions.TeikemException ex, Guid correlationId) => new
    {
        type = "https://teikem.app/errors/" + ex.Code, title = ex.Message, status = ex.StatusCode, code = ex.Code,
        errors = ex.Errors, correlationId,
    };
}
