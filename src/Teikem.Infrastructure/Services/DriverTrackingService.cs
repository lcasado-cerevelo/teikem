using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>Valores GLOBALES del seguimiento (appsettings `DriverApp:Tracking`), iguales para todas las compañías. Se guarda por movimiento y no por reloj: la app envía un punto cuando el chofer se movió MinDistanceMeters o pasó MaxIntervalSeconds (y nunca con el teléfono quieto).</summary>
public sealed class DriverTrackingOptions
{
    public int MinDistanceMeters { get; set; } = 150;
    public int MaxIntervalSeconds { get; set; } = 90;
    public int UploadEverySeconds { get; set; } = 120;
    public int RetentionDays { get; set; } = 10;
}

/// <summary>
/// Seguimiento de la ruta del chofer (app del chofer, 2026-10-23). La app manda por LOTES (cada 2 minutos) los puntos que juntó; el servidor los guarda en DriverLocationPing —la misma tabla que ya lee el monitor—
/// ligados a la ruta EN CURSO del chofer en esa compañía. Si el chofer trabaja para dos compañías con rutas en curso, la app manda el lote a las dos (cada una con su token) y cada compañía guarda el suyo.
/// Los avisos (apagó el envío, perdió el permiso, lo reanudó) se guardan aparte para que el monitor distinga «lo apagó» de «sin señal». Todo se purga a los RetentionDays días.
/// </summary>
public sealed class DriverTrackingService(TeikemDbContext db, ITenantContext tenant, IOptions<DriverTrackingOptions> options, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;
    private DriverTrackingOptions O => options.Value;

    public DriverTrackingConfigDto Config() => new(O.MinDistanceMeters, O.MaxIntervalSeconds, O.UploadEverySeconds, O.RetentionDays);

    private async Task<Domain.Fleet.Driver> CurrentDriverAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        return await db.Drivers.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.IsActive, ct) ?? throw new ForbiddenException(DriverAppRules.NotADriverMessage);
    }

    /// <summary>
    /// Recibe un lote. 400 si pasa de 500 puntos o 50 avisos, o un aviso tiene un tipo inválido; 403 si el usuario no es un chofer. Sin ruta en curso en esta compañía, los puntos no se guardan (`RouteActive` false) pero los avisos sí.
    /// Idempotente: reenviar el mismo lote no repite nada.
    /// </summary>
    public async Task<DriverTrackingUploadResultDto> UploadAsync(DriverTrackingUploadRequest? req, CancellationToken ct)
    {
        var driver = await CurrentDriverAsync(ct);
        var points = req?.Points ?? Array.Empty<DriverTrackingPointDto>();
        var events = req?.Events ?? Array.Empty<DriverTrackingEventDto>();
        if (points.Count > DriverTrackingRules.MaxPointsPerBatch) throw new ValidationException("points", DriverTrackingRules.TooManyPointsMessage);
        if (events.Count > DriverTrackingRules.MaxEventsPerBatch) throw new ValidationException("events", DriverTrackingRules.TooManyEventsMessage);
        if (events.Any(e => !DriverTrackingRules.ValidEventType(e.Type))) throw new ValidationException("events", DriverTrackingRules.EventTypeInvalidMessage);

        var now = _clock.UtcNow;
        var tripId = await (from t in db.Trips.AsNoTracking()
                            join s in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals s.StatusCodeId
                            where t.DriverId == driver.DriverId && t.IsActive && s.InternalCode == TripStatuses.InProgress
                            orderby t.ActualStartUtc descending, t.TripId descending
                            select (int?)t.TripId).FirstOrDefaultAsync(ct);

        var good = points.Where(p => DriverTrackingRules.Acceptable(p.Latitude, p.Longitude, p.CapturedAtUtc, now, O.RetentionDays))
            .GroupBy(p => p.CapturedAtUtc).Select(g => g.First()).ToList();
        var accepted = 0;
        if (tripId is not null && good.Count > 0)
            accepted = await db.InsertPingsAsync(driver.DriverId, tripId, good.Select(p => new PingInput(p.Latitude, p.Longitude, p.SpeedKmh, p.HeadingDeg, DateTime.SpecifyKind(p.CapturedAtUtc, DateTimeKind.Utc))).ToList(), ct);

        var stored = 0;
        if (events.Count > 0)
        {
            var times = events.Select(e => e.OccurredAtUtc).ToList();
            var existing = await db.DriverTrackingEvents.AsNoTracking().Where(e => e.DriverId == driver.DriverId && times.Contains(e.OccurredAtUtc)).Select(e => new { e.EventType, e.OccurredAtUtc }).ToListAsync(ct);
            foreach (var e in events.GroupBy(x => (x.Type.Trim().ToUpperInvariant(), x.OccurredAtUtc)).Select(g => g.First()))
            {
                var type = e.Type.Trim().ToUpperInvariant();
                if (e.OccurredAtUtc > now + DriverTrackingRules.FutureTolerance || existing.Any(x => x.EventType == type && x.OccurredAtUtc == e.OccurredAtUtc)) continue;
                db.DriverTrackingEvents.Add(new DriverTrackingEvent
                {
                    TenantId = ((TenantContext)tenant).RequireTenantId(), DriverId = driver.DriverId, TripId = tripId, EventType = type,
                    OccurredAtUtc = DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc), ReceivedAtUtc = now,
                });
                stored++;
            }
            if (stored > 0) await db.SaveChangesAsync(ct);
        }
        return new DriverTrackingUploadResultDto(tripId is not null, accepted, points.Count - accepted, stored);
    }

    /// <summary>La huella de una ruta y sus avisos, para el monitor (la oficina). 404 si la ruta no existe.</summary>
    public async Task<TripTrackingDto> GetTripTrackAsync(Guid tripPublicId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().Where(t => t.PublicId == tripPublicId).Select(t => new { t.TripId, t.PublicId, t.Code, t.DriverId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Ruta", tripPublicId);
        var points = await db.TrackOfTripAsync(trip.TripId, ct);
        var events = await db.DriverTrackingEvents.AsNoTracking().Where(e => e.TripId == trip.TripId).OrderBy(e => e.OccurredAtUtc).Select(e => new DriverTrackingEventDto(e.EventType, e.OccurredAtUtc)).ToListAsync(ct);
        return new TripTrackingDto(trip.PublicId, trip.Code, points.Select(p => new TripTrackPointDto(p.Lat, p.Lng, p.SpeedKmh, p.CapturedAtUtc)).ToList(), events);
    }

    /// <summary>Retención: borra los puntos y avisos de esta compañía más viejos que RetentionDays (lo llama el trabajo programado).</summary>
    public async Task<(int Points, int Events)> PurgeAsync(CancellationToken ct)
    {
        var cutoff = _clock.UtcNow.AddDays(-Math.Max(1, O.RetentionDays));
        var points = await db.PurgePingsAsync(cutoff, ct);
        var events = db.Database.IsRelational() ? await db.DriverTrackingEvents.Where(e => e.OccurredAtUtc < cutoff).ExecuteDeleteAsync(ct) : 0;
        return (points, events);
    }
}

/// <summary>Trabajo programado: purga los puntos y avisos del seguimiento más viejos que la retención (10 días por omisión). Cada noche a las 03:30 (hora local).</summary>
public sealed class DriverTrackingPurgeJob : Jobs.IScheduledJob
{
    public string Key => Jobs.JobKeys.DriverTrackingPurge;
    public string NameEs => "Limpiar el seguimiento de rutas";
    public string NameEn => "Clean up route tracking";
    public string DescriptionEs => "Borra los puntos de ubicación y los avisos del seguimiento de choferes más viejos que la retención (10 días).";
    public string DescriptionEn => "Deletes driver location points and tracking notices older than the retention (10 days).";
    public string DefaultKind => Teikem.Domain.Constants.JobScheduleKinds.Daily;
    public int? DefaultIntervalSeconds => null;
    public TimeSpan? DefaultDailyAt => new(3, 30, 0);
    public bool DefaultEnabled => true;
    public TimeSpan MaxRuntime => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(Jobs.JobRunContext context, CancellationToken ct)
    {
        var svc = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DriverTrackingService>(context.Services);
        var (points, events) = await svc.PurgeAsync(ct);
        return points + events == 0 ? "Nada que limpiar." : $"{points} puntos y {events} avisos borrados.";
    }
}
