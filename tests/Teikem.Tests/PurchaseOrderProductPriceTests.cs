using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Precio del producto al guardar una orden de compra: lo toma si no tenía costo, «actualizar» exige inventory.manage y un costo existente no cambia solo.</summary>
public sealed class PurchaseOrderProductPriceTests
{
    private static async Task<decimal?> CostAsync(WmsFixture f, Guid publicId)
    {
        f.Db.ChangeTracker.Clear();
        return (await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == publicId)).PurchaseCost;
    }

    private static async Task SetCostAsync(WmsFixture f, Guid publicId, decimal? cost)
    {
        var p = await f.Db.Set<Product>().SingleAsync(x => x.PublicId == publicId);
        p.PurchaseCost = cost;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Product_without_cost_takes_the_order_line_cost_and_an_existing_cost_is_left_alone()
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton<PurchaseOrderService>());
        var w = await f.AddWarehouseAsync("W1");
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Prov", IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        var a = await f.AddProductAsync("PA");
        var b = await f.AddProductAsync("PB");
        await SetCostAsync(f, a.PublicId, null);
        await SetCostAsync(f, b.PublicId, 5m);

        await f.Get<PurchaseOrderService>().CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(a.PublicId, 2m, 8m), new PurchaseOrderLineRequest(b.PublicId, 2m, 9m) }), default);

        Assert.Equal(8m, await CostAsync(f, a.PublicId));
        Assert.Equal(5m, await CostAsync(f, b.PublicId));
    }

    [Fact]
    public async Task Asking_to_update_the_product_price_changes_an_existing_cost()
    {
        await using var f = await WmsFixture.CreateAsync(s => s.AddSingleton<PurchaseOrderService>());
        var w = await f.AddWarehouseAsync("W1");
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Prov", IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        var a = await f.AddProductAsync("PA");
        await SetCostAsync(f, a.PublicId, 5m);

        await f.Get<PurchaseOrderService>().CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, w.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(a.PublicId, 2m, 9m, UpdateProductPrice: true) }), default);
        Assert.Equal(9m, await CostAsync(f, a.PublicId));
    }
}
