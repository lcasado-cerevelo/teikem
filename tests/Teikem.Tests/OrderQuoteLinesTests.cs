using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Consolidación de líneas de la orden para cotizar (Lote 3, P4) e integración pura con ContractRateResolver.</summary>
public class OrderQuoteLinesTests
{
    private const string Std = "STANDARD";
    private const string Box = "BOX";
    private const string Env = "ENVELOPE";
    private const int ContractId = 7;

    [Fact]
    public void Build_consolidates_by_package_type_in_order_of_appearance()
    {
        var lines = OrderQuoteLines.Build(Std, new[] { (Box, 2), (Env, 1), (Box, 1) });
        Assert.Equal(2, lines.Count);
        Assert.Equal(new QuoteLine(Std, Box, 3), lines[0]);
        Assert.Equal(new QuoteLine(Std, Env, 1), lines[1]);
        Assert.Null(ContractRateResolver.FindDuplicateLine(lines));
    }

    [Fact]
    public void Build_empty_returns_empty()
        => Assert.Empty(OrderQuoteLines.Build(Std, Array.Empty<(string, int)>()));

    [Fact]
    public void Build_is_case_insensitive_on_package_type()
    {
        var lines = OrderQuoteLines.Build(Std, new[] { ("box", 2), ("BOX", 3), (" Box ", 1) });
        var line = Assert.Single(lines);
        Assert.Equal(6, line.Pieces);
        Assert.Equal("box", line.PackageType); // primera grafía vista, recortada
        Assert.Null(ContractRateResolver.FindDuplicateLine(lines));
    }

    [Fact]
    public void Build_ignores_blank_package_types()
    {
        var lines = OrderQuoteLines.Build(Std, new[] { (Box, 1), ("", 5), ("  ", 2) });
        var line = Assert.Single(lines);
        Assert.Equal(1, line.Pieces);
    }

    /// <summary>
    /// Filas de la bitácora / smoke del Lote 2: STANDARD/BOX base 7 + tramos 2–5 a 1.00 y 6+ a 0.75, STANDARD/ENVELOPE 5,
    /// despacho 3, COD 2.5 % de 200 = 5. La orden [(BOX,2),(ENVELOPE,1),(BOX,6)] consolida a BOX 8 + ENVELOPE 1 y cotiza
    /// 7 + 6.25 + 3 + 5 + 5 = 26.25 (mismo número que el smoke), con despacho y COD una sola vez en la primera línea.
    /// </summary>
    [Fact]
    public void QuoteOrder_with_consolidated_lines_reproduces_smoke_total_26_25()
    {
        var rows = new[]
        {
            new RateRow(1, ContractId, RateKinds.PerService, Std, Box, 7m, Array.Empty<TierRow>()),
            new RateRow(2, ContractId, RateKinds.PerService, Std, Env, 5m, Array.Empty<TierRow>()),
            new RateRow(3, ContractId, RateKinds.ExtraPiece, Std, Box, 0m, new[] { new TierRow(31, 2, 5, 1.00m), new TierRow(32, 6, null, 0.75m) }),
        };
        var flags = new ContractFlags(true, true, true, true, 3m, "PERCENT", 2.5m);
        var lines = OrderQuoteLines.Build(Std, new[] { (Box, 2), (Env, 1), (Box, 6) });

        var q = ContractRateResolver.QuoteOrder(rows, flags, lines, codAmount: 200m);

        Assert.Equal(2, q.Lines.Count);
        Assert.Equal(7m, q.Lines[0].BaseRate);
        Assert.Equal(6.25m, q.Lines[0].ExtraPieces);
        Assert.Equal(RateSources.Contract, q.Lines[0].BaseSource);
        Assert.Equal(5m, q.Lines[1].BaseRate);
        Assert.Equal(0m, q.Lines[1].ExtraPieces);
        Assert.Equal(3m, q.DispatchFee);
        Assert.Equal(5m, q.CodFee);
        Assert.Equal(26.25m, q.Total);
        // despacho y COD solo en la primera línea
        Assert.Equal(7m + 6.25m + 3m + 5m, q.Lines[0].LineTotal);
        Assert.Equal(5m, q.Lines[1].LineTotal);
    }

    [Fact]
    public void Without_consolidation_the_resolver_would_reject_the_repeated_pair()
    {
        var raw = new[] { new QuoteLine(Std, Box, 2), new QuoteLine(Std, Env, 1), new QuoteLine(Std, Box, 6) };
        Assert.Equal(2, ContractRateResolver.FindDuplicateLine(raw));
    }
}
