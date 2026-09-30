using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 15 (P0) — reglas puras de la franja "Almacén hoy" del Pulso del día (lote15-plan.md §2.2, con las decisiones del dueño
/// de la §9, que mandan):
/// - D1: 7 barritas = los últimos 7 días calendario INCLUIDO HOY, en la zona de la compañía (hora de Puerto Rico, el mismo
///   punto único del Lote 14: ITenantClock / <see cref="LocalDay"/>). La última es hoy y crece durante el día; un día sin
///   movimiento vale 0 (barrita vacía). El número grande es lo de HOY y en letra pequeña va el total de los 7 días.
/// - D2: "Unidades recibidas" = lo que de verdad llegó (RECEIPT más los ajustes RECEIPT_VARIANCE: el neto con signo, igual que el
///   indicador 'Unidades recibidas'); "Unidades de salida" = recolección (ISSUE) + cruce de muelle (CROSSDOCK), y eliminar una
///   recolección (ajuste PICK_BATCH_REVERSAL) resta el día en que se elimina. No cuentan daños, pérdidas ni transferencias.
///   El ledger guarda la salida en negativo (D3 del Lote 6): la salida es −cantidad.
/// - D3: tono naranja (borde y número) en "Conteos con diferencia" si HOY hubo alguno y en "Productos bajo mínimo" si hay alguno.
/// Los días son [00:00 local, 00:00 local del día siguiente) en UTC: aguantan días de 23 o 25 horas en zonas con horario de verano.
/// </summary>
public static class WarehousePulseRules
{
    /// <summary>D1: siete días, incluido hoy.</summary>
    public const int DefaultDays = 7;
    public const int MinDays = 1;
    public const int MaxDays = 14;

    public const string DaysOutOfRange = "Los días deben estar entre 1 y 14.";

    /// <summary>¿Está el número de días en el rango permitido (1–14)?</summary>
    public static bool IsValidDays(int days) => days is >= MinDays and <= MaxDays;

    /// <summary>
    /// Los <paramref name="days"/> días locales que terminan HOY (de la más vieja a hoy), cada uno con sus medianoches locales
    /// en UTC (inicio inclusivo, fin exclusivo). Fuera de 1–14 lanza ArgumentOutOfRangeException (el servicio valida antes).
    /// </summary>
    public static IReadOnlyList<PulseDay> Days(DateTime nowUtc, TimeZoneInfo zone, int days = DefaultDays)
    {
        if (!IsValidDays(days)) throw new ArgumentOutOfRangeException(nameof(days), DaysOutOfRange);
        var today = LocalDay.Today(nowUtc, zone);
        var result = new List<PulseDay>(days);
        for (var i = days - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            result.Add(new PulseDay(d, LocalDay.StartOfDayUtc(d, zone), LocalDay.StartOfDayUtc(d.AddDays(1), zone)));
        }
        return result;
    }

    /// <summary>Posición del día que contiene el instante UTC (fechas de la base con Kind Unspecified = UTC), o −1 si cae fuera.</summary>
    public static int IndexOf(IReadOnlyList<PulseDay> days, DateTime utc)
    {
        var t = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        for (var i = 0; i < days.Count; i++)
            if (t >= days[i].StartUtc && t < days[i].EndUtc) return i;
        return -1;
    }

    /// <summary>
    /// D2 — unidades recibidas que aporta un movimiento (cantidad del ledger CON signo): RECEIPT suma su cantidad y un ADJUSTMENT
    /// con motivo RECEIPT_VARIANCE suma la suya (negativa si llegó menos de lo esperado): el neto es lo que de verdad llegó.
    /// Cualquier otro movimiento vale 0.
    /// </summary>
    public static decimal ReceivedUnits(string? txnType, string? reasonCode, decimal quantity)
    {
        if (Is(txnType, InventoryTxnTypes.Receipt)) return quantity;
        if (Is(txnType, InventoryTxnTypes.Adjustment) && Is(reasonCode, AdjustmentReasons.ReceiptVariance)) return quantity;
        return 0m;
    }

    /// <summary>
    /// D2 — unidades de salida que aporta un movimiento (cantidad del ledger CON signo): ISSUE (recolección) y CROSSDOCK (cruce de
    /// muelle) se guardan en negativo y suman −cantidad; el ajuste PICK_BATCH_REVERSAL (eliminar una recolección) se guarda en
    /// positivo y resta (−cantidad). Daños, pérdidas, vencidos, conteos y transferencias valen 0.
    /// </summary>
    public static decimal OutboundUnits(string? txnType, string? reasonCode, decimal quantity)
    {
        if (Is(txnType, InventoryTxnTypes.Issue) || Is(txnType, InventoryTxnTypes.CrossDock)) return -quantity;
        if (Is(txnType, InventoryTxnTypes.Adjustment) && Is(reasonCode, AdjustmentReasons.PickBatchReversal)) return -quantity;
        return 0m;
    }

    /// <summary>¿Cuenta el movimiento en "Unidades recibidas"?</summary>
    public static bool IsReceived(string? txnType, string? reasonCode)
        => Is(txnType, InventoryTxnTypes.Receipt) || (Is(txnType, InventoryTxnTypes.Adjustment) && Is(reasonCode, AdjustmentReasons.ReceiptVariance));

    /// <summary>¿Cuenta el movimiento en "Unidades de salida"?</summary>
    public static bool IsOutbound(string? txnType, string? reasonCode)
        => Is(txnType, InventoryTxnTypes.Issue) || Is(txnType, InventoryTxnTypes.CrossDock)
           || (Is(txnType, InventoryTxnTypes.Adjustment) && Is(reasonCode, AdjustmentReasons.PickBatchReversal));

    /// <summary>
    /// Relleno de días vacíos: una serie de <paramref name="days"/> posiciones en 0 a la que se suman los valores por índice de
    /// día (los índices fuera de rango, p. ej. −1, se ignoran). Siempre hay una barrita por día.
    /// </summary>
    public static decimal[] Series(int days, IEnumerable<(int Index, decimal Value)> values)
    {
        var result = new decimal[Math.Max(0, days)];
        foreach (var (index, value) in values ?? Enumerable.Empty<(int, decimal)>())
            if (index >= 0 && index < result.Length) result[index] += value;
        return result;
    }

    /// <summary>D3 — "Conteos con diferencia" va en naranja si HOY hubo alguno.</summary>
    public static bool CountsAlert(int countsToday) => countsToday > 0;

    /// <summary>D3 — "Productos bajo mínimo" va en naranja si hay alguno en este momento.</summary>
    public static bool BelowMinAlert(int belowMinProducts) => belowMinProducts > 0;

    private static bool Is(string? value, string code) => string.Equals(value, code, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un día local de la franja: la fecha y sus medianoches locales en UTC (inicio inclusivo, fin exclusivo).</summary>
public sealed record PulseDay(DateOnly Date, DateTime StartUtc, DateTime EndUtc);
