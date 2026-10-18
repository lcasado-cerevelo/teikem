using Teikem.Domain.Billing;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 115 — cargo por devolución: por orden (una sola vez), por tipo de paquete o por pieza.</summary>
public class ReturnFeeRulesTests
{
    private static readonly IReadOnlyDictionary<int, decimal> ByType = new Dictionary<int, decimal> { [1] = 5m, [2] = 8m };

    [Theory]
    [InlineData("ORDER", 12, false, 12)]
    [InlineData("ORDER", 12, true, 0)]        // la orden ya tiene su cargo: no se cobra otra vez al sumar piezas
    [InlineData("PIECE", 3, false, 9)]        // 3 piezas × 3
    [InlineData("PIECE", 3, true, 9)]
    [InlineData("PACKAGE_TYPE", null, false, 18)]   // 5 + 5 + 8
    [InlineData(null, 12, false, 0)]
    [InlineData("OTRO", 12, false, 0)]
    public void The_fee_depends_on_the_mode(string? mode, int? amount, bool already, int expected)
        => Assert.Equal((decimal)expected, ReturnFeeRules.Compute(mode, amount is null ? null : (decimal)amount, ByType, new int?[] { 1, 1, 2 }, already));

    [Fact]
    public void A_package_type_without_an_amount_does_not_charge_and_no_pieces_means_no_fee()
    {
        Assert.Equal(5m, ReturnFeeRules.Compute("PACKAGE_TYPE", null, ByType, new int?[] { 1, 9, null }, false));
        Assert.Equal(0m, ReturnFeeRules.Compute("PIECE", 4m, ByType, Array.Empty<int?>(), false));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("order", "ORDER")]
    [InlineData(" piece ", "PIECE")]
    [InlineData("package_type", "PACKAGE_TYPE")]
    [InlineData("cada cosa", "?")]
    public void The_mode_is_normalized_and_an_unknown_one_is_flagged(string? raw, string? expected)
        => Assert.Equal(expected, ReturnFeeModes.Normalize(raw));
}
