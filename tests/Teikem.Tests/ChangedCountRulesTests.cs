using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (D2, D3, D4 y hora de Puerto Rico) — reglas puras de "Conteo de lo cambiado": ventana por defecto y editable,
/// posiciones movidas sin los movimientos de un conteo, líneas de la posición completa con las claves vaciadas, estatus final
/// del conteo (D7) y mensajes exactos.
/// </summary>
public sealed class ChangedCountRulesTests
{
    private static readonly TimeZoneInfo Pr = LocalDay.DefaultZone;
    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_window_starts_at_local_midnight_the_first_time()
    {
        // 30/09 02:00 UTC = 29/09 22:00 en Puerto Rico: "hoy" es el 29 y empieza a las 04:00 UTC del 29.
        var (from, to) = ChangedCountRules.DefaultWindow(null, Utc(2026, 9, 30, 2), Pr);
        Assert.Equal(Utc(2026, 9, 29, 4), from);
        Assert.Equal(Utc(2026, 9, 30, 2), to);
        Assert.Equal(DateTimeKind.Utc, from.Kind);

        // 30/09 04:00 UTC = 00:00 local del 30.
        Assert.Equal(Utc(2026, 9, 30, 4), ChangedCountRules.DefaultWindow(null, Utc(2026, 9, 30, 4, 30), Pr).FromUtc);
    }

    [Fact]
    public void Default_window_continues_from_the_last_generation_and_is_capped_at_31_days()
    {
        var now = Utc(2026, 9, 30, 15);
        Assert.Equal(Utc(2026, 9, 28, 13), ChangedCountRules.DefaultWindow(Utc(2026, 9, 28, 13), now, Pr).FromUtc);
        // Fecha de la base sin Kind → UTC.
        var unspecified = DateTime.SpecifyKind(Utc(2026, 9, 28, 13), DateTimeKind.Unspecified);
        Assert.Equal(Utc(2026, 9, 28, 13), ChangedCountRules.DefaultWindow(unspecified, now, Pr).FromUtc);
        // Más de 31 días atrás → ahora − 31 días.
        Assert.Equal(now.AddDays(-31), ChangedCountRules.DefaultWindow(Utc(2026, 1, 1, 0), now, Pr).FromUtc);
        // Un 'hasta' anterior en el futuro (reloj adelantado) → desde = ahora.
        Assert.Equal(now, ChangedCountRules.DefaultWindow(now.AddHours(2), now, Pr).FromUtc);
    }

    [Fact]
    public void Requested_window_is_validated_and_to_never_passes_now()
    {
        var now = Utc(2026, 9, 30, 15);
        var ok = ChangedCountRules.ResolveWindow(Utc(2026, 9, 1, 4), now.AddDays(3), null, now, Pr);
        Assert.Null(ok.Error);
        Assert.Equal((Utc(2026, 9, 1, 4), now), (ok.FromUtc, ok.ToUtc));

        var inverted = ChangedCountRules.ResolveWindow(Utc(2026, 9, 29, 0), Utc(2026, 9, 28, 0), null, now, Pr);
        Assert.Equal(("fromUtc", "La fecha 'desde' no puede ser posterior a la fecha 'hasta'."), (inverted.Field, inverted.Error));

        var tooLong = ChangedCountRules.ResolveWindow(Utc(2026, 8, 1, 0), Utc(2026, 9, 2, 0), null, now, Pr);
        Assert.Equal(("fromUtc", "El rango de \"lo cambiado\" admite como máximo 31 días."), (tooLong.Field, tooLong.Error));
        Assert.Null(ChangedCountRules.ResolveWindow(Utc(2026, 8, 1, 0), Utc(2026, 9, 1, 0), null, now, Pr).Error);   // 31 días exactos

        // Solo 'desde': 'hasta' = ahora; solo 'hasta': 'desde' = el por defecto.
        Assert.Equal(now, ChangedCountRules.ResolveWindow(Utc(2026, 9, 30, 4), null, null, now, Pr).ToUtc);
        Assert.Equal(Utc(2026, 9, 30, 4), ChangedCountRules.ResolveWindow(null, Utc(2026, 9, 30, 10), null, now, Pr).FromUtc);
    }

