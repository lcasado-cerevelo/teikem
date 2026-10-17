using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Legal;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Middleware;

/// <summary>
/// Lote 99: con sesión y sin aceptar la versión vigente del documento legal, todo el API (menos sesión, /me y el propio documento) responde 403 'legal_acceptance_required'.
/// Las sesiones de aparato (claim `did`, app de almacén con PIN) quedan fuera por ahora. Se apaga con Legal:RequireAcceptance = false (solo para desarrollo).
/// </summary>
public sealed class LegalAcceptanceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant, LegalService legal)
    {
        if (legal.Enforced && tenant.IsAuthenticated && tenant.UserId is int uid
            && !LegalRules.IsExemptPath(http.Request.Path.Value) && http.User.FindFirst(DeviceClaims.DeviceId) is null
            && !await legal.HasAcceptedAsync(uid, http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new
            {
                type = "https://teikem.app/errors/" + LegalRules.ErrorCode, title = LegalRules.RequiredMessage, status = 403, code = LegalRules.ErrorCode, correlationId = tenant.CorrelationId,
            });
            return;
        }
        await next(http);
    }
}
