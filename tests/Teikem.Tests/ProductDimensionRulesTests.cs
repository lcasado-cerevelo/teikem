using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 230 — dimensiones (largo × ancho × alto con su unidad) y peso con unidad del producto: cálculo del volumen por unidad, sincronización con el volumen manual,
/// conversión, peso en lb/kg, sugerencia de unidades por la región de la compañía, el alta/edición/detalle del producto y la importación.
/// </summary>
public sealed class ProductDimensionRulesTests
{
    private static readonly DimensionState Empty = new(null, null, null, null, null);

    // ---------------------------------------------------------------- reglas puras

    [Theory]
    [InlineData("IN", 10, 10, 10, 0.0164)]      // 1000 in³ = 0.016387 m³
    [InlineData("CM", 100, 50, 20, 0.1)]        // 100 000 cm³
    [InlineData("FT", 2, 3, 4, 0.6796)]         // 24 ft³ = 0.679604 m³
    [InlineData("M", 1.2, 1, 0.5, 0.6)]
    public void With_the_three_dimensions_the_volume_is_calculated_in_cubic_meters(string unit, double l, double w, double h, double expectedM3)
    {
        var v = ProductDimensionRules.VolumeM3((decimal)l, (decimal)w, (decimal)h, unit);
        Assert.Equal((decimal)expectedM3, v);
    }

    [Fact]
    public void A_missing_dimension_gives_no_calculated_volume()
    {
        Assert.Null(ProductDimensionRules.VolumeM3(10m, null, 10m, DimensionUnits.Inch));
        Assert.Null(ProductDimensionRules.VolumeM3(10m, 0m, 10m, DimensionUnits.Inch));
        Assert.False(ProductDimensionRules.HasAllThree(1m, 1m, null));
    }

    [Fact]
    public void With_the_three_dimensions_a_manual_volume_is_ignored_and_the_calculated_one_wins()
    {
        var (s, error) = ProductDimensionRules.Apply(Empty, 100m, 50m, 20m, DimensionUnits.Centimeter, volumeM3: 9m, DimensionUnits.Inch);
        Assert.Null(error);
        Assert.Equal(0.1m, s.VolumeM3);
        Assert.Equal(DimensionUnits.Centimeter, s.DimensionUnit);
    }

    [Fact]
    public void Without_the_three_the_manual_volume_is_accepted_and_kept()
    {
        var (s, _) = ProductDimensionRules.Apply(Empty, 100m, null, null, DimensionUnits.Centimeter, volumeM3: 0.25m, DimensionUnits.Inch);
        Assert.Equal((100m, (decimal?)null, 0.25m), (s.Length!.Value, s.Width, s.VolumeM3!.Value));

        // otra medida suelta: el volumen escrito antes no se pierde
        var (s2, _) = ProductDimensionRules.Apply(s, null, 40m, null, null, null, DimensionUnits.Inch);
        Assert.Equal(0.25m, s2.VolumeM3);
        // con la tercera se reemplaza por el calculado
        var (s3, _) = ProductDimensionRules.Apply(s2, null, null, 10m, null, null, DimensionUnits.Inch);
        Assert.Equal(0.04m, s3.VolumeM3);
        // quitar una medida (0) deja el último volumen
        var (s4, _) = ProductDimensionRules.Apply(s3, 0m, null, null, null, null, DimensionUnits.Inch);
        Assert.Null(s4.Length);
        Assert.Equal(0.04m, s4.VolumeM3);
        // y sin las tres se vuelve a poder escribir a mano
        var (s5, _) = ProductDimensionRules.Apply(s4, null, null, null, null, 0.5m, DimensionUnits.Inch);
        Assert.Equal(0.5m, s5.VolumeM3);
    }

    [Fact]
    public void Without_a_unit_the_dimensions_take_the_current_one_or_the_suggested_one()
    {
        var (s, _) = ProductDimensionRules.Apply(Empty, 10m, 10m, 10m, null, null, DimensionUnits.Inch);
        Assert.Equal(DimensionUnits.Inch, s.DimensionUnit);
        var (s2, _) = ProductDimensionRules.Apply(s with { DimensionUnit = DimensionUnits.Foot }, 1m, null, null, null, null, DimensionUnits.Inch);
        Assert.Equal(DimensionUnits.Foot, s2.DimensionUnit);
        Assert.Equal(2.8317m, s2.VolumeM3);   // 1 × 10 × 10 = 100 ft³
    }

