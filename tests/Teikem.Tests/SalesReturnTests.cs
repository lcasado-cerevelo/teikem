using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

// Lote 81 (P6): devoluciones de venta — pedir (tope y ventana), autorizar, rechazar, cancelar, recibir con lo bueno y lo dañado (recibo tipo RETURN, ledger y daños) y acreditar con notas de crédito.
public sealed class SalesReturnTests
{
    [Fact]
    public void Rules_returnable_window_and_pending()
    {
        Assert.Equal(6m, SalesReturnRules.Returnable(10m, 3m, 1m));
        Assert.Equal(0m, SalesReturnRules.Returnable(10m, 8m, 5m));   // nunca negativo
        var last = new DateOnly(2026, 10, 1);
        Assert.True(SalesReturnRules.WithinWindow(last, 30, new DateOnly(2026, 10, 31)));
        Assert.False(SalesReturnRules.WithinWindow(last, 30, new DateOnly(2026, 11, 1)));
        Assert.True(SalesReturnRules.WithinWindow(last, 0, new DateOnly(2030, 1, 1)));   // sin ventana
        Assert.Equal(4m, SalesReturnRules.PendingToReceive(10m, 6m));
        Assert.Equal(0m, SalesReturnRules.PendingToReceive(10m, 12m));
        Assert.True(SalesReturnRules.IsValidQuantity(1.25m));
        Assert.False(SalesReturnRules.IsValidQuantity(0m));
        Assert.False(SalesReturnRules.IsValidQuantity(1.0005m));
        Assert.Equal("De MED solo se pueden devolver 4 más.", SalesReturnRules.ExceedsReturnable("MED", 4m));
    }

