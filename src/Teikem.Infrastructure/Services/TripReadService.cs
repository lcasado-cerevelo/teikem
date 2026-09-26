using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P1) — lectura de rutas (Trip + su Route vigente): ficha, listado e ítems por id. Solo lee; no escribe nada.
/// - Todo parte de db.Trips (filtro global de tenant). Route y RouteStop no tienen TenantId: se alcanzan SIEMPRE por el
///   TripId de un Trip ya filtrado; las órdenes, por db.TransportOrders (también filtrada).
/// - Coordenadas y pings (GEOGRAPHY / DriverLocationPing) solo por TripQueries (SQL crudo confinado con 'TenantId =').
/// - Consultas por lote (sin N+1): la ficha y el listado arman sus diccionarios con una consulta por tabla.
/// - Las rutas canceladas (IsActive = 0) también se leen; el listado las oculta salvo IncludeCancelled o filtro explícito.
/// - Ningún DTO lleva montos (Amount/Rate): este módulo no maneja dinero.
/// </summary>
public sealed class TripReadService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses, TripIssueBuilder issueBuilder)
{
    // ================================================================ ficha

    /// <summary>Ficha de la ruta por PublicId (404 'Ruta no encontrada.').</summary>
    public async Task<TripDetailDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.PublicId == publicId, ct)
                   ?? throw new NotFoundException(TripQueries.TripLabel, null, true);
        return await DetailAsync(trip, ct);
    }

    /// <summary>Ficha de la ruta por id interno (uso de servicios; el id nunca llega desde el request). 404 si no es del tenant.</summary>
    public async Task<TripDetailDto> GetByIdAsync(int tripId, CancellationToken ct)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == tripId, ct)
                   ?? throw new NotFoundException(TripQueries.TripLabel, null, true);
        return await DetailAsync(trip, ct);
    }

    private async Task<TripDetailDto> DetailAsync(Trip trip, CancellationToken ct)
    {
        var tripId = trip.TripId;
        var statusMap = await StatusMapAsync(ct);
        var status = statusMap.GetValueOrDefault(trip.StatusCodeId);
        var statusCode = status?.Code ?? string.Empty;

        // Cabecera: zona, chofer y vehículo (bajo el filtro de tenant).
        var zone = trip.DispatchZoneId is int zoneId
            ? await db.DispatchZones.AsNoTracking().Where(z => z.DispatchZoneId == zoneId)
                .Select(z => new { z.Code, z.Name }).FirstOrDefaultAsync(ct)
            : null;
        var driver = trip.DriverId is int driverId
            ? await db.Drivers.AsNoTracking().Where(d => d.DriverId == driverId)
                .Select(d => new { d.PublicId, d.EmployeeCode, d.FullName, d.MaxStopsPerRoute }).FirstOrDefaultAsync(ct)
            : null;
        var vehicle = trip.VehicleId is int vehicleId
            ? await db.Vehicles.AsNoTracking().Where(v => v.VehicleId == vehicleId)
                .Select(v => new { v.PublicId, v.Code, v.PlateNumber }).FirstOrDefaultAsync(ct)
            : null;

        // Versión vigente de la ruta (UX_Route_Trip_Active: a lo sumo una) y sus paradas en secuencia.
        var route = await db.Routes.AsNoTracking().Where(r => r.TripId == tripId && r.IsActive)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
        var stops = route is null ? new List<StopRow>() : await StopRowsAsync(route.RouteId, ct);
        var stopDtos = await ToStopDtosAsync(stops, statusMap, ct);

        var tenantDefault = await TenantMaxStopsDefaultAsync(ct);
        int? effectiveMax = driver is null ? null : FleetRules.EffectiveMaxStops(driver.MaxStopsPerRoute, tenantDefault);
        var stopCount = stopDtos.Count;

        var isTerminal = status?.IsTerminal ?? false;
        var isEditable = trip.IsActive && TripRules.IsEditable(statusCode);
        var canEditHeader = trip.IsActive && !isTerminal
                            && await statuses.IsAllowedAsync(EntityTypes.Trip, trip.StatusCodeId, Capabilities.EditTrip, ct);

        // Avisos y bloqueantes (TripIssueBuilder); la elegibilidad de las órdenes solo importa mientras la ruta se edita.
        IReadOnlyList<TripIssueDto> issues = Array.Empty<TripIssueDto>();
        if (trip.IsActive && !isTerminal)
            issues = (await IssuesAsync(new[] { tripId }, isEditable, ct)).GetValueOrDefault(tripId) ?? Array.Empty<TripIssueDto>();

        var lastRunUnassigned = route is null ? Array.Empty<UnassignedStopDto>() : await LastRunUnassignedAsync(tripId, route.RouteId, ct);
        var lastPing = (await PingsAsync(new[] { new TripPingKey(tripId, trip.DriverId, trip.ActualStartUtc) }, ct)).GetValueOrDefault(tripId);

        return new TripDetailDto(
            Id: tripId,
            PublicId: trip.PublicId,
            Code: trip.Code,
            PlanDate: trip.PlanDate,
            DispatchZoneId: trip.DispatchZoneId,
            ZoneCode: zone?.Code,
            ZoneName: zone?.Name,
            DriverPublicId: driver?.PublicId,
            DriverCode: driver?.EmployeeCode,
            DriverName: driver?.FullName,
            VehiclePublicId: vehicle?.PublicId,
            VehicleCode: vehicle?.Code,
            VehiclePlate: vehicle?.PlateNumber,
            StatusCode: statusCode,
            Status: status?.Label ?? string.Empty,
            IsEditable: isEditable,
            CanEditHeader: canEditHeader,
            IsTerminal: isTerminal,
            RouteId: route?.RouteId,
            RouteVersion: route?.Version,
            RouteStatusCode: route is null ? null : statusMap.GetValueOrDefault(route.StatusCodeId)?.Code,
            RouteStatus: route is null ? null : statusMap.GetValueOrDefault(route.StatusCodeId)?.Label,
            StopCount: stopCount,
            EffectiveMaxStops: effectiveMax,
            OverStopLimit: TripRules.OverStopLimit(stopCount, effectiveMax),
            TotalWeightKg: stops.Sum(s => s.WeightKg ?? 0m),
            TotalVolumeM3: stops.Sum(s => s.VolumeM3 ?? 0m),
            TotalDistanceKm: trip.TotalDistanceKm,
            TotalDurationMin: trip.TotalDurationMin,
            PlannedStartUtc: trip.PlannedStartUtc,
            PlannedEndUtc: trip.PlannedEndUtc,
            ActualStartUtc: trip.ActualStartUtc,
            ActualEndUtc: trip.ActualEndUtc,
            Stops: stopDtos,
            Issues: issues,
            LastRunUnassigned: lastRunUnassigned,
            LastPing: lastPing,
            IsActive: trip.IsActive,
            CreatedAtUtc: trip.CreatedAtUtc,
            UpdatedAtUtc: trip.UpdatedAtUtc,
            RowVersion: RowVersionOf(trip.RowVersion));
    }

    // ================================================================ listado

    /// <summary>
    /// Listado del día (sin fecha = hoy UTC) o de un rango From..To. Filtros: estatus (400 si es desconocido), zona y chofer
    /// (404 si no es del tenant). La búsqueda libre (código, zona, chofer, vehículo, estatus) corre en memoria DESPUÉS de
    /// los filtros. Sin IncludeCancelled ni filtro de estatus, se ocultan las rutas eliminadas (IsActive = 0).
    /// </summary>
    public async Task<IReadOnlyList<TripListItemDto>> ListAsync(TripListQuery query, CancellationToken ct)
    {
        query ??= new TripListQuery();
        var statusMap = await StatusMapAsync(ct);
        var statusIds = ResolveStatusFilter(query.Status, statusMap);

        var q = db.Trips.AsNoTracking();
        if (query.Date is DateOnly date) q = q.Where(t => t.PlanDate == date);
        else if (query.From is not null || query.To is not null)
        {
            if (query.From is DateOnly f && query.To is DateOnly t0 && f > t0) throw new ValidationException("from", TripPlanningRules.DateRangeMessage);
            if (query.From is DateOnly from) q = q.Where(t => t.PlanDate >= from);
            if (query.To is DateOnly to) q = q.Where(t => t.PlanDate <= to);
        }
        else
        {
            var today = Today();
            q = q.Where(t => t.PlanDate == today);
        }

        if (statusIds is not null) q = q.Where(t => statusIds.Contains(t.StatusCodeId));
        else if (!query.IncludeCancelled) q = q.Where(t => t.IsActive);

        if (query.DispatchZoneId is int zoneId) q = q.Where(t => t.DispatchZoneId == zoneId);
        if (query.DriverPublicId is Guid driverPublicId)
        {
            var driver = await db.ResolveDriverAsync(driverPublicId, false, ct); // 404 'Chofer no encontrado.'
            var driverId = driver.DriverId;
            q = q.Where(t => t.DriverId == driverId);
        }

        var trips = await q.ToListAsync(ct);
        var items = await BuildItemsAsync(trips, statusMap, ct);
        if (!string.IsNullOrWhiteSpace(query.Search))
            items = items.Where(i => FleetRules.MatchesSearch(query.Search, i.Code, i.ZoneCode, i.ZoneName, i.DriverCode, i.DriverName,
                i.VehicleCode, i.StatusCode, i.Status)).ToList();

        return items
            .OrderBy(i => i.PlanDate)
            .ThenBy(i => i.ZoneCode ?? "￿", StringComparer.Ordinal)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Ítems de listado de las rutas del tenant con esos ids (incluidas las eliminadas), en el orden de la entrada.</summary>
    public async Task<IReadOnlyList<TripListItemDto>> ItemsAsync(IReadOnlyCollection<int> tripIds, CancellationToken ct)
    {
        if (tripIds is null || tripIds.Count == 0) return Array.Empty<TripListItemDto>();
        var ids = tripIds.Distinct().ToList();
        var trips = await db.Trips.AsNoTracking().Where(t => ids.Contains(t.TripId)).ToListAsync(ct);
        var items = await BuildItemsAsync(trips, await StatusMapAsync(ct), ct);
        var byId = items.ToDictionary(i => i.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>
    /// Avisos y bloqueantes por ruta (TripIssueBuilder) como DTO. Lo usa también TripService (reasignación en bloque:
    /// avisos DRIVER_DOUBLE_BOOKED).
    /// </summary>
    public async Task<Dictionary<int, IReadOnlyList<TripIssueDto>>> IssuesAsync(IReadOnlyCollection<int> tripIds, bool includeOrderEligibility, CancellationToken ct)
    {
        if (tripIds is null || tripIds.Count == 0) return new Dictionary<int, IReadOnlyList<TripIssueDto>>();
        var built = await issueBuilder.BuildAsync(tripIds, includeOrderEligibility, ct);
        return built.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TripIssueDto>)kv.Value.Select(i => new TripIssueDto(i.Code, i.Message, i.Blocking)).ToList());
    }

    private async Task<List<TripListItemDto>> BuildItemsAsync(List<Trip> trips, Dictionary<int, StatusInfo> statusMap, CancellationToken ct)
    {
        if (trips.Count == 0) return new List<TripListItemDto>();
        var tripIds = trips.Select(t => t.TripId).ToList();
        var zoneIds = trips.Where(t => t.DispatchZoneId.HasValue).Select(t => t.DispatchZoneId!.Value).Distinct().ToList();
        var driverIds = trips.Where(t => t.DriverId.HasValue).Select(t => t.DriverId!.Value).Distinct().ToList();
        var vehicleIds = trips.Where(t => t.VehicleId.HasValue).Select(t => t.VehicleId!.Value).Distinct().ToList();

        var zones = zoneIds.Count == 0
            ? new Dictionary<int, ZoneInfo>()
            : await db.DispatchZones.AsNoTracking().Where(z => zoneIds.Contains(z.DispatchZoneId))
                .Select(z => new ZoneInfo(z.DispatchZoneId, z.Code, z.Name)).ToDictionaryAsync(z => z.Id, ct);
        var drivers = driverIds.Count == 0
            ? new Dictionary<int, DriverInfo>()
            : await db.Drivers.AsNoTracking().Where(d => driverIds.Contains(d.DriverId))
                .Select(d => new DriverInfo(d.DriverId, d.PublicId, d.EmployeeCode, d.FullName, d.MaxStopsPerRoute)).ToDictionaryAsync(d => d.Id, ct);
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<int, VehicleInfo>()
            : await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
                .Select(v => new VehicleInfo(v.VehicleId, v.PublicId, v.Code)).ToDictionaryAsync(v => v.Id, ct);

        // Versión vigente por ruta y su cantidad real de paradas (dos consultas para todo el lote).
        var routes = await db.Routes.AsNoTracking().Where(r => tripIds.Contains(r.TripId) && r.IsActive)
            .Select(r => new { r.RouteId, r.TripId, r.Version, r.StatusCodeId }).ToListAsync(ct);
        var routeByTrip = routes.GroupBy(r => r.TripId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Version).First());
        var routeIds = routeByTrip.Values.Select(r => r.RouteId).ToList();
        var stopCounts = routeIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.RouteStops.AsNoTracking().Where(s => routeIds.Contains(s.RouteId))
                .GroupBy(s => s.RouteId).Select(g => new { RouteId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RouteId, x => x.Count, ct);

        var tenantDefault = await TenantMaxStopsDefaultAsync(ct);
        var items = new List<TripListItemDto>(trips.Count);
        foreach (var t in trips)
        {
            var status = statusMap.GetValueOrDefault(t.StatusCodeId);
            var zone = t.DispatchZoneId is int zid ? zones.GetValueOrDefault(zid) : null;
            var driver = t.DriverId is int did ? drivers.GetValueOrDefault(did) : null;
            var vehicle = t.VehicleId is int vid ? vehicles.GetValueOrDefault(vid) : null;
            var route = routeByTrip.GetValueOrDefault(t.TripId);
            var stopCount = route is null ? 0 : stopCounts.GetValueOrDefault(route.RouteId);
            int? effectiveMax = driver is null ? null : FleetRules.EffectiveMaxStops(driver.MaxStopsPerRoute, tenantDefault);

            items.Add(new TripListItemDto(
                Id: t.TripId,
                PublicId: t.PublicId,
                Code: t.Code,
                PlanDate: t.PlanDate,
                DispatchZoneId: t.DispatchZoneId,
                ZoneCode: zone?.Code,
                ZoneName: zone?.Name,
                DriverPublicId: driver?.PublicId,
                DriverCode: driver?.Code,
                DriverName: driver?.Name,
                VehiclePublicId: vehicle?.PublicId,
                VehicleCode: vehicle?.Code,
                StatusCode: status?.Code ?? string.Empty,
                Status: status?.Label ?? string.Empty,
                StatusColor: status?.Color,
                IsEditable: t.IsActive && TripRules.IsEditable(status?.Code ?? string.Empty),
                RouteVersion: route?.Version,
                RouteStatusCode: route is null ? null : statusMap.GetValueOrDefault(route.StatusCodeId)?.Code,
                StopCount: stopCount,
                EffectiveMaxStops: effectiveMax,
                OverStopLimit: TripRules.OverStopLimit(stopCount, effectiveMax),
                TotalDistanceKm: t.TotalDistanceKm,
                TotalDurationMin: t.TotalDurationMin,
                PlannedStartUtc: t.PlannedStartUtc,
                PlannedEndUtc: t.PlannedEndUtc,
                IsActive: t.IsActive));
        }
        return items;
    }

    // ================================================================ paradas

    private sealed record StopRow(
        int RouteStopId, int Sequence, int OrderStopId, int StatusCodeId,
        DateTime? PlannedArrivalUtc, DateTime? PlannedDepartureUtc, decimal? DistanceFromPrevKm, int? DurationFromPrevMin,
        DateTime? ActualArrivalUtc, DateTime? ActualDepartureUtc,
        Guid OrderPublicId, string OrderNumber, string PackBatchNumber, int ClientId,
        int? Pieces, decimal? WeightKg, decimal? VolumeM3,
        string? ConsigneeName, string Line1, string? Line2, string City, string? PostalCode,
        int? GeocodeAccuracyLookupId, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes);

    /// <summary>
    /// Paradas de una versión de ruta con su OrderStop y su orden. El JOIN a db.TransportOrders (filtro de tenant) es la
    /// segunda barrera: una parada cuya orden no fuera del tenant no aparece.
    /// </summary>
    private async Task<List<StopRow>> StopRowsAsync(int routeId, CancellationToken ct)
        => await (from rs in db.RouteStops.AsNoTracking()
                  join os in db.OrderStops.AsNoTracking() on rs.OrderStopId equals os.OrderStopId
                  join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                  where rs.RouteId == routeId
                  orderby rs.Sequence, rs.RouteStopId
                  select new StopRow(
                      rs.RouteStopId, rs.Sequence, rs.OrderStopId, rs.StatusCodeId,
                      rs.PlannedArrivalUtc, rs.PlannedDepartureUtc, rs.DistanceFromPrevKm, rs.DurationFromPrevMin,
                      rs.ActualArrivalUtc, rs.ActualDepartureUtc,
                      o.PublicId, o.OrderNumber, o.PackBatchNumber, o.ClientId,
                      o.TotalPieces, o.TotalWeightKg, o.TotalVolumeM3,
                      os.SnapName, os.SnapLine1, os.SnapLine2, os.SnapCity, os.SnapPostalCode,
                      os.GeocodeAccuracyLookupId, os.WindowStartUtc, os.WindowEndUtc, os.ServiceMinutes))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<RouteStopDto>> ToStopDtosAsync(List<StopRow> stops, Dictionary<int, StatusInfo> statusMap, CancellationToken ct)
    {
        if (stops.Count == 0) return Array.Empty<RouteStopDto>();

        var clientIds = stops.Select(s => s.ClientId).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var points = await PointsAsync(stops.Select(s => s.OrderStopId).Distinct().ToList(), ct);
        var zoneCodes = await ZoneCodesAsync(stops.Select(s => (s.PostalCode, s.City)).ToList(), ct);

        var list = new List<RouteStopDto>(stops.Count);
        for (var i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            var accuracy = s.GeocodeAccuracyLookupId is int accId ? await lookups.GetAsync(accId, ct) : null;
            var accuracyCode = accuracy?.InternalCode;
            var point = points.GetValueOrDefault(s.OrderStopId);
            var stopStatus = statusMap.GetValueOrDefault(s.StatusCodeId);

            list.Add(new RouteStopDto(
                Id: s.RouteStopId,
                Sequence: s.Sequence,
                OrderStopId: s.OrderStopId,
                OrderPublicId: s.OrderPublicId,
                OrderNumber: s.OrderNumber,
                PackBatchNumber: s.PackBatchNumber,
                ClientName: clients.GetValueOrDefault(s.ClientId) ?? string.Empty,
                ConsigneeName: s.ConsigneeName,
                Line1: s.Line1,
                Line2: s.Line2,
                City: s.City,
                PostalCode: s.PostalCode,
                ZoneCode: zoneCodes[i],
                Point: point,
                GeocodeAccuracyCode: accuracyCode,
                GeocodeAccuracy: accuracy is null ? null : MultilingualText.Resolve(accuracy.LabelJson, tenant.Lang),
                IsApproximate: TripPlanningRules.IsApproximatePin(point is not null, accuracyCode),
                WindowStartUtc: s.WindowStartUtc,
                WindowEndUtc: s.WindowEndUtc,
                ServiceMinutes: s.ServiceMinutes,
                PlannedArrivalUtc: s.PlannedArrivalUtc,
                PlannedDepartureUtc: s.PlannedDepartureUtc,
                DistanceFromPrevKm: s.DistanceFromPrevKm,
                DurationFromPrevMin: s.DurationFromPrevMin,
                LateForWindow: TripPlanningRules.LateForWindow(s.PlannedArrivalUtc, s.WindowEndUtc),
                Pieces: s.Pieces,
                WeightKg: s.WeightKg,
                VolumeM3: s.VolumeM3,
                StatusCode: stopStatus?.Code ?? string.Empty,
                Status: stopStatus?.Label ?? string.Empty,
                ActualArrivalUtc: s.ActualArrivalUtc,
                ActualDepartureUtc: s.ActualDepartureUtc));
        }
        return list;
    }

    /// <summary>
    /// 'No cupieron' de la última corrida OK de la versión vigente: RoutePlanJson.ParseUnassigned (tolerante: JSON nulo o
    /// dañado = lista vacía), solo las órdenes del tenant que SIGUEN sin ruta vigente, con la etiqueta del motivo.
    /// </summary>
    private async Task<IReadOnlyList<UnassignedStopDto>> LastRunUnassignedAsync(int tripId, int routeId, CancellationToken ct)
    {
        var okId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.OptimizationRunStatus && s.InternalCode == OptimizationRunStatuses.Ok)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (okId is null) return Array.Empty<UnassignedStopDto>();
        var okStatusId = okId.Value;

        var json = await db.OptimizationRuns.AsNoTracking()
            .Where(r => r.TripId == tripId && r.RouteId == routeId && r.StatusCodeId == okStatusId)
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.OptimizationRunId)
            .Select(r => r.ResponseJson).FirstOrDefaultAsync(ct);

        var parsed = RoutePlanJson.ParseUnassigned(json);
        if (parsed.Count == 0) return Array.Empty<UnassignedStopDto>();

        var publicIds = parsed.Select(p => p.OrderPublicId).Distinct().ToList();
        var stillFree = await db.TransportOrders.AsNoTracking()
            .Where(o => publicIds.Contains(o.PublicId) && o.IsActive
                        && !db.TripOrders.Any(x => x.TransportOrderId == o.TransportOrderId && x.IsCurrent))
            .Select(o => new { o.PublicId, o.OrderNumber })
            .ToDictionaryAsync(o => o.PublicId, o => o.OrderNumber, ct);

        return parsed
            .Where(p => stillFree.ContainsKey(p.OrderPublicId))
            .GroupBy(p => p.OrderPublicId).Select(g => g.First())
            .Select(p => new UnassignedStopDto(p.OrderPublicId, stillFree[p.OrderPublicId], p.ReasonCode, UnassignedReasons.Message(p.ReasonCode)))
            .ToList();
    }

    // ================================================================ adaptadores a las costuras de P0 (TripQueries, DispatchZoneMatcher)

    /// <summary>Coordenadas de las paradas (TripQueries.StopPointsAsync, SQL crudo con TenantId) como GeoPointDto.</summary>
    private async Task<Dictionary<int, GeoPointDto>> PointsAsync(IReadOnlyCollection<int> orderStopIds, CancellationToken ct)
    {
        var result = new Dictionary<int, GeoPointDto>();
        if (orderStopIds.Count == 0) return result;
        var rows = await db.StopPointsAsync(orderStopIds, ct);
        foreach (var kv in rows) result[kv.Key] = new GeoPointDto(kv.Value.Lat, kv.Value.Lng);
        return result;
    }

    /// <summary>Código de zona por parada (CP > rango > municipio). Ambigua o sin zona = null.</summary>
    private async Task<IReadOnlyList<string?>> ZoneCodesAsync(IReadOnlyList<(string? PostalCode, string City)> addresses, CancellationToken ct)
    {
        if (addresses.Count == 0) return Array.Empty<string?>();
        var members = await db.ActiveZoneMembersAsync(ct);
        return addresses.Select(a =>
        {
            var r = DispatchZoneMatcher.Resolve(members, a.PostalCode, a.City);
            return r.Ambiguous ? null : r.ZoneCode;
        }).ToList();
    }

    /// <summary>Último ping por ruta (TripQueries.LastPingsAsync, con el respaldo del chofer desde la salida) como DTO.</summary>
    private async Task<Dictionary<int, DriverPingDto>> PingsAsync(IReadOnlyCollection<TripPingKey> keys, CancellationToken ct)
    {
        var result = new Dictionary<int, DriverPingDto>();
        if (keys.Count == 0) return result;
        var rows = await db.LastPingsAsync(keys, ct);
        foreach (var kv in rows)
        {
            var p = kv.Value;
            result[kv.Key] = new DriverPingDto(new GeoPointDto(p.Lat, p.Lng), p.SpeedKmh, p.HeadingDeg, p.CapturedAtUtc, p.ReceivedAtUtc, p.LinkedToTrip);
        }
        return result;
    }

    // ================================================================ helpers

    private sealed record ZoneInfo(int Id, string Code, string? Name);
    private sealed record DriverInfo(int Id, Guid PublicId, string Code, string Name, int? MaxStopsPerRoute);
    private sealed record VehicleInfo(int Id, Guid PublicId, string Code);
    private sealed record StatusInfo(string Domain, string Code, string Label, string? Color, bool IsTerminal);

    private async Task<int> TenantMaxStopsDefaultAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        return await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => t.MaxStopsPerRouteDefault).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Estatus de TripStatus, RouteStatus y RouteStopStatus con la etiqueta/color del tenant (StatusCodeOverride), indexados
    /// por StatusCodeId (los ids no se repiten entre dominios).
    /// </summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
    {
        var domains = new[] { StatusDomains.TripStatus, StatusDomains.RouteStatus, StatusDomains.RouteStopStatus };
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => domains.Contains(s.Entity)).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(s => s.StatusCodeId, s =>
        {
            var o = overrides.GetValueOrDefault(s.StatusCodeId);
            return new StatusInfo(s.Entity, s.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(s.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? s.ColorHex, s.StageKind?.InternalCode == StageKinds.Terminal);
        });
    }

    /// <summary>Filtro status[] por código del dominio TripStatus; un código desconocido es 400 'Estatus de ruta desconocido: 'X'.'</summary>
    private static List<int>? ResolveStatusFilter(string[]? codes, Dictionary<int, StatusInfo> statusMap)
    {
        var wanted = codes?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        if (wanted is null || wanted.Count == 0) return null;
        var ids = new List<int>();
        foreach (var code in wanted)
        {
            var match = statusMap.FirstOrDefault(kv => kv.Value.Domain == StatusDomains.TripStatus
                                                       && string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null) throw new ValidationException("status", TripPlanningRules.UnknownStatusMessage(code));
            ids.Add(match.Key);
        }
        return ids;
    }

    private static string RowVersionOf(byte[]? rv) => rv is null ? string.Empty : Convert.ToBase64String(rv);
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
