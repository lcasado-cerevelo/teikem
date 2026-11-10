using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 148 — valuación del inventario sobre las capas de costo (líneas de recibo confirmadas, Lote 147) según el método de la compañía (<see cref="ValuationRules"/>).
/// Solo LEE: ni mueve inventario ni toca saldos. Las capas son por producto (el lote no cambia el costo en esta versión). Lo que hay en mano es la suma de los saldos de todos los almacenes
/// (o del almacén pedido). Sirve al reporte y al costo congelado de las salidas (<see cref="IssueCostsAsync"/>).
/// </summary>
public sealed class ValuationService(TeikemDbContext db)
{
    public const int MaxTake = 500;

    public async Task<string> MethodAsync(CancellationToken ct)
        => ValuationRules.Parse(await db.Tenants.AsNoTracking().Where(t => t.TenantId == db.CurrentTenantId).Select(t => t.ValuationMethod).FirstOrDefaultAsync(ct)) ?? ValuationMethods.Average;

    /// <summary>
    /// Capas de cada producto, de la más vieja a la más nueva: líneas de recibos confirmados y activos con algo recibido. Lote 232: también los ARMADOS de un kit armado
    /// (los kits que siguen armados, al costo sumado de sus piezas) y los DESARMADOS de sus piezas (lo que volvió, al costo con que salió), por su fecha.
    /// </summary>
    private async Task<Dictionary<int, List<CostLayer>>> LayersAsync(IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return new Dictionary<int, List<CostLayer>>();
        var rows = (await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           join h in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals h.ReceiptHeaderId
                           where productIds.Contains(l.ProductId) && h.IsActive && h.ReceivedAtUtc != null && l.ReceivedQty > 0m
                           select new { l.ProductId, Qty = l.ReceivedQty, l.UnitCost, At = h.ReceivedAtUtc!.Value, Source = 0, Seq = l.ReceiptLineId }).ToListAsync(ct)).ToList();
        // Lote 232: armados del kit (lo que sigue armado) y piezas devueltas por desarmados
        rows.AddRange(await db.Set<KitAssembly>().AsNoTracking()
            .Where(a => productIds.Contains(a.KitProductId) && a.Quantity > a.DisassembledQty)
            .Select(a => new { ProductId = a.KitProductId, Qty = a.Quantity - a.DisassembledQty, a.UnitCost, At = a.CreatedAtUtc, Source = 1, Seq = a.KitAssemblyId }).ToListAsync(ct));
        rows.AddRange(await (from l in db.Set<KitDisassemblyLine>().AsNoTracking()
                             join d in db.Set<KitDisassembly>().AsNoTracking() on l.KitDisassemblyId equals d.KitDisassemblyId
                             where productIds.Contains(l.ComponentProductId) && l.Quantity > 0m
                             select new { ProductId = l.ComponentProductId, Qty = l.Quantity, l.UnitCost, At = d.CreatedAtUtc, Source = 2, Seq = l.KitDisassemblyLineId }).ToListAsync(ct));
        return rows.OrderBy(r => r.At).ThenBy(r => r.Source).ThenBy(r => r.Seq)
            .GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Select(r => new CostLayer(r.Qty, r.UnitCost)).ToList());
    }

    /// <summary>
    /// Lote 232 — VALOR de lo que acaba de salir de cada producto (armado de kits): se llama con el inventario ya asentado; lo que había antes es lo que hay ahora más lo
    /// que salió. Valor = valor antes − valor después según el método de la compañía (así el valor total del inventario no cambia cuando ese valor entra como capa del kit).
    /// UnvaluedQty = unidades que salieron sin costo en ningún lado (ni capa ni costo del producto).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, (decimal Value, decimal UnvaluedQty)>> ConsumedValueAsync(IReadOnlyDictionary<int, decimal> issuedByProduct, CancellationToken ct)
    {
        var result = new Dictionary<int, (decimal, decimal)>();
        if (issuedByProduct.Count == 0) return result;
        var method = await MethodAsync(ct);
        var ids = issuedByProduct.Keys.ToList();
        var costs = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.PurchaseCost, ct);
        var layers = await LayersAsync(ids, ct);
        var onHandNow = (await db.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.ProductId)).Select(b => new { b.ProductId, b.QtyOnHand }).ToListAsync(ct))
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        foreach (var (productId, issued) in issuedByProduct)
        {
            var now = onHandNow.GetValueOrDefault(productId);
            var productLayers = layers.GetValueOrDefault(productId) ?? new List<CostLayer>();
            var before = ValuationRules.Value(method, now + issued, productLayers, costs.GetValueOrDefault(productId));
            var after = ValuationRules.Value(method, now, productLayers, costs.GetValueOrDefault(productId));
            result[productId] = (before.Value - after.Value, before.UnvaluedQty - after.UnvaluedQty);
        }
        return result;
    }

    /// <summary>
    /// Costo unitario de cada salida de una recolección, EN ORDEN, para congelarlo en su línea. Se llama con el inventario ya asentado: lo que había antes de sacar es lo que hay ahora más
    /// lo que sale. Con capas, cada salida de un producto consume primero la capa más vieja (se avanza línea por línea). null = sin costo en ningún lado.
    /// </summary>
    public async Task<IReadOnlyList<decimal?>> IssueCostsAsync(IReadOnlyList<(int ProductId, decimal Quantity)> issues, CancellationToken ct)
    {
        var result = new decimal?[issues.Count];
        if (issues.Count == 0) return result;
        var method = await MethodAsync(ct);
        var ids = issues.Select(i => i.ProductId).Distinct().ToList();
        var costs = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.PurchaseCost, ct);
        var layers = await LayersAsync(ids, ct);
        var onHandNow = (await db.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.ProductId)).Select(b => new { b.ProductId, b.QtyOnHand }).ToListAsync(ct))
            .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
        var before = ids.ToDictionary(id => id, id => onHandNow.GetValueOrDefault(id) + issues.Where(i => i.ProductId == id).Sum(i => i.Quantity));
        for (var i = 0; i < issues.Count; i++)
        {
            var (productId, qty) = issues[i];
            result[i] = ValuationRules.IssueUnitCost(method, before[productId], layers.GetValueOrDefault(productId) ?? new List<CostLayer>(), costs.GetValueOrDefault(productId));
            before[productId] -= qty;
        }
        return result;
    }

    /// <param name="warehousePublicId">Solo ese almacén (404 si no es de la compañía); null = todos.</param>
    /// <param name="clientPublicId">Solo el inventario de ese dueño (404 si no es de la compañía); null = todos.</param>
    public async Task<ValuationReportDto> ReportAsync(Guid? warehousePublicId, Guid? clientPublicId, string? search, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = take <= 0 ? MaxTake : Math.Min(take, MaxTake);
        int? warehouseId = null;
        if (warehousePublicId is Guid wp)
            warehouseId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == wp).Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Almacén");
        int? clientId = null;
        if (clientPublicId is Guid cp)
            clientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Cliente");
        var q = search?.Trim();

        var balances = db.Set<StockBalance>().AsNoTracking().AsQueryable();
        if (warehouseId is int w) balances = balances.Where(b => b.WarehouseId == w);
        var onHand = (await balances.GroupBy(b => b.ProductId).Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.QtyOnHand) }).ToListAsync(ct))
            .Where(x => x.Qty > 0m).ToDictionary(x => x.ProductId, x => x.Qty);

        var products = db.Set<Product>().AsNoTracking().Where(p => onHand.Keys.Contains(p.ProductId));
        if (clientId is int c) products = products.Where(p => p.ClientId == c);
        if (!string.IsNullOrEmpty(q)) products = products.Where(p => p.Sku.Contains(q) || p.Name.Contains(q));
        var plist = await products.Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name, p.ClientId, p.PurchaseCost }).ToListAsync(ct);

        var method = await MethodAsync(ct);
        var layers = await LayersAsync(plist.Select(p => p.ProductId).ToList(), ct);
        var clientIds = plist.Where(p => p.ClientId != null).Select(p => p.ClientId!.Value).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(cl => clientIds.Contains(cl.ClientId)).Select(cl => new { cl.ClientId, cl.PublicId, cl.Name }).ToDictionaryAsync(cl => cl.ClientId, ct);

        var rows = plist.Select(p =>
        {
            var qty = onHand[p.ProductId];
            var v = ValuationRules.Value(method, qty, layers.GetValueOrDefault(p.ProductId) ?? new List<CostLayer>(), p.PurchaseCost);
            var owner = p.ClientId is int cid ? clients.GetValueOrDefault(cid) : null;
            return new ValuationRowDto(p.PublicId, p.Sku, p.Name, owner?.PublicId, owner?.Name, qty, v.UnitCost, v.Value, v.UnvaluedQty, p.PurchaseCost);
        }).OrderByDescending(r => r.Value).ThenBy(r => r.Sku, StringComparer.OrdinalIgnoreCase).ToList();

        return new ValuationReportDto(rows.Count, skip, take, rows.Skip(skip).Take(take).ToList(), decimal.Round(rows.Sum(r => r.Value), 4, MidpointRounding.AwayFromZero),
            rows.Sum(r => r.OnHand), rows.Sum(r => r.UnvaluedQty), method);
    }
}
