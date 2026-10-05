using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Posición candidata para putaway: zona, capacidad (MaxWeightKg), peso actual y saldo del producto/lote en ella.
/// CurrentWeightKg = Σ en mano × peso unitario de todo lo que ya está en la posición. Lote 16: MaxCapacityQty = cupo en
/// unidades (null = sin cupo) y CurrentQty = unidades que ya ocupan la posición (en mano de todos los productos + lo
/// reservado por otras líneas del mismo recibo).
/// </summary>
public sealed record PutawayCandidate(int BinId, string BinCode, int ZoneId, string ZoneCode, string? ZoneTypeCode, bool IsActive,
    decimal? MaxWeightKg, decimal CurrentWeightKg, bool IsEmpty, decimal ProductQty, decimal LotQty, bool IsPreferred,
    int? MaxCapacityQty = null, decimal CurrentQty = 0m);

/// <summary>
/// Posición sugerida con su razón (ReasonCode + etiqueta) y la rotación con la que se calculó. Lote 16: cupo, unidades que
/// ya ocupan la posición y Fits (false solo si se pidieron también las que exceden el cupo).
/// </summary>
public sealed record PutawayRank(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason, string RotationClass,
    int? MaxCapacityQty = null, decimal CurrentQty = 0m, bool Fits = true);

/// <summary>
/// Lote 6 (D24, maestro L301) — putaway dirigido puro. La rotación se aproxima con las salidas (ISSUE + CROSSDOCK) de 30
/// días: FAST si salidas &gt; 0 y salidas ≥ existencia total; SLOW en otro caso. Razones, en orden:
/// PREFERRED, CONSOLIDATE_LOT, CONSOLIDATE, PICKING_FAST (solo FAST), RESERVE_EMPTY, RESERVE.
/// Excluye QUARANTINE, STAGING, CROSSDOCK, las inactivas y la de origen; REFRIGERATED solo como preferida o para
/// consolidar; capacidad por MaxWeightKg (NULL = sin límite); desempate por código.
/// Lote 16: capacidad también por unidades (CurrentQty + cantidad ≤ MaxCapacityQty; sin cupo = sin límite), también para las
/// tareas de acomodo (decisión del arquitecto); preferQuarantine (devoluciones, D6) incluye las posiciones de cuarentena y
/// las pone primero con la razón QUARANTINE_RETURN; includeOverCapacity agrega al final, con Fits = false, las que no caben
/// (la sugerencia de posición destino las muestra con aviso, D4).
/// </summary>
public static class PutawayRules
{
    public const string Preferred = "PREFERRED";
    public const string ConsolidateLot = "CONSOLIDATE_LOT";
    public const string Consolidate = "CONSOLIDATE";
    public const string PickingFast = "PICKING_FAST";
    public const string ReserveEmpty = "RESERVE_EMPTY";
    public const string Reserve = "RESERVE";
    /// <summary>Lote 16 (D6): en una devolución, la cuarentena primero.</summary>
    public const string QuarantineReturn = "QUARANTINE_RETURN";

    public static string ReasonLabel(string reasonCode) => reasonCode switch
    {
        QuarantineReturn => "Cuarentena (devolución)",
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
        => (c.MaxWeightKg is not decimal max || unitWeightKg is not decimal w || c.CurrentWeightKg + qty * w <= max)
           && (c.MaxCapacityQty is not int cap || c.CurrentQty + qty <= cap);

    private static readonly string[] ReasonOrder = { QuarantineReturn, Preferred, ConsolidateLot, Consolidate, PickingFast, ReserveEmpty, Reserve };

    public static IReadOnlyList<PutawayRank> Rank(IEnumerable<PutawayCandidate> candidates, int? lotId, decimal quantity,
        decimal? unitWeightKg, string rotationClass, int? excludeBinId, int take, bool preferQuarantine = false, bool includeOverCapacity = false)
    {
        var ranked = new List<(int Order, PutawayCandidate C, string Reason, bool Fits)>();
        foreach (var c in candidates ?? Enumerable.Empty<PutawayCandidate>())
        {
            if (!c.IsActive || c.BinId == excludeBinId) continue;
            if (c.ZoneTypeCode is ZoneTypes.Staging or ZoneTypes.CrossDock or ZoneTypes.Rental) continue;   // Lote 27: nunca se acomoda en En renta
            if (c.ZoneTypeCode is ZoneTypes.Quarantine && !preferQuarantine) continue;
            var fits = HasCapacity(c, quantity, unitWeightKg);
            if (!fits && !includeOverCapacity) continue;
            string? reason = null;
            if (c.ZoneTypeCode is ZoneTypes.Quarantine) reason = QuarantineReturn;
            else if (c.IsPreferred) reason = Preferred;
            else if (lotId is not null && c.LotQty > 0m) reason = ConsolidateLot;
            else if (c.ProductQty > 0m) reason = Consolidate;
            else if (c.ZoneTypeCode == ZoneTypes.Refrigerated) reason = null;
            else if (c.ZoneTypeCode == ZoneTypes.Picking)
                reason = rotationClass == RotationClasses.Fast && c.IsEmpty ? PickingFast : null;
            else if (c.ZoneTypeCode == ZoneTypes.Reserve) reason = c.IsEmpty ? ReserveEmpty : Reserve;
            if (reason is null) continue;
            ranked.Add((Array.IndexOf(ReasonOrder, reason), c, reason, fits));
        }
        // Las que caben primero (en el orden de razones); las que exceden el cupo, si se pidieron, al final.
        return ranked
            .OrderBy(r => r.Fits ? 0 : 1)
            .ThenBy(r => r.Order)
            .ThenBy(r => r.C.BinCode, StringComparer.Ordinal)
            .ThenBy(r => r.C.BinId)
            .Take(Math.Max(0, take))
            .Select(r => new PutawayRank(r.C.BinId, r.C.BinCode, r.C.ZoneCode, r.C.ZoneTypeCode, r.Reason, ReasonLabel(r.Reason), rotationClass,
                r.C.MaxCapacityQty, r.C.CurrentQty, r.Fits))
            .ToList();
    }
}
