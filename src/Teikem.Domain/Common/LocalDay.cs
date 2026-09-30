namespace Teikem.Domain.Common;

/// <summary>
/// Lote 14 — días locales de la compañía (decisión del dueño 2026-09-30: "hoy" empieza a medianoche en hora de Puerto Rico,
/// no en UTC). Reglas puras de conversión; el punto único que decide la zona de cada compañía es ITenantClock
/// (Infrastructure), que hoy usa <see cref="DefaultZone"/> y mañana leerá la zona configurada por compañía.
/// - Zona por defecto: America/Puerto_Rico (UTC−4 todo el año, sin horario de verano). En Windows el id es
///   "SA Western Standard Time"; se prueba primero el IANA (con ICU .NET lo traduce) y después el de Windows. Si el sistema no
///   tiene ninguno, se usa una zona fija UTC−4 equivalente.
/// - Un rango de días locales [desde, hasta] se convierte a UTC con 'desde' inclusivo (00:00 local) y 'hasta' EXCLUSIVO (00:00
///   local del día siguiente), igual que el Kárdex en UTC hasta el Lote 13.
/// - Las fechas que vienen de la base llegan con Kind Unspecified: se tratan como UTC.
/// </summary>
public static class LocalDay
{
    /// <summary>Zona por defecto de las compañías (IANA).</summary>
    public const string DefaultZoneId = "America/Puerto_Rico";
    /// <summary>Id de Windows de la misma zona (UTC−4 sin horario de verano).</summary>
    public const string DefaultWindowsZoneId = "SA Western Standard Time";
    /// <summary>Desfase de respaldo si el sistema no conoce ninguna de las dos zonas.</summary>
    public static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(-4);

    /// <summary>Zona por defecto resuelta una sola vez.</summary>
    public static TimeZoneInfo DefaultZone { get; } = ResolveZone(null);

    /// <summary>
    /// Zona por id (IANA o Windows); vacía o desconocida → la zona por defecto (Puerto Rico). Nunca devuelve null.
    /// </summary>
    public static TimeZoneInfo ResolveZone(string? zoneId)
    {
        foreach (var id in new[] { zoneId, DefaultZoneId, DefaultWindowsZoneId })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var tz)) return tz;
        }
        return TimeZoneInfo.CreateCustomTimeZone("AST-PR", FallbackOffset, "Hora del Atlántico (Puerto Rico)", "AST");
    }

    /// <summary>Instante UTC expresado en la hora local de la zona.</summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>Día local al que pertenece el instante UTC (p. ej. 2026-10-01 02:00 UTC es el 30/09 en Puerto Rico).</summary>
    public static DateOnly DayOf(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    /// <summary>"Hoy" en la zona a partir de la hora UTC actual.</summary>
    public static DateOnly Today(DateTime nowUtc, TimeZoneInfo zone) => DayOf(nowUtc, zone);

    /// <summary>
    /// Instante UTC de las 00:00 locales del día. En zonas con horario de verano cuyo salto cae a medianoche (la hora no
    /// existe), se toma el primer instante válido del día.
    /// </summary>
    public static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var guard = 0;
        while (zone.IsInvalidTime(local) && guard++ < 8) local = local.AddMinutes(30);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);
    }

    /// <summary>Rango UTC de días locales: desde inclusivo (00:00 local) y hasta EXCLUSIVO (00:00 local del día siguiente).</summary>
    public static (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to, TimeZoneInfo zone)
        => (from is DateOnly f ? StartOfDayUtc(f, zone) : null, to is DateOnly t ? StartOfDayUtc(t.AddDays(1), zone) : null);
}
