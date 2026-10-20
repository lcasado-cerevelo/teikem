using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Cuántos avisos nuevos salieron por evento en una revisión (0 = nada nuevo que avisar o ya se había avisado esta semana).</summary>
public sealed record InventoryAlertScanResult(int Expiring, int Expired, int Low, int Out, int NoMovement)
{
    public int Total => Expiring + Expired + Low + Out + NoMovement;
}

/// <summary>
/// Lote 141 (2026-10-20) — revisión de inventario que publica los avisos de <see cref="InventoryEventProvider"/>: lotes por vencer y vencidos (con existencia), productos bajo el
/// mínimo, sin existencia y sin movimiento. Lo corre el trabajo diario INVENTORY_ALERT_SCAN, una compañía a la vez. Cada evento sale como UN aviso con el conjunto completo; la clave
/// de no repetición (<see cref="InventoryAlertRules.DedupKey"/>) evita repetirlo si el conjunto no cambió esa semana. Las decisiones están en <see cref="InventoryAlertRules"/>.
/// Para sumar otro aviso de inventario: un evento en <see cref="InventoryEventProvider"/> y un método Detect… aquí, llamado desde <see cref="ScanAsync"/>.
/// </summary>
public sealed class InventoryAlertService(TeikemDbContext db, ITenantClock clock, INotificationPublisher notifications)
{
    private static string Qty(decimal n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<InventoryAlertScanResult> ScanAsync(CancellationToken ct)
    {
        var settings = await db.Tenants.AsNoTracking().Where(t => t.TenantId == db.CurrentTenantId)
            .Select(t => new { t.InventoryExpiryWarnDays, t.InventoryNoMovementDays }).FirstOrDefaultAsync(ct);
        var warnDays = settings?.InventoryExpiryWarnDays ?? InventoryAlertRules.DefaultExpiryWarnDays;
        var staleDays = settings?.InventoryNoMovementDays ?? InventoryAlertRules.DefaultNoMovementDays;

        var (expiring, expired) = await DetectLotsAsync(warnDays, ct);
        var low = await DetectLowAsync(ct);
        var (outOfStock, stale) = await DetectStockAsync(staleDays, ct);

        var result = new InventoryAlertScanResult(
            await PublishAsync(InventoryEventCodes.LotExpiring, "LEX", expiring, new Dictionary<string, string?> { ["days"] = warnDays.ToString(CultureInfo.InvariantCulture) }, ct),
            await PublishAsync(InventoryEventCodes.LotExpired, "LEXD", expired, null, ct),
            await PublishAsync(InventoryEventCodes.StockLow, "LOW", low, null, ct),
            await PublishAsync(InventoryEventCodes.StockOut, "OUT", outOfStock, null, ct),
            await PublishAsync(InventoryEventCodes.NoMovement, "NMV", stale, new Dictionary<string, string?> { ["days"] = staleDays.ToString(CultureInfo.InvariantCulture) }, ct));
        if (result.Total > 0) await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Publica un aviso con el conjunto (si hay algo): 1 si salió nuevo, 0 si no hay nada o ya se había avisado igual esta semana.</summary>
    private async Task<int> PublishAsync(string eventCode, string keyPrefix, IReadOnlyList<Detected> items, Dictionary<string, string?>? extra, CancellationToken ct)
    {
        if (items.Count == 0) return 0;
        var values = new Dictionary<string, string?>(extra ?? new())
        {
            ["count"] = items.Count.ToString(CultureInfo.InvariantCulture),
            ["items"] = InventoryAlertRules.ItemsText(items.Select(i => i.Text).ToList()),
        };
        var key = InventoryAlertRules.DedupKey(keyPrefix, clock.UtcNow, items.Select(i => i.Id));
        return await notifications.PublishAsync(eventCode, values, key, ct) ? 1 : 0;
    }

    private sealed record Detected(string Id, string Text);

    /// <summary>Lotes con existencia (en mano &gt; 0) y fecha de vencimiento: los vencidos y los que vencen dentro del plazo. Por lote y almacén.</summary>
    private async Task<(List<Detected> Expiring, List<Detected> Expired)> DetectLotsAsync(int warnDays, CancellationToken ct)
    {
        var today = clock.Today;
        var limit = today.AddDays(warnDays);
        var rows = await (from b in db.StockBalances.AsNoTracking()
                          join l in db.InventoryLots.AsNoTracking() on b.LotId equals (int?)l.LotId
                          join p in db.Products.AsNoTracking() on b.ProductId equals p.ProductId
                          join w in db.Warehouses.AsNoTracking() on b.WarehouseId equals w.WarehouseId
                          where b.QtyOnHand > 0m && l.IsActive && p.IsActive && l.ExpiryDate != null && l.ExpiryDate <= limit
                          select new { l.LotId, b.WarehouseId, p.Sku, l.LotNumber, Expiry = l.ExpiryDate!.Value, b.QtyOnHand, Warehouse = w.Code }).ToListAsync(ct);
        var grouped = rows.GroupBy(r => (r.LotId, r.WarehouseId))
            .Select(g => new { g.Key, g.First().Sku, g.First().LotNumber, g.First().Expiry, g.First().Warehouse, Qty = g.Sum(x => x.QtyOnHand) })
            .OrderBy(x => x.Expiry).ThenBy(x => x.Sku, StringComparer.Ordinal).ThenBy(x => x.LotNumber, StringComparer.Ordinal).ToList();
        var expired = new List<Detected>();
        var expiring = new List<Detected>();
        foreach (var x in grouped)
        {
            var item = new Detected($"{x.Key.LotId}/{x.Key.WarehouseId}", $"{x.Sku} · {x.LotNumber} · {Day(x.Expiry)} · {Qty(x.Qty)} · {x.Warehouse}");
            if (InventoryAlertRules.IsExpired(x.Expiry, today)) expired.Add(item);
            else if (InventoryAlertRules.IsExpiring(x.Expiry, today, warnDays)) expiring.Add(item);
        }
        return (expiring, expired);
    }

    /// <summary>Productos activos con mínimo cuyo disponible total (en mano − reservado, todos los almacenes) es menor que el mínimo.</summary>
    private async Task<List<Detected>> DetectLowAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.MinQty != null).Select(p => new { p.ProductId, p.Sku, p.MinQty, p.IsActive }).ToListAsync(ct);
        if (products.Count == 0) return new();
        var avail = await db.StockBalances.AsNoTracking().GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Available = g.Sum(b => b.QtyOnHand - b.QtyReserved) }).ToDictionaryAsync(x => x.ProductId, x => x.Available, ct);
        return products
            .Select(p => (p, available: avail.GetValueOrDefault(p.ProductId)))
            .Where(x => InventoryAlertRules.IsLow(x.p.MinQty, x.available, x.p.IsActive))
            .OrderBy(x => x.p.Sku, StringComparer.Ordinal)
            .Select(x => new Detected(x.p.ProductId.ToString(CultureInfo.InvariantCulture), $"{x.p.Sku} · {Qty(x.available)} · {Qty(x.p.MinQty!.Value)}"))
            .ToList();
    }

    /// <summary>Sin existencia (tuvo movimientos y ya no queda nada en mano) y sin movimiento (hay existencia y el último movimiento es viejo).</summary>
    private async Task<(List<Detected> Out, List<Detected> Stale)> DetectStockAsync(int staleDays, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var onHand = await db.StockBalances.AsNoTracking().GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, OnHand = g.Sum(b => b.QtyOnHand) }).ToDictionaryAsync(x => x.ProductId, x => x.OnHand, ct);
        var last = await db.InventoryTransactions.AsNoTracking().GroupBy(t => t.ProductId)
            .Select(g => new { ProductId = g.Key, Last = g.Max(t => t.CreatedAtUtc) }).ToDictionaryAsync(x => x.ProductId, x => x.Last, ct);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.ProductId, p.Sku }).ToListAsync(ct);

        var outList = new List<Detected>();
        var staleList = new List<Detected>();
        foreach (var p in products.OrderBy(p => p.Sku, StringComparer.Ordinal))
        {
            var qty = onHand.GetValueOrDefault(p.ProductId);
            last.TryGetValue(p.ProductId, out var lastUtc);
            if (InventoryAlertRules.IsOutOfStock(last.ContainsKey(p.ProductId), qty))
                outList.Add(new Detected(p.ProductId.ToString(CultureInfo.InvariantCulture), p.Sku));
            else if (InventoryAlertRules.IsStale(last.ContainsKey(p.ProductId) ? lastUtc : null, now, staleDays, qty))
                staleList.Add(new Detected(p.ProductId.ToString(CultureInfo.InvariantCulture), $"{p.Sku} · {(int)(now - lastUtc).TotalDays} · {Qty(qty)}"));
        }
        return (outList, staleList);
    }
}
