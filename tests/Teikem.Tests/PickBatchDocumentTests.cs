using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 173 (C1, 2026-10-21) — «Recolección y empaque» agrupada por documento: una fila por aviso de salida, orden de venta o envío empacado con su avance (pedido, reservado, sacado) y sus
/// recolecciones como movimientos; una recolección va a UN documento (aviso de salida &gt; orden de venta &gt; envío); un despacho manual no tiene documento; las eliminadas no cuentan.
/// </summary>
public sealed class PickBatchDocumentTests
{
    private sealed record World(WmsFixture F, Teikem.Domain.Clients.Client Owner, Product P, PickBatchDocumentService Svc);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s => s.AddSingleton<PickBatchDocumentService>());
        await f.AddWarehouseAsync("W1");
        var owner = await f.AddClientAsync("ASICS");
        var p = await f.AddProductAsync("MED", ownerClientId: owner.ClientId);
        return new World(f, owner, p, f.Get<PickBatchDocumentService>());
    }

    private static async Task<ExitNotice> NoticeAsync(World x, string number, string status, decimal ordered, decimal reserved, decimal shipped)
    {
        var w = x.F.Db.Warehouses.First();
        var n = new ExitNotice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = number, WarehouseId = w.WarehouseId, ClientId = x.Owner.ClientId,
            RequestedDate = new DateOnly(2026, 10, 20), StatusCodeId = x.F.StatusId(ExitNoticeCodes.StatusDomain, status), CreatedAtUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
        };
        n.Lines.Add(new ExitNoticeLine { TenantId = WmsFixture.TenantId, LineNo = 1, ProductId = x.P.ProductId, QtyOrdered = ordered, QtyReserved = reserved, QtyShipped = shipped, QtyBackordered = ordered - reserved - shipped });
        x.F.Db.Set<ExitNotice>().Add(n);
        await x.F.Db.SaveChangesAsync();
        return n;
    }

    private static async Task<PickBatch> BatchAsync(World x, string number, DateTime at, decimal qty, int? exitNoticeId = null, int? salesOrderId = null, int? orderId = null, bool active = true,
        string status = PickBatchStatuses.Collected)
    {
        var w = x.F.Db.Warehouses.First();
        var b = new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = number, StatusCodeId = x.F.StatusId(StatusDomains.PickBatchStatus, status),
            CollectedAtUtc = at, ExitNoticeId = exitNoticeId, SalesOrderId = salesOrderId, TransportOrderId = orderId, IsActive = active,
        };
        b.Lines.Add(new PickBatchLine { ProductId = x.P.ProductId, FromBinId = 1, Quantity = qty, IssueTxnId = 1 });
        x.F.Db.Set<PickBatch>().Add(b);
        await x.F.Db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task An_exit_notice_is_one_row_with_its_progress_and_its_movements_newest_first()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await NoticeAsync(x, "SAL-00001", ExitNoticeStatuses.Partial, ordered: 10m, reserved: 4m, shipped: 6m);
        await BatchAsync(x, "EMP-00001", new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc), 2m, exitNoticeId: n.ExitNoticeId);
        await BatchAsync(x, "EMP-00002", new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), 4m, exitNoticeId: n.ExitNoticeId);

        var page = await x.Svc.ListAsync(new PickDocumentQuery(), default);
        var doc = Assert.Single(page.Items);
        Assert.Equal((PickBatchDocumentService.KindExitNotice, "SAL-00001", "Cliente ASICS", ExitNoticeStatuses.Partial), (doc.Kind, doc.Number, doc.ClientName, doc.StatusCode));
        Assert.Equal((10m, 4m, 6m, 2), (doc.Ordered, doc.Reserved, doc.Shipped, doc.BatchCount));
        Assert.Equal(new[] { "EMP-00002", "EMP-00001" }, doc.Batches.Select(b => b.Number));
        Assert.Equal(new[] { 4m, 2m }, doc.Batches.Select(b => b.TotalQty));
        Assert.Equal(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), doc.LastCollectedAtUtc);
    }

    [Fact]
    public async Task An_open_notice_with_something_reserved_shows_even_before_its_first_movement_and_a_cancelled_empty_one_does_not()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await NoticeAsync(x, "SAL-00001", ExitNoticeStatuses.Requested, 5m, 5m, 0m);
        await NoticeAsync(x, "SAL-00002", ExitNoticeStatuses.Cancelled, 5m, 0m, 0m);
        await NoticeAsync(x, "SAL-00003", ExitNoticeStatuses.Requested, 5m, 0m, 0m);   // nada reservado todavía: nada que sacar

        var page = await x.Svc.ListAsync(new PickDocumentQuery(), default);
        var doc = Assert.Single(page.Items);
        Assert.Equal(("SAL-00001", 0, (DateTime?)null), (doc.Number, doc.BatchCount, doc.LastCollectedAtUtc));
        Assert.Empty(doc.Batches);
    }

    [Fact]
    public async Task A_sales_order_and_a_packed_shipment_are_their_own_rows_and_a_manual_issue_has_no_document()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var so = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = "OV-00001", WarehouseId = x.F.Db.Warehouses.First().WarehouseId, ClientId = x.Owner.ClientId,
            RequestedDate = new DateOnly(2026, 10, 20), StatusCodeId = x.F.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Partial), CreatedAtUtc = DateTime.UtcNow,
        };
        so.Lines.Add(new SalesOrderLine { TenantId = WmsFixture.TenantId, LineNo = 1, ProductId = x.P.ProductId, QtyOrdered = 8m, QtyReserved = 3m, QtyShipped = 5m });
        x.F.Db.Set<SalesOrder>().Add(so);
        var order = new TransportOrder { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.Owner.ClientId, OrderNumber = "ORD-9", StatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Submitted), CreatedAtUtc = DateTime.UtcNow };
        x.F.Db.Set<TransportOrder>().Add(order);
        await x.F.Db.SaveChangesAsync();
        await BatchAsync(x, "EMP-00001", DateTime.UtcNow.AddMinutes(-5), 5m, salesOrderId: so.SalesOrderId);
        await BatchAsync(x, "EMP-00002", DateTime.UtcNow.AddMinutes(-3), 7m, orderId: order.TransportOrderId, status: PickBatchStatuses.Packed);
        await BatchAsync(x, "DMA-00001", DateTime.UtcNow.AddMinutes(-1), 1m);   // despacho manual: sin documento

        var page = await x.Svc.ListAsync(new PickDocumentQuery(), default);
        Assert.Equal(new[] { "ORD-9", "OV-00001" }, page.Items.Select(d => d.Number));   // la actividad más reciente primero
        var env = page.Items[0];
        Assert.Equal((PickBatchDocumentService.KindOrder, (decimal?)null, (decimal?)null, 7m), (env.Kind, env.Ordered, env.Reserved, env.Shipped));
        var sales = page.Items[1];
        Assert.Equal((8m, 3m, 5m, 1), (sales.Ordered, sales.Reserved, sales.Shipped, sales.BatchCount));
        Assert.DoesNotContain(page.Items.SelectMany(d => d.Batches), b => b.Number == "DMA-00001");
    }

    [Fact]
    public async Task A_batch_of_a_notice_that_also_has_a_sales_order_counts_only_for_the_notice_and_deleted_batches_do_not_count()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var n = await NoticeAsync(x, "SAL-00001", ExitNoticeStatuses.Partial, 10m, 2m, 8m);
        await BatchAsync(x, "EMP-00001", DateTime.UtcNow.AddMinutes(-2), 8m, exitNoticeId: n.ExitNoticeId, salesOrderId: 77);
        await BatchAsync(x, "EMP-00002", DateTime.UtcNow.AddMinutes(-1), 3m, exitNoticeId: n.ExitNoticeId, active: false, status: PickBatchStatuses.Cancelled);
        var doc = Assert.Single((await x.Svc.ListAsync(new PickDocumentQuery(), default)).Items);
        Assert.Equal(1, doc.BatchCount);
        Assert.Equal("EMP-00001", Assert.Single(doc.Batches).Number);
    }

    [Fact]
    public async Task Filters_by_kind_search_status_and_pending_and_pages()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await NoticeAsync(x, "SAL-00001", ExitNoticeStatuses.Requested, 5m, 5m, 0m);
        var b = await NoticeAsync(x, "SAL-00002", ExitNoticeStatuses.Shipped, 5m, 0m, 5m);
        await BatchAsync(x, "EMP-00001", DateTime.UtcNow.AddMinutes(-2), 5m, exitNoticeId: b.ExitNoticeId);

        Assert.Equal(2, (await x.Svc.ListAsync(new PickDocumentQuery(), default)).Total);
        Assert.Equal(new[] { "SAL-00001" }, (await x.Svc.ListAsync(new PickDocumentQuery(PendingOnly: true), default)).Items.Select(d => d.Number));
        Assert.Equal(new[] { "SAL-00002" }, (await x.Svc.ListAsync(new PickDocumentQuery(Status: new[] { "shipped" }), default)).Items.Select(d => d.Number));
        Assert.Equal(new[] { "SAL-00002" }, (await x.Svc.ListAsync(new PickDocumentQuery(Search: "00002"), default)).Items.Select(d => d.Number));
        Assert.Equal(new[] { "SAL-00001" }, (await x.Svc.ListAsync(new PickDocumentQuery(Search: "ASICS", PendingOnly: true), default)).Items.Select(d => d.Number));
        Assert.Empty((await x.Svc.ListAsync(new PickDocumentQuery(Kind: PickBatchDocumentService.KindSalesOrder), default)).Items);
        var first = await x.Svc.ListAsync(new PickDocumentQuery(Skip: 0, Take: 1), default);
        Assert.Equal((2, 1), (first.Total, first.Items.Count));
    }

    [Fact]
    public async Task An_unknown_kind_is_a_400_with_its_message()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.ListAsync(new PickDocumentQuery(Kind: "OTRO"), default));
        Assert.Contains(PickBatchDocumentService.UnknownKind("OTRO"), ex.Errors.SelectMany(e => e.Value));
    }
}
