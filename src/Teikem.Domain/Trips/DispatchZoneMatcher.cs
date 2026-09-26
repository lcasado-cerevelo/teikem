using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;

namespace Teikem.Domain.Trips;

/// <summary>Miembro de una zona ACTIVA visto por la resolución: código de la zona, criterio (ZoneMatchType) y valor normalizado.</summary>
public sealed record ZoneMember(string ZoneCode, string MatchTypeCode, string MatchValue);

/// <summary>
/// Resultado de resolver 'CP/pueblo → zona': ZoneCode y MatchedBy (POSTAL_CODE, POSTAL_RANGE o MUNICIPALITY) si hay una sola
/// zona ganadora; Ambiguous con los códigos empatados (Candidates) si varias zonas empatan en el mismo nivel; todo null si
/// ninguna coincide.
/// </summary>
public sealed record ZoneResolution(string? ZoneCode, string? MatchedBy, bool Ambiguous, IReadOnlyList<string> Candidates)
{
    public static readonly ZoneResolution None = new(null, null, false, Array.Empty<string>());
}

/// <summary>
/// Lote 5 (P0) — reglas puras de las zonas de despacho: normalización de los miembros, resolución 'CP/pueblo → zona' con
/// precedencia CP exacto &gt; rango postal &gt; municipio (sin acentos ni mayúsculas; empate en el mismo nivel = ambigua) y
/// detección de solapamiento con otra zona activa. Los mensajes son los que citan el manual y la FAQ.
/// </summary>
public static partial class DispatchZoneMatcher
{
    public const int MaxValueLength = 120;

    public const string ValueRequiredMessage = "Indique el valor del criterio.";
    public const string PostalCodeMessage = "El código postal debe tener 5 dígitos (ej. 00949).";
    public const string PostalRangeMessage = "El rango postal debe tener la forma 00900-00999 (inicio menor o igual que el fin).";
    public const string MunicipalityRequiredMessage = "Indique el municipio.";
    public const string MunicipalityTooLongMessage = "El municipio admite como máximo 120 caracteres.";
    public const string PolygonNotSupportedMessage = "Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio.";

    /// <summary>'Criterio de zona desconocido: 'X'.'</summary>
    public static string UnknownMatchTypeMessage(string? matchType) => $"Criterio de zona desconocido: '{matchType}'.";

    [GeneratedRegex(@"^(\d{5})(-\d{4})?$")]
    private static partial Regex ZipRegex();

    [GeneratedRegex(@"^(\d{5})\s*-\s*(\d{5})$")]
    private static partial Regex RangeRegex();

    /// <summary>
    /// Normaliza el valor de un miembro según su criterio. (valor, null) si es válido; (null, mensaje) si no.
    /// - POSTAL_CODE: 5 dígitos; se acepta ZIP+4 ('00949-1234' → '00949').
    /// - POSTAL_RANGE: 'AAAAA-BBBBB' con inicio ≤ fin (espacios alrededor del guion se quitan).
    /// - MUNICIPALITY: sin espacios de más, máximo 120 caracteres (se compara sin acentos ni mayúsculas).
    /// - POLYGON: 400 (todavía no se soporta). Criterio desconocido: 400.
    /// </summary>
    public static (string? Value, string? Error) NormalizeMember(string? matchTypeCode, string? rawValue)
    {
        var type = matchTypeCode?.Trim().ToUpperInvariant();
        if (type == ZoneMatchTypes.Polygon) return (null, PolygonNotSupportedMessage);
        if (type is not (ZoneMatchTypes.PostalCode or ZoneMatchTypes.PostalRange or ZoneMatchTypes.Municipality))
            return (null, UnknownMatchTypeMessage(matchTypeCode?.Trim()));

        var raw = rawValue?.Trim();
        if (string.IsNullOrEmpty(raw))
            return (null, type == ZoneMatchTypes.Municipality ? MunicipalityRequiredMessage : ValueRequiredMessage);

        switch (type)
        {
            case ZoneMatchTypes.PostalCode:
                return NormalizeZip(raw) is string zip ? (zip, null) : (null, PostalCodeMessage);
            case ZoneMatchTypes.PostalRange:
            {
                var m = RangeRegex().Match(raw);
                if (!m.Success) return (null, PostalRangeMessage);
                var (from, to) = (m.Groups[1].Value, m.Groups[2].Value);
                if (string.CompareOrdinal(from, to) > 0) return (null, PostalRangeMessage);
                return ($"{from}-{to}", null);
            }
            default:
            {
                var city = CollapseSpaces(raw);
                if (city.Length > MaxValueLength) return (null, MunicipalityTooLongMessage);
                return (city, null);
            }
        }
    }

