using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 13 — reglas puras del ciclo de estatus del recibo (ReceiptStatusRules): destino abierto por líneas (OpenTarget),
/// destino de la confirmación (ConfirmedTarget), camino escalonado (Path), respaldos con estatus apagados por la compañía,
/// fases del filtro y el filtro de diferencia; más los mensajes nuevos de ReceiptRules.
/// </summary>
public sealed class ReceiptStatusRulesTests
{
    private static readonly string[] All =
    {
        ReceiptStatuses.Expected, ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy, ReceiptStatuses.Received,
        ReceiptStatuses.ReceivedWithVariance, ReceiptStatuses.Putaway,
    };

    private static string[] Without(params string[] off) => All.Where(c => !off.Contains(c)).ToArray();

    [Theory]
    // actual, líneas, diferencia → destino (todo encendido)
    [InlineData(ReceiptStatuses.Expected, 0, false, ReceiptStatuses.Expected)]      // solo encabezado: se queda Esperado
    [InlineData(ReceiptStatuses.Expected, 1, false, ReceiptStatuses.Receiving)]
    [InlineData(ReceiptStatuses.Expected, 1, true, ReceiptStatuses.Discrepancy)]
    [InlineData(ReceiptStatuses.Receiving, 2, true, ReceiptStatuses.Discrepancy)]
    [InlineData(ReceiptStatuses.Discrepancy, 2, false, ReceiptStatuses.Receiving)]
    [InlineData(ReceiptStatuses.Discrepancy, 2, true, ReceiptStatuses.Discrepancy)]
    [InlineData(ReceiptStatuses.Receiving, 0, false, ReceiptStatuses.Receiving)]    // nunca vuelve a Esperado
    [InlineData(ReceiptStatuses.Discrepancy, 0, false, ReceiptStatuses.Receiving)]
    public void Open_target_follows_the_lines(string current, int lines, bool variance, string expected)
        => Assert.Equal(expected, ReceiptStatusRules.OpenTarget(current, lines, variance, All));

    [Fact]
    public void Open_target_falls_back_when_the_company_turns_statuses_off()
    {
        // Sin DISCREPANCY: la diferencia se queda en RECEIVING.
        Assert.Equal(ReceiptStatuses.Receiving, ReceiptStatusRules.OpenTarget(ReceiptStatuses.Receiving, 1, true, Without(ReceiptStatuses.Discrepancy)));
        // Sin RECEIVING: se queda en EXPECTED aunque tenga líneas (no hay camino a DISCREPANCY).
        var noReceiving = Without(ReceiptStatuses.Receiving);
        Assert.Equal(ReceiptStatuses.Expected, ReceiptStatusRules.OpenTarget(ReceiptStatuses.Expected, 3, true, noReceiving));
        Assert.Equal(ReceiptStatuses.Expected, ReceiptStatusRules.OpenTarget(ReceiptStatuses.Expected, 0, false, noReceiving));
    }

    [Fact]
    public void Confirmed_target_marks_the_variance_when_enabled()
    {
        Assert.Equal(ReceiptStatuses.Received, ReceiptStatusRules.ConfirmedTarget(false, All));
        Assert.Equal(ReceiptStatuses.ReceivedWithVariance, ReceiptStatusRules.ConfirmedTarget(true, All));
        Assert.Equal(ReceiptStatuses.Received, ReceiptStatusRules.ConfirmedTarget(true, Without(ReceiptStatuses.ReceivedWithVariance)));
    }

