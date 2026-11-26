using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P3): una orden con adiciones abiertas no se cancela (422 con el texto exacto); cancelada la adición, la madre sí, y su modificación pendiente queda RETIRADA.
public sealed class SalesCancelWithAdditionsTests
{
    [Fact]
    public async Task Cancelling_a_mother_with_an_open_addition_is_a_422_and_after_cancelling_the_addition_it_cancels_and_withdraws_the_pending_change()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        await x.SeedChangeCatalogsAsync();
        var mother = await x.RequestedAsync((x.P1, 2m));
        var parentRow = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == mother.PublicId);
        var additionId = await x.Svc.CreateAdditionCoreAsync(parentRow, new[] { new SalesOrderLineRequest(x.P2.PublicId, 1m) }, null, false, false, default);
        var addition = await x.Svc.GetAsync(additionId, default);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Svc.CancelAsync(mother.PublicId, "Ya no lo quiere", default));
        Assert.Equal($"La orden tiene adiciones abiertas ({addition.Number}): cancélelas o despáchelas antes de cancelar esta.", ex.Message);
        Assert.Equal(SalesOrderService.CancelWithOpenAdditions(addition.Number), ex.Message);
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal("REQUESTED", (await x.Svc.GetAsync(mother.PublicId, default)).Status);

        var pending = await x.AddPendingChangeAsync(parentRow.SalesOrderId);
        Assert.Equal((pending.PublicId, 1), ((await x.Svc.GetAsync(mother.PublicId, default)).PendingChangePublicId, (await x.Svc.GetAsync(mother.PublicId, default)).ChangeCount));
        Assert.Equal("CANCELLED", (await x.Svc.CancelAsync(additionId, null, default)).Status);
        var cancelled = await x.Svc.CancelAsync(mother.PublicId, "Ya no lo quiere", default);
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Null(cancelled.PendingChangePublicId);
        var change = await x.F.Db.Set<SalesOrderChange>().AsNoTracking().SingleAsync(c => c.PublicId == pending.PublicId);
        Assert.Equal((9104, false, "La orden se canceló"), (change.StatusCodeId, change.IsPending, change.DecisionNote));   // WITHDRAWN
        Assert.Equal((20m, 0m), await x.BalanceOfAsync(x.P1));   // la reserva de la madre se liberó
    }
}
