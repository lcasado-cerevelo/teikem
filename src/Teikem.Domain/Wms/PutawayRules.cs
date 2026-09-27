using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Posición candidata para putaway: zona, capacidad (MaxWeightKg), peso actual y saldo del producto/lote en ella.
/// CurrentWeightKg = Σ en mano × peso unitario de todo lo que ya está en la posición.
/// </summary>
public sealed record PutawayCandidate(int BinId, string BinCode, int ZoneId, string ZoneCode, string? ZoneTypeCode, bool IsActive,
    decimal? MaxWeightKg, decimal CurrentWeightKg, bool IsEmpty, decimal ProductQty, decimal LotQty, bool IsPreferred);

/// <summary>Posición sugerida con su razón (ReasonCode + etiqueta) y la rotación con la que se calculó.</summary>
public sealed record PutawayRank(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason, string RotationClass);

/// <summary>
/// Lote 6 (D24, maestro L301) — putaway dirigido puro. La rotación se aproxima con las salidas (ISSUE + CROSSDOCK) de 30
/// días: FAST si salidas &gt; 0 y salidas ≥ existencia total; SLOW en otro caso. Razones, en orden:
/// PREFERRED, CONSOLIDATE_LOT, CONSOLIDATE, PICKING_FAST (solo FAST), RESERVE_EMPTY, RESERVE.
/// Excluye QUARANTINE, STAGING, CROSSDOCK, las inactivas y la de origen; REFRIGERATED solo como preferida o para
/// consolidar; capacidad por MaxWeightKg (NULL = sin límite); desempate por código.
/// </summary>
public static class PutawayRules
{
    public const string Preferred = "PREFERRED";
    public const string ConsolidateLot = "CONSOLIDATE_LOT";
    public const string Consolidate = "CONSOLIDATE";
    public const string PickingFast = "PICKING_FAST";
    public const string ReserveEmpty = "RESERVE_EMPTY";
    public const string Reserve = "RESERVE";

    public static string ReasonLabel(string reasonCode) => reasonCode switch
    {
        Preferred => "Posición preferida del producto",
        ConsolidateLot => "Consolidar con el mismo lote",
        Consolidate => "Consolidar con el mismo producto",
        PickingFast => "Picking por alta rotación",
        ReserveEmpty => "Reserva vacía",
        Reserve => "Reserva con capacidad",
        _ => reasonCode,
    };

    /// <summary>FAST si issued30d &gt; 0 y issued30d ≥ onHandTotal; SLOW en otro caso.</summary>
    public static string Classify(decimal issued30d, decimal onHandTotal)
        => issued30d > 0m && issued30d >= onHandTotal ? RotationClasses.Fast : RotationClasses.Slow;

    private static bool HasCapacity(PutawayCandidate c, decimal qty, decimal? unitWeightKg)
        => c.MaxWeightKg is not decimal max || unitWeightKg is not decimal w || c.CurrentWeightKg + qty * w <= max;

    public static IReadOnlyList<PutawayRank> Rank(IEnumerable<PutawayCandidate> candidates, int? lotId, decimal quantity,
        decimal? unitWeightKg, string rotationClass, int? excludeBinId, int take)
    {
        var ranked = new List<(int Order, PutawayCandidate C, string Reason)>();
        foreach (var c in candidates ?? Enumerable.Empty<PutawayCandidate>())
        {
            if (!c.IsActive || c.BinId == excludeBinId) continue;
            if (c.ZoneTypeCode is ZoneTypes.Quarantine or ZoneTypes.Staging or ZoneTypes.CrossDock) continue;
            if (!HasCapacity(c, quantity, unitWeightKg)) continue;
            string? reason = null;
            if (c.IsPreferred) reason = Preferred;
            else if (lotId is not null && c.LotQty > 0m) reason = ConsolidateLot;
            else if (c.ProductQty > 0m) reason = Consolidate;
            else if (c.ZoneTypeCode == ZoneTypes.Refrigerated) reason = null;
            else if (c.ZoneTypeCode == ZoneTypes.Picking)
                reason = rotationClass == RotationClasses.Fast && c.IsEmpty ? PickingFast : null;
            else if (c.ZoneTypeCode == ZoneTypes.Reserve) reason = c.IsEmpty ? ReserveEmpty : Reserve;
            if (reason is null) continue;
            ranked.Add((Array.IndexOf(new[] { Preferred, ConsolidateLot, Consolidate, PickingFast, ReserveEmpty, Reserve }, reason), c, reason));
        }
        return ranked
            .OrderBy(r => r.Order)
            .ThenBy(r => r.C.BinCode, StringComparer.Ordinal)
            .ThenBy(r => r.C.BinId)
            .Take(Math.Max(0, take))
            .Select(r => new PutawayRank(r.C.BinId, r.C.BinCode, r.C.ZoneCode, r.C.ZoneTypeCode, r.Reason, ReasonLabel(r.Reason), rotationClass))
            .ToList();
    }
}
