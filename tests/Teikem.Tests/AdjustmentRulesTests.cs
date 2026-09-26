using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P3: reglas puras del ajuste manual ± con motivo (D7) y de la transferencia (D40), con los mensajes exactos que
/// citan el manual y la FAQ.
/// </summary>
public class AdjustmentRulesTests
{
    private static readonly string[] Catalog =
    {
        AdjustmentReasons.ReceiptVariance, AdjustmentReasons.CountVariance, AdjustmentReasons.Damage, AdjustmentReasons.Loss,
        AdjustmentReasons.Found, AdjustmentReasons.Expired, AdjustmentReasons.PoShortage, AdjustmentReasons.PickBatchReversal,
        AdjustmentReasons.Other,
    };

    // ---------------------------------------------------------------- dirección y magnitud

    [Fact]
    public void ToPosting_positive_is_entry_to_bin()
    {
        var p = AdjustmentRules.ToPosting(3m);
        Assert.True(p.IsValid);
        Assert.True(p.IsEntry);
        Assert.Equal(3m, p.Magnitude);
    }

    [Fact]
    public void ToPosting_negative_is_exit_with_magnitude()
    {
        var p = AdjustmentRules.ToPosting(-2m);
        Assert.True(p.IsValid);
        Assert.False(p.IsEntry);
        Assert.Equal(2m, p.Magnitude);   // el ledger recibe la magnitud y guarda el signo
    }

    [Fact]
    public void ToPosting_zero_and_null_are_errors()
    {
        Assert.Equal("La cantidad del ajuste no puede ser cero.", AdjustmentRules.ToPosting(0m).Error);
        Assert.Equal("Indique la cantidad del ajuste.", AdjustmentRules.ToPosting(null).Error);
        Assert.False(AdjustmentRules.ToPosting(0m).IsValid);
    }

    // ---------------------------------------------------------------- motivo

    [Theory]
    [InlineData(AdjustmentReasons.ReceiptVariance)]
    [InlineData(AdjustmentReasons.CountVariance)]
    [InlineData(AdjustmentReasons.PickBatchReversal)]
    public void System_reasons_are_rejected(string code)
    {
        Assert.Equal($"El motivo {code} lo asigna el sistema.", AdjustmentRules.ValidateReason(code, Catalog).Error);
        Assert.Equal($"El motivo {code} lo asigna el sistema.", AdjustmentRules.ValidateReason(code.ToLowerInvariant(), Catalog).Error);
    }

    [Fact]
    public void Reason_is_required_and_from_catalog()
    {
        Assert.Equal("Indique el motivo del ajuste.", AdjustmentRules.ValidateReason(null, Catalog).Error);
        Assert.Equal("Indique el motivo del ajuste.", AdjustmentRules.ValidateReason("  ", Catalog).Error);
        Assert.Equal("Motivo de ajuste desconocido: 'FOO'.", AdjustmentRules.ValidateReason("foo", Catalog).Error);
        Assert.Equal(((string?)AdjustmentReasons.Damage, (string?)null), AdjustmentRules.ValidateReason(" damage ", Catalog));
        Assert.Equal(AdjustmentReasons.Found, AdjustmentRules.ValidateReason("FOUND", Catalog).Code);
    }

    // ---------------------------------------------------------------- series

    [Fact]
    public void NormalizeSerials_trims_skips_blanks_and_rejects_duplicates()
    {
        var (serials, error) = AdjustmentRules.NormalizeSerials(new[] { " S1 ", "", null, "S2" });
        Assert.Null(error);
        Assert.Equal(new[] { "S1", "S2" }, serials);
        Assert.Equal("El número de serie s1 está repetido.", AdjustmentRules.NormalizeSerials(new[] { "S1", "s1" }).Error);
        Assert.Empty(AdjustmentRules.NormalizeSerials(null).Serials);
    }

    [Fact]
    public void NormalizeSerials_limits()
    {
        var longSerial = new string('X', 81);
        Assert.Equal($"El número de serie {longSerial} excede 80 caracteres.", AdjustmentRules.NormalizeSerials(new[] { longSerial }).Error);
        var many = Enumerable.Range(1, 501).Select(i => "S" + i).ToList();
        Assert.Equal("Una línea admite como máximo 500 números de serie.", AdjustmentRules.NormalizeSerials(many).Error);
        Assert.Null(AdjustmentRules.NormalizeSerials(many.Take(500)).Error);
    }

