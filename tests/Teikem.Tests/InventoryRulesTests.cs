using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P0 — reglas puras del inventario: cantidades, seguimiento, dirección y signo del ledger (D3), reconstrucción.</summary>
public class InventoryRulesTests
{
    [Theory]
    [InlineData(null, InventoryRules.QuantityMustBePositive)]
    [InlineData("0", InventoryRules.QuantityMustBePositive)]
    [InlineData("-1", InventoryRules.QuantityMustBePositive)]
    [InlineData("1.2345", InventoryRules.QuantityMaxDecimals)]
    [InlineData("10000000000000", InventoryRules.QuantityTooLarge)]
    [InlineData("1.125", null)]
    [InlineData("5", null)]
    public void ValidateQuantity_messages(string? raw, string? expected)
    {
        decimal? q = raw is null ? null : decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, InventoryRules.ValidateQuantity(q));
    }

    [Fact]
    public void Exact_messages()
    {
        Assert.Equal("La cantidad debe ser mayor que cero.", InventoryRules.QuantityMustBePositive);
        Assert.Equal("La cantidad admite como máximo 3 decimales.", InventoryRules.QuantityMaxDecimals);
        Assert.Equal("La cantidad excede el máximo permitido.", InventoryRules.QuantityTooLarge);
        Assert.Equal("La reserva a liberar excede lo reservado.", InventoryRules.ReleaseExceedsReserved);
        Assert.Equal("Inventario insuficiente de PN en A01-R01-N1-P01: disponible 2.5, solicitado 9999.",
            InventoryRules.InsufficientStockMessage("PN", "A01-R01-N1-P01", 2.5m, 9999m));
        Assert.Equal(200, InventoryRules.MaxPageSize);
    }

    [Fact]
    public void ValidateTracking_by_type()
    {
        Assert.Equal(InventoryRules.LotRequired, InventoryRules.ValidateTracking(TrackingTypes.Lot, false, 1m, 0));
        Assert.Null(InventoryRules.ValidateTracking(TrackingTypes.Lot, true, 1.5m, 0));
        Assert.Equal(InventoryRules.LotNotAllowed, InventoryRules.ValidateTracking(TrackingTypes.None, true, 1m, 0));
        Assert.Equal(InventoryRules.SerialNotAllowed, InventoryRules.ValidateTracking(TrackingTypes.None, false, 1m, 1));
        Assert.Equal(InventoryRules.SerialInteger, InventoryRules.ValidateTracking(TrackingTypes.Serial, false, 1.5m, 1));
        Assert.Equal(InventoryRules.SerialCount, InventoryRules.ValidateTracking(TrackingTypes.Serial, false, 3m, 2));
        Assert.Null(InventoryRules.ValidateTracking(TrackingTypes.Serial, true, 2m, 2));
    }

    [Fact]
    public void ValidatePosting_direction_by_type_on_the_magnitude()
    {
        Assert.Null(InventoryRules.ValidatePosting(InventoryTxnTypes.Receipt, null, null, 1, 10, 5m, null, false));
        Assert.Equal(InventoryRules.DirectionInvalid(InventoryTxnTypes.Receipt), InventoryRules.ValidatePosting(InventoryTxnTypes.Receipt, 1, 10, null, null, 5m, null, false));
        Assert.Null(InventoryRules.ValidatePosting(InventoryTxnTypes.Issue, 1, 10, null, null, 5m, null, false));
        Assert.Equal(InventoryRules.DirectionInvalid(InventoryTxnTypes.CrossDock), InventoryRules.ValidatePosting(InventoryTxnTypes.CrossDock, 1, 10, 1, 11, 5m, null, false));
        Assert.Equal(InventoryRules.DirectionInvalid(InventoryTxnTypes.Adjustment), InventoryRules.ValidatePosting(InventoryTxnTypes.Adjustment, 1, 10, 1, 11, 5m, "FOUND", false));
        Assert.Equal(InventoryRules.ReasonRequired, InventoryRules.ValidatePosting(InventoryTxnTypes.Adjustment, null, null, 1, 10, 5m, " ", false));
        Assert.Equal(InventoryRules.SameBin, InventoryRules.ValidatePosting(InventoryTxnTypes.Transfer, 1, 10, 1, 10, 5m, null, false));
        Assert.Null(InventoryRules.ValidatePosting(InventoryTxnTypes.Transfer, 1, 10, 2, 20, 5m, null, false));
        Assert.Equal(InventoryRules.BinRequired, InventoryRules.ValidatePosting(InventoryTxnTypes.Receipt, null, null, 1, null, 5m, null, false));
        Assert.Equal(InventoryRules.SerialQty, InventoryRules.ValidatePosting(InventoryTxnTypes.Receipt, null, null, 1, 10, 2m, null, true));
        Assert.Equal(InventoryRules.QuantityMustBePositive, InventoryRules.ValidatePosting(InventoryTxnTypes.Receipt, null, null, 1, 10, -1m, null, false));
        Assert.Equal(InventoryRules.UnknownTxnType("FOO"), InventoryRules.ValidatePosting("FOO", null, null, 1, 10, 1m, null, false));
    }

    [Fact]
    public void StoredQuantity_follows_the_direction()
    {
        Assert.Equal(10m, InventoryRules.StoredQuantity(10m, hasTo: true));   // RECEIPT
        Assert.Equal(-3m, InventoryRules.StoredQuantity(3m, hasTo: false));   // ISSUE
        Assert.Equal(2m, InventoryRules.StoredQuantity(2m, hasTo: true));     // TRANSFER
        Assert.Equal(-1m, InventoryRules.StoredQuantity(1m, hasTo: false));   // ADJUSTMENT de salida
    }

    private static readonly LedgerRow[] Rows =
    {
        new(InventoryTxnTypes.Receipt, 1, null, null, null, 7, 10, 10m),
        new(InventoryTxnTypes.Issue, 1, null, 7, 10, null, null, -3m),
        new(InventoryTxnTypes.Transfer, 1, null, 7, 10, 7, 11, 2m),
        new(InventoryTxnTypes.Adjustment, 1, null, 7, 11, null, null, -1m),
        new(InventoryTxnTypes.Receipt, 1, 5, null, null, 7, 10, 4m),
    };

    [Fact]
    public void Rebuild_by_key_separates_lots()
    {
        var r = InventoryRules.Rebuild(Rows);
        Assert.Equal(5m, r[new BalanceKey(1, 7, 10, null)]);   // 10 − 3 − 2
        Assert.Equal(1m, r[new BalanceKey(1, 7, 11, null)]);   // 2 − 1
        Assert.Equal(4m, r[new BalanceKey(1, 7, 10, 5)]);      // lote X aparte del lote NULL
    }

    [Fact]
    public void NetByProduct_ignores_transfers()
    {
        var net = InventoryRules.NetByProduct(Rows.Take(4));
        Assert.Equal(6m, net[1]);   // 10 − 3 − 1 (TRANSFER no cuenta)
    }

    [Fact]
    public void Money_and_format()
    {
        Assert.Equal(123.4560m, InventoryRules.Round4(123.45600m));
        Assert.Equal(0.0001m, InventoryRules.Round4(0.00005m));
        Assert.Equal(123.456m, InventoryRules.CostValue(10m, 12.3456m));
        Assert.Null(InventoryRules.CostValue(10m, null));
        Assert.Equal("2.5", InventoryRules.FormatQty(2.500m));
        Assert.Equal("5", InventoryRules.FormatQty(5.000m));
        Assert.Equal(2m, InventoryRules.Available(5m, 3m));
    }

    [Fact]
    public void Balance_keys_order_like_the_lock_order()
    {
        var keys = new[] { new BalanceKey(2, 1, 1, null), new BalanceKey(1, 2, null, null), new BalanceKey(1, 1, 5, 3), new BalanceKey(1, 1, 5, null), new BalanceKey(1, 1, null, null) };
        var sorted = keys.OrderBy(k => k).ToList();
        Assert.Equal(new[] { new BalanceKey(1, 1, null, null), new BalanceKey(1, 1, 5, null), new BalanceKey(1, 1, 5, 3), new BalanceKey(1, 2, null, null), new BalanceKey(2, 1, 1, null) }, sorted);
    }
}
