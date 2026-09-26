using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P7): monto congelado del viaje del chofer (CK_DriverTrip_Rate), fecha y notas.</summary>
public class DriverTripRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    [Fact]
    public void Freeze_with_rate_keeps_amount_and_rate_id()
    {
        var f = DriverTripRules.Freeze(75m, 12);
        Assert.Equal(75m, f.Amount);
        Assert.Equal(12, f.RateId);
        Assert.False(f.RateMissing);
        Assert.Null(f.Note);
        Assert.True(DriverTripRules.SatisfiesRateCheck(f.Amount, f.RateId, f.RateMissing));
    }

    [Fact]
    public void Freeze_without_rate_is_zero_and_missing()
    {
        var f = DriverTripRules.Freeze(null, null);
        Assert.Equal(0m, f.Amount);
        Assert.Null(f.RateId);
        Assert.True(f.RateMissing);
        Assert.Equal("sin tarifa configurada", f.Note);
        Assert.True(DriverTripRules.SatisfiesRateCheck(f.Amount, f.RateId, f.RateMissing));
    }

    [Fact]
    public void Freeze_zero_on_purpose_is_not_missing()
    {
        var f = DriverTripRules.Freeze(0m, 7);
        Assert.Equal(0m, f.Amount);
        Assert.Equal(7, f.RateId);
        Assert.False(f.RateMissing);
        Assert.Null(f.Note);
        Assert.True(DriverTripRules.SatisfiesRateCheck(f.Amount, f.RateId, f.RateMissing));
    }

    [Theory]
    [InlineData(80.0, null)]
    [InlineData(null, 5)]
    public void Freeze_with_half_a_rate_is_missing_and_consistent(double? rate, int? rateId)
    {
        var f = DriverTripRules.Freeze(rate is null ? null : (decimal)rate.Value, rateId);
        Assert.True(f.RateMissing);
        Assert.Equal(0m, f.Amount);
        Assert.Null(f.RateId);
        Assert.True(DriverTripRules.SatisfiesRateCheck(f.Amount, f.RateId, f.RateMissing));
    }

    [Fact]
    public void Rate_check_mirrors_the_sql_constraint()
    {
        Assert.False(DriverTripRules.SatisfiesRateCheck(10m, null, true));   // sin tarifa con monto
        Assert.False(DriverTripRules.SatisfiesRateCheck(0m, 3, true));       // sin tarifa con id
        Assert.False(DriverTripRules.SatisfiesRateCheck(10m, null, false));  // con tarifa sin id
        Assert.False(DriverTripRules.SatisfiesRateCheck(-1m, 3, false));     // CK_DriverTrip_Amount
    }

    [Fact]
    public void ValidateTripDate_rejects_future_only()
    {
        Assert.Equal("La fecha del viaje no puede ser futura.", DriverTripRules.ValidateTripDate(Today.AddDays(1), Today));
        Assert.Null(DriverTripRules.ValidateTripDate(Today, Today));
        Assert.Null(DriverTripRules.ValidateTripDate(Today.AddDays(-1), Today));
    }

    [Fact]
    public void ValidateNotes_caps_at_500()
    {
        Assert.NotNull(DriverTripRules.ValidateNotes(new string('x', 501)));
        Assert.Null(DriverTripRules.ValidateNotes(new string('x', 500)));
        Assert.Null(DriverTripRules.ValidateNotes(null));
    }
}
