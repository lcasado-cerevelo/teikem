using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Api.Middleware;

/// <summary>
/// Lote 101: aísla el portal del staff. Una sesión de PORTAL solo llega a /api/v1/portal/*, a la sesión (/auth/*) y al documento legal (403 'portal_scope' en lo demás); y una sesión del staff no entra a
/// /api/v1/portal/* (403 'portal_scope'). Lo que no tiene sesión (login del portal, configuración) pasa: lo cubren sus propias reglas.
/// Es una segunda barrera: aunque una ruta del staff olvidara su permiso, un token de portal no la alcanza.
/// </summary>
public sealed class PortalScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        if (tenant.IsAuthenticated)
        {
            var path = http.Request.Path.Value;
            string? deny = null;
            if (tenant.IsPortal && !PortalScopeRules.IsAllowedForPortal(path)) deny = PortalScopeRules.NotForPortalMessage;
            else if (tenant.IsPortal && tenant.PortalLocationId is not null && !PortalScopeRules.IsAllowedForConsignee(path)) deny = PortalScopeRules.NotForPortalMessage;   // Lote 117: el consignatario solo toca su espacio
            else if (!tenant.IsPortal && PortalScopeRules.IsPortalOnlyPath(path) && !IsPublicPortalPath(path)) deny = PortalScopeRules.PortalOnlyMessage;
            if (deny is not null)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { type = "https://teikem.app/errors/" + PortalScopeRules.NotForPortalCode, title = deny, status = 403, code = PortalScopeRules.NotForPortalCode, correlationId = tenant.CorrelationId });
                return;
            }
        }
        await next(http);
    }

    /// <summary>Rutas del portal que no necesitan sesión (aunque alguien las llame con un token del staff no se las bloquea: no leen datos).</summary>
    private static bool IsPublicPortalPath(string? path)
    {
        var p = (path ?? string.Empty).TrimEnd('/').ToLowerInvariant();
        return p.StartsWith(PortalScopeRules.PortalPrefix + "/auth/");
    }
}