    [Fact]
    public void Path_goes_through_receiving_from_expected_to_the_laterals()
    {
        Assert.Empty(ReceiptStatusRules.Path(ReceiptStatuses.Receiving, ReceiptStatuses.Receiving, All));
        Assert.Equal(new[] { ReceiptStatuses.Receiving }, ReceiptStatusRules.Path(ReceiptStatuses.Expected, ReceiptStatuses.Receiving, All));
        Assert.Equal(new[] { ReceiptStatuses.Receiving, ReceiptStatuses.Discrepancy },
            ReceiptStatusRules.Path(ReceiptStatuses.Expected, ReceiptStatuses.Discrepancy, All));
        Assert.Equal(new[] { ReceiptStatuses.Receiving, ReceiptStatuses.ReceivedWithVariance },
            ReceiptStatusRules.Path(ReceiptStatuses.Expected, ReceiptStatuses.ReceivedWithVariance, All));
        Assert.Equal(new[] { ReceiptStatuses.ReceivedWithVariance },
            ReceiptStatusRules.Path(ReceiptStatuses.Discrepancy, ReceiptStatuses.ReceivedWithVariance, All));
        Assert.Equal(new[] { ReceiptStatuses.Received }, ReceiptStatusRules.Path(ReceiptStatuses.Receiving, ReceiptStatuses.Received, All));
        // Con RECEIVING apagado se va directo (entrada lateral EXPECTED → RECEIVED_VARIANCE sembrada).
        Assert.Equal(new[] { ReceiptStatuses.ReceivedWithVariance },
            ReceiptStatusRules.Path(ReceiptStatuses.Expected, ReceiptStatuses.ReceivedWithVariance, Without(ReceiptStatuses.Receiving)));
        // Sin distinguir mayúsculas.
        Assert.Empty(ReceiptStatusRules.Path("receiving", ReceiptStatuses.Receiving, All));
    }

    [Fact]
    public void Open_and_confirmed_sets()
    {
        Assert.All(ReceiptStatuses.OpenCodes, c => Assert.True(ReceiptStatusRules.IsOpen(c)));
        Assert.All(ReceiptStatuses.ConfirmedCodes, c => Assert.True(ReceiptStatusRules.IsConfirmed(c)));
        Assert.False(ReceiptStatusRules.IsOpen(ReceiptStatuses.Received));
        Assert.False(ReceiptStatusRules.IsOpen(ReceiptStatuses.Putaway));
        Assert.False(ReceiptStatusRules.IsOpen(null));
        Assert.False(ReceiptStatusRules.IsOpen("OPEN"));   // retirado en el Lote 13
        Assert.False(ReceiptStatusRules.IsConfirmed(ReceiptStatuses.Putaway));
        Assert.True(ReceiptStatusRules.IsPosted(ReceiptStatuses.Putaway));
        Assert.True(ReceiptStatusRules.IsPosted(ReceiptStatuses.ReceivedWithVariance));
        Assert.False(ReceiptStatusRules.IsPosted(ReceiptStatuses.Discrepancy));
    }

    [Fact]
    public void Phase_codes_and_unknown_phase()
    {
        Assert.Equal(ReceiptStatuses.OpenCodes, ReceiptStatusRules.PhaseCodes("open").Codes);
        Assert.Equal(new[] { ReceiptStatuses.Received, ReceiptStatuses.ReceivedWithVariance }, ReceiptStatusRules.PhaseCodes(" PENDING_PUTAWAY ").Codes);
        Assert.Equal(new[] { ReceiptStatuses.Putaway }, ReceiptStatusRules.PhaseCodes("DONE").Codes);
        var (codes, error) = ReceiptStatusRules.PhaseCodes("CLOSED");
        Assert.Empty(codes);
        Assert.Equal("Fase desconocida: 'CLOSED'. Use OPEN, PENDING_PUTAWAY o DONE.", error);
    }

    [Fact]
    public void Variance_filter_is_normalized_and_validated()
    {
        Assert.Equal(new[] { "SHORT", "NONE" }, ReceiptStatusRules.ParseVariance(new[] { " short", "NONE", "Short", "", null }).Codes);
        Assert.Empty(ReceiptStatusRules.ParseVariance(null).Codes);
        var (codes, error) = ReceiptStatusRules.ParseVariance(new[] { "OVER", "MISSING" });
        Assert.Empty(codes);
        Assert.Equal("Diferencia desconocida: 'MISSING'. Use SHORT, OVER o NONE.", error);
    }

