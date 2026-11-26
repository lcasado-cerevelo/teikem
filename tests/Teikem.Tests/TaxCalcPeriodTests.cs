using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 248 (dinero): la exención de producto por temporada en el cálculo de TaxService y en la orden de venta. IVU estatal 10.5 % + municipal 1 %; el período
// exime las líneas de PRODUCTO gravables o de tasa cero que cubre (motivo EXEMPT_PRODUCT_PERIOD, con su nombre), nunca los cargos, y gana a la exención del cliente.
public sealed class TaxCalcPeriodTests
{
    private sealed class World : IAsyncDisposable
    {
        public PriceWorld X { get; private init; } = null!;
        public TaxService Tax => X.F.Get<TaxService>();
        public TaxExemptionPeriodService Periods => new(X.F.Db, X.F.Tenant, TenantClock.Default);
        public DateOnly Today => X.Today;
        public const int Root = 7001, Notebooks = 7002, Pencils = 7003, Ruled = 7004;

        public static async Task<World> CreateAsync(bool includedPrices = false, string rounding = TaxRoundingModes.Line)
        {
            var x = await PriceWorld.CreateAsync();
            x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Tax);
            var db = x.F.Db;
            // categorías de impuesto (EXEMPT / ZERO_RATE) y el árbol de categorías de productos: Escolar → Libretas → Rayadas; Escolar → Lápices
            db.LookupCodes.Add(new LookupCode { LookupCodeId = 8801, Entity = TaxCodes.CategoryDomain, InternalCode = TaxCategories.Exempt, LabelJson = "{\"es\":\"Exento\"}", IsActive = true });
            db.LookupCodes.Add(new LookupCode { LookupCodeId = 8802, Entity = TaxCodes.CategoryDomain, InternalCode = TaxCategories.ZeroRate, LabelJson = "{\"es\":\"Cero\"}", IsActive = true });
            db.Set<ProductCategory>().AddRange(
                new ProductCategory { ProductCategoryId = Root, TenantId = WmsFixture.TenantId, Name = "Escolar" },
                new ProductCategory { ProductCategoryId = Notebooks, TenantId = WmsFixture.TenantId, ParentId = Root, Name = "Libretas" },
                new ProductCategory { ProductCategoryId = Pencils, TenantId = WmsFixture.TenantId, ParentId = Root, Name = "Lápices" },
                new ProductCategory { ProductCategoryId = Ruled, TenantId = WmsFixture.TenantId, ParentId = Notebooks, Name = "Rayadas" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            x.F.Lookups.Load(await db.LookupCodes.AsNoTracking().ToListAsync());
            var w = new World { X = x };
            await w.Tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
                new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
            await w.Tax.UpdateSettingsAsync(new TaxSettingsRequest(includedPrices, rounding, "IVU", null, new[] { new TaxChargeRuleRequest("FREIGHT", true) }), default);
            return w;
        }

        public async Task<Product> ProductAsync(string sku, int? categoryId = null, string taxCategory = TaxCategories.Taxable, decimal price = 100m)
        {
            var p = await X.ProductAsync(sku, price);
            var row = await X.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
            row.ProductCategoryId = categoryId;
            row.TaxCategoryLookupId = taxCategory switch { TaxCategories.Exempt => 8801, TaxCategories.ZeroRate => 8802, _ => null };
            await X.F.Db.SaveChangesAsync();
            X.F.Db.ChangeTracker.Clear();
            return row;
        }

        public Task<TaxCalcDto> CalcAsync(DateOnly date, int? clientId, params TaxCalcLineRequest[] lines)
            => Tax.CalculateAsync(new TaxCalcRequest(clientId, date, lines), default);

        public ValueTask DisposeAsync() => X.DisposeAsync();
    }

    private static TaxCalcLineRequest Line(string key, Product p, decimal amount = 100m) => new(key, p.ProductId, null, amount);

