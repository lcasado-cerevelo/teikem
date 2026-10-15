using System.Globalization;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Jobs;

/// <summary>
/// Lote 50 (P2.2) — reglas PURAS del horario de un trabajo programado (sin base de datos ni reloj propio: reciben la hora).
/// - INTERVAL: cada N segundos (60 s a 7 días); la siguiente corrida cuenta desde que TERMINÓ la anterior.
/// - DAILY: una vez al día a la hora local HH:mm de la compañía (hoy America/Puerto_Rico, LocalDay.DefaultZone).
/// - Falla: se reintenta pronto (1, 2 y 5 min) hasta 3 fallas seguidas; después vuelve a su horario normal.
/// </summary>
public static class JobSchedule
{
    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 604_800;   // 7 días
    public const int MaxQuickRetries = 3;
    public static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5) };

    public const string JobNotFoundMessage = "Trabajo no encontrado.";
    public const string IntervalRangeMessage = "El intervalo debe estar entre 60 segundos y 7 días (604800).";
    public const string DailyAtRequiredMessage = "Indique la hora diaria (HH:mm).";
    public const string DailyAtInvalidMessage = "La hora diaria no es válida; use HH:mm (24 horas).";
    public const string AlreadyRunningMessage = "El trabajo ya se está ejecutando; espere a que termine.";

    public static string UnknownKindMessage(string? kind) => $"Tipo de horario desconocido: '{kind}'. Use INTERVAL o DAILY.";

    /// <summary>Normaliza y valida el horario. Devuelve el error (mensaje + campo) o null.</summary>
    public static (string? Kind, int? IntervalSeconds, TimeSpan? DailyAt, string? Field, string? Error) Validate(string? kind, int? intervalSeconds, string? dailyAt)
    {
        var k = (kind ?? string.Empty).Trim().ToUpperInvariant();
        if (k == JobScheduleKinds.Interval)
        {
            if (intervalSeconds is null or < MinIntervalSeconds or > MaxIntervalSeconds)
                return (null, null, null, "intervalSeconds", IntervalRangeMessage);
            return (k, intervalSeconds, null, null, null);
        }
        if (k == JobScheduleKinds.Daily)
        {
            if (string.IsNullOrWhiteSpace(dailyAt)) return (null, null, null, "dailyAt", DailyAtRequiredMessage);
            if (!TimeSpan.TryParseExact(dailyAt.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var t) || t >= TimeSpan.FromDays(1))
                return (null, null, null, "dailyAt", DailyAtInvalidMessage);
            return (k, null, t, null, null);
        }
        return (null, null, null, "scheduleKind", UnknownKindMessage(kind));
    }

    /// <summary>Próxima corrida normal a partir de un instante (fin de la corrida o momento de guardar el horario).</summary>
    public static DateTime Next(string kind, int? intervalSeconds, TimeSpan? dailyAt, DateTime fromUtc, TimeZoneInfo? zone = null)
    {
        if (kind == JobScheduleKinds.Interval) return fromUtc.AddSeconds(intervalSeconds ?? MinIntervalSeconds);
        zone ??= LocalDay.DefaultZone;
        var local = LocalDay.ToLocal(fromUtc, zone);
        var candidate = local.Date + (dailyAt ?? TimeSpan.Zero);
        if (candidate <= local) candidate = candidate.AddDays(1);
        // Un horario local inexistente (salto de verano) se corre una hora después.
        if (zone.IsInvalidTime(candidate)) candidate = candidate.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone);
    }

    /// <summary>Próxima corrida tras una falla: reintento rápido mientras no pasen MaxQuickRetries fallas seguidas; después, el horario normal.</summary>
    public static DateTime NextAfterFailure(string kind, int? intervalSeconds, TimeSpan? dailyAt, int consecutiveFailures, DateTime nowUtc, TimeZoneInfo? zone = null)
        => consecutiveFailures is >= 1 and <= MaxQuickRetries
            ? nowUtc + RetryDelays[consecutiveFailures - 1]
            : Next(kind, intervalSeconds, dailyAt, nowUtc, zone);
}
