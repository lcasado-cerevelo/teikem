using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 21 — reglas puras de captura original y corrección (CycleCountRules.ApplyCapture y LedgerNotes).</summary>
public sealed class CycleCountEvidenceRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = T0.AddHours(1);
    private static readonly DateTime T2 = T0.AddHours(2);

    private static CycleCountRules.CaptureState Apply(CycleCountRules.CaptureState s, decimal? qty, int user, DateTime at, bool finished = false)
        => CycleCountRules.ApplyCapture(s, qty, null, user, at, finished);

    [Fact]
    public void First_capture_sets_the_original_and_who_captured()
    {
        var s = Apply(CycleCountRules.CaptureState.Empty, 5m, 1, T0);
        Assert.Equal((5m, 5m, 1, (DateTime?)T0, (int?)null), (s.CountedQty, s.CapturedQty, s.CapturedBy, s.CapturedAtUtc, s.CorrectedBy));
        Assert.False(s.WasCorrected);
    }

    [Fact]
    public void Same_user_recaptures_while_open_replacing_the_original_without_a_correction()
    {
        var s = Apply(Apply(CycleCountRules.CaptureState.Empty, 5m, 1, T0), 6m, 1, T1);
        Assert.Equal((6m, 6m, (DateTime?)T1, false), (s.CountedQty, s.CapturedQty, s.CapturedAtUtc, s.WasCorrected));
    }

    [Fact]
    public void Another_user_corrects_and_the_original_is_kept()
    {
        var s = Apply(Apply(CycleCountRules.CaptureState.Empty, 0m, 1, T0), 1m, 2, T1);
        Assert.Equal((1m, 0m, 1, 2, (DateTime?)T1, true), (s.CountedQty, s.CapturedQty, s.CapturedBy, s.CorrectedBy, s.CorrectedAtUtc, s.WasCorrected));
    }

    [Fact]
    public void After_the_count_is_finished_even_the_same_user_corrects()
    {
        var s = Apply(Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0), 4m, 1, T1, finished: true);
        Assert.Equal((4m, 3m, 1, true), (s.CountedQty, s.CapturedQty, s.CorrectedBy ?? -1, s.WasCorrected));
    }

    [Fact]
    public void Returning_to_the_captured_value_clears_the_correction()
    {
        var corrected = Apply(Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0), 4m, 2, T1);
        var back = Apply(corrected, 3m, 2, T2);
        Assert.Equal((3m, 3m, false, (int?)null, (DateTime?)null), (back.CountedQty, back.CapturedQty, back.WasCorrected, back.CorrectedBy, back.CorrectedAtUtc));
    }

    [Fact]
    public void Resending_the_same_value_changes_nothing()
    {
        var corrected = Apply(Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0), 4m, 2, T1);
        Assert.Equal(corrected, Apply(corrected, 4m, 3, T2));
        var plain = Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0);
        Assert.Equal(plain, Apply(plain, 3m, 1, T2));
    }

    [Fact]
    public void Clearing_the_capture_returns_the_line_to_pending_and_drops_the_evidence()
    {
        var corrected = Apply(Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0), 4m, 2, T1);
        Assert.Equal(CycleCountRules.CaptureState.Empty, Apply(corrected, null, 2, T2));
    }

    [Fact]
    public void A_line_with_a_value_but_no_evidence_takes_it_as_the_original_and_is_corrected()
    {
        var legacy = CycleCountRules.CaptureState.Empty with { CountedQty = 5m };
        var s = Apply(legacy, 6m, 1, T1);
        Assert.Equal((6m, 5m, (int?)null, 1, true), (s.CountedQty, s.CapturedQty, s.CapturedBy, s.CorrectedBy, s.WasCorrected));
    }

    [Fact]
    public void Serial_lines_compare_the_serial_set_regardless_of_order_and_case()
    {
        var first = CycleCountRules.ApplyCapture(CycleCountRules.CaptureState.Empty, 2m, new[] { "A", "B" }, 1, T0, false);
        var sameSet = CycleCountRules.ApplyCapture(first, 2m, new[] { "b", "a" }, 2, T1, false);
        Assert.Equal(first, sameSet);                                    // mismo conjunto: no hay edición
        var changed = CycleCountRules.ApplyCapture(first, 2m, new[] { "A", "C" }, 2, T1, false);
        Assert.True(changed.WasCorrected);                               // misma cantidad, otra serie: corrección
        var back = CycleCountRules.ApplyCapture(changed, 2m, new[] { "B", "A" }, 2, T2, false);
        Assert.False(back.WasCorrected);                                 // vuelve al conjunto original
    }

    [Fact]
    public void Ledger_notes_are_the_classic_text_without_a_correction_and_carry_the_evidence_with_one()
    {
        var plain = Apply(CycleCountRules.CaptureState.Empty, 3m, 1, T0);
        Assert.Equal("Conteo CC-00007", CycleCountRules.LedgerNotes("CC-00007", plain, "Ana", "2026-10-03 10:00", null, null));
        var corrected = Apply(plain, 4m, 2, T1);
        var notes = CycleCountRules.LedgerNotes("CC-00007", corrected, "Ana", "2026-10-03 10:00", "Beto", "2026-10-03 11:00");
        Assert.Equal("Conteo CC-00007 · contó 3 (Ana, 2026-10-03 10:00) · corregido de 3 a 4 por Beto (2026-10-03 11:00)", notes);
        Assert.Contains("usuario desconocido", CycleCountRules.LedgerNotes("CC-1", corrected, null, null, null, null));
        Assert.True(CycleCountRules.LedgerNotes("CC-1", corrected, new string('x', 400), null, "B", null).Length <= 300);
    }
}
