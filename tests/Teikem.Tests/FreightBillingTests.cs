using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 74 (P5): flete de las órdenes de transporte en la facturación — cuándo se factura según el modelo de cobro del contrato, el desglose por concepto con su fila de tarifa, nunca dos veces
// (bloqueo de la orden), liberar al cancelar la corrida o anular la factura, y facturar producto y flete juntos.
public sealed class FreightBillingTests
{
    [Fact]
    public void Rules_billable_statuses_charge_types_and_quote_match()
    {
        Assert.Equal(FreightRules.PickedUpOrLater, FreightRules.BillableStatuses("BY_PICKUP"));
        Assert.Equal(FreightRules.PickedUpOrLater, FreightRules.BillableStatuses(" by_pickup "));
        Assert.Equal(FreightRules.DeliveredOnly, FreightRules.BillableStatuses("BY_DELIVERY"));
        Assert.Equal(FreightRules.DeliveredOnly, FreightRules.BillableStatuses("MIXED"));
        Assert.Equal(FreightRules.DeliveredOnly, FreightRules.BillableStatuses(null));
        Assert.DoesNotContain(OrderStatuses.Failed, FreightRules.PickedUpOrLater);
        Assert.DoesNotContain(OrderStatuses.Failed, FreightRules.PickedUpOrLater);
        Assert.Equal(new[] { "FREIGHT", "FREIGHT", "HANDLING", "SURCHARGE", "FREIGHT", "FREIGHT" },
            new[] { FreightComponents.BaseFreight, FreightComponents.ExtraPieces, FreightComponents.DispatchFee, FreightComponents.CodFee, FreightComponents.SpecialService, FreightComponents.Quoted }.Select(FreightRules.ChargeTypeOf));
        Assert.True(FreightRules.MatchesQuote(11.50005m, 11.5001m));   // se compara a 4 decimales
        Assert.False(FreightRules.MatchesQuote(11.5m, 11.51m));
        Assert.Equal("T-1 · Flete NEXT_DAY/BOX", FreightRules.Describe("T-1", FreightComponents.BaseFreight, "NEXT_DAY", "BOX"));
    }

    [Fact]
    public void Breakdown_adds_up_to_the_quote_and_keeps_the_rate_row_of_each_concept()
    {
        var rows = new[]
        {
            new RateRow(11, 5, RateKinds.PerService, "NEXT_DAY", "BOX", 6.5m, Array.Empty<TierRow>()),
            new RateRow(12, 5, RateKinds.ExtraPiece, "NEXT_DAY", "BOX", 0m, new[] { new TierRow(1, 2, 5, 1m), new TierRow(2, 6, null, 0.75m) }),
        };
        var flags = new ContractFlags(true, true, true, true, 2m, "FIXED", 1.5m);
        var lines = new[] { new QuoteLine("NEXT_DAY", "BOX", 7) };
        var pieces = ContractRateResolver.Breakdown(rows, flags, lines, 100m);
        Assert.Equal(new[] { FreightComponents.BaseFreight, FreightComponents.ExtraPieces, FreightComponents.DispatchFee, FreightComponents.CodFee }, pieces.Select(p => p.Component).ToArray());
        Assert.Equal(new int?[] { 11, 12, null, null }, pieces.Select(p => p.RateComponentId).ToArray());
        Assert.Equal(new[] { 6.5m, 5.5m, 2m, 1.5m }, pieces.Select(p => p.Amount).ToArray());   // piezas 2–5 a 1.00 (4) + 6–7 a 0.75 (1.5) = 5.5
        Assert.Equal(ContractRateResolver.QuoteOrder(rows, flags, lines, 100m).Total, pieces.Sum(p => p.Amount));
        Assert.Empty(ContractRateResolver.Breakdown(rows, flags, Array.Empty<QuoteLine>(), 0m));   // sin líneas no hay despacho ni COD
    }

