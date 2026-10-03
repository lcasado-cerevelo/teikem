using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Common;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>
/// Lote 14 — PUNTO ÚNICO de "hoy" y de los días locales de la compañía (decisión del dueño 2026-09-30: hora de Puerto Rico).
/// Todo filtro o resumen "por día" del lote (Kárdex, resumen, fecha de detección de descuadres) y los que vengan (lo cambiado
/// del Conteo, el Pulso del Lote 15) convierten sus fechas con este reloj, nunca con DateTime.UtcNow.Date.
/// Región y formatos (2026-10): la zona sale de Tenant.TimeZoneId del tenant del contexto (<see cref="ContextTenantClock"/>, el
/// registrado en DI); sin tenant en contexto (jobs, seed, pruebas) se usa la zona por defecto (LocalDay.DefaultZone,
/// America/Puerto_Rico). Quien lo usa no cambia.
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
/// Reloj con una zona FIJA: <see cref="Default"/> (Puerto Rico) es el respaldo sin tenant y el que registran las pruebas; las
/// pruebas crean uno con zona y hora fijas. El de la aplicación es <see cref="ContextTenantClock"/> (zona de cada compañía).
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

/// <summary>
/// Región y formatos (2026-10) — caché de la zona horaria por compañía (singleton sobre IMemoryCache): TenantId → TimeZoneInfo.
/// La llena <see cref="ContextTenantClock"/> la primera vez que una compañía pide su zona; TenantService la invalida al guardar
/// los ajustes (cambio de zona) y caduca sola a los <see cref="Lifetime"/> por si hay más de una instancia del API.
/// </summary>
public sealed class TenantZoneCache(IMemoryCache cache)
{
    /// <summary>Vida máxima de una zona en caché (las demás instancias del API ven el cambio a más tardar en este tiempo).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private static string Key(int tenantId) => $"tenant-zone:{tenantId}";

    /// <summary>
    /// Zona de la compañía; si no está en caché la lee con <paramref name="loadZoneId"/> (id IANA o de Windows guardado) y la
    /// resuelve con LocalDay.ResolveZone: vacía o desconocida → zona por defecto, nunca null.
    /// </summary>
    public TimeZoneInfo GetOrLoad(int tenantId, Func<int, string?> loadZoneId)
    {
        if (cache.TryGetValue(Key(tenantId), out TimeZoneInfo? zone) && zone is not null) return zone;
        zone = LocalDay.ResolveZone(loadZoneId(tenantId));
        cache.Set(Key(tenantId), zone, Lifetime);
        return zone;
    }

    /// <summary>Olvida la zona de la compañía (la próxima lectura la vuelve a cargar).</summary>
    public void Invalidate(int tenantId) => cache.Remove(Key(tenantId));
}

/// <summary>
/// Región y formatos (2026-10) — reloj de la compañía DEL CONTEXTO (scoped): la zona es Tenant.TimeZoneId del TenantId que fijó
/// el middleware desde el principal (JWT `tid`, nunca del request) o un seeder/job con TenantContext.As. Se evalúa en cada
/// llamada, así que un job que recorre compañías con As(...) obtiene la zona de cada una. Sin tenant → LocalDay.DefaultZone.
/// La zona se lee de la base una vez por compañía (TenantZoneCache) con el DbContext del alcance, dentro de su misma conexión y
/// transacción: no espera bloqueos de otra conexión.
/// </summary>
public sealed class ContextTenantClock : ITenantClock
{
    private readonly ITenantContext _tenant;
    private readonly TeikemDbContext _db;
    private readonly TenantZoneCache _zones;
    private readonly Func<DateTime> _utcNow;

    public ContextTenantClock(ITenantContext tenant, TeikemDbContext db, TenantZoneCache zones)
        : this(tenant, db, zones, null) { }

    /// <summary>Para pruebas: hora UTC fija.</summary>
    public ContextTenantClock(ITenantContext tenant, TeikemDbContext db, TenantZoneCache zones, Func<DateTime>? utcNow)
    {
        _tenant = tenant;
        _db = db;
        _zones = zones;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TimeZoneInfo Zone => _tenant.TenantId is int tenantId ? _zones.GetOrLoad(tenantId, LoadZoneId) : LocalDay.DefaultZone;
    public DateTime UtcNow => DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc);
    public DateOnly Today => LocalDay.Today(UtcNow, Zone);
    public DateTime StartOfDayUtc(DateOnly day) => LocalDay.StartOfDayUtc(day, Zone);
    public DateOnly DayOf(DateTime utc) => LocalDay.DayOf(utc, Zone);
    public (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to) => LocalDay.UtcRange(from, to, Zone);

    /// <summary>Id de zona guardado (Tenant no lleva filtro de tenant; se ignora el de inactivas: una compañía inactiva conserva su zona).</summary>
    private string? LoadZoneId(int tenantId)
        => _db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.TimeZoneId).FirstOrDefault();
}
