using Teikem.Domain.Sales;
using Xunit;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;
using R = Teikem.Domain.Sales.SalesAnalysisRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 241 — reglas puras del análisis de ventas: ABC 80/15/5 (por lo acumulado ANTES del producto), estado (sin ventas, parado, nuevo, subiendo, bajando, estable),
/// promedio/mínimo/máximo mensual de meses completos, semanas (ventana de 8 semanas completas, semanas usadas, desviación poblacional, confianza), cobertura, saldo
/// proyectado, punto de reorden sugerido (hoja «Reorder point»), tendencia, cuota vigente, conciliación, prorrateo de kits (regla del IIF), duplicados Bill / Item Receipt,
/// nombres y clases. Los números de las semanas son los de Movimiento_de_productos.xlsx (Solutions, al 5-nov-2026).
/// </summary>
public sealed class SalesAnalysisRulesTests
{
    private static DateOnly D(int y, int m, int d = 1) => new(y, m, d);

    [Fact]
    public void Abc_uses_the_share_accumulated_before_each_product()
    {
        // 79.7 % + 13.9 %: el segundo también es A porque lo acumulado antes de él (79.7 %) es menor que 80 %
        var abc = R.Abc(new[] { ("strips", 797m), ("monitor", 139m), ("gloves", 10m), ("lancets", 10m), ("other", 44m), ("none", 0m), ("credit", -5m) });
        Assert.Equal("A", abc["strips"]);
        Assert.Equal("A", abc["monitor"]);
        Assert.Equal("B", abc["other"]);     // antes de él: 93.6 % → B
        Assert.Equal("C", abc["gloves"]);    // empate con lancets: el orden ordinal decide (gloves antes) → antes 98.0 %
        Assert.Equal("C", abc["lancets"]);
        Assert.Equal(R.AbcNone, abc["none"]);
        Assert.Equal(R.AbcNone, abc["credit"]);
    }

    [Theory]
    [InlineData(null, null, null, R.StatusNoSales)]
    [InlineData("2026-09-03", "2026-08-01", null, R.StatusStale)]        // 63 días sin vender al 5-nov
    [InlineData("2026-09-29", "2026-08-01", null, R.StatusStable)]       // 37 días: no está parado; sin tendencia = estable
    [InlineData("2026-11-05", "2026-09-01", -0.6, R.StatusNew)]          // empezó en septiembre (el mes anterior al último completo)
    [InlineData("2026-11-05", "2026-06-01", 0.3352, R.StatusRising)]
    [InlineData("2026-11-05", "2026-06-01", -0.2823, R.StatusFalling)]
    [InlineData("2026-11-05", "2026-06-01", -0.2347, R.StatusStable)]    // −23 %: menos de 25 %
    [InlineData("2026-11-05", "2026-06-01", 0.25, R.StatusRising)]       // justo 25 %
    public void Status_follows_the_rules_of_the_notes_sheet(string? lastSale, string? firstMonth, double? trend, string expected)
    {
        var status = R.ProductStatus(lastSale is null ? null : DateOnly.Parse(lastSale), firstMonth is null ? null : DateOnly.Parse(firstMonth), D(2026, 11, 5),
            trend is null ? null : (decimal)trend.Value);
        Assert.Equal(expected, status);
    }

    [Fact]
    public void Monthly_stats_use_complete_months_from_the_first_month_with_sales()
    {
        var units = new Dictionary<DateOnly, decimal> { [D(2026, 3)] = 121m, [D(2026, 11)] = 50m };
        var s = R.MonthStats(units, D(2025, 12), D(2026, 10));
        Assert.Equal(8, s.MonthsUsed);          // marzo a octubre; noviembre (incompleto) no cuenta
        Assert.Equal(15.13m, s.Average);        // 121 / 8
        Assert.Equal(0m, s.Min);
        Assert.Equal(121m, s.Max);
        Assert.Equal(D(2026, 3), s.BestMonth);

        var onlyCurrent = R.MonthStats(new Dictionary<DateOnly, decimal> { [D(2026, 11)] = 5m }, D(2025, 12), D(2026, 10));
        Assert.Equal(0, onlyCurrent.MonthsUsed);
        Assert.Null(onlyCurrent.Average);
    }

    [Fact]
    public void Week_window_is_the_last_eight_complete_weeks_monday_to_sunday()
    {
        var (from, to) = R.WeekWindow(D(2026, 11, 5));   // jueves
        Assert.Equal(D(2026, 9, 7), from);
        Assert.Equal(D(2026, 11, 2), to);                 // exclusivo: la última semana termina el domingo 1-nov
        Assert.Equal(D(2026, 11, 2), R.WeekStart(D(2026, 11, 8)));
    }

