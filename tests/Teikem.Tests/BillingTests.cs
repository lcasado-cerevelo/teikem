using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 72 (P5): facturación — corrida (generar, revisar, aprobar, cancelar), venta de producto de las órdenes de venta, nunca dos veces, ajuste manual, anular y la base «al entregar».
public sealed class BillingTests
{
    [Fact]
    public void Rules_billable_tax_portion_and_due_date()
    {
        Assert.Equal(4m, BillingRules.Billable(10m, 2m, 4m));
        Assert.Equal(0m, BillingRules.Billable(3m, 0m, 5m));   // nunca negativo
        Assert.Equal(1.15m, BillingRules.TaxPortion(2.30m, 10m, 5m, 10m, 0m));   // proporcional
        Assert.Equal(1.15m, BillingRules.TaxPortion(2.30m, 10m, 5m, 5m, 1.15m));   // la última parte toma el resto exacto
        Assert.Equal(0.77m, BillingRules.TaxPortion(2.30m, 3m, 1m, 3m, 0m));
        Assert.Equal(0.76m, BillingRules.TaxPortion(2.30m, 3m, 1m, 1m, 1.54m));   // ...así las tres partes suman 2.30
        Assert.Equal(0m, BillingRules.TaxPortion(0m, 3m, 1m, 3m, 0m));
        Assert.Equal(new DateOnly(2026, 11, 15), BillingRules.DueDate(new DateOnly(2026, 10, 16), 30));
        Assert.Equal(new DateOnly(2026, 10, 16), BillingRules.DueDate(new DateOnly(2026, 10, 16), -5));
    }

    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService sales, BillingService billing, WarehouseBin bin, Product p)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Sales { get; } = sales;
        public BillingService Billing { get; } = billing;
        public WarehouseBin Bin { get; } = bin;
        public Product P { get; } = p;
    }

    private static async Task<World> NewWorldAsync(decimal stock = 20m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<BillingService>();
        });
        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Billing);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = stock, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w, c, f.Get<SalesOrderService>(), f.Get<BillingService>(), bin, p);
    }

    /// <summary>Una orden solicitada de `qty` de MED y, si se pide, ya despachada `shipped`.</summary>
    private static async Task<SalesOrderDto> OrderAsync(World x, decimal qty, decimal shipped, Teikem.Domain.Clients.Client? client = null, Product? product = null)
    {
        var d = await x.Sales.CreateAsync(new SalesOrderRequest(x.W.PublicId, (client ?? x.C).PublicId, Lines: new[] { new SalesOrderLineRequest((product ?? x.P).PublicId, qty) }), default);
        var o = await x.Sales.RequestAsync(d.PublicId, null, default);
        if (shipped > 0m) o = (await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, shipped) }), default)).Order;
        return o;
    }

    private static BillingRunRequest Period(Teikem.Domain.Clients.Client? client = null)
        => new(client?.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Generating_bills_what_was_dispatched_with_the_tax_copy_and_claims_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 10m, 4m);
        // la línea de la orden guarda su impuesto (copia): 10 piezas = 11.50 de IVU
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync(l => l.SalesOrderLineId == o.Lines[0].Id);
        (line.TaxAmount, line.TaxRatePct) = (11.50m, 11.5m);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var run = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(("GENERATED", 1), (run.Status, run.InvoiceCount));
        var inv = Assert.Single(run.Invoices);
        Assert.Equal(("DRAFT", "FAC-00001", 40m), (inv.Status, inv.Number, inv.Subtotal));
        Assert.Equal(4.60m, inv.TaxAmount);   // 11.50 × 4/10
        Assert.Equal(44.60m, run.TotalAmount);
        var detail = await x.Billing.GetInvoiceAsync(inv.PublicId, default);
        var l = Assert.Single(detail.Lines);
        Assert.Equal(("PRODUCT_SALE", 4m, 10m, o.Number), (l.ChargeType, l.Quantity, l.UnitAmount, l.SalesOrderNumber));
        Assert.Contains("MED", l.Description);
        Assert.Equal(4m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyInvoiced);
    }

    [Fact]
    public async Task Nothing_dispatched_or_already_billed_gives_the_nothing_to_bill_message_and_never_bills_twice()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));   // no hay nada despachado
        var o = await OrderAsync(x, 6m, 4m);
        await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));   // ya se facturó
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default);
        var second = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(20m, second.Invoices[0].Subtotal);   // solo las 2 nuevas
        Assert.Equal(6m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyInvoiced);
    }

    [Fact]
    public async Task The_last_part_of_a_partially_billed_line_takes_the_rest_of_the_tax_so_the_parts_add_up()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 3m, 1m);
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync();
        (line.TaxAmount, line.TaxRatePct) = (2.30m, 11.5m);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var taxes = new List<decimal> { (await x.Billing.GenerateAsync(Period(), default)).Invoices[0].TaxAmount };
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 1m) }), default);
        taxes.Add((await x.Billing.GenerateAsync(Period(), default)).Invoices[0].TaxAmount);
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 1m) }), default);
        taxes.Add((await x.Billing.GenerateAsync(Period(), default)).Invoices[0].TaxAmount);
        Assert.Equal(new[] { 0.77m, 0.77m, 0.76m }, taxes);
        Assert.Equal(2.30m, taxes.Sum());
    }

    [Fact]
    public async Task Review_then_approve_issues_the_invoices_with_dates_and_each_step_is_guarded()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, 4m, 4m);
        var run = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(BillingRules.ApproveNeedsReviewed, await MsgOf(() => x.Billing.ApproveAsync(run.Id, default)));
        var reviewed = await x.Billing.ReviewAsync(run.Id, default);
        Assert.Equal("REVIEWED", reviewed.Status);
        Assert.Equal(BillingRules.ReviewNeedsGenerated, await MsgOf(() => x.Billing.ReviewAsync(run.Id, default)));

        var approved = await x.Billing.ApproveAsync(run.Id, default);
        Assert.Equal("APPROVED", approved.Status);
        var inv = Assert.Single(approved.Invoices);
        var today = TenantClock.Default.Today;
        Assert.Equal(("ISSUED", today, today.AddDays(30)), (inv.Status, inv.IssueDate!.Value, inv.DueDate!.Value));
        Assert.Equal(BillingRules.CancelNeedsOpen, await MsgOf(() => x.Billing.CancelAsync(run.Id, null, default)));
        Assert.Equal(BillingRules.AdjustNeedsOpenRun, await MsgOf(() => x.Billing.AdjustLineAsync(inv.PublicId, (x.Billing.GetInvoiceAsync(inv.PublicId, default).Result).Lines[0].Id, new InvoiceLineAdjustRequest(9m, "x"), default)));
    }

    [Fact]
    public async Task Cancelling_an_open_run_voids_its_invoices_and_frees_what_it_had_claimed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, 4m, 4m);
        var run = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(4m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyInvoiced);
        var cancelled = await x.Billing.CancelAsync(run.Id, "Se generó por error", default);
        Assert.Equal(("CANCELLED", "VOID", 0m), (cancelled.Status, cancelled.Invoices[0].Status, cancelled.TotalAmount));
        Assert.Equal(0m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyInvoiced);
        var again = await x.Billing.GenerateAsync(Period(), default);   // vuelve a estar por facturar
        Assert.Equal(40m, again.Invoices[0].Subtotal);
    }

    [Fact]
    public async Task Adjusting_a_line_keeps_the_calculated_amount_recalculates_the_tax_and_the_totals_and_needs_a_reason()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await OrderAsync(x, 4m, 4m);
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync();
        (line.TaxAmount, line.TaxRatePct) = (4.60m, 11.5m);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var run = await x.Billing.GenerateAsync(Period(), default);
        var inv = run.Invoices[0];
        var lineId = (await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Lines[0].Id;

        Assert.Equal(BillingRules.AdjustNeedsPrice, await MsgOf(() => x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(null, "x"), default)));
        Assert.Equal(BillingRules.AdjustPriceInvalid, await MsgOf(() => x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(-1m, "x"), default)));
        Assert.Equal(BillingRules.AdjustNoteRequired, await MsgOf(() => x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(9m, "  "), default)));
        Assert.Equal(BillingRules.AdjustSameAmount, await MsgOf(() => x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(10m, "x"), default)));

        var adjusted = await x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(8m, "Descuento acordado"), default);
        var l = adjusted.Lines[0];
        Assert.Equal((8m, 10m, true, "Descuento acordado", 32m, 3.68m), (l.UnitAmount, l.OriginalUnitAmount, l.IsAdjusted, l.AdjustmentNote, l.LineSubtotal, l.TaxAmount));
        Assert.Equal((32m, 3.68m, 35.68m), (adjusted.Subtotal, adjusted.TaxAmount, adjusted.Total));
        Assert.Equal(35.68m, (await x.Billing.GetRunAsync(run.Id, default)).TotalAmount);
        // un segundo ajuste conserva el calculado ORIGINAL
        var again = await x.Billing.AdjustLineAsync(inv.PublicId, lineId, new InvoiceLineAdjustRequest(7m, "Otro"), default);
        Assert.Equal((7m, 10m), (again.Lines[0].UnitAmount, again.Lines[0].OriginalUnitAmount));
        Assert.Equal(o.Number, again.Lines[0].SalesOrderNumber);
    }

    [Fact]
    public async Task Voiding_an_issued_invoice_frees_it_but_not_with_payments()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, 4m, 4m);
        var run = await x.Billing.GenerateAsync(Period(), default);
        await x.Billing.ReviewAsync(run.Id, default);
        var inv = (await x.Billing.ApproveAsync(run.Id, default)).Invoices[0];

        x.F.Db.Set<Payment>().Add(new Payment { TenantId = WmsFixture.TenantId, InvoiceId = (await x.F.Db.Set<Invoice>().AsNoTracking().SingleAsync()).InvoiceId, PaymentMethodLookupId = x.F.LookupId(BillingCodes.PaymentMethodDomain, "CASH"), Amount = 10m, PaidAtUtc = DateTime.UtcNow });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(BillingRules.VoidHasPayments, await MsgOf(() => x.Billing.VoidAsync(inv.PublicId, null, default)));

        x.F.Db.Set<Payment>().RemoveRange(await x.F.Db.Set<Payment>().AsTracking().ToListAsync());
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var voided = await x.Billing.VoidAsync(inv.PublicId, "Cliente equivocado", default);
        Assert.Equal("VOID", voided.Status);
        Assert.Equal(0m, (await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync()).QtyInvoiced);
        Assert.Equal(BillingRules.VoidNeedsIssued, await MsgOf(() => x.Billing.VoidAsync(inv.PublicId, null, default)));
        Assert.Equal(0m, (await x.Billing.GetRunAsync(run.Id, default)).TotalAmount);
    }

    [Fact]
    public async Task One_invoice_per_client_and_a_client_filter_and_the_period_options()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("C2");
        await OrderAsync(x, 3m, 3m);
        await OrderAsync(x, 2m, 2m, other);
        var one = await x.Billing.GenerateAsync(Period(other), default);
        Assert.Equal(("Cliente C2", 1), (one.Invoices[0].ClientName, one.InvoiceCount));
        var rest = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(1, rest.InvoiceCount);
        Assert.Equal(30m, rest.Invoices[0].Subtotal);

        // un período anterior (y solo ese) no incluye lo despachado hoy
        await OrderAsync(x, 1m, 1m);
        var past = new BillingRunRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40)), OnlyInPeriod: true);
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(past, default)));
        Assert.Equal(BillingRules.PeriodInvalid, await MsgOf(() => x.Billing.GenerateAsync(new BillingRunRequest(null, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1)), default)));
        Assert.Equal(BillingRules.PeriodRequired, await MsgOf(() => x.Billing.GenerateAsync(new BillingRunRequest(), default)));
        Assert.Equal(2, (await x.Billing.ListRunsAsync(null, default)).Count);
        Assert.Equal(2, (await x.Billing.ListInvoicesAsync(null, null, null, null, 0, 50, default)).Total);
        Assert.Single((await x.Billing.ListInvoicesAsync(null, other.PublicId, null, null, 0, 50, default)).Items);
        Assert.Single((await x.Billing.ListInvoicesAsync("DRAFT", null, null, "FAC-00002", 0, 50, default)).Items);
    }

    [Fact]
    public async Task With_the_on_delivery_basis_only_pickups_and_delivered_dispatches_are_billed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Billing.UpdateSettingsAsync(new BillingSettingsRequest(SalesBillingTriggers.OnDelivery, null), default);
        var o = await OrderAsync(x, 6m, 4m);   // despacho 1: retiro (sin entrega)
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default);   // despacho 2
        // el despacho 2 lleva una entrega que todavía no se entrega
        var to = await x.F.AddOrderAsync(x.C.ClientId, "T-1");
        var batch2 = (await x.F.Db.Set<PickBatch>().AsTracking().OrderBy(b => b.PickBatchId).ToListAsync()).Last();
        batch2.TransportOrderId = to.TransportOrderId;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(new int?[] { null, to.TransportOrderId }, (await x.F.Db.Set<PickBatch>().AsNoTracking().OrderBy(b => b.PickBatchId).ToListAsync()).Select(b => b.TransportOrderId).ToArray());

        var first = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(40m, first.Invoices[0].Subtotal);   // solo las 4 del retiro
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));

        var tracked = await x.F.Db.TransportOrders.AsTracking().SingleAsync(t => t.TransportOrderId == to.TransportOrderId);
        tracked.StatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var second = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(20m, second.Invoices[0].Subtotal);   // ya entregado: las 2
    }

    [Fact]
    public async Task Settings_are_validated_and_invoices_are_isolated_by_company()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(new BillingSettingsDto("ON_DISPATCH", 30), await x.Billing.GetSettingsAsync(default));
        Assert.Equal(BillingRules.InvalidTrigger, await MsgOf(() => x.Billing.UpdateSettingsAsync(new BillingSettingsRequest("NUNCA", null), default)));
        Assert.Equal(BillingRules.DueDaysInvalid, await MsgOf(() => x.Billing.UpdateSettingsAsync(new BillingSettingsRequest(null, 400), default)));
        Assert.Equal(new BillingSettingsDto("ON_DELIVERY", 15), await x.Billing.UpdateSettingsAsync(new BillingSettingsRequest("on_delivery", 15), default));

        await OrderAsync(x, 2m, 2m);
        var run = await x.Billing.GenerateAsync(Period(), default);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => x.Billing.GetRunAsync(run.Id, default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Billing.GetInvoiceAsync(run.Invoices[0].PublicId, default));
            Assert.Empty(await x.Billing.ListRunsAsync(null, default));
        }
    }
}
