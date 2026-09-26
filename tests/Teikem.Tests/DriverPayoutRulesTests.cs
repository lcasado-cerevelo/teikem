using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P6): motor puro del pago al chofer (fórmulas, fallback de intentos y fila vigente).</summary>
public class DriverPayoutRulesTests
{
    private static readonly IReadOnlyDictionary<int, decimal> ThreeLevels = new Dictionary<int, decimal> { [1] = 1.00m, [2] = 1.50m, [3] = 2.00m };

    private static readonly PayoutAttempt[] ThreeFailedThenDelivered =
    {
        new(1, false), new(2, false), new(3, false), new(4, true),
    };

    private sealed class Row(DateOnly from, DateOnly? to, int id = 0) : IEffectiveDated
    {
        public int Id { get; } = id;
        public DateOnly EffectiveFrom { get; set; } = from;
        public DateOnly? EffectiveTo { get; set; } = to;
    }

    private static readonly DateOnly Today = new(2026, 9, 26);

    // ---------------- Compute ----------------

    [Theory]
    [InlineData(DriverPayoutFormulas.DeliveryPlusAttempts, 10.50)]
    [InlineData(DriverPayoutFormulas.DeliveryIncludesFirst, 9.50)]
    [InlineData(DriverPayoutFormulas.FailedReplacesDelivery, 8.50)]
    public void Three_failed_then_delivered(string formula, double expected)
    {
        var r = DriverPayoutRules.Compute(formula, 4.00m, ThreeLevels, ThreeFailedThenDelivered);
        Assert.Equal((decimal)expected, r.Total);
        Assert.Equal(r.Lines.Sum(l => l.Amount), r.Total);
        Assert.Single(r.Lines, l => l.Kind == DriverPayoutRules.LineDelivery);
    }

    [Theory]
    [InlineData(DriverPayoutFormulas.DeliveryPlusAttempts, 5.00)]
    [InlineData(DriverPayoutFormulas.DeliveryIncludesFirst, 4.00)]
    [InlineData(DriverPayoutFormulas.FailedReplacesDelivery, 4.00)]
    public void Delivered_on_first_attempt(string formula, double expected)
    {
        var r = DriverPayoutRules.Compute(formula, 4.00m, ThreeLevels, new[] { new PayoutAttempt(1, true) });
        Assert.Equal((decimal)expected, r.Total);
    }

    [Theory]
    [InlineData(DriverPayoutFormulas.DeliveryPlusAttempts, 2.50)]
    [InlineData(DriverPayoutFormulas.DeliveryIncludesFirst, 1.50)]
    [InlineData(DriverPayoutFormulas.FailedReplacesDelivery, 2.50)]
    public void Two_failed_without_delivery(string formula, double expected)
    {
        var r = DriverPayoutRules.Compute(formula, 4.00m, ThreeLevels, new[] { new PayoutAttempt(1, false), new PayoutAttempt(2, false) });
        Assert.Equal((decimal)expected, r.Total);
        Assert.DoesNotContain(r.Lines, l => l.Kind == DriverPayoutRules.LineDelivery);
    }

    [Fact]
    public void Unordered_attempts_give_same_result()
    {
        var shuffled = new[] { new PayoutAttempt(4, true), new PayoutAttempt(2, false), new PayoutAttempt(1, false), new PayoutAttempt(3, false) };
        var r = DriverPayoutRules.Compute(DriverPayoutFormulas.DeliveryPlusAttempts, 4.00m, ThreeLevels, shuffled);
        Assert.Equal(10.50m, r.Total);
    }

    [Fact]
    public void Delivery_without_rate_is_a_zero_line_with_note()
    {
        var r = DriverPayoutRules.Compute(DriverPayoutFormulas.DeliveryPlusAttempts, null, ThreeLevels, new[] { new PayoutAttempt(1, true) });
        var delivery = Assert.Single(r.Lines, l => l.Kind == DriverPayoutRules.LineDelivery);
        Assert.Equal(0m, delivery.Amount);
        Assert.Equal("sin tarifa configurada", delivery.Note);
        Assert.Equal(1.00m, r.Total);
    }

    [Fact]
    public void Missing_intermediate_level_pays_zero_with_note()
    {
        var rates = new Dictionary<int, decimal> { [1] = 1.00m, [3] = 2.00m };
        var r = DriverPayoutRules.Compute(DriverPayoutFormulas.FailedReplacesDelivery, 4.00m, rates,
            new[] { new PayoutAttempt(1, false), new PayoutAttempt(2, false), new PayoutAttempt(3, true) });
        var second = Assert.Single(r.Lines, l => l.AttemptNumber == 2 && l.Kind == DriverPayoutRules.LineAttempt);
        Assert.Equal(0m, second.Amount);
        Assert.Equal(DriverPayoutRules.NoRateNote, second.Note);
        Assert.Equal(5.00m, r.Total);
    }

