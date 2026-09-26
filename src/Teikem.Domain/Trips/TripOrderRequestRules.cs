using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Trips;

/// <summary>Resultado de normalizar la consulta 'Sin asignar': paginación efectiva o el error (campo + mensaje) para un 400.</summary>
public sealed record UnassignedQueryCheck(int Skip, int Take, string? Field, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Lote 5 (P2): reglas puras de las solicitudes de 'Órdenes en la ruta' (agregar y lista 'Sin asignar'). Sin BD: el servicio
/// (TripOrderService) las aplica antes de tocar datos y traduce los errores a 400. Los mensajes son 'public const' porque el
/// manual y la FAQ los citan tal cual.
/// </summary>
public static class TripOrderRequestRules
{
    /// <summary>Tope técnico de órdenes por solicitud de alta (DECISIÓN: límites técnicos duros).</summary>
    public const int MaxOrdersPerRequest = 200;

    public const string NoOrdersMessage = "Indique al menos una orden.";
    public const string TooManyOrdersMessage = "Agregue como máximo 200 órdenes por solicitud.";
    public const string ZoneFilterConflictMessage = "Use dispatchZoneId o noZone, no ambos.";
    public const string DateRangeMessage = "El rango de fechas es inválido.";

    /// <summary>
    /// Alta de órdenes a una ruta: colapsa duplicados conservando el orden de llegada (será el orden de las paradas nuevas).
    /// null o vacía → 'Indique al menos una orden.'; más de 200 distintas → 'Agregue como máximo 200 órdenes por solicitud.'
    /// </summary>
    public static (IReadOnlyList<Guid> Ids, string? Error) ValidateAdd(IEnumerable<Guid>? ids)
    {
        var distinct = new List<Guid>();
        if (ids is not null)
        {
            var seen = new HashSet<Guid>();
            foreach (var id in ids)
                if (seen.Add(id)) distinct.Add(id);
        }
        if (distinct.Count == 0) return (Array.Empty<Guid>(), NoOrdersMessage);
        if (distinct.Count > MaxOrdersPerRequest) return (Array.Empty<Guid>(), TooManyOrdersMessage);
        return (distinct, null);
    }

    /// <summary>
    /// Consulta 'Sin asignar': skip ≥ 0; take 1..500 (0 o negativo = 100), igual que el listado de órdenes (OrderRules.NormalizePaging).
    /// dispatchZoneId y noZone son excluyentes; requestedFrom no puede ser posterior a requestedTo.
    /// </summary>
    public static UnassignedQueryCheck NormalizeUnassignedQuery(int? dispatchZoneId, bool noZone, DateOnly? requestedFrom, DateOnly? requestedTo, int skip, int take)
    {
        var (s, t) = OrderRules.NormalizePaging(skip, take);
        if (dispatchZoneId.HasValue && noZone) return new UnassignedQueryCheck(s, t, "noZone", ZoneFilterConflictMessage);
        if (requestedFrom is DateOnly from && requestedTo is DateOnly to && from > to)
            return new UnassignedQueryCheck(s, t, "requestedTo", DateRangeMessage);
        return new UnassignedQueryCheck(s, t, null, null);
    }

    /// <summary>Filtro por código postal: prefijo, sin distinguir mayúsculas ('009' coincide con '00949' y '00949-1234'). Vacío = todo.</summary>
    public static bool MatchesPostalPrefix(string? postalCode, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return true;
        if (string.IsNullOrWhiteSpace(postalCode)) return false;
        return postalCode.Trim().StartsWith(prefix.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Filtro por pueblo: igualdad sin acentos, sin mayúsculas y con espacios colapsados ('bayamon' = 'Bayamón'). Vacío = todo.</summary>
    public static bool MatchesCity(string? city, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        if (string.IsNullOrWhiteSpace(city)) return false;
        return string.Equals(FoldCity(city), FoldCity(filter), StringComparison.Ordinal);
    }

    /// <summary>
    /// Rango de fecha solicitada: desde inclusivo, hasta inclusivo por día (se compara contra el día siguiente, exclusivo).
    /// Sin rango = todo; con rango, una orden sin fecha solicitada no entra.
    /// </summary>
    public static bool InRequestedRange(DateTime? requested, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return true;
        if (requested is not DateTime r) return false;
        if (from is DateOnly f && r < f.ToDateTime(TimeOnly.MinValue)) return false;
        if (to is DateOnly t && r >= t.AddDays(1).ToDateTime(TimeOnly.MinValue)) return false;
        return true;
    }

    private static string FoldCity(string text)
        => string.Join(' ', FleetRules.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
