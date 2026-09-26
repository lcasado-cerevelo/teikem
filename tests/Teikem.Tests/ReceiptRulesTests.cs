using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P4) — reglas puras de la Recepción: mensajes exactos (manual/FAQ), precarga R8, tipo y origen, cantidades y seguimiento.</summary>
public sealed class ReceiptRulesTests
{
    [Fact]
    public void Plan_messages_are_exact()
    {
        Assert.Equal("El recibo REC-00001 ya fue confirmado; no se puede modificar.", ReceiptRules.ReceiptNotOpen("REC-00001"));
        Assert.Equal("El aviso de llegada ya tiene un recibo abierto o confirmado.", ReceiptRules.AsnBusy);
        Assert.Equal("El aviso de llegada no está pendiente de recibir.", ReceiptRules.AsnNotExpected);
        Assert.Equal("El almacén no tiene una posición de recepción (zona STAGING); indíquela.", ReceiptRules.NoStagingBin);
        Assert.Equal("La posición de recepción debe estar en una zona STAGING o CROSSDOCK.", ReceiptRules.StagingMustBeStaging);
        Assert.Equal("El producto SKU-1 no pertenece al cliente del aviso de llegada.", ReceiptRules.OwnerMismatch("SKU-1"));
        Assert.Equal("La orden de compra solo admite productos propios; SKU-2 pertenece a un cliente.", ReceiptRules.OwnProductsOnly("SKU-2"));
        Assert.Equal("Las líneas del aviso de llegada no se eliminan; capture 0 como recibido.", ReceiptRules.AsnLineNotRemovable);
        Assert.Equal("La línea tiene asignaciones de cruce de muelle; cancélelas antes de eliminarla.", ReceiptRules.LineHasCrossDock);
        Assert.Equal("El lote L1 ya existe con otras fechas; corrija las fechas o use otro número de lote.", ReceiptRules.LotExistsWithOtherDates("L1"));
    }

    [Fact]
    public void Max_lines_is_200()
    {
        Assert.Equal(200, ReceiptRules.MaxLines);
        Assert.Equal(500, ReceiptRules.MaxSerialsPerLine);
        Assert.Contains("200", ReceiptRules.TooManyLines);
    }

    [Fact]
    public void Received_quantity_starts_equal_to_expected()
    {
        Assert.Equal(12.5m, ReceiptRules.InitialReceived(12.5m));
        Assert.Equal(0m, ReceiptRules.LineVariance(true, 12.5m, ReceiptRules.InitialReceived(12.5m)));
    }

    [Theory]
    [InlineData(ReceiptOrigins.PurchaseOrder, ReceiptTypes.Asn)]
    [InlineData(ReceiptOrigins.Asn, ReceiptTypes.Asn)]
    [InlineData(ReceiptOrigins.Blind, ReceiptTypes.Blind)]
    [InlineData(ReceiptOrigins.Return, ReceiptTypes.Return)]
    public void TypeFor_maps_origin_to_receipt_type(string origin, string type) => Assert.Equal(type, ReceiptRules.TypeFor(origin));

