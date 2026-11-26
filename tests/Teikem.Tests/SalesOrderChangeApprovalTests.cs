using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P5): la segunda aprobación. Con ALWAYS nada se aplica hasta que otra persona (sales.modify.approve) aprueba; una sola pendiente por orden; la huella
// detecta lo que pasó entre el registro y la aprobación; rechazar exige nota; retirar; cancelar la orden la retira; la bandeja pagina y ordena en el servidor; la
// adición con aprobación crea la hija al aprobar.
public sealed class SalesOrderChangeApprovalTests
{
    [Fact]
    public async Task With_always_nothing_is_applied_until_another_person_approves()
    {
        await using var x = await ChangeKit.WorldAsync();
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.Always);
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var pending = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)), default);

        Assert.Equal((SalesOrderChangeStatuses.PendingApproval, true, true, SalesChangeApprovalModes.Always),
            (pending.Item.Status, pending.Item.IsPending, pending.Item.RequiresApproval, pending.Item.ApprovalRule));
        Assert.Equal((30m, 50m), (pending.Item.TotalBefore, pending.Item.TotalAfter));   // proyectado
        var untouched = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((3m, 3m, pending.Item.PublicId, 1), (untouched.Lines[0].QtyOrdered, untouched.Lines[0].QtyReserved, untouched.PendingChangePublicId, untouched.PendingChangeNo));
        Assert.Equal((20m, 3m), await x.BalanceOfAsync(x.P1));
        var notice = Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.ApprovalPending);
        Assert.Equal($"APPROVAL-{pending.Item.PublicId:N}", notice.Key);
        Assert.DoesNotContain(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.OrderChanged);

        // una segunda modificación mientras hay una pendiente → 409
        var second = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default));
        Assert.Equal(SalesOrderChangeService.PendingExists(1), second.Message);

        // quien la registró no la aprueba
        var self = await Assert.ThrowsAsync<ForbiddenException>(() => svc.ApproveAsync(o.PublicId, pending.Item.PublicId, null, default));
        Assert.Equal(SalesOrderChangeService.SelfApproval, self.Message);

        // otra persona con sales.modify.approve
        x.F.Tenant.UserId = 2;
        var approved = await svc.ApproveAsync(o.PublicId, pending.Item.PublicId, new SalesOrderChangeDecisionRequest("Visto bueno"), default);
        Assert.Equal((SalesOrderChangeStatuses.Applied, false, 2, "Visto bueno"), (approved.Item.Status, approved.Item.IsPending, approved.DecidedBy, approved.DecisionNote));
        Assert.NotNull(approved.AppliedAtUtc);
        Assert.Equal(new[] { SalesOrderChangeStatuses.PendingApproval, SalesOrderChangeStatuses.Applied }, await ChangeKit.HistoryAsync(x.F, pending.Item.PublicId));
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal((5m, 5m, (Guid?)null), (after.Lines[0].QtyOrdered, after.Lines[0].QtyReserved, after.PendingChangePublicId));
        Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.OrderChanged && p.Key == $"CHG-{pending.Item.PublicId:N}");
        await x.AssertCoherentAsync(o.PublicId);

        // ya no está pendiente
        var again = await Assert.ThrowsAsync<ConflictException>(() => svc.ApproveAsync(o.PublicId, pending.Item.PublicId, null, default));
        Assert.Equal(SalesOrderChangeService.NotPending, again.Message);
    }

    [Fact]
    public async Task A_dispatch_between_registering_and_approving_is_a_409_by_the_fingerprint()
    {
        await using var x = await ChangeKit.WorldAsync();
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.Always);
        var o = await x.RequestedAsync((x.P1, 5m));
        var svc = ChangeKit.Service(x);
        var pending = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 6m)), default);

        await x.DispatchAsync(o.PublicId, o.Lines[0].Id, 2m);   // el almacén no se bloquea mientras está pendiente
        x.F.Tenant.UserId = 2;
        var e = await Assert.ThrowsAsync<ConflictException>(() => svc.ApproveAsync(o.PublicId, pending.Item.PublicId, null, default));

        Assert.Equal(SalesOrderChangeService.FingerprintChanged, e.Message);
        Assert.True((await ChangeKit.RowAsync(x.F, pending.Item.PublicId)).IsPending);
        Assert.Equal(5m, (await x.Svc.GetAsync(o.PublicId, default)).Lines[0].QtyOrdered);
    }

    [Fact]
    public async Task Rejecting_needs_a_note_withdrawing_does_not_and_cancelling_the_order_withdraws_the_pending_one()
    {
        await using var x = await ChangeKit.WorldAsync();
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.Always);
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var first = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)), default);
        x.F.Tenant.UserId = 2;
        var noNote = await Assert.ThrowsAsync<ValidationException>(() => svc.RejectAsync(o.PublicId, first.Item.PublicId, new SalesOrderChangeDecisionRequest("  "), default));
        Assert.Equal(SalesOrderChangeService.RejectNoteRequired, noNote.Message);
        var rejected = await svc.RejectAsync(o.PublicId, first.Item.PublicId, new SalesOrderChangeDecisionRequest("El cliente no confirmó"), default);
        Assert.Equal((SalesOrderChangeStatuses.Rejected, false, "El cliente no confirmó"), (rejected.Item.Status, rejected.Item.IsPending, rejected.DecisionNote));

        x.F.Tenant.UserId = 1;
        var second = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default);
        Assert.Equal(2, second.Item.ChangeNo);
        var withdrawn = await svc.WithdrawAsync(o.PublicId, second.Item.PublicId, null, default);
        Assert.Equal(SalesOrderChangeStatuses.Withdrawn, withdrawn.Item.Status);

        var third = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 2m)), default);
        await x.Svc.CancelAsync(o.PublicId, "El cliente canceló", default);
        var row = await ChangeKit.RowAsync(x.F, third.Item.PublicId);
        Assert.False(row.IsPending);
        Assert.Equal(new[] { SalesOrderChangeStatuses.PendingApproval, SalesOrderChangeStatuses.Withdrawn }, await ChangeKit.HistoryAsync(x.F, third.Item.PublicId));

        var history = await svc.ListAsync(o.PublicId, default);
        Assert.Equal(new[] { 3, 2, 1 }, history.Select(h => h.ChangeNo));
        Assert.Equal(new[] { SalesOrderChangeStatuses.Withdrawn, SalesOrderChangeStatuses.Withdrawn, SalesOrderChangeStatuses.Rejected }, history.Select(h => h.Status));
        Assert.Equal(3m, (await x.Svc.GetAsync(o.PublicId, default)).Lines[0].QtyOrdered);
    }

    [Fact]
    public async Task The_pending_tray_pages_searches_and_sorts_on_the_server()
    {
        await using var x = await ChangeKit.WorldAsync(stock: 100m);
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.AboveAmount, 15m);
        var svc = ChangeKit.Service(x);
        var orders = new List<SalesOrderDto>();
        for (var i = 0; i < 3; i++)
        {
            var o = await x.RequestedAsync((x.P1, 1m));
            orders.Add(o);
            await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 3m + i)), default);   // +20, +30, +40: todas pasan de 15
        }
        // ABOVE_AMOUNT con |Δ| = 10 (no pasa de 15): se aplica sin aprobación
        var small = await x.RequestedAsync((x.P1, 1m));
        var applied = await svc.CreateAsync(small.PublicId, ChangeKit.Req(ChangeKit.Qty(small.Lines[0].Id, 2m)), default);
        Assert.Equal(SalesOrderChangeStatuses.Applied, applied.Item.Status);

        var page = await svc.PendingApprovalsAsync(new SalesChangePendingQuery(0, 2), default);
        Assert.Equal((3, 2), (page.Total, page.Items.Count));
        var all = await svc.PendingApprovalsAsync(new SalesChangePendingQuery(0, 500, Sort: "order"), default);
        Assert.Equal(200, all.Take);
        Assert.Equal(orders.Select(o => o.Number).OrderBy(n => n), all.Items.Select(i => i.OrderNumber));
        var byDelta = await svc.PendingApprovalsAsync(new SalesChangePendingQuery(Sort: "-delta"), default);
        Assert.Equal(new[] { 40m, 30m, 20m }, byDelta.Items.Select(i => i.TotalDelta));
        var one = await svc.PendingApprovalsAsync(new SalesChangePendingQuery(Q: orders[1].Number), default);
        Assert.Equal(orders[1].Number, Assert.Single(one.Items).OrderNumber);
        Assert.Equal(x.C.Name, one.Items[0].ClientName);
    }

    [Fact]
    public async Task An_addition_with_approval_creates_the_child_when_approved()
    {
        await using var x = await ChangeKit.WorldAsync();
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.Always);
        var o = await x.RequestedAsync((x.P1, 3m));
        var svc = ChangeKit.Service(x);

        var pending = await svc.CreateAdditionAsync(o.PublicId, ChangeKit.Addition((x.P2, 2m)), default);
        Assert.Equal((SalesChangeKinds.Addition, SalesOrderChangeStatuses.PendingApproval, (Guid?)null, 16m),
            (pending.Item.Kind, pending.Item.Status, pending.Item.AdditionPublicId, pending.Item.TotalAfter));
        Assert.Equal(1, await x.F.Db.Set<SalesOrder>().AsNoTracking().CountAsync());

        x.F.Tenant.UserId = 2;
        var approved = await svc.ApproveAsync(o.PublicId, pending.Item.PublicId, null, default);
        Assert.Equal(SalesOrderChangeStatuses.Applied, approved.Item.Status);
        Assert.NotNull(approved.Item.AdditionPublicId);
        var child = await x.Svc.GetAsync(approved.Item.AdditionPublicId!.Value, default);
        Assert.Equal((SalesOrderStatuses.Requested, o.PublicId, 2m), (child.Status, child.ParentPublicId!.Value, child.Lines[0].QtyReserved));
        Assert.Single(x.Publisher.Published, p => p.Event == SalesChangeEventCodes.AdditionCreated);
    }
}