    [Fact]
    public void ValidateTracking_serial_requires_integer_and_one_serial_per_unit()
    {
        Assert.Null(AdjustmentRules.ValidateTracking(TrackingTypes.Serial, "PS", 2m, false, 2));
        Assert.Equal(("quantity", "En productos con serie la cantidad debe ser entera."),
            AdjustmentRules.ValidateTracking(TrackingTypes.Serial, "PS", 1.5m, false, 1));
        Assert.Equal(("serialNumbers", "El producto PS se controla por serie; capture los números de serie."),
            AdjustmentRules.ValidateTracking(TrackingTypes.Serial, "PS", 1m, false, 0));
        Assert.Equal(("serialNumbers", "Se capturaron 2 serie(s) para una cantidad de 3; deben coincidir."),
            AdjustmentRules.ValidateTracking(TrackingTypes.Serial, "PS", 3m, false, 2));
    }

    [Fact]
    public void ValidateTracking_lot_and_none()
    {
        Assert.Equal(("lot", "El producto PL se controla por lote; indique el lote."),
            AdjustmentRules.ValidateTracking(TrackingTypes.Lot, "PL", 1m, false, 0));
        Assert.Null(AdjustmentRules.ValidateTracking(TrackingTypes.Lot, "PL", 1m, true, 0));
        Assert.Equal(("lot", "El producto PN no se controla por lote; no indique lote."),
            AdjustmentRules.ValidateTracking(TrackingTypes.None, "PN", 1m, true, 0));
        Assert.Equal(("serialNumbers", "El producto PN no se controla por serie; no capture números de serie."),
            AdjustmentRules.ValidateTracking(TrackingTypes.None, "PN", 1m, false, 1));
        Assert.Null(AdjustmentRules.ValidateTracking(TrackingTypes.None, "PN", 2.125m, false, 0));
    }

    // ---------------------------------------------------------------- lotes

    [Fact]
    public void Lot_input_and_dates()
    {
        Assert.Equal("Indique el número de lote.", AdjustmentRules.ValidateLotInput(" ", null, null).Error);
        Assert.Equal("El número de lote admite como máximo 60 caracteres.", AdjustmentRules.ValidateLotInput(new string('L', 61), null, null).Error);
        Assert.Equal("La fecha de fabricación no puede ser posterior al vencimiento.",
            AdjustmentRules.ValidateLotInput("L1", new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)).Error);
        Assert.Equal("L1", AdjustmentRules.ValidateLotInput(" L1 ", null, new DateOnly(2027, 1, 1)).Number);

        var mfg = new DateOnly(2026, 1, 1);
        var exp = new DateOnly(2027, 1, 1);
        Assert.True(AdjustmentRules.LotDatesMatch(mfg, exp, null, null));
        Assert.True(AdjustmentRules.LotDatesMatch(mfg, exp, mfg, exp));
        Assert.False(AdjustmentRules.LotDatesMatch(mfg, exp, null, new DateOnly(2027, 2, 1)));
        Assert.Equal("El lote L1 ya existe con otras fechas; corrija las fechas o use otro número de lote.",
            AdjustmentRules.LotExistsWithOtherDates("L1"));
    }

    // ---------------------------------------------------------------- transferencia y notas

    [Fact]
    public void Transfer_bins_required_and_distinct()
    {
        Assert.Equal(("toBinId", "La posición de origen y la de destino son la misma."), AdjustmentRules.ValidateTransferBins(5, 5));
        Assert.Equal(("fromBinId", "Indique la posición de origen."), AdjustmentRules.ValidateTransferBins(null, 5));
        Assert.Equal(("toBinId", "Indique la posición de destino."), AdjustmentRules.ValidateTransferBins(5, null));
        Assert.Null(AdjustmentRules.ValidateTransferBins(5, 6));
    }

    [Fact]
    public void Notes_are_trimmed_and_limited()
    {
        Assert.Equal(((string?)null, (string?)null), AdjustmentRules.NormalizeNotes("  "));
        Assert.Equal("hola", AdjustmentRules.NormalizeNotes(" hola ").Notes);
        Assert.Equal("Las notas admiten como máximo 300 caracteres.", AdjustmentRules.NormalizeNotes(new string('n', 301)).Error);
    }
}
