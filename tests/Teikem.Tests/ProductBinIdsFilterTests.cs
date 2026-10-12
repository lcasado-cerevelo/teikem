using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-12 — filtro «Posición» de Productos e inventario como desplegable de selección múltiple: <c>BinIds</c> deja los productos con
/// existencia en alguna de las posiciones elegidas, y las cantidades de la fila son solo las de ESAS posiciones. Se combina con el texto
/// (<c>BinSearch</c>) y sin ninguno no filtra.
/// </summary>
public class ProductBinIdsFilterTests
{
    [Fact]
    public async Task Bin_ids_keep_products_stocked_in_those_bins_and_count_only_their_quantities()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton<ProductService>());
        var w = await f.AddWarehouseAsync("W1");
        var z = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var a = await f.AddBinAsync(z, "A-01");
        var b = await f.AddBinAsync(z, "A-02");
        var c = await f.AddBinAsync(z, "B-01");
        var p1 = await f.AddProductAsync("P1");
        var p2 = await f.AddProductAsync("P2");
        var p3 = await f.AddProductAsync("P3");
        InventoryPosting In(int productId, int binId, decimal qty)
            => new(InventoryTxnTypes.Receipt, productId, qty, ToWarehouseId: w.WarehouseId, ToBinId: binId);
        await f.PostAsync(In(p1.ProductId, a.WarehouseBinId, 5m), In(p1.ProductId, c.WarehouseBinId, 3m), In(p2.ProductId, b.WarehouseBinId, 2m), In(p3.ProductId, c.WarehouseBinId, 9m));
        var products = f.Get<ProductService>();

        var all = await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default);
        Assert.Equal(3, all.Total);

        var inAB = await products.ListAsync(new ProductListQuery(BinIds: new[] { a.WarehouseBinId, b.WarehouseBinId }), InventoryScope.Any, default);
        Assert.Equal(new[] { "P1", "P2" }, inAB.Items.Select(i => i.Sku).ToArray());
        Assert.Equal(2, inAB.Total);
        Assert.Equal(5m, inAB.Items.Single(i => i.Sku == "P1").QtyOnHand);   // solo lo de A-01, no los 3 de B-01

        var inC = await products.ListAsync(new ProductListQuery(BinIds: new[] { c.WarehouseBinId }), InventoryScope.Any, default);
        Assert.Equal(new[] { "P1", "P3" }, inC.Items.Select(i => i.Sku).ToArray());

        // texto y ids juntos: deben cumplir los dos
        var both = await products.ListAsync(new ProductListQuery(BinSearch: "b-", BinIds: new[] { a.WarehouseBinId, c.WarehouseBinId }), InventoryScope.Any, default);
        Assert.Equal(new[] { "P1", "P3" }, both.Items.Select(i => i.Sku).ToArray());

        var empty = await products.ListAsync(new ProductListQuery(BinIds: Array.Empty<int>()), InventoryScope.Any, default);
        Assert.Equal(3, empty.Total);
    }
}