    [Fact]
    public void Dimensions_are_validated_and_units_normalized()
    {
        var errors = ProductDimensionRules.ValidateDimensions(-1m, 1.23456m, 0m);
        Assert.Equal(new[] { ("length", "El largo no puede ser negativo."), ("width", "El ancho debe ser un número de hasta 4 decimales.") }, errors.ToArray());
        Assert.Equal(("CM", (string?)null), ProductDimensionRules.NormalizeDimensionUnit(" cm "));
        Assert.Equal(ProductDimensionRules.DimensionUnitInvalid, ProductDimensionRules.NormalizeDimensionUnit("mm").Error);
        Assert.Equal(("LB", (string?)null), ProductDimensionRules.NormalizeWeightUnit("lb"));
        Assert.Equal(ProductDimensionRules.WeightUnitInvalid, ProductDimensionRules.NormalizeWeightUnit("oz").Error);
        Assert.Equal((null, null), ProductDimensionRules.NormalizeWeightUnit("  "));
        Assert.Equal(ProductDimensionRules.WeightNegative, ProductDimensionRules.ValidateWeight(-1m, WeightUnits.Pound));
    }

    [Theory]
    [InlineData(2.5, "LB", 1.134)]
    [InlineData(1, "LB", 0.454)]
    [InlineData(1.25, "KG", 1.25)]
    public void The_weight_is_stored_in_kg(double weight, string unit, double expectedKg)
        => Assert.Equal((decimal)expectedKg, ProductDimensionRules.ToKg((decimal)weight, unit));

    [Theory]
    [InlineData(2.5)]
    [InlineData(0.1)]
    [InlineData(12.34)]
    [InlineData(1500.99)]
    public void Pounds_round_trip_through_kg_with_two_decimals(double pounds)
    {
        var kg = ProductDimensionRules.ToKg((decimal)pounds, WeightUnits.Pound);
        Assert.Equal((decimal)pounds, ProductDimensionRules.FromKg(kg, WeightUnits.Pound));
    }

    [Fact]
    public void The_weight_in_its_unit_wins_over_weightKg_and_the_unit_is_remembered()
    {
        var w = ProductDimensionRules.ApplyWeight(new WeightState(null, null), 2.5m, WeightUnits.Pound, 9m, WeightUnits.Kilogram);
        Assert.Equal(new WeightState(1.134m, "LB"), w);
        // la forma de siempre (kg) conserva la unidad con que se muestra
        Assert.Equal(new WeightState(2m, "LB"), ProductDimensionRules.ApplyWeight(w, null, null, 2m, WeightUnits.Kilogram));
        Assert.Equal(new WeightState(2m, "KG"), ProductDimensionRules.ApplyWeight(new WeightState(null, null), null, null, 2m, WeightUnits.Pound));
        // sin unidad: la que ya tenía o la sugerida
        Assert.Equal(new WeightState(0.454m, "LB"), ProductDimensionRules.ApplyWeight(new WeightState(null, null), 1m, null, null, WeightUnits.Pound));
        // solo la unidad: el peso no cambia
        Assert.Equal(new WeightState(1.134m, "KG"), ProductDimensionRules.ApplyWeight(w, null, WeightUnits.Kilogram, null, WeightUnits.Pound));
    }

    [Theory]
    [InlineData("PR", "IMPERIAL", "IN", "FT", "LB")]
    [InlineData("US", "IMPERIAL", "IN", "FT", "LB")]
    [InlineData("MX", "METRIC", "CM", "M", "KG")]
    [InlineData(null, "IMPERIAL", "IN", "FT", "LB")]   // sin región = Puerto Rico
    public void The_units_are_suggested_by_the_company_region(string? region, string system, string small, string large, string weight)
    {
        var s = ProductDimensionRules.Suggest(region);
        Assert.Equal((system, small, large, weight), (s.System, s.DimensionUnit, s.LargeDimensionUnit, s.WeightUnit));
    }

