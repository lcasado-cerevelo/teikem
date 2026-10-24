using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// El escaneo es a la PIEZA (Luis, 2026-10-24): es ella la que dice Sí, Ok o No — la web y la app móvil usan este servicio. El número del envío no es una pieza («No»); una pieza nueva dice «Sí» aunque el envío ya estuviera
/// en ese estatus (otra pieza lo movió); solo la MISMA pieza otra vez con la misma acción dice «Ok»; un estatus final dice «No».
/// </summary>
public partial class TripStatusEffectTests
{
    private static ScanActionService ScanActionOf(Fixture f)
        => new(f.Db, f.Services.GetRequiredService<ITenantContext>(), f.Statuses, null!, null!, null!, null);

    [Fact]
    public async Task The_piece_says_yes_or_ok_or_no_not_the_order_number_and_not_the_status_the_order_already_had()
    {
        await using var f = await Fixture.CreateAsync();
        var inTransit = await f.SeedOrderAsync(OrderStatuses.InTransit);
        var delivered = await f.SeedOrderAsync(OrderStatuses.Delivered);
        await SeedPiecesAsync(f, inTransit, "A-1", "A-2");
        await SeedPiecesAsync(f, delivered, "Z-1");
        var scan = ScanActionOf(f);

        // el número del envío (o del empaque) no es una pieza: No
        var byOrder = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, Fixture.OrderNumber(inTransit)), default);
        Assert.Equal((ScanActionOutcomes.NotFound, ScanVoices.NotFound, ScanActions.PieceNotFoundMessage), (byOrder.Outcome, byOrder.Voice, byOrder.Message));
        Assert.Equal((ScanActionOutcomes.NotFound, ScanVoices.NotFound), ((await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, "NO-EXISTE"), default)) is var nf ? (nf.Outcome, nf.Voice) : default));

        // la pieza A-1 (el envío ya estaba En tránsito, pero esta pieza es nueva para «Salir»): Sí
        var first = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, "A-1"), default);
        Assert.Equal((ScanActionOutcomes.Counted, ScanVoices.Found, 1, 2), (first.Outcome, first.Voice, first.Scanned, first.Total));
        // la misma pieza otra vez: Ok, sin contarla dos veces
        var again = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, "A-1"), default);
        Assert.Equal((ScanActionOutcomes.Duplicate, ScanVoices.Duplicate, 1), (again.Outcome, again.Voice, again.Scanned));
        // otra pieza del mismo envío: Sí y cuenta «2 de 2»
        var second = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, "A-2"), default);
        Assert.Equal((ScanActionOutcomes.Counted, ScanVoices.Found, 2, 2), (second.Outcome, second.Voice, second.Scanned, second.Total));

        // una pieza de un envío entregado (estatus final): No
        var final = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Depart, "Z-1"), default);
        Assert.Equal((ScanActionOutcomes.Final, ScanVoices.NotFound), (final.Outcome, final.Voice));
    }

    [Fact]
    public async Task A_cancelled_order_piece_says_no()
    {
        await using var f = await Fixture.CreateAsync();
        var cancelled = await f.SeedOrderAsync(OrderStatuses.Cancelled);
        await SeedPiecesAsync(f, cancelled, "C-1");

        var r = await ScanActionOf(f).ApplyAsync(new ScanActionRequest(ScanActions.Depart, "C-1"), default);

        Assert.Equal((ScanActionOutcomes.Final, ScanVoices.NotFound), (r.Outcome, r.Voice));
    }

    [Fact]
    public async Task The_snapshot_driver_of_a_package_is_the_driver_of_its_current_route_of_any_kind_and_status()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        f.Db.Drivers.Add(new Driver { DriverId = 8, TenantId = TenantId, EmployeeCode = "D8", FullName = "Chofer Ocho", UserId = 98, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (dispatched, ids) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Assigned, 1);   // chofer 7
        var pickupOrder = await f.SeedOrderAsync(OrderStatuses.Submitted);
        var free = await f.SeedOrderAsync(OrderStatuses.Unassigned);
        // una ruta de RECOGIDA aún PLANEADA del chofer 8 con un envío: sigue siendo «del chofer 8»
        f.Db.Trips.Add(new Teikem.Domain.Trips.Trip { TripId = 2, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "T-2", PlanDate = TenantClock.Default.Today, DriverId = 8, IsActive = true, Kind = TripKinds.Pickup,
            StatusCodeId = f.Id(StatusDomains.TripStatus, TripStatuses.Planned), CreatedAtUtc = DateTime.UtcNow });
        f.Db.TripOrders.Add(new TripOrder { TripOrderId = 900, TenantId = TenantId, TripId = 2, TransportOrderId = pickupOrder, SortHint = 1, IsCurrent = true, AssignedAtUtc = DateTime.UtcNow });
        // un envío que SALIÓ de una ruta (ya no vigente) no es de ese chofer
        f.Db.TripOrders.Add(new TripOrder { TripOrderId = 901, TenantId = TenantId, TripId = 2, TransportOrderId = free, SortHint = 2, IsCurrent = false, AssignedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();

        var page = await SnapshotOf(f).PageAsync(0, 100, default);
        var drivers = page.Drivers!.ToDictionary(d => d.Name, d => d.PublicId);
        Guid? DriverOf(int orderId) => page.Orders.Single(o => o.OrderNumber == Fixture.OrderNumber(orderId)).DriverPublicId;

        Assert.Equal(drivers["Chofer Siete"], DriverOf(ids[0]));
        Assert.Equal(drivers["Chofer Ocho"], DriverOf(pickupOrder));
        Assert.Null(DriverOf(free));
    }

    [Fact]
    public async Task A_shipment_further_along_than_the_action_stops_and_warns_and_only_counts_the_piece_when_confirmed_without_moving_the_status_back()
    {
        await using var f = await Fixture.CreateAsync();
        var inTransit = await f.SeedOrderAsync(OrderStatuses.InTransit);
        await SeedPiecesAsync(f, inTransit, "B-1", "B-2");
        var scan = ScanActionOf(f);

        var warn = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Arrive, "B-1"), default);   // «Llegó» sobre uno que ya va En tránsito

        Assert.Equal((ScanActionOutcomes.Beyond, ScanVoices.Warn, 0), (warn.Outcome, warn.Voice, warn.Scanned));
        Assert.Contains("B-1", warn.Message);
        Assert.Contains(Fixture.OrderNumber(inTransit), warn.Message);
        Assert.Contains("va después de", warn.Message);
        Assert.False(await f.Db.OrderPieceScans.AnyAsync());   // no se contó nada

        var go = await scan.ApplyAsync(new ScanActionRequest(ScanActions.Arrive, "B-1", null, null, ConfirmBeyond: true), default);

        Assert.Equal((ScanActionOutcomes.Counted, ScanVoices.Found, 1), (go.Outcome, go.Voice, go.Scanned));
        Assert.Equal(OrderStatuses.InTransit, await StatusCodeOfOrderAsync(f, inTransit));   // el estatus no retrocedió
        Assert.Equal(ScanActionOutcomes.Duplicate, (await scan.ApplyAsync(new ScanActionRequest(ScanActions.Arrive, "B-1", null, null, true), default)).Outcome);
    }

    private static ScanLookupService LookupOf(Fixture f) => new(f.Db, f.Services.GetRequiredService<ITenantContext>());

    [Fact]
    public async Task The_lookup_finds_whatever_is_in_the_database_for_a_package_number_cancelled_inactive_claimed_or_in_a_route()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Siete", UserId = 99, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        var (trip, ids) = await f.SeedTripAsync(TripStatuses.Dispatched, RouteStatuses.Active, OrderStatuses.Assigned, 1);
        var cancelled = await f.SeedOrderAsync(OrderStatuses.Cancelled);
        await SeedPiecesAsync(f, ids[0], "R-1");
        await SeedPiecesAsync(f, cancelled, "X-1", "X-2");
        var x2 = await f.Db.OrderPieces.AsTracking().SingleAsync(p => p.PackageNumber == "X-2");
        x2.MissingStatus = PieceMissing.Claimed;
        x2.ClaimId = 1;
        (await f.Db.OrderPieces.AsTracking().SingleAsync(p => p.PackageNumber == "X-1")).IsActive = false;
        f.Db.Claims.Add(new Claim { ClaimId = 1, TenantId = TenantId, Number = "RC-0001", TransportOrderId = cancelled, ClientId = 1, StatusCodeId = f.Id(StatusDomains.OrderStatus, OrderStatuses.Submitted), Description = "se perdió" });
        f.Db.OrderPieceScans.Add(new OrderPieceScan { TenantId = TenantId, OrderPieceId = ids[0] * 100 + 1, Action = ScanActions.Route, ScannedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        var lookup = LookupOf(f);

        var inRoute = Assert.Single((await lookup.LookupAsync("R-1", default)).Items);
        Assert.Equal(new[] { "IN_ROUTE" }, inRoute.Flags);
        Assert.Equal((trip.Code, "Chofer Siete", ScanActions.Route), (inRoute.TripCode, inRoute.DriverName, Assert.Single(inRoute.Scans).Action));

        var claimed = Assert.Single((await lookup.LookupAsync("X-2", default)).Items);
        Assert.Equal(new[] { "ORDER_CANCELLED", "PIECE_CLAIMED" }, claimed.Flags);
        Assert.Equal(("RC-0001", PieceMissing.Claimed, 1), (claimed.ClaimNumber, claimed.Missing, claimed.Pieces));   // la pieza inactiva no cuenta en el total

        var inactive = Assert.Single((await lookup.LookupAsync("X-1", default)).Items);
        Assert.Contains("PIECE_INACTIVE", inactive.Flags);   // también las dadas de baja

        // si no es una pieza, se busca por el envío
        var byOrder = await lookup.LookupAsync(Fixture.OrderNumber(cancelled), default);
        Assert.Equal("ORDER_NUMBER", byOrder.MatchedBy);
        Assert.Null(Assert.Single(byOrder.Items).PieceNumber);

        var nothing = await lookup.LookupAsync("NO-EXISTE", default);
        Assert.Empty(nothing.Items);
        Assert.Null(nothing.MatchedBy);
        await Assert.ThrowsAsync<Teikem.Infrastructure.Exceptions.ValidationException>(() => lookup.LookupAsync("  ", default));
    }
}
