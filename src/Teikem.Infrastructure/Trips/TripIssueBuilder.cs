using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Trips;

/// <summary>
/// Lote 5 (P0) — avisos y bloqueantes de las rutas (ficha, selector de despacho, despacho, reasignación y 'Planificar el
/// día'), por lote y sin N+1: una consulta por tabla para todas las rutas pedidas, la disponibilidad del Lote 4 en la fecha
/// de cada ruta (IFleetAvailabilityService) y la elegibilidad de las órdenes vigentes (RouteWriter.CheckEligibilityAsync).
/// Las reglas (qué bloquea, qué avisa y con qué mensaje) viven en TripRules.BuildIssues. Solo lee.
/// </summary>
public sealed class TripIssueBuilder(TeikemDbContext db, IFleetAvailabilityService availability, RouteWriter writer, ILookupCache lookups)
{
    /// <summary>Con más recursos que este umbral por fecha, la disponibilidad se evalúa con una sola lectura del tenant.</summary>
    private const int PerResourceThreshold = 4;

    public async Task<Dictionary<int, IReadOnlyList<TripIssue>>> BuildAsync(IReadOnlyCollection<int> tripIds, bool includeOrderEligibility, CancellationToken ct)
    {
        var result = new Dictionary<int, IReadOnlyList<TripIssue>>();
        var ids = (tripIds ?? Array.Empty<int>()).Distinct().ToList();
        if (ids.Count == 0) return result;

        var trips = await db.Trips.AsNoTracking().Where(t => ids.Contains(t.TripId)).ToListAsync(ct);
        if (trips.Count == 0) return result;

        // Choferes (máximo de paradas) y vehículos (capacidad).
        var driverIds = trips.Where(t => t.DriverId.HasValue).Select(t => t.DriverId!.Value).Distinct().ToList();
        var vehicleIds = trips.Where(t => t.VehicleId.HasValue).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var driverMax = driverIds.Count == 0
            ? new Dictionary<int, int?>()
            : await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                .ToDictionaryAsync(d => d.DriverId, d => d.MaxStopsPerRoute, ct);
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<int, VehicleCap>()
            : (await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
                    .Select(v => new VehicleCap(v.VehicleId, v.MaxStops, v.MaxWeightKg, v.MaxVolumeM3)).ToListAsync(ct))
                .ToDictionary(v => v.VehicleId);
        var tenantIds = trips.Select(t => t.TenantId).Distinct().ToList();
        var tenantDefaults = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.TenantId))
            .ToDictionaryAsync(t => t.TenantId, t => (int?)t.MaxStopsPerRouteDefault, ct);

        // Versión vigente de cada ruta y sus paradas con ventana, carga de la orden y precisión del pin.
        var routes = (await db.Routes.AsNoTracking().Where(r => ids.Contains(r.TripId) && r.IsActive)
                .Select(r => new { r.RouteId, r.TripId, r.Version }).ToListAsync(ct))
            .GroupBy(r => r.TripId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Version).First().RouteId);
        var routeIds = routes.Values.ToList();
        var stops = routeIds.Count == 0
            ? new List<StopRow>()
            : await (from rs in db.RouteStops.AsNoTracking()
                     join os in db.OrderStops.AsNoTracking() on rs.OrderStopId equals os.OrderStopId
                     join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                     where routeIds.Contains(rs.RouteId)
                     select new StopRow(rs.RouteId, rs.OrderStopId, rs.PlannedArrivalUtc, os.WindowEndUtc, os.GeocodeAccuracyLookupId,
                         o.TotalWeightKg, o.TotalVolumeM3))
                .ToListAsync(ct);
        var stopsByRoute = stops.GroupBy(s => s.RouteId).ToDictionary(g => g.Key, g => g.ToList());
        var points = await db.StopPointsAsync(stops.Select(s => s.OrderStopId).Distinct().ToList(), ct);
        var accuracyCodes = new Dictionary<int, string?>();
        foreach (var accId in stops.Where(s => s.GeocodeAccuracyLookupId.HasValue).Select(s => s.GeocodeAccuracyLookupId!.Value).Distinct())
            accuracyCodes[accId] = (await lookups.GetAsync(accId, ct))?.InternalCode;

        // Otras rutas activas del mismo día con el mismo chofer o vehículo (doble asignación: solo avisa).
        var dates = trips.Select(t => t.PlanDate).Distinct().ToList();
        var sameDay = (driverIds.Count == 0 && vehicleIds.Count == 0)
            ? new List<OtherTrip>()
            : await db.Trips.AsNoTracking()
                .Where(t => t.IsActive && dates.Contains(t.PlanDate)
                            && ((t.DriverId != null && driverIds.Contains(t.DriverId.Value)) || (t.VehicleId != null && vehicleIds.Contains(t.VehicleId.Value))))
                .Select(t => new OtherTrip(t.TripId, t.Code, t.PlanDate, t.DriverId, t.VehicleId))
                .ToListAsync(ct);

        // Disponibilidad (Lote 4) en la fecha de cada ruta.
        var driverAvailability = new Dictionary<(int, DateOnly), AvailabilityResult>();
        var vehicleAvailability = new Dictionary<(int, DateOnly), AvailabilityResult>();
        foreach (var date in dates)
        {
            var dayDrivers = trips.Where(t => t.PlanDate == date && t.DriverId.HasValue).Select(t => t.DriverId!.Value).Distinct().ToList();
            var dayVehicles = trips.Where(t => t.PlanDate == date && t.VehicleId.HasValue).Select(t => t.VehicleId!.Value).Distinct().ToList();
            if (dayDrivers.Count + dayVehicles.Count > PerResourceThreshold)
            {
                var all = await availability.GetAsync(date, false, ct);
                foreach (var d in all.Drivers.Where(d => dayDrivers.Contains(d.Id)))
                    driverAvailability[(d.Id, date)] = new AvailabilityResult(d.Available, d.Issues.Select(i => new AvailabilityIssue(i.Code, i.Message, i.Blocking)).ToList());
                foreach (var v in all.Vehicles.Where(v => dayVehicles.Contains(v.Id)))
                    vehicleAvailability[(v.Id, date)] = new AvailabilityResult(v.Available, v.Issues.Select(i => new AvailabilityIssue(i.Code, i.Message, i.Blocking)).ToList());
            }
            // Los que no vinieron en la lectura del tenant (dados de baja definitiva) o pocos recursos: uno por uno.
            foreach (var id in dayDrivers.Where(id => !driverAvailability.ContainsKey((id, date))))
                driverAvailability[(id, date)] = await availability.CheckDriverAsync(id, date, ct);
            foreach (var id in dayVehicles.Where(id => !vehicleAvailability.ContainsKey((id, date))))
                vehicleAvailability[(id, date)] = await availability.CheckVehicleAsync(id, date, ct);
        }

        // Elegibilidad de las órdenes vigentes (solo si se pide: ruta editable, selector y despacho).
        var ineligibleByTrip = new Dictionary<int, List<OrderIneligibility>>();
        if (includeOrderEligibility)
        {
            var links = await db.TripOrders.AsNoTracking().Where(x => ids.Contains(x.TripId) && x.IsCurrent)
                .Select(x => new { x.TripId, x.TransportOrderId }).ToListAsync(ct);
            var orderIds = links.Select(l => l.TransportOrderId).Distinct().ToList();
            if (orderIds.Count > 0)
            {
                var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).ToListAsync(ct);
                var reasons = await writer.CheckEligibilityAsync(orders, ct);
                var numbers = orders.ToDictionary(o => o.TransportOrderId, o => o.OrderNumber);
                foreach (var l in links.Where(l => reasons.ContainsKey(l.TransportOrderId)).OrderBy(l => numbers.GetValueOrDefault(l.TransportOrderId), StringComparer.Ordinal))
                {
                    if (!ineligibleByTrip.TryGetValue(l.TripId, out var list)) ineligibleByTrip[l.TripId] = list = new List<OrderIneligibility>();
                    list.Add(new OrderIneligibility(numbers.GetValueOrDefault(l.TransportOrderId) ?? string.Empty, reasons[l.TransportOrderId]));
                }
            }
        }

        foreach (var t in trips)
        {
            var routeStops = routes.TryGetValue(t.TripId, out var routeId) ? stopsByRoute.GetValueOrDefault(routeId) ?? new List<StopRow>() : new List<StopRow>();
            var vehicle = t.VehicleId is int vid ? vehicles.GetValueOrDefault(vid) : null;
            int? effectiveMax = t.DriverId is int did
                ? FleetRules.EffectiveMaxStops(driverMax.GetValueOrDefault(did), tenantDefaults.GetValueOrDefault(t.TenantId))
                : null;
            var late = routeStops.Count(s => s.PlannedArrivalUtc is DateTime a && s.WindowEndUtc is DateTime e && a > e);
            var approximate = routeStops.Count(s => IsApproximate(points.ContainsKey(s.OrderStopId),
                s.GeocodeAccuracyLookupId is int acc ? accuracyCodes.GetValueOrDefault(acc) : null));

            var input = new TripIssueInput(
                TripCode: t.Code,
                HasDriver: t.DriverId.HasValue,
                HasVehicle: t.VehicleId.HasValue,
                StopCount: routeStops.Count,
                DriverAvailability: t.DriverId is int d1 ? driverAvailability.GetValueOrDefault((d1, t.PlanDate)) : null,
                VehicleAvailability: t.VehicleId is int v1 ? vehicleAvailability.GetValueOrDefault((v1, t.PlanDate)) : null,
                EffectiveMaxStops: effectiveMax,
                VehicleMaxStops: vehicle?.MaxStops,
                VehicleMaxWeightKg: vehicle?.MaxWeightKg,
                VehicleMaxVolumeM3: vehicle?.MaxVolumeM3,
                TotalWeightKg: routeStops.Sum(s => s.WeightKg ?? 0m),
                TotalVolumeM3: routeStops.Sum(s => s.VolumeM3 ?? 0m),
                LateStops: late,
                HasPlannedStart: t.PlannedStartUtc.HasValue,
                ApproximatePins: approximate,
                DriverOtherTrips: t.DriverId is int d2
                    ? sameDay.Where(o => o.TripId != t.TripId && o.PlanDate == t.PlanDate && o.DriverId == d2).Select(o => o.Code).ToList()
                    : Array.Empty<string>(),
                VehicleOtherTrips: t.VehicleId is int v2
                    ? sameDay.Where(o => o.TripId != t.TripId && o.PlanDate == t.PlanDate && o.VehicleId == v2).Select(o => o.Code).ToList()
                    : Array.Empty<string>(),
                IneligibleOrders: ineligibleByTrip.GetValueOrDefault(t.TripId) ?? (IReadOnlyList<OrderIneligibility>)Array.Empty<OrderIneligibility>());
            result[t.TripId] = TripRules.BuildIssues(input);
        }
        return result;
    }

    private sealed record VehicleCap(int VehicleId, int? MaxStops, decimal? MaxWeightKg, decimal? MaxVolumeM3);
    private sealed record StopRow(int RouteId, int OrderStopId, DateTime? PlannedArrivalUtc, DateTime? WindowEndUtc, int? GeocodeAccuracyLookupId,
        decimal? WeightKg, decimal? VolumeM3);
    private sealed record OtherTrip(int TripId, string Code, DateOnly PlanDate, int? DriverId, int? VehicleId);

    /// <summary>Pin aproximado: sin coordenada, o con precisión distinta de EXACT y MANUAL.</summary>
    private static bool IsApproximate(bool hasPoint, string? accuracyCode)
        => !hasPoint
           || !(string.Equals(accuracyCode, GeocodeAccuracies.Exact, StringComparison.OrdinalIgnoreCase)
                || string.Equals(accuracyCode, GeocodeAccuracies.Manual, StringComparison.OrdinalIgnoreCase));
}
