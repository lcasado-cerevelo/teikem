namespace Teikem.Domain.Security;

/// <summary>
/// Lote 101: qué rutas puede tocar una sesión de PORTAL y cuáles son solo del portal. Un usuario de portal NUNCA llega a las rutas del staff aunque su token sea válido (los permisos son del staff y el portal
/// no tiene ninguno): solo ve <c>/api/v1/portal/*</c>, la sesión (<c>/api/v1/auth/*</c>, sin cambiar de compañía ni los aparatos) y el documento legal. Y al revés: una sesión del staff no entra a <c>/api/v1/portal/*</c>.
/// </summary>
public static class PortalScopeRules
{
    public const string PortalPrefix = "/api/v1/portal";
    public const string NotForPortalCode = "portal_scope";
    public const string NotForPortalMessage = "Esta ruta no está disponible para usuarios del portal.";
    public const string PortalOnlyMessage = "Esta ruta es solo para usuarios del portal.";

    private static string Norm(string? path) => (path ?? string.Empty).TrimEnd('/').ToLowerInvariant();

    /// <summary>¿Puede una sesión de portal llamar a esta ruta? Todo lo que no es API (archivos de la web) queda fuera de la regla.</summary>
    public static bool IsAllowedForPortal(string? path)
    {
        var p = Norm(path);
        if (!p.StartsWith("/api/") && !p.StartsWith("/hubs")) return true;
        if (p == PortalPrefix || p.StartsWith(PortalPrefix + "/")) return true;
        if (p.StartsWith("/api/v1/legal")) return true;
        if (p.StartsWith("/api/v1/auth/"))
        {
            // la sesión sí (renovar, salir, contraseña, MFA, computadoras de confianza); lo propio del staff no
            if (p.StartsWith("/api/v1/auth/switch-tenant") || p.StartsWith("/api/v1/auth/device") || p.StartsWith("/api/v1/auth/onboarding")) return false;
            return true;
        }
        return false;
    }

    public const string ConsigneePrefix = PortalPrefix + "/consignee";

    /// <summary>
    /// Lote 117: el consignatario con acceso propio es un usuario de portal MÁS limitado: de <c>/api/v1/portal/*</c> solo toca su propio espacio (<c>/portal/consignee/*</c>) y el inicio de sesión
    /// (<c>/portal/auth/*</c>); las rutas del cliente (órdenes, mensajes con el equipo, avisos al consignatario...) le responden 403, aunque su token sea válido. Lo demás (sesión, documento legal) es igual que el resto del portal.
    /// </summary>
    public static bool IsAllowedForConsignee(string? path)
    {
        var p = Norm(path);
        if (p == PortalPrefix || p.StartsWith(PortalPrefix + "/"))
            return p.StartsWith(PortalPrefix + "/auth/") || p == ConsigneePrefix || p.StartsWith(ConsigneePrefix + "/");
        return IsAllowedForPortal(path);
    }

    /// <summary>¿Es una ruta exclusiva del portal (a la que una sesión del staff no entra)?</summary>
    public static bool IsPortalOnlyPath(string? path)
    {
        var p = Norm(path);
        return p == PortalPrefix || p.StartsWith(PortalPrefix + "/");
    }
}
