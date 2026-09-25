using Teikem.Domain.Orders;
using Teikem.Infrastructure.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Reglas puras del límite de crédito (Lote 3, P4).</summary>
public class CreditRulesTests
{
    [Fact]
    public void No_limit_always_ok_and_no_available()
    {
        var (ok, available) = CreditRules.Evaluate(new CreditCheck(null, 999999m, 123456m));
        Assert.True(ok);
        Assert.Null(available);
    }

    [Fact]
    public void Equality_is_allowed()
    {
        var (ok, available) = CreditRules.Evaluate(new CreditCheck(100m, 60m, 40m));
        Assert.True(ok);
        Assert.Equal(40m, available);
    }

    [Fact]
    public void One_cent_over_is_not_ok()
    {
        var (ok, available) = CreditRules.Evaluate(new CreditCheck(100m, 60m, 40.01m));
        Assert.False(ok);
        Assert.Equal(40m, available);
    }

    [Fact]
    public void Negative_exposure_is_treated_as_zero()
    {
        var (ok, available) = CreditRules.Evaluate(new CreditCheck(100m, -50m, 100m));
        Assert.True(ok);
        Assert.Equal(100m, available); // Available = Limit − 0, no Limit + 50
        Assert.Equal(0m, CreditRules.Exposure(new CreditCheck(100m, -50m, 1m)));
    }

    [Fact]
    public void Available_is_limit_minus_exposure_even_when_already_overdrawn()
    {
        var (ok, available) = CreditRules.Evaluate(new CreditCheck(100m, 130m, 0m));
        Assert.False(ok);
        Assert.Equal(-30m, available);
    }

    [Fact]
    public void Message_is_exact()
        => Assert.Equal("El cliente excede su límite de crédito: límite 100.00, en curso 60.00, esta orden 40.01.",
            CreditRules.Message(new CreditCheck(100m, 60m, 40.01m)));

    [Fact]
    public void Message_uses_invariant_decimal_point_and_two_decimals()
        => Assert.Equal("El cliente excede su límite de crédito: límite 1000.50, en curso 0.00, esta orden 12.35.",
            CreditRules.Message(new CreditCheck(1000.5m, -3m, 12.345m)));

    [Fact]
    public void Round4_does_not_change_the_comparison()
    {
        var amount = Money.Round4(40.00004m); // 40.0000
        var (ok, _) = CreditRules.Evaluate(new CreditCheck(100m, 60m, amount));
        Assert.True(ok);
        var over = Money.Round4(40.00005m); // 40.0001 (AwayFromZero)
        var (okOver, _) = CreditRules.Evaluate(new CreditCheck(100m, 60m, over));
        Assert.False(okOver);
    }
}
