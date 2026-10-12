using Teikem.Domain.Trips;

namespace Teikem.Domain.Orders;

/// <summary>
/// Lote 39 (F-C1): reglas puras de los filtros de lista de GET /api/v1/orders (serviceTypes, packageTypes, cities,
/// postalCodePrefix, clientIds, statuses). Sin BD: OrderReadService normaliza con ellas, resuelve los códigos contra los
/// catálogos y traduce los desconocidos a 400 con estos mensajes (los cita el manual).
/// - Cada lista es "cualquiera de" (OR dentro del filtro) y los filtros se combinan entre sí con AND.
/// - Valores en blanco se ignoran; repetidos cuentan una vez. Los códigos de catálogo se comparan en mayúsculas.
/// - El pueblo se compara por igualdad sin mayúsculas, sin tildes y con espacios colapsados ('bayamon' = 'Bayamón'), la misma
///   regla que la lista 'Sin asignar' de Sala de despacho (TripOrderRequestRules.MatchesCity).
/// </summary>
public static class OrderListFilterRules
{
    public static string UnknownServiceType(string code) => $"Tipo de servicio desconocido: {code}.";
    public static string UnknownPackageType(string code) => $"Tipo de paquete desconocido: {code}.";
    public static string UnknownStatus(string code) => $"Estatus desconocido: {code}.";

    /// <summary>Códigos de catálogo: recortados, en mayúsculas, sin vacíos ni repetidos (orden de llegada).</summary>
    public static IReadOnlyList<string> NormalizeCodes(IEnumerable<string?>? values)
        => (values ?? Enumerable.Empty<string?>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Textos libres (pueblos): recortados, sin vacíos ni repetidos (sin distinguir mayúsculas).</summary>
    public static IReadOnlyList<string> NormalizeTexts(IEnumerable<string?>? values)
        => (values ?? Enumerable.Empty<string?>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Prefijo de ZIP recortado; null si viene vacío (sin filtro).</summary>
    public static string? NormalizePostalPrefix(string? prefix) => string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();

    /// <summary>
    /// De los pueblos tal como están guardados en las paradas (<paramref name="storedCities"/>), los que coinciden con alguno de los
    /// pedidos sin mayúsculas ni tildes. El servicio filtra luego por igualdad exacta contra esta lista (traducible a SQL).
    /// </summary>
    public static IReadOnlyList<string> MatchingCities(IEnumerable<string?> storedCities, IReadOnlyList<string> wanted)
        => storedCities
            .Where(c => !string.IsNullOrWhiteSpace(c) && wanted.Any(w => TripOrderRequestRules.MatchesCity(c, w)))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>¿El ZIP empieza con el prefijo? (sin prefijo = todo; sin ZIP = no). Misma regla que 'Sin asignar'.</summary>
    public static bool MatchesPostalPrefix(string? postalCode, string? prefix) => TripOrderRequestRules.MatchesPostalPrefix(postalCode, prefix);
}