    [Fact]
    public void Weekly_stats_match_the_reorder_point_sheet()
    {
        var strips = R.WeekStats(new[] { 2815m, 3660m, 7909m, 5056m, 2668m, 7012m, 4416m, 8610m });
        Assert.Equal((8, 8, 5268.25m, 2159.84m, R.ConfidenceHigh), (strips.WeeksUsed, strips.WeeksWithSales, strips.Average, strips.StdDev, strips.Confidence));

        var control = R.WeekStats(new[] { 0m, 0m, 0m, 1m, 0m, 0m, 0m, 0m });   // usa 5 semanas (desde la primera con venta), vende en 1
        Assert.Equal((5, 1, 0.2m, 0.4m, R.ConfidenceSporadic), (control.WeeksUsed, control.WeeksWithSales, control.Average, control.StdDev, control.Confidence));

        var underpads = R.WeekStats(new[] { 0m, 0m, 0m, 0m, 0m, 0m, 8m, 31m });
        Assert.Equal((2, 19.5m, 11.5m, R.ConfidenceLow), (underpads.WeeksUsed, underpads.Average, underpads.StdDev, underpads.Confidence));

        var half = R.WeekStats(new[] { 0m, 0m, 0m, 0m, 15m, 0m, 0m, 9m });       // 2 de 4 semanas con venta: esporádica
        Assert.Equal((4, 6m, 6.36m, R.ConfidenceSporadic), (half.WeeksUsed, half.Average, half.StdDev, half.Confidence));

        var none = R.WeekStats(new decimal[8]);
        Assert.Equal((0, (decimal?)null, R.ConfidenceNone), (none.WeeksUsed, none.Average, none.Confidence));
    }

    [Fact]
    public void Suggested_reorder_point_uses_lead_time_and_service_level()
    {
        Assert.Equal(6766, R.SuggestedReorderPoint(5268.25m, 2159.84m, 5, 1.645m));
        Assert.Equal(59, R.SuggestedReorderPoint(33.75m, 24.54m, 5, 1.645m));
        Assert.Equal(1, R.SuggestedReorderPoint(0.2m, 0.4m, 5, 1.645m));
        Assert.Equal(30, R.SuggestedReorderPoint(19.5m, 11.5m, 5, 1.645m));
        Assert.Equal(14, R.SuggestedReorderPoint(14m, 0m, 7, 1.645m));           // sin desviación: solo la venta del tiempo de entrega (14 / 7 × 7)
        Assert.Null(R.SuggestedReorderPoint(null, null, 5, 1.645m));
    }

    [Fact]
    public void Coverage_and_projection_come_from_the_weekly_average()
    {
        Assert.Equal(4.6m, R.CoverageWeeks(24130m, 5268.2m));
        Assert.Equal(1.1m, R.CoverageMonths(24130m, 5268.2m));     // 4.58 semanas ÷ 4.348
        Assert.Null(R.CoverageWeeks(100m, null));
        Assert.Null(R.CoverageWeeks(100m, 0m));
        Assert.Equal(0m, R.CoverageWeeks(-5m, 10m));
        Assert.Equal(1565m, R.ProjectedBalance(2000m, 100m, 1));   // 2000 − 100 × 4.348
        Assert.Equal(0m, R.ProjectedBalance(100m, 100m, 3));
        Assert.Equal(100m, R.ProjectedBalance(100m, null, 2));
    }

    [Fact]
    public void Trend_compares_the_month_with_the_average_of_the_two_previous_and_needs_sales_in_all_three()
    {
        Assert.Equal(0.1551m, R.Trend(23818m, 23554m, 17684m));
        Assert.Null(R.Trend(0m, 10m, 10m));
        Assert.Null(R.Trend(10m, 10m, 0m));
    }

    [Fact]
    public void Effective_quota_is_the_month_or_the_latest_previous_one()
    {
        var quotas = new[] { (D(2026, 8), 15000m), (D(2026, 10), 16500m) };
        Assert.Null(R.EffectiveQuota(quotas, D(2026, 7)));
        Assert.Equal(15000m, R.EffectiveQuota(quotas, D(2026, 9)));
        Assert.Equal(16500m, R.EffectiveQuota(quotas, D(2026, 10, 15)));
        Assert.Equal(16500m, R.EffectiveQuota(quotas, D(2027, 3)));
        Assert.Equal(1.0148m, R.Compliance(16744.75m, 16500m));
        Assert.Null(R.Compliance(100m, null));
        Assert.Equal(-0.0314m, R.ChangePct(60773m, 58866.41m));
        Assert.Null(R.ChangePct(0m, 5m));
    }

