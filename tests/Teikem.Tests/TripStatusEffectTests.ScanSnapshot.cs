using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Trips;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 216 (2026-10-24): la descarga del escaneo sin señal y el Enrutar a la ruta escogida.</summary>
public partial class TripStatusEffectTests
{
    private static ScanSnapshotService SnapshotOf(Fixture f, int? limit = null)
        => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), f.Statuses, null, limit);

    private static async Task SeedPiecesAsync(Fixture f, int orderId, params string[] numbers)
    {
        var seq = 1;
        foreach (var n in numbers)
            f.Db.OrderPieces.Add(new OrderPiece { OrderPieceId = orderId * 100 + seq, TenantId = TenantId, TransportOrderId = orderId, CargoLineId = 1, Sequence = seq++, PackageNumber = n, IsActive = true });
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Scan_snapshot_downloads_only_orders_that_are_not_completed_newest_first_with_their_pieces_and_scans()
    {
        await using var f = await Fixture.CreateAsync();
        var oldest = await f.SeedOrderAsync(OrderStatuses.Unassigned);
        var done = await f.SeedOrderAsync(OrderStatuses.Delivered);
        var transit = await f.SeedOrderAsync(OrderStatuses.InTransit);
        var newest = await f.SeedOrderAsync(OrderStatuses.Assigned);
        foreach (var (id, days) in new[] { (oldest, 5), (done, 4), (transit, 3), (newest, 1) })
            (await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.TransportOrderId == id)).CreatedAtUtc = DateTime.UtcNow.AddDays(-days);
        await f.Db.SaveChangesAsync();
        await SeedPiecesAsync(f, newest, "P-1", "P-2");
        f.Db.OrderPieceScans.Add(new OrderPieceScan { TenantId = TenantId, OrderPieceId = newest * 100 + 1, Action = ScanActions.Route, ScannedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();

        var page = await SnapshotOf(f).PageAsync(0, 100, default);

        Assert.Equal((3, false), (page.Total, page.Capped));
        Assert.Equal(new[] { newest, transit, oldest }, page.Orders.Select(o => int.Parse(o.OrderNumber.Split('-')[1])).ToArray());   // el envío entregado no baja
        var first = page.Orders[0];
        Assert.Equal(OrderStatuses.Assigned, first.StatusCode);
        Assert.Equal(new[] { "P-1", "P-2" }, first.Pieces.Select(p => p.Number).ToArray());
        Assert.Equal(new[] { ScanActions.Route }, first.Pieces[0].Actions);
        Assert.Empty(first.Pieces[1].Actions);
        Assert.Contains(page.Statuses, s => s.Code == OrderStatuses.InTransit && s.StageKind == StageKinds.Pipeline);
    }

    [Fact]
    public async Task Scan_snapshot_caps_at_the_limit_keeping_the_newest_pages_through_and_sends_statuses_and_routes_only_on_the_first_page()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Assigned, 1);
        var extra = new List<int>();
        for (var i = 0; i < 4; i++) extra.Add(await f.SeedOrderAsync(OrderStatuses.Unassigned));
        var all = ids.Concat(extra).ToList();
        for (var i = 0; i < all.Count; i++)
            (await f.Db.TransportOrders.AsTracking().SingleAsync(o => o.TransportOrderId == all[i])).CreatedAtUtc = DateTime.UtcNow.AddHours(-i);   // all[0] es el más nuevo
        await f.Db.SaveChangesAsync();

        var svc = SnapshotOf(f, limit: 3);
        var p0 = await svc.PageAsync(0, 2, default);
        var p1 = await svc.PageAsync(2, 2, default);
        var p2 = await svc.PageAsync(4, 2, default);

        Assert.Equal((3, true, 2, 1, 0), (p0.Total, p0.Capped, p0.Orders.Count, p1.Orders.Count, p2.Orders.Count));
        Assert.Equal(all.Take(3).Select(Fixture.OrderNumber).ToArray(), p0.Orders.Concat(p1.Orders).Select(o => o.OrderNumber).ToArray());   // se bajan los 3 más nuevos; los viejos quedan fuera
        Assert.NotEmpty(p0.Statuses);
        Assert.Empty(p1.Statuses);
        var route = Assert.Single(p0.Routes);
        Assert.Equal((trip.PublicId, "Chofer Siete", 1), (route.PublicId, route.DriverName, route.StopCount));
        Assert.Empty(p1.Routes);
        Assert.Equal(trip.Code, p0.Orders.First(o => o.OrderNumber == Fixture.OrderNumber(ids[0])).TripCode);
    }

    private static OutboundScanService ScannerOf(Fixture f)
    {
        var tenant = f.Services.GetRequiredService<ITenantContext>();
        return new OutboundScanService(f.Db, tenant, f.Lookups, f.Statuses, new OrderReadService(f.Db, tenant, f.Lookups, f.Statuses), f.Services.GetRequiredService<RouteWriter>());
    }

    [Fact]
    public async Task Scanning_with_a_chosen_route_puts_the_order_in_that_route_even_if_already_dispatched_and_a_closed_route_leaves_it_unassigned()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, _) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Assigned, 1);
        var free = await f.SeedOrderAsync(OrderStatuses.Unassigned);
        var scanner = ScannerOf(f);

        var r = await scanner.ScanAsync(new OutboundScanRequest(Fixture.OrderNumber(free), null, trip.PublicId), default);

        Assert.Equal((ScanOutcomes.FoundAssigned, trip.Code), (r.Outcome, r.TripCode));
        Assert.True(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == free && x.TripId == trip.TripId));

        // una ruta cerrada no recibe: el envío queda sin asignar con el mensaje de la ruta
        var other = await f.SeedOrderAsync(OrderStatuses.Unassigned);
        (await f.Db.Trips.AsTracking().SingleAsync()).StatusCodeId = f.Id(StatusDomains.TripStatus, TripStatuses.Completed);
        await f.Db.SaveChangesAsync();
        var closed = await scanner.ScanAsync(new OutboundScanRequest(Fixture.OrderNumber(other), null, trip.PublicId), default);
        Assert.Equal((ScanOutcomes.FoundUnassigned, ScanReasonCodes.NoOpenRoute), (closed.Outcome, closed.ReasonCode));
        Assert.Equal(OutboundScanRules.ChosenRouteClosedMessage(trip.Code), closed.Message);
        Assert.False(await f.Db.TripOrders.AnyAsync(x => x.IsCurrent && x.TransportOrderId == other));

        var missing = await scanner.ScanAsync(new OutboundScanRequest(Fixture.OrderNumber(other), null, Guid.NewGuid()), default);
        Assert.Equal(OutboundScanRules.ChosenRouteMissingMessage, missing.Message);
    }
}
