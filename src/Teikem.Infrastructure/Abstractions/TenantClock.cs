using Teikem.Domain.Common;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Lote 14 — PUNTO ÚNICO de "hoy" y de los días locales de la compañía (decisión del dueño 2026-09-30: hora de Puerto Rico).
/// Todo filtro o resumen "por día" del lote (Kárdex, resumen, fecha de detección de descuadres) y los que vengan (lo cambiado
/// del Conteo, el Pulso del Lote 15) convierten sus fechas con este reloj, nunca con DateTime.UtcNow.Date.
/// Hoy todas las compañías usan la zona por defecto (LocalDay.DefaultZone, America/Puerto_Rico); cuando la compañía guarde su
/// zona, la implementación la leerá aquí (del tenant del contexto) sin tocar a los que la usan.
/// </summary>
public interface ITenantClock
{
    /// <summary>Zona horaria de la compañía.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>Instante actual en UTC.</summary>
    DateTime UtcNow { get; }

    /// <summary>"Hoy" en la zona de la compañía.</summary>
    DateOnly Today { get; }

    /// <summary>Instante UTC de las 00:00 locales del día.</summary>
    DateTime StartOfDayUtc(DateOnly day);

    /// <summary>Día local al que pertenece el instante UTC.</summary>
    DateOnly DayOf(DateTime utc);

    /// <summary>Rango UTC de días locales: desde inclusivo y hasta EXCLUSIVO (00:00 local del día siguiente).</summary>
    (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to);
}

/// <summary>
/// Reloj de la compañía con la zona por defecto (Puerto Rico). Se registra como singleton; las pruebas crean uno con zona y
/// hora fijas.
/// </summary>
public sealed class TenantClock : ITenantClock
{
    /// <summary>Reloj real con la zona por defecto.</summary>
    public static readonly TenantClock Default = new(LocalDay.DefaultZone, null);

    private readonly Func<DateTime> _utcNow;

    public TenantClock(TimeZoneInfo zone, Func<DateTime>? utcNow)
    {
        Zone = zone ?? LocalDay.DefaultZone;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TimeZoneInfo Zone { get; }
    public DateTime UtcNow => DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc);
    public DateOnly Today => LocalDay.Today(UtcNow, Zone);
    public DateTime StartOfDayUtc(DateOnly day) => LocalDay.StartOfDayUtc(day, Zone);
    public DateOnly DayOf(DateTime utc) => LocalDay.DayOf(utc, Zone);
    public (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to) => LocalDay.UtcRange(from, to, Zone);
}
