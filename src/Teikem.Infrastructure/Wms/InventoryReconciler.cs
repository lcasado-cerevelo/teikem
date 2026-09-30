using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Wms;

/// <summary>Resultado de una pasada del reconciliador: descuadres, saldos revisados y productos que se revisaron.</summary>
public sealed record ReconcilerResult(IReadOnlyList<ReconciliationMismatch> Mismatches, int BalancesChecked, IReadOnlySet<int> ProductsSeen);

/// <summary>
/// Lote 14 (P1) — conciliación Kárdex ↔ saldo de SOLO LECTURA, extraída tal cual de TraceabilityService.ReconcileAsync (que
/// ahora delega aquí) y usada también por InventoryReconciliationService y el cierre de import-legacy (antes duplicado en
/// InventoryLedger.ReconcileAsync, que cargaba todo el ledger en memoria y se retiró).
/// - Agrega en SQL: lado To y lado From por (producto, almacén, posición, lote) con Σ |Q|; neto por producto sin TRANSFER; y los
///   saldos. La comparación es la regla pura ReconciliationRules.Compare.
/// - Sin bloqueos: una pasada puede ver un movimiento a medias entre dos consultas (la base no usa RCSI). Quien persiste
///   descuadres confirma cada producto bajo bloqueo (InventoryReconciliationService) antes de abrir o cerrar nada.
/// - TenantId del principal: InventoryTransaction y StockBalance llevan el filtro global de tenant.
/// </summary>
public sealed class InventoryReconciler(TeikemDbContext db, ILookupCache lookups, InventoryReadService reads)
{
    /// <summary>
    /// Descuadres de los productos indicados (NULL = todo el tenant). ProductsSeen = productos con movimientos o saldos en la
    /// pasada (con una lista, además, los pedidos: un producto sin nada también está "revisado" y cuadrado).
    /// </summary>
    public async Task<ReconcilerResult> ComputeAsync(IReadOnlyCollection<int>? productIds, CancellationToken ct)
    {
        var txns = db.Set<InventoryTransaction>().AsNoTracking().AsQueryable();
        var balancesQuery = db.Set<StockBalance>().AsNoTracking().AsQueryable();
        if (productIds is not null)
        {
            var ids = productIds.Distinct().ToList();
            if (ids.Count == 0) return new ReconcilerResult(Array.Empty<ReconciliationMismatch>(), 0, new HashSet<int>());
            if (ids.Count == 1)
            {
                var only = ids[0];
                txns = txns.Where(t => t.ProductId == only);
                balancesQuery = balancesQuery.Where(b => b.ProductId == only);
            }
            else
            {
                txns = txns.Where(t => ids.Contains(t.ProductId));
                balancesQuery = balancesQuery.Where(b => ids.Contains(b.ProductId));
            }
        }

        var toSides = await txns.Where(t => t.ToWarehouseId != null)
            .GroupBy(t => new { t.ProductId, t.ToWarehouseId, t.ToBinId, t.LotId })
            .Select(g => new { g.Key.ProductId, g.Key.ToWarehouseId, g.Key.ToBinId, g.Key.LotId, Qty = g.Sum(t => Math.Abs(t.Quantity)) })
            .ToListAsync(ct);
        var fromSides = await txns.Where(t => t.FromWarehouseId != null)
            .GroupBy(t => new { t.ProductId, t.FromWarehouseId, t.FromBinId, t.LotId })
            .Select(g => new { g.Key.ProductId, g.Key.FromWarehouseId, g.Key.FromBinId, g.Key.LotId, Qty = g.Sum(t => Math.Abs(t.Quantity)) })
            .ToListAsync(ct);
        var rebuilt = KardexRules.Rebuild(
            toSides.Select(x => (new BalanceKey(x.ProductId, x.ToWarehouseId!.Value, x.ToBinId, x.LotId), x.Qty)),
            fromSides.Select(x => (new BalanceKey(x.ProductId, x.FromWarehouseId!.Value, x.FromBinId, x.LotId), x.Qty)));

        var transferId = await lookups.TryGetIdAsync(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer, ct) ?? -1;
        var netByProduct = await txns.Where(t => t.TxnTypeLookupId != transferId)
            .GroupBy(t => t.ProductId)
            .Select(g => new { ProductId = g.Key, Net = g.Sum(t => t.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Net, ct);

        var balances = await balancesQuery
            .Select(b => new { b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId, b.QtyOnHand })
            .ToListAsync(ct);
        var balanceByKey = balances.GroupBy(b => new BalanceKey(b.ProductId, b.WarehouseId, b.WarehouseBinId, b.LotId))
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        var onHandByProduct = balances.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));

        var mismatches = ReconciliationRules.Compare(rebuilt, balanceByKey, netByProduct, onHandByProduct);
        var seen = rebuilt.Keys.Select(k => k.ProductId).Concat(netByProduct.Keys).Concat(onHandByProduct.Keys).ToHashSet();
        if (productIds is not null) seen.UnionWith(productIds);
        return new ReconcilerResult(mismatches, balances.Count, seen);
    }

    /// <summary>
    /// Filas legibles de los descuadres (SKU, almacén, posición y lote; el total del producto lleva el almacén '—'), en el
    /// orden de entrada. Catálogos por lotes de consultas (sin N+1).
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationRowDto>> ToRowsAsync(IReadOnlyList<ReconciliationMismatch> mismatches, CancellationToken ct)
    {
        if (mismatches.Count == 0) return Array.Empty<ReconciliationRowDto>();
        var products = await reads.ProductInfoAsync(mismatches.Select(m => m.Key.ProductId).Distinct().ToList(), ct);
        var warehouses = await reads.WarehouseInfoAsync(mismatches.Where(m => !m.ProductTotal).Select(m => m.Key.WarehouseId), ct);
        var bins = await reads.BinInfoAsync(mismatches.Where(m => m.Key.BinId.HasValue).Select(m => m.Key.BinId!.Value), ct);
        var lots = await reads.LotInfoAsync(mismatches.Where(m => m.Key.LotId.HasValue).Select(m => m.Key.LotId!.Value), ct);
        var rows = new List<ReconciliationRowDto>(mismatches.Count);
        foreach (var m in mismatches)
        {
            var p = products.GetValueOrDefault(m.Key.ProductId);
            rows.Add(new ReconciliationRowDto(p?.PublicId ?? Guid.Empty, p?.Sku ?? string.Empty,
                m.ProductTotal ? TraceabilityService.ProductTotalMarker : warehouses.GetValueOrDefault(m.Key.WarehouseId)?.Code ?? string.Empty,
                m.Key.BinId is int b ? bins.GetValueOrDefault(b)?.Code : null,
                m.Key.LotId is int l ? lots.GetValueOrDefault(l)?.Number : null,
                m.LedgerQty, m.BalanceQty));
        }
        return rows;
    }
}
