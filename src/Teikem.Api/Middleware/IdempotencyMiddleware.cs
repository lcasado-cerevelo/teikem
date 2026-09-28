using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Teikem.Api.Middleware;

/// <summary>
/// Lote 8A — idempotencia del API (cabecera Idempotency-Key, módulo 13 del maestro) para la cola de salida del aparato de
/// almacén y cualquier cliente que reintente escrituras. Reglas puras en <see cref="IdempotencyRules"/>.
/// <para>Aplica a POST, PUT, PATCH y DELETE AUTENTICADOS (hay TenantId y UserId del principal) que traen la cabecera, fuera de
/// las rutas que devuelven credenciales (/api/v1/auth, /api/v1/me, /api/v1/devices, /api/v1/platform, /api/v1/users: el alta
/// de usuario devuelve la contraseña temporal) y de las invitaciones de portal (/api/v1/clients/{id}/portal-users/invite y
/// .../resend-invite devuelven el token de invitación). Sin cabecera, anónimo
/// o ruta excluida: pasa de largo sin tocar nada.</para>
/// <list type="bullet">
/// <item>Clave vacía, de más de 80 caracteres o repetida → 400 'La clave de idempotencia no es válida.'.</item>
/// <item>Clave lógica (TenantId, UserId, clave) en IntegrationMessageLog (índice único UX_IntegrationLog_Idem); huella SHA-256
/// de método, ruta y cuerpo.</item>
/// <item>Misma clave y misma huella con respuesta guardada → el mismo código y cuerpo, con cabecera Idempotent-Replayed: true
/// (sin volver a ejecutar).</item>
/// <item>Misma clave con otra huella → 409 'La clave de idempotencia ya se usó con otro contenido.'.</item>
/// <item>Misma clave aún sin respuesta (otra petición en curso) → 409 'La operación con esta clave todavía se está procesando.'.</item>
/// <item>Primera vez: se inserta el registro (en vuelo) ANTES de ejecutar; al terminar se guarda la respuesta si es &lt; 500
/// (salvo 401/403/408/423/429) y es JSON o texto; si no, el registro se borra para permitir el reintento con la misma clave.</item>
/// <item>Registros de más de 7 días no cuentan y se borran perezosamente al insertar uno nuevo; uno en vuelo por más de 10
/// minutos (proceso caído) se descarta.</item>
/// <item>Con la clave registrada, la operación ya no se cancela si el cliente se desconecta (RequestAborted desligado): termina,
/// guarda su respuesta y el reintento recibe Replay. Guardar la respuesta se reintenta si falla.</item>
/// </list>
/// Nunca guarda el cuerpo de la petición (puede traer PIN o contraseñas), solo su huella. Va después de UseAuthorization: las
/// peticiones rechazadas por autenticación o permiso no dejan registro; antes de repetir se vuelven a aplicar [RequireModule] y
/// [RequireAal2] del endpoint (son filtros de MVC que la repetición no alcanza) y las comprobaciones de módulo y permiso que la
/// operación original hizo en el servicio o el controlador (<see cref="IdempotencyCheckRecorder"/>, guardadas en
/// ReplayChecksJson): la que se cumplía y ya no → el mismo 403 que una llamada nueva; otro cambio (p. ej. el permiso que decide
/// el conteo a ciegas) → 409 'La operación con esta clave ya no puede repetirse con los permisos actuales.'.
/// La clave distingue mayúsculas (collation binaria de la columna).
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    private const int EndpointMaxLength = 200;

    public async Task InvokeAsync(HttpContext http, TenantContext tenant, TeikemDbContext db, ILookupCache lookups, IOptions<HttpJsonOptions> jsonOptions)
    {
        var request = http.Request;
        if (!request.Headers.TryGetValue(IdempotencyRules.HeaderName, out var values)
            || !IdempotencyRules.AppliesToMethod(request.Method)
            || tenant.TenantId is not int tenantId || tenant.UserId is not int userId
            || IdempotencyRules.IsExcludedPath(request.Path.Value))
        {
            await next(http);
            return;
        }

        var (key, error) = IdempotencyRules.ValidateKey(values.ToArray());
        if (error is not null) throw new ValidationException(IdempotencyRules.HeaderName, error);

        var ct = http.RequestAborted;
        var method = request.Method.ToUpperInvariant();
        var pathAndQuery = request.Path.Value + request.QueryString.Value;

        // Huella del cuerpo: se lee completo y se rebobina para el controlador.
        request.EnableBuffering();
        byte[] body;
        using (var ms = new MemoryStream())
        {
            await request.Body.CopyToAsync(ms, ct);
            body = ms.ToArray();
        }
        request.Body.Position = 0;
        var hash = IdempotencyRules.ComputeHash(method, pathAndQuery, body);
        var now = DateTime.UtcNow;

        // El filtro global de tenant acota la búsqueda a la compañía del principal.
        var existing = await db.IntegrationMessageLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.IdempotencyKey == key)
            .Select(l => new { l.IntegrationMessageLogId, l.RequestHash, l.ResponseCode, l.ResponseJson, l.ReplayChecksJson, l.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);

        switch (IdempotencyRules.Decide(existing?.RequestHash, existing?.ResponseCode, existing?.CreatedAtUtc, hash, now))
        {
            case IdempotencyDecision.Replay:
                // Los filtros [RequireModule] y [RequireAal2] corren dentro de MVC y la repetición no llega ahí: se vuelven a
                // aplicar aquí para que un módulo apagado (403) o una ventana AAL2 vencida no se salten con la clave.
                await EnsureEndpointFiltersAsync(http, ct);
                // Las comprobaciones que hizo el servicio o el controlador tampoco se alcanzan: se reevalúan aquí.
                await EnsureRecordedChecksAsync(http, existing!.ReplayChecksJson, ct);
                await ReplayAsync(http, existing!.ResponseCode!.Value, existing.ResponseJson);
                return;
            case IdempotencyDecision.BodyMismatch:
                throw new ConflictException(IdempotencyRules.BodyMismatchMessage);
            case IdempotencyDecision.InFlight:
                throw new ConflictException(IdempotencyRules.InFlightMessage);
            case IdempotencyDecision.Expired:
                await DeleteAsync(db, existing!.IntegrationMessageLogId);
                break;
        }

        var recordId = await InsertInFlightAsync(db, lookups, tenantId, userId, key!, method, pathAndQuery, hash, now, ct);

        // Con el registro en vuelo, la operación se desliga de la desconexión del cliente: si el aparato pierde la señal
        // después del commit (p. ej. al releer el recibo confirmado), la operación termina, su respuesta se guarda y el
        // reintento de la cola recibe Replay en vez de ejecutarse otra vez (sin esto la cancelación borraba la clave y el
        // reintento duplicaba la operación). El enlace de CancellationToken de MVC lee este valor.
        http.RequestAborted = CancellationToken.None;

        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        var recorder = http.RequestServices?.GetService<IdempotencyCheckRecorder>();
        recorder?.Start();
        string? checks = null;
        try
        {
            await next(http);
        }
        catch (TeikemException ex) when (!http.Response.HasStarted)
        {
            // Rechazo de negocio: se responde aquí (mismo ProblemDetails que ExceptionHandlingMiddleware) para poder guardarlo.
            checks = IdempotencyRules.SerializeChecks(recorder?.Stop());
            http.Response.Body = originalBody;
            http.Response.StatusCode = ex.StatusCode;
            var problem = JsonSerializer.Serialize(ExceptionHandlingMiddleware.ProblemBody(ex, tenant.CorrelationId), jsonOptions.Value.SerializerOptions);
            if (IdempotencyRules.ShouldStore(ex.StatusCode)) await StoreAsync(db, recordId, ex.StatusCode, problem, checks);
            else await DeleteAsync(db, recordId);
            http.Response.ContentType = "application/json; charset=utf-8";
            await http.Response.WriteAsync(problem, Encoding.UTF8, CancellationToken.None);
            return;
        }
        catch
        {
            // 5xx, cancelación o respuesta ya iniciada: no se guarda; el reintento con la misma clave vuelve a ejecutar.
            recorder?.Stop();
            http.Response.Body = originalBody;
            await DeleteAsync(db, recordId);
            throw;
        }

        checks = IdempotencyRules.SerializeChecks(recorder?.Stop());
        http.Response.Body = originalBody;
        var status = http.Response.StatusCode;
        var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (IdempotencyRules.ShouldStore(status) && IsTextual(http.Response.ContentType, buffer.Length)) await StoreAsync(db, recordId, status, text, checks);
        else await DeleteAsync(db, recordId);

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, CancellationToken.None);
    }

    /// <summary>Inserta el registro en vuelo (y borra perezosamente los vencidos del tenant). Otra petición con la misma clave ganó la carrera → 409 en vuelo.</summary>
    private async Task<long> InsertInFlightAsync(TeikemDbContext db, ILookupCache lookups, int tenantId, int userId, string key, string method,
        string pathAndQuery, string hash, DateTime now, CancellationToken ct)
    {
        var purgeBefore = IdempotencyRules.PurgeBefore(now);
        try
        {
            await db.IntegrationMessageLogs
                .Where(l => l.UserId != null && l.Method != null && l.IdempotencyKey != null && l.CreatedAtUtc < purgeBefore)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudieron borrar los registros de idempotencia vencidos del tenant {TenantId}.", tenantId);
        }

        var record = new IntegrationMessageLog
        {
            TenantId = tenantId, UserId = userId, IdempotencyKey = key, Method = method, RequestHash = hash, CreatedAtUtc = now,
            Endpoint = pathAndQuery.Length > EndpointMaxLength ? pathAndQuery[..EndpointMaxLength] : pathAndQuery,
            DirectionLookupId = await lookups.GetIdAsync(LookupDomains.MessageDirection, MessageDirections.Inbound, ct),
        };
        db.IntegrationMessageLogs.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbExtensions.IsUniqueViolation(ex))
        {
            throw new ConflictException(IdempotencyRules.InFlightMessage);
        }
        finally
        {
            // El registro no debe quedar en el rastreador del contexto que usa el controlador.
            db.Entry(record).State = EntityState.Detached;
        }
        return record.IntegrationMessageLogId;
    }

    private const int StoreAttempts = 3;

    /// <summary>
    /// Guarda la respuesta (ExecuteUpdate: no toca el rastreador ni guarda cambios pendientes del controlador). La operación
    /// ya se ejecutó: si el guardado falla se reintenta, porque un registro sin respuesta termina dándose por abandonado
    /// (<see cref="IdempotencyRules.InFlightTimeout"/>) y el reintento del cliente volvería a ejecutar la operación.
    /// </summary>
    private async Task StoreAsync(TeikemDbContext db, long recordId, int status, string body, string? checks)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.IntegrationMessageLogs.Where(l => l.IntegrationMessageLogId == recordId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.ResponseCode, status).SetProperty(l => l.ResponseJson, body)
                        .SetProperty(l => l.ReplayChecksJson, checks), CancellationToken.None);
                return;
            }
            catch (Exception ex) when (attempt < StoreAttempts)
            {
                logger.LogWarning(ex, "No se pudo guardar la respuesta idempotente {RecordId} (intento {Attempt}); se reintenta.", recordId, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "No se pudo guardar la respuesta idempotente {RecordId}.", recordId);
                return;
            }
        }
    }

    private async Task DeleteAsync(TeikemDbContext db, long recordId)
    {
        try
        {
            await db.IntegrationMessageLogs.Where(l => l.IntegrationMessageLogId == recordId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo liberar la clave de idempotencia {RecordId}.", recordId);
        }
    }

    private static async Task EnsureEndpointFiltersAsync(HttpContext http, CancellationToken ct)
    {
        var meta = http.GetEndpoint()?.Metadata;
        if (meta is null) return;
        var modules = http.RequestServices.GetRequiredService<ModuleService>();
        foreach (var m in meta.GetOrderedMetadata<RequireModuleAttribute>())
            await modules.EnsureEnabledAsync(m.ModuleKey, ct);
        if (meta.GetMetadata<RequireAal2Attribute>() is not null)
            await RequireAal2Attribute.EnsureAsync(http.RequestServices, ct);
    }

    /// <summary>
    /// Reevalúa las comprobaciones de módulo y permiso que la operación original hizo en el servicio o el controlador. La que
    /// se cumplía y ya no → el mismo rechazo que una llamada nueva (403 module_disabled, o 403 con PERMISSION_DENIED); otro
    /// cambio (la que no se cumplía y ahora sí, p. ej. el modo a ciegas del conteo) o registro ilegible → 409, sin repetir.
    /// </summary>
    private static async Task EnsureRecordedChecksAsync(HttpContext http, string? checksJson, CancellationToken ct)
    {
        var checks = IdempotencyRules.ParseChecks(checksJson);
        if (checks is null) throw new ConflictException(IdempotencyRules.RecheckChangedMessage);
        if (checks.Count == 0) return;
        var modules = http.RequestServices.GetRequiredService<ModuleService>();
        var permissions = http.RequestServices.GetRequiredService<PermissionService>();
        var changed = false;
        foreach (var check in checks)
        {
            var isModule = check.Kind == IdempotencyRules.ModuleCheck;
            var now = isModule ? await modules.IsEnabledAsync(check.Code, ct) : await permissions.HasPermissionAsync(check.Code, ct);
            if (now == check.Result) continue;
            if (check.Result)
            {
                if (isModule) throw new ModuleDisabledException(check.Code);
                await permissions.EnsureAsync(check.Code, ct);
            }
            changed = true;
        }
        if (changed) throw new ConflictException(IdempotencyRules.RecheckChangedMessage);
    }

    private static async Task ReplayAsync(HttpContext http, int status, string? body)
    {
        http.Response.StatusCode = status;
        http.Response.Headers[IdempotencyRules.ReplayedHeader] = "true";
        if (string.IsNullOrEmpty(body)) return;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(body, Encoding.UTF8, http.RequestAborted);
    }

    /// <summary>Solo se guardan respuestas vacías, JSON o de texto (ResponseJson es NVARCHAR).</summary>
    private static bool IsTextual(string? contentType, long length)
        => length == 0 || string.IsNullOrEmpty(contentType)
           || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
           || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
}
