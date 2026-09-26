using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P7) — monitoreo de las rutas del día: despachadas o en curso (y completadas si se piden), con progreso sobre la
/// versión vigente, próxima ETA, pines aproximados, máximo de paradas del chofer y último ping.
/// - Solo lee. Todo parte de db.Trips (filtro global de tenant); Route y RouteStop (sin TenantId) se alcanzan SIEMPRE por un
///   JOIN con Trips ya filtrados, y las paradas además por db.TransportOrders (segunda barrera).
/// - Consultas por lote (sin N+1): una por tabla; los pings en UNA llamada a TripQueries.LastPingsAsync, que ya aplica el
///   respaldo del chofer (último ping sin TripId desde ActualStartUtc, LinkedToTrip = false).
/// - Los totales se calculan sobre la fecha y la zona ANTES de la búsqueda libre (MonitorRules): buscar nunca los cambia.
/// - 'Hoy' se toma en UTC; la fecha se puede indicar explícitamente. El mapa lo dibuja el front con GET /trips/{id}.
/// - Ningún DTO lleva montos (Amount/Rate).
/// </summary>
public sealed class TripMonitorService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private sealed record StatusInfo(string Code, string Label);

    public async Task<MonitorDto> ListAsync(MonitorQuery query, CancellationToken ct)
    {
        query ??= new MonitorQuery();
        var date = query.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var tripStatuses = await StatusMapAsync(StatusDomains.TripStatus, ct);
        var stopStatuses = await StatusMapAsync(StatusDomains.RouteStopStatus, ct);

        var wanted = new List<string> { TripStatuses.Dispatched, TripStatuses.InProgress };
        if (query.IncludeCompleted) wanted.Add(TripStatuses.Completed);
        var statusIds = tripStatuses.Where(kv => wanted.Contains(kv.Value.Code, StringComparer.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();

        var tripQuery = db.Trips.AsNoTracking()
            .Where(t => t.IsActive && t.PlanDate == date && statusIds.Contains(t.StatusCodeId));
        if (query.DispatchZoneId is int zoneFilter) tripQuery = tripQuery.Where(t => t.DispatchZoneId == zoneFilter);
        var trips = await tripQuery.ToListAsync(ct);
        if (trips.Count == 0)
            return new MonitorDto(date, new MonitorTotalsDto(0, 0, 0, 0, 0, 0, 0), Array.Empty<MonitorTripDto>());

        var tripIds = trips.Select(t => t.TripId).ToList();

        // Cabeceras: zonas, choferes y vehículos (bajo el filtro de tenant), una consulta cada uno.
        var zoneIds = trips.Where(t => t.DispatchZoneId.HasValue).Select(t => t.DispatchZoneId!.Value).Distinct().ToList();
        var zones = zoneIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.DispatchZones.AsNoTracking().Where(z => zoneIds.Contains(z.DispatchZoneId))
                .ToDictionaryAsync(z => z.DispatchZoneId, z => z.Code, ct);

        var driverIds = trips.Where(t => t.DriverId.HasValue).Select(t => t.DriverId!.Value).Distinct().ToList();
        var drivers = driverIds.Count == 0
            ? new Dictionary<int, (string Code, string Name, int? MaxStops)>()
            : (await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                    .Select(d => new { d.DriverId, d.EmployeeCode, d.FullName, d.MaxStopsPerRoute }).ToListAsync(ct))
                .ToDictionary(d => d.DriverId, d => (d.EmployeeCode, d.FullName, d.MaxStopsPerRoute));

        var vehicleIds = trips.Where(t => t.VehicleId.HasValue).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
                .ToDictionaryAsync(v => v.VehicleId, v => v.Code, ct);

        // Versión vigente de cada ruta (UX_Route_Trip_Active: a lo sumo una), alcanzada por el JOIN con Trips filtrados.
        var routes = (await (from r in db.Routes.AsNoTracking()
                             join t in db.Trips.AsNoTracking() on r.TripId equals t.TripId
                             where tripIds.Contains(t.TripId) && r.IsActive
                             select new { r.RouteId, r.TripId, r.Version }).ToListAsync(ct))
            .GroupBy(r => r.TripId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Version).First().RouteId);
        var routeIds = routes.Values.Distinct().ToList();

        // Paradas de las versiones vigentes, con su OrderStop (precisión del pin) y la orden del tenant como segunda barrera.
        var stopRows = routeIds.Count == 0
            ? new List<StopRow>()
            : await (from rs in db.RouteStops.AsNoTracking()
                     join os in db.OrderStops.AsNoTracking() on rs.OrderStopId equals os.OrderStopId
                     join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                     where routeIds.Contains(rs.RouteId)
                     select new StopRow(rs.RouteId, rs.OrderStopId, rs.StatusCodeId, rs.PlannedArrivalUtc, os.GeocodeAccuracyLookupId))
                .ToListAsync(ct);
        var stopsByRoute = stopRows.GroupBy(s => s.RouteId).ToDictionary(g => g.Key, g => g.ToList());

        // Coordenadas (GEOGRAPHY, SQL crudo confinado en TripQueries con TenantId): solo importa si la parada tiene pin.
        var orderStopIds = stopRows.Select(s => s.OrderStopId).Distinct().ToList();
        var withPoint = orderStopIds.Count == 0
            ? new HashSet<int>()
            : (await db.StopPointsAsync(orderStopIds, ct)).Keys.ToHashSet();

        // Último ping por ruta, en UNA llamada (con el respaldo del chofer desde la salida).
        var keys = trips.Select(t => new TripPingKey(t.TripId, t.DriverId, t.ActualStartUtc)).ToList();
        var pings = await db.LastPingsAsync(keys, ct);

        var tenantDefault = await TenantMaxStopsDefaultAsync(ct);

        var items = new List<(MonitorTripDto Dto, MonitorTripFacts Facts)>(trips.Count);
        foreach (var t in trips)
        {
            var stops = routes.TryGetValue(t.TripId, out var routeId) ? stopsByRoute.GetValueOrDefault(routeId) ?? new List<StopRow>() : new List<StopRow>();
            var inputs = new List<MonitorStopInput>(stops.Count);
            foreach (var s in stops)
            {
                var accuracy = s.GeocodeAccuracyLookupId is int accId ? await lookups.GetAsync(accId, ct) : null;
                inputs.Add(new MonitorStopInput(stopStatuses.GetValueOrDefault(s.StatusCodeId)?.Code, s.PlannedArrivalUtc,
                    withPoint.Contains(s.OrderStopId), accuracy?.InternalCode));
            }

            var progress = MonitorRules.Progress(inputs.Select(i => i.StatusCode));
            (string Code, string Name, int? MaxStops)? driver = t.DriverId is int dId && drivers.TryGetValue(dId, out var d) ? d : null;
            int? effectiveMax = driver is null ? null : FleetRules.EffectiveMaxStops(driver.Value.MaxStops, tenantDefault);
            var overLimit = TripRules.OverStopLimit(progress.Total, effectiveMax);
            var status = tripStatuses.GetValueOrDefault(t.StatusCodeId);

            DriverPingDto? ping = null;
            if (pings.TryGetValue(t.TripId, out var p))
                ping = new DriverPingDto(new GeoPointDto(p.Lat, p.Lng), p.SpeedKmh, p.HeadingDeg, p.CapturedAtUtc, p.ReceivedAtUtc, p.LinkedToTrip);

            var dto = new MonitorTripDto(
                PublicId: t.PublicId,
                Code: t.Code,
                PlanDate: t.PlanDate,
                ZoneCode: t.DispatchZoneId is int zId ? zones.GetValueOrDefault(zId) : null,
                DriverCode: driver?.Code,
                DriverName: driver?.Name,
                VehicleCode: t.VehicleId is int vId ? vehicles.GetValueOrDefault(vId) : null,
                StatusCode: status?.Code ?? string.Empty,
                Status: status?.Label ?? string.Empty,
                TotalStops: progress.Total,
                CompletedStops: progress.Completed,
                FailedStops: progress.Failed,
                PendingStops: progress.Pending,
                ApproximateStops: MonitorRules.ApproximateCount(inputs),
                NextEtaUtc: MonitorRules.NextEta(inputs),
                PlannedEndUtc: t.PlannedEndUtc,
                OverStopLimit: overLimit,
                LastPing: ping);
            items.Add((dto, new MonitorTripFacts(progress.Total, progress.Completed, progress.Failed, progress.Pending, overLimit, ping is not null)));
        }

        // Totales sobre la fecha y la zona; la búsqueda solo filtra la lista que se devuelve.
        var ordered = items
            .OrderBy(i => i.Dto.ZoneCode ?? "￿", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Dto.Code, StringComparer.Ordinal)
            .ToList();
        var (totals, visibleItems) = MonitorRules.Compose(ordered, i => i.Facts, query.Search,
            i => new[] { i.Dto.Code, i.Dto.ZoneCode, i.Dto.DriverCode, i.Dto.DriverName, i.Dto.VehicleCode, i.Dto.StatusCode, i.Dto.Status });
        var visible = visibleItems.Select(i => i.Dto).ToList();

        return new MonitorDto(date,
            new MonitorTotalsDto(totals.Trips, totals.TotalStops, totals.CompletedStops, totals.FailedStops, totals.PendingStops,
                totals.OverStopLimitTrips, totals.TripsWithoutPing),
            visible);
    }

    // ================================================================ helpers

    private sealed record StopRow(int RouteId, int OrderStopId, int StatusCodeId, DateTime? PlannedArrivalUtc, int? GeocodeAccuracyLookupId);

    private async Task<int?> TenantMaxStopsDefaultAsync(CancellationToken ct)
    {
        if (tenant.TenantId is not int tenantId) return null;
        return await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId)
            .Select(t => (int?)t.MaxStopsPerRouteDefault).FirstOrDefaultAsync(ct);
    }

    /// <summary>Estatus de un dominio con la etiqueta personalizada del tenant (StatusCodeOverride), por StatusCodeId.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId))
            .ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(s.LabelJson, overrides.GetValueOrDefault(s.StatusCodeId)?.CustomLabelJson), tenant.Lang)));
    }
}
