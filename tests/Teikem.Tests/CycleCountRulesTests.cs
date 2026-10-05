using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P6 — reglas puras del conteo cíclico (D22): selección con tope, foto vieja, diferencia informativa contra la
/// foto frente al ajuste contra el saldo actual, captura por cantidad o por serie, plan de series (bajas, altas, TRANSFER)
/// y los mensajes exactos que citan el manual y la FAQ.
/// </summary>
public class CycleCountRulesTests
{
    private static CountCandidate C(int bin, string binCode, int product, decimal qty, int? lot = null, string sku = "SKU")
        => new(bin, binCode, product, sku, lot, lot is null ? null : "L" + lot, qty);

    [Fact]
    public void SelectLines_keeps_positive_stock_in_deterministic_order()
    {
        var (lines, error) = CycleCountRules.SelectLines(new[]
        {
            C(2, "B-02", 10, 5m), C(1, "B-01", 11, 3m, sku: "ZZ"), C(1, "B-01", 10, 0m), C(1, "B-01", 12, 1m, sku: "AA"),
            C(1, "B-01", 12, 2m, lot: 7, sku: "AA"),
        });
        Assert.Null(error);
        Assert.Equal(new[] { (1, 12, (int?)null), (1, 12, 7), (1, 11, null), (2, 10, null) },
            lines.Select(l => (l.BinId, l.ProductId, l.LotId)));
    }

    [Fact]
    public void SelectLines_over_1000_is_rejected_with_the_exact_message()
    {
        var many = Enumerable.Range(1, 1001).Select(i => C(i, $"B-{i:0000}", 1, 1m));
        var (lines, error) = CycleCountRules.SelectLines(many);
        Assert.Empty(lines);
        Assert.Equal("El conteo admite como máximo 1000 líneas; acote los filtros.", error);

        var (ok, none) = CycleCountRules.SelectLines(Enumerable.Range(1, 1000).Select(i => C(i, $"B-{i:0000}", 1, 1m)));
        Assert.Null(none);
        Assert.Equal(1000, ok.Count);
    }

    [Fact]
    public void SelectLines_without_stock_is_rejected()
    {
        var (lines, error) = CycleCountRules.SelectLines(new[] { C(1, "B-01", 1, 0m) });
        Assert.Empty(lines);
        Assert.Equal(CycleCountRules.NothingSelected, error);
    }

    [Fact]
    public void IsStale_variance_and_adjustment()
    {
        Assert.False(CycleCountRules.IsStale(8m, 8m));
        Assert.True(CycleCountRules.IsStale(8m, 7m));

        // Foto 8, contado 9, un despacho de 1 después de la foto (saldo actual 7):
        Assert.Equal(1m, CycleCountRules.Variance(9m, 8m));        // informativa, contra la foto
        Assert.Equal(2m, CycleCountRules.Adjustment(9m, 7m));      // lo que se asienta, contra el saldo actual
        Assert.Null(CycleCountRules.Variance(null, 8m));
        Assert.Equal(-1m, CycleCountRules.Adjustment(4m, 5m));
        Assert.Equal(0m, CycleCountRules.Adjustment(5m, 5m));
    }

