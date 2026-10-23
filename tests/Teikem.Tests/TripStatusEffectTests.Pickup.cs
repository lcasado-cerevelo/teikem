using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Rutas de RECOGIDA (2026-10-23): el chofer recoge envíos «Sometidos»; salir no los mueve, recoger los pasa a «Recogido» y la parada se cierra.</summary>
public partial class TripStatusEffectTests
{
    private static async Task<(DriverAppService App, Teikem.Domain.Trips.Trip Trip, List<int> Orders)> PickupDispatchedAsync(Fixture f, int orders)
    {
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 1, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Submitted, orders);
        trip.Kind = TripKinds.Pickup;
        var pickupType = await f.Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup);
        foreach (var stop in await f.Db.OrderStops.ToListAsync()) stop.StopTypeLookupId = pickupType;   // el único tramo de estos envíos es la recogida
        await f.Db.SaveChangesAsync();
        return (AppOf(f), trip, ids);
    }

    [Fact]
    public async Task Accepting_a_pickup_route_starts_it_without_moving_the_shipments_and_the_manifest_says_it_is_a_pickup()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await PickupDispatchedAsync(f, 2);

        Assert.Equal("PICKUP", Assert.Single(await app.ListManifestsAsync(default)).Kind);
        var r = await app.AcceptAsync(trip.PublicId, null, default);

        Assert.Equal((2, 0, true), (r.Accepted, r.Declined, r.Started));
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.InProgress), (await f.Db.Trips.AsNoTracking().SingleAsync()).StatusCodeId);
        foreach (var id in orders)
            Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Submitted), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == id)).StatusCodeId);
    }

    [Fact]
    public async Task Picking_up_moves_the_shipment_to_picked_up_closes_its_stop_and_a_retry_changes_nothing()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await PickupDispatchedAsync(f, 2);
        await app.AcceptAsync(trip.PublicId, null, default);
        var pub = await PublicOfAsync(f, orders[0]);

        var r = await app.PickupAsync(pub, new DriverPickupRequest(" Don Luis ", "dos cajas", null, 18.4, -66.1, null), default);

        Assert.False(r.AlreadyDone);
        Assert.Equal(OrderStatuses.Pickup, r.StatusCode);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Submitted), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[1])).StatusCodeId);   // el otro sigue pendiente
        var stop = await f.Db.RouteStops.AsNoTracking().SingleAsync(s => s.OrderStopId == (f.Db.OrderStops.Where(o => o.TransportOrderId == orders[0]).Select(o => o.OrderStopId).First()));
        Assert.Equal(f.Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Completed), stop.StatusCodeId);
        Assert.Contains("Recogido desde la app por Chofer Siete (lo entregó Don Luis)", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));

        var again = await app.PickupAsync(pub, new DriverPickupRequest("Don Luis", null, null, null, null, null), default);
        Assert.True(again.AlreadyDone);
    }

    [Fact]
    public async Task A_pickup_route_refuses_to_deliver_and_a_delivery_route_refuses_to_pick_up_and_both_need_the_manifest_accepted()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await PickupDispatchedAsync(f, 1);
        var pub = await PublicOfAsync(f, orders[0]);

        Assert.Equal(DriverAppRules.NotAcceptedMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => app.PickupAsync(pub, new DriverPickupRequest(null, null, null, null, null, null), default))).Message);
        await app.AcceptAsync(trip.PublicId, null, default);
        Assert.Equal(DriverAppRules.WrongKindForDeliverMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => app.DeliverAsync(pub, new DriverDeliverRequest("Ana", null, null, null, null, null, null), default))).Message);

        await using var g = await Fixture.CreateAsync();
        var (app2, trip2, orders2) = await DispatchedAsync(g, 1);
        await app2.AcceptAsync(trip2.PublicId, null, default);
        var pub2 = await PublicOfAsync(g, orders2[0]);
        Assert.Equal(DriverAppRules.WrongKindForPickupMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => app2.PickupAsync(pub2, new DriverPickupRequest(null, null, null, null, null, null), default))).Message);
    }

    [Fact]
    public async Task A_failed_pickup_keeps_the_shipment_submitted_marks_the_stop_failed_and_lets_the_route_close_once_everything_is_attended()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await PickupDispatchedAsync(f, 2);
        await app.AcceptAsync(trip.PublicId, null, default);
        var a = await PublicOfAsync(f, orders[0]);
        var b = await PublicOfAsync(f, orders[1]);

        var failed = await app.FailAsync(a, new DriverFailRequest("ABSENT", "no había nadie", null, null), default);
        Assert.Equal(OrderStatuses.Submitted, failed.StatusCode);
        var stopA = await f.Db.RouteStops.AsNoTracking().SingleAsync(s => s.OrderStopId == (f.Db.OrderStops.Where(o => o.TransportOrderId == orders[0]).Select(o => o.OrderStopId).First()));
        Assert.Equal(f.Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Failed), stopA.StatusCodeId);
        Assert.Empty(await f.Db.Set<DeliveryAttempt>().ToListAsync());     // no es un intento de entrega

        var tracked = await f.Db.Trips.SingleAsync();
        var blocked = await Assert.ThrowsAsync<StatusRuleException>(() => f.Lifecycle.CompleteTrackedAsync(tracked, null, default));
        Assert.Contains(Fixture.OrderNumber(orders[1]), blocked.Message);   // el segundo todavía no se atiende

        await app.PickupAsync(b, new DriverPickupRequest(null, null, null, null, null, null), default);
        await f.Lifecycle.CompleteTrackedAsync(tracked, null, default);
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Completed), tracked.StatusCodeId);
    }

    [Theory]
    [InlineData(true, false, false, "SUBMITTED", "PIPELINE", true, true, null)]                       // pendiente de recoger, con su parada
    [InlineData(true, false, false, "PICKUP", "PIPELINE", false, true, "El envío ya fue recogido (o salió del flujo de recogida); no se puede asignar a una ruta de recogida.")]
    [InlineData(true, false, false, "SUBMITTED", "PIPELINE", true, false, "El envío no tiene una parada de recogida pendiente.")]
    [InlineData(true, true, false, "SUBMITTED", "PIPELINE", true, true, "El envío es una entrega especial; se paga por viaje y no va en una ruta.")]
    public void Pickup_eligibility_needs_a_submitted_order_with_a_pending_pickup_stop(bool active, bool special, bool _, string code, string stage, bool initial, bool pendingStop, string? expected)
    {
        var reason = TripRules.CheckPickupEligibility(new OrderEligibilityInput("A-1", active, special, code, code, stage, initial, 1, 5, false, pendingStop));
        if (special) Assert.Equal(TripRules.SpecialDeliveryMessage, reason);
        else Assert.Equal(expected, reason);
    }

    [Theory]
    [InlineData(null, "DELIVERY")]
    [InlineData("", "DELIVERY")]
    [InlineData(" pickup ", "PICKUP")]
    [InlineData("DELIVERY", "DELIVERY")]
    [InlineData("OTRO", null)]
    public void Trip_kinds_are_normalized(string? input, string? expected) => Assert.Equal(expected, TripKinds.Normalize(input));
}
