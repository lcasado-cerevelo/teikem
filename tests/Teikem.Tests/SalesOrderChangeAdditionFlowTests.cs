using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P5): la orden interna de adición desde el servicio. El registro ADDITION se guarda en la raíz con AdditionSalesOrderId; la hija nace solicitada (el
// RequestAsync de siempre: «Orden solicitada») y cuelga de la raíz aunque se pida desde otra adición; aviso «Adición a una orden de venta»; madre en borrador → 422;
// faltante → 409.
public sealed class SalesOrderChangeAdditionFlowTests
{
    [Fact]
    public async Task The_addition_record_carries_the_child_and_the_notices_go_out()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var dto = await svc.CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P2, 2m)) with { Notes = "Faltó en la entrega" }, default);

        Assert.Equal((SalesChangeKinds.Addition, SalesOrderChangeStatuses.Applied, 1, o.PublicId), (dto.Item.Kind, dto.Item.Status, dto.Item.ChangeNo, dto.Item.OrderPublicId));
        Assert.Equal((0m, 16m, 16m, 1), (dto.Item.TotalBefore, dto.Item.TotalAfter, dto.Item.TotalDelta, dto.LinesAdded));
        var row = await ChangeKit.RowAsync(x.F, dto.Item.PublicId);
        Assert.NotNull(row.AdditionSalesOrderId);
        var child = await x.Svc.GetAsync(dto.Item.AdditionPublicId!.Value, default);
        Assert.Equal((dto.Item.AdditionNumber, SalesOrderStatuses.Requested, o.PublicId, 2m), (child.Number, child.Status, child.ParentPublicId!.Value, child.Lines[0].QtyReserved));
        Assert.Equal($"Adición de {o.Number} — Faltó en la entrega", child.Notes);
        Assert.Contains(dto.Item.AdditionNumber!, dto.Item.Summary);
        var parent = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal(child.PublicId, Assert.Single(parent.Additions!).PublicId);

        var notice = Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.AdditionCreated);
        Assert.Equal(($"ADD-{dto.Item.PublicId:N}", child.Number, o.Number), (notice.Key, notice.Values["additionNumber"], notice.Values["orderNumber"]));
        Assert.Contains(x.Publisher.Published, p => p.Event == SalesEventCodes.OrderRequested && p.Values["orderNumber"] == child.Number);
        Assert.Equal(new[] { SalesOrderChangeStatuses.PendingApproval, SalesOrderChangeStatuses.Applied }, await ChangeKit.HistoryAsync(x.F, dto.Item.PublicId));
    }

    [Fact]
    public async Task An_addition_of_an_addition_hangs_from_the_root_and_its_record_goes_on_the_root()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);
        var first = await svc.CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P2, 1m)), default);

        var second = await svc.CreateAdditionAsync(first.Item.AdditionPublicId!.Value, ChangeKit.Addition((x.P3, 1m)), default);

        Assert.Equal((o.PublicId, 2), (second.Item.OrderPublicId, second.Item.ChangeNo));
        var grandChild = await x.Svc.GetAsync(second.Item.AdditionPublicId!.Value, default);
        Assert.Equal(o.PublicId, grandChild.ParentPublicId);
        Assert.Equal(2, (await x.Svc.GetAsync(o.PublicId, default)).Additions!.Count);
    }

    [Fact]
    public async Task A_draft_parent_is_a_422_and_a_shortage_is_a_409()
    {
        await using var x = await ChangeKit.WorldAsync();
        var draft = await x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P1.PublicId, 1m) },
            DeliveryMode: SalesDeliveryModes.Delivery), default);
        var svc = ChangeKit.Service(x);
        var e = await Assert.ThrowsAsync<StatusRuleException>(() => svc.CreateAdditionAsync(draft.PublicId, ChangeKit.Addition((x.P2, 1m)), default));
        Assert.Equal(SalesOrderService.AdditionParentNotOpen, e.Message);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());

        var o = await x.RequestedAsync((x.P1, 3m));
        var shortage = await Assert.ThrowsAsync<InsufficientStockException>(() => svc.CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P2, 500m)), default));
        Assert.Equal(409, shortage.StatusCode);
        // (con SQL Server la transacción se revierte entera: ni registro ni hija; InMemory no tiene transacciones)
    }
}