    [Fact]
    public void Moved_keys_take_both_sides_in_the_warehouse_and_skip_count_movements()
    {
        var keys = ChangedCountRules.MovedKeys(new[]
        {
            new ChangedMovement(null, null, 1, 10, 100, null),              // entrada a la posición 10
            new ChangedMovement(1, 11, null, null, 100, 7),                 // salida de la 11 (lote 7)
            new ChangedMovement(1, 12, 2, 50, 101, null),                   // transferencia a otro almacén: solo el lado de este
            new ChangedMovement(2, 51, 1, 13, 102, null),                   // desde otro almacén: solo el lado de este
            new ChangedMovement(1, 14, 1, 15, 103, null),                   // interna: las dos posiciones
            new ChangedMovement(null, null, 1, 16, 104, null, FromCycleCount: true),   // ajuste de un conteo: no cuenta (D3)
            new ChangedMovement(null, null, 1, 10, 100, null),              // repetido
        }, warehouseId: 1);
        Assert.Equal(new[]
        {
            new MovedKey(10, 100, null), new MovedKey(11, 100, 7), new MovedKey(12, 101, null), new MovedKey(13, 102, null),
            new MovedKey(14, 103, null), new MovedKey(15, 103, null),
        }, keys);
        Assert.Equal(new[] { 10, 11, 12, 13, 14, 15 }, ChangedCountRules.Positions(keys));
    }

    [Fact]
    public void Lines_are_the_whole_position_plus_the_moved_keys_left_at_zero()
    {
        var balances = new[]
        {
            new CountCandidate(10, "A-01", 100, "SKU-B", null, null, 4m),   // movido, con saldo
            new CountCandidate(10, "A-01", 101, "SKU-A", null, null, 2m),   // NO movido, con saldo: entra (posición completa)
            new CountCandidate(10, "A-01", 102, "SKU-C", 7, "L7", 0m),     // movido y vaciado: entra con SystemQty 0
            new CountCandidate(10, "A-01", 103, "SKU-D", null, null, 0m),   // en 0 de antes y sin movimiento: no entra
        };
        var moved = new[] { new MovedKey(10, 100, null), new MovedKey(10, 102, 7) };

        var lines = ChangedCountRules.SelectLines(balances, moved, includeEmpty: true);
        Assert.Equal(new[] { ("SKU-A", 2m), ("SKU-B", 4m), ("SKU-C", 0m) }, lines.Select(l => (l.Sku, l.QtyOnHand)));

        var withoutEmpty = ChangedCountRules.SelectLines(balances, moved, includeEmpty: false);
        Assert.Equal(new[] { "SKU-A", "SKU-B" }, withoutEmpty.Select(l => l.Sku));

        // Posición vaciada por completo: con includeEmpty cuenta con líneas en 0; sin él no hay nada que contar.
        var emptied = new[] { new CountCandidate(11, "A-02", 100, "SKU-B", null, null, 0m) };
        Assert.Single(ChangedCountRules.SelectLines(emptied, new[] { new MovedKey(11, 100, null) }, includeEmpty: true));
        Assert.Empty(ChangedCountRules.SelectLines(emptied, new[] { new MovedKey(11, 100, null) }, includeEmpty: false));
    }

    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("Las 3 posiciones con cambios ya tienen un conteo pendiente.", ChangedCountRules.AllHaveOpenCount(3));
        Assert.Equal("Hay 250 posiciones con cambios; se generan como máximo 200 a la vez. Acote el rango de fechas o las zonas.",
            ChangedCountRules.TooManyPositions(250));
        Assert.Equal("No hubo movimientos en ALM-01 entre 29/09/2026 00:00 y 29/09/2026 22:00; no hay posiciones que contar.",
            ChangedCountRules.NoMovements("ALM-01", Utc(2026, 9, 29, 4), Utc(2026, 9, 30, 2), Pr));
        Assert.Equal(200, ChangedCountRules.MaxPositions);
        Assert.Equal(31, ChangedCountRules.MaxWindowDays);
    }

    [Fact]
    public void Reconcile_target_is_variance_when_something_was_posted()
    {
        Assert.Equal(CycleCountStatuses.ReconciledVariance, CycleCountRules.ReconcileTarget(1));
        Assert.Equal(CycleCountStatuses.Reconciled, CycleCountRules.ReconcileTarget(0));
        Assert.True(CycleCountStatuses.IsReconciled(CycleCountStatuses.Reconciled));
        Assert.True(CycleCountStatuses.IsReconciled(CycleCountStatuses.ReconciledVariance));
        Assert.False(CycleCountStatuses.IsReconciled(CycleCountStatuses.Counted));
        Assert.False(CycleCountStatuses.IsReconciled(null));
    }
}
