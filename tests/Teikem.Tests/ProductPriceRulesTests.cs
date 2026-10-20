using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Precio del producto al guardar un aviso de llegada: lo toma si no tenía costo (primera línea gana), solo cambia con «actualizar» y nunca si es igual.</summary>
public sealed class ProductPriceRulesTests
{
    [Fact]
    public void Empty_cost_is_null_or_zero()
    {
        Assert.True(ProductPriceRules.IsEmpty(null));
        Assert.True(ProductPriceRules.IsEmpty(0m));
        Assert.False(ProductPriceRules.IsEmpty(0.0001m));
    }

    [Fact]
    public void Differs_only_when_both_have_value_and_are_not_equal()
    {
        Assert.True(ProductPriceRules.Differs(8m, 5m));
        Assert.False(ProductPriceRules.Differs(5m, 5m));
        Assert.False(ProductPriceRules.Differs(8m, 0m));
        Assert.False(ProductPriceRules.Differs(8m, null));
        Assert.False(ProductPriceRules.Differs(null, 5m));
    }

    [Fact]
    public void Product_without_cost_takes_the_first_line_price()
    {
        var plan = ProductPriceRules.Plan(new[] { new ProductPriceLine(1, 4m, false, null), new ProductPriceLine(1, 9m, false, null), new ProductPriceLine(2, 3m, false, 0m) });
        Assert.Equal(4m, plan[1]);
        Assert.Equal(3m, plan[2]);
    }

    [Fact]
    public void Existing_cost_changes_only_when_asked_and_different()
    {
        Assert.Empty(ProductPriceRules.Plan(new[] { new ProductPriceLine(1, 9m, false, 5m) }));
        Assert.Empty(ProductPriceRules.Plan(new[] { new ProductPriceLine(1, 5m, true, 5m) }));
        Assert.Equal(9m, ProductPriceRules.Plan(new[] { new ProductPriceLine(1, 9m, true, 5m) })[1]);
    }

    [Fact]
    public void Lines_without_price_or_with_zero_change_nothing()
    {
        Assert.Empty(ProductPriceRules.Plan(new[] { new ProductPriceLine(1, null, true, null), new ProductPriceLine(2, 0m, true, 3m) }));
    }

    [Fact]
    public async Task Saving_an_asn_fills_a_product_without_cost_and_leaves_an_existing_cost_alone()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var product = await f.Db.Set<Product>().SingleAsync(p => p.PublicId == f.ProductClientPublicId);
        product.PurchaseCost = null;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 2m, UnitPrice: 7.5m) }), default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(7.5m, (await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == f.ProductClientPublicId)).PurchaseCost);

        // ya tiene costo: otro aviso con otro precio no lo cambia
        await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 2m, UnitPrice: 11m) }), default);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(7.5m, (await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == f.ProductClientPublicId)).PurchaseCost);
    }
}
