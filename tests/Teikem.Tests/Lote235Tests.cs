using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 235 — almacén de la orden y disponible al escoger productos (docs/lote235-decisiones.md): el almacén principal es uno solo por compañía (marcar uno
/// desmarca el otro; sin marcar = el activo más antiguo), la orden sin almacén indicado toma el principal, y GET /sales-orders/stock da lo disponible para vender
/// por producto y almacén (sin cuarentena, sin lo reservado, sin posiciones inactivas; un kit de oferta = kits completos que se arman con sus piezas).
/// </summary>
public sealed class Lote235Tests
{
    private static WarehouseService Warehouses(PriceWorld x)
        => new(x.F.Db, x.F.Get<ITenantContext>(), x.F.Get<ILookupCache>(), x.F.Get<StatusService>());

    private static SalesStockService Stock(PriceWorld x) => new(x.F.Db, x.F.Get<ILookupCache>());

    private static async Task AddStockAsync(PriceWorld x, Product p, WarehouseBin bin, decimal onHand, decimal reserved = 0m)
    {
        x.F.Db.StockBalances.Add(new StockBalance
        {
            TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = bin.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = onHand,
            QtyReserved = reserved, UpdatedAtUtc = DateTime.UtcNow,
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static decimal Available(SalesStockDto dto, Product p, Warehouse w)
        => dto.Products.SingleOrDefault(x => x.ProductPublicId == p.PublicId)?.Stock.SingleOrDefault(s => s.WarehousePublicId == w.PublicId)?.Available ?? 0m;

    // ---------------------------------------------------------------- reglas puras

    [Fact]
    public void The_main_warehouse_is_the_marked_one_or_the_oldest_active()
    {
        Assert.Null(WarehouseRules.MainWarehouseId(Array.Empty<(int, bool, bool)>()));
        Assert.Equal(3, WarehouseRules.MainWarehouseId(new[] { (5, false, true), (3, false, true), (9, false, true) }));   // ninguno marcado: el más antiguo
        Assert.Equal(9, WarehouseRules.MainWarehouseId(new[] { (5, false, true), (3, false, true), (9, true, true) }));    // el marcado
        Assert.Equal(5, WarehouseRules.MainWarehouseId(new[] { (5, false, true), (3, true, false), (9, false, true) }));   // el marcado está de baja: no cuenta
        Assert.Null(WarehouseRules.MainWarehouseId(new[] { (3, true, false) }));
    }

    [Fact]
    public void The_available_rules_count_whole_kits_and_word_the_backorder_notice()
    {
        var sellable = new Dictionary<int, decimal> { [1] = 13m, [2] = 1m };
        Assert.Equal(1m, SalesStockRules.KitsAvailable(new[] { (1, 6m), (2, 1m) }, sellable));   // 13 ÷ 6 = 2, pero solo hay 1 de la otra pieza
        Assert.Equal(2m, SalesStockRules.KitsAvailable(new[] { (1, 6m) }, sellable));
        Assert.Equal(0m, SalesStockRules.KitsAvailable(new[] { (1, 6m), (3, 1m) }, sellable));   // una pieza sin disponible
        Assert.Equal(0m, SalesStockRules.KitsAvailable(Array.Empty<(int, decimal)>(), sellable));

        // pedir más de lo disponible NO se bloquea: se marca lo que queda en backorder
        Assert.Equal(0m, SalesStockRules.Backorder(5m, 5m));
        Assert.Equal(3m, SalesStockRules.Backorder(8m, 5m));
        Assert.Equal(1m, SalesStockRules.Backorder(1m, -2m));
        Assert.Equal("Solo hay 5 disponibles en Almacén W1; 3 quedan en backorder.", SalesStockRules.BackorderMessage(5m, 3m, "Almacén W1"));
        Assert.Equal("Solo hay 2.5 disponibles en Almacén W1; 1 queda en backorder.", SalesStockRules.BackorderMessage(2.5m, 1m, "Almacén W1"));
        Assert.Equal("Solo hay 1 disponible en Almacén W1; 2 quedan en backorder.", SalesStockRules.BackorderMessage(1m, 2m, "Almacén W1"));
        Assert.Equal("No hay disponible en Almacén W1; 4 quedan en backorder.", SalesStockRules.BackorderMessage(0m, 4m, "Almacén W1"));
    }

    // ---------------------------------------------------------------- almacén principal: uno solo

    [Fact]
    public async Task Only_one_warehouse_is_the_main_one_and_marking_another_unmarks_the_previous()
    {
        await using var x = await PriceWorld.CreateAsync();
        var w2 = await x.F.AddWarehouseAsync("W2");
        var w3 = await x.F.AddWarehouseAsync("W3");
        var service = Warehouses(x);

        // ninguno marcado: el principal es el activo más antiguo (W1)
        var list = await service.ListAsync(false, default);
        Assert.Equal(new[] { ("W1", true), ("W2", false), ("W3", false) }, list.Select(w => (w.Code, w.IsMain)).ToArray());
        Assert.False(await x.F.Db.Warehouses.AsNoTracking().AnyAsync(w => w.IsMain));

        var marked = await service.UpdateAsync(w2.PublicId, new WarehousePatchRequest(IsMain: true), default);
        Assert.True(marked.Warehouse.IsMain);
        Assert.Equal(new[] { ("W1", false), ("W2", true), ("W3", false) }, (await service.ListAsync(false, default)).Select(w => (w.Code, w.IsMain)).ToArray());

        // marcar otro desmarca el anterior: en la base queda uno solo
        await service.UpdateAsync(w3.PublicId, new WarehousePatchRequest(IsMain: true, Name: "Tres"), default);
        var rows = await x.F.Db.Warehouses.AsNoTracking().OrderBy(w => w.Code).Select(w => new { w.Code, w.IsMain, w.Name }).ToListAsync();
        Assert.Equal(new[] { ("W1", false), ("W2", false), ("W3", true) }, rows.Select(r => (r.Code, r.IsMain)).ToArray());
        Assert.Equal("Tres", rows[2].Name);   // los demás campos del mismo PATCH también se guardan
        Assert.False((await service.GetAsync(w2.PublicId, default)).Warehouse.IsMain);

        // el principal no se «quita»: se cambia marcando otro
        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(w3.PublicId, new WarehousePatchRequest(IsMain: false), default));
        Assert.Equal(new[] { WarehouseRules.MainCannotBeUnset }, ex.Errors!["isMain"]);

        // un almacén dado de baja no se marca
        var off = await x.F.AddWarehouseAsync("W4", isActive: false);
        await Assert.ThrowsAsync<StatusRuleException>(() => service.UpdateAsync(off.PublicId, new WarehousePatchRequest(IsMain: true), default));
        Assert.True(await x.F.Db.Warehouses.AsNoTracking().AnyAsync(w => w.Code == "W3" && w.IsMain));   // nada quedó a medias
    }

    // ---------------------------------------------------------------- la orden sin almacén toma el principal

    [Fact]
    public async Task An_order_without_a_warehouse_takes_the_main_one()
    {
        await using var x = await PriceWorld.CreateAsync();
        var p = await x.ProductAsync("A1", 10m, stock: 5m);
        var w2 = await x.F.AddWarehouseAsync("W2");
        var line = new SalesOrderLineRequest(p.PublicId, 1m);

        // sin principal marcado: el activo más antiguo
        var first = await x.Sales.CreateAsync(new SalesOrderRequest(null, x.C1.PublicId, Lines: new[] { line }), default);
        Assert.Equal(x.W.PublicId, first.WarehousePublicId);

        await Warehouses(x).UpdateAsync(w2.PublicId, new WarehousePatchRequest(IsMain: true), default);
        var second = await x.Sales.CreateAsync(new SalesOrderRequest(null, x.C1.PublicId, Lines: new[] { line }), default);
        Assert.Equal(w2.PublicId, second.WarehousePublicId);

        // el que se indica gana al principal
        var third = await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C1.PublicId, Lines: new[] { line }), default);
        Assert.Equal(x.W.PublicId, third.WarehousePublicId);
    }

    // ---------------------------------------------------------------- disponible por almacén

    [Fact]
    public async Task The_stock_lists_what_can_be_sold_per_warehouse()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A1", 10m, stock: 100m);   // W1: 100
        var b = await x.ProductAsync("B1", 10m);                // sin existencia
        var w2 = await x.F.AddWarehouseAsync("W2");
        var pick2 = await x.F.AddBinAsync(await x.F.AddZoneAsync(w2, "PZ", ZoneTypes.Picking), "P-01");
        var off2 = await x.F.AddBinAsync(await x.F.AddZoneAsync(w2, "PX", ZoneTypes.Picking), "X-01", isActive: false);
        var quarantine2 = await x.F.AddBinAsync(await x.F.AddZoneAsync(w2, "QZ", ZoneTypes.Quarantine), "Q-01");
        await AddStockAsync(x, a, pick2, 7m, reserved: 2m);   // 5 disponibles
        await AddStockAsync(x, a, off2, 30m);                 // posición inactiva: no cuenta
        await AddStockAsync(x, a, quarantine2, 50m);          // cuarentena: no cuenta
        var closed = await x.F.AddWarehouseAsync("W9", isActive: false);
        var foreign = await x.F.AddProductAsync("CLI-1", ownerClientId: x.C1.ClientId);   // inventario de un cliente: no se vende
        await AddStockAsync(x, foreign, pick2, 9m);
        await Warehouses(x).UpdateAsync(w2.PublicId, new WarehousePatchRequest(IsMain: true), default);

        var dto = await Stock(x).GetAsync(default);
        Assert.Equal(new[] { ("W1", false), ("W2", true) }, dto.Warehouses.Select(w => (w.Code, w.IsMain)).ToArray());   // solo activos, por código, con el principal
        Assert.DoesNotContain(dto.Warehouses, w => w.PublicId == closed.PublicId);
        Assert.Equal(100m, Available(dto, a, x.W));
        Assert.Equal(5m, Available(dto, a, w2));
        Assert.DoesNotContain(dto.Products, r => r.ProductPublicId == b.PublicId);         // sin disponible en ningún almacén: no viaja
        Assert.DoesNotContain(dto.Products, r => r.ProductPublicId == foreign.PublicId);

        // es la misma cuenta que usa Solicitar
        var sellable = await x.Sales.SellableAsync(w2.WarehouseId, new[] { a.ProductId }, default);
        Assert.Equal(sellable[a.ProductId], Available(dto, a, w2));
    }

    [Fact]
    public async Task An_offer_kit_shows_how_many_whole_kits_can_be_built_in_each_warehouse()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY", stock: 100m);
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY", stock: 10m);
        var kit = await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1",
            new[] { new KitComponentRequest(strips.PublicId, 6m), new KitComponentRequest(monitor.PublicId, 1m) }, Brand: "PRODIGY"), default);
        var kitRow = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == kit.ProductPublicId);
        var w2 = await x.F.AddWarehouseAsync("W2");
        var pick2 = await x.F.AddBinAsync(await x.F.AddZoneAsync(w2, "PZ", ZoneTypes.Picking), "P-01");
        await AddStockAsync(x, strips, pick2, 13m);
        await AddStockAsync(x, monitor, pick2, 1m);
        var w3 = await x.F.AddWarehouseAsync("W3");
        var pick3 = await x.F.AddBinAsync(await x.F.AddZoneAsync(w3, "PZ", ZoneTypes.Picking), "P-01");
        await AddStockAsync(x, strips, pick3, 60m);   // en W3 falta el monitor: no se arma ninguno

        var dto = await Stock(x).GetAsync(default);
        Assert.Equal(10m, Available(dto, kitRow, x.W));   // 100 ÷ 6 = 16, pero solo 10 monitores
        Assert.Equal(1m, Available(dto, kitRow, w2));     // 13 ÷ 6 = 2, pero solo 1 monitor
        Assert.Equal(0m, Available(dto, kitRow, w3));
        Assert.DoesNotContain(dto.Products.Single(p => p.ProductPublicId == kitRow.PublicId).Stock, s => s.WarehousePublicId == w3.PublicId);
        // las piezas siguen con su propio disponible
        Assert.Equal(13m, Available(dto, strips, w2));
    }
}