    [Fact]
    public void TypeFor_rejects_unknown_origin() => Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptRules.TypeFor("FOO"));

    [Fact]
    public void OriginOf_distinguishes_purchase_order_client_asn_blind_and_return()
    {
        Assert.Equal(ReceiptOrigins.PurchaseOrder, ReceiptRules.OriginOf(ReceiptTypes.Asn, hasAsn: true, asnFromPurchaseOrder: true));
        Assert.Equal(ReceiptOrigins.Asn, ReceiptRules.OriginOf(ReceiptTypes.Asn, hasAsn: true, asnFromPurchaseOrder: false));
        Assert.Equal(ReceiptOrigins.Blind, ReceiptRules.OriginOf(ReceiptTypes.Blind, false, false));
        Assert.Equal(ReceiptOrigins.Return, ReceiptRules.OriginOf(ReceiptTypes.Return, false, false));
    }

    [Fact]
    public void Manual_type_defaults_to_blind_and_rejects_asn_and_unknown()
    {
        Assert.Equal((ReceiptTypes.Blind, (string?)null), ReceiptRules.ParseManualType(null));
        Assert.Equal((ReceiptTypes.Return, (string?)null), ReceiptRules.ParseManualType(" return "));
        Assert.Equal(ReceiptRules.AsnTypeNeedsDocument, ReceiptRules.ParseManualType("ASN").Error);
        Assert.Equal("Tipo de recepción desconocido: 'FOO'. Use ASN, BLIND o RETURN.", ReceiptRules.ParseManualType("FOO").Error);
    }

    [Fact]
    public void Only_asn_receipts_expect_quantities_and_have_variance()
    {
        Assert.True(ReceiptRules.ExpectsQuantities(ReceiptTypes.Asn));
        Assert.False(ReceiptRules.ExpectsQuantities(ReceiptTypes.Blind));
        Assert.Equal(-2m, ReceiptRules.LineVariance(true, 10m, 8m));
        Assert.Equal(3m, ReceiptRules.LineVariance(true, null, 3m));     // línea extra: espera 0
        Assert.Equal(0m, ReceiptRules.LineVariance(false, null, 7m));    // ciego: espera lo recibido
        Assert.False(ReceiptRules.HasVariance(false, null, 7m));
        Assert.True(ReceiptRules.HasVariance(true, 10m, 0m));
    }

    [Fact]
    public void Received_quantity_validation()
    {
        Assert.Equal(ReceiptRules.ReceivedQtyRequired, ReceiptRules.ValidateReceivedQty(null));
        Assert.Equal(ReceiptRules.ReceivedQtyNegative, ReceiptRules.ValidateReceivedQty(-1m));
        Assert.Equal("La cantidad admite como máximo 3 decimales.", ReceiptRules.ValidateReceivedQty(1.2345m));
        Assert.Equal("La cantidad excede el máximo permitido.", ReceiptRules.ValidateReceivedQty(10_000_000_000_000m));
        Assert.Null(ReceiptRules.ValidateReceivedQty(0m));
        Assert.Null(ReceiptRules.ValidateReceivedQty(1.125m));
        Assert.Equal(ReceiptRules.ExpectedQtyPositive, ReceiptRules.ValidateExpectedQty(0m));
    }

    [Fact]
    public void Tracking_validation_at_confirmation()
    {
        Assert.Null(ReceiptRules.ValidateTracking(TrackingTypes.None, "P", 5m, false, 0));
        Assert.Equal(ReceiptRules.LotNotAllowed("P"), ReceiptRules.ValidateTracking(TrackingTypes.None, "P", 5m, true, 0));
        Assert.Equal(ReceiptRules.SerialsNotAllowed("P"), ReceiptRules.ValidateTracking(TrackingTypes.None, "P", 1m, false, 1));
        Assert.Equal("El producto PL se controla por lote: indique el lote.", ReceiptRules.ValidateTracking(TrackingTypes.Lot, "PL", 5m, false, 0));
        Assert.Null(ReceiptRules.ValidateTracking(TrackingTypes.Lot, "PL", 0m, false, 0));   // en 0 no mueve inventario
        Assert.Equal("El producto PS se controla por serie: la cantidad debe ser entera.", ReceiptRules.ValidateTracking(TrackingTypes.Serial, "PS", 1.5m, false, 1));
        Assert.Equal("El producto PS se controla por serie: capture 3 número(s) de serie (hay 2).", ReceiptRules.ValidateTracking(TrackingTypes.Serial, "PS", 3m, false, 2));
        Assert.Null(ReceiptRules.ValidateTracking(TrackingTypes.Serial, "PS", 2m, true, 2));
    }

    [Fact]
    public void Capture_allows_pending_serials_but_not_fractional_serial_quantity()
    {
        Assert.Null(ReceiptRules.ValidateCapture(TrackingTypes.Serial, "PS", 3m, false, 0));
        Assert.Equal(ReceiptRules.SerialIntegerQty("PS"), ReceiptRules.ValidateCapture(TrackingTypes.Serial, "PS", 0.5m, false, 0));
        Assert.Equal(ReceiptRules.SerialsNotAllowed("PL"), ReceiptRules.ValidateCapture(TrackingTypes.Lot, "PL", 1m, true, 1));
    }

    [Fact]
    public void Serials_are_trimmed_unique_case_insensitive_and_bounded()
    {
        var (ok, err) = ReceiptRules.NormalizeSerials(new[] { " S1 ", "S2" });
        Assert.Null(err);
        Assert.Equal(new[] { "S1", "S2" }, ok);
        Assert.Equal("El número de serie 's1' está repetido.", ReceiptRules.NormalizeSerials(new[] { "S1", "s1" }).Error);
        Assert.Equal(ReceiptRules.SerialEmpty, ReceiptRules.NormalizeSerials(new[] { "S1", " " }).Error);
        Assert.Equal(ReceiptRules.SerialTooLong, ReceiptRules.NormalizeSerials(new[] { new string('x', 81) }).Error);
        Assert.Equal(ReceiptRules.TooManySerials, ReceiptRules.NormalizeSerials(Enumerable.Range(0, 501).Select(i => $"S{i}")).Error);
        Assert.Empty(ReceiptRules.NormalizeSerials(null).Serials);
    }

    [Fact]
    public void Lot_is_required_trimmed_and_dates_ordered()
    {
        Assert.Equal(("L1", (string?)null), ReceiptRules.NormalizeLot(" L1 ", null, null));
        Assert.Equal(ReceiptRules.LotNumberRequired, ReceiptRules.NormalizeLot("  ", null, null).Error);
        Assert.Equal(ReceiptRules.LotDates, ReceiptRules.NormalizeLot("L1", new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 1)).Error);
    }

    [Fact]
    public void Lot_dates_match_only_on_captured_dates()
    {
        var mfg = new DateOnly(2026, 1, 1);
        var exp = new DateOnly(2027, 1, 1);
        Assert.True(ReceiptRules.LotDatesMatch(mfg, exp, null, null));
        Assert.True(ReceiptRules.LotDatesMatch(mfg, exp, mfg, exp));
        Assert.True(ReceiptRules.LotDatesMatch(mfg, exp, null, exp));
        Assert.False(ReceiptRules.LotDatesMatch(mfg, exp, null, exp.AddDays(1)));
        Assert.False(ReceiptRules.LotDatesMatch(null, null, mfg, null));   // no se sobrescriben fechas (D34)
    }
}
