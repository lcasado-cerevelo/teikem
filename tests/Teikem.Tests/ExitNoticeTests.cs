using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 143 (2026-10-20) — aviso de salida: reglas puras y el servicio sobre datos reales. Alta que reserva lo que hay y deja el resto pendiente (nunca se rechaza por falta de
/// existencia), solo productos del dueño, rotación al reservar, modificar y cancelar liberan exactamente lo reservado, el pendiente se reserva solo cuando entra mercancía (el aviso
/// más antiguo primero), despachar consume la reserva (y la mueve si se escoge otra posición, salvo con la rotación obligatoria) y deshacer una recolección reabre el aviso.
/// </summary>
public class ExitNoticeTests
{
    // ------------------------------------------------------------------ reglas puras

    [Theory]
    [InlineData(null, ExitNoticeRules.QuantityRequired)]
    [InlineData(0.0, ExitNoticeRules.QuantityRequired)]
    [InlineData(-1.0, ExitNoticeRules.QuantityRequired)]
    [InlineData(1.2345, ExitNoticeRules.QuantityDecimals)]
    [InlineData(100000000.0, ExitNoticeRules.QuantityTooLarge)]
    [InlineData(2.5, null)]
    public void Quantity_is_positive_with_up_to_three_decimals(double? qty, string? expected)
        => Assert.Equal(expected, ExitNoticeRules.QuantityError(qty is null ? null : (decimal)qty));

    [Fact]
    public void Optional_texts_are_trimmed_and_limited()
    {
        Assert.Equal(((string?)null, (string?)null), ExitNoticeRules.NormalizeText("  ", 80, "x"));
        Assert.Equal(("abc", (string?)null), ExitNoticeRules.NormalizeText(" abc ", 80, "x"));
        Assert.Equal(((string?)null, "x"), ExitNoticeRules.NormalizeText(new string('a', 81), 80, "x"));
    }

    [Theory]
    [InlineData("REQUESTED", false, false, null)]               // nada despachado y algo pendiente: sin cambio
    [InlineData("REQUESTED", true, false, "PARTIAL")]            // salió algo, queda más
    [InlineData("PARTIAL", true, false, null)]
    [InlineData("REQUESTED", true, true, "SHIPPED")]             // todo terminado y se despachó algo
    [InlineData("PARTIAL", true, true, "SHIPPED")]
    [InlineData("REQUESTED", false, true, "CANCELLED")]          // todo cerrado con faltante y nada despachado
    [InlineData("SHIPPED", true, true, null)]                    // un estatus final no se recalcula
    [InlineData("CANCELLED", false, true, null)]
    public void Status_follows_the_lines(string current, bool anyShipped, bool allDone, string? expected)
        => Assert.Equal(expected, ExitNoticeRules.NextStatus(current, anyShipped, allDone));

    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("El producto MED no pertenece al cliente del aviso de salida.", ExitNoticeRules.OwnerMismatch("MED"));
        Assert.Equal("No se puede despachar más de lo reservado de MED (3). Reserve primero el pendiente.", ExitNoticeRules.DispatchMoreThanReserved("MED", 3m));
        Assert.Equal("Aviso de salida no encontrado.", ExitNoticeRules.NotFound);
        Assert.Equal("SAL-#####", ExitNoticeCodes.NumberPattern);
    }

    [Fact]
    public void Events_job_and_permission_are_registered()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new ExitNoticeEventProvider() });
        Assert.Equal(ExitNoticeEventCodes.All.OrderBy(x => x), catalog.All.Select(e => e.Code).OrderBy(x => x));
        foreach (var e in catalog.All)
        {
            Assert.InRange(e.Code.Length, 1, 60);
            Assert.Equal(ModuleKeys.WmsLotSerial, e.ModuleKey);
            var known = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), v => Assert.Contains(v, known));
        }
        var job = new ExitNoticeBackorderScanJob();
        Assert.Equal((JobKeys.ExitNoticeBackorderScan, JobScheduleKinds.Interval, 600, true), (job.Key, job.DefaultKind, job.DefaultIntervalSeconds, job.DefaultEnabled));
        Assert.Contains(PermissionCatalog.All, p => p.Code == PermissionCatalog.ExitNoticeCreate);
        Assert.Contains(PermissionCatalog.ExitNoticeCreate, PermissionCatalog.RoleTemplates["WarehouseOperator"]);
    }

    // ------------------------------------------------------------------ el servicio

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Pick, WarehouseBin Reserve, Teikem.Domain.Clients.Client Owner, Product Plain, ExitNoticeService Svc);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<INotificationEventProvider, ExitNoticeEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<ExitNoticeService>();
            s.AddSingleton<SyncService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var pick = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var reserve = await f.AddBinAsync(await f.AddZoneAsync(w, "RZ", ZoneTypes.Reserve), "R-01");
        var owner = await f.AddClientAsync("ASICS");
        var plain = await f.AddProductAsync("MED", ownerClientId: owner.ClientId);
        return new World(f, w, pick, reserve, owner, plain, f.Get<ExitNoticeService>());
    }

    private static Task ReceiveAsync(World x, Product p, decimal qty, WarehouseBin? bin = null, int? lotId = null)
        => x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, qty, LotId: lotId, ToWarehouseId: x.W.WarehouseId, ToBinId: (bin ?? x.Pick).WarehouseBinId));

    private static ExitNoticeRequest Req(World x, params (Product P, decimal Qty)[] lines)
        => new(x.W.PublicId, x.Owner.PublicId, Reference: "OC-9", Destination: "Retiro en muelle 2", Lines: lines.Select(l => new ExitNoticeLineRequest(l.P.PublicId, l.Qty)).ToList());

    private static async Task<decimal> ReservedAsync(WmsFixture f, Product p)
        => (await f.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == p.ProductId).ToListAsync()).Sum(b => b.QtyReserved);

    private static async Task<decimal> OnHandAsync(WmsFixture f, Product p)
        => (await f.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == p.ProductId).ToListAsync()).Sum(b => b.QtyOnHand);

    [Fact]
    public async Task Create_reserves_everything_when_there_is_stock_and_numbers_the_notice()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 4m)), default);
        Assert.Equal(("SAL-00001", ExitNoticeStatuses.Requested, "OC-9", "Retiro en muelle 2"), (n.Number, n.StatusCode, n.Reference, n.Destination));
        var line = Assert.Single(n.Lines);
        Assert.Equal((4m, 4m, 0m, 0m), (line.QtyOrdered, line.QtyReserved, line.QtyBackordered, line.QtyShipped));
        Assert.False(n.HasBackorder);
        Assert.True(n.CanModify && n.CanCancel && n.CanDispatch);
        Assert.Equal(4m, await ReservedAsync(x.F, x.Plain));
        Assert.Single(await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == ExitNoticeEventCodes.Requested).ToListAsync());
    }

    [Fact]
    public async Task Create_never_rejects_for_lack_of_stock_the_rest_is_pending()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 3m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 10m)), default);
        var line = n.Lines.Single();
        Assert.Equal((3m, 7m), (line.QtyReserved, line.QtyBackordered));
        Assert.True(n.HasBackorder);
        Assert.Equal(3m, await ReservedAsync(x.F, x.Plain));

        var none = await x.F.AddProductAsync("VIT", ownerClientId: x.Owner.ClientId);
        var empty = await x.Svc.CreateAsync(Req(x, (none, 5m)), default);   // ni una unidad: todo pendiente
        Assert.Equal((0m, 5m), (empty.Lines.Single().QtyReserved, empty.Lines.Single().QtyBackordered));
        Assert.Equal("SAL-00002", empty.Number);
    }

    [Fact]
    public async Task Only_active_products_of_the_owner_without_series_or_repeats_are_accepted()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("DEPOT");
        var foreign = await x.F.AddProductAsync("FOR", ownerClientId: other.ClientId);
        var own = await x.F.AddProductAsync("OWN");   // propio de la compañía: tampoco es del cliente
        var serial = await x.F.AddProductAsync("SER", TrackingTypes.Serial, ownerClientId: x.Owner.ClientId);

        var mismatch = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(Req(x, (foreign, 1m)), default));
        Assert.Equal(ExitNoticeRules.OwnerMismatch("FOR"), Assert.Single(mismatch.Errors!["lines[0].productPublicId"]));
        var ownMismatch = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(Req(x, (own, 1m)), default));
        Assert.Equal(ExitNoticeRules.OwnerMismatch("OWN"), Assert.Single(ownMismatch.Errors!["lines[0].productPublicId"]));
        var ser = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CreateAsync(Req(x, (serial, 1m)), default));
        Assert.Equal(ExitNoticeRules.SerialNotSupported, ser.Message);
        var dup = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(Req(x, (x.Plain, 1m), (x.Plain, 2m)), default));
        Assert.Equal(ExitNoticeRules.ProductRepeated, Assert.Single(dup.Errors!["lines[1].productPublicId"]));
        var qty = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(Req(x, (x.Plain, 0m)), default));
        Assert.Equal(ExitNoticeRules.QuantityRequired, Assert.Single(qty.Errors!["lines[0].quantity"]));
        var noLines = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(new ExitNoticeRequest(x.W.PublicId, x.Owner.PublicId), default));
        Assert.Equal(ExitNoticeRules.LinesRequired, Assert.Single(noLines.Errors!["lines"]));
        var noClient = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.CreateAsync(new ExitNoticeRequest(x.W.PublicId, null, Lines: new[] { new ExitNoticeLineRequest(x.Plain.PublicId, 1m) }), default));
        Assert.Equal(ExitNoticeRules.ClientRequired, Assert.Single(noClient.Errors!["clientPublicId"]));
        Assert.Empty(await x.F.Db.ExitNotices.AsNoTracking().ToListAsync());   // nada se guardó
    }

    [Fact]
    public async Task A_lot_product_reserves_the_lot_that_expires_first_across_positions()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var p = await x.F.AddProductAsync("LOT-1", TrackingTypes.Lot, ownerClientId: x.Owner.ClientId);
        var today = TenantClock.Default.Today;
        var late = await x.F.AddLotAsync(p, "L-LATE", today.AddDays(90));
        var soon = await x.F.AddLotAsync(p, "L-SOON", today.AddDays(10));
        await ReceiveAsync(x, p, 5m, x.Pick, late.LotId);
        await ReceiveAsync(x, p, 5m, x.Reserve, soon.LotId);

        var n = await x.Svc.CreateAsync(Req(x, (p, 7m)), default);
        var reservations = await x.F.Db.ExitNoticeReservations.AsNoTracking().OrderBy(r => r.ExitNoticeReservationId).ToListAsync();
        Assert.Equal(new int?[] { soon.LotId, late.LotId }, reservations.Select(r => r.LotId).ToArray());   // FEFO: primero el que vence antes
        Assert.Equal(new[] { 5m, 2m }, reservations.Select(r => r.Quantity).ToArray());
        Assert.Equal(7m, n.Lines.Single().QtyReserved);
    }

    [Fact]
    public async Task Pending_is_reserved_when_stock_arrives_oldest_notice_first_with_one_alert_each()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var first = await x.Svc.CreateAsync(Req(x, (x.Plain, 4m)), default);    // sin existencia: todo pendiente
        var second = await x.Svc.CreateAsync(Req(x, (x.Plain, 4m)), default);
        await ReceiveAsync(x, x.Plain, 6m);

        Assert.Equal(2, await x.Svc.ReserveBackordersAsync(default));
        var a = await x.Svc.GetAsync(first.PublicId, default);
        var b = await x.Svc.GetAsync(second.PublicId, default);
        Assert.Equal((4m, 0m), (a.Lines.Single().QtyReserved, a.Lines.Single().QtyBackordered));   // el más antiguo se cubre completo
        Assert.Equal((2m, 2m), (b.Lines.Single().QtyReserved, b.Lines.Single().QtyBackordered));   // el siguiente toma lo que quedó
        Assert.Equal(2, (await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == ExitNoticeEventCodes.BackorderReserved).ToListAsync()).Count);

        Assert.Equal(0, await x.Svc.ReserveBackordersAsync(default));   // nada nuevo: no se repite
    }

    [Fact]
    public async Task Modify_line_up_reserves_more_and_down_frees_the_pending_first()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 5m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 8m)), default);   // 5 reservadas, 3 pendientes
        var id = n.Lines.Single().Id;

        var down = await x.Svc.ModifyLineAsync(n.PublicId, id, new ExitNoticeLineModifyRequest(6m), default);   // baja 2: sale del pendiente
        Assert.Equal((6m, 5m, 1m), (down.Lines.Single().QtyOrdered, down.Lines.Single().QtyReserved, down.Lines.Single().QtyBackordered));
        var lower = await x.Svc.ModifyLineAsync(n.PublicId, id, new ExitNoticeLineModifyRequest(3m), default);    // baja 3 más: 1 del pendiente y 2 de la reserva
        Assert.Equal((3m, 3m, 0m), (lower.Lines.Single().QtyOrdered, lower.Lines.Single().QtyReserved, lower.Lines.Single().QtyBackordered));
        Assert.Equal(3m, await ReservedAsync(x.F, x.Plain));
        var up = await x.Svc.ModifyLineAsync(n.PublicId, id, new ExitNoticeLineModifyRequest(9m), default);       // sube 6: hay 2 más, 4 quedan pendientes
        Assert.Equal((9m, 5m, 4m), (up.Lines.Single().QtyOrdered, up.Lines.Single().QtyReserved, up.Lines.Single().QtyBackordered));

        var bad = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ModifyLineAsync(n.PublicId, id, new ExitNoticeLineModifyRequest(0m), default));
        Assert.Equal(ExitNoticeRules.QuantityRequired, Assert.Single(bad.Errors!["quantity"]));
    }

    [Fact]
    public async Task Cancel_frees_exactly_what_was_reserved_and_a_shipped_notice_cannot_be_cancelled()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 6m)), default);
        var cancelled = await x.Svc.CancelAsync(n.PublicId, "El cliente lo anuló.", default);
        Assert.Equal(ExitNoticeStatuses.Cancelled, cancelled.StatusCode);
        Assert.Equal(0m, await ReservedAsync(x.F, x.Plain));
        Assert.Empty(await x.F.Db.ExitNoticeReservations.AsNoTracking().ToListAsync());
        var again = await Assert.ThrowsAsync<ConflictException>(() => x.Svc.CancelAsync(n.PublicId, null, default));
        Assert.Equal(ExitNoticeRules.AlreadyCancelled, again.Message);

        var n2 = await x.Svc.CreateAsync(Req(x, (x.Plain, 3m)), default);
        await x.Svc.DispatchAsync(n2.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(n2.Lines.Single().Id, 1m) }), default);
        var blocked = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CancelAsync(n2.PublicId, null, default));
        Assert.Equal(ExitNoticeRules.OnlyRequestedCancellable, blocked.Message);
    }

    [Fact]
    public async Task Dispatch_consumes_the_reservation_takes_the_stock_out_and_moves_the_status()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 6m)), default);
        var lineId = n.Lines.Single().Id;

        var tooMuch = await Assert.ThrowsAsync<ConflictException>(() => x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 7m) }), default));
        Assert.Equal(ExitNoticeRules.DispatchMoreThanReserved("MED", 6m), tooMuch.Message);

        var first = await x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 4m) }, "Primera parte"), default);
        Assert.Equal(ExitNoticeStatuses.Partial, first.Notice.StatusCode);
        Assert.Equal((4m, 2m), (first.Notice.Lines.Single().QtyShipped, first.Notice.Lines.Single().QtyReserved));
        Assert.Equal(6m, await OnHandAsync(x.F, x.Plain));
        Assert.Equal(2m, await ReservedAsync(x.F, x.Plain));
        var batch = await x.F.Db.Set<PickBatch>().AsNoTracking().SingleAsync();
        Assert.Equal(first.PickBatchPublicId, batch.PublicId);
        Assert.NotNull(batch.ExitNoticeId);
        Assert.Equal(lineId, (await x.F.Db.Set<PickBatchLine>().AsNoTracking().SingleAsync()).ExitNoticeLineId);

        var second = await x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 2m) }), default);
        Assert.Equal(ExitNoticeStatuses.Shipped, second.Notice.StatusCode);
        Assert.Equal(2, second.Notice.Dispatches.Count);
        Assert.Equal(4m, await OnHandAsync(x.F, x.Plain));
        Assert.Equal(0m, await ReservedAsync(x.F, x.Plain));
        Assert.Single(await x.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == ExitNoticeEventCodes.Shipped).ToListAsync());

        var done = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 1m) }), default));
        Assert.Equal(ExitNoticeRules.NotDispatchable, done.Message);
    }

    [Fact]
    public async Task Dispatching_from_another_position_moves_the_reservation_unless_rotation_is_enforced()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 5m, x.Pick);
        await ReceiveAsync(x, x.Plain, 5m, x.Reserve);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 3m)), default);   // la rotación por antigüedad toma de la primera posición
        var lineId = n.Lines.Single().Id;
        var reservedBin = (await x.F.Db.ExitNoticeReservations.AsNoTracking().SingleAsync()).WarehouseBinId;
        var otherBin = reservedBin == x.Pick.WarehouseBinId ? x.Reserve : x.Pick;

        (await x.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationEnforcement = RotationEnforcements.Enforce;
        await x.F.Db.SaveChangesAsync();
        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 3m, otherBin.WarehouseBinId) }), default));
        Assert.Contains("La rotación es obligatoria", refused.Message);

        (await x.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationEnforcement = RotationEnforcements.Suggest;
        await x.F.Db.SaveChangesAsync();
        var moved = await x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 3m, otherBin.WarehouseBinId) }), default);
        Assert.Equal(ExitNoticeStatuses.Shipped, moved.Notice.StatusCode);
        var line = await x.F.Db.Set<PickBatchLine>().AsNoTracking().SingleAsync();
        Assert.Equal(otherBin.WarehouseBinId, line.FromBinId);   // salió de donde el operario escogió
        Assert.Equal(0m, await ReservedAsync(x.F, x.Plain));     // y no quedó nada reservado en la posición original
    }

    [Fact]
    public async Task Closing_a_pending_line_finishes_it_and_closing_everything_without_shipping_cancels_the_notice()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 5m)), default);   // sin existencia: todo pendiente
        var lineId = n.Lines.Single().Id;
        var closed = await x.Svc.CloseLineAsync(n.PublicId, lineId, null, default);
        Assert.Equal(ExitNoticeStatuses.Cancelled, closed.StatusCode);   // nada que despachar y nada pendiente
        Assert.True(closed.Lines.Single().IsClosed);
        Assert.Equal(5m, closed.Lines.Single().QtyOrdered);              // la cantidad pedida se conserva
        var again = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CloseLineAsync(n.PublicId, lineId, null, default));
        Assert.Equal(ExitNoticeRules.NotOpenForBackorder, again.Message);
    }

    [Fact]
    public async Task Fulfill_line_needs_pending_and_stock()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 5m)), default);
        var lineId = n.Lines.Single().Id;
        var nothing = await Assert.ThrowsAsync<ConflictException>(() => x.Svc.FulfillLineAsync(n.PublicId, lineId, default));
        Assert.Equal(ExitNoticeRules.NothingToFulfill, nothing.Message);
        await ReceiveAsync(x, x.Plain, 2m);
        var partial = await x.Svc.FulfillLineAsync(n.PublicId, lineId, default);
        Assert.Equal((2m, 3m), (partial.Lines.Single().QtyReserved, partial.Lines.Single().QtyBackordered));
        var list = await x.Svc.BackordersAsync(null, default);
        Assert.Equal(("SAL-00001", 3m), (Assert.Single(list).Number, list[0].QtyBackordered));
    }

    [Fact]
    public async Task Undoing_a_dispatch_reopens_the_notice_and_reserves_again()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 4m)), default);
        var lineId = n.Lines.Single().Id;
        var result = await x.Svc.DispatchAsync(n.PublicId, new ExitNoticeDispatchRequest(new[] { new ExitNoticeDispatchLineRequest(lineId, 4m) }), default);
        Assert.Equal(ExitNoticeStatuses.Shipped, result.Notice.StatusCode);
        var batch = await x.F.Db.Set<PickBatch>().AsNoTracking().SingleAsync();
        var batchLine = await x.F.Db.Set<PickBatchLine>().AsNoTracking().SingleAsync();

        // PickBatchService devuelve la existencia a su posición y luego llama al gancho
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Adjustment, x.Plain.ProductId, 4m, ToWarehouseId: x.W.WarehouseId, ToBinId: batchLine.FromBinId, ReasonCode: AdjustmentReasons.PickBatchReversal));
        await x.F.Db.Database.BeginTransactionAsync();
        await x.Svc.OnPickBatchReversedAsync(batch.ExitNoticeId!.Value, batch.Number, new[] { new ExitNoticeReversedLine(lineId, x.Plain.ProductId, batchLine.FromBinId, batchLine.LotId, 4m) }, default);

        var reopened = await x.Svc.GetAsync(n.PublicId, default);
        Assert.Equal(ExitNoticeStatuses.Requested, reopened.StatusCode);
        Assert.Equal((0m, 4m), (reopened.Lines.Single().QtyShipped, reopened.Lines.Single().QtyReserved));
        Assert.Equal(4m, await ReservedAsync(x.F, x.Plain));
    }

    [Fact]
    public async Task List_filters_by_status_text_and_backorder()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var full = await x.Svc.CreateAsync(Req(x, (x.Plain, 2m)), default);
        var pending = await x.Svc.CreateAsync(Req(x, (x.Plain, 20m)), default);   // 8 reservadas y 12 pendientes
        await x.Svc.CancelAsync(full.PublicId, null, default);

        Assert.Equal(2, (await x.Svc.ListAsync(null, null, null, false, null, null, 0, 50, default)).Total);
        Assert.Equal(new[] { pending.Number }, (await x.Svc.ListAsync("requested", null, null, false, null, null, 0, 50, default)).Items.Select(i => i.Number).ToArray());
        Assert.Equal(new[] { pending.Number }, (await x.Svc.ListAsync(null, null, null, true, null, null, 0, 50, default)).Items.Select(i => i.Number).ToArray());
        Assert.Single((await x.Svc.ListAsync(null, null, "Retiro", false, null, null, 0, 1, default)).Items);   // texto en el destino (la base real no distingue mayúsculas)
        Assert.Equal(2, (await x.Svc.ListAsync(null, x.Owner.PublicId, "ASICS", false, null, null, 0, 50, default)).Total);
        Assert.Empty((await x.Svc.ListAsync(null, null, "nadie", false, null, null, 0, 50, default)).Items);
    }

    [Fact]
    public async Task The_device_sync_sends_open_notices_with_the_reserved_bins_and_marks_the_closed_ones_inactive()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, x.Plain, 10m);
        var n = await x.Svc.CreateAsync(Req(x, (x.Plain, 4m)), default);
        var sync = x.F.Get<SyncService>();

        var page = await sync.ExitNoticesAsync(new SyncQuery(WarehousePublicId: x.W.PublicId), default);
        var notice = Assert.Single(page.Items);
        Assert.True(notice.IsActive);
        Assert.Equal(("SAL-00001", ExitNoticeStatuses.Requested, "Cliente ASICS"), (notice.Number, notice.StatusCode, notice.ClientName));
        var line = Assert.Single(notice.Lines);
        Assert.Equal((4m, 4m, 0m), (line.QtyOrdered, line.QtyReserved, line.QtyBackordered));
        var res = Assert.Single(line.Reservations);
        Assert.Equal(("P-01", 4m), (res.BinCode, res.Quantity));

        // cancelado: solo llega con since, y como inactivo (el aparato lo borra)
        await x.Svc.CancelAsync(n.PublicId, null, default);
        Assert.Empty((await sync.ExitNoticesAsync(new SyncQuery(WarehousePublicId: x.W.PublicId), default)).Items);
        var since = await sync.ExitNoticesAsync(new SyncQuery(Since: DateTime.UtcNow.AddMinutes(-30), WarehousePublicId: x.W.PublicId), default);
        Assert.False(Assert.Single(since.Items).IsActive);
    }
}
