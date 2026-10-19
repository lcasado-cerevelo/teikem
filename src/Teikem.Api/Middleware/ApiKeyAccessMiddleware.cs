using System.Diagnostics;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Middleware;

/// <summary>
/// Lote 135 (P11) — la API de los clientes. Una sesión de LLAVE DE API (token con kind=API) solo puede llamar a las rutas que sus alcances abren (<see cref="ApiAccessRules"/>): todo lo demás es 403 aunque el
/// token sea válido y la cuenta de servicio tenga el permiso. Además: la llave debe seguir activa (una revocada deja de servir de inmediato, aunque su token siga vivo), cada solicitud cuenta contra el
/// límite de uso de la llave (429 con Retry-After; cabeceras X-RateLimit-*) y queda en la bitácora de la llave. Las sesiones de personas no pasan por aquí.
/// </summary>
public sealed class ApiKeyAccessMiddleware(RequestDelegate next, ILogger<ApiKeyAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant, ApiKeyService keys)
    {
        if (!tenant.IsAuthenticated || !tenant.IsApiKey) { await next(http); return; }

        var keyId = http.User.FindFirst(TeikemClaims.ApiKeyId)?.Value ?? string.Empty;
        var scopes = (http.User.FindFirst(TeikemClaims.Scopes)?.Value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var resolved = await keys.ResolveKeyAsync(keyId, http.RequestAborted);
        if (resolved is not { Active: true } key)
        {
            await Problem(http, tenant, StatusCodes.Status401Unauthorized, "unauthorized", Teikem.Domain.Integrations.ApiKeyRules.InvalidCredentialsMessage);
            if (resolved is { } revoked)   // una llave revocada que sigue llamando también queda anotada
            {
                try { await keys.LogRequestAsync(tenant.TenantId ?? 0, revoked.Id, http.Request.Method, http.Request.Path.Value ?? string.Empty, StatusCodes.Status401Unauthorized, 0, CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "No se pudo anotar la solicitud de la llave {Key}.", keyId); }
            }
            return;
        }

        var watch = Stopwatch.StartNew();
        var gate = keys.Limiter.TryAcquire(key.Id, key.RateLimit);
        http.Response.Headers["X-RateLimit-Limit"] = key.RateLimit.ToString();
        http.Response.Headers["X-RateLimit-Remaining"] = gate.Remaining.ToString();
        if (!gate.Allowed)
        {
            http.Response.Headers["Retry-After"] = gate.RetryAfterSeconds.ToString();
            await Problem(http, tenant, StatusCodes.Status429TooManyRequests, "rate_limited", ApiKeyRules.RateLimitedMessage(gate.RetryAfterSeconds));
        }
        else if (!ApiAccessRules.IsAllowed(http.Request.Method, http.Request.Path.Value, scopes, out var needed))
            await Problem(http, tenant, StatusCodes.Status403Forbidden, ApiAccessRules.ForbiddenCode, ApiAccessRules.ForbiddenMessage(needed));
        else
            await next(http);

        // la bitácora de la llave nunca rompe la respuesta
        try { await keys.LogRequestAsync(tenant.TenantId ?? 0, key.Id, http.Request.Method, http.Request.Path.Value ?? string.Empty, http.Response.StatusCode, (int)watch.ElapsedMilliseconds, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudo anotar la solicitud de la llave {Key}.", keyId); }
    }

    private static Task Problem(HttpContext http, TenantContext tenant, int status, string code, string title)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/" + code, title, status, code, correlationId = tenant.CorrelationId });
    }
}
