using Teikem.Domain.Wms;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Avisos de flota (2026-10-21, Lote 168): reglas PURAS de qué se avisa de los documentos de vehículos (registro, seguro, inspección, permiso) y de choferes
/// (licencias y certificaciones). Documento vencido = su fecha de vencimiento es ANTERIOR a hoy; por vencer = vence hoy o dentro de los próximos N días (ajuste de la
/// compañía). Un documento SIN fecha de vencimiento no avisa nunca. La clave de no repetición es la misma de los avisos de inventario (semana ISO + huella del conjunto).
/// </summary>
public static class FleetAlertRules
{
    public const int DefaultDocWarnDays = 30;
    public const int MaxDocWarnDays = 365;

    public const string DocWarnDaysRange = "Los días de aviso de documentos de la flota deben estar entre 1 y 365.";

    public static bool ValidDocWarnDays(int days) => days is >= 1 and <= MaxDocWarnDays;

    // ---------------------------------------------------------------- Lote 179 (G2, 2026-10-21): umbrales fijos (por ahora no se ajustan por compañía; ver docs/informe-autonomo-2026-10-21.md)

    /// <summary>Una orden de trabajo abierta (OPEN o IN_PROGRESS) por MÁS de estos días desde que se creó avisa.</summary>
    public const int WorkOrderStaleDays = 14;
    /// <summary>Un vehículo o chofer activo sin viaje (no cancelado) en MÁS de estos días avisa; uno nuevo cuenta desde su alta.</summary>
    public const int IdleDays = 14;
    /// <summary>Solo se evalúa la última carga de combustible si es de los últimos días; se compara contra el promedio de las anteriores (de 3 a 5 con rendimiento).</summary>
    public const int FuelWindowDays = 30;
    public const int FuelMinHistory = 3;
    public const int FuelHistoryMax = 5;
    /// <summary>Un km/L que se aparta más de este porcentaje del promedio de las cargas anteriores está fuera de rango.</summary>
    public const decimal FuelDeviationPct = 30m;

    /// <summary>¿Lleva abierta más de <paramref name="days"/> días? (el día de la creación cuenta como 0).</summary>
    public static bool IsWorkOrderStale(DateOnly createdDay, DateOnly today, int days = WorkOrderStaleDays) => today.DayNumber - createdDay.DayNumber > days;

    /// <summary>Días abierta.</summary>
    public static int DaysOpen(DateOnly createdDay, DateOnly today) => Math.Max(0, today.DayNumber - createdDay.DayNumber);

    /// <summary>
    /// ¿Está sin actividad? Su último viaje (o, si nunca tuvo, su alta) fue hace MÁS de <paramref name="days"/> días. Devuelve los días sin actividad o null si no está inactivo.
    /// </summary>
    public static int? IdleDaysOf(DateOnly? lastTripDay, DateOnly createdDay, DateOnly today, int days = IdleDays)
    {
        var since = lastTripDay is DateOnly t && t > createdDay ? t : createdDay;
        var idle = today.DayNumber - since.DayNumber;
        return idle > days ? idle : null;
    }

    /// <summary>
    /// Combustible fuera de rango: el km/L de la última carga contra el promedio de las anteriores (necesita al menos <see cref="FuelMinHistory"/>; se toman las últimas
    /// <see cref="FuelHistoryMax"/>). Devuelve (promedio, desviación en %, fuera) o null si no hay historia suficiente o el promedio es 0.
    /// </summary>
    public static (decimal Mean, decimal DeviationPct, bool Out)? FuelDeviation(decimal latestKmPerLiter, IReadOnlyList<decimal> previousKmPerLiter, decimal thresholdPct = FuelDeviationPct)
    {
        if (previousKmPerLiter.Count < FuelMinHistory) return null;
        var used = previousKmPerLiter.Skip(Math.Max(0, previousKmPerLiter.Count - FuelHistoryMax)).ToList();
        var mean = used.Average();
        if (mean <= 0m) return null;
        var dev = Math.Round((latestKmPerLiter - mean) / mean * 100m, 1);
        return (Math.Round(mean, 2), dev, Math.Abs(dev) > thresholdPct);
    }

    public static bool IsExpired(DateOnly? expiry, DateOnly today) => expiry is DateOnly e && InventoryAlertRules.IsExpired(e, today);

    public static bool IsExpiring(DateOnly? expiry, DateOnly today, int warnDays) => expiry is DateOnly e && InventoryAlertRules.IsExpiring(e, today, warnDays);

    /// <summary>Días que faltan para vencer (negativo = ya venció).</summary>
    public static int DaysToExpiry(DateOnly expiry, DateOnly today) => InventoryAlertRules.DaysToExpiry(expiry, today);
}
