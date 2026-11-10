using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 230 — sustitución de piezas de un kit en una orden de venta: misma categoría exacta y mismo precio de venta, misma cantidad, la marca «no sustituible» del
/// kit, pieza sin categoría, kit fijo y ajustable, kit de una oferta, la reserva y el despacho con la pieza real y el precio de la línea sin cambio.
/// Lote 231: solo el kit de OFERTA sustituye (KIT-1 es un kit de oferta puesto a la venta en una oferta publicada); un kit que no lo es va con sus piezas.
/// </summary>
public sealed class KitSubstitutionTests
{
    private const int Gloves = 601, Nitrile = 602, Vinyl = 603;

    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, KitService kits, WarehouseBin bin,
        Dictionary<string, Product> p)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public KitService Kits { get; } = kits;
        public WarehouseBin Bin { get; } = bin;
        public Product this[string sku] => p[sku];
    }

    /// <summary>
    /// KIT-1 (precio 50) = 2 × GM (guante M, Nitrilo, 10) + 1 × CAJA (sin categoría, 5). Candidatos: GL (Nitrilo, 10) válido; GXL (Nitrilo, 11) otro precio;
    /// GV (Vinilo, 10) otra categoría; GS (Nitrilo, 10) inactivo; CAJA2 (sin categoría, 5). Existencia de GM, GL y CAJA.
    /// </summary>
    private static async Task<World> NewWorldAsync(bool onSale = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<KitService>();
            s.AddSingleton<SyncService>();
            s.AddSingleton<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());
        });
        f.SetModules(ModuleKeys.SalesOrders);
        f.Db.Set<ProductCategory>().AddRange(
            new ProductCategory { ProductCategoryId = Gloves, TenantId = WmsFixture.TenantId, Name = "Guantes" },
            new ProductCategory { ProductCategoryId = Nitrile, TenantId = WmsFixture.TenantId, Name = "Nitrilo", ParentId = Gloves },
            new ProductCategory { ProductCategoryId = Vinyl, TenantId = WmsFixture.TenantId, Name = "Vinilo", ParentId = Gloves });
        await f.Db.SaveChangesAsync();
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var products = new Dictionary<string, Product>();
        foreach (var (sku, category, price, active) in new (string, int?, decimal, bool)[]
                 {
                     ("KIT-1", null, 50m, true), ("GM", Nitrile, 10m, true), ("CAJA", null, 5m, true), ("GL", Nitrile, 10m, true), ("GXL", Nitrile, 11m, true),
                     ("GV", Vinyl, 10m, true), ("GS", Nitrile, 10m, false), ("CAJA2", null, 5m, true),
                 })
        {
            var p = await f.AddProductAsync(sku, isActive: active, purchaseCost: 2m);
            var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
            (row.SalePrice, row.ProductCategoryId) = (price, category);
            products[sku] = row;
        }
        foreach (var (sku, qty) in new[] { ("GM", 10m), ("GL", 10m), ("CAJA", 10m) })
            f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = products[sku].ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = qty, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var kits = f.Get<KitService>();
        await kits.SetAsync(products["KIT-1"].PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(products["GM"].PublicId, 2m), new KitComponentRequest(products["CAJA"].PublicId, 1m) }), default);
        await OfferKitTestData.MakeOfferKitAsync(f, products["KIT-1"].ProductId, onSale ? 50m : null);   // Lote 231: kit de oferta (y a la venta)
        return new World(f, w, c, f.Get<SalesOrderService>(), kits, bin, products);
    }

    private static KitComponentRequest Piece(World x, string sku, decimal qty, string? substitutes = null)
        => new(x[sku].PublicId, qty, SubstitutesProductPublicId: substitutes is null ? null : x[substitutes].PublicId);

    private static Task<SalesOrderDto> DraftAsync(World x, decimal kits, params KitComponentRequest[] components)
        => x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x["KIT-1"].PublicId, kits, Components: components.Length == 0 ? null : components) }), default);

    private static async Task<(int Status, string Message)> ErrorOf(Func<Task> act)
    {
        var ex = await Assert.ThrowsAnyAsync<TeikemException>(act);
        return (ex.StatusCode, ex.Message);
    }

    private static async Task<(decimal OnHand, decimal Reserved)> BalanceAsync(World x, string sku)
    {
        var b = await x.F.BalanceAsync(x[sku].ProductId, x.Bin.WarehouseBinId);
        return (b?.QtyOnHand ?? 0m, b?.QtyReserved ?? 0m);
    }

    [Fact]
    public async Task The_substitutes_of_a_piece_are_the_products_of_the_same_category_and_price()
    {
        var x = await NewWorldAsync(onSale: false);   // sin usar todavía: su composición se puede cambiar
        await using var _ = x.F;
        var subs = await x.Svc.SubstitutesAsync(x["KIT-1"].PublicId, default);
        Assert.Equal(new[] { "GM", "CAJA" }, subs.Components.Select(c => c.Sku).ToArray());
        var gm = subs.Components[0];
        Assert.Equal(("Guantes / Nitrilo", 2m, false), (gm.CategoryPath, gm.Quantity, gm.NotSubstitutable));
        Assert.Equal(new[] { "GL" }, gm.Substitutes.Select(s => s.Sku).ToArray());   // ni otro precio (GXL), ni otra categoría (GV), ni inactivo (GS), ni la pieza misma
        Assert.Empty(subs.Components[1].Substitutes);   // sin categoría no se sustituye

        // la pieza marcada «no sustituible» no trae sustitutos (y la marca se ve en el editor del kit)
        var kit = await x.Kits.SetAsync(x["KIT-1"].PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(x["GM"].PublicId, 2m, NotSubstitutable: true), new KitComponentRequest(x["CAJA"].PublicId, 1m) }), default);
        Assert.Equal(new[] { true, false }, kit.Components.Select(c => c.NotSubstitutable).ToArray());
        var again = await x.Svc.SubstitutesAsync(x["KIT-1"].PublicId, default);
        Assert.True(again.Components[0].NotSubstitutable);
        Assert.Empty(again.Components[0].Substitutes);
        // sin mandar la marca, se conserva
        kit = await x.Kits.SetAsync(x["KIT-1"].PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(x["GM"].PublicId, 3m), new KitComponentRequest(x["CAJA"].PublicId, 1m) }), default);
        Assert.True(kit.Components[0].NotSubstitutable);

        // un producto que no es kit: 422
        var (status, message) = await ErrorOf(() => x.Svc.SubstitutesAsync(x["GM"].PublicId, default));
        Assert.Equal((422, KitRules.NotAKit("GM")), (status, message));
    }

    [Fact]
    public async Task A_piece_is_substituted_the_line_keeps_its_price_and_shows_the_original()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var d = await DraftAsync(x, 3m, Piece(x, "GL", 2m, substitutes: "GM"), Piece(x, "CAJA", 1m));
        var line = Assert.Single(d.Lines);
        Assert.Equal((50m, 150m), (line.UnitPrice, line.LineSubtotal));   // el precio del kit no cambia
        var gl = line.Components![0];
        Assert.Equal(("GL", 2m, 6m, "GM", x["GM"].PublicId), (gl.Sku, gl.QtyPerKit, gl.Needed, gl.SubstitutesSku, gl.SubstitutesProductPublicId!.Value));
        Assert.Null(line.Components[1].SubstitutesSku);
        var saved = await x.F.Db.Set<SalesOrderLineComponent>().AsNoTracking().OrderBy(c => c.SalesOrderLineComponentId).ToListAsync();
        Assert.Equal(new[] { (x["GL"].ProductId, (int?)x["GM"].ProductId), (x["CAJA"].ProductId, (int?)null) }, saved.Select(c => (c.ComponentProductId, c.SubstitutedProductId)).ToArray());

        // guardar de nuevo el borrador sin piezas conserva la sustitución
        var again = await x.Svc.UpdateAsync(d.PublicId, new SalesOrderRequest(Lines: new[] { new SalesOrderLineRequest(x["KIT-1"].PublicId, 4m) }), default);
        Assert.Equal(("GL", "GM", 8m), (again.Lines[0].Components![0].Sku, again.Lines[0].Components![0].SubstitutesSku, again.Lines[0].Components![0].Needed));
        Assert.Equal(200m, again.Lines[0].LineSubtotal);
    }

    [Fact]
    public async Task The_reservation_and_the_dispatch_use_the_real_piece()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var d = await DraftAsync(x, 3m, Piece(x, "GL", 2m, substitutes: "GM"), Piece(x, "CAJA", 1m));
        var o = await x.Svc.RequestAsync(d.PublicId, null, default);
        Assert.Equal((10m, 6m), await BalanceAsync(x, "GL"));
        Assert.Equal((10m, 0m), await BalanceAsync(x, "GM"));   // la pieza original no se toca
        Assert.Equal((10m, 3m), await BalanceAsync(x, "CAJA"));
        Assert.Equal(new[] { 6m, 3m }, o.Lines[0].Components!.Select(c => c.Reserved).ToArray());

        var sent = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 3m) }), default);
        Assert.Equal("SHIPPED", sent.Order.Status);
        Assert.Equal((4m, 0m), await BalanceAsync(x, "GL"));
        Assert.Equal((10m, 0m), await BalanceAsync(x, "GM"));
        var pickLines = await x.F.Db.Set<PickBatchLine>().AsNoTracking().ToListAsync();
        Assert.Contains(pickLines, l => l.ProductId == x["GL"].ProductId);
        Assert.DoesNotContain(pickLines, l => l.ProductId == x["GM"].ProductId);
    }

    [Fact]
    public async Task The_rule_rejects_another_price_another_category_another_quantity_no_category_and_a_non_substitutable_piece()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal((400, KitSubstitutionRules.PriceDiffers("GXL", "GM")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GXL", 2m, "GM"), Piece(x, "CAJA", 1m))));
        Assert.Equal((400, KitSubstitutionRules.CategoryDiffers("GV", "GM")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GV", 2m, "GM"), Piece(x, "CAJA", 1m))));
        Assert.Equal((400, KitSubstitutionRules.QuantityDiffers("GM", 2m)), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 3m, "GM"), Piece(x, "CAJA", 1m))));
        Assert.Equal((400, KitSubstitutionRules.NoCategory("CAJA")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GM", 2m), Piece(x, "CAJA2", 1m, "CAJA"))));
        Assert.Equal((400, KitSubstitutionRules.NotAPiece("GV", "KIT-1")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 2m, "GV"), Piece(x, "CAJA", 1m))));
        Assert.Equal((400, KitSubstitutionRules.SubstituteIsAnotherPiece("CAJA")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "CAJA", 2m, "GM"))));
        Assert.Equal((400, KitRules.ComponentInactive("GS")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GS", 2m, "GM"), Piece(x, "CAJA", 1m))));
        // en un kit fijo, fuera de la sustitución, las piezas son las del catálogo
        Assert.Equal((400, SalesRules.KitNotAdjustable("KIT-1")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 2m, "GM"), Piece(x, "CAJA", 2m))));
        Assert.Equal((400, SalesRules.KitNotAdjustable("KIT-1")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 2m, "GM"))));

        await OfferKitTestData.SetPieceNotSubstitutableAsync(x.F, x["KIT-1"].ProductId, x["GM"].ProductId, true);   // ya está en una oferta: se prepara en los datos
        Assert.Equal((400, KitSubstitutionRules.NotSubstitutable("GM")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 2m, "GM"), Piece(x, "CAJA", 1m))));
        Assert.Empty(await x.F.Db.Set<SalesOrder>().AsNoTracking().ToListAsync());   // nada se guardó
    }

    [Fact]
    public void The_pure_rule_checks_owner_kit_and_missing_price()
    {
        var piece = new SubstitutionProduct(1, "GM", Nitrile, 10m, true, false, null);
        Assert.Null(KitSubstitutionRules.Check(piece, false, new SubstitutionProduct(2, "GL", Nitrile, 10m, true, false, null), null));
        Assert.Equal(KitSubstitutionRules.SubstituteOtherOwner("GL"), KitSubstitutionRules.Check(piece, false, new SubstitutionProduct(2, "GL", Nitrile, 10m, true, false, 7), null));
        Assert.Equal(KitSubstitutionRules.SubstituteIsKit("K2"), KitSubstitutionRules.Check(piece, false, new SubstitutionProduct(2, "K2", Nitrile, 10m, true, true, null), null));
        Assert.Equal(KitSubstitutionRules.NoPrice("GM"), KitSubstitutionRules.Check(piece with { SalePrice = null }, false, new SubstitutionProduct(2, "GL", Nitrile, null, true, false, null), null));
        Assert.Equal(KitSubstitutionRules.NoCategory("GM"), KitSubstitutionRules.Check(piece with { CategoryId = null }, false, new SubstitutionProduct(2, "GL", null, 10m, true, false, null), null));
        Assert.False(KitSubstitutionRules.IsValidSubstitute(piece, false, piece, null));   // la pieza misma no es sustituto
    }

    [Fact]
    public async Task An_adjustable_kit_can_substitute_and_adjust_and_without_substitution_still_works_as_before()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OfferKitTestData.MakeOfferKitAsync(x.F, x["KIT-1"].ProductId, null, adjustable: true);
        var d = await DraftAsync(x, 1m, Piece(x, "GL", 3m, "GM"), Piece(x, "CAJA", 2m));   // ajustable: la cantidad es la que pone el vendedor
        Assert.Equal(new[] { ("GL", 3m, "GM"), ("CAJA", 2m, (string?)null) }, d.Lines[0].Components!.Select(c => (c.Sku, c.QtyPerKit, c.SubstitutesSku)).ToArray());
        Assert.Equal(50m, d.Lines[0].LineSubtotal);
        // la regla de la sustitución sigue valiendo
        Assert.Equal((400, KitSubstitutionRules.PriceDiffers("GXL", "GM")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GXL", 2m, "GM"), Piece(x, "CAJA", 1m))));
        // ajustar sin sustituir, como siempre
        var plain = await DraftAsync(x, 1m, Piece(x, "GM", 1m), Piece(x, "CAJA", 4m));
        Assert.All(plain.Lines[0].Components!, c => Assert.Null(c.SubstitutesSku));
    }

    [Fact]
    public async Task A_kit_that_is_not_an_offer_kit_does_not_substitute_nor_adjust_its_pieces()   // Lote 231 (p. ej. un kit armado o un kit viejo)
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(p => p.ProductId == x["KIT-1"].ProductId);
        (row.IsOfferKit, row.KitAdjustable) = (false, true);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var subs = await x.Svc.SubstitutesAsync(x["KIT-1"].PublicId, default);
        Assert.Equal((false, false), (subs.IsOfferKit, subs.Adjustable));
        Assert.Equal(new[] { "GM", "CAJA" }, subs.Components.Select(c => c.Sku).ToArray());
        Assert.All(subs.Components, c => Assert.Empty(c.Substitutes));
        Assert.Equal((400, OfferKitRules.PiecesFixed("KIT-1")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GL", 2m, "GM"), Piece(x, "CAJA", 1m))));
        Assert.Equal((400, OfferKitRules.PiecesFixed("KIT-1")), await ErrorOf(() => DraftAsync(x, 1m, Piece(x, "GM", 1m), Piece(x, "CAJA", 4m))));
        Assert.Equal(50m, (await DraftAsync(x, 1m)).Lines[0].UnitPrice);   // con sus piezas, sí
    }

    [Fact]
    public async Task The_device_sync_sends_the_real_piece()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var d = await DraftAsync(x, 1m, Piece(x, "GL", 2m, "GM"), Piece(x, "CAJA", 1m));
        await x.Svc.RequestAsync(d.PublicId, null, default);
        var page = await x.F.Get<SyncService>().SalesOrdersAsync(new SyncQuery(WarehousePublicId: x.W.PublicId), default);
        var line = Assert.Single(Assert.Single(page.Items).Lines);
        Assert.Equal(new[] { "GL", "CAJA" }, line.Components!.Select(c => c.Sku).ToArray());
    }
}
