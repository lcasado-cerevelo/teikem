using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 236 — el disponible de un kit sale DESGLOSADO por pieza (docs/lote236-decisiones.md): GET /sales-orders/stock trae, además de lo disponible por producto y
/// almacén, los kits que se arman al pedir con sus piezas (SKU, nombre y cantidad por kit, en el orden del kit), aunque no haya existencia; un producto con
/// existencia propia que no es de tipo KIT (como el kit armado) no viaja en Kits. La cuenta por pieza la hace la pantalla con el disponible de Products.
/// </summary>
public sealed class Lote236Tests
{
    private static SalesStockService Stock(PriceWorld x) => new(x.F.Db, x.F.Get<ILookupCache>());

    private static decimal Available(SalesStockDto dto, Guid productPublicId, Warehouse w)
        => dto.Products.SingleOrDefault(x => x.ProductPublicId == productPublicId)?.Stock.SingleOrDefault(s => s.WarehousePublicId == w.PublicId)?.Available ?? 0m;

    [Fact]
    public async Task The_stock_brings_each_offer_kit_with_its_pieces_and_each_piece_keeps_its_own_available()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY", stock: 100m);
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY", stock: 10m);
        var kit = await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1",
            new[] { new KitComponentRequest(strips.PublicId, 6m), new KitComponentRequest(monitor.PublicId, 1m) }, Brand: "PRODIGY"), default);

        var dto = await Stock(x).GetAsync(default);

        var row = Assert.Single(dto.Kits, k => k.KitPublicId == kit.ProductPublicId);
        Assert.Equal(new[] { (strips.PublicId, "52800", 6m), (monitor.PublicId, "070120", 1m) }, row.Components.Select(c => (c.ProductPublicId, c.Sku, c.QtyPerKit)).ToArray());
        Assert.All(row.Components, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        // el kit: 100 ÷ 6 = 16, pero solo 10 monitores (la pieza que limita); cada pieza con su disponible para el desglose
        Assert.Equal(10m, Available(dto, kit.ProductPublicId, x.W));
        Assert.Equal(100m, Available(dto, strips.PublicId, x.W));
        Assert.Equal(10m, Available(dto, monitor.PublicId, x.W));
        // las piezas no son kits
        Assert.DoesNotContain(dto.Kits, k => k.KitPublicId == strips.PublicId || k.KitPublicId == monitor.PublicId);
    }

    [Fact]
    public async Task A_kit_without_any_stock_still_travels_with_its_pieces()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY");
        var kit = await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1",
            new[] { new KitComponentRequest(strips.PublicId, 6m), new KitComponentRequest(monitor.PublicId, 1m) }, Brand: "PRODIGY"), default);

        var dto = await Stock(x).GetAsync(default);

        Assert.DoesNotContain(dto.Products, p => p.ProductPublicId == kit.ProductPublicId);   // sin disponible: 0 (no viaja)
        var row = Assert.Single(dto.Kits, k => k.KitPublicId == kit.ProductPublicId);
        Assert.Equal(2, row.Components.Count);
    }

    [Fact]
    public async Task A_product_with_its_own_stock_that_is_not_a_built_on_order_kit_is_not_broken_down()
    {
        await using var x = await PriceWorld.CreateAsync();
        var piece = await x.ProductAsync("PIEZA", 2m, "PRODIGY", stock: 50m);
        // como el kit ARMADO: tiene piezas en su receta, pero su tipo no es KIT y lleva su propia existencia
        var assembled = await x.ProductAsync("ARMADO", 20m, "PRODIGY", stock: 7m);
        x.F.Db.Set<ProductKitComponent>().Add(new ProductKitComponent { TenantId = WmsFixture.TenantId, KitProductId = assembled.ProductId, ComponentProductId = piece.ProductId, Quantity = 4m, SortOrder = 1 });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var dto = await Stock(x).GetAsync(default);

        Assert.DoesNotContain(dto.Kits, k => k.KitPublicId == assembled.PublicId);
        Assert.Equal(7m, Available(dto, assembled.PublicId, x.W));   // su existencia, como cualquier producto
    }
}
