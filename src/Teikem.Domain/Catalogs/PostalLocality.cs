using System.Globalization;
using System.Text;
using Teikem.Domain.Common;

namespace Teikem.Domain.Catalogs;

/// <summary>
/// Lote 1 (cambios de Almacén) — catálogo GLOBAL de referencia ciudad ↔ código postal (sin TenantId, como LookupCode global).
/// Sembrado por Diseño/logistica-db-seed.sql desde el catálogo USPS (42.522 ZIP de EE. UU., territorios y Puerto Rico). City es
/// el nombre postal oficial del ZIP (mayúsculas, sin acentos); en Puerto Rico, Municipality es el municipio (00952 SABANA SECA →
/// Toa Baja). Único (CountryLookupId, PostalCode, City). Solo lectura desde el API: el formulario de almacén llena
/// City/State/PostalCode/Country con la fila elegida (esas columnas de Warehouse siguen siendo texto). No se audita.
/// </summary>
public class PostalLocality : ISoftDeletable
{
    public int PostalLocalityId { get; set; }
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string? State { get; set; }
    /// <summary>Solo Puerto Rico: municipio al que pertenece el ZIP (la ciudad postal puede ser un barrio).</summary>
    public string? Municipality { get; set; }
    public int CountryLookupId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Reglas puras de la búsqueda de localidades postales (sin BD).</summary>
public static class PostalLocalityRules
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    /// <summary>take ≤ 0 → DefaultTake; más de MaxTake → MaxTake.</summary>
    public static int Take(int take) => take <= 0 ? DefaultTake : Math.Min(take, MaxTake);

    /// <summary>
    /// Forma comparable de un texto: sin acentos ni diéresis (NFD sin marcas), en mayúsculas invariantes, con los espacios
    /// recortados y colapsados. 'Mayagüez' → 'MAYAGUEZ'; '  san   germán ' → 'SAN GERMAN'; null → ''.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!space) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>La búsqueda es por código postal cuando solo trae dígitos (y guion, para ZIP+4): se compara por prefijo.</summary>
    public static bool IsPostalCodeSearch(string normalizedSearch)
        => normalizedSearch.Length > 0 && normalizedSearch.All(c => char.IsAsciiDigit(c) || c == '-') && normalizedSearch.Any(char.IsAsciiDigit);

    /// <summary>
    /// Rango de coincidencia de una localidad para la búsqueda ya normalizada (null = no coincide; menor = mejor):
    /// por código postal, 0 si el ZIP empieza por el texto (solo los 5 dígitos del ZIP+4 cuentan); por ciudad, 0 si la ciudad
    /// empieza por el texto, 1 si alguna palabra de la ciudad empieza por él, 2 si lo contiene. Búsqueda vacía → 0 (todo).
    /// </summary>
    public static int? Rank(string normalizedSearch, string city, string postalCode)
        => RankNormalized(normalizedSearch, Normalize(city), null, postalCode);

    /// <summary>
    /// Igual que <see cref="Rank"/> pero con la ciudad y el municipio YA normalizados (el servicio los calcula una vez al cargar
    /// el catálogo): gana el mejor rango entre ciudad postal y municipio.
    /// </summary>
    public static int? RankNormalized(string normalizedSearch, string normalizedCity, string? normalizedMunicipality, string postalCode)
    {
        if (normalizedSearch.Length == 0) return 0;
        if (IsPostalCodeSearch(normalizedSearch))
        {
            var zip = normalizedSearch.Split('-')[0];
            return postalCode.StartsWith(zip, StringComparison.Ordinal) ? 0 : null;
        }
        var byCity = TextRank(normalizedSearch, normalizedCity);
        var byMunicipality = string.IsNullOrEmpty(normalizedMunicipality) ? null : TextRank(normalizedSearch, normalizedMunicipality);
        return (byCity, byMunicipality) switch
        {
            (null, var m) => m,
            (var c, null) => c,
            var (c, m) => Math.Min(c!.Value, m!.Value),
        };
    }

    private static int? TextRank(string search, string text)
    {
        if (text.StartsWith(search, StringComparison.Ordinal)) return 0;
        if (text.Contains(" " + search, StringComparison.Ordinal)) return 1;
        return text.Contains(search, StringComparison.Ordinal) ? 2 : null;
    }
}