    [Fact]
    public void Reconciliation_reports_what_does_not_add_up()
    {
        var ok = R.Reconcile(141925.81m, -23.16m, 0m, 141902.65m);
        Assert.Equal(0m, ok.Difference);
        Assert.Equal(10m, R.Reconcile(100m, 0m, 0m, 110m).Difference);
    }

    [Fact]
    public void Kits_are_split_among_their_parts_with_the_quickbooks_rule()
    {
        var parts = new[] { new SalesKitPart(1, 1m, 10m), new SalesKitPart(2, 2m, 5m) };
        var shares = R.SplitKit(2m, 30m, parts);
        Assert.Equal(new[] { (1, 2m, 15m), (2, 4m, 15m) }, shares.Select(s => (s.ProductId, s.Quantity, s.Amount)).ToArray());
        // monto que no reparte exacto: cierra al centavo
        var odd = R.SplitKit(1m, 10m, new[] { new SalesKitPart(1, 1m, 1m), new SalesKitPart(2, 1m, 1m), new SalesKitPart(3, 1m, 1m) });
        Assert.Equal(10m, odd.Sum(s => s.Amount));
        Assert.Equal(new[] { 3.34m, 3.33m, 3.33m }, odd.Select(s => s.Amount).ToArray());
        // piezas sin precio: a partes iguales; una nota de crédito (negativo) respeta el signo
        var credit = R.SplitKit(-1m, -8m, new[] { new SalesKitPart(1, 1m, null), new SalesKitPart(2, 1m, 0m) });
        Assert.Equal(new[] { (-1m, -4m), (-1m, -4m) }, credit.Select(s => (s.Quantity, s.Amount)).ToArray());
    }

    [Fact]
    public void A_bill_that_repeats_its_item_receipt_is_dropped_once()
    {
        var rows = new[]
        {
            new PurchaseDupRow("Item Receipt", "PDC-1", "ADVANCE LOGISTICS LLC", "52800", 1000m),
            new PurchaseDupRow("Bill", "PDC-1", "Advance Logistics LLC", "52800", 1000m),   // la misma transacción: se descarta
            new PurchaseDupRow("Bill", "PDC-1", "ADVANCE LOGISTICS LLC", "52800", 1000m),   // un segundo Bill igual ya no tiene recibo que repetir
            new PurchaseDupRow("Bill", "PDC-2", "ADVANCE LOGISTICS LLC", "52800", 1000m),
            new PurchaseDupRow("Credit", "R-1", "GLOBAL", "ARC1", -10m),
        };
        Assert.Equal(new[] { 1 }, R.DuplicatedBills(rows).ToArray());
    }

    [Fact]
    public void Names_and_classes()
    {
        Assert.Equal("FARMACIA DEL RIO", R.NameKey("  Farmacia  Del Río "));
        Assert.Equal("FARMACIA DEL CENTRO", R.NameKey("FARMACIA DEL CENTRO."));
        Assert.Equal(string.Empty, R.NameKey(null));

        Assert.Equal(C.ClassProduct, R.ClassOf("Inventory", "52800 (PRODIGY STRIPS)", false));
        Assert.Equal(C.ClassDiscount, R.ClassOf("Discounts", "Discount 2%", false));
        Assert.Equal(C.ClassDiscount, R.ClassOf("Other Charges", "DESCUENTO ESPECIAL", false));
        Assert.Equal(C.ClassShipping, R.ClassOf("Service", "1000 (SHIPPING AND DELIVERY)", false));
        Assert.Equal(C.ClassOther, R.ClassOf("Service", "CCTEST (CC Test)", false));
        Assert.Equal(C.ClassOther, R.ClassOf("Parts", "001 ALLOWANCE (AJUSTE)", false));
        Assert.Equal(C.ClassProduct, R.ClassOf("Parts", "53310 (CONTROL SOLUTION)", true));
        Assert.Equal(C.ClassProduct, R.ClassOf("Something new", "X1 (Algo)", false));
        Assert.True(R.CountsForRep(C.ClassDiscount));
        Assert.False(R.CountsForRep(C.ClassShipping));
    }

    [Fact]
    public void Months_are_parsed_and_listed()
    {
        Assert.Equal(D(2026, 10), R.ParseMonth("2026-10"));
        Assert.Equal(D(2026, 10), R.ParseMonth("2026-10-17"));
        Assert.Null(R.ParseMonth("10/2026"));
        Assert.Null(R.ParseMonth(""));
        Assert.Equal(12, R.Months(D(2025, 12), D(2026, 11)).Count);
        Assert.Equal(D(2026, 10), R.LastCompleteMonth(D(2026, 11, 5)));
        Assert.Equal("2026-03", R.MonthKey(D(2026, 3)));
    }
}
