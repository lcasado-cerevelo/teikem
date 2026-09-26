using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Producto candidato a reabasto de su posición preferida de PICKING (D23).</summary>
/// <param name="PickBinOnHand">Existencia EN MANO actual de la posición preferida (todos los lotes).</param>
/// <param name="HasOpenTask">Ya hay una REPLENISH abierta hacia esa posición (idempotencia por almacén).</param>
public sealed record ReplenishmentProduct(
    int ProductId, string Sku, int PickBinId, decimal? MinPickQty, decimal? MaxPickQty, decimal PickBinOnHand, bool HasOpenTask);

/// <summary>Saldo de origen posible (una fila de StockBalance del mismo producto y almacén).</summary>
public sealed record ReplenishmentSource(
    int BinId, string BinCode, string? ZoneTypeCode, bool BinActive, int? LotId, DateOnly? ExpiryDate, decimal QtyOnHand, decimal QtyReserved);

/// <summary>Movimiento planeado RESERVE → PICKING: una tarea REPLENISH por asignación (posición y lote).</summary>
public sealed record ReplenishmentMove(int FromBinId, int? LotId, decimal Quantity);

/// <summary>Resultado del plan de un producto: movimientos o el motivo de omisión (OPEN_TASK, NO_RESERVE, NOT_BELOW_MIN).</summary>
public sealed record ReplenishmentPlan(int ProductId, decimal Target, decimal Needed, string? SkipReason, IReadOnlyList<ReplenishmentMove> Moves);

/// <summary>
/// Lote 6 (P5) — reabasto bajo demanda de la posición de picking (R15, D23), puro:
/// - Aplica a productos con MinPickQty y posición preferida en una zona PICKING (sin serie; lo filtra el servicio).
/// - Dispara cuando la existencia en mano de la posición preferida es MENOR que MinPickQty.
/// - Objetivo = MaxPickQty o, si no hay, 2 × MinPickQty; se pide objetivo − existencia, acotado por la reserva disponible.
/// - Origen: SOLO saldos de zonas RESERVE activas, con disponible = en mano − reservado &gt; 0 (lo reservado no se toca), en
///   orden FEFO (vencimiento ascendente, sin fecha al final), luego código de posición y lote: determinista, el mismo orden
///   que StockAllocator restringido a RESERVE.
/// - Omisiones: OPEN_TASK (ya hay una REPLENISH abierta hacia la posición), NOT_BELOW_MIN (no hace falta) y NO_RESERVE (no
///   hay reserva disponible). Una tarea por asignación (posición + lote).
/// </summary>
public static class ReplenishmentRules
{
    public const string SkipOpenTask = "OPEN_TASK";
    public const string SkipNoReserve = "NO_RESERVE";
    public const string SkipNotBelowMin = "NOT_BELOW_MIN";

    /// <summary>¿El producto participa del reabasto? Requiere MinPickQty &gt; 0.</summary>
    public static bool Applies(decimal? minPickQty) => minPickQty is decimal m && m > 0m;

    /// <summary>Objetivo de la posición de picking: MaxPickQty o 2 × MinPickQty.</summary>
    public static decimal Target(decimal minPickQty, decimal? maxPickQty)
        => maxPickQty is decimal max && max > 0m ? max : 2m * minPickQty;

    public static bool IsBelowMin(decimal onHand, decimal minPickQty) => onHand < minPickQty;

    /// <summary>Disponible de un saldo (lo reservado no se reabastece).</summary>
    public static decimal Available(decimal onHand, decimal reserved) => Math.Max(0m, onHand - reserved);

    /// <summary>Orden FEFO de las fuentes RESERVE elegibles (activas, con disponible, distintas de la posición de picking).</summary>
    public static IReadOnlyList<ReplenishmentSource> EligibleSources(IEnumerable<ReplenishmentSource> sources, int pickBinId)
        => sources
            .Where(s => s.BinActive && s.BinId != pickBinId
                        && string.Equals(s.ZoneTypeCode, ZoneTypes.Reserve, StringComparison.OrdinalIgnoreCase)
                        && Available(s.QtyOnHand, s.QtyReserved) > 0m)
            .OrderBy(s => s.ExpiryDate is null ? 1 : 0)
            .ThenBy(s => s.ExpiryDate)
            .ThenBy(s => s.BinCode, StringComparer.Ordinal)
            .ThenBy(s => s.BinId)
            .ThenBy(s => s.LotId ?? 0)
            .ToList();

    /// <summary>Plan de reabasto de un producto.</summary>
    public static ReplenishmentPlan Plan(ReplenishmentProduct product, IEnumerable<ReplenishmentSource> sources)
    {
        var min = product.MinPickQty ?? 0m;
        var target = Applies(product.MinPickQty) ? Target(min, product.MaxPickQty) : 0m;
        var needed = Math.Max(0m, target - product.PickBinOnHand);

        if (!Applies(product.MinPickQty) || !IsBelowMin(product.PickBinOnHand, min) || needed <= 0m)
            return new ReplenishmentPlan(product.ProductId, target, 0m, SkipNotBelowMin, Array.Empty<ReplenishmentMove>());
        if (product.HasOpenTask)
            return new ReplenishmentPlan(product.ProductId, target, needed, SkipOpenTask, Array.Empty<ReplenishmentMove>());

        var moves = new List<ReplenishmentMove>();
        var remaining = needed;
        foreach (var s in EligibleSources(sources, product.PickBinId))
        {
            if (remaining <= 0m) break;
            var take = Math.Min(remaining, Available(s.QtyOnHand, s.QtyReserved));
            if (take <= 0m) continue;
            moves.Add(new ReplenishmentMove(s.BinId, s.LotId, take));
            remaining -= take;
        }
        return moves.Count == 0
            ? new ReplenishmentPlan(product.ProductId, target, needed, SkipNoReserve, Array.Empty<ReplenishmentMove>())
            : new ReplenishmentPlan(product.ProductId, target, needed, null, moves);
    }
}
