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
            // Compañía desactivada por la plataforma (2026-10-26): quien ya tenía una sesión no pasa (la plataforma sí, para poder volver a activarla).
            if (tenant.TenantId is int activeTid && !tenant.IsPlatformAdmin && !await TenantStatusGuard.IsActiveAsync(db, activeTid, http.RequestAborted))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/forbidden", title = TenantStatusGuard.InactiveMessage, status = 403, code = "forbidden", correlationId = correlation });
                return;
            }
            if (long.TryParse(user.FindFirst(TeikemClaims.Aal2At)?.Value, out var aal2)) tenant.Aal2VerifiedAtUtc = DateTimeOffset.FromUnixTimeSeconds(aal2).UtcDateTime;
            // Sesión de PORTAL (Lote 101): el cliente del claim `cid` es el alcance de datos (segundo filtro, además de la compañía); no se carga el alcance del staff.
            if (user.FindFirst(TeikemClaims.UserKind)?.Value == Teikem.Domain.Constants.UserKinds.Portal)
            {
                tenant.IsPortal = true;
                if (int.TryParse(user.FindFirst(TeikemClaims.ClientId)?.Value, out var cid)) { tenant.PortalClientId = cid; tenant.ScopedClientIds = new[] { cid }; }
                else tenant.ScopedClientIds = new[] { -1 };   // un token de portal sin cliente no ve nada
                if (int.TryParse(user.FindFirst(TeikemClaims.LocationId)?.Value, out var loc)) tenant.PortalLocationId = loc;   // Lote 117: consignatario
            }
            else if (user.FindFirst(TeikemClaims.UserKind)?.Value == Teikem.Domain.Integrations.ApiAccessRules.UserKind)
            {
                // Llave de API (Lote 135): si la integración tiene cliente, ese cliente es el alcance de datos (segundo filtro, además de la compañía), igual que el portal.
                tenant.IsApiKey = true;
                if (int.TryParse(user.FindFirst(TeikemClaims.ClientId)?.Value, out var apiCid)) tenant.ScopedClientIds = new[] { apiCid };
            }
            else await LoadDataScopeAsync(tenant, db, http.RequestAborted);
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
            if (ex is Teikem.Infrastructure.Exceptions.RateLimitedException limited) http.Response.Headers["Retry-After"] = limited.RetryAfterSeconds.ToString();
            await http.Response.WriteAsJsonAsync(ProblemBody(ex, tenant.CorrelationId));
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // el cuerpo superó el máximo del endpoint (p. ej. subida de logo sin Content-Length): 413 con el formato de siempre
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/payload_too_large", title = "El archivo supera el tamaño máximo permitido.", status = 413, code = "payload_too_large", correlationId = tenant.CorrelationId });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not Teikem.Infrastructure.Exceptions.TeikemException && http.RequestAborted.IsCancellationRequested)
        {
            // El cliente ya cerró la conexión (cambió de pantalla, recargó, cortó la red): SQL Server aborta la consulta en vuelo y SqlClient lo
            // reporta como «severe error / Operation cancelled by user». No es un fallo del servidor: nadie recibirá la respuesta, así que se
            // registra como aviso suave (sin «Error no controlado») y se marca 499 (cliente cerró la petición) en lugar de 500.
            logger.LogDebug(ex, "Petición cancelada por el cliente. Correlation {Correlation}", tenant.CorrelationId);
            if (!http.Response.HasStarted) http.Response.StatusCode = 499;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && DeadlockRules.IsDeadlock(ex))
        {
            // Lote 41: interbloqueo de SQL Server que sobrevivió a los reintentos de EF → 409 'concurrent_edit' (no 500): la
            // operación no se aplicó y basta con volver a intentarla. IdempotencyMiddleware ya liberó la clave (no es de dominio).
            logger.LogWarning(ex, "Interbloqueo de SQL Server tras los reintentos; se responde 409 {Code}. Correlation {Correlation}",
                DeadlockRules.Code, tenant.CorrelationId);
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            await http.Response.WriteAsJsonAsync(ConcurrentEditBody(http.Request.Path.Value, tenant.CorrelationId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error no controlado. Correlation {Correlation}", tenant.CorrelationId);
            if (http.Response.HasStarted) throw;
            http.Response.StatusCode = 500;
            await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/internal", title = "Error interno.", status = 500, code = "internal", correlationId = tenant.CorrelationId });
        }
    }

    /// <summary>Lote 41 — cuerpo del 409 por interbloqueo (mismo formato ProblemDetails; mensaje de rutas en /api/v1/trips…).</summary>
    public static object ConcurrentEditBody(string? requestPath, Guid correlationId) => new
    {
        type = "https://teikem.app/errors/" + DeadlockRules.Code, title = DeadlockRules.MessageFor(requestPath), status = StatusCodes.Status409Conflict,
        code = DeadlockRules.Code, correlationId,
    };

    /// <summary>Cuerpo ProblemDetails de una excepción de dominio (lo reutiliza IdempotencyMiddleware para guardar la respuesta).</summary>
    public static object ProblemBody(Teikem.Infrastructure.Exceptions.TeikemException ex, Guid correlationId) => new
    {
        type = "https://teikem.app/errors/" + ex.Code, title = ex.Message, status = ex.StatusCode, code = ex.Code,
        errors = ex.Errors, correlationId,
    };
}