    [Fact]
    public void Reserved_guard_and_pending_lines()
    {
        Assert.True(CycleCountRules.CountBelowReserved(2m, 3m));
        Assert.False(CycleCountRules.CountBelowReserved(3m, 3m));
        Assert.Equal(2, CycleCountRules.PendingLines(new decimal?[] { 1m, null, 0m, null }));
        Assert.Equal(0, CycleCountRules.PendingLines(new decimal?[] { 0m }));
    }

    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("El conteo ya fue reconciliado; solo se consulta.", CycleCountRules.CountNotOpen);
        Assert.Equal("Faltan 3 línea(s) por contar.", CycleCountRules.CountIncomplete(3));
        Assert.Equal("El conteo de PN en A01-R01-N1-P01 (1.5) es menor que lo reservado (2); libere la reserva antes de reconciliar.",
            CycleCountRules.ReservedAboveCount("PN", "A01-R01-N1-P01", 1.5m, 2.000m));
        Assert.Equal("En productos con serie se capturan los números de serie, no la cantidad.", CycleCountRules.SerialCountedByList);
        Assert.Equal("Esa posición, producto y lote ya están en el conteo.", CycleCountRules.LineDuplicated);
        Assert.Equal("Las tareas de conteo se completan desde Conteo cíclico.", CycleCountRules.CompleteFromCountScreen);
    }

    [Fact]
    public void Capture_quantity_for_none_and_lot()
    {
        var zero = CycleCountRules.Capture(TrackingTypes.None, "PN", 0m, null);
        Assert.Equal(0m, zero.Counted);
        Assert.Null(zero.Serials);
        Assert.Null(zero.Error);
        var lot = CycleCountRules.Capture(TrackingTypes.Lot, "PL", 2.125m, null);
        Assert.Equal(2.125m, lot.Counted);
        Assert.Null(lot.Error);

        var negative = CycleCountRules.Capture(TrackingTypes.None, "PN", -1m, null);
        Assert.Equal(("countedQty", CycleCountRules.CountedNegative), (negative.Field, negative.Error));
        var decimals = CycleCountRules.Capture(TrackingTypes.None, "PN", 1.0001m, null);
        Assert.Equal("La cantidad admite como máximo 3 decimales.", decimals.Error);
        var serials = CycleCountRules.Capture(TrackingTypes.None, "PN", null, new[] { "S1" });
        Assert.Equal(("serialNumbers", "El producto PN no se controla por serie; no capture números de serie."), (serials.Field, serials.Error));

        // Sin cantidad: la captura se borra.
        var clear = CycleCountRules.Capture(TrackingTypes.None, "PN", null, null);
        Assert.Null(clear.Counted);
        Assert.Null(clear.Error);
    }

    [Fact]
    public void Capture_serial_counts_by_list()
    {
        var ok = CycleCountRules.Capture(TrackingTypes.Serial, "PS", null, new[] { " S1 ", "S2", "" });
        Assert.Null(ok.Error);
        Assert.Equal(2m, ok.Counted);
        Assert.Equal(new[] { "S1", "S2" }, ok.Serials);

        var zero = CycleCountRules.Capture(TrackingTypes.Serial, "PS", null, Array.Empty<string>());
        Assert.Equal(0m, zero.Counted);

        var byQty = CycleCountRules.Capture(TrackingTypes.Serial, "PS", 3m, null);
        Assert.Equal(("countedQty", CycleCountRules.SerialCountedByList), (byQty.Field, byQty.Error));
        var mismatch = CycleCountRules.Capture(TrackingTypes.Serial, "PS", 3m, new[] { "S1" });
        Assert.Equal(CycleCountRules.SerialCountedByList, mismatch.Error);

        var dup = CycleCountRules.Capture(TrackingTypes.Serial, "PS", null, new[] { "S1", "s1" });
        Assert.Equal("El número de serie s1 está repetido.", dup.Error);
        var tooLong = CycleCountRules.Capture(TrackingTypes.Serial, "PS", null, new[] { new string('X', 81) });
        Assert.StartsWith("El número de serie XXX", tooLong.Error);
        var tooMany = CycleCountRules.Capture(TrackingTypes.Serial, "PS", null, Enumerable.Range(1, 501).Select(i => "S" + i).ToArray());
        Assert.Equal("Una línea admite como máximo 500 números de serie.", tooMany.Error);
    }

    [Fact]
    public void ValidateLot_by_tracking()
    {
        Assert.Equal("El producto PL se controla por lote; indique el lote.", CycleCountRules.ValidateLot(TrackingTypes.Lot, "PL", false));
        Assert.Null(CycleCountRules.ValidateLot(TrackingTypes.Lot, "PL", true));
        Assert.Equal("El producto PN no se controla por lote; no indique lote.", CycleCountRules.ValidateLot(TrackingTypes.None, "PN", true));
        Assert.Null(CycleCountRules.ValidateLot(TrackingTypes.Serial, "PS", true));
        Assert.Null(CycleCountRules.ValidateLot(TrackingTypes.Serial, "PS", false));
    }

    [Fact]
    public void LotDatesMatch_ignores_uncaptured_dates()
    {
        var mfg = new DateOnly(2026, 1, 1);
        var exp = new DateOnly(2027, 1, 1);
        Assert.True(CycleCountRules.LotDatesMatch(mfg, exp, null, null));
        Assert.True(CycleCountRules.LotDatesMatch(mfg, exp, mfg, exp));
        Assert.False(CycleCountRules.LotDatesMatch(mfg, exp, null, exp.AddDays(1)));
    }

    // ---------------------------------------------------------------- series

    private static Dictionary<string, SerialLocation> Loc(params SerialLocation[] locations)
        => locations.ToDictionary(l => l.SerialNumber, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void SerialVariance_removals_additions_and_transfers()
    {
        // Posición 100 del almacén 1: el sistema tiene S1 y S2. Se cuentan S2, S3 (desconocida), S4 (despachada) y S5 (en la posición 200).
        var result = CycleCountRules.SerialVariance(
            new[] { "S1", "S2" },
            new[] { "S2", "S3", "S4", "S5" },
            Loc(new SerialLocation("S2", SerialStatuses.Available, 1, 100, null),
                new SerialLocation("S4", SerialStatuses.Shipped, null, null, 9),
                new SerialLocation("S5", SerialStatuses.Available, 1, 200, 3)),
            warehouseId: 1, binId: 100);

        Assert.Equal(new[] { "S1" }, result.Removals);
        Assert.Equal(new[] { "S3", "S4" }, result.Additions);
        var t = Assert.Single(result.Transfers);
        Assert.Equal(new SerialTransferMove("S5", 1, 200, 3), t);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void SerialVariance_serial_counted_in_another_line_is_not_written_off()
    {
        var result = CycleCountRules.SerialVariance(new[] { "S1", "S2" }, new[] { "S2" }, Loc(), 1, 100,
            new HashSet<string>(new[] { "s1" }, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(result.Removals);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void SerialVariance_case_insensitive_and_same_bin_other_lot_does_not_move()
    {
        var result = CycleCountRules.SerialVariance(new[] { "ABC-1" }, new[] { "abc-1", "X9" },
            Loc(new SerialLocation("X9", SerialStatuses.Available, 1, 100, 42)), 1, 100);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void SerialVariance_serial_in_other_warehouse_is_transferred_and_reserved_counts_as_in_stock()
    {
        var result = CycleCountRules.SerialVariance(Array.Empty<string>(), new[] { "S7" },
            Loc(new SerialLocation("S7", SerialStatuses.Reserved, 2, 500, null)), 1, 100);
        Assert.Equal(new SerialTransferMove("S7", 2, 500, null), Assert.Single(result.Transfers));
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.Available));
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.Reserved));
        // Lote 27 (Rentas): en renta y en proceso siguen en inventario (ocupan posición).
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.OnRent));
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.InProcess));
        Assert.False(CycleCountRules.IsInStock(SerialStatuses.Shipped));
        Assert.False(CycleCountRules.IsInStock(SerialStatuses.Scrapped));
        Assert.False(CycleCountRules.IsInStock(null));
    }

    [Fact]
    public void FirstSerialCountedTwice_detects_duplicates_across_lines()
    {
        Assert.Null(CycleCountRules.FirstSerialCountedTwice(new[] { new[] { "S1", "S2" }, new[] { "S3" } }));
        Assert.Equal("s2", CycleCountRules.FirstSerialCountedTwice(new[] { new[] { "S1", "S2" }, new[] { "s2" } }));
        Assert.Equal("La serie S9 está capturada en más de una línea del conteo.", CycleCountRules.SerialCountedTwice("S9"));
    }
}
