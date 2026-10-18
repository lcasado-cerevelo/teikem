using System.Text.RegularExpressions;

namespace Teikem.Domain.Tenancy;

/// <summary>
/// Lote 103 — seguimiento público «¿Cómo va mi orden?». Cada compañía tiene (si la enciende) una dirección corta propia, el «identificador de seguimiento» (<c>Tenant.TrackingSlug</c>), porque el número de conduce
/// es único dentro de cada compañía pero se repite entre compañías. Sin identificador, el seguimiento público está APAGADO para esa compañía.
/// </summary>
public static partial class TrackingRules
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    public const string InvalidSlugMessage = "El identificador debe tener de 3 a 40 caracteres: letras minúsculas, números y guiones (sin empezar ni terminar con guion).";
    public const string ReservedSlugMessage = "Ese identificador no está disponible.";
    public const string SlugTakenMessage = "Ese identificador ya lo usa otra compañía.";
    public const string NotFoundMessage = "No encontramos ese número de conduce.";

    /// <summary>Palabras que no pueden ser un identificador (rutas de la app y nombres que confunden).</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "api", "admin", "track", "login", "mfa", "legal", "home", "orders", "notices", "settings", "order", "lock", "confirm-browser", "portal", "public", "www", "app", "teikem", "static", "assets",
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    /// <summary>Normaliza lo escrito (recorta y pasa a minúsculas). Vacío = apagar el seguimiento (null).</summary>
    public static string? Normalize(string? slug) => string.IsNullOrWhiteSpace(slug) ? null : slug.Trim().ToLowerInvariant();

    public static bool IsValidSlug(string? slug) => slug is { Length: >= MinLength and <= MaxLength } && SlugPattern().IsMatch(slug);

    public static bool IsReserved(string? slug) => slug is not null && Reserved.Contains(slug);

    /// <summary>
    /// ¿Parece un número de conduce? (letras, números y guiones, hasta 30 caracteres). Solo evita consultas absurdas; el formato real lo define cada compañía (por omisión CND-#######), así que no se exige uno concreto.
    /// </summary>
    public static bool LooksLikeWaybill(string? value) => value is { Length: >= 3 and <= 40 } && WaybillPattern().IsMatch(value.Trim());

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$")]
    private static partial Regex WaybillPattern();
}
