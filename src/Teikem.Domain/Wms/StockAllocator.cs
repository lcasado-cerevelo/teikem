using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Saldo candidato para asignar una salida. Available = en mano − reservado (lo reservado no se asigna).</summary>
public sealed record StockCandidate(int BinId, string BinCode, string? ZoneTypeCode, int? LotId, DateOnly? ExpiryDate, decimal Available);

/// <summary>Toma de una posición/lote.</summary>
public sealed record StockAllocation(int BinId, int? LotId, decimal Quantity);

/// <summary>Resultado de la asignación: tomas en orden y faltante (0 si alcanzó).</summary>
public sealed record StockAllocationResult(IReadOnlyList<StockAllocation> Allocations, decimal Shortfall)
{
    public bool IsComplete => Shortfall <= 0m;
}

/// <summary>
/// Lote 6 (D14) — Asignación pura de una salida sobre saldos:
/// 1. FEFO: vencimiento ascendente, sin vencimiento al final;
/// 2. zona PICKING &lt; RESERVE &lt; REFRIGERATED &lt; STAGING (otras al final);
/// 3. desempate determinista por código de posición y lote.
/// Excluye QUARANTINE, CROSSDOCK y disponible ≤ 0. lotId/binId restringen los candidatos si vienen.
/// </summary>
public static class StockAllocator
{
    private static readonly string[] ZoneOrder = { ZoneTypes.Picking, ZoneTypes.Reserve, ZoneTypes.Refrigerated, ZoneTypes.Staging };

    public static bool IsExcludedZone(string? zoneType) => zoneType is ZoneTypes.Quarantine or ZoneTypes.CrossDock;

    public static int ZoneRank(string? zoneType)
    {
        var i = Array.IndexOf(ZoneOrder, zoneType);
        return i < 0 ? ZoneOrder.Length : i;
    }

    public static StockAllocationResult Allocate(decimal quantity, int? lotId, int? binId, IEnumerable<StockCandidate> candidates)
    {
        var remaining = quantity;
        var result = new List<StockAllocation>();
        var ordered = (candidates ?? Enumerable.Empty<StockCandidate>())
            .Where(c => c.Available > 0m && !IsExcludedZone(c.ZoneTypeCode))
            .Where(c => lotId is null || c.LotId == lotId)
            .Where(c => binId is null || c.BinId == binId)
            .OrderBy(c => c.ExpiryDate.HasValue ? 0 : 1)
            .ThenBy(c => c.ExpiryDate)
            .ThenBy(c => ZoneRank(c.ZoneTypeCode))
            .ThenBy(c => c.BinCode, StringComparer.Ordinal)
            .ThenBy(c => c.BinId)
            .ThenBy(c => c.LotId ?? 0);
        foreach (var c in ordered)
        {
            if (remaining <= 0m) break;
            var take = Math.Min(remaining, c.Available);
            result.Add(new StockAllocation(c.BinId, c.LotId, take));
            remaining -= take;
        }
        return new StockAllocationResult(result, remaining > 0m ? remaining : 0m);
    }
}