    /// <summary>
    /// Zona de una dirección sobre los miembros de las zonas ACTIVAS: CP exacto &gt; rango postal &gt; municipio (sin acentos).
    /// En cada nivel, una sola zona = ganadora; varias = ambigua (sin zona) con los candidatos; ninguna = siguiente nivel.
    /// </summary>
    public static ZoneResolution Resolve(IEnumerable<ZoneMember>? members, string? postalCode, string? city)
    {
        var list = (members ?? Array.Empty<ZoneMember>()).Where(m => m is not null).ToList();
        if (list.Count == 0) return ZoneResolution.None;

        var zip = NormalizeZip(postalCode?.Trim());
        if (zip is not null)
        {
            var byCode = Level(list.Where(m => Same(m.MatchTypeCode, ZoneMatchTypes.PostalCode) && m.MatchValue.Trim() == zip), ZoneMatchTypes.PostalCode);
            if (byCode is not null) return byCode;
            var byRange = Level(list.Where(m => Same(m.MatchTypeCode, ZoneMatchTypes.PostalRange) && InRange(m.MatchValue, zip)), ZoneMatchTypes.PostalRange);
            if (byRange is not null) return byRange;
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var folded = FoldCity(city);
            var byCity = Level(list.Where(m => Same(m.MatchTypeCode, ZoneMatchTypes.Municipality) && FoldCity(m.MatchValue) == folded), ZoneMatchTypes.Municipality);
            if (byCity is not null) return byCity;
        }
        return ZoneResolution.None;
    }

    /// <summary>
    /// ¿El valor (ya normalizado) choca con un miembro de OTRA zona activa? Devuelve el código de la zona dueña o null.
    /// Chocan: el mismo CP, el mismo municipio (sin acentos) o rangos postales que se solapan. Un CP dentro del rango de
    /// otra zona no choca: la precedencia CP &gt; rango lo resuelve.
    /// </summary>
    public static string? FindConflict(IEnumerable<ZoneMember>? activeMembers, string? ownZoneCode, string matchTypeCode, string value)
    {
        foreach (var m in (activeMembers ?? Array.Empty<ZoneMember>())
                     .Where(m => m is not null && !Same(m.ZoneCode, ownZoneCode) && Same(m.MatchTypeCode, matchTypeCode))
                     .OrderBy(m => m.ZoneCode, StringComparer.Ordinal))
        {
            var clash = matchTypeCode.ToUpperInvariant() switch
            {
                ZoneMatchTypes.PostalCode => m.MatchValue.Trim() == value.Trim(),
                ZoneMatchTypes.PostalRange => RangesOverlap(m.MatchValue, value),
                ZoneMatchTypes.Municipality => FoldCity(m.MatchValue) == FoldCity(value),
                _ => string.Equals(m.MatchValue.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase),
            };
            if (clash) return m.ZoneCode;
        }
        return null;
    }

    /// <summary>CP de 5 dígitos (ZIP+4 se recorta a 5); null si no tiene la forma.</summary>
    public static string? NormalizeZip(string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode)) return null;
        var m = ZipRegex().Match(postalCode.Trim());
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Municipio para comparar: sin acentos, minúsculas y espacios colapsados ('  Bayamón ' → 'bayamon').</summary>
    public static string FoldCity(string? city)
        => string.IsNullOrWhiteSpace(city) ? string.Empty : CollapseSpaces(FleetRules.Fold(city.Trim()));

    private static ZoneResolution? Level(IEnumerable<ZoneMember> hits, string matchedBy)
    {
        var zones = hits.Select(h => h.ZoneCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (zones.Count == 0) return null;
        return zones.Count == 1
            ? new ZoneResolution(zones[0], matchedBy, false, Array.Empty<string>())
            : new ZoneResolution(null, null, true, zones);
    }

    private static bool InRange(string range, string zip)
        => ParseRange(range) is { } r && string.CompareOrdinal(zip, r.From) >= 0 && string.CompareOrdinal(zip, r.To) <= 0;

    private static bool RangesOverlap(string a, string b)
        => ParseRange(a) is { } ra && ParseRange(b) is { } rb
           && string.CompareOrdinal(ra.From, rb.To) <= 0 && string.CompareOrdinal(rb.From, ra.To) <= 0;

    private static (string From, string To)? ParseRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var m = RangeRegex().Match(value.Trim());
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    private static string CollapseSpaces(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
