using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P3): guarda GLOBAL de la reversa de una recolección de venta. Con ON_DISPATCH / ON_DELIVERY no se deshace lo que dejaría lo despachado por debajo de lo
// facturado más lo devuelto (409 con las facturas: la factura emitida no se reabre); con ON_REQUEST lo facturado no cuenta (la reversa es libre). Sin factura, como antes.
// (En InMemory no hay transacción: lo que la reversa ya movió en el Kárdex antes del 409 no se revierte aquí; en SQL Server se revierte todo con la transacción.)
public sealed class SalesReversalInvoicedGuardTests
{
    private static async Task InvoiceAsync(SalesChangeWorld x, int lineId, decimal invoiced, decimal returned = 0m, string? number = "FAC-00012")
    {
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync(l => l.SalesOrderLineId == lineId);
        (line.QtyInvoiced, line.QtyReturned) = (invoiced, returned);
        if (number is not null)
        {
            var invoice = new Invoice
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C.ClientId, InvoiceNumber = number,
                StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Issued), CurrencyLookupId = 1, Subtotal = 10m, IsActive = true,
            };
            x.F.Db.Set<Invoice>().Add(invoice);
            await x.F.Db.SaveChangesAsync();
            x.F.Db.Set<InvoiceLine>().Add(new InvoiceLine { InvoiceId = invoice.InvoiceId, SalesOrderLineId = lineId, Description = "x", Quantity = invoiced, UnitAmount = 10m, ChargeTypeLookupId = 1 });
        }
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static async Task SetTriggerAsync(SalesChangeWorld x, string trigger)
    {
        var t = await x.F.Db.Tenants.AsTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        t.SalesBillingTrigger = trigger;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Undoing_an_invoiced_pick_is_a_409_with_the_invoice_and_the_line_keeps_what_was_shipped()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 4m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        await InvoiceAsync(x, o.Lines[0].Id, 4m);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.Batches.DeleteAsync(d.PickBatchPublicId, new PickBatchDeleteRequest("Error"), default));
        Assert.Equal(SalesChangeRules.BatchInvoiced(d.PickBatchNumber, "FAC-00012"), ex.Message);
        Assert.Equal($"La recolección {d.PickBatchNumber} ya está facturada (FAC-00012): no se puede deshacer.", ex.Message);
        x.F.Db.ChangeTracker.Clear();
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((4m, 0m, "SHIPPED"), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved, after.Status));
        Assert.Equal("COLLECTED", Assert.Single(after.Dispatches!).Status);
    }

    [Fact]
    public async Task What_was_returned_also_counts_and_without_an_invoice_the_message_says_so()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 4m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        await InvoiceAsync(x, o.Lines[0].Id, 0m, returned: 1m, number: null);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => x.Batches.DeleteAsync(d.PickBatchPublicId, null, default));
        Assert.Equal(SalesChangeRules.BatchInvoiced(d.PickBatchNumber, "—"), ex.Message);
    }

    [Fact]
    public async Task Undoing_a_pick_that_leaves_enough_shipped_to_cover_the_invoice_is_allowed()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 6m));
        await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        var second = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 2m);
        await InvoiceAsync(x, o.Lines[0].Id, 4m);   // lo facturado lo cubre la primera recolección

        await x.Batches.DeleteAsync(second.PickBatchPublicId, null, default);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((4m, 2m, "PARTIAL"), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved, after.Status));
    }

    [Fact]
    public async Task With_billing_on_request_the_reversal_is_free_and_the_line_goes_back_to_reserved()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        await SetTriggerAsync(x, SalesBillingTriggers.OnRequest);
        var o = await x.RequestedAsync((x.P1, 4m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        await InvoiceAsync(x, o.Lines[0].Id, 4m);

        await x.Batches.DeleteAsync(d.PickBatchPublicId, null, default);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((0m, 4m, "REQUESTED"), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved, after.Status));
        Assert.Equal((20m, 4m), await x.BalanceOfAsync(x.P1));   // el inventario volvió a su posición y quedó reservado
    }

    [Fact]
    public async Task Without_an_invoice_the_pick_is_undone_as_before()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var o = await x.RequestedAsync((x.P1, 4m));
        var d = await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 4m);
        await x.Batches.DeleteAsync(d.PickBatchPublicId, null, default);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((0m, 4m, "REQUESTED"), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved, after.Status));
        var batch = await x.F.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PublicId == d.PickBatchPublicId);
        Assert.False(batch.IsActive);
    }
}
