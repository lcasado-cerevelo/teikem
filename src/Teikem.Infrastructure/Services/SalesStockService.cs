using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 235 — lo DISPONIBLE al escoger productos en una venta. Una sola lectura para todo el catálogo: los almacenes activos (con el principal marcado) y lo
/// «disponible para vender» de cada producto propio y activo en cada almacén activo — la misma cuenta que usa Solicitar (SalesOrderService.PlanAsync): en mano −
/// reservado, en posiciones activas de zonas activas que no son de cuarentena, cruce de muelle ni renta. Un kit que se arma al pedir (tipo KIT) no tiene
/// existencia: su disponible = kits completos que se arman con sus piezas en ese almacén; el kit armado lleva la suya. Foto sin bloqueo (dos ventas a la vez las
/// resuelve Solicitar, que vuelve a validar).
/// </summary>
public sealed class SalesStockService(TeikemDbContext db, ILookupCache lookups)
{
    public async Task<SalesStockDto> GetAsync(CancellationToken ct)
    {
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Code)
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code, w.Name, w.IsMain }).ToListAsync(ct);
        if (warehouses.Count == 0) return new SalesStockDto(Array.Empty<SalesStockWarehouseDto>(), Array.Empty<SalesStockProductDto>(), Array.Empty<SalesStockKitDto>());
        var mainId = WarehouseRules.MainWarehouseId(warehouses.Select(w => (w.WarehouseId, w.IsMain, true)));
        var warehouseDtos = warehouses.Select(w => new SalesStockWarehouseDto(w.PublicId, w.Code, w.Name, w.WarehouseId == mainId)).ToList();
        var warehouseIds = warehouses.Select(w => w.WarehouseId).ToList();

        // zonas de las que no se vende (PickBatchRules.IsPickableZone): cuarentena, cruce de muelle y renta
        var blocked = new List<int?>();
        foreach (var code in new[] { ZoneTypes.Quarantine, ZoneTypes.CrossDock, ZoneTypes.Rental })
            if (await lookups.TryGetIdAsync(LookupDomains.ZoneType, code, ct) is int id) blocked.Add(id);

        var rows = await (from b in db.StockBalances.AsNoTracking()
                          join bin in db.WarehouseBins.AsNoTracking() on b.WarehouseBinId equals (int?)bin.WarehouseBinId
                          join z in db.WarehouseZones.AsNoTracking() on bin.WarehouseZoneId equals z.WarehouseZoneId
                          join p in db.Products.AsNoTracking() on b.ProductId equals p.ProductId
                          where warehouseIds.Contains(b.WarehouseId) && b.QtyOnHand > b.QtyReserved && bin.IsActive && z.IsActive
                                && p.IsActive && p.ClientId == null && !blocked.Contains(z.ZoneTypeLookupId)
                          group b by new { b.WarehouseId, b.ProductId } into g
                          select new { g.Key.WarehouseId, g.Key.ProductId, Qty = g.Sum(x => x.QtyOnHand - x.QtyReserved) }).ToListAsync(ct);
        // por almacén: producto → disponible
        var sellable = warehouseIds.ToDictionary(id => id, _ => new Dictionary<int, decimal>());
        foreach (var r in rows) sellable[r.WarehouseId][r.ProductId] = r.Qty;

        // kits que se arman al pedir (tipo KIT): sin existencia propia; disponible = kits completos con sus piezas
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var kitIds = kitTypeId is null ? new List<int>()
            : await db.Products.AsNoTracking().Where(p => p.IsActive && p.ClientId == null && p.ProductTypeLookupId == kitTypeId).Select(p => p.ProductId).ToListAsync(ct);
        var kitSet = kitIds.ToHashSet();
        var components = kitIds.Count == 0 ? new Dictionary<int, List<(int ComponentProductId, decimal QtyPerKit)>>()
            : (await db.Set<ProductKitComponent>().AsNoTracking().Where(c => kitIds.Contains(c.KitProductId))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.ProductKitComponentId)
                .Select(c => new { c.KitProductId, c.ComponentProductId, c.Quantity }).ToListAsync(ct))
                .GroupBy(c => c.KitProductId).ToDictionary(g => g.Key, g => g.Select(c => (c.ComponentProductId, c.Quantity)).ToList());

        // producto → (almacén → disponible), solo lo mayor que cero
        var byProduct = new Dictionary<int, List<(int WarehouseId, decimal Qty)>>();
        void Add(int productId, int warehouseId, decimal qty)
        {
            if (qty <= 0m) return;
            if (!byProduct.TryGetValue(productId, out var list)) byProduct[productId] = list = new List<(int, decimal)>();
            list.Add((warehouseId, qty));
        }
        foreach (var w in warehouses)
        {
            var here = sellable[w.WarehouseId];
            foreach (var (productId, qty) in here)
                if (!kitSet.Contains(productId)) Add(productId, w.WarehouseId, qty);
            foreach (var (kitId, comps) in components)
                Add(kitId, w.WarehouseId, SalesStockRules.KitsAvailable(comps, here));
        }
        // Lote 236: los kits con sus piezas viajan siempre (aunque no haya existencia), para desglosar el disponible del kit por pieza
        var productIds = byProduct.Keys.Concat(components.Keys).Concat(components.Values.SelectMany(c => c.Select(x => x.ComponentProductId))).Distinct().ToList();
        if (productIds.Count == 0) return new SalesStockDto(warehouseDtos, Array.Empty<SalesStockProductDto>(), Array.Empty<SalesStockKitDto>());
        var publicIds = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.ProductId)).Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToListAsync(ct);
        var byId = publicIds.ToDictionary(p => p.ProductId);
        var warehousePublic = warehouses.ToDictionary(w => w.WarehouseId, w => w.PublicId);
        var products = publicIds.Where(p => byProduct.ContainsKey(p.ProductId)).OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SalesStockProductDto(p.PublicId, byProduct[p.ProductId].Select(x => new SalesStockQtyDto(warehousePublic[x.WarehouseId], x.Qty)).ToList()))
            .ToList();
        var kits = components.Where(k => byId.ContainsKey(k.Key)).OrderBy(k => byId[k.Key].Sku, StringComparer.OrdinalIgnoreCase)
            .Select(k => new SalesStockKitDto(byId[k.Key].PublicId, k.Value.Where(c => byId.ContainsKey(c.ComponentProductId))
                .Select(c => new SalesStockKitComponentDto(byId[c.ComponentProductId].PublicId, byId[c.ComponentProductId].Sku, byId[c.ComponentProductId].Name, c.Item2)).ToList()))
            .ToList();
        return new SalesStockDto(warehouseDtos, products, kits);
    }
}
