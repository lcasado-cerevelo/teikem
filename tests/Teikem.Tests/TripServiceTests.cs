using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;
using TripIssueCodes = Teikem.Domain.Trips.TripIssueCodes;
using TripPlanningRules = Teikem.Domain.Trips.TripPlanningRules;
using TripRules = Teikem.Domain.Trips.TripRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 — servicios de la cabecera y de los avisos de la ruta sobre InMemory (TripServiceFixture):
/// - editar la cabecera de una ruta abierta: la hora de salida recalcula las ETAs; la fecha corre la salida y revalida la
///   disponibilidad del chofer y del vehículo actuales (maestro L286); quitar la salida deja las ETAs sin hora;
/// - máximo de paradas efectivo (maestro L270): sin override del chofer vale Tenant.MaxStopsPerRouteDefault, en la ficha, el
///   listado, el selector de despacho, los avisos y el monitor;
/// - avisos no bloqueantes que TripIssueBuilder arma desde la BD (capacidad, ventanas tardías, sin salida, pines
///   aproximados, doble asignación del vehículo sin cruzarla con el chofer) e isApproximate en la ficha (maestro L268).
/// </summary>
public class TripServiceTests
{
    private static async Task<(TripServiceFixture F, int TripId, Guid PublicId, List<SeededOrder> Orders)> DraftTripWithOrdersAsync(int count)
    {
        var f = await TripServiceFixture.CreateAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var orders = new List<SeededOrder>();
        for (var i = 0; i < count; i++) orders.Add(await f.SeedOrderAsync(OrderStatuses.Confirmed));
        if (count > 0) await f.AddOrdersAsync(tripId, orders.Select(o => o.Id).ToArray());
        return (f, tripId, publicId, orders);
    }

    private static async Task<List<(int OrderStopId, DateTime? Arrival, DateTime? Departure)>> ActiveStopTimesAsync(TripServiceFixture f, int tripId)
    {
        var routeId = await f.Db.Routes.AsNoTracking().Where(r => r.TripId == tripId && r.IsActive).Select(r => r.RouteId).SingleAsync();
        return (await f.Db.RouteStops.AsNoTracking().Where(s => s.RouteId == routeId).OrderBy(s => s.Sequence)
                .Select(s => new { s.OrderStopId, s.PlannedArrivalUtc, s.PlannedDepartureUtc }).ToListAsync())
            .Select(s => (s.OrderStopId, s.PlannedArrivalUtc, s.PlannedDepartureUtc)).ToList();
    }

    // ================================================================ PATCH de la cabecera de una ruta abierta

    [Fact]
    public async Task Changing_the_planned_start_recomputes_the_etas_of_the_current_version()
    {
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        var before = await ActiveStopTimesAsync(f, tripId);
        var tripBefore = await f.Db.Trips.AsNoTracking().SingleAsync(t => t.TripId == tripId);
        Assert.All(before, s => Assert.NotNull(s.Arrival));
        Assert.NotNull(tripBefore.PlannedEndUtc);

        var newStart = tripBefore.PlannedStartUtc!.Value.AddHours(1);
        var detail = await f.Get<TripService>().UpdateAsync(publicId, new TripPatchRequest(PlannedStartUtc: newStart), default);

        Assert.Equal(newStart, detail.PlannedStartUtc);
        Assert.Equal(tripBefore.PlannedEndUtc!.Value.AddHours(1), detail.PlannedEndUtc);
        var after = await ActiveStopTimesAsync(f, tripId);
        Assert.Equal(before.Select(s => (s.OrderStopId, s.Arrival!.Value.AddHours(1), s.Departure!.Value.AddHours(1))),
            after.Select(s => (s.OrderStopId, s.Arrival!.Value, s.Departure!.Value)));
        Assert.Equal(after.Select(s => s.Arrival), detail.Stops.Select(s => s.PlannedArrivalUtc));
    }

    [Fact]
    public async Task Changing_the_plan_date_revalidates_the_current_vehicle_and_driver_and_shifts_the_start()
    {
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(1);
        await using var _f = f;
        var newDate = TripServiceFixture.Today.AddDays(2);
        var svc = f.Get<TripService>();

        // Vehículo actual no disponible en la fecha nueva → 409 y la ruta no cambia.
        f.Availability.BlockedVehicles.Add(9);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(publicId, new TripPatchRequest(PlanDate: newDate), default));
        Assert.StartsWith(TripPlanningRules.VehicleNotAvailablePrefix, ex.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(TripServiceFixture.Today, await f.Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.PlanDate).SingleAsync());
        f.Availability.BlockedVehicles.Clear();

