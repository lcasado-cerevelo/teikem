using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 107 — la estación de escaneo con acción escogida (ScanActionService): cada acción lleva la orden a su estatus brincando los de en medio (registrados «brincados», misma fecha y hora), ya más adelante o final no se toca,
/// desde Intento fallido se regresa al almacén, Enrutar además la pone en una ruta abierta (y sin ruta no cambia nada). Silencioso: cada escaneo contesta con su resultado, sin excepciones.
/// </summary>
public class ScanActionServiceTests
{
    private static Task<ScanActionResultDto> ApplyAsync(TripServiceFixture f, string action, string code)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(action, code, TripServiceFixture.Today), default);

    private static async Task<List<(string To, bool Skipped, DateTime At)>> HistoryAsync(TripServiceFixture f, int orderId)
    {
        var typeId = await f.Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var rows = await f.Db.EntityStatusHistories.AsNoTracking().Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == orderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        return rows.Select(h => (h.ToStatus!.InternalCode, h.IsSkipped, h.ChangedAtUtc)).ToList();
    }

    [Fact]
    public async Task Pickup_takes_a_submitted_order_to_picked_up_and_marks_it()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);
        var r = await ApplyAsync(f, ScanActions.Pickup, order.OrderNumber);

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal("found", r.Voice);
        Assert.Equal(OrderStatuses.Submitted, r.FromStatus);
        Assert.Equal(OrderStatuses.Pickup, r.ToStatus);
        Assert.Empty(r.Skipped);
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(order.Id));
        Assert.NotNull((await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == order.Id)).ConfirmedAtUtc);
        var h = await HistoryAsync(f, order.Id);
        Assert.Equal(new[] { (OrderStatuses.Pickup, false) }, h.Select(x => (x.To, x.Skipped)).ToArray());
    }

    [Fact]
    public async Task Skipping_registers_the_in_between_statuses_as_skipped_with_the_same_date_and_time()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);
        var r = await ApplyAsync(f, ScanActions.Depart, order.PackBatchNumber);   // por el número de empaque

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(OrderStatuses.InTransit, r.ToStatus);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned }, r.Skipped.ToArray());
        var h = await HistoryAsync(f, order.Id);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit }, h.Select(x => x.To).ToArray());
        Assert.Equal(new[] { true, true, true, false }, h.Select(x => x.Skipped).ToArray());
        Assert.Single(h.Select(x => x.At).Distinct());   // la misma fecha y hora
    }

    [Fact]
    public async Task Arrive_then_arrive_again_is_a_no_op_and_depart_skips_assigned()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Pickup);
        var arrive = await ApplyAsync(f, ScanActions.Arrive, order.OrderNumber);
        Assert.Equal(OrderStatuses.Unassigned, arrive.ToStatus);
        Assert.Empty(arrive.Skipped);

        var again = await ApplyAsync(f, ScanActions.Arrive, order.OrderNumber);
        Assert.Equal(ScanActionOutcomes.AlreadyBeyond, again.Outcome);
        Assert.Equal("dup", again.Voice);
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(order.Id));

        var depart = await ApplyAsync(f, ScanActions.Depart, order.OrderNumber);
        Assert.Equal(new[] { OrderStatuses.Assigned }, depart.Skipped.ToArray());
        Assert.Equal(OrderStatuses.InTransit, await f.OrderStatusAsync(order.Id));
    }

    [Fact]
    public async Task An_order_already_beyond_the_chosen_action_is_not_touched()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.InTransit);
        var before = (await HistoryAsync(f, order.Id)).Count;
        foreach (var action in new[] { ScanActions.Pickup, ScanActions.Arrive, ScanActions.Depart })
            Assert.Equal(ScanActionOutcomes.AlreadyBeyond, (await ApplyAsync(f, action, order.OrderNumber)).Outcome);
        Assert.Equal(OrderStatuses.InTransit, await f.OrderStatusAsync(order.Id));
        Assert.Equal(before, (await HistoryAsync(f, order.Id)).Count);
    }

    [Theory]
    [InlineData(OrderStatuses.Delivered)]
    [InlineData(OrderStatuses.Cancelled)]
    public async Task A_final_order_is_ignored_without_changing_anything(string finalStatus)
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(finalStatus);
        var r = await ApplyAsync(f, ScanActions.Arrive, order.OrderNumber);
        Assert.Equal(ScanActionOutcomes.Final, r.Outcome);
        Assert.Equal("dup", r.Voice);
        Assert.Equal(finalStatus, await f.OrderStatusAsync(order.Id));
        Assert.Empty(await HistoryAsync(f, order.Id));
    }

    [Fact]
    public async Task From_a_failed_attempt_arrive_returns_the_order_to_unassigned_and_the_failure_stays_in_the_history()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Failed);
        var r = await ApplyAsync(f, ScanActions.Arrive, order.OrderNumber);
        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(OrderStatuses.Failed, r.FromStatus);
        Assert.Equal(OrderStatuses.Unassigned, r.ToStatus);
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(order.Id));
        var typeId = await f.Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var row = await f.Db.EntityStatusHistories.AsNoTracking().Include(h => h.FromStatus).Where(h => h.EntityTypeLookupId == typeId && h.EntityId == order.Id).SingleAsync();
        Assert.Equal(OrderStatuses.Failed, row.FromStatus!.InternalCode);

        // y «Recoger» sobre una fallida ya está más adelante
        var failed2 = await f.SeedOrderAsync(OrderStatuses.Failed);
        Assert.Equal(ScanActionOutcomes.AlreadyBeyond, (await ApplyAsync(f, ScanActions.Pickup, failed2.OrderNumber)).Outcome);
        Assert.Equal(OrderStatuses.Failed, await f.OrderStatusAsync(failed2.Id));
    }

    [Fact]
    public async Task Route_puts_the_order_in_the_open_route_and_leaves_it_assigned_with_the_skipped_statuses_marked()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);   // ni recogida ni en el almacén: el escaneo la lleva hasta Asignada

        var r = await ApplyAsync(f, ScanActions.Route, order.OrderNumber);

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(OrderStatuses.Assigned, r.ToStatus);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned }, r.Skipped.ToArray());
        Assert.Equal(trip.PublicId, r.TripPublicId);
        Assert.Equal(OrderStatuses.Assigned, await f.OrderStatusAsync(order.Id));
        Assert.True(await f.Db.TripOrders.AsNoTracking().AnyAsync(t => t.TransportOrderId == order.Id && t.IsCurrent && t.TripId == trip.TripId));
    }

    [Fact]
    public async Task Route_without_an_open_route_changes_nothing()
    {
        await using var f = await TripServiceFixture.CreateAsync();   // sin rutas abiertas
        var order = await f.SeedOrderAsync(OrderStatuses.Pickup);
        var r = await ApplyAsync(f, ScanActions.Route, order.OrderNumber);
        Assert.Equal(ScanActionOutcomes.NoRoute, r.Outcome);
        Assert.Equal("notfound", r.Voice);
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(order.Id));
        Assert.Empty(await HistoryAsync(f, order.Id));
        Assert.False(await f.Db.TripOrders.AsNoTracking().AnyAsync(t => t.TransportOrderId == order.Id));
    }

    [Fact]
    public async Task Unknown_codes_and_bad_requests_answer_cleanly()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var nf = await ApplyAsync(f, ScanActions.Arrive, "NO-EXISTE-123");
        Assert.Equal(ScanActionOutcomes.NotFound, nf.Outcome);
        Assert.Equal("notfound", nf.Voice);
        var bad = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest("VOLAR", "X"), default));
        Assert.Equal(ScanActions.InvalidActionMessage, bad.Message);
        await Assert.ThrowsAsync<ValidationException>(() => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(ScanActions.Arrive, "  "), default));
    }

    [Fact]
    public void The_actions_map_to_their_statuses()
    {
        Assert.Equal(OrderStatuses.Pickup, ScanActions.TargetStatus(ScanActions.Pickup));
        Assert.Equal(OrderStatuses.Unassigned, ScanActions.TargetStatus(ScanActions.Arrive));
        Assert.Equal(OrderStatuses.Assigned, ScanActions.TargetStatus(ScanActions.Route));
        Assert.Equal(OrderStatuses.InTransit, ScanActions.TargetStatus(ScanActions.Depart));
        Assert.Equal(ScanActions.Arrive, ScanActions.Normalize(" arrive "));
        Assert.Null(ScanActions.Normalize("x"));
    }
}
