using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Identity;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 214 (2026-10-24): el personal completa cualquier orden desde la app móvil con la misma evidencia del chofer. El usuario de la prueba (1) NO está vinculado a un chofer: el viaje es del chofer 7 (usuario 99).
/// </summary>
public partial class TripStatusEffectTests
{
    private static async Task<(DriverAppService App, MobileOrderService Orders, Teikem.Domain.Trips.Trip Trip, List<int> Ids)> StaffAndRouteAsync(Fixture f, int orders, string orderStatus = OrderStatuses.InTransit)
    {
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "ana", Email = "ana@teikem.test", FullName = "Ana Oficina", IsActive = true });
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(TripStatuses.InProgress, RouteStatuses.Active, orderStatus, orders);
        var app = AppOf(f);
        var tenant = f.Services.GetRequiredService<ITenantContext>();
        var reader = new OrderReadService(f.Db, tenant, f.Lookups, f.Statuses);
        return (app, new MobileOrderService(f.Db, tenant, reader, app, f.Statuses, f.Lookups), trip, ids);
    }

    /// <summary>El fixture no registra el efecto que anota el intento al cambiar de estatus; se siembra como lo haría (origen STATUS) para que la app lo marque.</summary>
    private static async Task SeedStatusAttemptAsync(Fixture f, int orderId, bool delivered)
    {
        f.Db.Set<DeliveryAttempt>().Add(new DeliveryAttempt { TenantId = TenantId, TransportOrderId = orderId, AttemptNumber = 1, Delivered = delivered, Source = DeliveryAttemptSources.Status, AttemptedAtUtc = DateTime.UtcNow, IsActive = true });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Staff_delivers_an_order_of_another_drivers_route_credited_to_that_driver_by_default_and_the_history_names_the_person()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, _, _, orders) = await StaffAndRouteAsync(f, 2);
        var pub = await PublicOfAsync(f, orders[0]);
        await SeedStatusAttemptAsync(f, orders[0], delivered: true);

        // Como chofer no puede (no lo es); como personal sí.
        await Assert.ThrowsAsync<ForbiddenException>(() => app.DeliverAsync(pub, new DriverDeliverRequest("Ana", null, null, null, null, null, null), default));
        var r = await app.DeliverAnyAsync(pub, new DriverDeliverRequest(" Luis Cliente ", "en recepción", null, 18.4, -66.1, null, null), default);

        Assert.False(r.AlreadyDone);
        Assert.Equal(OrderStatuses.Delivered, r.StatusCode);
        var proof = await f.Db.Set<DeliveryProof>().SingleAsync();
        Assert.Equal(("Luis Cliente", DeliveryProofCodes.SourceDriverApp, 1), (proof.RecipientName, proof.Source, proof.RecordedBy));
        Assert.Contains("Entregado desde la app por Ana Oficina.", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));
        var stopId = await f.Db.OrderStops.Where(o => o.TransportOrderId == orders[0]).Select(o => o.OrderStopId).FirstAsync();
        Assert.Equal(f.Id(StatusDomains.RouteStopStatus, RouteStopStatuses.Completed), (await f.Db.RouteStops.AsNoTracking().SingleAsync(s => s.OrderStopId == stopId)).StatusCodeId);
        // Por omisión el intento queda a cuenta del chofer de la ruta (liquidación); el historial y el comprobante dicen quién lo cerró.
        Assert.Equal(7, (await f.Db.Set<DeliveryAttempt>().SingleAsync(a => a.TransportOrderId == orders[0] && a.Delivered)).DriverId);
        // El otro envío de la ruta sigue igual.
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[1])).StatusCodeId);

        Assert.True((await app.DeliverAnyAsync(pub, new DriverDeliverRequest("Luis Cliente", null, null, null, null, null, null), default)).AlreadyDone);
    }

    [Fact]
    public async Task Staff_can_close_an_order_without_crediting_the_route_driver()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, _, _, orders) = await StaffAndRouteAsync(f, 2);
        await SeedStatusAttemptAsync(f, orders[0], delivered: true);

        await app.DeliverAnyAsync(await PublicOfAsync(f, orders[0]), new DriverDeliverRequest("Luis", null, null, null, null, null, null, null, CreditToDriver: false), default);

        Assert.Null((await f.Db.Set<DeliveryAttempt>().SingleAsync(a => a.TransportOrderId == orders[0] && a.Delivered)).DriverId);
        Assert.Contains("Entregado desde la app por Ana Oficina.", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));
    }

    [Fact]
    public async Task Staff_fails_an_order_with_a_reason_without_being_a_driver()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, _, _, orders) = await StaffAndRouteAsync(f, 1);
        var pub = await PublicOfAsync(f, orders[0]);

        var r = await app.FailAnyAsync(pub, new DriverFailRequest("absent", "nadie abrió", null, null), default);

        Assert.Equal(OrderStatuses.Failed, r.StatusCode);
        Assert.Contains("No entregado desde la app", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));
        Assert.Empty(await f.Db.Set<DeliveryProof>().ToListAsync());
    }

    [Fact]
    public async Task Staff_picks_up_a_submitted_order_that_is_not_in_any_route_and_an_unknown_order_is_not_found()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, _, trip, orders) = await StaffAndRouteAsync(f, 1, OrderStatuses.Submitted);
        foreach (var l in await f.Db.TripOrders.AsTracking().ToListAsync()) l.IsCurrent = false;   // sin ruta vigente: una orden en su etapa inicial se recoge
        await f.Db.SaveChangesAsync();
        var pub = await PublicOfAsync(f, orders[0]);

        var r = await app.PickupAnyAsync(pub, new DriverPickupRequest("Don Luis", null, null, null, null, null), default);

        Assert.Equal(OrderStatuses.Pickup, r.StatusCode);
        Assert.Contains("Recogido desde la app por Ana Oficina (lo entregó Don Luis)", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));
        Assert.True((await app.PickupAnyAsync(pub, new DriverPickupRequest("Don Luis", null, null, null, null, null), default)).AlreadyDone);
        await Assert.ThrowsAsync<NotFoundException>(() => app.PickupAnyAsync(Guid.NewGuid(), null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => app.DeliverAnyAsync(Guid.NewGuid(), null, default));
    }

    [Fact]
    public async Task The_mobile_order_card_tells_the_route_the_driver_and_which_action_fits_and_the_search_finds_the_exact_number_first()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, orders, trip, ids) = await StaffAndRouteAsync(f, 1);
        var pub = await PublicOfAsync(f, ids[0]);

        var card = await orders.GetAsync(pub, default);
        Assert.Equal((trip.Code, "Chofer Siete", true, true, false), (card.TripCode, card.DriverName, card.CanDeliver, card.CanFail, card.CanPickup));
        Assert.Equal(OrderStatuses.InTransit, card.Shipment.StatusCode);
        Assert.Equal("DELIVERY", card.Shipment.Kind);
        Assert.Null(card.Proof);
        Assert.Empty(card.Attempts);

        await app.FailAnyAsync(pub, new DriverFailRequest("absent", null, null, null), default);
        card = await orders.GetAsync(pub, default);
        Assert.Equal((true, false), (card.CanDeliver, card.CanFail));   // fallida: se puede volver a intentar la entrega

        var exact = await orders.SearchAsync(Fixture.OrderNumber(ids[0]), default);
        Assert.Equal(OrderReadService.MatchedByOrderNumber, exact.MatchedBy);
        Assert.Equal(pub, Assert.Single(exact.Items).PublicId);
        Assert.Equal(MobileOrderRules.QueryRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => orders.SearchAsync("  ", default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => orders.GetAsync(Guid.NewGuid(), default));
    }
}