    private sealed class World(WmsFixture f, Client c, BillingService billing)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public BillingService Billing { get; } = billing;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<BillingService>();
        });
        f.SetModules(ModuleKeys.Billing);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<BillingService>());
    }

    private static async Task<int> ContractAsync(World x, string? model)
    {
        var c = new Contract
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C.ClientId, ContractNumber = "K-1", Title = "Marco", StartDate = new DateOnly(2026, 1, 1),
            StatusCodeId = 1, BillingModelLookupId = model is null ? null : x.F.LookupId(LookupDomains.BillingModel, model), IsActive = true,
        };
        x.F.Db.Set<Contract>().Add(c);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return c.ContractId;
    }

    /// <summary>Una orden de transporte ya cotizada (monto congelado) en el estatus dado.</summary>
    private static async Task<Teikem.Domain.Orders.TransportOrder> QuotedOrderAsync(World x, string number, string status, decimal quoted, int? contractId = null, bool active = true)
    {
        var o = await x.F.AddOrderAsync(x.C.ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.QuotedAmount, t.QuotedAtUtc, t.ContractId, t.IsActive) = (quoted, DateTime.UtcNow.AddDays(-1), contractId, active);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static BillingRunRequest Period(bool? products = null, bool? freight = null)
        => new(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), IncludeProducts: products, IncludeFreight: freight);

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    private static async Task<bool> InvoicedAsync(World x, int orderId) => (await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == orderId)).FreightInvoiced;

    [Fact]
    public async Task A_delivered_order_is_billed_once_with_its_quoted_amount_and_the_claim_is_released_by_cancel_and_void()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 25m);

        var run = await x.Billing.GenerateAsync(Period(), default);
        var inv = Assert.Single(run.Invoices);
        Assert.Equal(("DRAFT", 25m), (inv.Status, inv.Subtotal));
        var detail = await x.Billing.GetInvoiceAsync(inv.PublicId, default);
        var line = Assert.Single(detail.Lines);
        Assert.Equal(("FREIGHT", "T-1", 25m, 1m), (line.ChargeType, line.TransportOrderNumber, line.UnitAmount, line.Quantity));
        Assert.Contains("T-1", line.Description);
        Assert.True(await InvoicedAsync(x, o.TransportOrderId));
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));   // nunca dos veces

        // cancelar la corrida libera el flete y se puede facturar otra vez
        await x.Billing.CancelAsync(run.Id, null, default);
        Assert.False(await InvoicedAsync(x, o.TransportOrderId));
        var again = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(25m, again.Invoices[0].Subtotal);

        // aprobada y emitida, anular la factura también lo libera
        await x.Billing.ReviewAsync(again.Id, default);
        var issued = (await x.Billing.ApproveAsync(again.Id, default)).Invoices[0];
        Assert.True(await InvoicedAsync(x, o.TransportOrderId));
        await x.Billing.VoidAsync(issued.PublicId, "Error", default);
        Assert.False(await InvoicedAsync(x, o.TransportOrderId));
    }

    [Fact]
    public async Task The_contract_billing_model_decides_when_the_freight_is_billable()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var byPickup = await ContractAsync(x, "BY_PICKUP");
        var picked = await QuotedOrderAsync(x, "T-PICK", OrderStatuses.Pickup, 10m, byPickup);
        await QuotedOrderAsync(x, "T-NOCONTRACT", OrderStatuses.Pickup, 7m);   // sin contrato: solo al entregar
        await QuotedOrderAsync(x, "T-FAILED", OrderStatuses.Failed, 7m, byPickup);   // fallida: nunca

        var run = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(10m, Assert.Single(run.Invoices).Subtotal);
        Assert.True(await InvoicedAsync(x, picked.TransportOrderId));
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));
    }

    [Fact]
    public async Task Lote114_a_pick_up_order_is_billed_when_it_reaches_the_warehouse_whatever_the_contract_model_and_a_zero_one_is_not()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var byDelivery = await ContractAsync(x, "BY_DELIVERY");
        var inWarehouse = await QuotedOrderAsync(x, "T-PICKUP", OrderStatuses.InWarehouse, 15m, byDelivery);   // el pick up no se entrega nunca: se cobra En almacén
        await QuotedOrderAsync(x, "T-FREE", OrderStatuses.InWarehouse, 0m, byDelivery);                        // pick up no facturable (cotizado en cero)
        await QuotedOrderAsync(x, "T-ROUTE", OrderStatuses.Assigned, 9m, byDelivery);                          // por entrega: todavía no

        var run = await x.Billing.GenerateAsync(Period(), default);
        Assert.Equal(15m, Assert.Single(run.Invoices).Subtotal);
        Assert.True(await InvoicedAsync(x, inWarehouse.TransportOrderId));
    }

    [Fact]
    public async Task Lote115_the_return_fee_is_billed_as_its_own_line_apart_from_the_freight_and_is_released_when_the_run_is_cancelled()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var returned = await QuotedOrderAsync(x, "T-DEV", OrderStatuses.Returned, 0m);
        var tracked = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == returned.TransportOrderId);
        tracked.ReturnFeeAmount = 20m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var run = await x.Billing.GenerateAsync(Period(), default);
        var inv = Assert.Single(run.Invoices);
        Assert.Equal(20m, inv.Subtotal);
        var line = Assert.Single((await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Lines);
        Assert.Contains("Cargo por devolución", line.Description);
        var order = await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == returned.TransportOrderId);
        Assert.True(order.ReturnFeeInvoiced);
        Assert.False(order.FreightInvoiced);                                                    // el flete no se tocó
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));   // nunca dos veces

        await x.Billing.CancelAsync(run.Id, null, default);
        Assert.False((await x.F.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == returned.TransportOrderId)).ReturnFeeInvoiced);
        Assert.Equal(20m, (await x.Billing.GenerateAsync(Period(), default)).Invoices[0].Subtotal);
    }

    [Fact]
    public async Task Drafts_inactive_unquoted_and_zero_orders_are_not_billed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await QuotedOrderAsync(x, "T-DRAFT", OrderStatuses.Submitted, 10m);
        await QuotedOrderAsync(x, "T-INACTIVE", OrderStatuses.Delivered, 10m, active: false);
        await QuotedOrderAsync(x, "T-ZERO", OrderStatuses.Delivered, 0m);
        await x.F.AddOrderAsync(x.C.ClientId, "T-UNQUOTED", OrderStatuses.Delivered);   // sin cotizar
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(), default)));
    }

    [Fact]
    public async Task Product_and_freight_can_be_billed_together_or_separately()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 12m);
        await QuotedOrderAsync(x, "T-2", OrderStatuses.Delivered, 8m);

        Assert.Equal(FreightRules.NothingSelected, await MsgOf(() => x.Billing.GenerateAsync(Period(false, false), default)));
        Assert.Equal(BillingRules.NothingToBill, await MsgOf(() => x.Billing.GenerateAsync(Period(true, false), default)));   // sin ventas de producto no hay nada que facturar
        var run = await x.Billing.GenerateAsync(Period(false, true), default);
        var inv = Assert.Single(run.Invoices);
        Assert.Equal(20m, inv.Subtotal);   // dos órdenes del mismo cliente en una factura
        Assert.Equal(2, (await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Lines.Count);
    }

    [Fact]
    public async Task Adjusting_a_freight_line_keeps_the_calculated_amount()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await QuotedOrderAsync(x, "T-1", OrderStatuses.Delivered, 25m);
        var run = await x.Billing.GenerateAsync(Period(), default);
        var detail = await x.Billing.GetInvoiceAsync(run.Invoices[0].PublicId, default);
        var adjusted = await x.Billing.AdjustLineAsync(detail.PublicId, detail.Lines[0].Id, new InvoiceLineAdjustRequest(20m, "Descuento pactado"), default);
        Assert.Equal((20m, 25m, true), (adjusted.Subtotal, adjusted.Lines[0].OriginalUnitAmount, adjusted.Lines[0].IsAdjusted));
    }
}
