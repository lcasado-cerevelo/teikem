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
/// Lote 112 — devolución: la orden queda «Devuelta» y se crea la orden de vuelta (recogido ↔ entrega) con las piezas que se devuelven, desde el botón (nace Sometida) o escaneando «Devolver» (nace Sometida y salta a Recogida);
/// las piezas siguientes se suman a la misma vuelta; el cierre del conteo de la orden devuelta deja «Por verificar» lo que no se devolvió.
/// </summary>
public class ReturnOrderServiceTests
{
    private static ReturnOrderService Svc(TripServiceFixture f) => f.Get<ReturnOrderService>();

    /// <summary>Orden con N piezas, una parada DELIVERY (la del fixture) y una PICKUP agregada.</summary>
    private static async Task<SeededOrder> OrderAsync(TripServiceFixture f, string status = OrderStatuses.Unassigned, int pieces = 4, bool withPickup = true)
    {
        var o = await f.SeedOrderAsync(status, pieces: pieces);
        if (withPickup)
        {
            f.Db.OrderStops.Add(new OrderStop
            {
                OrderStopId = 20000 + o.Id, TransportOrderId = o.Id, StopTypeLookupId = await f.Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup), Sequence = 1, SnapName = "Almacén del cliente", SnapLine1 = "Av. 1", SnapCity = "Bayamón",
                SnapPostalCode = "00956", ServiceMinutes = 5, StatusCodeId = f.Db.OrderStops.First(s => s.TransportOrderId == o.Id).StatusCodeId,
            });
            await f.Db.SaveChangesAsync();
            f.Db.ChangeTracker.Clear();
        }
        return o;
    }

    private static string Piece(SeededOrder o, int i) => TripServiceFixture.PieceNumberOf(o.Id, i);

    [Fact]
    public async Task The_button_returns_the_chosen_pieces_in_a_return_order_that_is_born_submitted_and_leaves_the_order_returned()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderAsync(f);

        var r = await Svc(f).ReturnAsync(o.PublicId, new ReturnOrderRequest(new[] { Piece(o, 1), Piece(o, 2) }, null), default);

        Assert.Equal(2, r.Pieces);
        Assert.Contains(r.ReturnOrderNumber, r.Message);
        Assert.Equal(OrderStatuses.Returned, await f.OrderStatusAsync(o.Id));
        var back = await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.PublicId == r.ReturnOrderPublicId);
        Assert.Equal(o.Id, back.ReturnOfOrderId);
        Assert.Equal(OrderStatuses.Submitted, await f.OrderStatusAsync(back.TransportOrderId));
        Assert.Null(back.QuotedAmount);                                                         // la vuelta no lleva flete
        Assert.Equal(2, back.TotalPieces);
        Assert.False(string.IsNullOrWhiteSpace(back.WaybillNumber));

        // recogido ↔ entrega: la vuelta recoge donde la original entregó y entrega donde la original recogió
        var originalStops = await f.Db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == o.Id).OrderBy(s => s.Sequence).ToListAsync();
        var backStops = await f.Db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == back.TransportOrderId).OrderBy(s => s.Sequence).ToListAsync();
        Assert.Equal(2, backStops.Count);
        Assert.Equal(originalStops[1].SnapLine1, backStops[0].SnapLine1);                       // sale de la entrega de la original
        Assert.Equal(originalStops[0].SnapLine1, backStops[1].SnapLine1);                       // llega al recogido de la original
        Assert.Equal(await f.Lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Pickup), backStops[0].StopTypeLookupId);

        // las piezas se mudaron con sus mismos números; las otras siguen en la original
        var moved = await f.Db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == back.TransportOrderId).OrderBy(p => p.Sequence).ToListAsync();
        Assert.Equal(new[] { Piece(o, 1), Piece(o, 2) }, moved.Select(p => p.PackageNumber).ToArray());
        Assert.Equal(new[] { 1, 2 }, moved.Select(p => p.Sequence).ToArray());
        Assert.Equal(2, await f.Db.OrderPieces.CountAsync(p => p.TransportOrderId == o.Id));
    }

    [Fact]
    public async Task Without_numbers_every_piece_that_is_not_missing_travels()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderAsync(f, pieces: 3);
        var lost = await f.Db.OrderPieces.SingleAsync(p => p.PackageNumber == Piece(o, 3));
        lost.MissingStatus = PieceMissing.Lost;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var r = await Svc(f).ReturnAsync(o.PublicId, new ReturnOrderRequest(null, "Cliente cerrado"), default);
        Assert.Equal(2, r.Pieces);
        Assert.Equal("Cliente cerrado", (await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.PublicId == r.ReturnOrderPublicId)).Notes);
        Assert.Equal(PieceMissing.Lost, (await f.Db.OrderPieces.AsNoTracking().SingleAsync(p => p.PackageNumber == Piece(o, 3))).MissingStatus);   // la perdida sigue en Reclamaciones
    }

    [Fact]
    public async Task Returning_validates_the_order_the_pieces_and_the_client_pickup()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var submitted = await OrderAsync(f, OrderStatuses.Submitted, 1);
        Assert.Equal(ReturnOrderService.SubmittedMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).ReturnAsync(submitted.PublicId, null, default))).Message);

        var delivered = await OrderAsync(f, OrderStatuses.Delivered, 1);
        var fin = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).ReturnAsync(delivered.PublicId, null, default));
        Assert.StartsWith($"La orden {delivered.OrderNumber} está en un estatus final", fin.Message);

        await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).ReturnAsync(Guid.NewGuid(), null, default));

        var o = await OrderAsync(f, pieces: 2);
        var other = await OrderAsync(f, pieces: 1);
        var foreign = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).ReturnAsync(o.PublicId, new ReturnOrderRequest(new[] { Piece(other, 1) }, null), default));
        Assert.Equal(string.Format(ReturnOrderService.PieceNotOfOrderMessage, Piece(other, 1)), foreign.Message);

        var noPickup = await OrderAsync(f, withPickup: false, pieces: 1);                      // sin recogido en la orden ni en el cliente: no hay a dónde regresar
        Assert.Equal(ReturnOrderService.NoPickupMessage, (await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).ReturnAsync(noPickup.PublicId, null, default))).Message);
    }

    // ---------------------------------------------------------------- escaneo

    private static Task<ScanActionResultDto> ReturnScanAsync(TripServiceFixture f, SeededOrder o, int piece)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(ScanActions.Return, Piece(o, piece), TripServiceFixture.Today), default);

    [Fact]
    public async Task Scanning_return_creates_the_return_order_born_submitted_and_jumped_to_pickup_and_the_next_pieces_join_it()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderAsync(f, OrderStatuses.Failed, 4);

        var first = await ReturnScanAsync(f, o, 1);
        Assert.Equal(ScanActionOutcomes.Returned, first.Outcome);
        Assert.Equal("found", first.Voice);
        Assert.Equal(OrderStatuses.Returned, await f.OrderStatusAsync(o.Id));
        var back = await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.ReturnOfOrderId == o.Id);
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(back.TransportOrderId));   // nace Sometida y salta hasta la escaneada
        Assert.NotNull(back.ConfirmedAtUtc);
        var typeId = await f.Db.LookupCodes.Where(l => l.Entity == "EntityType" && l.InternalCode == "TRANSPORT_ORDER").Select(l => l.LookupCodeId).SingleAsync();
        var history = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == back.TransportOrderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal(new[] { OrderStatuses.Submitted, OrderStatuses.Pickup }, history.Select(h => f.Db.StatusCodes.Single(s => s.StatusCodeId == h.ToStatusCodeId).InternalCode).ToArray());

        var second = await ReturnScanAsync(f, o, 2);                                          // la misma orden ya Devuelta: se suma a la misma vuelta
        Assert.Equal(ScanActionOutcomes.Returned, second.Outcome);
        Assert.Contains(back.OrderNumber, second.Message);
        Assert.Equal(1, await f.Db.TransportOrders.CountAsync(x => x.ReturnOfOrderId == o.Id));
        Assert.Equal(2, (await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.TransportOrderId == back.TransportOrderId)).TotalPieces);

        Assert.Equal(ScanActionOutcomes.Duplicate, (await ReturnScanAsync(f, o, 2)).Outcome);   // repetir la misma pieza: duplicado («Ok»)
    }

    [Fact]
    public async Task Scanning_return_refuses_missing_pieces_submitted_orders_and_orders_already_final()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderAsync(f, OrderStatuses.Unassigned, 2);
        var piece = await f.Db.OrderPieces.SingleAsync(p => p.PackageNumber == Piece(o, 1));
        piece.MissingStatus = PieceMissing.ToVerify;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var missing = await ReturnScanAsync(f, o, 1);
        Assert.Equal(ScanActionOutcomes.NotFound, missing.Outcome);
        Assert.Equal("notfound", missing.Voice);

        var delivered = await OrderAsync(f, OrderStatuses.Delivered, 1);
        Assert.Equal(ScanActionOutcomes.Final, (await ReturnScanAsync(f, delivered, 1)).Outcome);

        var submitted = await OrderAsync(f, OrderStatuses.Submitted, 1);
        await Assert.ThrowsAsync<StatusRuleException>(() => ReturnScanAsync(f, submitted, 1));
    }

    [Fact]
    public async Task Closing_the_count_of_a_returned_order_lists_what_was_not_returned_and_leaves_it_to_verify()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderAsync(f, OrderStatuses.Failed, 3);
        await ReturnScanAsync(f, o, 1);

        var asked = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(o.PublicId), default);
        Assert.True(asked.NeedsConfirmation);
        Assert.Equal(new[] { Piece(o, 2), Piece(o, 3) }, asked.Preview!.MissingNumbers.ToArray());

        var closed = await f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(o.PublicId, true), default);
        Assert.Equal(2, closed.Missing);
        Assert.All(await f.Db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == o.Id).ToListAsync(), p => Assert.Equal(PieceMissing.ToVerify, p.MissingStatus));
    }

    // ---------------------------------------------------------------- cargo por devolución (Lote 115)

    private static async Task<SeededOrder> OrderWithFeeAsync(TripServiceFixture f, string? mode, decimal? amount, int pieces = 3, bool byType = false)
    {
        f.Db.Set<Teikem.Domain.Clients.Contract>().Add(new Teikem.Domain.Clients.Contract { ContractId = 1, TenantId = TripServiceFixture.TenantId, ClientId = 1, ContractNumber = "K-1", Title = "Marco", StartDate = new DateOnly(2026, 1, 1), StatusCodeId = 1, ReturnFeeMode = mode, ReturnFeeAmount = amount });
        if (byType) f.Db.ContractReturnFees.Add(new Teikem.Domain.Clients.ContractReturnFee { ContractId = 1, PackageTypeLookupId = 7, Amount = 5m });
        await f.Db.SaveChangesAsync();
        var o = await OrderAsync(f, pieces: pieces);
        var order = await f.Db.TransportOrders.SingleAsync(x => x.TransportOrderId == o.Id);
        order.ContractId = 1;
        foreach (var line in f.Db.CargoLines.Where(l => l.TransportOrderId == o.Id)) line.PackageTypeLookupId = 7;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return o;
    }

    private static async Task<decimal?> FeeOfAsync(TripServiceFixture f, SeededOrder o) => (await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.TransportOrderId == o.Id)).ReturnFeeAmount;

    [Fact]
    public async Task By_order_the_fee_is_charged_once_even_if_more_pieces_are_returned_later()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithFeeAsync(f, "ORDER", 12m);
        await ReturnScanAsync(f, o, 1);
        Assert.Equal(12m, await FeeOfAsync(f, o));
        await ReturnScanAsync(f, o, 2);
        Assert.Equal(12m, await FeeOfAsync(f, o));
    }

    [Fact]
    public async Task By_piece_the_fee_grows_with_each_returned_piece()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithFeeAsync(f, "PIECE", 3m);
        await Svc(f).ReturnAsync(o.PublicId, new ReturnOrderRequest(new[] { Piece(o, 1), Piece(o, 2) }, null), default);
        Assert.Equal(6m, await FeeOfAsync(f, o));
        await ReturnScanAsync(f, o, 3);
        Assert.Equal(9m, await FeeOfAsync(f, o));
    }

    [Fact]
    public async Task By_package_type_the_amount_of_each_returned_type_is_added()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithFeeAsync(f, "PACKAGE_TYPE", null, byType: true);
        await Svc(f).ReturnAsync(o.PublicId, new ReturnOrderRequest(new[] { Piece(o, 1), Piece(o, 2) }, null), default);
        Assert.Equal(10m, await FeeOfAsync(f, o));                                           // 2 piezas del tipo 7 × 5
    }

    [Fact]
    public async Task Without_a_fee_in_the_contract_or_without_a_contract_nothing_is_charged()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var none = await OrderWithFeeAsync(f, null, null);
        await Svc(f).ReturnAsync(none.PublicId, null, default);
        Assert.Null(await FeeOfAsync(f, none));
    }

    [Fact]
    public async Task Without_a_contract_nothing_is_charged()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var noContract = await OrderAsync(f, pieces: 1);
        await Svc(f).ReturnAsync(noContract.PublicId, null, default);
        Assert.Null(await FeeOfAsync(f, noContract));
    }
}
