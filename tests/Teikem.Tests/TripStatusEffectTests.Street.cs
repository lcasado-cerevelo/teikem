using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 215 (2026-10-24): asignar en la calle — mandar, mover y sacar órdenes de rutas despachadas o en curso, con aviso al chofer.</summary>
public partial class TripStatusEffectTests
{
    private static StreetAssignService StreetOf(Fixture f)
        => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), f.Statuses, f.Services.GetRequiredService<RouteWriter>(), null!, null!, null!, f.Lookups);

    private static async Task<(StreetAssignService Street, Teikem.Domain.Trips.Trip Trip, int InRoute, int Free)> LiveRouteAsync(Fixture f, string tripStatus, string orderStatus)
    {
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(tripStatus, RouteStatuses.Active, orderStatus, 1);
        var free = await f.SeedOrderAsync(OrderStatuses.Unassigned);
        return (StreetOf(f), trip, ids[0], free);
    }

    private static async Task<string> StatusCodeOfOrderAsync(Fixture f, int orderId)
        => (await f.Db.StatusCodes.AsNoTracking().SingleAsync(s => s.StatusCodeId == f.Db.TransportOrders.AsNoTracking().Single(o => o.TransportOrderId == orderId).StatusCodeId)).InternalCode;

    [Fact]
    public async Task Street_assign_to_a_dispatched_route_adds_the_stop_leaves_the_order_assigned_and_notifies_the_driver()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, trip, _, free) = await LiveRouteAsync(f, TripStatuses.Dispatched, OrderStatuses.Assigned);

        var r = await street.AssignAsync(new StreetAssignRequest(await PublicOfAsync(f, free), trip.PublicId), default);

        Assert.Equal(("ASSIGNED", OrderStatuses.Assigned, true), (r.Action, r.OrderStatusCode, r.DriverNotified));
        Assert.Equal(2, await f.Db.TripOrders.CountAsync(x => x.IsCurrent));
        Assert.Equal(2, await f.Db.RouteStops.CountAsync());
        var notice = await f.Db.DriverNotices.SingleAsync();
        Assert.Equal((DriverNoticeCodes.StopAdded, 7), (notice.Kind, notice.DriverId));
        Assert.Contains(Fixture.OrderNumber(free), notice.Body);
        Assert.Contains("Asignada desde la calle a la ruta", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, free)));
    }

    [Fact]
    public async Task Street_assign_to_a_route_in_progress_puts_the_order_in_transit()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, trip, _, free) = await LiveRouteAsync(f, TripStatuses.InProgress, OrderStatuses.InTransit);

        var r = await street.AssignAsync(new StreetAssignRequest(await PublicOfAsync(f, free), trip.PublicId), default);

        Assert.Equal(OrderStatuses.InTransit, r.OrderStatusCode);
        Assert.Equal(OrderStatuses.InTransit, await StatusCodeOfOrderAsync(f, free));
    }

    [Fact]
    public async Task Street_assign_refuses_a_route_of_the_other_kind_an_order_already_in_that_route_and_an_order_that_is_already_closed()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, trip, inRoute, free) = await LiveRouteAsync(f, TripStatuses.Dispatched, OrderStatuses.Assigned);
        var freePub = await PublicOfAsync(f, free);

        trip.Kind = TripKinds.Pickup;
        await f.Db.SaveChangesAsync();
        Assert.Equal(StreetAssignRules.WrongKindMessage(Fixture.OrderNumber(free), TripKinds.Delivery), (await Assert.ThrowsAsync<StatusRuleException>(() => street.AssignAsync(new StreetAssignRequest(freePub, trip.PublicId), default))).Message);

        // el rechazo limpió el rastreo: se vuelve a leer la ruta para devolverla a entrega
        (await f.Db.Trips.AsTracking().SingleAsync()).Kind = TripKinds.Delivery;
        await f.Db.SaveChangesAsync();
        var inRoutePub = await PublicOfAsync(f, inRoute);
        Assert.Equal(TripRules.AlreadyInTripMessage, (await Assert.ThrowsAsync<ConflictException>(() => street.AssignAsync(new StreetAssignRequest(inRoutePub, trip.PublicId), default))).Message);

        await f.SetOrderStatusAsync(free, OrderStatuses.Delivered);
        Assert.Contains("ya no se puede mover de ruta", (await Assert.ThrowsAsync<StatusRuleException>(() => street.AssignAsync(new StreetAssignRequest(freePub, trip.PublicId), default))).Message);
        Assert.Equal(0, await f.Db.DriverNotices.CountAsync());
        await Assert.ThrowsAsync<ValidationException>(() => street.AssignAsync(null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => street.AssignAsync(new StreetAssignRequest(Guid.NewGuid(), trip.PublicId), default));
    }

    [Fact]
    public async Task Street_unassign_takes_the_order_out_of_the_live_route_back_to_unassigned_and_tells_the_driver()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, _, inRoute, _) = await LiveRouteAsync(f, TripStatuses.InProgress, OrderStatuses.InTransit);

        var r = await street.UnassignAsync(new StreetUnassignRequest(await PublicOfAsync(f, inRoute)), default);

        Assert.Equal(("UNASSIGNED", OrderStatuses.Unassigned, true), (r.Action, r.OrderStatusCode, r.DriverNotified));
        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent));
        Assert.Equal(0, await f.Db.RouteStops.CountAsync());
        Assert.Equal(DriverNoticeCodes.StopRemoved, (await f.Db.DriverNotices.SingleAsync()).Kind);

        var pub = await PublicOfAsync(f, inRoute);
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => street.UnassignAsync(new StreetUnassignRequest(pub), default));
        Assert.Equal(StreetAssignRules.NotInAnyTripMessage(Fixture.OrderNumber(inRoute)), again.Message);
    }

    [Fact]
    public async Task Street_assign_moves_an_order_from_one_route_to_another_and_notifies_both_drivers()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, from, inRoute, _) = await LiveRouteAsync(f, TripStatuses.Dispatched, OrderStatuses.Assigned);
        f.Db.Drivers.Add(new Driver { DriverId = 8, TenantId = TenantId, EmployeeCode = "D8", FullName = "Chofer Ocho", UserId = 98, StatusCodeId = 1, IsActive = true });
        var to = new Teikem.Domain.Trips.Trip
        {
            TripId = 2, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "T-2", PlanDate = TenantClock.Default.Today, DriverId = 8, VehicleId = 9, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            StatusCodeId = f.Id(StatusDomains.TripStatus, TripStatuses.InProgress), PlannedStartUtc = from.PlannedStartUtc,
        };
        f.Db.Trips.Add(to);
        f.Db.Routes.Add(new Teikem.Domain.Trips.Route { RouteId = 2, TripId = 2, Version = 1, IsActive = true, StatusCodeId = f.Id(StatusDomains.RouteStatus, RouteStatuses.Active), StopCount = 0, CreatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();

        var r = await street.AssignAsync(new StreetAssignRequest(await PublicOfAsync(f, inRoute), to.PublicId), default);

        Assert.Equal(("MOVED", OrderStatuses.InTransit), (r.Action, r.OrderStatusCode));
        Assert.Equal(2, (await f.Db.TripOrders.SingleAsync(x => x.IsCurrent && x.TransportOrderId == inRoute)).TripId);
        var kinds = (await f.Db.DriverNotices.OrderBy(n => n.DriverNoticeId).ToListAsync()).Select(n => (n.Kind, n.DriverId)).ToList();
        Assert.Equal(new[] { (DriverNoticeCodes.StopRemoved, 7), (DriverNoticeCodes.StopAdded, 8) }, kinds);
    }

    [Fact]
    public async Task Street_targets_lists_the_open_routes_of_the_same_kind_and_says_when_the_order_cannot_move()
    {
        await using var f = await Fixture.CreateAsync();
        var (street, trip, inRoute, free) = await LiveRouteAsync(f, TripStatuses.Dispatched, OrderStatuses.Assigned);

        var t = await street.TargetsAsync(await PublicOfAsync(f, free), default);
        Assert.Equal((TripKinds.Delivery, true, null), (t.Kind, t.Movable, t.CurrentTripPublicId));
        var one = Assert.Single(t.Trips);
        Assert.Equal((trip.PublicId, "Chofer Siete", false, 1), (one.PublicId, one.DriverName, one.IsCurrent, one.StopCount));
        Assert.Contains(t.Drivers, d => d.Name == "Chofer Siete" && d.HasOpenTrip);

        var mine = await street.TargetsAsync(await PublicOfAsync(f, inRoute), default);
        Assert.Equal(trip.PublicId, mine.CurrentTripPublicId);
        Assert.True(Assert.Single(mine.Trips).IsCurrent);

        await f.SetOrderStatusAsync(free, OrderStatuses.Delivered);
        var closed = await street.TargetsAsync(await PublicOfAsync(f, free), default);
        Assert.False(closed.Movable);
        Assert.Contains("ya no se puede mover", closed.Reason);
    }
}