    [Fact]
    public async Task A_selected_product_is_exempt_inside_the_period_including_both_edges_and_pays_the_day_after()
    {
        await using var w = await World.CreateAsync();
        var notebook = await w.ProductAsync("LIB-1");
        var other = await w.ProductAsync("OTRO-1");
        var from = w.Today.AddDays(10);
        var to = w.Today.AddDays(20);
        await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Regreso a clases", "OA 2026-15", from, to, "SELECTED", new[] { notebook.PublicId }), default);

        foreach (var day in new[] { from, to })
        {
            var r = await w.CalcAsync(day, null, Line("lib", notebook), Line("otro", other));
            var l = r.Lines[0];
            Assert.Equal((0m, TaxReasons.ExemptProductPeriod, true, "Regreso a clases", (string?)null), (l.TaxAmount, l.Reason, l.IsTaxExempt, l.ExemptionPeriod, l.ExemptionCertificate));
            Assert.Equal((11.5m, TaxReasons.Taxable, (string?)null), (r.Lines[1].TaxAmount, r.Lines[1].Reason, r.Lines[1].ExemptionPeriod));
            Assert.Equal(11.5m, r.TaxAmount);
        }
        var after = await w.CalcAsync(to.AddDays(1), null, Line("lib", notebook));
        Assert.Equal((11.5m, TaxReasons.Taxable, (string?)null), (after.TaxAmount, after.Lines[0].Reason, after.Lines[0].ExemptionPeriod));
        var before = await w.CalcAsync(from.AddDays(-1), null, Line("lib", notebook));
        Assert.Equal(11.5m, before.TaxAmount);
    }