    [Fact]
    public void No_attempt_levels_pay_zero_with_note()
    {
        var r = DriverPayoutRules.Compute(DriverPayoutFormulas.DeliveryPlusAttempts, 4.00m, new Dictionary<int, decimal>(),
            new[] { new PayoutAttempt(1, false), new PayoutAttempt(2, true) });
        Assert.Equal(4.00m, r.Total);
        Assert.All(r.Lines.Where(l => l.Kind == DriverPayoutRules.LineAttempt), l =>
        {
            Assert.Equal(0m, l.Amount);
            Assert.Equal(DriverPayoutRules.NoRateNote, l.Note);
        });
    }

    [Fact]
    public void Unknown_formula_throws()
    {
        Assert.Throws<ArgumentException>(() => DriverPayoutRules.Compute("FOO", 1m, ThreeLevels, new[] { new PayoutAttempt(1, true) }));
    }

    // ---------------- AttemptRate (fallback R16) ----------------

    [Fact]
    public void Attempt_beyond_highest_level_uses_highest_level()
    {
        var pick = DriverPayoutRules.AttemptRate(ThreeLevels, 7);
        Assert.Equal(2.00m, pick.Amount);
        Assert.Equal(3, pick.LevelUsed);
        Assert.Null(pick.Note);
    }

    [Fact]
    public void Attempt_on_configured_level_uses_its_rate()
    {
        Assert.Equal(1.50m, DriverPayoutRules.AttemptRate(ThreeLevels, 2).Amount);
    }

    [Fact]
    public void Fallback_uses_highest_level_the_driver_has_not_tenant_levels()
    {
        var onlyFirst = new Dictionary<int, decimal> { [1] = 1.25m };
        Assert.Equal(1.25m, DriverPayoutRules.AttemptRate(onlyFirst, 3).Amount);
    }

    // ---------------- Validaciones ----------------

    [Fact]
    public void ValidateAttemptNumber_messages()
    {
        Assert.Equal("El intento 3 no existe; los niveles configurados van de 1 a 2.", DriverPayoutRules.ValidateAttemptNumber(3, 2));
        Assert.NotNull(DriverPayoutRules.ValidateAttemptNumber(0, 2));
        Assert.Null(DriverPayoutRules.ValidateAttemptNumber(2, 2));
        Assert.Null(DriverPayoutRules.ValidateAttemptNumber(1, 2));
    }

    [Fact]
    public void Known_formulas()
    {
        Assert.True(DriverPayoutRules.IsKnownFormula("DELIVERY_PLUS_ATTEMPTS"));
        Assert.True(DriverPayoutRules.IsKnownFormula(" delivery_includes_first "));
        Assert.True(DriverPayoutRules.IsKnownFormula("FAILED_REPLACES_DELIVERY"));
        Assert.False(DriverPayoutRules.IsKnownFormula("FOO"));
        Assert.False(DriverPayoutRules.IsKnownFormula(null));
        Assert.Equal(20, DriverPayoutRules.MaxAttemptLevels);
    }

    [Fact]
    public void ValidateAttempts_rejects_bad_sequences()
    {
        Assert.NotNull(DriverPayoutRules.ValidateAttempts(Array.Empty<PayoutAttempt>()));
        Assert.NotNull(DriverPayoutRules.ValidateAttempts(new[] { new PayoutAttempt(1, false), new PayoutAttempt(1, true) }));
        Assert.NotNull(DriverPayoutRules.ValidateAttempts(new[] { new PayoutAttempt(1, true), new PayoutAttempt(2, true) }));
        Assert.NotNull(DriverPayoutRules.ValidateAttempts(new[] { new PayoutAttempt(1, true), new PayoutAttempt(2, false) }));
        Assert.NotNull(DriverPayoutRules.ValidateAttempts(new[] { new PayoutAttempt(0, true) }));
        Assert.Null(DriverPayoutRules.ValidateAttempts(ThreeFailedThenDelivered));
    }

    // ---------------- PickCurrent ----------------

    [Fact]
    public void PickCurrent_effective_to_is_exclusive()
    {
        var closedToday = new Row(Today.AddDays(-10), Today, 1);
        var openToday = new Row(Today, null, 2);
        var pick = DriverPayoutRules.PickCurrent(new[] { closedToday, openToday }, Today);
        Assert.Same(openToday, pick);
        Assert.Same(closedToday, DriverPayoutRules.PickCurrent(new[] { closedToday, openToday }, Today.AddDays(-1)));
    }

    [Fact]
    public void PickCurrent_zero_length_row_is_never_current()
    {
        var zero = new Row(Today, Today, 1);
        Assert.Null(DriverPayoutRules.PickCurrent(new[] { zero }, Today));
    }

    [Fact]
    public void PickCurrent_without_rows_or_before_any_validity_is_null()
    {
        Assert.Null(DriverPayoutRules.PickCurrent(Array.Empty<Row>(), Today));
        Assert.Null(DriverPayoutRules.PickCurrent(new[] { new Row(Today, null) }, Today.AddDays(-1)));
    }

    [Fact]
    public void PickCurrent_tie_prefers_latest_effective_from()
    {
        var older = new Row(Today.AddDays(-5), null, 1);
        var newer = new Row(Today.AddDays(-1), null, 2);
        Assert.Same(newer, DriverPayoutRules.PickCurrent(new[] { older, newer }, Today));
    }
}
