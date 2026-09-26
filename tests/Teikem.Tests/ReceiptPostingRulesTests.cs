using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P4) — matriz de asientos de una línea de recibo al confirmar (D4). Los asientos van en MAGNITUD (&gt; 0) con su
/// dirección; el ledger pone el signo. Propiedad: el neto CON SIGNO de RECEIPT + ADJUSTMENT es igual a lo recibido.
/// </summary>
public sealed class ReceiptPostingRulesTests
{
    private static string Fmt(IEnumerable<PlannedReceiptPosting> ps)
        => string.Join(" ", ps.Select(p => $"{p.TxnType}{(p.Inbound ? "+" : "-")}{p.Quantity:0.###}{(p.ReasonCode is null ? "" : ":" + p.ReasonCode)}"));

    [Fact]
    public void Blind_posts_a_single_receipt_for_what_was_received()
    {
        var plan = ReceiptPostingRules.Plan(false, null, 7m, TrackingTypes.None);
        Assert.Equal("RECEIPT+7", Fmt(plan));
        Assert.Null(plan[0].ReasonCode);
        Assert.Empty(ReceiptPostingRules.Plan(false, null, 0m, TrackingTypes.None));
    }

    [Fact]
    public void Expected_equal_to_received_posts_only_the_receipt()
        => Assert.Equal("RECEIPT+10", Fmt(ReceiptPostingRules.Plan(true, 10m, 10m, TrackingTypes.Lot)));

    [Fact]
    public void Short_receipt_posts_expected_and_an_outbound_variance()
    {
        var plan = ReceiptPostingRules.Plan(true, 10m, 8m, TrackingTypes.None);
        Assert.Equal($"RECEIPT+10 ADJUSTMENT-2:{AdjustmentReasons.ReceiptVariance}", Fmt(plan));
        Assert.Equal(-2m, plan[1].SignedQuantity);
        Assert.Equal(8m, ReceiptPostingRules.Net(plan));
    }

    [Fact]
    public void Over_receipt_posts_expected_and_an_inbound_variance()
        => Assert.Equal($"RECEIPT+5 ADJUSTMENT+1.5:{AdjustmentReasons.ReceiptVariance}", Fmt(ReceiptPostingRules.Plan(true, 5m, 6.5m, TrackingTypes.None)));

    [Fact]
    public void Nothing_received_posts_expected_in_and_the_same_out()
    {
        var plan = ReceiptPostingRules.Plan(true, 4m, 0m, TrackingTypes.None);
        Assert.Equal($"RECEIPT+4 ADJUSTMENT-4:{AdjustmentReasons.ReceiptVariance}", Fmt(plan));
        Assert.Equal(0m, ReceiptPostingRules.Net(plan));
    }

    [Fact]
    public void Extra_line_of_an_asn_receipt_is_only_an_inbound_adjustment()
    {
        Assert.Equal($"ADJUSTMENT+3:{AdjustmentReasons.ReceiptVariance}", Fmt(ReceiptPostingRules.Plan(true, null, 3m, TrackingTypes.None)));
        Assert.Empty(ReceiptPostingRules.Plan(true, null, 0m, TrackingTypes.None));
    }

    [Fact]
    public void Serial_blind_posts_one_receipt_per_serial()
    {
        var plan = ReceiptPostingRules.Plan(false, null, 3m, TrackingTypes.Serial, new[] { "S1", "S2", "S3" });
        Assert.Equal(3, plan.Count);
        Assert.All(plan, p => { Assert.Equal(InventoryTxnTypes.Receipt, p.TxnType); Assert.Equal(1m, p.Quantity); Assert.True(p.Inbound); });
        Assert.Equal(new[] { "S1", "S2", "S3" }, plan.Select(p => p.SerialNumber));
    }

    [Fact]
    public void Serial_over_receipt_posts_expected_serials_as_receipt_and_the_rest_as_adjustment()
    {
        var plan = ReceiptPostingRules.Plan(true, 2m, 3m, TrackingTypes.Serial, new[] { "A", "B", "C" });
        Assert.Equal($"RECEIPT+1 RECEIPT+1 ADJUSTMENT+1:{AdjustmentReasons.ReceiptVariance}", Fmt(plan));
        Assert.Equal("C", plan[2].SerialNumber);
    }

    [Fact]
    public void Serial_short_receipt_posts_only_the_serials_that_arrived()
    {
        var plan = ReceiptPostingRules.Plan(true, 3m, 1m, TrackingTypes.Serial, new[] { "A" });
        Assert.Equal("RECEIPT+1", Fmt(plan));
        Assert.Empty(ReceiptPostingRules.Plan(true, 3m, 0m, TrackingTypes.Serial, Array.Empty<string>()));
    }

    [Fact]
    public void Serial_extra_line_posts_inbound_adjustments()
        => Assert.Equal($"ADJUSTMENT+1:{AdjustmentReasons.ReceiptVariance} ADJUSTMENT+1:{AdjustmentReasons.ReceiptVariance}",
            Fmt(ReceiptPostingRules.Plan(true, null, 2m, TrackingTypes.Serial, new[] { "X", "Y" })));

    [Fact]
    public void Serial_count_must_match_and_received_cannot_be_negative()
    {
        Assert.Throws<ArgumentException>(() => ReceiptPostingRules.Plan(false, null, 2m, TrackingTypes.Serial, new[] { "A" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptPostingRules.Plan(false, null, -1m, TrackingTypes.None));
    }

    [Fact]
    public void All_postings_are_magnitudes()
        => Assert.All(ReceiptPostingRules.Plan(true, 10m, 3m, TrackingTypes.None), p => Assert.True(p.Quantity > 0m));

    /// <summary>Propiedad: sobre 200 casos generados (determinista), Σ con signo = recibido y toda magnitud &gt; 0.</summary>
    [Fact]
    public void Signed_net_equals_received_on_generated_cases()
    {
        var rnd = new Random(20260926);
        var trackings = new[] { TrackingTypes.None, TrackingTypes.Lot, TrackingTypes.Serial };
        for (var i = 0; i < 200; i++)
        {
            var tracking = trackings[i % 3];
            var expects = rnd.Next(4) != 0;
            decimal? expected = !expects || rnd.Next(5) == 0 ? null : RandomQty(rnd, tracking == TrackingTypes.Serial, positive: true);
            var received = RandomQty(rnd, tracking == TrackingTypes.Serial, positive: false);
            var serials = tracking == TrackingTypes.Serial
                ? Enumerable.Range(1, (int)received).Select(n => $"S{i}-{n}").ToArray()
                : null;

            var plan = ReceiptPostingRules.Plan(expects, expected, received, tracking, serials);

            Assert.Equal(received, ReceiptPostingRules.Net(plan));
            Assert.All(plan, p => Assert.True(p.Quantity > 0m, $"caso {i}: magnitud {p.Quantity}"));
            Assert.All(plan.Where(p => p.TxnType == InventoryTxnTypes.Adjustment), p => Assert.Equal(AdjustmentReasons.ReceiptVariance, p.ReasonCode));
            Assert.All(plan.Where(p => p.TxnType == InventoryTxnTypes.Receipt), p => Assert.True(p.Inbound));
        }
    }

    private static decimal RandomQty(Random rnd, bool integer, bool positive)
    {
        var whole = rnd.Next(positive ? 1 : 0, 25);
        if (integer) return whole;
        var q = whole + rnd.Next(0, 1000) / 1000m;
        return q > 0m || !positive ? q : 1m;
    }
}