    [Fact]
    public async Task A_category_covers_its_subcategories_not_its_siblings_and_a_removed_target_no_longer_exempts()
    {
        await using var w = await World.CreateAsync();
        var plain = await w.ProductAsync("LIB-1", World.Notebooks);
        var ruled = await w.ProductAsync("LIB-R", World.Ruled);
        var pencil = await w.ProductAsync("LAP-1", World.Pencils);
        var day = w.Today.AddDays(5);
        var p = await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Libretas", "OA", day, day.AddDays(3), "SELECTED", null, new[] { World.Notebooks }), default);

        var r = await w.CalcAsync(day, null, Line("a", plain), Line("b", ruled), Line("c", pencil));
        Assert.Equal(new[] { TaxReasons.ExemptProductPeriod, TaxReasons.ExemptProductPeriod, TaxReasons.Taxable }, r.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(11.5m, r.TaxAmount);

        // se cambia la selección (todavía está programado): Libretas queda con IsActive = 0 y ya no exime; Lápices sí
        await w.Periods.UpdateAsync(p.PublicId, new TaxExemptionPeriodPatchRequest(CategoryIds: new[] { World.Pencils }), default);
        var moved = await w.CalcAsync(day, null, Line("a", plain), Line("b", ruled), Line("c", pencil));
        Assert.Equal(new[] { TaxReasons.Taxable, TaxReasons.Taxable, TaxReasons.ExemptProductPeriod }, moved.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(23m, moved.TaxAmount);
        Assert.False((await w.X.F.Db.Set<TaxExemptionPeriodCategory>().AsNoTracking().SingleAsync(c => c.ProductCategoryId == World.Notebooks)).IsActive);
    }

    [Fact]
    public async Task All_products_never_touches_charges_exempt_keeps_its_reason_and_zero_rate_becomes_period()
    {
        await using var w = await World.CreateAsync();
        var taxable = await w.ProductAsync("G-1");
        var exempt = await w.ProductAsync("E-1", taxCategory: TaxCategories.Exempt);
        var zero = await w.ProductAsync("Z-1", taxCategory: TaxCategories.ZeroRate);
        var day = w.Today.AddDays(1);
        await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Todo", "Emergencia", day, day, "ALL"), default);

        var r = await w.CalcAsync(day, null, Line("g", taxable), Line("e", exempt), Line("z", zero), new TaxCalcLineRequest("flete", null, "FREIGHT", 50m),
            new TaxCalcLineRequest("envio", null, "PRODUCT_SALE", 10m));
        Assert.Equal(new[] { TaxReasons.ExemptProductPeriod, TaxReasons.ExemptProduct, TaxReasons.ExemptProductPeriod, TaxReasons.Taxable, TaxReasons.Taxable },
            r.Lines.Select(l => l.Reason).ToArray());
        Assert.Equal(new[] { "Todo", null, "Todo", null, null }, r.Lines.Select(l => l.ExemptionPeriod).ToArray());
        Assert.Equal(5.75m + 1.15m, r.TaxAmount);   // solo los cargos
    }

    [Fact]
    public async Task With_a_partial_city_exemption_the_period_wins_on_its_line_and_the_rest_pays_ten_and_a_half()
    {
        await using var w = await World.CreateAsync();
        var client = await w.X.F.AddClientAsync("CX");
        await TaxTestData.SeedDocumentExemptionAsync(w.Tax, w.X.F.Db, WmsFixture.TenantId, client.ClientId, "MUN-9", w.Today.AddDays(-30), w.Today.AddDays(300), "COMPONENTS", "CITY");
        var season = await w.ProductAsync("S-1");
        var normal = await w.ProductAsync("N-1");
        var day = w.Today.AddDays(2);
        await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Temporada", "OA", day, day.AddDays(1), "SELECTED", new[] { season.PublicId }), default);

        var r = await w.CalcAsync(day, client.ClientId, Line("s", season), Line("n", normal));
        Assert.Equal((0m, TaxReasons.ExemptProductPeriod, (string?)null), (r.Lines[0].TaxAmount, r.Lines[0].Reason, r.Lines[0].ExemptionCertificate));
        Assert.Equal((10.5m, TaxReasons.ExemptClientPartial, "MUN-9", 10.5m), (r.Lines[1].TaxAmount, r.Lines[1].Reason, r.Lines[1].ExemptionCertificate, r.Lines[1].TaxRatePct));
        Assert.Equal(10.5m, r.TaxAmount);
        Assert.Equal(r.TaxAmount, r.Components.Sum(c => c.Amount));
    }

    [Theory]
    [InlineData(TaxRoundingModes.Line, 0.08)]       // 0.0380 → 0.04 × 2
    [InlineData(TaxRoundingModes.Document, 0.08)]   // 0.0759 una sola vez
    public async Task Rounding_by_line_and_by_document_square_to_the_cent_with_a_period_line(string rounding, double expectedTax)
    {
        await using var w = await World.CreateAsync(rounding: rounding);
        var season = await w.ProductAsync("S-1");
        var a = await w.ProductAsync("A-1");
        var b = await w.ProductAsync("B-1");
        var day = w.Today.AddDays(3);
        await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Temporada", "OA", day, day, "SELECTED", new[] { season.PublicId }), default);
        var r = await w.CalcAsync(day, null, Line("s", season, 0.33m), Line("a", a, 0.33m), Line("b", b, 0.33m));
        Assert.Equal((decimal)expectedTax, r.TaxAmount);
        Assert.Equal(r.Subtotal + r.TaxAmount, r.Total);
        Assert.Equal(0.99m, r.Subtotal);
        Assert.Equal(r.TaxAmount, r.Components.Sum(c => c.Amount));
        Assert.Equal(0m, r.Lines[0].TaxAmount);
    }

    [Fact]
    public async Task An_inactive_period_or_one_of_another_company_does_not_apply()
    {
        await using var w = await World.CreateAsync();
        var p1 = await w.ProductAsync("P-1");
        var day = w.Today.AddDays(4);
        var period = await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Temporada", "OA", day, day, "ALL"), default);
        Assert.Equal(0m, (await w.CalcAsync(day, null, Line("p", p1))).TaxAmount);
        await w.Periods.SetActiveAsync(period.PublicId, false, default);
        Assert.Equal(11.5m, (await w.CalcAsync(day, null, Line("p", p1))).TaxAmount);

        // un período de TODO de la otra compañía no toca a ésta
        using (w.X.F.AsTenant(WmsFixture.OtherTenantId))
            await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Ajeno", "OA", day, day, "ALL"), default);
        Assert.Equal(11.5m, (await w.CalcAsync(day, null, Line("p", p1))).TaxAmount);
    }

    [Fact]
    public async Task The_sales_order_copies_the_exempt_line_without_rate()
    {
        await using var w = await World.CreateAsync();
        var season = await w.ProductAsync("S-1");
        var normal = await w.ProductAsync("N-1");
        await w.Periods.CreateAsync(new TaxExemptionPeriodRequest("Hoy", "OA", w.Today, w.Today.AddDays(5), "SELECTED", new[] { season.PublicId }), default);
        var o = await w.X.DraftAsync(w.X.C1, new SalesOrderLineRequest(season.PublicId, 1m), new SalesOrderLineRequest(normal.PublicId, 1m));
        Assert.Equal(11.5m, o.TaxAmount);
        var orderId = (await w.X.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(x => x.PublicId == o.PublicId)).SalesOrderId;
        var stored = await w.X.F.Db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == orderId).OrderBy(l => l.SalesOrderLineId).ToListAsync();
        var line = stored.Single(l => l.ProductId == season.ProductId);
        Assert.Equal((true, (int?)null, 0m, 0m), (line.IsTaxExempt, line.TaxRateId, line.TaxRatePct, line.TaxAmount));
        Assert.NotNull(stored.Single(l => l.ProductId == normal.ProductId).TaxRateId);
    }
}
