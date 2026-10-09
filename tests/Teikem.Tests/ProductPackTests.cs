using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-09 — Empaque del producto (opcional, UNO por producto): unidad de empaque del catálogo UnitOfMeasure (Caja, Barril…) y cuántas unidades BASE
/// trae. Ambos o ninguno; cantidad &gt; 0 (hasta 3 decimales); distinto de la unidad base; se puede cambiar o quitar siempre (el inventario no se toca).
/// </summary>
public sealed class ProductPackTests
{
    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<ProductService>());

    [Fact]
    public void Pure_rules_require_both_or_none_and_a_positive_quantity()
    {
        Assert.Empty(ProductRules.ValidatePack(false, null, false));
        Assert.Equal(("packUom", ProductRules.PackQtyWithoutUnit), Assert.Single(ProductRules.ValidatePack(false, 12m, false)));
        Assert.Equal(("packQty", ProductRules.PackUnitWithoutQty), Assert.Single(ProductRules.ValidatePack(true, null, false)));
        Assert.Equal(("packQty", ProductRules.PackQtyInvalid), Assert.Single(ProductRules.ValidatePack(true, 0m, false)));
        Assert.Equal(("packQty", ProductRules.PackQtyInvalid), Assert.Single(ProductRules.ValidatePack(true, -1m, false)));
        Assert.Equal(("packQty", ProductRules.PackQtyInvalid), Assert.Single(ProductRules.ValidatePack(true, 1.2345m, false)));
        Assert.Equal(("packUom", ProductRules.PackSameAsBase), Assert.Single(ProductRules.ValidatePack(true, 12m, true)));
        Assert.Empty(ProductRules.ValidatePack(true, 12.5m, false));
    }

    [Fact]
    public async Task Pack_is_saved_on_create_shown_changed_and_cleared()
    {
        await using var f = await CreateAsync();
        var products = f.Get<ProductService>();

        var plain = await products.CreateAsync(new ProductCreateRequest("P-0", "Sin empaque"), default);
        Assert.Null(plain.Product.PackUomCode);
        Assert.Null(plain.Product.PackQty);

        var created = await products.CreateAsync(new ProductCreateRequest("P-1", "Mascarillas", PackUom: " box ", PackQty: 12m), default);
        Assert.Equal(("BOX", 12m), (created.Product.PackUomCode, created.Product.PackQty));
        Assert.Equal("BOX", created.Product.PackUomName);   // el nombre es el del catálogo (en la prueba, su código)

        // cambiar solo la cantidad conserva la unidad; cambiar la unidad conserva la cantidad
        var qtyOnly = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(PackQty: 24m), default);
        Assert.Equal(("BOX", 24m), (qtyOnly.Product.PackUomCode, qtyOnly.Product.PackQty));
        var unitOnly = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(PackUom: "BARREL"), default);
        Assert.Equal(("BARREL", 24m), (unitOnly.Product.PackUomCode, unitOnly.Product.PackQty));

        // un PATCH que no menciona el empaque no lo toca
        var renamed = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(Name: "Mascarillas N95"), default);
        Assert.Equal(("BARREL", 24m), (renamed.Product.PackUomCode, renamed.Product.PackQty));

        var cleared = await products.UpdateAsync(created.Product.PublicId, new ProductPatchRequest(ClearPack: true), default);
        Assert.Null(cleared.Product.PackUomCode);
        Assert.Null(cleared.Product.PackQty);
    }

    [Fact]
    public async Task Invalid_pack_is_rejected_with_the_field_message()
    {
        await using var f = await CreateAsync();
        var products = f.Get<ProductService>();

        var qtyOnly = await Assert.ThrowsAsync<ValidationException>(() => products.CreateAsync(new ProductCreateRequest("P-2", "x", PackQty: 12m), default));
        Assert.Equal(ProductRules.PackQtyWithoutUnit, Assert.Single(qtyOnly.Errors!["packUom"]));
        var unitOnly = await Assert.ThrowsAsync<ValidationException>(() => products.CreateAsync(new ProductCreateRequest("P-3", "x", PackUom: "BOX"), default));
        Assert.Equal(ProductRules.PackUnitWithoutQty, Assert.Single(unitOnly.Errors!["packQty"]));
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => products.CreateAsync(new ProductCreateRequest("P-4", "x", PackUom: "NOPE", PackQty: 3m), default));
        Assert.Equal(ProductRules.UnknownPackUom("NOPE"), Assert.Single(unknown.Errors!["packUom"]));
        var same = await Assert.ThrowsAsync<ValidationException>(() => products.CreateAsync(new ProductCreateRequest("P-5", "x", PackUom: "UN", PackQty: 3m), default));
        Assert.Equal(ProductRules.PackSameAsBase, Assert.Single(same.Errors!["packUom"]));

        var p = await products.CreateAsync(new ProductCreateRequest("P-6", "ok"), default);
        var patchQtyOnly = await Assert.ThrowsAsync<ValidationException>(() => products.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(PackQty: 5m), default));
        Assert.Equal(ProductRules.PackQtyWithoutUnit, Assert.Single(patchQtyOnly.Errors!["packUom"]));
    }

    [Fact]
    public async Task The_pack_can_be_changed_even_when_the_product_has_movements()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", Teikem.Domain.Constants.ZoneTypes.Picking), "A-01");
        var p = await f.AddProductAsync("P-7");
        await f.PostAsync(new Teikem.Infrastructure.Wms.InventoryPosting(Teikem.Domain.Constants.InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId));

        var changed = await f.Get<ProductService>().UpdateAsync(p.PublicId, new ProductPatchRequest(PackUom: "BOX", PackQty: 6m), default);
        Assert.Equal(("BOX", 6m), (changed.Product.PackUomCode, changed.Product.PackQty));
        Assert.True(changed.HasMovements);   // el empaque no es inmutable: el inventario siempre está en unidad base
    }
}
