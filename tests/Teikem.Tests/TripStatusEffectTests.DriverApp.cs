using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// App del chofer (2026-10-23): aceptar el manifiesto = la salida (envíos a En tránsito, ruta En curso); los envíos que no acepta salen de la ruta, vuelven a «Sin asignar» y quedan con su motivo.
/// Mismo fixture de las pruebas de efectos de viaje (estatus, efectos y RouteWriter reales).
/// </summary>
public partial class TripStatusEffectTests
{
    private static DriverAppService AppOf(Fixture f)
        => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), f.Statuses, f.Lifecycle, f.Services.GetRequiredService<RouteWriter>(), f.Lookups,
            new DeliveryRegistrationService(f.Db, f.Services.GetRequiredService<ITenantContext>(), f.Statuses));

    private static async Task<(DriverAppService App, Teikem.Domain.Trips.Trip Trip, List<int> Orders)> DispatchedAsync(Fixture f, int orders, bool linkDriver = true)
    {
        // El usuario de la prueba es el 1 (TenantContext del fixture); el viaje sembrado es del chofer 7.
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = linkDriver ? 1 : 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Assigned, orders);
        return (AppOf(f), trip, ids);
    }

    [Fact]
    public async Task Accept_without_declines_starts_the_trip_and_moves_every_shipment_to_in_transit()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 2);

        var r = await app.AcceptAsync(trip.PublicId, null, default);

        Assert.Equal((2, 0, true), (r.Accepted, r.Declined, r.Started));
        var saved = await f.Db.Trips.AsNoTracking().SingleAsync();
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.InProgress), saved.StatusCodeId);
        Assert.NotNull(saved.AcceptedAtUtc);
        Assert.Equal(1, saved.AcceptedBy);
        foreach (var id in orders)
            Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == id)).StatusCodeId);
        Assert.Empty(await f.Db.TripOrderDeclines.ToListAsync());
    }

    [Fact]
    public async Task Declined_shipments_leave_the_route_go_back_to_unassigned_and_keep_their_reason_while_the_rest_goes_in_transit()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 3);
        var declinedPublic = (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[1])).PublicId;

        var r = await app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(declinedPublic, "NO_SPACE", " no cabe ") }, null), default);

        Assert.Equal((2, 1, true), (r.Accepted, r.Declined, r.Started));
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.Unassigned), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[1])).StatusCodeId);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[0])).StatusCodeId);
        Assert.Equal(f.Id(StatusDomains.OrderStatus, OrderStatuses.InTransit), (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[2])).StatusCodeId);
        // sale de la ruta (vínculo y parada)
        Assert.Equal(new[] { orders[0], orders[2] }, (await f.Db.TripOrders.Where(l => l.IsCurrent).Select(l => l.TransportOrderId).ToListAsync()).OrderBy(x => x));
        Assert.Equal(2, await f.Db.RouteStops.CountAsync());
        var d = await f.Db.TripOrderDeclines.SingleAsync();
        Assert.Equal((orders[1], 7, "no cabe"), (d.TransportOrderId, d.DriverId, d.Note));
        Assert.Contains("No aceptado por el chofer", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[1])));
    }

    [Fact]
    public async Task Declining_every_shipment_leaves_the_trip_dispatched_and_empty()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 1);
        var pub = (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[0])).PublicId;

        var r = await app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(pub, "OTHER", null) }, null), default);

        Assert.Equal((0, 1, false), (r.Accepted, r.Declined, r.Started));
        var saved = await f.Db.Trips.AsNoTracking().SingleAsync();
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Dispatched), saved.StatusCodeId);
        Assert.Null(saved.AcceptedAtUtc);
    }

    [Fact]
    public async Task Accepting_twice_or_a_trip_that_is_not_dispatched_is_a_422()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, _) = await DispatchedAsync(f, 1);
        await app.AcceptAsync(trip.PublicId, null, default);

        var again = await Assert.ThrowsAsync<StatusRuleException>(() => app.AcceptAsync(trip.PublicId, null, default));
        Assert.Equal(DriverAppRules.AlreadyAcceptedMessage(TripCode), again.Message);
        Assert.Equal(422, again.StatusCode);
    }

    [Fact]
    public async Task A_reason_is_required_valid_and_the_shipment_must_belong_to_the_manifest()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 2);
        var pub = (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orders[0])).PublicId;

        var none = await Assert.ThrowsAsync<ValidationException>(() => app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(pub, " ", null) }, null), default));
        Assert.Equal(DriverAppRules.ReasonRequiredMessage, none.Message);
        var bad = await Assert.ThrowsAsync<ValidationException>(() => app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(pub, "NOPE", null) }, null), default));
        Assert.Equal(DriverAppRules.ReasonInvalidMessage, bad.Message);
        var longNote = await Assert.ThrowsAsync<ValidationException>(() => app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(pub, "OTHER", new string('x', 301)) }, null), default));
        Assert.Equal(DriverAppRules.NoteTooLongMessage, longNote.Message);
        var dup = await Assert.ThrowsAsync<ValidationException>(() => app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(pub, "OTHER", null), new DriverDeclineItem(pub, "OTHER", null) }, null), default));
        Assert.Equal(DriverAppRules.DuplicateShipmentMessage, dup.Message);
        var foreign = await Assert.ThrowsAsync<ValidationException>(() => app.AcceptAsync(trip.PublicId, new DriverAcceptRequest(new[] { new DriverDeclineItem(Guid.NewGuid(), "OTHER", null) }, null), default));
        Assert.Equal(DriverAppRules.ShipmentNotInManifestMessage, foreign.Message);
        // nada cambió
        Assert.Equal(f.Id(StatusDomains.TripStatus, TripStatuses.Dispatched), (await f.Db.Trips.AsNoTracking().SingleAsync()).StatusCodeId);
    }

    [Fact]
    public async Task A_manifest_of_another_driver_is_a_404_and_a_user_without_driver_is_a_403()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, _) = await DispatchedAsync(f, 1, linkDriver: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => app.ListManifestsAsync(default));

        await using var g = await Fixture.CreateAsync();
        var (app2, trip2, _) = await DispatchedAsync(g, 1);
        g.Db.Drivers.Add(new Driver { DriverId = 8, TenantId = TenantId, EmployeeCode = "D8", FullName = "Otro", UserId = 2, StatusCodeId = 1, IsActive = true });
        var t = await g.Db.Trips.SingleAsync();
        t.DriverId = 8;
        await g.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => app2.GetManifestAsync(trip2.PublicId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => app2.AcceptAsync(trip2.PublicId, null, default));
    }

    [Fact]
    public async Task The_driver_sees_only_his_dispatched_or_in_progress_manifests_with_their_shipments_in_route_order()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 2);

        var list = await app.ListManifestsAsync(default);
        var m = Assert.Single(list);
        Assert.Equal((trip.PublicId, TripCode, "DELIVERY", false, 2), (m.PublicId, m.Code, m.Kind, m.Accepted, m.ShipmentCount));

        var detail = await app.GetManifestAsync(trip.PublicId, default);
        Assert.Equal(new[] { 1, 2 }, detail.Shipments.Select(s => s.Sequence));
        Assert.Equal("Calle 1", detail.Shipments[0].Address);
        Assert.Equal(Fixture.OrderNumber(orders[0]), detail.Shipments[0].OrderNumber);
    }

    // ------------------------------------------------------------ entregar / no entregar desde la app

    private static async Task<Guid> PublicOfAsync(Fixture f, int orderId) => (await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orderId)).PublicId;

    [Fact]
    public async Task Delivering_before_accepting_the_manifest_is_refused_with_the_accept_first_message()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, _, orders) = await DispatchedAsync(f, 1);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => app.DeliverAsync(await PublicOfAsync(f, orders[0]), new DriverDeliverRequest("Ana", null, null, null, null, null, null), default));

        Assert.Equal(DriverAppRules.NotAcceptedMessage, ex.Message);
    }

    [Fact]
    public async Task Delivering_after_accepting_registers_the_proof_as_driver_app_and_moves_the_order_to_delivered_and_a_retry_changes_nothing()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 2);
        await app.AcceptAsync(trip.PublicId, null, default);
        var pub = await PublicOfAsync(f, orders[0]);

        var r = await app.DeliverAsync(pub, new DriverDeliverRequest(" Ana Pérez ", "en la puerta", null, 18.4, -66.1, null, null), default);

        Assert.False(r.AlreadyDone);
        Assert.Equal(OrderStatuses.Delivered, r.StatusCode);
        var proof = await f.Db.Set<DeliveryProof>().SingleAsync();
        Assert.Equal(("Ana Pérez", DeliveryProofCodes.SourceDriverApp, DeliveryProofCodes.SignatureRecipient, DeliveryProofCodes.GeoOk), (proof.RecipientName, proof.Source, proof.SignatureKind, proof.GeoStatus));
        Assert.Equal(18.4m, proof.Latitude);

        var again = await app.DeliverAsync(pub, new DriverDeliverRequest("Ana Pérez", null, null, null, null, null, null), default);
        Assert.True(again.AlreadyDone);
        Assert.Equal(1, await f.Db.Set<DeliveryProof>().CountAsync());
    }

    [Fact]
    public async Task Delivering_needs_the_recipient_and_a_valid_signature_and_a_shipment_of_another_driver_is_not_found()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 1);
        await app.AcceptAsync(trip.PublicId, null, default);
        var pub = await PublicOfAsync(f, orders[0]);

        Assert.Equal(DeliveryProofRules.RecipientRequired, (await Assert.ThrowsAsync<ValidationException>(() => app.DeliverAsync(pub, new DriverDeliverRequest(" ", null, null, null, null, null, null), default))).Message);
        Assert.Equal(DriverAppRules.SignatureInvalidMessage, (await Assert.ThrowsAsync<ValidationException>(() => app.DeliverAsync(pub, new DriverDeliverRequest("Ana", null, null, null, null, "no-es-base64!!", null), default))).Message);
        Assert.Equal(DriverAppRules.TooManyPhotosMessage, (await Assert.ThrowsAsync<ValidationException>(() => app.DeliverAsync(pub, new DriverDeliverRequest("Ana", null, null, null, null, null, new[] { "AA==", "AA==", "AA==", "AA==" }), default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => app.DeliverAsync(Guid.NewGuid(), new DriverDeliverRequest("Ana", null, null, null, null, null, null), default));
        Assert.Empty(await f.Db.Set<DeliveryProof>().ToListAsync());
    }

    [Fact]
    public async Task Failing_a_shipment_needs_a_valid_reason_and_moves_it_to_failed_and_a_retry_changes_nothing()
    {
        await using var f = await Fixture.CreateAsync();
        var (app, trip, orders) = await DispatchedAsync(f, 1);
        await app.AcceptAsync(trip.PublicId, null, default);
        var pub = await PublicOfAsync(f, orders[0]);

        Assert.Equal(DriverAppRules.FailureReasonRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => app.FailAsync(pub, new DriverFailRequest(" ", null, null, null), default))).Message);
        Assert.Equal(DriverAppRules.FailureReasonInvalidMessage, (await Assert.ThrowsAsync<ValidationException>(() => app.FailAsync(pub, new DriverFailRequest("NOPE", null, null, null), default))).Message);

        var r = await app.FailAsync(pub, new DriverFailRequest("absent", "nadie abrió", null, null), default);

        Assert.Equal(OrderStatuses.Failed, r.StatusCode);
        Assert.Contains("No entregado desde la app", string.Join('|', await f.CommentsAsync(EntityTypes.TransportOrder, orders[0])));
        Assert.Empty(await f.Db.Set<DeliveryProof>().ToListAsync());
    }
}
