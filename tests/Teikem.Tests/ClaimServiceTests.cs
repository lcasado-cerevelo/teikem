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
/// Lote 111 — faltantes y reclamaciones: Por verificar → Perdido → Reclamada → Resuelta, la reclamación REC-##### (orden, piezas, descripción, valor), lo que pasa cuando una pieza aparece (se quita de su reclamación; si esta se queda sin piezas se da de baja;
/// de una reclamación cerrada no se acepta) y las validaciones.
/// </summary>
public class ClaimServiceTests
{
    private static ClaimService Svc(TripServiceFixture f) => f.Get<ClaimService>();

    /// <summary>Orden con N piezas de las que las primeras <paramref name="toVerify"/> quedan «Por verificar».</summary>
    private static async Task<SeededOrder> OrderWithMissingAsync(TripServiceFixture f, int pieces = 4, int toVerify = 3, string status = OrderStatuses.Pickup)
    {
        var order = await f.SeedOrderAsync(status, pieces: pieces);
        var ps = await f.Db.OrderPieces.Where(p => p.TransportOrderId == order.Id).OrderBy(p => p.Sequence).ToListAsync();
        foreach (var p in ps.Take(toVerify)) { p.MissingStatus = PieceMissing.ToVerify; p.MissingAtUtc = DateTime.UtcNow; }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return order;
    }

    private static string[] Numbers(SeededOrder o, params int[] which) => which.Select(i => TripServiceFixture.PieceNumberOf(o.Id, i)).ToArray();

    private static async Task<List<OrderPiece>> PiecesAsync(TripServiceFixture f, int orderId)
        => await f.Db.OrderPieces.AsNoTracking().Where(p => p.TransportOrderId == orderId).OrderBy(p => p.Sequence).ToListAsync();

