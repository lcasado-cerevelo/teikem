using System.Text.RegularExpressions;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Analytics;

/// <summary>Cuándo se envía un informe por correo: todos los días, un día de la semana o un día del mes, a una hora local de la compañía.</summary>
public sealed record ReportSchedule(string Kind, int Hour, int Minute, int DayOfWeek, int DayOfMonth)
{
    public const string Daily = "DAILY";
    public const string Weekly = "WEEKLY";
    public const string Monthly = "MONTHLY";
}

/// <summary>
/// Lote 92 (P12) — informes programados por correo. La programación vive en `ReportDefinition.ScheduleCron` con una forma de cron restringida de 5 campos (minuto hora día-del-mes mes día-de-la-semana, mes siempre `*`):
/// `M H * * *` = todos los días; `M H * * D` = semanal (D de 0 a 6 con 0 = domingo; el 7 también vale domingo); `M H D * *` = mensual (D de 1 a 28, para que el día exista todos los meses). Los destinatarios van en
/// `DeliveryEmails`, separados por coma o punto y coma. La hora es la local de la compañía. Reglas puras, sin BD.
/// </summary>
public static partial class ReportScheduleRules
{
    public const int MaxRecipients = 10;
    public const int MaxEmailsLength = 400;
    public const int MaxRows = 200;

    public const string InvalidScheduleMessage = "La programación debe ser diaria, semanal (día 0 a 6) o mensual (día 1 a 28) con una hora válida.";
    public const string ScheduleNeedsEmailsMessage = "Una programación necesita al menos un correo destinatario.";
    public const string EmailsNeedScheduleMessage = "Los correos destinatarios necesitan una programación.";
    public const string TooManyRecipientsMessage = "Máximo 10 correos destinatarios.";
    public const string InvalidRangeMessage = "El período del informe enviado debe ser Últimos 7 días, Últimos 30 días, Este mes o Todo.";
    public const string SystemReportMessage = "Los informes de sistema no se programan; guarde una copia propia.";
    public const string NoOwnerMessage = "El informe no tiene dueño para correrlo.";
    public const string NoScheduleMessage = "El informe no tiene programación ni correos destinatarios.";
    public static string InvalidEmailMessage(string email) => $"El correo '{email}' no es válido.";

    /// <summary>Estatus del último envío (ReportDefinition.LastDeliveryStatus).</summary>
    public const string StatusSent = "SENT";
    public const string StatusSkipped = "SKIPPED";
    public const string StatusFailed = "FAILED";

    /// <summary>Períodos que se pueden enviar: lo que tiene sentido para un informe que llega solo. El personalizado no (no hay a quién preguntarle las fechas).</summary>
    public static readonly IReadOnlyList<string> Ranges = new[] { DateRangeModes.Last7, DateRangeModes.Last30, DateRangeModes.ThisMonth, DateRangeModes.All };
    public const string DefaultRange = DateRangeModes.Last7;

    [GeneratedRegex(@"^(\d{1,2}) (\d{1,2}) (\*|\d{1,2}) \* (\*|\d)$")]
    private static partial Regex CronRegex();