        // Chofer actual no disponible en la fecha nueva → 409.
        f.Availability.BlockedDrivers.Add(7);
        ex = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateAsync(publicId, new TripPatchRequest(PlanDate: newDate), default));
        Assert.StartsWith(TripPlanningRules.DriverNotAvailablePrefix, ex.Message);
        Assert.Contains(AlwaysAvailable.BlockedDriverMessage.TrimEnd('.'), ex.Message);
        f.Db.ChangeTracker.Clear();
        f.Availability.BlockedDrivers.Clear();

        // Sin bloqueos: la fecha cambia y la salida se corre a la fecha nueva conservando la hora (y las ETAs con ella).
        var detail = await svc.UpdateAsync(publicId, new TripPatchRequest(PlanDate: newDate), default);
        Assert.Equal(newDate, detail.PlanDate);
        Assert.Equal(newDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), detail.PlannedStartUtc);
        Assert.All(detail.Stops, s => Assert.Equal(newDate, DateOnly.FromDateTime(s.PlannedArrivalUtc!.Value)));
    }

    [Fact]
    public async Task Clearing_the_planned_start_leaves_the_route_without_etas_and_warns()
    {
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;

        var detail = await f.Get<TripService>().UpdateAsync(publicId, new TripPatchRequest(ClearPlannedStart: true), default);

        Assert.Null(detail.PlannedStartUtc);
        Assert.Null(detail.PlannedEndUtc);
        Assert.Null(await f.Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.PlannedStartUtc).SingleAsync());
        Assert.All(await ActiveStopTimesAsync(f, tripId), s => Assert.Null(s.Arrival));
        Assert.Contains(detail.Issues, i => i.Code == TripIssueCodes.NoPlannedStart && !i.Blocking && i.Message == TripRules.NoPlannedStartMessage);
    }

    // ================================================================ máximo de paradas efectivo (L270)

    [Fact]
    public async Task Effective_max_stops_falls_back_to_the_tenant_default_everywhere()
    {
        var (f, tripId, publicId, _) = await DraftTripWithOrdersAsync(2);
        await using var _f = f;
        await f.SeedDriverAsync(7, maxStopsPerRoute: null);              // chofer de SeedTripAsync, sin override
        var reader = f.Get<TripReadService>();
        var dispatch = f.Get<TripDispatchService>();

        // Default del tenant (25): 2 paradas no pasan el máximo.
        var detail = await reader.GetAsync(publicId, default);
        Assert.Equal(25, detail.EffectiveMaxStops);
        Assert.False(detail.OverStopLimit);
        Assert.DoesNotContain(detail.Issues, i => i.Code == TripIssueCodes.OverStopLimit);
        var item = Assert.Single(await reader.ItemsAsync(new[] { tripId }, default));
        Assert.Equal(25, item.EffectiveMaxStops);
        Assert.False(item.OverStopLimit);
        Assert.Equal(25, Assert.Single(await dispatch.ListDispatchableAsync(TripServiceFixture.Today, default)).Trip.EffectiveMaxStops);

        // El tenant baja su default a 1: la ruta de 2 paradas queda sobre el máximo en todas partes, solo como aviso.
        await f.SetTenantMaxStopsDefaultAsync(1);
        var message = TripRules.OverStopLimitMessage(2, 1);
        detail = await reader.GetAsync(publicId, default);
        Assert.Equal(1, detail.EffectiveMaxStops);
        Assert.True(detail.OverStopLimit);
        Assert.Contains(detail.Issues, i => i.Code == TripIssueCodes.OverStopLimit && !i.Blocking && i.Message == message);
        item = Assert.Single(await reader.ItemsAsync(new[] { tripId }, default));
        Assert.Equal(1, item.EffectiveMaxStops);
        Assert.True(item.OverStopLimit);
        var selectable = Assert.Single(await dispatch.ListDispatchableAsync(TripServiceFixture.Today, default));
        Assert.True(selectable.Trip.OverStopLimit);
        Assert.True(selectable.CanDispatch);
        Assert.Contains(selectable.Issues, i => i.Code == TripIssueCodes.OverStopLimit && !i.Blocking);
        var issues = (await f.Get<TripIssueBuilder>().BuildAsync(new[] { tripId }, false, default))[tripId];
        Assert.Contains(issues, i => i.Code == TripIssueCodes.OverStopLimit && i.Message == message);

        // Monitor (ruta despachada): alerta por ruta y en los totales.
        await f.SetTripStatusAsync(tripId, TripStatuses.Dispatched);
        var monitor = await f.Get<TripMonitorService>().ListAsync(new MonitorQuery(Date: TripServiceFixture.Today), default);
        Assert.True(Assert.Single(monitor.Trips).OverStopLimit);
        Assert.Equal(1, monitor.Totals.OverStopLimitTrips);
    }

    // ================================================================ avisos desde la BD (TripIssueBuilder) e isApproximate

    [Fact]
    public async Task Issue_builder_computes_capacity_late_windows_no_start_pins_and_vehicle_double_booking()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedVehicleAsync(9, maxStops: 1, maxWeightKg: 10m, maxVolumeM3: 0.1m);   // vehículo de SeedTripAsync
        var (tripA, publicA) = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);   // chofer 7, vehículo 9
        var (tripB, _) = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);         // mismo vehículo...
        var b = await f.Db.Trips.SingleAsync(t => t.TripId == tripB);
        b.DriverId = 8;                                                                             // ...otro chofer
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.AddOrdersAsync(tripA, o1.Id, o2.Id);

        // Carga de o1 por encima de la capacidad; ventana de o1 que la llegada planificada ya pasó; ruta sin hora de salida.
        var windowEnd = TripServiceFixture.Today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var order = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == o1.Id);
        order.TotalWeightKg = 30m;
        order.TotalVolumeM3 = 0.5m;
        var stop = await f.Db.OrderStops.SingleAsync(s => s.OrderStopId == o1.StopId);
        stop.WindowStartUtc = windowEnd.AddHours(-2);
        stop.WindowEndUtc = windowEnd;
        var a = await f.Db.Trips.SingleAsync(t => t.TripId == tripA);
        a.PlannedStartUtc = null;
        var routeStop = await f.Db.RouteStops.SingleAsync(s => s.OrderStopId == o1.StopId);
        routeStop.PlannedArrivalUtc = windowEnd.AddHours(1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var all = await f.Get<TripIssueBuilder>().BuildAsync(new[] { tripA, tripB }, false, default);

        // Ruta A: solo avisos (ningún bloqueante), en el orden estable de TripRules.BuildIssues, con sus mensajes exactos.
        // Con InMemory no hay coordenadas: las 2 paradas cuentan como aproximadas (ZIP_CENTROID sin punto).
        var issuesA = all[tripA];
        Assert.Equal(new[]
        {
            (TripIssueCodes.OverVehicleStops, TripRules.OverVehicleStopsMessage(2, 1)),
            (TripIssueCodes.OverWeight, TripRules.OverWeightMessage(30m, 10m)),
            (TripIssueCodes.OverVolume, TripRules.OverVolumeMessage(0.5m, 0.1m)),
            (TripIssueCodes.LateWindows, TripRules.LateWindowsMessage(1)),
            (TripIssueCodes.NoPlannedStart, TripRules.NoPlannedStartMessage),
            (TripIssueCodes.ApproximatePins, TripRules.ApproximatePinsMessage(2)),
            (TripIssueCodes.VehicleDoubleBooked, TripRules.VehicleDoubleBookedMessage(new[] { "2026-0002" })),
        }, issuesA.Select(i => (i.Code, i.Message)));
        Assert.All(issuesA, i => Assert.False(i.Blocking));
        // El cruce es por vehículo, no por chofer (choferes distintos: sin DRIVER_DOUBLE_BOOKED).
        Assert.DoesNotContain(issuesA, i => i.Code == TripIssueCodes.DriverDoubleBooked);
        var issuesB = all[tripB];
        Assert.Contains(issuesB, i => i.Code == TripIssueCodes.VehicleDoubleBooked && i.Message == TripRules.VehicleDoubleBookedMessage(new[] { "2026-0001" }));
        Assert.DoesNotContain(issuesB, i => i.Code == TripIssueCodes.DriverDoubleBooked);

        // Ficha (L268): las paradas sin punto son aproximadas y la tardía se marca.
        var detail = await f.Get<TripReadService>().GetAsync(publicA, default);
        Assert.All(detail.Stops, s => Assert.True(s.IsApproximate));
        Assert.All(detail.Stops, s => Assert.Null(s.Point));
        Assert.True(detail.Stops.Single(s => s.OrderStopId == o1.StopId).LateForWindow);
        Assert.False(detail.Stops.Single(s => s.OrderStopId == o2.StopId).LateForWindow);
    }

    // ================================================================ cabecera de una ruta despachada (EDIT_TRIP habilitada)

    private static async Task<Guid> DriverPublicIdAsync(TripServiceFixture f, int driverId)
        => await f.Db.Drivers.AsNoTracking().Where(d => d.DriverId == driverId).Select(d => d.PublicId).SingleAsync();

    [Theory]
    [InlineData("driver")]
    [InlineData("vehicle")]
    [InlineData("start")]
    public async Task Dispatched_route_header_cannot_lose_driver_vehicle_or_planned_start(string what)
    {
        // Sin fila de StatusCapability, EDIT_TRIP queda permitida también en DISPATCHED (como si el tenant la habilitara).
        await using var f = await TripServiceFixture.CreateAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Dispatched, f.Zone1);
        var req = what switch
        {
            "driver" => new TripPatchRequest(ClearDriver: true),
            "vehicle" => new TripPatchRequest(ClearVehicle: true),
            _ => new TripPatchRequest(ClearPlannedStart: true),
        };

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<TripService>().UpdateAsync(publicId, req, default));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal(TripPlanningRules.DispatchedRequiredMessage, ex.Message);
        f.Db.ChangeTracker.Clear();
        var trip = await f.Db.Trips.AsNoTracking().SingleAsync(t => t.TripId == tripId);
        Assert.Equal(7, trip.DriverId);
        Assert.Equal(9, trip.VehicleId);
        Assert.NotNull(trip.PlannedStartUtc);
    }

    [Fact]
    public async Task Dispatched_route_header_accepts_changing_the_driver()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedDriverAsync(8, null, "D8", "Chofer Ocho");
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Dispatched, f.Zone1);

        await f.Get<TripService>().UpdateAsync(publicId, new TripPatchRequest(DriverPublicId: await DriverPublicIdAsync(f, 8)), default);

        Assert.Equal(8, await f.Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.DriverId).SingleAsync());
    }

    // ================================================================ reasignación en bloque y EDIT_TRIP

    [Fact]
    public async Task Zone_reassignment_skips_routes_whose_status_denies_edit_trip()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedDriverAsync(8, null, "D8", "Chofer Ocho");
        var planned = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        var draft = await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1);
        await f.DenyCapabilityAsync(EntityTypes.Trip, StatusDomains.TripStatus, TripStatuses.Planned, Capabilities.EditTrip);

        var d8 = await DriverPublicIdAsync(f, 8);

        // El PATCH de cabecera respeta la capacidad negada...
        await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<TripService>().UpdateAsync(planned.PublicId,
            new TripPatchRequest(DriverPublicId: d8), default));
        f.Db.ChangeTracker.Clear();

        // ... y la reasignación en bloque también: solo cambia la ruta DRAFT.
        var result = await f.Get<TripService>().ReassignZoneAsync(
            new ZoneReassignRequest(TripServiceFixture.Today, new[] { f.Zone1 }, d8), default);

        Assert.Equal(1, result.TripsUpdated);
        Assert.Equal(new[] { draft.PublicId }, result.Trips.Select(t => t.PublicId));
        Assert.Equal(7, await f.Db.Trips.AsNoTracking().Where(t => t.TripId == planned.TripId).Select(t => t.DriverId).SingleAsync());
        Assert.Equal(8, await f.Db.Trips.AsNoTracking().Where(t => t.TripId == draft.TripId).Select(t => t.DriverId).SingleAsync());
    }

    // ================================================================ listado GET /trips

    [Fact]
    public async Task List_hides_deleted_routes_and_applies_status_range_zone_and_driver_filters_before_search()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        await f.SeedDriverAsync(7, null);
        var a = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var b = await f.SeedTripAsync("2026-0002", TripStatuses.Planned, f.Zone2);
        var deleted = await f.SeedTripAsync("2026-0003", TripStatuses.Cancelled, f.Zone1, isActive: false);
        var reader = f.Get<TripReadService>();
        var today = TripServiceFixture.Today;

        // Por defecto se ocultan las eliminadas; includeCancelled o el filtro de estatus las traen.
        Assert.Equal(new[] { a.PublicId, b.PublicId }, (await reader.ListAsync(new TripListQuery(Date: today), default)).Select(i => i.PublicId));
        Assert.Contains(deleted.PublicId, (await reader.ListAsync(new TripListQuery(Date: today, IncludeCancelled: true), default)).Select(i => i.PublicId));
        var cancelled = await reader.ListAsync(new TripListQuery(Date: today, Status: new[] { "cancelled" }), default);
        Assert.Equal(new[] { deleted.PublicId }, cancelled.Select(i => i.PublicId));
        Assert.All(cancelled, i => Assert.Equal(TripStatuses.Cancelled, i.StatusCode));

        // Estatus desconocido y rango invertido → 400.
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => reader.ListAsync(new TripListQuery(Status: new[] { "FOO" }), default));
        Assert.Equal("Estatus de ruta desconocido: 'FOO'.", unknown.Message);
        var range = await Assert.ThrowsAsync<ValidationException>(() => reader.ListAsync(new TripListQuery(From: today.AddDays(1), To: today), default));
        Assert.Equal(TripPlanningRules.DateRangeMessage, range.Message);

        // La búsqueda corre DESPUÉS del filtro de zona: el código de una ruta de Z1 no la trae si se filtra por Z2.
        Assert.Empty(await reader.ListAsync(new TripListQuery(Date: today, DispatchZoneId: f.Zone2, Search: "2026-0001"), default));
        Assert.Equal(new[] { a.PublicId },
            (await reader.ListAsync(new TripListQuery(Date: today, DispatchZoneId: f.Zone1, Search: "2026-0001"), default)).Select(i => i.PublicId));

        // Filtro por chofer: el propio filtra; uno de otro tenant es 404 (aislamiento por parámetro de filtro).
        Assert.Equal(new[] { a.PublicId, b.PublicId },
            (await reader.ListAsync(new TripListQuery(Date: today, DriverPublicId: await DriverPublicIdAsync(f, 7)), default)).Select(i => i.PublicId));
        var foreign = await f.SeedForeignDriverAsync();
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => reader.ListAsync(new TripListQuery(DriverPublicId: foreign), default));
        Assert.Equal("Chofer no encontrado.", nf.Message);
    }

    // ================================================================ zona de otro tenant

    [Fact]
    public async Task Create_and_update_with_a_zone_of_another_tenant_are_404()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var foreignZone = await f.SeedForeignZoneAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var svc = f.Get<TripService>();

        var create = await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.CreateAsync(new TripCreateRequest(TripServiceFixture.Today, DispatchZoneId: foreignZone), default));
        Assert.Equal("Zona de despacho no encontrada.", create.Message);
        var update = await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.UpdateAsync(publicId, new TripPatchRequest(DispatchZoneId: foreignZone), default));
        Assert.Equal("Zona de despacho no encontrada.", update.Message);

        Assert.Equal(1, await f.Db.Trips.AsNoTracking().CountAsync());
        Assert.Equal(f.Zone1, await f.Db.Trips.AsNoTracking().Where(t => t.TripId == tripId).Select(t => t.DispatchZoneId).SingleAsync());
    }

    // ================================================================ CATALOG apagado

    [Fact]
    public async Task With_catalog_disabled_every_driver_or_vehicle_action_is_module_disabled()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var (tripId, publicId) = await f.SeedTripAsync("2026-0001", TripStatuses.Planned, f.Zone1);
        await f.SetModuleEnabledAsync(ModuleKeys.Catalog, false);
        var trips = f.Get<TripService>();
        var dispatch = f.Get<TripDispatchService>();

        var calls = new Func<Task>[]
        {
            () => dispatch.DispatchAsync(publicId, null, default),
            () => dispatch.DispatchBatchAsync(new TripBatchDispatchRequest(new[] { publicId }), default),
            () => trips.ReassignZoneAsync(new ZoneReassignRequest(TripServiceFixture.Today, new[] { f.Zone1 }, Guid.NewGuid()), default),
            () => trips.UpdateAsync(publicId, new TripPatchRequest(DriverPublicId: Guid.NewGuid()), default),
            () => trips.UpdateAsync(publicId, new TripPatchRequest(VehiclePublicId: Guid.NewGuid()), default),
        };
        foreach (var call in calls)
        {
            var ex = await Assert.ThrowsAsync<ModuleDisabledException>(call);
            Assert.Equal(403, ex.StatusCode);
            Assert.Equal("module_disabled", ex.Code);
        }

        var trip = await f.Db.Trips.AsNoTracking().SingleAsync(t => t.TripId == tripId);
        Assert.Equal(TripStatuses.Planned, f.CodeOf(trip.StatusCodeId));
        Assert.Equal(7, trip.DriverId);
        Assert.Equal(9, trip.VehicleId);
    }
}