    [Fact]
    public void The_volume_is_shown_in_the_cubic_unit_of_the_dimensions()
    {
        var calc = ProductDimensionRules.DisplayVolume(new DimensionState(4.5m, 3.2m, 2.1m, DimensionUnits.Inch, 0.0005m));
        Assert.Equal(new VolumeDisplay(30.24m, "IN3", true), calc);
        var manual = ProductDimensionRules.DisplayVolume(new DimensionState(2m, null, null, DimensionUnits.Foot, 0.6796m));
        Assert.Equal(new VolumeDisplay(24m, "FT3", false), manual with { Volume = Math.Round(manual.Volume!.Value, 1) });
        Assert.Equal(new VolumeDisplay(0.3m, "M3", false), ProductDimensionRules.DisplayVolume(new DimensionState(null, null, null, null, 0.3m)));
    }

    // ---------------------------------------------------------------- producto (alta, edición, detalle, importación)

    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<ProductService>();
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<EntityImportService>();
        s.AddSingleton<IImportEntityRows, ProductImportRows>();
    });

    [Fact]
    public async Task Creating_a_product_with_three_dimensions_calculates_the_volume_and_keeps_the_units()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductService>();
        var p = await svc.CreateAsync(new ProductCreateRequest("CAJA-1", "Cajita", VolumeM3: 5m, Length: 10m, Width: 10m, Height: 10m, DimensionUnit: "in", Weight: 2.5m, WeightUnit: "LB"), default);
        Assert.Equal(0.0164m, p.VolumeM3);   // el manual se ignora
        Assert.Equal(1.134m, p.WeightKg);
        var m = p.Measures!;
        Assert.Equal((10m, 10m, 10m, "IN", 1000m, "IN3", true, 2.5m, "LB"),
            (m.Length!.Value, m.Width!.Value, m.Height!.Value, m.DimensionUnit, m.Volume!.Value, m.VolumeUnit, m.VolumeCalculated, m.Weight!.Value, m.WeightUnit));
    }

    [Fact]
    public async Task Without_a_unit_the_region_suggestion_is_used_and_a_metric_company_gets_cm_and_kg()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductService>();
        var pr = await svc.CreateAsync(new ProductCreateRequest("PR-1", "Uno", Length: 2m, Weight: 1m), default);
        Assert.Equal(("IN", "LB", 0.454m), (pr.Measures!.DimensionUnit, pr.Measures.WeightUnit, pr.WeightKg!.Value));
        Assert.Equal(("PR", "IN", "FT", "LB"), await SuggestionAsync(svc));

        var t = await f.Db.Set<Tenant>().AsTracking().SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.RegionCode = "MX";
        await f.Db.SaveChangesAsync();
        Assert.Equal(("MX", "CM", "M", "KG"), await SuggestionAsync(svc));
        var mx = await svc.CreateAsync(new ProductCreateRequest("MX-1", "Dos", Length: 2m, Weight: 1m), default);
        Assert.Equal(("CM", "KG", 1m), (mx.Measures!.DimensionUnit, mx.Measures.WeightUnit, mx.WeightKg!.Value));
    }

    private static async Task<(string, string, string, string)> SuggestionAsync(ProductService svc)
    {
        var s = await svc.UnitsSuggestionAsync(default);
        Assert.Equal(new[] { "IN", "CM", "FT", "M" }, s.DimensionUnits);
        Assert.Equal(new[] { "LB", "KG" }, s.WeightUnits);
        return (s.RegionCode, s.DimensionUnit, s.LargeDimensionUnit, s.WeightUnit);
    }

    [Fact]
    public async Task Editing_keeps_a_written_volume_until_the_three_dimensions_are_there()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductService>();
        var p = await svc.CreateAsync(new ProductCreateRequest("BIG-1", "Grande", VolumeM3: 1.5m), default);
        p = await svc.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(Length: 2m, DimensionUnit: "M"), default);
        Assert.Equal(1.5m, p.VolumeM3);
        Assert.False(p.Measures!.VolumeCalculated);
        p = await svc.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(Width: 1m, Height: 0.5m, VolumeM3: 7m), default);
        Assert.Equal(1m, p.VolumeM3);
        Assert.True(p.Measures!.VolumeCalculated);
        p = await svc.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(Height: 0m), default);   // 0 = quitar
        Assert.Null(p.Measures!.Height);
        Assert.Equal(1m, p.VolumeM3);
        p = await svc.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(Weight: 3m, WeightUnit: "KG"), default);
        Assert.Equal((3m, "KG"), (p.WeightKg!.Value, p.Measures!.WeightUnit));
        p = await svc.UpdateAsync(p.Product.PublicId, new ProductPatchRequest(WeightUnit: "LB"), default);   // solo cambia cómo se muestra
        Assert.Equal((3m, 6.61m), (p.WeightKg!.Value, p.Measures!.Weight!.Value));
    }

    [Fact]
    public async Task Invalid_dimensions_and_units_answer_400_with_the_exact_message()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<ProductService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ProductCreateRequest("X-1", "X", Length: -1m, DimensionUnit: "MM", Weight: -2m, WeightUnit: "OZ"), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("El largo no puede ser negativo.", ex.Errors!["length"][0]);
        Assert.Equal(ProductDimensionRules.DimensionUnitInvalid, ex.Errors["dimensionUnit"][0]);
        Assert.Equal(ProductDimensionRules.WeightUnitInvalid, ex.Errors["weightUnit"][0]);
        var neg = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ProductCreateRequest("X-2", "X", Weight: -2m), default));
        Assert.Equal(ProductDimensionRules.WeightNegative, neg.Errors!["weight"][0]);
    }

    [Fact]
    public async Task The_detail_shows_the_category_with_its_parent()
    {
        await using var f = await CreateAsync();
        f.Db.Set<ProductCategory>().AddRange(new ProductCategory { ProductCategoryId = 501, TenantId = WmsFixture.TenantId, Name = "Guantes" },
            new ProductCategory { ProductCategoryId = 502, TenantId = WmsFixture.TenantId, Name = "Nitrilo", ParentId = 501 });
        await f.Db.SaveChangesAsync();
        var svc = f.Get<ProductService>();
        var p = await svc.CreateAsync(new ProductCreateRequest("G-M", "Guante M", CategoryId: 502), default);
        Assert.Equal(("Guantes / Nitrilo", 501, "Guantes"), (p.CategoryPath, p.CategoryParentId!.Value, p.CategoryParentName));
        var top = await svc.CreateAsync(new ProductCreateRequest("G-X", "Guante", CategoryId: 501), default);
        Assert.Equal(("Guantes", (int?)null), (top.CategoryPath, top.CategoryParentId));
    }

    [Fact]
    public async Task The_import_takes_the_new_optional_columns_with_the_same_rule()
    {
        await using var f = await CreateAsync();
        var tpl = await f.Get<ImportTemplateService>().CreateAsync(new ImportTemplateCreateRequest("Medidas",
            [new(1, "sku"), new(2, "name"), new(3, "length"), new(4, "width"), new(5, "height"), new(6, "dimensionUnit"), new(7, "weight"), new(8, "weightUnit"), new(9, "volumeM3")], null),
            ImportKinds.Product, default);
        var file = Encoding.UTF8.GetBytes(string.Join("\n",
            "sku,name,l,w,h,u,wt,wu,vol",
            "M-1,Caja,100,50,20,CM,2.5,LB,9",
            "M-2,Suelto,10,,,FT,,,0.75",
            "M-3,Malo,-1,,,MM,1,OZ,"));
        var result = await f.Get<EntityImportService>().ImportAsync(ImportKinds.Product, tpl.PublicId, file, "m.csv", commit: true, default);
        Assert.Equal((3, 2, 1), (result.Total, result.Valid, result.Rejected));
        Assert.Equal(new[] { "dimensionUnit", "length", "weightUnit" }, result.Rows[2].Errors.Keys.OrderBy(k => k).ToArray());

        var m1 = await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.Sku == "M-1");
        Assert.Equal((0.1m, 1.134m, "CM", "LB"), (m1.VolumeM3!.Value, m1.WeightKg!.Value, m1.DimensionUnit, m1.WeightUnit));
        var m2 = await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.Sku == "M-2");
        Assert.Equal((10m, 0.75m, "FT"), (m2.Length!.Value, m2.VolumeM3!.Value, m2.DimensionUnit));
    }
}