    private sealed class World(WmsFixture f, Warehouse w, Client c, SalesOrderService sales, SalesReturnService returns, BillingService billing, CreditNoteService credits, WarehouseBin staging, WarehouseBin reserve,
        WarehouseBin quarantine, Product p)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Client C { get; } = c;
        public SalesOrderService Sales { get; } = sales;
        public SalesReturnService Returns { get; } = returns;
        public BillingService Billing { get; } = billing;
        public CreditNoteService Credits { get; } = credits;
        public WarehouseBin Staging { get; } = staging;
        public WarehouseBin Reserve { get; } = reserve;
        public WarehouseBin Quarantine { get; } = quarantine;
        public Product P { get; } = p;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<IPurchaseOrderReceiving, ReceivingFakePurchaseOrders>();
            s.AddSingleton<IReceiptConfirmationParticipant, ReceivingFakeCrossDock>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
            s.AddReceiptDamageDeps();
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<BillingService>();
            s.AddSingleton<CreditNoteService>();
            s.AddSingleton<SalesReturnService>();
        });
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Billing);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var pick = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var stg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var q = await f.AddBinAsync(await f.AddZoneAsync(w, "CUA", ZoneTypes.Quarantine), "Q-01");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = pick.WarehouseBinId, QtyOnHand = 30m, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w, c, f.Get<SalesOrderService>(), f.Get<SalesReturnService>(), f.Get<BillingService>(), f.Get<CreditNoteService>(), stg, rsv, q, p);
    }

    /// <summary>Una orden solicitada de `qty` de MED, despachada `shipped`.</summary>
    private static async Task<SalesOrderDto> OrderAsync(World x, decimal qty, decimal shipped)
    {
        var d = await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P.PublicId, qty) }), default);
        var o = await x.Sales.RequestAsync(d.PublicId, null, default);
        return (await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, shipped) }), default)).Order;
    }

    private static SalesReturnRequest Req(SalesOrderDto o, decimal qty, string reason = "DAMAGED")
        => new(o.PublicId, reason, "El cliente la devolvió", new[] { new SalesReturnLineRequest(o.Lines[0].Id, qty) });

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    private static async Task<decimal> OnHandAsync(World x, WarehouseBin b) => await x.F.OnHandAsync(x.P.ProductId, b.WarehouseBinId);

    [Fact]
    public async Task Requesting_validates_and_numbers_the_return_and_the_cap_counts_open_returns()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var r = await x.Returns.CreateAsync(Req(o, 3m), default);
        Assert.Equal(("DV-00001", "REQUESTED", "DAMAGED", 3m), (r.Number, r.Status, r.Reason, r.Lines[0].QtyRequested));
        Assert.Equal((o.Number, 3m), (r.OrderNumber, r.Lines[0].Pending));

        var ret = await x.Returns.ReturnableAsync(o.PublicId, default);
        var line = Assert.Single(ret.Lines);
        Assert.Equal((8m, 0m, 3m, 5m), (line.Shipped, line.Returned, line.Pending, line.Returnable));
        Assert.False(ret.OutsideWindow);

        Assert.Equal(SalesReturnRules.ExceedsReturnable("MED", 5m), await MsgOf(() => x.Returns.CreateAsync(Req(o, 6m), default)));   // 8 − 0 − 3 pendientes
        Assert.Equal("DV-00002", (await x.Returns.CreateAsync(Req(o, 5m, "WRONG_ITEM"), default)).Number);
        Assert.Equal(SalesReturnRules.ExceedsReturnable("MED", 0m), await MsgOf(() => x.Returns.CreateAsync(Req(o, 1m), default)));

        Assert.Equal(SalesReturnRules.OrderRequired, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(Reason: "DAMAGED", Lines: Req(o, 1m).Lines), default)));
        Assert.Equal(SalesReturnRules.ReasonRequired, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(o.PublicId, Lines: Req(o, 1m).Lines), default)));
        Assert.Equal(SalesReturnRules.ReasonInvalid, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(o.PublicId, "NADA", Lines: Req(o, 1m).Lines), default)));
        Assert.Equal(SalesReturnRules.LinesRequired, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(o.PublicId, "DAMAGED", Lines: Array.Empty<SalesReturnLineRequest>()), default)));
        Assert.Equal(SalesReturnRules.NoteTooLong, await MsgOf(() => x.Returns.CreateAsync(Req(o, 1m) with { Note = new string('n', 501) }, default)));
        var o2 = await OrderAsync(x, 10m, 8m);   // una orden sin devoluciones para el resto de las validaciones
        Assert.Equal(SalesReturnRules.LineNotFound, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(o2.PublicId, "DAMAGED", Lines: new[] { new SalesReturnLineRequest(99999, 1m) }), default)));
        Assert.Equal(SalesReturnRules.QuantityInvalid, await MsgOf(() => x.Returns.CreateAsync(Req(o2, 0m), default)));
        Assert.Equal(SalesReturnRules.LineRepeated, await MsgOf(() => x.Returns.CreateAsync(new SalesReturnRequest(o2.PublicId, "DAMAGED", Lines: new[] { new SalesReturnLineRequest(o2.Lines[0].Id, 1m), new SalesReturnLineRequest(o2.Lines[0].Id, 1m) }), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Returns.CreateAsync(Req(o, 1m) with { SalesOrderPublicId = Guid.NewGuid() }, default));
    }

    [Fact]
    public async Task An_order_with_nothing_dispatched_is_not_returnable()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var d = await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P.PublicId, 5m) }), default);
        var o = await x.Sales.RequestAsync(d.PublicId, null, default);
        Assert.Equal(SalesReturnRules.OrderNotReturnable, await MsgOf(() => x.Returns.CreateAsync(Req(o, 1m), default)));
    }

    [Fact]
    public async Task Outside_the_window_only_who_can_authorize_may_request_and_must_confirm()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var batch = await x.F.Db.Set<PickBatch>().AsTracking().SingleAsync();
        batch.CollectedAtUtc = DateTime.UtcNow.AddDays(-45);   // la ventana por omisión es de 30 días
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var ret = await x.Returns.ReturnableAsync(o.PublicId, default);
        Assert.True(ret.OutsideWindow);
        Assert.Equal(30, ret.WindowDays);
        Assert.Equal(SalesReturnRules.OutsideWindowNeedsRight, await MsgOf(() => x.Returns.CreateAsync(Req(o, 2m), default)));   // sin confirmar
        var ok = await x.Returns.CreateAsync(Req(o, 2m) with { ConfirmOutsideWindow = true }, default);
        Assert.True(ok.OutsideWindow);

        x.F.Tenant.IsPlatformAdmin = false;   // sin sales.return.authorize
        Assert.Equal(SalesReturnRules.OutsideWindowNeedsRight, await MsgOf(() => x.Returns.CreateAsync(Req(o, 2m) with { ConfirmOutsideWindow = true }, default)));
        x.F.Tenant.IsPlatformAdmin = true;

        // sin ventana (0) nunca está fuera
        Assert.Equal(0, (await x.Returns.UpdateSettingsAsync(new SalesReturnSettingsRequest(0), default)).WindowDays);
        Assert.False((await x.Returns.ReturnableAsync(o.PublicId, default)).OutsideWindow);
        Assert.Equal(SalesReturnRules.WindowDaysInvalid, await MsgOf(() => x.Returns.UpdateSettingsAsync(new SalesReturnSettingsRequest(-1), default)));
        Assert.Equal(SalesReturnRules.WindowDaysInvalid, await MsgOf(() => x.Returns.UpdateSettingsAsync(new SalesReturnSettingsRequest(4000), default)));
    }

    [Fact]
    public async Task Authorize_reject_and_cancel_follow_the_status_rules()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var a = await x.Returns.CreateAsync(Req(o, 1m), default);
        var auth = await x.Returns.AuthorizeAsync(a.PublicId, "El cliente la lleva el lunes", default);
        Assert.Equal(("AUTHORIZED", "El cliente la lleva el lunes"), (auth.Status, auth.AuthorizationNote));
        Assert.NotNull(auth.AuthorizedAtUtc);
        Assert.Equal(SalesReturnRules.AuthorizeNeedsRequested, await MsgOf(() => x.Returns.AuthorizeAsync(a.PublicId, null, default)));
        Assert.Equal(SalesReturnRules.RejectNeedsRequested, await MsgOf(() => x.Returns.RejectAsync(a.PublicId, "no", default)));
        Assert.Equal("CANCELLED", (await x.Returns.CancelAsync(a.PublicId, "El cliente se arrepintió", default)).Status);
        Assert.Equal(SalesReturnRules.CancelNeedsOpen, await MsgOf(() => x.Returns.CancelAsync(a.PublicId, null, default)));
        // cancelada: lo pedido vuelve a estar disponible
        Assert.Equal(8m, (await x.Returns.ReturnableAsync(o.PublicId, default)).Lines[0].Returnable);

        var b = await x.Returns.CreateAsync(Req(o, 2m), default);
        Assert.Equal(SalesReturnRules.RejectReasonRequired, await MsgOf(() => x.Returns.RejectAsync(b.PublicId, " ", default)));
        var rej = await x.Returns.RejectAsync(b.PublicId, "Fuera de política", default);
        Assert.Equal(("REJECTED", "Fuera de política"), (rej.Status, rej.AuthorizationNote));
        Assert.Equal(SalesReturnRules.ReceiveNeedsAuthorized, await MsgOf(() => x.Returns.ReceiveAsync(b.PublicId, new SalesReturnReceiveRequest(new[] { new SalesReturnReceiveLineRequest(b.Lines[0].Id, 1m) }), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Returns.AuthorizeAsync(Guid.NewGuid(), null, default));
    }

    private static SalesReturnReceiveRequest Receive(SalesReturnDto r, decimal good, decimal damaged = 0m, string? note = null, bool? discard = null, int? target = null, bool? closeRest = null)
        => new(new[] { new SalesReturnReceiveLineRequest(r.Lines[0].Id, good, damaged, note, discard, target) }, closeRest);

    [Fact]
    public async Task Receiving_creates_a_return_receipt_moves_the_good_to_stock_quarantines_the_damaged_and_counts_as_returned()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);   // salen 8 de la posición de recolección (quedan 22)
        var r = await x.Returns.AuthorizeAsync((await x.Returns.CreateAsync(Req(o, 5m), default)).PublicId, null, default);
        var before = await x.F.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == x.P.ProductId).SumAsync(b => b.QtyOnHand);

        Assert.Equal(SalesReturnRules.ReceiveLinesRequired, await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, new SalesReturnReceiveRequest(Array.Empty<SalesReturnReceiveLineRequest>()), default)));
        Assert.Equal(SalesReturnRules.ReceiveLineNotFound, await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, new SalesReturnReceiveRequest(new[] { new SalesReturnReceiveLineRequest(99999, 1m) }), default)));
        Assert.Equal(SalesReturnRules.ReceiveQuantityInvalid, await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, 0m), default)));
        Assert.Equal(SalesReturnRules.ReceiveQuantityInvalid, await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, -1m, 2m), default)));
        Assert.Equal(SalesReturnRules.DamagedNeedsNote, await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, 1m, 1m), default)));
        Assert.Equal(SalesReturnRules.ExceedsPending("MED", 5m), await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, 4m, 2m, "caja rota"), default)));

        var got = await x.Returns.ReceiveAsync(r.PublicId, Receive(r, 3m, 1m, "caja rota", target: x.Reserve.WarehouseBinId), default);
        Assert.Equal(("RECEIVED", 4m, 1m, 1m), (got.Status, got.Lines[0].QtyReceived, got.Lines[0].QtyDamaged, got.Lines[0].Pending));
        var receipt = Assert.Single(got.Receipts);
        var header = await x.F.Db.Set<ReceiptHeader>().AsNoTracking().SingleAsync(h => h.PublicId == receipt.PublicId);
        Assert.Equal((r.Number, x.F.LookupId(LookupDomains.ReceiptType, ReceiptTypes.Return)), (header.Reference, header.ReceiptTypeLookupId));
        Assert.NotNull(header.SalesReturnId);

        // lo bueno (3) entró; lo dañado (1) sigue el flujo de daños con la causa «Devolución de cliente»
        var after = await x.F.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == x.P.ProductId).SumAsync(b => b.QtyOnHand);
        Assert.Equal(4m, after - before);   // 3 buenas + 1 dañada en cuarentena (cuenta como existencia, no como disponible)
        var damage = await x.F.Db.Set<DamageReport>().AsNoTracking().SingleAsync();
        Assert.Equal((1m, x.F.LookupId(LookupDomains.DamageCause, DamageCauses.CustomerReturn)), (damage.Quantity, damage.CauseLookupId));
        Assert.Equal(1m, await OnHandAsync(x, x.Quarantine));

        // la línea de la orden cuenta lo devuelto y ya no es facturable
        var line = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync();
        Assert.Equal((4m, 4m), (line.QtyReturned, BillingRules.Billable(line.QtyShipped, line.QtyReturned, line.QtyInvoiced)));
        Assert.Equal(3m, (await x.Returns.ReturnableAsync(o.PublicId, default)).Lines[0].Returnable);   // 8 despachadas − 4 devueltas − 1 todavía pendiente
    }

    [Fact]
    public async Task Receiving_in_parts_the_rest_cannot_pass_what_is_pending_and_close_rest_releases_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var r = await x.Returns.AuthorizeAsync((await x.Returns.CreateAsync(Req(o, 6m), default)).PublicId, null, default);
        var first = await x.Returns.ReceiveAsync(r.PublicId, Receive(r, 2m, target: x.Reserve.WarehouseBinId), default);
        Assert.Equal(("RECEIVED", 4m), (first.Status, first.Lines[0].Pending));
        Assert.Equal(SalesReturnRules.ExceedsPending("MED", 4m), await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, 5m), default)));

        // lo pendiente (4) sigue contando contra lo que se puede devolver hasta cerrarlo
        Assert.Equal(2m, (await x.Returns.ReturnableAsync(o.PublicId, default)).Lines[0].Returnable);   // 8 − 2 devuelto − 4 pendiente
        var closed = await x.Returns.ReceiveAsync(r.PublicId, Receive(r, 1m, target: x.Reserve.WarehouseBinId, closeRest: true), default);
        Assert.Equal((3m, 0m, 2, 5m), (closed.Lines[0].QtyReceived, closed.Lines[0].Pending, closed.Receipts.Count, (await x.Returns.ReturnableAsync(o.PublicId, default)).Lines[0].Returnable));   // 8 − 3
        Assert.Equal(SalesReturnRules.ExceedsPending("MED", 0m), await MsgOf(() => x.Returns.ReceiveAsync(r.PublicId, Receive(r, 1m), default)));
        Assert.Equal(SalesReturnRules.CancelNeedsOpen, await MsgOf(() => x.Returns.CancelAsync(r.PublicId, null, default)));   // ya se recibió algo
    }

    [Fact]
    public async Task Crediting_a_received_return_creates_a_draft_credit_note_per_invoice_and_marks_it_credited()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var run = await x.Billing.GenerateAsync(new BillingRunRequest(x.C.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), default);
        await x.Billing.ReviewAsync(run.Id, default);
        var invoice = (await x.Billing.ApproveAsync(run.Id, default)).Invoices[0];   // 8 × 10 = 80

        var r = await x.Returns.AuthorizeAsync((await x.Returns.CreateAsync(Req(o, 3m), default)).PublicId, null, default);
        Assert.Equal(SalesReturnRules.CreditNeedsReceived, await MsgOf(() => x.Returns.CreditAsync(r.PublicId, default)));
        await x.Returns.ReceiveAsync(r.PublicId, Receive(r, 3m, target: x.Reserve.WarehouseBinId), default);
        var credited = await x.Returns.CreditAsync(r.PublicId, default);
        Assert.Equal(("CREDITED", 3m), (credited.Status, credited.Lines[0].QtyCredited));
        var note = Assert.Single(credited.CreditNotes);
        Assert.Equal("DRAFT", note.Status);
        Assert.Equal(30m, (await x.Credits.GetAsync(note.PublicId, default)).Subtotal);   // 3 × 10
        var n = await x.Credits.GetAsync(note.PublicId, default);
        Assert.Equal(("RETURN", invoice.Number, 3m), (n.Reason, n.InvoiceNumber, n.Lines[0].Quantity));
        Assert.Equal(3m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyCredited);
        Assert.Equal(SalesReturnRules.CreditNeedsReceived, await MsgOf(() => x.Returns.CreditAsync(r.PublicId, default)));   // ya está acreditada
    }

    [Fact]
    public async Task What_was_never_invoiced_has_nothing_to_credit_but_is_no_longer_billable()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);   // sin corrida: nada facturado
        var r = await x.Returns.AuthorizeAsync((await x.Returns.CreateAsync(Req(o, 3m), default)).PublicId, null, default);
        await x.Returns.ReceiveAsync(r.PublicId, Receive(r, 3m, target: x.Reserve.WarehouseBinId), default);
        Assert.Equal(SalesReturnRules.NothingToCredit, await MsgOf(() => x.Returns.CreditAsync(r.PublicId, default)));
        var run = await x.Billing.GenerateAsync(new BillingRunRequest(x.C.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), default);
        Assert.Equal(50m, run.Invoices[0].Subtotal);   // 8 − 3 devueltas = 5 × 10
    }

    [Fact]
    public async Task Lists_filter_by_status_client_order_and_text_and_returns_are_isolated_by_company()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 8m);
        var a = await x.Returns.CreateAsync(Req(o, 1m), default);
        var b = await x.Returns.CreateAsync(Req(o, 2m), default);
        await x.Returns.AuthorizeAsync(b.PublicId, null, default);
        Assert.Equal(2, (await x.Returns.ListAsync(null, null, null, null, 0, 50, default)).Total);
        Assert.Equal(b.Number, Assert.Single((await x.Returns.ListAsync("authorized", null, null, null, 0, 50, default)).Items).Number);
        Assert.Equal(2, (await x.Returns.ListAsync(null, x.C.PublicId, o.PublicId, "DV-", 0, 50, default)).Total);
        Assert.Equal(a.Number, Assert.Single((await x.Returns.ListAsync(null, null, null, a.Number, 0, 50, default)).Items).Number);
        Assert.Equal(0, (await x.Returns.ListAsync(null, Guid.NewGuid(), null, null, 0, 50, default)).Total);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => x.Returns.GetAsync(a.PublicId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Returns.AuthorizeAsync(a.PublicId, null, default));
            Assert.Equal(0, (await x.Returns.ListAsync(null, null, null, null, 0, 50, default)).Total);
        }
    }
}
