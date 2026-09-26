using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
using DispatchBatchRules = Teikem.Domain.Trips.DispatchBatchRules;
using RouteStopEntity = Teikem.Domain.Trips.RouteStop;
using TripEntity = Teikem.Domain.Trips.Trip;
using TripOrderEntity = Teikem.Domain.Trips.TripOrder;
using TripRoute = Teikem.Domain.Trips.Route;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 (P4): efectos de TripStatus (despacho, salida, cierre y 'Eliminar ruta'), la salida de TripLifecycleService y el
/// efecto de OrderStatus que libera órdenes de rutas abiertas. InMemory con StatusService, RouteWriter y los efectos reales
/// (resueltos por DI de forma perezosa, como en producción). Pipelines sembrados igual que logistica-db-seed.sql.
/// </summary>
public class TripStatusEffectTests
{
    private const int TenantId = 1;
    private const string TripCode = "2026-0001";

    // ================================================================ despacho (→ DISPATCHED)

    [Fact]
    public async Task Dispatch_from_planned_freezes_route_and_moves_orders_to_planned_step_by_step()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 2);

        await f.TransitionTripAsync(trip, TripStatuses.Dispatched);

        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Dispatched), (await f.Db.Trips.SingleAsync()).StatusCodeId);
        var route = await f.Db.Routes.SingleAsync(r => r.IsActive);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Active), route.StatusCodeId);
        Assert.Equal(new[] { "Despachada" }, await f.CommentsAsync(EntityTypes.Route, route.RouteId));

        foreach (var orderId in orders)
        {
            var order = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orderId);
            Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Planned), order.StatusCodeId);
            Assert.NotEqual(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), order.StatusCodeId);
            Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned },
                await f.HistoryCodesAsync(EntityTypes.TransportOrder, orderId));
            Assert.All(await f.CommentsAsync(EntityTypes.TransportOrder, orderId), c => Assert.Equal($"Despachada en la ruta {TripCode}", c));
        }

        // Las paradas siguen en PENDING y las órdenes siguen en la ruta.
        Assert.All(await f.Db.RouteStops.ToListAsync(), s => Assert.Equal(f.Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending), s.StatusCodeId));
        Assert.Equal(2, await f.Db.TripOrders.CountAsync(x => x.IsCurrent));
    }

    [Fact]
    public async Task Dispatch_of_a_never_optimized_route_walks_draft_optimized_active_with_manual_comment()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Draft, OrderStatuses.Confirmed, 1);

        await f.TransitionTripAsync(trip, TripStatuses.Dispatched);

        var route = await f.Db.Routes.SingleAsync(r => r.IsActive);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Active), route.StatusCodeId);
        Assert.Equal(new[] { RouteStatuses.Optimized, RouteStatuses.Active }, await f.HistoryCodesAsync(EntityTypes.Route, route.RouteId));
        Assert.All(await f.CommentsAsync(EntityTypes.Route, route.RouteId), c => Assert.Equal("Secuencia manual confirmada al despachar", c));
    }

    [Fact]
    public async Task Dispatch_with_planned_disabled_leaves_orders_in_the_last_enabled_stage_before_it()
    {
        await using var f = await Fixture.CreateAsync();
        await f.DisableAsync(StatusDomains.OrderStatus, OrderStatuses.Planned);
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1);

        await f.TransitionTripAsync(trip, TripStatuses.Dispatched);

        var order = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orders[0]);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Inbound), order.StatusCodeId);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Inbound }, await f.HistoryCodesAsync(EntityTypes.TransportOrder, orders[0]));
    }

    [Fact]
    public async Task Dispatch_with_route_active_disabled_is_rejected()
    {
        // Riesgo 2 del plan: si el tenant deshabilita ACTIVE en RouteStatus, despachar responde un 422 explícito.
        await using var f = await Fixture.CreateAsync();
        await f.DisableAsync(StatusDomains.RouteStatus, RouteStatuses.Active);
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Dispatched));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("El pipeline de rutas de esta compañía no tiene habilitada la etapa ACTIVE.", ex.Message);
        Assert.Equal(DispatchBatchRules.PipelineMissing(RouteStatuses.Active), ex.Message);

        // Nada quedó escrito: la ruta, la versión y las órdenes siguen como estaban.
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Planned), (await f.Db.Trips.AsNoTracking().SingleAsync()).StatusCodeId);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Optimized), (await f.Db.Routes.AsNoTracking().SingleAsync()).StatusCodeId);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Confirmed),
            (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[0])).StatusCodeId);
    }

    [Fact]
    public async Task Dispatch_without_vehicle_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1, withVehicle: false);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Dispatched));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal($"La ruta {TripCode} no se puede despachar: {DispatchBatchRules.NoVehicleMessage.TrimEnd('.')}.", ex.Message);
    }

    [Fact]
    public async Task Dispatch_without_driver_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1, withDriver: false);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Dispatched));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal($"La ruta {TripCode} no se puede despachar: {DispatchBatchRules.NoDriverMessage.TrimEnd('.')}.", ex.Message);
    }

    [Fact]
    public async Task Dispatch_without_stops_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 0);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Dispatched));
        Assert.Contains(DispatchBatchRules.NoStopsMessage.TrimEnd('.'), ex.Message);
    }

    [Fact]
    public async Task Dispatch_rechecks_order_eligibility_under_lock()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1);
        await f.DenyAssignTripAsync(OrderStatuses.Confirmed);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Dispatched));
        Assert.Equal($"Orden {Fixture.OrderNumber(orders[0])}: El estatus actual no permite la acción 'ASSIGN_TRIP'.", ex.Message);
        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors!.ContainsKey(Fixture.OrderNumber(orders[0])));
    }

    // ================================================================ salida (→ IN_PROGRESS)

    [Fact]
    public async Task Start_sets_actual_start_and_moves_pipeline_orders_to_in_transit_skipping_laterals()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Planned, 2);
        await f.SetOrderStatusAsync(orders[1], OrderStatuses.OnHold);

        await f.Lifecycle.StartTrackedAsync(trip, null, default);
        await f.Db.SaveChangesAsync();

        var saved = await f.Db.Trips.SingleAsync();
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.InProgress), saved.StatusCodeId);
        Assert.NotNull(saved.ActualStartUtc);
        Assert.Equal(new[] { "Salida registrada" }, await f.CommentsAsync(EntityTypes.Trip, trip.TripId));

        var moved = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orders[0]);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), moved.StatusCodeId);
        Assert.Equal(new[] { $"Salió en la ruta {TripCode}" }, await f.CommentsAsync(EntityTypes.TransportOrder, orders[0]));

        var lateral = await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orders[1]);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.OnHold), lateral.StatusCodeId);
    }

    [Fact]
    public async Task Start_of_a_planned_trip_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Lifecycle.StartTrackedAsync(trip, null, default));
        Assert.Equal($"La ruta {TripCode} no está despachada; despáchela antes de registrar su salida.", ex.Message);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Start_of_a_dispatched_trip_without_driver_or_vehicle_is_rejected(bool withDriver, bool withVehicle)
    {
        // Defensa en profundidad: aunque la cabecera haya perdido el chofer o el vehículo, la ruta no sale.
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Planned, 1,
            withVehicle: withVehicle, withDriver: withDriver);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Lifecycle.StartTrackedAsync(trip, null, default));
        var missing = withDriver ? DispatchBatchRules.NoVehicleMessage : DispatchBatchRules.NoDriverMessage;
        Assert.Equal($"La ruta {TripCode} no se puede despachar: {missing.TrimEnd('.')}.", ex.Message);
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Dispatched), trip.StatusCodeId);
    }

    [Fact]
    public async Task Start_of_a_started_trip_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.InProgress, RouteStatuses.Active, OrderStatuses.InTransit, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Lifecycle.StartTrackedAsync(trip, null, default));
        Assert.Equal($"La ruta {TripCode} ya salió.", ex.Message);
    }

    // ================================================================ cierre (→ COMPLETED, Lote 7)

    [Fact]
    public async Task Completing_sets_actual_end_and_ends_current_assignments()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.InProgress, RouteStatuses.Active, OrderStatuses.InTransit, 2);
        trip.ActualStartUtc = DateTime.UtcNow.AddHours(-3);
        await f.Db.SaveChangesAsync();

        await f.TransitionTripAsync(trip, TripStatuses.Completed);

        var saved = await f.Db.Trips.SingleAsync();
        Assert.NotNull(saved.ActualEndUtc);
        Assert.True(saved.ActualEndUtc >= saved.ActualStartUtc);
        Assert.Equal(2, await f.Db.TripOrders.CountAsync());
        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent));
    }

    // ================================================================ 'Eliminar ruta' (→ CANCELLED)

    [Fact]
    public async Task Cancelling_from_planned_releases_orders_and_archives_the_route()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 2);

        await f.TransitionTripAsync(trip, TripStatuses.Cancelled);

        var saved = await f.Db.Trips.SingleAsync();
        Assert.False(saved.IsActive);
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Cancelled), saved.StatusCodeId);
        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent));
        Assert.False(await f.Db.RouteStops.AnyAsync());

        var route = await f.Db.Routes.SingleAsync();
        Assert.False(route.IsActive);
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Archived), route.StatusCodeId);

        // Liberar nunca cambia el estatus de la orden.
        foreach (var orderId in orders)
            Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Confirmed),
                (await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orderId)).StatusCodeId);
    }

    [Fact]
    public async Task Cancelling_a_dispatched_trip_is_rejected()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Planned, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionTripAsync(trip, TripStatuses.Cancelled));
        Assert.Equal($"La ruta {TripCode} ya fue despachada; no se puede eliminar.", ex.Message);
    }

    // ================================================================ nacimiento y otras entidades

    [Fact]
    public async Task Birth_and_other_entities_have_no_effect()
    {
        await using var f = await Fixture.CreateAsync();
        var (trip, orders) = await f.SeedTripAsync(TripStatuses.Draft, RouteStatuses.Draft, OrderStatuses.Confirmed, 1, withVehicle: false);
        var effect = f.Services.GetServices<IStatusTransitionEffect>().OfType<TripStatusEffect>().Single();
        var dispatched = await f.StatusAsync(StatusDomains.TripStatus, TripStatuses.Dispatched);
        var cancelled = await f.StatusAsync(StatusDomains.TripStatus, TripStatuses.Cancelled);
        var draft = await f.StatusAsync(StatusDomains.TripStatus, TripStatuses.Draft);

        // Nacimiento (From null): nada, aunque el destino fuera DISPATCHED y faltara el vehículo.
        await effect.OnTransitionedAsync(new StatusTransitionContext(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, null, dispatched, null), default);
        // Otra entidad del mismo dominio: nada.
        await effect.OnTransitionedAsync(new StatusTransitionContext(StatusDomains.TripStatus, EntityTypes.Vehicle, trip.TripId, draft, cancelled, null), default);
        await f.Db.SaveChangesAsync();

        Assert.True((await f.Db.Trips.SingleAsync()).IsActive);
        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[0]));
        Assert.Equal(f.Id(StatusDomains.RouteStatus, RouteStatuses.Draft), (await f.Db.Routes.SingleAsync()).StatusCodeId);
    }

    // ================================================================ TripOrderReleaseEffect (OrderStatus)

    [Fact]
    public async Task Cancelling_an_order_in_an_open_route_releases_it()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 2);

        await f.TransitionOrderAsync(orders[0], OrderStatuses.Cancelled);

        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[0]));
        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[1]));
        var stops = await f.Db.RouteStops.OrderBy(s => s.Sequence).ToListAsync();
        Assert.Single(stops);
        Assert.Equal(1, stops[0].Sequence);
    }

    [Fact]
    public async Task Sending_an_order_to_a_lateral_releases_it_from_a_draft_route()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, orders) = await f.SeedTripAsync(TripStatuses.Draft, RouteStatuses.Draft, OrderStatuses.Confirmed, 1);

        await f.TransitionOrderAsync(orders[0], OrderStatuses.OnHold);

        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[0]));
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.OnHold),
            (await f.Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orders[0])).StatusCodeId);
    }

    [Theory]
    [InlineData(TripStatuses.Dispatched, OrderStatuses.Planned)]
    [InlineData(TripStatuses.InProgress, OrderStatuses.InTransit)]
    public async Task Cancelling_an_order_in_a_dispatched_or_started_route_is_rejected(string tripStatus, string orderStatus)
    {
        await using var f = await Fixture.CreateAsync();
        var (_, orders) = await f.SeedTripAsync(tripStatus, RouteStatuses.Active, orderStatus, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.TransitionOrderAsync(orders[0], OrderStatuses.Cancelled));
        Assert.Equal($"La orden va en la ruta {TripCode} ya despachada; no se puede cancelar mientras la ruta esté en curso.", ex.Message);
    }

    [Fact]
    public async Task Lateral_in_a_dispatched_route_changes_nothing()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, orders) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Planned, 1);

        await f.TransitionOrderAsync(orders[0], OrderStatuses.OnHold);

        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[0]));
        Assert.Equal(1, await f.Db.RouteStops.CountAsync());
    }

    [Fact]
    public async Task Order_without_route_or_other_entity_is_a_no_op()
    {
        await using var f = await Fixture.CreateAsync();
        var (_, orders) = await f.SeedTripAsync(TripStatuses.Planned, RouteStatuses.Optimized, OrderStatuses.Confirmed, 1);
        var loose = await f.SeedOrderAsync(OrderStatuses.Confirmed);

        await f.TransitionOrderAsync(loose, OrderStatuses.Cancelled);   // sin ruta: no-op

        var effect = f.Services.GetServices<IStatusTransitionEffect>().OfType<TripOrderReleaseEffect>().Single();
        var confirmed = await f.StatusAsync(StatusDomains.OrderStatus, OrderStatuses.Confirmed);
        var cancelled = await f.StatusAsync(StatusDomains.OrderStatus, OrderStatuses.Cancelled);
        await effect.OnTransitionedAsync(new StatusTransitionContext(StatusDomains.OrderStatus, EntityTypes.Client, orders[0], confirmed, cancelled, null), default);
        await f.Db.SaveChangesAsync();

        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == orders[0]));
    }

    // ================================================================ fixture

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Dictionary<string, int> _statusIds = new(StringComparer.OrdinalIgnoreCase);
        private int _nextOrderId = 100;
        private int _nextStopId = 1000;

        private Fixture(TeikemDbContext db, ServiceProvider services, TestLookups lookups)
        {
            Db = db;
            Services = services;
            Lookups = lookups;
        }

        public TeikemDbContext Db { get; }
        public ServiceProvider Services { get; }
        public TestLookups Lookups { get; }
        public StatusService Statuses => Services.GetRequiredService<StatusService>();

        /// <summary>StartTrackedAsync no usa la ficha: TripReadService no hace falta.</summary>
        public TripLifecycleService Lifecycle => new(Db, Statuses, null!);

        public static string OrderNumber(int orderId) => $"2026-{orderId:000000}";

        public static async Task<Fixture> CreateAsync()
        {
            var tenant = new TenantContext { TenantId = TenantId, UserId = 1 };
            var options = new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("trip-status-effect-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new TeikemDbContext(options, tenant);
            var lookups = new TestLookups();

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton<ITenantContext>(tenant);
            services.AddSingleton<ILookupCache>(lookups);
            services.AddSingleton<IStatusTransitionEffect>(sp => new TripStatusEffect(db, sp, lookups));
            services.AddSingleton<IStatusTransitionEffect>(sp => new TripOrderReleaseEffect(db, sp));
            // PermissionService solo lo usa GetHistoryAsync (no se usa aquí).
            services.AddSingleton(sp => new StatusService(db, tenant, lookups, sp.GetServices<IStatusTransitionEffect>(), null!));
            services.AddSingleton<RouteWriter>();
            var provider = services.BuildServiceProvider();

            var f = new Fixture(db, provider, lookups);
            await f.SeedCatalogsAsync();
            return f;
        }

        public int Id(string domain, string code) => _statusIds[domain + "|" + code];

        public Task<StatusCode> StatusAsync(string domain, string code)
            => Db.StatusCodes.AsNoTracking().Include(s => s.StageKind).SingleAsync(s => s.Entity == domain && s.InternalCode == code);

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
                L(LookupDomains.EntityType, EntityTypes.Client),
                L(LookupDomains.EntityType, EntityTypes.Vehicle),
                L(LookupDomains.Capability, Capabilities.AssignTrip),
                L(LookupDomains.StopType, StopTypes.Pickup),
                L(LookupDomains.StopType, StopTypes.Delivery),
            };
            Db.LookupCodes.AddRange(all);
            Lookups.Load(all);

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
            S(StatusDomains.OrderStatus, OrderStatuses.Partial, lat, 21);
            S(StatusDomains.OrderStatus, OrderStatuses.Failed, lat, 22);
            S(StatusDomains.OrderStatus, OrderStatuses.Cancelled, term, 30);

            S(StatusDomains.StopStatus, StopStatuses.Pending, pipe, 1, true);
            S(StatusDomains.StopStatus, StopStatuses.EnRoute, pipe, 2);
            S(StatusDomains.StopStatus, StopStatuses.Completed, term, 3);
            S(StatusDomains.StopStatus, StopStatuses.Failed, lat, 4);

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
            S(StatusDomains.RouteStopStatus, "ON_THE_WAY", pipe, 2);
            S(StatusDomains.RouteStopStatus, "ARRIVED", pipe, 3);
            S(StatusDomains.RouteStopStatus, "COMPLETED", term, 4);
            S(StatusDomains.RouteStopStatus, "FAILED", lat, 5);

            Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Tenant de prueba", MaxStopsPerRouteDefault = 25, IsActive = true });
            await Db.SaveChangesAsync();
        }

        /// <summary>Orden activa, no especial, con una parada DELIVERY pendiente.</summary>
        public async Task<int> SeedOrderAsync(string orderStatus)
        {
            var orderId = _nextOrderId++;
            Db.TransportOrders.Add(new TransportOrder
            {
                TransportOrderId = orderId, PublicId = Guid.NewGuid(), TenantId = TenantId, ClientId = 1,
                OrderNumber = OrderNumber(orderId), PackBatchNumber = $"PB{orderId}", ClientInvoiceNumber = $"F{orderId}",
                ServiceTypeLookupId = 1, StatusCodeId = Id(StatusDomains.OrderStatus, orderStatus), IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
            Db.OrderStops.Add(new OrderStop
            {
                OrderStopId = _nextStopId++, TransportOrderId = orderId,
                StopTypeLookupId = await Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery),
                Sequence = 2, SnapLine1 = "Calle 1", SnapCity = "San Juan", SnapPostalCode = "00901",
                StatusCodeId = Id(StatusDomains.StopStatus, StopStatuses.Pending),
            });
            await Db.SaveChangesAsync();
            return orderId;
        }

        /// <summary>Ruta con su versión vigente y 'orderCount' órdenes en secuencia 1..N; devuelve el Trip tracked.</summary>
        public async Task<(TripEntity Trip, List<int> Orders)> SeedTripAsync(
            string tripStatus, string routeStatus, string orderStatus, int orderCount, bool withVehicle = true, bool withDriver = true)
        {
            var orders = new List<int>();
            for (var i = 0; i < orderCount; i++) orders.Add(await SeedOrderAsync(orderStatus));

            var trip = new TripEntity
            {
                TripId = 1, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = TripCode,
                PlanDate = DateOnly.FromDateTime(DateTime.UtcNow), DriverId = withDriver ? 7 : null, VehicleId = withVehicle ? 9 : null,
                StatusCodeId = Id(StatusDomains.TripStatus, tripStatus), PlannedStartUtc = DateTime.UtcNow.Date.AddHours(12),
                IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Trips.Add(trip);
            var route = new TripRoute
            {
                RouteId = 1, TripId = trip.TripId, Version = 1, IsActive = true,
                StatusCodeId = Id(StatusDomains.RouteStatus, routeStatus), StopCount = orderCount, CreatedAtUtc = DateTime.UtcNow,
            };
            Db.Routes.Add(route);

            var seq = 1;
            foreach (var orderId in orders)
            {
                var stopId = await Db.OrderStops.Where(s => s.TransportOrderId == orderId).Select(s => s.OrderStopId).SingleAsync();
                Db.TripOrders.Add(new TripOrderEntity
                {
                    TripOrderId = orderId, TenantId = TenantId, TripId = trip.TripId, TransportOrderId = orderId,
                    SortHint = seq, IsCurrent = true, AssignedAtUtc = DateTime.UtcNow, AssignedBy = 1,
                });
                Db.RouteStops.Add(new RouteStopEntity
                {
                    RouteStopId = stopId, RouteId = route.RouteId, OrderStopId = stopId, Sequence = seq++,
                    StatusCodeId = Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending),
                });
            }
            await Db.SaveChangesAsync();
            return (trip, orders);
        }

        public async Task SetOrderStatusAsync(int orderId, string code)
        {
            var order = await Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orderId);
            order.StatusCodeId = Id(StatusDomains.OrderStatus, code);
            await Db.SaveChangesAsync();
        }

        /// <summary>Override del tenant que deshabilita la etapa (StatusCodeOverride.IsEnabled = false).</summary>
        public async Task DisableAsync(string domain, string code)
        {
            Db.StatusCodeOverrides.Add(new StatusCodeOverride { TenantId = TenantId, StatusCodeId = Id(domain, code), IsEnabled = false });
            await Db.SaveChangesAsync();
        }

        /// <summary>Regla del tenant que niega ASSIGN_TRIP en ese estatus de orden.</summary>
        public async Task DenyAssignTripAsync(string orderStatus)
        {
            Db.StatusCapabilities.Add(new StatusCapability
            {
                TenantId = TenantId,
                EntityTypeLookupId = await Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder),
                StatusCodeId = Id(StatusDomains.OrderStatus, orderStatus),
                CapabilityLookupId = await Lookups.GetIdAsync(LookupDomains.Capability, Capabilities.AssignTrip),
                IsAllowed = false,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>Lo mismo que hacen los servicios: transición y luego el estatus en la entidad, todo en una unidad de trabajo.</summary>
        public async Task TransitionTripAsync(TripEntity trip, string toCode)
        {
            var to = await Statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId, toCode, null, default);
            trip.StatusCodeId = to.StatusCodeId;
            await Db.SaveChangesAsync();
        }

        public async Task TransitionOrderAsync(int orderId, string toCode)
        {
            var order = await Db.TransportOrders.SingleAsync(o => o.TransportOrderId == orderId);
            var to = await Statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, orderId, order.StatusCodeId, toCode, null, default);
            order.StatusCodeId = to.StatusCodeId;
            await Db.SaveChangesAsync();
        }

        public async Task<List<string>> HistoryCodesAsync(string entityType, int entityId)
        {
            var typeId = await Lookups.GetIdAsync(LookupDomains.EntityType, entityType);
            var toIds = await Db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
                .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
            var codes = _statusIds.ToDictionary(kv => kv.Value, kv => kv.Key.Split('|')[1]);
            return toIds.Select(id => codes[id]).ToList();
        }

        public async Task<List<string?>> CommentsAsync(string entityType, int entityId)
        {
            var typeId = await Lookups.GetIdAsync(LookupDomains.EntityType, entityType);
            return await Db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == entityId)
                .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.Comment).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    /// <summary>ILookupCache en memoria sobre los LookupCode sembrados.</summary>
    private sealed class TestLookups : ILookupCache
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
