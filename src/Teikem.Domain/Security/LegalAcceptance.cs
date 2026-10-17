namespace Teikem.Domain.Security;

/// <summary>
/// Lote 99 (portal de clientes): aceptación del documento legal único («Política de privacidad y términos de uso»). Un solo documento, una sola versión para
/// toda la plataforma (staff, portal del cliente y consignatarios). Cada aceptación guarda la versión, cuándo y desde dónde; al cambiar la versión vigente
/// se vuelve a pedir en el siguiente ingreso. Tabla global (por usuario), sin TenantId.
/// </summary>
public class LegalAcceptance
{
    public long LegalAcceptanceId { get; set; }
    public int UserId { get; set; }
    /// <summary>Versión aceptada (<see cref="LegalRules.CurrentVersion"/> al momento de aceptar).</summary>
    public string Version { get; set; } = string.Empty;
    public string Lang { get; set; } = "es";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class LegalRules
{
    /// <summary>
    /// Versión vigente del documento (fecha de su última modificación, AAAA-MM-DD). Al cambiar el texto de <c>LegalText</c> se cambia esta fecha: todos los
    /// usuarios vuelven a aceptar en su siguiente ingreso.
    /// </summary>
    public const string CurrentVersion = "2026-10-17.2";

    public const string ErrorCode = "legal_acceptance_required";
    public const string RequiredMessage = "Debe aceptar la política de privacidad y los términos de uso para continuar.";
    public const string VersionMismatchMessage = "La versión del documento ya no es la vigente. Vuelva a leerlo.";

    /// <summary>Rutas que siguen abiertas aunque falte aceptar (sesión, documento y estado): sin ellas no habría cómo aceptar ni cerrar sesión.</summary>
    public static bool IsExemptPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        var p = path.TrimEnd('/').ToLowerInvariant();
        return p.StartsWith("/api/v1/auth") || p.StartsWith("/api/v1/legal") || p == "/api/v1/me" || p == "/api/v1/portal/me" || !p.StartsWith("/api/");
    }
}
