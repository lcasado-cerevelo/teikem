namespace Teikem.Domain.Security;

/// <summary>
/// Lote 100: computadora (navegador) de confianza de un usuario. La primera vez que alguien entra bien con su contraseña desde un navegador que no se reconoce, se le pide confirmar por
/// correo; al confirmar, ese navegador queda de confianza <see cref="TrustedBrowserRules.Days"/> días sin uso (la ventana se renueva en cada ingreso). El navegador guarda una marca secreta
/// aleatoria y aquí solo queda su resumen (SHA-256). Tabla global por usuario, sin TenantId.
/// </summary>
public class TrustedBrowser
{
    public long TrustedBrowserId { get; set; }
    public Guid PublicId { get; set; }
    public int UserId { get; set; }
    /// <summary>SHA-256 (hex) de la marca secreta que guarda el navegador.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>SHA-256 (hex) de la marca del enlace del correo; null una vez confirmado.</summary>
    public string? ConfirmHash { get; set; }
    public DateTime? LinkExpiresAtUtc { get; set; }
    public DateTime? EmailSentAtUtc { get; set; }
    /// <summary>Texto legible: «Chrome en Windows».</summary>
    public string Label { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    /// <summary>Hasta cuándo vale la confianza (se corre en cada ingreso). Null = aún sin confirmar.</summary>
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsTrusted(DateTime nowUtc) => RevokedAtUtc is null && ConfirmedAtUtc is not null && ExpiresAtUtc is DateTime e && e > nowUtc;
}

public static class TrustedBrowserRules
{
    public const int Days = 30;
    public const int LinkHours = 24;
    public const int ResendMinutes = 2;
    /// <summary>Largo mínimo de la marca que manda el navegador (la web genera 64 caracteres).</summary>
    public const int MinTokenLength = 32;
    public const int MaxTokenLength = 200;
    /// <summary>Tope de correos de confirmación por usuario en 24 horas (para que nadie llene el correo de otro).</summary>
    public const int MaxEmailsPerDay = 10;

    public const string LinkInvalidMessage = "El enlace no es válido o venció. Vuelva a intentar entrar para recibir uno nuevo.";
    public const string NotFoundMessage = "Computadora";

    /// <summary>«Chrome en Windows», «Safari en iPhone»… a partir del User-Agent (sin dependencias; lo que no se reconoce queda como «Navegador»).</summary>
    public static string Describe(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        string browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("OPR/") || ua.Contains("Opera") ? "Opera" : ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Chrome/") || ua.Contains("CriOS/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "Navegador";
        string os = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android" : ua.Contains("Windows") ? "Windows"
            : ua.Contains("Mac OS X") || ua.Contains("Macintosh") ? "Mac" : ua.Contains("CrOS") ? "ChromeOS" : ua.Contains("Linux") ? "Linux" : string.Empty;
        return os.Length == 0 ? browser : $"{browser} en {os}";
    }

    public static bool IsValidToken(string? token) => token is { Length: >= MinTokenLength and <= MaxTokenLength };
}
