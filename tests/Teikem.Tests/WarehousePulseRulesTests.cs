using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 15 (P0) — reglas puras de la franja "Almacén hoy": ventana de 7 días LOCALES incluido hoy (D1, hora de Puerto Rico con
/// el mismo LocalDay del Lote 14), orillas de medianoche y días de 25 horas, relleno de días vacíos, qué movimientos son
/// entrada o salida (D2: la reversa de una recolección resta; transferencias, daños y pérdidas valen 0), tono naranja (D3) y el
/// mensaje exacto del 400.
/// </summary>
public class WarehousePulseRulesTests
{
    private static readonly TimeZoneInfo Pr = LocalDay.DefaultZone;

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    [Fact]
    public void Seven_local_days_ending_today_in_puerto_rico()
    {
        // 03:30Z del 30/09 = 23:30 del 29/09 en Puerto Rico: hoy es el 29 y los 7 días van del 23 al 29, cada uno desde las 04:00Z.
        var days = WarehousePulseRules.Days(Utc(2026, 9, 30, 3, 30), Pr);
        Assert.Equal(WarehousePulseRules.DefaultDays, days.Count);
        Assert.Equal(7, days.Count);
        Assert.Equal(Enumerable.Range(23, 7).Select(d => new DateOnly(2026, 9, d)), days.Select(d => d.Date));
        Assert.All(days, d => Assert.Equal(Utc(d.Date.Year, d.Date.Month, d.Date.Day, 4), d.StartUtc));
        Assert.All(days, d => Assert.Equal(d.StartUtc.AddDays(1), d.EndUtc));
        for (var i = 1; i < days.Count; i++) Assert.Equal(days[i - 1].EndUtc, days[i].StartUtc);   // seguidos, sin huecos

        // A las 04:00Z exactas ya es el 30 (la última barrita es hoy).
        var at4 = WarehousePulseRules.Days(Utc(2026, 9, 30, 4), Pr);
        Assert.Equal(new DateOnly(2026, 9, 30), at4[^1].Date);
        Assert.Equal(new DateOnly(2026, 9, 24), at4[0].Date);
        Assert.Equal(Utc(2026, 10, 1, 4), at4[^1].EndUtc);
    }

    [Fact]
    public void A_day_of_25_hours_in_a_zone_with_daylight_saving()
    {
        var ny = TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var iana) ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        // 01/11/2026 termina el horario de verano en Nueva York: ese día local dura 25 horas.
        var days = WarehousePulseRules.Days(Utc(2026, 11, 2, 12), ny, 3);
        Assert.Equal(new[] { new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2) }, days.Select(d => d.Date));
        Assert.Equal(Utc(2026, 11, 1, 4), days[1].StartUtc);
        Assert.Equal(Utc(2026, 11, 2, 5), days[1].EndUtc);
        Assert.Equal(TimeSpan.FromHours(25), days[1].EndUtc - days[1].StartUtc);
        Assert.Equal(TimeSpan.FromHours(24), days[0].EndUtc - days[0].StartUtc);
    }

    [Fact]
    public void Days_between_1_and_14_and_the_exact_message()
    {
        Assert.Single(WarehousePulseRules.Days(Utc(2026, 9, 30, 12), Pr, 1));
        Assert.Equal(new DateOnly(2026, 9, 30), WarehousePulseRules.Days(Utc(2026, 9, 30, 12), Pr, 1)[0].Date);
        var fourteen = WarehousePulseRules.Days(Utc(2026, 9, 30, 12), Pr, 14);
        Assert.Equal(14, fourteen.Count);
        Assert.Equal(new DateOnly(2026, 9, 17), fourteen[0].Date);
        foreach (var n in new[] { 0, -1, 15 })
        {
            Assert.False(WarehousePulseRules.IsValidDays(n));
            Assert.Throws<ArgumentOutOfRangeException>(() => WarehousePulseRules.Days(Utc(2026, 9, 30, 12), Pr, n));
        }
        Assert.True(WarehousePulseRules.IsValidDays(1) && WarehousePulseRules.IsValidDays(7) && WarehousePulseRules.IsValidDays(14));
        Assert.Equal("Los días deben estar entre 1 y 14.", WarehousePulseRules.DaysOutOfRange);
    }

    [Fact]
    public void Index_of_a_moment_uses_local_midnights_and_edges()
    {
        var days = WarehousePulseRules.Days(Utc(2026, 9, 30, 12), Pr);   // 24/09 … 30/09
        Assert.Equal(-1, WarehousePulseRules.IndexOf(days, Utc(2026, 9, 24, 3, 59, 59)));   // 23:59:59 del 23 local: fuera
        Assert.Equal(0, WarehousePulseRules.IndexOf(days, Utc(2026, 9, 24, 4)));             // 00:00 del 24 local
        Assert.Equal(5, WarehousePulseRules.IndexOf(days, Utc(2026, 9, 30, 3, 59)));          // 23:59 del 29 local: ayer
        Assert.Equal(6, WarehousePulseRules.IndexOf(days, Utc(2026, 9, 30, 4)));              // 00:00 del 30 local: hoy
        Assert.Equal(6, WarehousePulseRules.IndexOf(days, Utc(2026, 10, 1, 3, 59, 59)));      // 23:59:59 de hoy
        Assert.Equal(-1, WarehousePulseRules.IndexOf(days, Utc(2026, 10, 1, 4)));             // mañana: fuera
        // Fechas de la base: Kind Unspecified = UTC.
        Assert.Equal(6, WarehousePulseRules.IndexOf(days, DateTime.SpecifyKind(new DateTime(2026, 10, 1, 1, 0, 0), DateTimeKind.Unspecified)));
    }

    [Theory]
    [InlineData(InventoryTxnTypes.Receipt, null, 10, 10, 0)]
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.ReceiptVariance, -2, -2, 0)]   // llegó menos: se descuenta
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.ReceiptVariance, 3, 3, 0)]     // llegó más
    [InlineData(InventoryTxnTypes.Issue, null, -4, 0, 4)]                                       // recolección (el ledger la guarda en −)
    [InlineData(InventoryTxnTypes.CrossDock, null, -5, 0, 5)]                                   // cruce de muelle
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.PickBatchReversal, 4, 0, -4)]   // eliminar la recolección: resta
    [InlineData(InventoryTxnTypes.Transfer, null, 6, 0, 0)]
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.Damage, -1, 0, 0)]
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.Loss, -1, 0, 0)]
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.CountVariance, 2, 0, 0)]
    [InlineData(InventoryTxnTypes.Adjustment, AdjustmentReasons.Found, 2, 0, 0)]
    [InlineData("receipt", null, 1, 1, 0)]                                                      // sin distinguir mayúsculas
    public void Received_and_outbound_units_by_type_and_reason(string type, string? reason, int qty, int received, int outbound)
    {
        Assert.Equal(received, WarehousePulseRules.ReceivedUnits(type, reason, qty));
        Assert.Equal(outbound, WarehousePulseRules.OutboundUnits(type, reason, qty));
        Assert.Equal(received != 0, WarehousePulseRules.IsReceived(type, reason));
        Assert.Equal(outbound != 0, WarehousePulseRules.IsOutbound(type, reason));
    }

    [Fact]
    public void Series_fills_empty_days_with_zero_and_ignores_out_of_range()
    {
        var s = WarehousePulseRules.Series(7, new[] { (6, 3m), (6, 2m), (0, 1m), (-1, 99m), (7, 99m) });
        Assert.Equal(new[] { 1m, 0m, 0m, 0m, 0m, 0m, 5m }, s);
        Assert.Equal(new decimal[7], WarehousePulseRules.Series(7, Array.Empty<(int, decimal)>()));
    }

    [Fact]
    public void Orange_tone_for_counts_today_and_products_below_minimum()
    {
        Assert.True(WarehousePulseRules.CountsAlert(1));
        Assert.False(WarehousePulseRules.CountsAlert(0));
        Assert.True(WarehousePulseRules.BelowMinAlert(3));
        Assert.False(WarehousePulseRules.BelowMinAlert(0));
    }
}