    [Fact]
    public void New_messages_are_exact()
    {
        Assert.Equal("La cantidad esperada solo se captura en recibos ciegos o de devolución; en uno con aviso de llegada u orden de compra viene del documento.",
            ReceiptRules.ExpectedOnlyWithoutDocument);
        Assert.Equal("La cantidad esperada no puede ser negativa.", ReceiptRules.ExpectedQtyNegative);
        Assert.Equal("El producto de una línea del aviso de llegada o de la orden de compra no se puede cambiar.", ReceiptRules.DocumentLineProductFixed);
        Assert.Equal("La línea tiene asignaciones de cruce de muelle; cancélelas antes de cambiar el producto.", ReceiptRules.LineHasCrossDockProductChange);
        Assert.Equal("El tipo de un recibo con aviso de llegada u orden de compra no se puede cambiar.", ReceiptRules.TypeFixedWithDocument);
        Assert.Equal("El almacén solo se puede cambiar en un recibo sin aviso de llegada ni orden de compra y sin líneas.", ReceiptRules.WarehouseFixed);
        Assert.Equal("El transporte admite como máximo 80 caracteres.", ReceiptRules.CarrierTooLong);
        Assert.Equal("La referencia admite como máximo 80 caracteres.", ReceiptRules.ReferenceTooLong);
    }

    [Fact]
    public void Manual_expected_quantity_and_header_text()
    {
        Assert.Null(ReceiptRules.ValidateManualExpectedQty(0m, TrackingTypes.None, "A"));
        Assert.Equal(ReceiptRules.ExpectedQtyNegative, ReceiptRules.ValidateManualExpectedQty(-1m, TrackingTypes.None, "A"));
        Assert.Equal(ReceiptRules.QtyDecimals, ReceiptRules.ValidateManualExpectedQty(1.2345m, TrackingTypes.None, "A"));
        Assert.Equal(ReceiptRules.QtyTooLarge, ReceiptRules.ValidateManualExpectedQty(ReceiptRules.MaxQuantity + 1m, TrackingTypes.None, "A"));
        Assert.Equal(ReceiptRules.SerialIntegerQty("S"), ReceiptRules.ValidateManualExpectedQty(1.5m, TrackingTypes.Serial, "S"));

        Assert.Equal((false, (string?)null, (string?)null), ReceiptRules.PatchText(null, ReceiptRules.CarrierTooLong));
        Assert.Equal((true, (string?)null, (string?)null), ReceiptRules.PatchText("  ", ReceiptRules.CarrierTooLong));
        Assert.Equal((true, "DHL", (string?)null), ReceiptRules.PatchText(" DHL ", ReceiptRules.CarrierTooLong));
        Assert.Equal(ReceiptRules.CarrierTooLong, ReceiptRules.PatchText(new string('x', 81), ReceiptRules.CarrierTooLong).Error);
        Assert.Equal(((string?)null, (string?)null), ReceiptRules.CreateText("", ReceiptRules.ReferenceTooLong));
        Assert.Equal(ReceiptRules.ReferenceTooLong, ReceiptRules.CreateText(new string('x', 81), ReceiptRules.ReferenceTooLong).Error);
        Assert.Equal(new string('x', 80), ReceiptRules.CreateText(new string('x', 80), ReceiptRules.ReferenceTooLong).Value);
    }

    [Fact]
    public void Blind_variance_uses_the_captured_expected_but_posts_what_was_received()
    {
        // Decisión 3 del Lote 13: en ciegos la diferencia marca el estatus, pero al Kárdex entra lo recibido (sin ajuste).
        Assert.True(ReceiptRules.HasVariance(false, 5m, 3m));
        Assert.Equal(-2m, ReceiptRules.LineVariance(false, 5m, 3m));
        Assert.False(ReceiptRules.HasVariance(false, null, 3m));
        var plan = Assert.Single(ReceiptPostingRules.Plan(false, 5m, 3m, TrackingTypes.None));
        Assert.Equal((InventoryTxnTypes.Receipt, 3m), (plan.TxnType, plan.Quantity));
    }
}