    [GeneratedRegex(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$")]
    private static partial Regex EmailRegex();

    public static string NormalizeRange(string? mode)
    {
        var m = string.IsNullOrWhiteSpace(mode) ? DefaultRange : mode.Trim().ToUpperInvariant();
        return Ranges.Contains(m) ? m : throw new FormatException(InvalidRangeMessage);
    }

    public static bool IsValidRange(string? mode) => string.IsNullOrWhiteSpace(mode) || Ranges.Contains(mode.Trim().ToUpperInvariant());

    /// <summary>Lee un cron restringido; null si no es válido o está vacío.</summary>
    public static ReportSchedule? TryParse(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return null;
        var m = CronRegex().Match(cron.Trim());
        if (!m.Success) return null;
        var minute = int.Parse(m.Groups[1].Value);
        var hour = int.Parse(m.Groups[2].Value);
        if (minute > 59 || hour > 23) return null;
        var dom = m.Groups[3].Value;
        var dow = m.Groups[4].Value;
        if (dom == "*" && dow == "*") return new ReportSchedule(ReportSchedule.Daily, hour, minute, 0, 0);
        if (dom == "*")
        {
            var d = int.Parse(dow);
            return d > 7 ? null : new ReportSchedule(ReportSchedule.Weekly, hour, minute, d % 7, 0);
        }
        if (dow != "*") return null;
        var day = int.Parse(dom);
        return day is >= 1 and <= 28 ? new ReportSchedule(ReportSchedule.Monthly, hour, minute, 0, day) : null;
    }

    /// <summary>El cron de una programación (para guardarla desde la pantalla).</summary>
    public static string Build(ReportSchedule s) => s.Kind switch
    {
        ReportSchedule.Weekly => $"{s.Minute} {s.Hour} * * {s.DayOfWeek}",
        ReportSchedule.Monthly => $"{s.Minute} {s.Hour} {s.DayOfMonth} * *",
        _ => $"{s.Minute} {s.Hour} * * *",
    };

    /// <summary>Correos válidos y distintos (sin distinguir mayúsculas). Un valor mal escrito lanza FormatException con el mensaje exacto; null o vacío = ninguno.</summary>
    public static IReadOnlyList<string> ParseEmails(string? emails)
    {
        if (string.IsNullOrWhiteSpace(emails)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var raw in emails.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length > 200 || !EmailRegex().IsMatch(raw)) throw new FormatException(InvalidEmailMessage(raw));
            if (!list.Contains(raw, StringComparer.OrdinalIgnoreCase)) list.Add(raw);
        }
        if (list.Count > MaxRecipients) throw new FormatException(TooManyRecipientsMessage);
        return list;
    }

    /// <summary>La última vez (hora local) en que la programación debió enviarse, a más tardar `localNow`.</summary>
    public static DateTime LastOccurrenceLocal(ReportSchedule s, DateTime localNow)
    {
        var at = localNow.Date.AddHours(s.Hour).AddMinutes(s.Minute);
        switch (s.Kind)
        {
            case ReportSchedule.Weekly:
            {
                var back = ((int)localNow.DayOfWeek - s.DayOfWeek + 7) % 7;
                at = at.AddDays(-back);
                break;
            }
            case ReportSchedule.Monthly:
            {
                at = new DateTime(localNow.Year, localNow.Month, s.DayOfMonth, s.Hour, s.Minute, 0);
                if (at > localNow) { var prev = localNow.AddMonths(-1); at = new DateTime(prev.Year, prev.Month, s.DayOfMonth, s.Hour, s.Minute, 0); }
                return at;
            }
        }
        return at > localNow ? at.AddDays(s.Kind == ReportSchedule.Weekly ? -7 : -1) : at;
    }

    /// <summary>
    /// ¿Toca enviar? Sí si la última ocurrencia de la programación (a la hora local de la compañía) cae después del último envío (`lastDeliveryUtc`) y ya pasó. Sin último envío no toca: el servicio
    /// lo fija al guardar la programación, así una programación nueva no dispara una ocurrencia anterior a que se creara. Devuelve la ocurrencia (UTC) para registrarla.
    /// </summary>
    public static DateTime? DueOccurrenceUtc(ReportSchedule s, DateTime? lastDeliveryUtc, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (lastDeliveryUtc is null) return null;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var local = LastOccurrenceLocal(s, localNow);
        var occurrence = zone.IsInvalidTime(local) ? local.AddHours(1) : local;
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(occurrence, DateTimeKind.Unspecified), zone);
        return utc > DateTime.SpecifyKind(lastDeliveryUtc.Value, DateTimeKind.Utc) && utc <= nowUtc ? utc : null;
    }

    /// <summary>Valida el par programación + destinatarios + período al guardar un informe; lanza FormatException con el mensaje exacto (el servicio lo convierte en 400).</summary>
    public static (string? Cron, string? Emails, string? Range) ValidateForSave(string? cron, string? emails, string? range)
    {
        var hasCron = !string.IsNullOrWhiteSpace(cron);
        var list = ParseEmails(emails);
        if (hasCron && TryParse(cron) is null) throw new FormatException(InvalidScheduleMessage);
        if (hasCron && list.Count == 0) throw new FormatException(ScheduleNeedsEmailsMessage);
        if (!hasCron && list.Count > 0) throw new FormatException(EmailsNeedScheduleMessage);
        if (!IsValidRange(range)) throw new FormatException(InvalidRangeMessage);
        return (hasCron ? cron!.Trim() : null, list.Count == 0 ? null : string.Join(", ", list), hasCron ? NormalizeRange(range) : null);
    }
}
