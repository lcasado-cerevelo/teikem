using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (P0, D24) — putaway dirigido. Carga por lotes (sin N+1) las posiciones activas del almacén, sus zonas, los saldos y
/// los pesos; calcula la rotación con UNA consulta (salidas ISSUE + CROSSDOCK del producto en los últimos 30 días, que en el
/// ledger son negativas) contra la existencia total, y delega el orden en PutawayRules (puras).
/// </summary>
public sealed class PutawaySuggester(TeikemDbContext db, ILookupCache lookups)
{
    public const int RotationWindowDays = 30;

    public async Task<IReadOnlyList<PutawaySuggestion>> SuggestAsync(int warehouseId, int productId, int? lotId, decimal quantity, int? excludeBinId,
        int take, CancellationToken ct)
    {
        if (take <= 0) return Array.Empty<PutawaySuggestion>();
        var product = await db.Products.AsNoTracking().Where(p => p.ProductId == productId)
            .Select(p => new { p.ProductId, p.WeightKg, p.PreferredWarehouseId, p.PreferredBinId }).FirstOrDefaultAsync(ct);
        if (product is null) return Array.Empty<PutawaySuggestion>();

        var zoneTypes = (await lookups.GetDomainAsync(LookupDomains.ZoneType, ct)).ToDictionary(l => l.LookupCodeId, l => l.InternalCode);
        var bins = await (from b in db.WarehouseBins.AsNoTracking()
                          join z in db.WarehouseZones.AsNoTracking() on b.WarehouseZoneId equals z.WarehouseZoneId
                          join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where b.WarehouseId == warehouseId
                          select new { b.WarehouseBinId, b.Code, b.MaxWeightKg, BinActive = b.IsActive, z.WarehouseZoneId, ZoneCode = z.Code, ZoneActive = z.IsActive, z.ZoneTypeLookupId })
            .ToListAsync(ct);
        if (bins.Count == 0) return Array.Empty<PutawaySuggestion>();

        var balances = await db.StockBalances.AsNoTracking()
            .Where(s => s.WarehouseId == warehouseId && s.WarehouseBinId != null && s.QtyOnHand != 0m)
            .Select(s => new { BinId = s.WarehouseBinId!.Value, s.ProductId, s.LotId, s.QtyOnHand })
            .ToListAsync(ct);
        var otherProductIds = balances.Select(b => b.ProductId).Distinct().ToList();
        var weights = await db.Products.AsNoTracking().Where(p => otherProductIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => p.WeightKg ?? 0m, ct);

        // Rotación: salidas de 30 días (−Σ Quantity de ISSUE y CROSSDOCK, negativas en el ledger) contra la existencia total.
        var issueId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Issue, ct) ?? -1;
        var crossDockId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.CrossDock, ct) ?? -1;
        var since = DateTime.UtcNow.AddDays(-RotationWindowDays);
        var issued30d = -await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.ProductId == productId && t.CreatedAtUtc >= since && (t.TxnTypeLookupId == issueId || t.TxnTypeLookupId == crossDockId))
            .SumAsync(t => (decimal?)t.Quantity, ct) ?? 0m;
        var onHandTotal = await db.StockBalances.AsNoTracking().Where(s => s.ProductId == productId).SumAsync(s => (decimal?)s.QtyOnHand, ct) ?? 0m;
        var rotation = PutawayRules.Classify(issued30d, onHandTotal);

        var byBin = balances.GroupBy(b => b.BinId).ToDictionary(g => g.Key, g => g.ToList());
        var candidates = bins.Select(b =>
        {
            var rows = byBin.GetValueOrDefault(b.WarehouseBinId) ?? new();
            var weight = rows.Sum(r => r.QtyOnHand * weights.GetValueOrDefault(r.ProductId));
            return new PutawayCandidate(b.WarehouseBinId, b.Code, b.WarehouseZoneId, b.ZoneCode,
                b.ZoneTypeLookupId is int zt ? zoneTypes.GetValueOrDefault(zt) : null, b.BinActive && b.ZoneActive, b.MaxWeightKg, weight,
                rows.Count == 0, rows.Where(r => r.ProductId == productId).Sum(r => r.QtyOnHand),
                lotId is null ? 0m : rows.Where(r => r.ProductId == productId && r.LotId == lotId).Sum(r => r.QtyOnHand),
                product.PreferredWarehouseId == warehouseId && product.PreferredBinId == b.WarehouseBinId);
        }).ToList();

        return PutawayRules.Rank(candidates, lotId, quantity, product.WeightKg, rotation, excludeBinId, take)
            .Select(r => new PutawaySuggestion(r.BinId, r.BinCode, r.ZoneCode, r.ZoneTypeCode, r.ReasonCode, r.Reason, r.RotationClass))
            .ToList();
    }
}
