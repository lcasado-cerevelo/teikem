using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;
using RouteStopEntity = Teikem.Domain.Trips.RouteStop;
using TripEntity = Teikem.Domain.Trips.Trip;
using TripOrderEntity = Teikem.Domain.Trips.TripOrder;
using TripRoute = Teikem.Domain.Trips.Route;
using TripRules = Teikem.Domain.Trips.TripRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P0: RouteWriter, la única vía de escritura de TripOrder, Route y RouteStop (InMemory con StatusService real).
/// Agregar es atómico y re-verifica elegibilidad y ruta vigente; liberar solo toca la versión vigente de ESA ruta; el
/// contenido de una ruta despachada queda congelado pero sus tiempos se pueden recalcular; una ruta terminal no se recalcula.
/// </summary>
public class RouteWriterTests
{
    private const int TenantId = 1;

    // ================================================================ agregar

    [Fact]
    public async Task AddOrders_creates_current_links_and_pending_stops_in_a_new_draft_route()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Planned);

        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2 }, default);
        await f.Db.SaveChangesAsync();

        var links = await f.Db.TripOrders.AsNoTracking().OrderBy(x => x.SortHint).ToListAsync();
        Assert.Equal(new[] { o1, o2 }, links.Select(l => l.TransportOrderId));
        Assert.All(links, l => Assert.True(l.IsCurrent));
        Assert.All(links, l => Assert.Equal(TenantId, l.TenantId));
        Assert.All(links, l => Assert.Equal(1, l.AssignedBy));

        var route = await f.Db.Routes.AsNoTracking().SingleAsync();
        Assert.Equal(1, route.Version);
        Assert.True(route.IsActive);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Draft), route.StatusCodeId);
        Assert.Equal(2, route.StopCount);

        var stops = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, stops.Select(s => s.Sequence));
        Assert.Equal(new[] { f.StopOf(o1), f.StopOf(o2) }, stops.Select(s => s.OrderStopId));
        Assert.All(stops, s => Assert.Equal(f.Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending), s.StatusCodeId));
        // ETAs recalculadas (sin coordenadas: 15 min por tramo) y fin planificado de la ruta.
        Assert.All(stops, s => Assert.NotNull(s.PlannedArrivalUtc));
        Assert.All(stops, s => Assert.Equal(15, s.DurationFromPrevMin));
        Assert.NotNull((await f.Db.Trips.AsNoTracking().SingleAsync()).PlannedEndUtc);

        // Historial: nacimiento de la versión (ROUTE) y ninguno por parada; el estatus de las órdenes no cambia.
        Assert.Equal(1, await f.HistoryCountAsync(EntityTypes.Route, route.RouteId));
        Assert.Equal(1, await f.Db.EntityStatusHistories.CountAsync());
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Confirmed), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == o1)).StatusCodeId);
    }

    [Fact]
    public async Task Adding_to_an_existing_route_appends_at_the_end()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1 }, default);
        await f.Db.SaveChangesAsync();

        await f.Writer.AddOrdersAsync(trip, new[] { o2 }, default);
        await f.Db.SaveChangesAsync();

        Assert.Single(await f.Db.Routes.ToListAsync());
        var stops = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(new[] { f.StopOf(o1), f.StopOf(o2) }, stops.Select(s => s.OrderStopId));
    }

    [Fact]
    public async Task Duplicate_and_order_in_another_route_are_409()
    {
        await using var f = await Fx.CreateAsync();
        var a = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var b = await f.SeedTripAsync("2026-0002", TripStatuses.Planned);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(a, new[] { o1 }, default);
        await f.Db.SaveChangesAsync();

        var dup = await Assert.ThrowsAsync<ConflictException>(() => f.Writer.AddOrdersAsync(a, new[] { o1 }, default));
        Assert.Equal("La orden ya está en esta ruta.", dup.Message);
        var other = await Assert.ThrowsAsync<ConflictException>(() => f.Writer.AddOrdersAsync(b, new[] { o1 }, default));
        Assert.Equal("La orden ya está asignada a la ruta 2026-0001.", other.Message);
    }

    [Fact]
    public async Task Ineligible_orders_reject_the_whole_request()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var ok = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var draft = await f.SeedOrderAsync(OrderStatuses.Draft);
        var special = await f.SeedOrderAsync(OrderStatuses.Confirmed, special: true);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.AddOrdersAsync(trip, new[] { ok, draft, special }, default));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("Hay órdenes que no se pueden asignar a la ruta.", ex.Message);
        Assert.Equal(new[] { "La orden está en Entrada; confírmela antes de asignarla a una ruta." }, ex.Errors![Fx.OrderNumber(draft)]);
        Assert.Equal(new[] { "Las entregas especiales se asignan al chofer desde la orden; no pasan por Sala de despacho." }, ex.Errors[Fx.OrderNumber(special)]);
        Assert.False(ex.Errors.ContainsKey(Fx.OrderNumber(ok)));

        Assert.False(await f.Db.TripOrders.AnyAsync());
        Assert.False(await f.Db.RouteStops.AnyAsync());
    }

    [Fact]
    public async Task Orders_of_different_clients_consolidate_in_one_route_even_with_the_same_number()
    {
        // Consolidación multi-cliente (maestro L263): la numeración es por cliente, así que dos clientes pueden repetir el
        // número; la ruta los distingue por id (TripOrder/RouteStop) y no por número.
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var a = await f.SeedOrderAsync(OrderStatuses.Confirmed, clientId: 1, orderNumber: "R-00001");
        var b = await f.SeedOrderAsync(OrderStatuses.Confirmed, clientId: 2, orderNumber: "R-00001");

        await f.Writer.AddOrdersAsync(trip, new[] { a, b }, default);
        await f.Db.SaveChangesAsync();

        Assert.Equal(new[] { a, b }, (await f.Db.TripOrders.AsNoTracking().OrderBy(x => x.SortHint).ToListAsync()).Select(l => l.TransportOrderId));
        Assert.Equal(new[] { f.StopOf(a), f.StopOf(b) }, (await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync()).Select(s => s.OrderStopId));
    }

    [Fact]
    public async Task Ineligible_orders_of_two_clients_with_the_same_number_share_the_error_key_and_nothing_is_written()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var ok = await f.SeedOrderAsync(OrderStatuses.Confirmed, clientId: 1, orderNumber: "R-00002");
        var draft = await f.SeedOrderAsync(OrderStatuses.Draft, clientId: 1, orderNumber: "R-00001");
        var lateral = await f.SeedOrderAsync(OrderStatuses.OnHold, clientId: 2, orderNumber: "R-00001");

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.AddOrdersAsync(trip, new[] { ok, draft, lateral }, default));
        Assert.Equal(422, ex.StatusCode);
        // La clave de errors es el número de orden (formato {número: [motivo]}); si dos clientes lo comparten, sus motivos
        // quedan bajo la misma clave sin perder ninguno y la solicitud completa se rechaza.
        Assert.Equal(new[] { "R-00001" }, ex.Errors!.Keys);
        Assert.Equal(new[]
        {
            "La orden está en Entrada; confírmela antes de asignarla a una ruta.",
            "La orden está en 'ON_HOLD'; regrésela al pipeline antes de asignarla a una ruta.",
        }, ex.Errors["R-00001"]);
        Assert.False(await f.Db.TripOrders.AnyAsync());
        Assert.False(await f.Db.RouteStops.AnyAsync());
    }

    [Fact]
    public async Task Order_without_assign_trip_capability_is_not_eligible()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.DenyAssignTripAsync(OrderStatuses.Confirmed);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.AddOrdersAsync(trip, new[] { o }, default));
        Assert.Equal(new[] { "El estatus actual no permite la acción 'ASSIGN_TRIP'." }, ex.Errors![Fx.OrderNumber(o)]);
    }

    [Fact]
    public async Task Order_of_another_tenant_is_404()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var foreign = await f.SeedOrderAsync(OrderStatuses.Confirmed, tenantId: 2);
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => f.Writer.AddOrdersAsync(trip, new[] { foreign }, default));
        Assert.Equal("Orden no encontrado.", ex.Message);
    }

    // ================================================================ liberar

    [Fact]
    public async Task ReleaseOrder_deletes_link_and_stop_and_resequences()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Planned);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o3 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2, o3 }, default);
        await f.Db.SaveChangesAsync();

        await f.Writer.ReleaseOrderAsync(trip, o2, default);
        await f.Db.SaveChangesAsync();

        Assert.Equal(new[] { o1, o3 }, await f.Db.TripOrders.AsNoTracking().OrderBy(x => x.TransportOrderId).Select(x => x.TransportOrderId).ToListAsync());
        var stops = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, stops.Select(s => s.Sequence));
        Assert.Equal(new[] { f.StopOf(o1), f.StopOf(o3) }, stops.Select(s => s.OrderStopId));
        Assert.Equal(2, (await f.Db.Routes.AsNoTracking().SingleAsync()).StopCount);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Confirmed), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == o2)).StatusCodeId);
    }

    [Fact]
    public async Task Releasing_an_order_that_is_not_in_the_route_is_404()
    {
        await using var f = await Fx.CreateAsync();
        var a = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var b = await f.SeedTripAsync("2026-0002", TripStatuses.Draft);
        var inB = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(b, new[] { inB }, default);
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TripMemberNotFoundException>(() => f.Writer.ReleaseOrderAsync(a, inB, default));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("La orden no está en esta ruta.", ex.Message);
        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.TripId == b.TripId && x.TransportOrderId == inB && x.IsCurrent));
    }

    [Fact]
    public async Task Release_never_touches_archived_versions_nor_other_trips()
    {
        await using var f = await Fx.CreateAsync();
        var a = await f.SeedTripAsync("2026-0001", TripStatuses.Planned);
        var b = await f.SeedTripAsync("2026-0002", TripStatuses.Planned);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var ob = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(a, new[] { o1, o2 }, default);
        await f.Writer.AddOrdersAsync(b, new[] { ob }, default);
        await f.Db.SaveChangesAsync();

        // Versión 2 de la ruta A (la 1 queda ARCHIVED con sus dos paradas).
        await f.Writer.ReplaceActiveRouteAsync(a, new[] { f.StopOf(o2), f.StopOf(o1) }, RouteStatuses.Optimized, "Optimización #1", default);
        await f.Db.SaveChangesAsync();
        var v1 = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.TripId == a.TripId && r.Version == 1);

        await f.Writer.ReleaseOrderAsync(a, o1, default);
        await f.Db.SaveChangesAsync();

        Assert.Equal(2, await f.Db.RouteStops.CountAsync(s => s.RouteId == v1.RouteId));
        var v2 = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.TripId == a.TripId && r.IsActive);
        Assert.Equal(new[] { f.StopOf(o2) }, await f.Db.RouteStops.Where(s => s.RouteId == v2.RouteId).Select(s => s.OrderStopId).ToListAsync());

        await f.Writer.ReleaseAllAsync(a, default);
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.TripId == a.TripId));
        Assert.Equal(2, await f.Db.RouteStops.CountAsync(s => s.RouteId == v1.RouteId));
        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.TripId == b.TripId && x.TransportOrderId == ob && x.IsCurrent));
        var bRoute = await f.Db.Routes.AsNoTracking().SingleAsync(r => r.TripId == b.TripId);
        Assert.Equal(1, await f.Db.RouteStops.CountAsync(s => s.RouteId == bRoute.RouteId));
    }

    // ================================================================ versiones y secuencia

    [Fact]
    public async Task ReplaceActiveRoute_creates_version_n_plus_1_and_keeps_a_single_active_one()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2 }, default);
        await f.Db.SaveChangesAsync();

        await f.Writer.ReplaceActiveRouteAsync(trip, new[] { f.StopOf(o2), f.StopOf(o1) }, RouteStatuses.Optimized, "Optimización #7", default);
        await f.Db.SaveChangesAsync();
        await f.Writer.RecomputeAsync(trip, default);
        await f.Db.SaveChangesAsync();

        var routes = await f.Db.Routes.AsNoTracking().OrderBy(r => r.Version).ToListAsync();
        Assert.Equal(new[] { 1, 2 }, routes.Select(r => r.Version));
        Assert.Equal(new[] { false, true }, routes.Select(r => r.IsActive));
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Archived), routes[0].StatusCodeId);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Optimized), routes[1].StatusCodeId);
        Assert.Equal(2, routes[1].StopCount);
        var stops = await f.Db.RouteStops.AsNoTracking().Where(s => s.RouteId == routes[1].RouteId).OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(new[] { f.StopOf(o2), f.StopOf(o1) }, stops.Select(s => s.OrderStopId));
        Assert.Contains("Optimización #7", await f.CommentsAsync(EntityTypes.Route, routes[1].RouteId));

        // Otra vez: v3, sigue habiendo una sola vigente.
        await f.Writer.ReplaceActiveRouteAsync(trip, new[] { f.StopOf(o1), f.StopOf(o2) }, RouteStatuses.Optimized, "Optimización #8", default);
        await f.Db.SaveChangesAsync();
        Assert.Equal(3, await f.Db.Routes.CountAsync());
        Assert.Equal(3, (await f.Db.Routes.SingleAsync(r => r.IsActive)).Version);
    }

    [Fact]
    public async Task ReplaceActiveRoute_requires_exactly_the_current_stops()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1 }, default);
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.ReplaceActiveRouteAsync(trip, new[] { f.StopOf(o1), 999999 }, RouteStatuses.Optimized, null, default));
    }

    [Fact]
    public async Task ApplySequence_reorders_and_rejects_invalid_permutations()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2 }, default);
        await f.Db.SaveChangesAsync();
        var ids = await f.Db.RouteStops.OrderBy(s => s.Sequence).Select(s => s.RouteStopId).ToListAsync();

        var bad = await Assert.ThrowsAsync<ValidationException>(() => f.Writer.ApplySequenceAsync(trip, new[] { ids[0], ids[0] }, default));
        Assert.Equal(400, bad.StatusCode);
        Assert.Equal("La secuencia debe incluir exactamente las paradas de la ruta vigente, sin repetir.", bad.Message);
        await Assert.ThrowsAsync<ValidationException>(() => f.Writer.ApplySequenceAsync(trip, new[] { ids[1], 424242 }, default));

        await f.Writer.ApplySequenceAsync(trip, new[] { ids[1], ids[0] }, default);
        await f.Db.SaveChangesAsync();
        Assert.Equal(new[] { ids[1], ids[0] }, await f.Db.RouteStops.OrderBy(s => s.Sequence).Select(s => s.RouteStopId).ToListAsync());
    }

    [Fact]
    public async Task Reorder_recomputes_etas_in_the_same_version_without_an_optimization_run()
    {
        // Lo mismo que hace RouteOptimizationService.ReorderAsync: ApplySequence + RecomputeAsync (L265: sin re-optimizar).
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2 }, default);
        await f.Db.SaveChangesAsync();
        var before = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();
        var ids = before.Select(s => s.RouteStopId).ToList();
        var versionBefore = (await f.Db.Routes.AsNoTracking().SingleAsync(r => r.IsActive)).Version;

        await f.Writer.ApplySequenceAsync(trip, new[] { ids[1], ids[0] }, default);
        await f.Db.SaveChangesAsync();
        await f.Writer.RecomputeAsync(trip, default);
        await f.Db.SaveChangesAsync();

        var after = await f.Db.RouteStops.AsNoTracking().OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(new[] { ids[1], ids[0] }, after.Select(s => s.RouteStopId));
        Assert.All(after, s => Assert.NotNull(s.PlannedArrivalUtc));
        Assert.True(after[0].PlannedArrivalUtc < after[1].PlannedArrivalUtc);
        // La parada que pasó a ser la primera llega antes que cuando era la segunda.
        Assert.True(after[0].PlannedArrivalUtc < before[1].PlannedArrivalUtc);
        var active = await f.Db.Routes.AsNoTracking().Where(r => r.IsActive).ToListAsync();
        Assert.Single(active);
        Assert.Equal(versionBefore, active[0].Version);
        Assert.Empty(await f.Db.OptimizationRuns.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Hard_cap_allows_300_stops_and_rejects_one_more_without_writing()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var first = new List<int>();
        for (var i = 0; i < TripRules.MaxStopsHardCap; i++) first.Add(await f.SeedOrderAsync(OrderStatuses.Confirmed));
        await f.Writer.AddOrdersAsync(trip, first, default);
        await f.Db.SaveChangesAsync();
        Assert.Equal(300, await f.Db.RouteStops.CountAsync());

        var extra = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Writer.AddOrdersAsync(trip, new[] { extra }, default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Una ruta admite como máximo 300 paradas.", ex.Message);
        Assert.Equal(TripRules.MaxStopsHardCapMessage, ex.Message);
        Assert.Equal(new[] { TripRules.MaxStopsHardCapMessage }, ex.Errors!["orderPublicIds"]);

        Assert.False(f.Db.ChangeTracker.Entries<TripOrderEntity>().Any(e => e.State == EntityState.Added));
        Assert.False(f.Db.ChangeTracker.Entries<RouteStopEntity>().Any(e => e.State == EntityState.Added));
        Assert.False(await f.Db.TripOrders.AnyAsync(t => t.TransportOrderId == extra));
        Assert.Equal(300, await f.Db.RouteStops.CountAsync());
    }

    // ================================================================ ruta despachada / cerrada

    [Fact]
    public async Task Content_of_a_dispatched_route_is_frozen_but_times_can_be_recomputed()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1 }, default);
        await f.Db.SaveChangesAsync();
        trip.StatusCodeId = f.Id(StatusDomains.TripStatus, TripStatuses.Dispatched);
        await f.Db.SaveChangesAsync();
        const string frozen = "La ruta 2026-0001 ya fue despachada; no se puede editar ni eliminar.";

        Assert.Equal(frozen, (await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.AddOrdersAsync(trip, new[] { o2 }, default))).Message);
        Assert.Equal(frozen, (await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.ReleaseOrderAsync(trip, o1, default))).Message);
        Assert.Equal(frozen, (await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.ReleaseAllAsync(trip, default))).Message);
        Assert.Equal(frozen, (await Assert.ThrowsAsync<StatusRuleException>(() => f.Writer.ApplySequenceAsync(trip, Array.Empty<int>(), default))).Message);
        Assert.Equal(frozen, (await Assert.ThrowsAsync<StatusRuleException>(
            () => f.Writer.ReplaceActiveRouteAsync(trip, new[] { f.StopOf(o1) }, RouteStatuses.Optimized, null, default))).Message);

        // La hora de salida de una cabecera despachada (EDIT_TRIP) mueve las ETAs.
        trip.PlannedStartUtc = trip.PlannedStartUtc!.Value.AddHours(1);
        await f.Writer.RecomputeAsync(trip, default);
        await f.Db.SaveChangesAsync();
        var stop = await f.Db.RouteStops.AsNoTracking().SingleAsync();
        Assert.Equal(trip.PlannedStartUtc.Value.AddMinutes(15), stop.PlannedArrivalUtc);
    }

    // ================================================================ toda mutación marca el Trip (RowVersion)

    [Theory]
    [InlineData("add")]
    [InlineData("release")]
    [InlineData("releaseAll")]
    [InlineData("sequence")]
    [InlineData("recompute")]
    public async Task Every_mutation_marks_the_trip_as_modified_so_its_rowversion_changes(string operation)
    {
        // Contrato de concurrencia del lote: el RowVersion del Trip cambia en cada mutación (en SQL Server, porque el Trip
        // queda Modified). La FASE 3 de la optimización detecta por él los cambios que no crean versión (agregar, quitar,
        // escanear, reordenar, pin), y los PATCH con un rowVersion anterior responden 409.
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o2 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var o3 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        await f.Writer.AddOrdersAsync(trip, new[] { o1, o2 }, default);
        await f.Db.SaveChangesAsync();
        var ids = await f.Db.RouteStops.OrderBy(s => s.Sequence).Select(s => s.RouteStopId).ToListAsync();

        var before = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        f.Db.Entry(trip).Property(t => t.UpdatedAtUtc).CurrentValue = before;
        f.Db.Entry(trip).State = EntityState.Unchanged;

        switch (operation)
        {
            case "add": await f.Writer.AddOrdersAsync(trip, new[] { o3 }, default); break;
            case "release": await f.Writer.ReleaseOrderAsync(trip, o1, default); break;
            case "releaseAll": await f.Writer.ReleaseAllAsync(trip, default); break;
            case "sequence": await f.Writer.ApplySequenceAsync(trip, new[] { ids[1], ids[0] }, default); break;
            default: await f.Writer.RecomputeAsync(trip, default); break;   // lo que usa también el pin manual
        }

        Assert.Equal(EntityState.Modified, f.Db.Entry(trip).State);
        Assert.NotEqual(before, trip.UpdatedAtUtc);
    }

    [Fact]
    public async Task Recompute_on_a_terminal_route_throws()
    {
        await using var f = await Fx.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.RecomputeAsync(trip, default));
    }

    [Fact]
    public async Task An_untracked_trip_is_rejected()
    {
        await using var f = await Fx.CreateAsync();
        var seeded = await f.SeedTripAsync("2026-0001", TripStatuses.Draft);
        var o1 = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        f.Db.Entry(seeded).State = EntityState.Detached;
        var detached = await f.Db.Trips.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.AddOrdersAsync(detached, new[] { o1 }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.RecomputeAsync(detached, default));
    }

    [Fact]
    public async Task Eligibility_is_reported_per_order()
    {
        await using var f = await Fx.CreateAsync();
        var ok = await f.SeedOrderAsync(OrderStatuses.Confirmed);
        var lateral = await f.SeedOrderAsync(OrderStatuses.OnHold);
        var gone = await f.SeedOrderAsync(OrderStatuses.InTransit);
        var noStop = await f.SeedOrderAsync(OrderStatuses.Confirmed, withDelivery: false);
        var orders = await f.Db.TransportOrders.AsNoTracking().ToListAsync();

        var reasons = await f.Writer.CheckEligibilityAsync(orders, default);
        Assert.False(reasons.ContainsKey(ok));
        Assert.Equal("La orden está en 'ON_HOLD'; regrésela al pipeline antes de asignarla a una ruta.", reasons[lateral]);
        Assert.Equal("La orden ya salió a ruta o terminó; no se puede asignar a otra ruta.", reasons[gone]);
        Assert.Equal("La orden no tiene una parada de entrega pendiente.", reasons[noStop]);
    }

    // ================================================================ fixture

    private sealed class Fx : IAsyncDisposable
    {
        private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _stopByOrder = new();
        private int _nextOrderId = 5000;
        private int _nextStopId = 9000;
        private int _nextTripId = 700;

        private Fx(TeikemDbContext db, StatusService statuses, RouteWriter writer, Lookups lookups)
        {
            Db = db;
            Statuses = statuses;
            Writer = writer;
            LookupCache = lookups;
        }

        public TeikemDbContext Db { get; }
        public StatusService Statuses { get; }
        public RouteWriter Writer { get; }
        public Lookups LookupCache { get; }

        public static string OrderNumber(int orderId) => $"2026-{orderId:000000}";

        public static async Task<Fx> CreateAsync()
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
            var options = new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("route-writer-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new TeikemDbContext(options, tenant);
            var lookups = new Lookups();
            var statuses = new StatusService(db, tenant, lookups, Array.Empty<IStatusTransitionEffect>(), null!);
            var f = new Fx(db, statuses, new RouteWriter(db, statuses, lookups, tenant), lookups);
            await f.SeedCatalogsAsync();
            return f;
        }

        public int Id(string domain, string code) => _statusIds[domain + "|" + code];

        public int StopOf(int orderId) => _stopByOrder[orderId];

        private async Task SeedCatalogsAsync()
        {
            var id = 1;
            LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            var pipe = L(LookupDomains.StageKind, StageKinds.Pipeline);
            var lat = L(LookupDomains.StageKind, StageKinds.Lateral);
            var term = L(LookupDomains.StageKind, StageKinds.Terminal);
            var all = new List<LookupCode>
            {
                pipe, lat, term,
                L(LookupDomains.EntityType, EntityTypes.Trip),
                L(LookupDomains.EntityType, EntityTypes.Route),
                L(LookupDomains.EntityType, EntityTypes.TransportOrder),
                L(LookupDomains.Capability, Capabilities.AssignTrip),
                L(LookupDomains.StopType, StopTypes.Pickup),
                L(LookupDomains.StopType, StopTypes.Delivery),
            };
            Db.LookupCodes.AddRange(all);
            LookupCache.Load(all);

            var sid = 100;
            void S(string domain, string code, LookupCode kind, int sort, bool initial = false)
            {
                var s = new StatusCode { StatusCodeId = sid++, Entity = domain, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = kind.LookupCodeId, SortOrder = sort, IsInitial = initial, IsActive = true };
                Db.StatusCodes.Add(s);
                _statusIds[domain + "|" + code] = s.StatusCodeId;
            }

            // Igual que logistica-db-seed.sql
            S(StatusDomains.OrderStatus, OrderStatuses.Draft, pipe, 1, true);
            S(StatusDomains.OrderStatus, OrderStatuses.Confirmed, pipe, 2);
            S(StatusDomains.OrderStatus, OrderStatuses.Pickup, pipe, 3);
            S(StatusDomains.OrderStatus, OrderStatuses.Inbound, pipe, 4);
            S(StatusDomains.OrderStatus, OrderStatuses.Planned, pipe, 5);
            S(StatusDomains.OrderStatus, OrderStatuses.InTransit, pipe, 6);
            S(StatusDomains.OrderStatus, OrderStatuses.Arrived, pipe, 7);
            S(StatusDomains.OrderStatus, OrderStatuses.Delivered, term, 8);
            S(StatusDomains.OrderStatus, OrderStatuses.OnHold, lat, 20);
            S(StatusDomains.OrderStatus, OrderStatuses.Cancelled, term, 30);
            S(StatusDomains.StopStatus, StopStatuses.Pending, pipe, 1, true);
            S(StatusDomains.StopStatus, StopStatuses.Completed, term, 3);
            S(StatusDomains.TripStatus, TripStatuses.Draft, pipe, 1, true);
            S(StatusDomains.TripStatus, TripStatuses.Planned, pipe, 2);
            S(StatusDomains.TripStatus, TripStatuses.Dispatched, pipe, 3);
            S(StatusDomains.TripStatus, TripStatuses.InProgress, pipe, 4);
            S(StatusDomains.TripStatus, TripStatuses.Completed, term, 5);
            S(StatusDomains.TripStatus, TripStatuses.Cancelled, term, 6);
            S(StatusDomains.RouteStatus, RouteStatuses.Draft, pipe, 1, true);
            S(StatusDomains.RouteStatus, RouteStatuses.Optimized, pipe, 2);
            S(StatusDomains.RouteStatus, RouteStatuses.Active, pipe, 3);
            S(StatusDomains.RouteStatus, RouteStatuses.Archived, term, 4);
            S(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending, pipe, 1, true);
            S(StatusDomains.RouteStopStatus, RouteStopStatuses.Completed, term, 4);

            Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", MaxStopsPerRouteDefault = 25, IsActive = true });
            await Db.SaveChangesAsync();
        }

        public async Task<int> SeedOrderAsync(string status, bool special = false, int tenantId = TenantId, bool withDelivery = true,
            int clientId = 1, string? orderNumber = null)
        {
            var orderId = _nextOrderId++;
            Db.TransportOrders.Add(new TransportOrder
            {
                TransportOrderId = orderId, PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId,
                OrderNumber = orderNumber ?? OrderNumber(orderId), PackBatchNumber = $"PB{orderId}", ClientInvoiceNumber = $"F{orderId}",
                ServiceTypeLookupId = 1, StatusCodeId = Id(StatusDomains.OrderStatus, status), IsActive = true,
                IsSpecialDelivery = special, CreatedAtUtc = DateTime.UtcNow,
            });
            if (withDelivery)
            {
                var stopId = _nextStopId++;
                Db.OrderStops.Add(new OrderStop
                {
                    OrderStopId = stopId, TransportOrderId = orderId,
                    StopTypeLookupId = await LookupCache.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery),
                    Sequence = 2, SnapLine1 = "Calle 1", SnapCity = "San Juan", SnapPostalCode = "00901", ServiceMinutes = 10,
                    StatusCodeId = Id(StatusDomains.StopStatus, StopStatuses.Pending),
                });
                _stopByOrder[orderId] = stopId;
            }
            await Db.SaveChangesAsync();
            return orderId;
        }

        /// <summary>Ruta sin versión todavía, tracked (como la deja LockTripAsync).</summary>
        public async Task<TripEntity> SeedTripAsync(string code, string status)
        {
            var trip = new TripEntity
            {
                TripId = _nextTripId++, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = code,
                PlanDate = new DateOnly(2026, 9, 26), StatusCodeId = Id(StatusDomains.TripStatus, status),
                PlannedStartUtc = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Trips.Add(trip);
            await Db.SaveChangesAsync();
            return trip;
        }

        public async Task DenyAssignTripAsync(string orderStatus)
        {
            Db.StatusCapabilities.Add(new StatusCapability
            {
                TenantId = TenantId,
                EntityTypeLookupId = await LookupCache.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder),
                StatusCodeId = Id(StatusDomains.OrderStatus, orderStatus),
                CapabilityLookupId = await LookupCache.GetIdAsync(LookupDomains.Capability, Capabilities.AssignTrip),
                IsAllowed = false,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<int> HistoryCountAsync(string entityType, int entityId)
        {
            var typeId = await LookupCache.GetIdAsync(LookupDomains.EntityType, entityType);
            return await Db.EntityStatusHistories.CountAsync(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId);
        }

        public async Task<List<string?>> CommentsAsync(string entityType, int entityId)
        {
            var typeId = await LookupCache.GetIdAsync(LookupDomains.EntityType, entityType);
            return await Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
                .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.Comment).ToListAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    /// <summary>ILookupCache en memoria sobre los LookupCode sembrados.</summary>
    private sealed class Lookups : ILookupCache
    {
        private List<LookupCode> _all = new();

        public void Load(IEnumerable<LookupCode> all) => _all = all.ToList();

        public Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default)
            => Task.FromResult(Find(entity, code)?.LookupCodeId ?? throw new NotFoundException(entity, code));

        public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
            => Task.FromResult(Find(entity, code)?.LookupCodeId);

        public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
            => Task.FromResult(_all.FirstOrDefault(l => l.LookupCodeId == lookupCodeId));

        public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LookupCode>>(_all.Where(l => string.Equals(l.Entity, entity, StringComparison.OrdinalIgnoreCase)).ToList());

        public void Invalidate() { }

        private LookupCode? Find(string entity, string code)
            => _all.FirstOrDefault(l => string.Equals(l.Entity, entity, StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(l.InternalCode, code, StringComparison.OrdinalIgnoreCase));
    }
}