    [Fact]
    public async Task The_queue_lists_orders_with_pieces_to_verify_or_lost_and_hides_the_rest()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var a = await OrderWithMissingAsync(f);
        await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);                       // sin faltantes: no sale
        var queue = await Svc(f).ToVerifyAsync(null, 0, 25, default);

        Assert.Equal(1, queue.Total);
        var item = queue.Items.Single();
        Assert.Equal(a.PublicId, item.OrderPublicId);
        Assert.Equal(4, item.TotalPieces);
        Assert.Equal(3, item.Pieces.Count);
        Assert.All(item.Pieces, p => Assert.Equal("Por verificar", p.StatusLabel));
        Assert.Equal(1, (await Svc(f).ToVerifyAsync(TripServiceFixture.PieceNumberOf(a.Id, 2), 0, 25, default)).Total);   // se busca por número de pieza
        Assert.Equal(0, (await Svc(f).ToVerifyAsync("NO-HAY", 0, 25, default)).Total);
    }

    [Fact]
    public async Task Marking_lost_moves_to_verify_pieces_to_lost_all_or_the_chosen_ones()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f);
        var one = await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, Numbers(o, 1)), default);
        Assert.Equal(new[] { "LOST", "TO_VERIFY", "TO_VERIFY" }, one.Pieces.Select(p => p.Status).ToArray());

        var rest = await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);     // sin números: todas las «Por verificar»
        Assert.All(rest.Pieces, p => Assert.Equal("LOST", p.Status));
    }

    [Fact]
    public async Task Marking_lost_validates_the_pieces_and_the_order()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f);
        var other = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 1);
        await Assert.ThrowsAsync<ValidationException>(() => Svc(f).MarkLostAsync(new MarkLostRequest(null, null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).MarkLostAsync(new MarkLostRequest(Guid.NewGuid(), null), default));
        var notOfOrder = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, Numbers(other, 1)), default));
        Assert.Equal(string.Format(ClaimRules.PieceNotOfOrderMessage, Numbers(other, 1)[0]), notOfOrder.Message);
        var notToVerify = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, Numbers(o, 4)), default));   // la 4.ª no está faltante
        Assert.Equal(string.Format(ClaimRules.PieceNotToVerifyMessage, Numbers(o, 4)[0]), notToVerify.Message);
        var none = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);
        var empty = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).MarkLostAsync(new MarkLostRequest(none.PublicId, null), default));
        Assert.Equal(ClaimRules.NoPiecesMessage, empty.Message);
    }

    [Fact]
    public async Task A_claim_gathers_the_lost_pieces_of_an_order_with_its_value_and_leaves_them_claimed()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, Numbers(o, 1, 2)), default);                   // la 3.ª sigue «Por verificar»

        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 250.50m, "  Llamar al cliente  "), default);

        Assert.StartsWith("REC-", claim.Number);
        Assert.Matches("^REC-[0-9]{5}$", claim.Number);
        Assert.Equal((ClaimStatuses.Claimed, 250.50m, "Llamar al cliente"), (claim.Status, claim.DeclaredValue, claim.Notes));
        Assert.Equal(new[] { "CLAIMED", "CLAIMED" }, claim.Pieces.Select(p => p.Status).ToArray());
        Assert.All(claim.Pieces, p => Assert.Equal(claim.Number, p.ClaimNumber));
        Assert.StartsWith("2 × ", claim.Description);
        var pieces = await PiecesAsync(f, o.Id);
        Assert.Equal(new[] { "CLAIMED", "CLAIMED", "TO_VERIFY", null }, pieces.Select(p => p.MissingStatus).ToArray());   // la que no se perdió queda como estaba

        var again = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, null, null), default));   // ya no hay perdidas sin reclamar
        Assert.Equal(ClaimRules.NoLostPiecesMessage, again.Message);
        var list = await Svc(f).ListAsync(null, null, 0, 25, default);
        Assert.Equal((1, claim.Number, 2), (list.Total, list.Items.Single().Number, list.Items.Single().Pieces));
    }

    [Fact]
    public async Task Claiming_validates_the_value_the_notes_and_that_the_pieces_are_lost()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f);
        await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, Numbers(o, 1), null, null), default));   // sigue «Por verificar»: hay que marcarla perdida primero
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var neg = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, -1m, null), default));
        Assert.Equal(ClaimRules.NegativeValueMessage, neg.Message);
        var long_ = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 1m, new string('x', ClaimRules.NotesMax + 1)), default));
        Assert.Equal(ClaimRules.NotesTooLongMessage, long_.Message);
    }

    [Fact]
    public async Task Resolving_a_claim_marks_it_resolved_with_its_resolution_and_its_pieces_resolved()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 2);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 100m, null), default);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("QUIEN SABE", null, null, null), default));
        Assert.Equal(ClaimRules.InvalidResolutionMessage, bad.Message);
        var noAmount = await Assert.ThrowsAsync<ValidationException>(() => Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("paid", null, null, null), default));
        Assert.Equal(ClaimRules.AmountRequiredMessage, noAmount.Message);

        var done = await Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("paid", 80m, "Pagada por la aseguradora", null), default);
        Assert.Equal((ClaimStatuses.Resolved, ClaimResolutions.Paid, 80m, "Pagada por la aseguradora"), (done.Status, done.Resolution, done.AmountPaid, done.ResolutionNotes));
        Assert.NotNull(done.ResolvedAtUtc);
        Assert.All(await PiecesAsync(f, o.Id), p => { if (p.ClaimId is not null) Assert.Equal(PieceMissing.Resolved, p.MissingStatus); });
        var twice = await Assert.ThrowsAsync<ConflictException>(() => Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("closed", null, null, null), default));
        Assert.Equal(ClaimRules.AlreadyResolvedMessage, twice.Message);
        Assert.Equal(1, (await Svc(f).ListAsync("RESOLVED", null, 0, 25, default)).Total);
        Assert.Equal(0, (await Svc(f).ListAsync("CLAIMED", null, 0, 25, default)).Total);
    }

    [Fact]
    public async Task A_rejected_or_closed_claim_does_not_keep_the_paid_amount()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 1);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);
        var done = await Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("REJECTED", 55m, null, null), default);
        Assert.Equal((ClaimResolutions.Rejected, (decimal?)null), (done.Resolution, done.AmountPaid));
    }

    // ---------------------------------------------------------------- una pieza que aparece (escaneo)

    private static Task<ScanActionResultDto> ScanAsync(TripServiceFixture f, SeededOrder o, int piece, string action = ScanActions.Arrive)
        => f.Get<ScanActionService>().ApplyAsync(new ScanActionRequest(action, TripServiceFixture.PieceNumberOf(o.Id, piece), TripServiceFixture.Today), default);

    [Fact]
    public async Task A_claimed_piece_that_shows_up_leaves_its_claim_and_waits_for_its_new_order()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 2);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);

        var r = await ScanAsync(f, o, 1);
        Assert.Equal(ScanActionOutcomes.Reappeared, r.Outcome);
        Assert.Equal("found", r.Voice);                                                              // «Sí»
        Assert.Contains(claim.Number, r.Message);
        var piece = (await PiecesAsync(f, o.Id))[0];
        Assert.Null(piece.MissingStatus);
        Assert.Null(piece.ClaimId);
        Assert.NotNull(piece.ReappearedAtUtc);
        Assert.Equal(ScanActions.Arrive, piece.ReappearedAction);
        Assert.Equal(OrderStatuses.Pickup, await f.OrderStatusAsync(o.Id));                          // su orden original no se mueve
        var still = await Svc(f).GetAsync(claim.PublicId, default);
        Assert.Equal(1, still.Pieces.Count);                                                          // se le rebajó la pieza
        Assert.Equal(ScanActionOutcomes.Duplicate, (await ScanAsync(f, o, 1)).Outcome);               // escanearla otra vez con la misma acción: duplicado («Ok»)
    }

    [Fact]
    public async Task A_claim_that_loses_all_its_pieces_is_taken_down()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 2);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);

        await ScanAsync(f, o, 1);
        await ScanAsync(f, o, 2);

        Assert.False((await f.Db.Claims.AsNoTracking().SingleAsync()).IsActive);
        Assert.Equal(0, (await Svc(f).ListAsync(null, null, 0, 25, default)).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).GetAsync(claim.PublicId, default));
    }

    [Fact]
    public async Task A_piece_of_a_closed_claim_is_not_accepted()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 1);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        var claim = await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);
        await Svc(f).ResolveAsync(claim.PublicId, new ClaimResolveRequest("closed", null, null, null), default);

        var r = await ScanAsync(f, o, 1);
        Assert.Equal(ScanActionOutcomes.NotFound, r.Outcome);
        Assert.Equal("notfound", r.Voice);                                                           // «No»
        Assert.Equal(ClaimRules.ClosedClaimPieceMessage, r.Message);
        Assert.Equal(PieceMissing.Resolved, (await PiecesAsync(f, o.Id))[0].MissingStatus);
    }

    [Fact]
    public async Task A_lost_piece_of_an_order_that_already_closed_is_not_ignored_as_final_when_it_shows_up()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, toVerify: 1, status: OrderStatuses.Delivered);               // la orden se entregó incompleta
        var r = await ScanAsync(f, o, 1);
        Assert.Equal(ScanActionOutcomes.Reappeared, r.Outcome);                                      // en vez de «FINAL»: la pieza espera su orden nueva
    }

    // ---------------------------------------------------------------- la orden nueva al cerrar el conteo (111b)

    private static Task<ScanCloseCountResultDto> CloseAsync(TripServiceFixture f, SeededOrder o, bool? confirm)
        => f.Get<ScanActionService>().CloseCountAsync(new ScanCloseCountRequest(o.PublicId, confirm), default);

    [Fact]
    public async Task Closing_the_count_asks_for_confirmation_when_pieces_reappeared_and_changes_nothing()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, pieces: 3, toVerify: 2);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);
        await ScanAsync(f, o, 1);

        var asked = await CloseAsync(f, o, null);
        Assert.True(asked.NeedsConfirmation);
        Assert.NotNull(asked.Preview);
        Assert.Equal(new[] { TripServiceFixture.PieceNumberOf(o.Id, 1) }, asked.Preview!.ReappearedNumbers);
        Assert.Null(asked.NewOrderPublicId);
        Assert.Equal(1, await f.Db.TransportOrders.AsNoTracking().CountAsync());               // todavía no se creó nada
        Assert.NotNull((await PiecesAsync(f, o.Id))[0].ReappearedAtUtc);
    }

    [Fact]
    public async Task Confirming_creates_one_new_order_with_the_pieces_that_reappeared_for_the_same_client_and_consignee()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, pieces: 4, toVerify: 3);
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, 10m, null), default);
        await ScanAsync(f, o, 1);
        await ScanAsync(f, o, 2);

        var done = await CloseAsync(f, o, true);
        Assert.False(done.NeedsConfirmation);
        Assert.NotNull(done.NewOrderPublicId);
        Assert.Contains(done.NewOrderNumber!, done.Message);

        var original = await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.TransportOrderId == o.Id);
        var created = await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.PublicId == done.NewOrderPublicId);
        Assert.NotEqual(original.TransportOrderId, created.TransportOrderId);
        Assert.Equal((original.ClientId, original.ServiceTypeLookupId, 2), (created.ClientId, created.ServiceTypeLookupId, created.TotalPieces));
        Assert.False(string.IsNullOrWhiteSpace(created.WaybillNumber));
        Assert.NotEqual(original.WaybillNumber, created.WaybillNumber);
        Assert.Null(created.QuotedAmount);                                                      // no se cotiza ni se cobra de nuevo
        Assert.Equal(await f.Db.OrderStops.CountAsync(s => s.TransportOrderId == o.Id), await f.Db.OrderStops.CountAsync(s => s.TransportOrderId == created.TransportOrderId));
        Assert.Equal(OrderStatuses.Unassigned, await f.OrderStatusAsync(created.TransportOrderId));   // Llegó
        var moved = await PiecesAsync(f, created.TransportOrderId);
        Assert.Equal(Numbers(o, 1, 2), moved.Select(p => p.PackageNumber).ToArray());
        Assert.All(moved, p => Assert.Equal((null, null, null), (p.MissingStatus, p.ClaimId, (DateTime?)p.ReappearedAtUtc)));
        Assert.Equal(new[] { 1, 2 }, moved.Select(p => p.Sequence).ToArray());
        var left = await PiecesAsync(f, o.Id);
        Assert.Equal(2, left.Count);                                                             // la original conserva las suyas (la 3.ª sigue «Por verificar»)
        Assert.Equal(PieceMissing.Claimed, left.First(p => p.PackageNumber == Numbers(o, 3)[0]).MissingStatus);

        var again = await CloseAsync(f, o, true);                                               // cerrar otra vez no crea otra
        Assert.Null(again.NewOrderPublicId);
        Assert.Equal(2, await f.Db.TransportOrders.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task The_new_order_repeats_the_submitted_and_pickup_history_of_the_original_with_the_same_dates_before_the_scanned_status()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await OrderWithMissingAsync(f, pieces: 3, toVerify: 2);
        var typeId = await f.Db.LookupCodes.Where(l => l.Entity == "EntityType" && l.InternalCode == "TRANSPORT_ORDER").Select(l => l.LookupCodeId).SingleAsync();
        int Status(string code) => f.Db.StatusCodes.Single(x => x.Entity == "OrderStatus" && x.InternalCode == code).StatusCodeId;
        var submittedAt = new DateTime(2026, 9, 1, 14, 0, 0, DateTimeKind.Utc);
        var pickupAt = new DateTime(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc);
        f.Db.EntityStatusHistories.Add(new Teikem.Domain.Catalogs.EntityStatusHistory { TenantId = TripServiceFixture.TenantId, EntityTypeLookupId = typeId, EntityId = o.Id, FromStatusCodeId = null, ToStatusCodeId = Status(OrderStatuses.Submitted), ChangedAtUtc = submittedAt });
        f.Db.EntityStatusHistories.Add(new Teikem.Domain.Catalogs.EntityStatusHistory { TenantId = TripServiceFixture.TenantId, EntityTypeLookupId = typeId, EntityId = o.Id, FromStatusCodeId = Status(OrderStatuses.Submitted), ToStatusCodeId = Status(OrderStatuses.Pickup), ChangedAtUtc = pickupAt });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await Svc(f).MarkLostAsync(new MarkLostRequest(o.PublicId, null), default);
        await Svc(f).CreateAsync(new ClaimCreateRequest(o.PublicId, null, null, null), default);
        await ScanAsync(f, o, 1);

        var done = await CloseAsync(f, o, true);
        var created = await f.Db.TransportOrders.AsNoTracking().SingleAsync(x => x.PublicId == done.NewOrderPublicId);
        var history = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == created.TransportOrderId).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.Equal(new[] { Status(OrderStatuses.Submitted), Status(OrderStatuses.Pickup), Status(OrderStatuses.Unassigned) }, history.Select(h => h.ToStatusCodeId).ToArray());
        Assert.Equal(submittedAt, history[0].ChangedAtUtc);                                       // Sometida con la fecha de la original
        Assert.Equal(pickupAt, history[1].ChangedAtUtc);                                          // Recogida con la fecha de la original
        Assert.True(history[2].ChangedAtUtc > pickupAt);                                          // el estatus del escaneo es de ahora
        Assert.Equal(pickupAt, created.ConfirmedAtUtc);
    }

    [Fact]
    public async Task Closing_the_count_without_reappeared_pieces_needs_no_confirmation()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Pickup, pieces: 2);
        var r = await CloseAsync(f, o, null);
        Assert.False(r.NeedsConfirmation);
        Assert.Null(r.Preview);
    }

    [Fact]
    public void The_claim_number_is_the_id_with_zeros_and_the_resolutions_normalize()
    {
        Assert.Equal("REC-00012", ClaimRules.NumberOf(12));
        Assert.Equal("PAID", ClaimResolutions.Normalize(" paid "));
        Assert.Null(ClaimResolutions.Normalize("otra"));
    }
}
