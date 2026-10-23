using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-22 (dictado de Luis) — envíos de «ida y vuelta» (marca <c>roundTrip</c> del tipo de servicio): el chofer va a la RECOGIDA, así que la zona, la parada de la ruta y el
/// mapa salen de la dirección de recogida y no de la de entrega; la lista y la ruta dicen qué se visita (recoger o entregar) y el tipo de servicio.
/// </summary>
public class RoundTripRoutingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("{}", false)]
    [InlineData("{\"roundTrip\":true}", true)]
    [InlineData("{\"ROUNDTRIP\":true,\"pickUp\":false}", true)]
    [InlineData("{\"roundTrip\":\"si\"}", false)]
    public void The_round_trip_mark_is_read_from_the_extra_json(string? json, bool roundTrip)
        => Assert.Equal(roundTrip, ServiceTypeMarks.Parse(json).RoundTrip);

    [Fact]
    public void A_company_setting_can_turn_the_round_trip_mark_on_or_off()
    {
        Assert.True(ServiceTypeMarks.Parse("{\"roundTrip\":false}").Apply("{\"roundTrip\":true}").RoundTrip);
        Assert.False(ServiceTypeMarks.Parse("{\"roundTrip\":true}").Apply("{\"roundTrip\":false}").RoundTrip);
        Assert.True(ServiceTypeMarks.Parse("{\"pickUp\":true,\"roundTrip\":true}").Apply("{\"billable\":false}").RoundTrip);
    }

    /// <summary>Orden asignable con entrega en Z1 (00901) y recogida en Z2 (00902); el tipo de servicio del fixture se marca de ida y vuelta si se pide.</summary>
    private static async Task<(SeededOrder Order, int PickupStopId)> SeedAsync(TripServiceFixture f, bool roundTrip)
    {
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, postalCode: "00901");
        var pickupStopId = 9000 + o.Id;
        f.Db.OrderStops.Add(new OrderStop
        {
            OrderStopId = pickupStopId, TransportOrderId = o.Id,
            StopTypeLookupId = await f.Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup),
            Sequence = 1, SnapName = "TIENDA", SnapLine1 = "Calle Tienda", SnapCity = "Jayuya", SnapPostalCode = "00902", ServiceMinutes = 10,
            StatusCodeId = f.Db.StatusCodes.Where(s => s.Entity == StatusDomains.StopStatus && s.InternalCode == StopStatuses.Pending).Select(s => s.StatusCodeId).First(),
        });
        // el tipo de servicio del envío: un valor de la lista ServiceType, con o sin la marca de ida y vuelta
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 900, Entity = LookupDomains.ServiceType, InternalCode = "SVC", LabelJson = "{\"es\":\"Servicio\"}", IsActive = true, ExtraJson = roundTrip ? "{\"pickUp\":false,\"billable\":true,\"roundTrip\":true}" : null });
        (await f.Db.TransportOrders.SingleAsync(x => x.TransportOrderId == o.Id)).ServiceTypeLookupId = 900;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (o, pickupStopId);
    }

    [Fact]
    public async Task The_unassigned_list_zones_a_round_trip_order_by_its_pickup_and_says_what_is_visited()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var (o, _) = await SeedAsync(f, roundTrip: true);

        var page = await f.Get<TripOrderService>().ListUnassignedAsync(new UnassignedOrdersQuery(), default);
        var item = Assert.Single(page.Items, i => i.PublicId == o.PublicId);
        Assert.Equal("Z2", item.ZoneCode);                 // la recogida (00902), no la entrega (00901)
        Assert.Equal("Jayuya", item.City);
        Assert.Equal("00902", item.PostalCode);
        Assert.Equal(StopTypes.Pickup, item.StopKind);
        Assert.True(item.IsRoundTrip);
    }

    [Fact]
    public async Task A_normal_order_keeps_its_delivery_zone_and_kind()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var (o, _) = await SeedAsync(f, roundTrip: false);

        var item = Assert.Single((await f.Get<TripOrderService>().ListUnassignedAsync(new UnassignedOrdersQuery(), default)).Items, i => i.PublicId == o.PublicId);
        Assert.Equal("Z1", item.ZoneCode);
        Assert.Equal(StopTypes.Delivery, item.StopKind);
        Assert.False(item.IsRoundTrip);
    }

    [Fact]
    public async Task Adding_a_round_trip_order_to_a_route_creates_the_stop_on_its_pickup()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var (o, pickupStopId) = await SeedAsync(f, roundTrip: true);
        var (tripId, tripPublicId) = await f.SeedTripAsync("R-RT", TripStatuses.Draft);
        await f.AddOrdersAsync(tripId, o.Id);

        var detail = await f.Get<TripReadService>().GetAsync(tripPublicId, default);
        var stop = Assert.Single(detail.Stops);
        Assert.Equal(pickupStopId, stop.OrderStopId);
        Assert.Equal(StopTypes.Pickup, stop.StopKind);
        Assert.True(stop.IsRoundTrip);
        Assert.Equal("Jayuya", stop.City);
        Assert.Equal("00902", stop.PostalCode);
    }

    [Fact]
    public async Task Adding_a_normal_order_still_routes_its_delivery()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var (o, _) = await SeedAsync(f, roundTrip: false);
        var (tripId, tripPublicId) = await f.SeedTripAsync("R-ND", TripStatuses.Draft);
        await f.AddOrdersAsync(tripId, o.Id);

        var stop = Assert.Single((await f.Get<TripReadService>().GetAsync(tripPublicId, default)).Stops);
        Assert.Equal(o.StopId, stop.OrderStopId);
        Assert.Equal(StopTypes.Delivery, stop.StopKind);
        Assert.False(stop.IsRoundTrip);
    }
}
