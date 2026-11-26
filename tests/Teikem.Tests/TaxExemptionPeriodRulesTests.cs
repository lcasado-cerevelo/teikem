using Teikem.Domain.Tax;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 248: reglas puras de la exención de producto por temporada — vigencia con bordes, cobertura (ALL, producto, categoría con sus subcategorías), estado
// derivado, nombre del más temprano y el tratamiento EXEMPT_PRODUCT_PERIOD en el cálculo.
public sealed class TaxExemptionPeriodRulesTests
{
    private static readonly DateOnly Aug1 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);

    // árbol: 1 Escolar → 2 Libretas → 4 Libretas rayadas; 1 → 3 Lápices (hermana de 2)
    private static readonly Dictionary<int, int?> Tree = new() { [1] = null, [2] = 1, [3] = 1, [4] = 2 };

    private static TaxPeriodInfo P(int id, string scope, int[]? products = null, int[]? categories = null, string? name = null, DateOnly? from = null)
        => new(id, name ?? "P" + id, from ?? Aug1, Aug31, scope, (products ?? Array.Empty<int>()).ToHashSet(), (categories ?? Array.Empty<int>()).ToHashSet());

    [Fact]
    public void In_effect_includes_both_edges()
    {
        Assert.True(TaxExemptionPeriodRules.InEffect(Aug1, Aug31, Aug1));
        Assert.True(TaxExemptionPeriodRules.InEffect(Aug1, Aug31, Aug31));
        Assert.False(TaxExemptionPeriodRules.InEffect(Aug1, Aug31, Aug31.AddDays(1)));
        Assert.False(TaxExemptionPeriodRules.InEffect(Aug1, Aug31, Aug1.AddDays(-1)));
    }

    [Fact]
    public void Coverage_by_all_by_product_and_by_category_with_its_subcategories_but_not_its_siblings()
    {
        Assert.True(TaxExemptionPeriodRules.Covers(new[] { P(1, TaxExemptionPeriodScopes.All) }, 99, null, Tree));
        Assert.True(TaxExemptionPeriodRules.Covers(new[] { P(1, TaxExemptionPeriodScopes.Selected, products: new[] { 99 }) }, 99, 3, Tree));
        var notebooks = new[] { P(1, TaxExemptionPeriodScopes.Selected, categories: new[] { 2 }) };
        Assert.True(TaxExemptionPeriodRules.Covers(notebooks, 50, 2, Tree));    // la categoría escogida
        Assert.True(TaxExemptionPeriodRules.Covers(notebooks, 51, 4, Tree));    // su subcategoría
        Assert.False(TaxExemptionPeriodRules.Covers(notebooks, 52, 3, Tree));   // la hermana no
        Assert.False(TaxExemptionPeriodRules.Covers(notebooks, 53, 1, Tree));   // ni la madre
        Assert.False(TaxExemptionPeriodRules.Covers(notebooks, 54, null, Tree));
        // a prueba de ciclos en el árbol
        var loop = new Dictionary<int, int?> { [5] = 6, [6] = 5 };
        Assert.False(TaxExemptionPeriodRules.Covers(notebooks, 55, 5, loop));
        Assert.Equal(new HashSet<int> { 4, 2, 1 }, TaxExemptionPeriodRules.CategoryWithAncestors(4, Tree));
    }

    [Fact]
    public void Overlapping_periods_show_the_earliest_name_and_state_is_derived_from_the_date()
    {
        var a = P(2, TaxExemptionPeriodScopes.All, name: "Regreso a clases", from: Aug1);
        var b = P(1, TaxExemptionPeriodScopes.All, name: "Agosto", from: Aug1.AddDays(5));
        var c = P(3, TaxExemptionPeriodScopes.All, name: "Empate", from: Aug1);
        Assert.Equal("Regreso a clases", TaxExemptionPeriodRules.FirstName(new[] { b, c, a }));
        Assert.Null(TaxExemptionPeriodRules.FirstName(Array.Empty<TaxPeriodInfo>()));

        Assert.Equal(TaxExemptionPeriodStates.Scheduled, TaxExemptionPeriodRules.State(true, Aug1, Aug31, Aug1.AddDays(-1)));
        Assert.Equal(TaxExemptionPeriodStates.Active, TaxExemptionPeriodRules.State(true, Aug1, Aug31, Aug31));
        Assert.Equal(TaxExemptionPeriodStates.Ended, TaxExemptionPeriodRules.State(true, Aug1, Aug31, Aug31.AddDays(1)));
        Assert.Equal(TaxExemptionPeriodStates.Inactive, TaxExemptionPeriodRules.State(false, Aug1, Aug31, Aug1));
        Assert.Equal(TaxExemptionPeriodScopes.All, TaxExemptionPeriodRules.NormalizeScope(null));
        Assert.Equal(TaxExemptionPeriodScopes.Selected, TaxExemptionPeriodRules.NormalizeScope(" selected "));
        Assert.Null(TaxExemptionPeriodRules.NormalizeScope("ALGUNOS"));
    }

    [Fact]
    public void A_period_line_carries_no_tax_is_exempt_and_ignores_the_partial_client_exemption()
    {
        var rate = new TaxRateInfo(1, "IVU", "IVU", 11.5m, new[] { new TaxComponentInfo("STATE", "Estatal", 10.5m), new TaxComponentInfo("CITY", "Municipal", 1m) });
        var r = TaxRules.Calculate(new[] { new TaxLineInput("temporada", 100m, TaxTreatment.ExemptProductPeriod), new TaxLineInput("otro", 100m, TaxTreatment.Taxable) },
            rate, false, TaxRoundingModes.Line, new[] { "CITY" });
        var period = r.Lines[0];
        Assert.Equal((0m, TaxReasons.ExemptProductPeriod, true, (int?)null, 0m), (period.TaxAmount, period.Reason, period.IsTaxExempt, period.TaxRateId, period.TaxRatePct));
        Assert.Empty(period.Components);
        Assert.Equal((10.5m, TaxReasons.ExemptClientPartial), (r.Lines[1].TaxAmount, r.Lines[1].Reason));
        Assert.Equal(10.5m, r.TaxAmount);
        Assert.Equal(TaxReasons.ExemptProductPeriod, TaxRules.ReasonOf(TaxTreatment.ExemptProductPeriod));
        Assert.True(TaxRules.IsTaxExempt(TaxTreatment.ExemptProductPeriod));
    }
}
