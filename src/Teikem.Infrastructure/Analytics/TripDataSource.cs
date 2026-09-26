using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 5 (P7) — fuente de datos de Rutas (Trip + su versión vigente) para vistas, indicadores y gráficos (módulos G/H/I).
/// Sigue el patrón de las fuentes de Flota: lectura AsNoTracking bajo el filtro de tenant, tope de
/// ClientDataSourceHelpers.MaxRows filas, etiquetas de estatus por dominio, respeto de q.Ids (relaciones muchos-a-uno) y del
/// rango sobre PlanDate (DATE: desde inclusivo, hasta exclusivo; el resolutor entrega "mañana 00:00").
/// - Route y RouteStop no llevan TenantId: se alcanzan SOLO por un JOIN con db.Trips (ya filtrado), en consultas por lote.
/// - Las rutas eliminadas (CANCELLED, IsActive = 0) también se leen; el campo IsActive permite filtrarlas.
/// - Sin dinero: no hay campos Amount ni Rate (despachar no crea DriverTrip).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (vista "Rutas", indicador "Rutas sobre el máximo de paradas",
///   gráfico "Rutas por estatus"); no cambiarlos sin cambiar el seeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class TripDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Trip;
    public string LabelEs => "Rutas";
    public string LabelEn => "Trips";
    /// <summary>Habilita los campos personalizados de la ruta como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.Trip;
    /// <summary>Actividad de período por fecha de la ruta.</summary>
    public string? DateField => "PlanDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Número de ruta", "Trip number", DataFieldType.Text),
        new DataField("PlanDate", "Fecha de la ruta", "Plan date", DataFieldType.Date),
        new DataField("DispatchZoneId", "Id de zona", "Zone id", DataFieldType.Number),
        new DataField("ZoneCode", "Zona", "Zone", DataFieldType.Text),
        new DataField("ZoneName", "Nombre de zona", "Zone name", DataFieldType.Text),
        new DataField("DriverId", "Id de chofer", "Driver id", DataFieldType.Number),
        new DataField("DriverCode", "Código de chofer", "Driver code", DataFieldType.Text),
        new DataField("DriverName", "Chofer", "Driver", DataFieldType.Text),
        new DataField("VehicleId", "Id de vehículo", "Vehicle id", DataFieldType.Number),
        new DataField("VehicleCode", "Vehículo", "Vehicle", DataFieldType.Text),
        new DataField("VehiclePlate", "Placa", "Plate number", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsEditable", "Editable", "Editable", DataFieldType.Bool),
        new DataField("RouteVersion", "Versión de la ruta", "Route version", DataFieldType.Number),
        new DataField("RouteStatus", "Estatus de la versión", "Route version status", DataFieldType.Text),
        new DataField("RouteStatusCode", "Código de estatus de la versión", "Route version status code", DataFieldType.Text),
        new DataField("StopCount", "Paradas", "Stops", DataFieldType.Number),
        new DataField("CompletedStops", "Paradas completadas", "Completed stops", DataFieldType.Number),
        new DataField("FailedStops", "Paradas fallidas", "Failed stops", DataFieldType.Number),
        new DataField("PendingStops", "Paradas pendientes", "Pending stops", DataFieldType.Number),
        new DataField("EffectiveMaxStops", "Máximo de paradas del chofer", "Driver max stops", DataFieldType.Number),
        new DataField("OverStopLimit", "Sobre el máximo de paradas", "Over stop limit", DataFieldType.Bool),
        new DataField("TotalDistanceKm", "Distancia (km)", "Distance (km)", DataFieldType.Number),
        new DataField("TotalDurationMin", "Duración (min)", "Duration (min)", DataFieldType.Number),
        new DataField("PlannedStartUtc", "Salida planificada", "Planned start", DataFieldType.Date),
        new DataField("PlannedEndUtc", "Fin planificado", "Planned end", DataFieldType.Date),
        new DataField("ActualStartUtc", "Salida real", "Actual start", DataFieldType.Date),
        new DataField("ActualEndUtc", "Fin real", "Actual end", DataFieldType.Date),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Driver", EntityTypes.Driver, "DriverId", "Chofer", "Driver"),
        new DataRelation("Vehicle", EntityTypes.Vehicle, "VehicleId", "Vehículo", "Vehicle"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Trips.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(t => wanted.Contains(t.TripId));
        }
        // Rango sobre PlanDate (DATE): desde inclusivo, hasta exclusivo (el resolutor de rangos entrega "mañana 00:00").
        if (q.FromUtc.HasValue)
        {
            var from = DateOnly.FromDateTime(q.FromUtc.Value);
            query = query.Where(t => t.PlanDate >= from);
        }
        if (q.ToUtc.HasValue)
        {
            var to = DateOnly.FromDateTime(q.ToUtc.Value);
            if (q.ToUtc.Value.TimeOfDay != TimeSpan.Zero) to = to.AddDays(1); // un "hasta" con hora incluye ese día
            query = query.Where(t => t.PlanDate < to);
        }

        var trips = await query.OrderByDescending(t => t.PlanDate).ThenByDescending(t => t.TripId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (trips.Count == 0) return new List<DataRow>();

        var tripIds = trips.Select(t => t.TripId).ToList();
        var lang = tenant.Lang;
        var tripStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.TripStatus, ct);
        var routeStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.RouteStatus, ct);
        var stopStatus = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.RouteStopStatus, ct);

        // Zonas, choferes y vehículos: una consulta cada uno (bajo el filtro de tenant).
        var zoneIds = trips.Where(t => t.DispatchZoneId.HasValue).Select(t => t.DispatchZoneId!.Value).Distinct().ToList();
        var zones = zoneIds.Count == 0
            ? new Dictionary<int, (string Code, string? Name)>()
            : (await db.DispatchZones.AsNoTracking().Where(z => zoneIds.Contains(z.DispatchZoneId))
                    .Select(z => new { z.DispatchZoneId, z.Code, z.Name }).ToListAsync(ct))
                .ToDictionary(z => z.DispatchZoneId, z => (z.Code, (string?)z.Name));

        var driverIds = trips.Where(t => t.DriverId.HasValue).Select(t => t.DriverId!.Value).Distinct().ToList();
        var drivers = driverIds.Count == 0
            ? new Dictionary<int, (string Code, string Name, int? MaxStops)>()
            : (await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                    .Select(d => new { d.DriverId, d.EmployeeCode, d.FullName, d.MaxStopsPerRoute }).ToListAsync(ct))
                .ToDictionary(d => d.DriverId, d => (d.EmployeeCode, d.FullName, d.MaxStopsPerRoute));

        var vehicleIds = trips.Where(t => t.VehicleId.HasValue).Select(t => t.VehicleId!.Value).Distinct().ToList();
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<int, (string Code, string? Plate)>()
            : (await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
                    .Select(v => new { v.VehicleId, v.Code, v.PlateNumber }).ToListAsync(ct))
                .ToDictionary(v => v.VehicleId, v => (v.Code, (string?)v.PlateNumber));

        // Versión vigente de cada ruta (a lo sumo una), por JOIN con Trips filtrados.
        var routes = (await (from r in db.Routes.AsNoTracking()
                             join t in db.Trips.AsNoTracking() on r.TripId equals t.TripId
                             where tripIds.Contains(t.TripId) && r.IsActive
                             select new { r.RouteId, r.TripId, r.Version, r.StatusCodeId }).ToListAsync(ct))
            .GroupBy(r => r.TripId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Version).First());
        var routeIds = routes.Values.Select(r => r.RouteId).ToList();

        // Conteo de paradas por versión vigente y estatus: una sola consulta agrupada.
        var stopCounts = routeIds.Count == 0
            ? new Dictionary<int, List<(int StatusCodeId, int Count)>>()
            : (await (from rs in db.RouteStops.AsNoTracking()
                      join r in db.Routes.AsNoTracking() on rs.RouteId equals r.RouteId
                      join t in db.Trips.AsNoTracking() on r.TripId equals t.TripId
                      where routeIds.Contains(rs.RouteId)
                      group rs by new { rs.RouteId, rs.StatusCodeId } into g
                      select new { g.Key.RouteId, g.Key.StatusCodeId, Count = g.Count() }).ToListAsync(ct))
                .GroupBy(x => x.RouteId)
                .ToDictionary(g => g.Key, g => g.Select(x => (x.StatusCodeId, x.Count)).ToList());

        var tenantId = tenant.TenantId;
        int? tenantDefault = tenantId is null
            ? null
            : await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId.Value)
                .Select(t => (int?)t.MaxStopsPerRouteDefault).FirstOrDefaultAsync(ct);

        var rows = new List<DataRow>(trips.Count);
        foreach (var t in trips)
        {
            var statusCode = ClientDataSourceHelpers.StatusCodeOf(tripStatus, t.StatusCodeId);
            var route = routes.GetValueOrDefault(t.TripId);
            var counts = route is null ? null : stopCounts.GetValueOrDefault(route.RouteId);
            var codes = counts is null
                ? Enumerable.Empty<string?>()
                : counts.SelectMany(c => Enumerable.Repeat(ClientDataSourceHelpers.StatusCodeOf(stopStatus, c.StatusCodeId), c.Count));
            var progress = MonitorRules.Progress(codes);

            (string Code, string? Name)? zone = t.DispatchZoneId is int zId && zones.TryGetValue(zId, out var z) ? z : null;
            (string Code, string Name, int? MaxStops)? driver = t.DriverId is int dId && drivers.TryGetValue(dId, out var d) ? d : null;
            (string Code, string? Plate)? vehicle = t.VehicleId is int vId && vehicles.TryGetValue(vId, out var v) ? v : null;
            int? effectiveMax = driver is null ? null : FleetRules.EffectiveMaxStops(driver.Value.MaxStops, tenantDefault);

            rows.Add(new DataRow
            {
                ["Id"] = t.TripId,
                ["PublicId"] = t.PublicId.ToString(),
                ["Code"] = t.Code,
                ["PlanDate"] = t.PlanDate,
                ["DispatchZoneId"] = t.DispatchZoneId,
                ["ZoneCode"] = zone?.Code,
                ["ZoneName"] = zone?.Name,
                ["DriverId"] = t.DriverId,
                ["DriverCode"] = driver?.Code,
                ["DriverName"] = driver?.Name,
                ["VehicleId"] = t.VehicleId,
                ["VehicleCode"] = vehicle?.Code,
                ["VehiclePlate"] = vehicle?.Plate,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(tripStatus, t.StatusCodeId, lang),
                ["StatusCode"] = statusCode,
                ["IsEditable"] = t.IsActive && TripRules.IsEditable(statusCode ?? string.Empty),
                ["RouteVersion"] = route?.Version,
                ["RouteStatus"] = route is null ? null : ClientDataSourceHelpers.StatusLabel(routeStatus, route.StatusCodeId, lang),
                ["RouteStatusCode"] = route is null ? null : ClientDataSourceHelpers.StatusCodeOf(routeStatus, route.StatusCodeId),
                ["StopCount"] = progress.Total,
                ["CompletedStops"] = progress.Completed,
                ["FailedStops"] = progress.Failed,
                ["PendingStops"] = progress.Pending,
                ["EffectiveMaxStops"] = effectiveMax,
                ["OverStopLimit"] = TripRules.OverStopLimit(progress.Total, effectiveMax),
                ["TotalDistanceKm"] = t.TotalDistanceKm,
                ["TotalDurationMin"] = t.TotalDurationMin,
                ["PlannedStartUtc"] = t.PlannedStartUtc,
                ["PlannedEndUtc"] = t.PlannedEndUtc,
                ["ActualStartUtc"] = t.ActualStartUtc,
                ["ActualEndUtc"] = t.ActualEndUtc,
                ["IsActive"] = t.IsActive,
                ["CreatedAtUtc"] = t.CreatedAtUtc,
            });
        }
        return rows;
    }
}
