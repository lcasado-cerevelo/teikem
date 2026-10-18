using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lotes 107 y 110 — la estación de escaneo con acción escogida (ScanActionService): lo que se escanea son las PIEZAS (el número de la orden no se encuentra); cada acción lleva la orden a su estatus brincando los de en medio
/// (registrados «brincados», misma fecha y hora), las piezas siguientes de la misma acción solo cuentan, repetir una pieza es un duplicado, ya más adelante cuenta, final se ignora, desde Intento fallido se regresa al almacén,
/// Enrutar además la pone en una ruta abierta (y sin ruta no cambia nada), y la pieza que no estuvo en una acción por la que pasó la orden —o al cerrar el conteo— queda faltante «Por verificar».
/// </summary>
public class ScanActionServiceTests
{
    private static Task<ScanActionResultDto> ApplyAsync(TripServiceFixture f, string action, string code)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(action, code, TripServiceFixture.Today), default);

    private static Task<ScanActionResultDto> PieceAsync(TripServiceFixture f, string action, SeededOrder order, int piece = 1)
        => ApplyAsync(f, action, TripServiceFixture.PieceNumberOf(order.Id, piece));

    private static async Task<List<(string To, bool Skipped, DateTime At)>> HistoryAsync(TripServiceFixture f, int orderId)
    {
        var typeId = await f.Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var rows = await f.Db.EntityStatusHistories.AsNoTracking().Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == typeId && h.EntityId == orderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        return rows.Select(h => (h.ToStatus!.InternalCode, h.IsSkipped, h.ChangedAtUtc)).ToList();
    }

    private static async Task<List<OrderPiece>> PiecesAsync(TripServiceFixture f, int orderId)
        => await f.Db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId).OrderBy(p => p.Sequence).ToListAsync();

    [Fact]
    public async Task The_order_number_is_not_scanned_only_pieces_are()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);
        foreach (var code in new[] { order.OrderNumber, order.PackBatchNumber, "NO-EXISTE-123" })
        {
            var r = await ApplyAsync(f, ScanActions.Pickup, code);
            Assert.Equal(ScanActionOutcomes.NotFound, r.Outcome);
            Assert.Equal("notfound", r.Voice);                                // «No»
            Assert.Equal(ScanActions.PieceNotFoundMessage, r.Message);
        }
        Assert.Equal(OrderStatuses.Submitted, await f.OrderStatusAsync(order.Id));
    }

    [Fact]
    public async Task Pickup_takes_a_submitted_order_to_picked_up_with_the_first_piece_and_counts_the_rest()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted, pieces: 3);
        var first = await PieceAsync(f, ScanActions.Pickup, order, 1);

        Assert.Equal(ScanActionOutcomes.Applied, first.Outcome);
        Assert.Equal("found", first.Voice);                                   // «Sí»
        Assert.Equal((OrderStatuses.Submitted, OrderStatuses.Pickup), (first.FromStatus, first.ToStatus));
        Assert.Equal((1, 3, 0), (first.Scanned, first.Total, first.Missing));
        Assert.Equal(TripServiceFixture.PieceNumberOf(order.Id, 1), first.PieceNumber);
        Assert.NotNull((await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == order.Id)).ConfirmedAtUtc);

        var second = await PieceAsync(f, ScanActions.Pickup, order, 2);        // la orden ya está Recogida: la pieza solo cuenta
        Assert.Equal(ScanActionOutcomes.Counted, second.Outcome);
        Assert.Equal("found", second.Voice);
        Assert.Equal((2, 3, 0), (second.Scanned, second.Total, second.Missing));
        Assert.Single(await HistoryAsync(f, order.Id));                       // un solo cambio de estatus
    }

    [Fact]
    public async Task Scanning_the_same_piece_twice_with_the_same_action_is_a_duplicate()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);
        await PieceAsync(f, ScanActions.Arrive, order, 1);
        var again = await PieceAsync(f, ScanActions.Arrive, order, 1);
        Assert.Equal(ScanActionOutcomes.Duplicate, again.Outcome);
        Assert.Equal("dup", again.Voice);                                     // «Ok»
        Assert.Equal(1, again.Scanned);                                       // no se cuenta dos veces
        Assert.Equal(1, await f.Db.OrderPieceScans.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Skipping_registers_the_in_between_statuses_as_skipped_with_the_same_date_and_time()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);
        var r = await PieceAsync(f, ScanActions.Depart, order);

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(OrderStatuses.InTransit, r.ToStatus);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned }, r.Skipped.ToArray());
        var h = await HistoryAsync(f, order.Id);
        Assert.Equal(new[] { true, true, true, false }, h.Select(x => x.Skipped).ToArray());
        Assert.Single(h.Select(x => x.At).Distinct());   // la misma fecha y hora
    }

    [Fact]
    public async Task A_late_piece_of_an_earlier_action_counts_without_moving_the_order_backwards()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.InTransit, pieces: 2);
        var r = await PieceAsync(f, ScanActions.Pickup, order, 1);
        Assert.Equal(ScanActionOutcomes.Counted, r.Outcome);
        Assert.Equal(OrderStatuses.InTransit, await f.OrderStatusAsync(order.Id));
        Assert.Empty(await HistoryAsync(f, order.Id));
    }

    [Theory]
    [InlineData(OrderStatuses.Delivered)]
    [InlineData(OrderStatuses.Cancelled)]
    public async Task A_final_order_is_ignored_without_changing_anything(string finalStatus)
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(finalStatus);
        var r = await PieceAsync(f, ScanActions.Arrive, order);
        Assert.Equal(ScanActionOutcomes.Final, r.Outcome);
        Assert.Equal("notfound", r.Voice);
        Assert.Equal(finalStatus, await f.OrderStatusAsync(order.Id));
        Assert.Empty(await HistoryAsync(f, order.Id));
        Assert.Empty(await f.Db.OrderPieceScans.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task From_a_failed_attempt_arrive_returns_the_order_to_unassigned_and_the_failure_stays_in_the_history()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Failed);
        var r = await PieceAsync(f, ScanActions.Arrive, order);
        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal((OrderStatuses.Failed, OrderStatuses.Unassigned), (r.FromStatus, r.ToStatus));
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(order.Id));
        var typeId = await f.Lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var row = await f.Db.EntityStatusHistories.AsNoTracking().Include(h => h.FromStatus).Where(h => h.EntityTypeLookupId == typeId && h.EntityId == order.Id).SingleAsync();
        Assert.Equal(OrderStatuses.Failed, row.FromStatus!.InternalCode);

        // y «Recoger» sobre una fallida ya está más adelante: la pieza solo cuenta
        var failed2 = await f.SeedOrderAsync(OrderStatuses.Failed);
        Assert.Equal(ScanActionOutcomes.Counted, (await PieceAsync(f, ScanActions.Pickup, failed2)).Outcome);
        Assert.Equal(OrderStatuses.Failed, await f.OrderStatusAsync(failed2.Id));
    }

    [Fact]
    public async Task Route_puts_the_order_in_the_open_route_and_leaves_it_assigned_with_the_skipped_statuses_marked()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var trip = await f.SeedTripAsync("2026-0001", TripStatuses.Draft, f.Zone1);
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted);   // ni recogida ni en el almacén: el escaneo la lleva hasta Asignada

        var r = await PieceAsync(f, ScanActions.Route, order);

        Assert.Equal(ScanActionOutcomes.Applied, r.Outcome);
        Assert.Equal(OrderStatuses.Assigned, r.ToStatus);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned }, r.Skipped.ToArray());
        Assert.Equal(trip.PublicId, r.TripPublicId);
        Assert.Equal(OrderStatuses.Assigned, await f.OrderStatusAsync(order.Id));
        Assert.True(await f.Db.TripOrders.AsNoTracking().AnyAsync(t => t.TransportOrderId == order.Id && t.IsCurrent && t.TripId == trip.TripId));
    }

    [Fact]
    public async Task Route_without_an_open_route_changes_nothing_and_does_not_register_the_piece()
    {
        await using var f = await TripServiceFixture.CreateAsync();   // sin rutas abiertas
        var order = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);
        var r = await PieceAsync(f, ScanActions.Route, order);
        Assert.Equal(ScanActionOutcomes.NoRoute, r.Outcome);
        Assert.Equal("notfound", r.Voice);
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(order.Id));
        Assert.Empty(await HistoryAsync(f, order.Id));
        Assert.False(await f.Db.TripOrders.AsNoTracking().AnyAsync(t => t.TransportOrderId == order.Id));
        Assert.Empty(await f.Db.OrderPieceScans.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_piece_missing_in_an_action_the_order_went_through_becomes_to_verify_when_the_order_moves_on()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Submitted, pieces: 5);
        for (var i = 1; i <= 4; i++) await PieceAsync(f, ScanActions.Arrive, order, i);       // «Llegó» en 4 de 5
        Assert.All(await PiecesAsync(f, order.Id), p => Assert.Null(p.MissingStatus));          // todavía nada: el conteo sigue abierto

        var next = await PieceAsync(f, ScanActions.Route, order, 1);                              // la orden pasa a la acción siguiente sin la quinta pieza (no hay ruta: se prueba con salir)
        Assert.Equal(ScanActionOutcomes.NoRoute, next.Outcome);                                   // sin ruta no cambia nada: todavía no hay faltante
        Assert.All(await PiecesAsync(f, order.Id), p => Assert.Null(p.MissingStatus));

        var depart = await PieceAsync(f, ScanActions.Depart, order, 1);                           // ahora sí sale: la quinta nunca llegó
        Assert.Equal(ScanActionOutcomes.Applied, depart.Outcome);
        var pieces = await PiecesAsync(f, order.Id);
        Assert.Equal(new[] { null, null, null, null, PieceMissing.ToVerify }, pieces.Select(p => p.MissingStatus).ToArray());
        Assert.Equal(1, depart.Missing);
    }

    [Fact]
    public async Task Closing_the_count_marks_what_was_not_scanned_and_a_piece_that_shows_up_stops_being_missing()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var order = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 3);
        await PieceAsync(f, ScanActions.Arrive, order, 1);
        await PieceAsync(f, ScanActions.Arrive, order, 2);

        // sin confirmar: dice qué pasará (la lista de las piezas sin escanear) y no cambia nada
        var asked = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(order.PublicId), default);
        Assert.True(asked.NeedsConfirmation);
        Assert.Equal(new[] { TripServiceFixture.PieceNumberOf(order.Id, 3) }, asked.Preview!.MissingNumbers.ToArray());
        Assert.Contains(TripServiceFixture.PieceNumberOf(order.Id, 3), asked.Message);
        Assert.Null((await PiecesAsync(f, order.Id))[2].MissingStatus);

        var close = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(order.PublicId, true), default);
        Assert.Equal((2, 3, 1), (close.Scanned, close.Total, close.Missing));
        Assert.Equal(new[] { TripServiceFixture.PieceNumberOf(order.Id, 3) }, close.MissingNumbers.ToArray());
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(order.Id));              // el cierre no cambia el estatus
        Assert.Equal(PieceMissing.ToVerify, (await PiecesAsync(f, order.Id))[2].MissingStatus);

        var found = await PieceAsync(f, ScanActions.Arrive, order, 3);                            // aparece
        Assert.Equal(ScanActionOutcomes.Counted, found.Outcome);
        Assert.Equal((3, 3, 0), (found.Scanned, found.Total, found.Missing));
        Assert.All(await PiecesAsync(f, order.Id), p => Assert.Null(p.MissingStatus));
    }

    [Fact]
    public async Task Closing_a_complete_count_leaves_nothing_missing_and_an_order_without_counting_has_nothing_to_close()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var done = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);
        await PieceAsync(f, ScanActions.Arrive, done, 1);
        await PieceAsync(f, ScanActions.Arrive, done, 2);
        var ok = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(done.PublicId), default);
        Assert.Equal((2, 2, 0), (ok.Scanned, ok.Total, ok.Missing));

        var untouched = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);                 // nadie escaneó nada: no hay conteo que cerrar
        var none = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(untouched.PublicId), default);
        Assert.Equal(0, none.Missing);
        await Assert.ThrowsAsync<ValidationException>(() => f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(null), default));
    }

    [Fact]
    public async Task Bad_requests_answer_cleanly()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var bad = await Assert.ThrowsAsync<ValidationException>(() => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest("VOLAR", "X"), default));
        Assert.Equal(ScanActions.InvalidActionMessage, bad.Message);
        await Assert.ThrowsAsync<ValidationException>(() => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(ScanActions.Arrive, "  "), default));
    }

    [Fact]
    public void The_actions_map_to_their_statuses_and_their_order()
    {
        Assert.Equal(OrderStatuses.Pickup, ScanActions.TargetStatus(ScanActions.Pickup));
        Assert.Equal(OrderStatuses.Unassigned, ScanActions.TargetStatus(ScanActions.Arrive));
        Assert.Equal(OrderStatuses.Assigned, ScanActions.TargetStatus(ScanActions.Route));
        Assert.Equal(OrderStatuses.InTransit, ScanActions.TargetStatus(ScanActions.Depart));
        Assert.Equal(new[] { 1, 2, 3, 4 }, ScanActions.All.Select(ScanActions.Rank));
        Assert.Equal(ScanActions.Arrive, ScanActions.ForStatus("UNASSIGNED"));
        Assert.Null(ScanActions.ForStatus("SUBMITTED"));
        Assert.Equal(ScanActions.Arrive, ScanActions.Normalize(" arrive "));
        Assert.Null(ScanActions.Normalize("x"));
    }
}
