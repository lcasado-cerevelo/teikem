using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P3): la orden interna de ADICIÓN nace por el alta de siempre (CreateCoreAsync) y se solicita con el RequestAsync de siempre: cuelga de la raíz, copia lo
// de la madre (cliente, almacén, entrega, consignación, vendedor y ruta), no pide firma y avisa «Orden solicitada».
public sealed class SalesOrderAdditionTests
{
    private static async Task<SalesOrder> TrackedAsync(SalesChangeWorld x, Guid publicId)
        => await x.F.Db.Set<SalesOrder>().AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.PublicId == publicId);

    [Fact]
    public async Task The_addition_is_born_draft_then_requested_hangs_from_the_parent_and_copies_its_terms_seller_and_route()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var mother = await x.RequestedAsync((x.P1, 2m));
        // la copia congelada de la madre (vendedor y ruta) y una firma que el cliente exige
        var row = await x.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == mother.PublicId);
        (row.SellerUserId, row.SalesRouteId, row.IsConsignment) = (77, 5, false);
        var client = await x.F.Db.Set<Teikem.Domain.Clients.Client>().AsTracking().SingleAsync(c => c.ClientId == x.C.ClientId);
        client.RequireOrderSignature = true;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var parent = await TrackedAsync(x, mother.PublicId);
        var id = await x.Svc.CreateAdditionCoreAsync(parent, new[] { new SalesOrderLineRequest(x.P2.PublicId, 3m) }, "Faltó una caja", false, false, default);
        var child = await x.Svc.GetAsync(id, default);
        Assert.Equal("REQUESTED", child.Status);
        Assert.Equal((mother.PublicId, mother.Number), (child.ParentPublicId, child.ParentNumber));
        Assert.Equal((x.C.PublicId, x.W.PublicId), (child.ClientPublicId, child.WarehousePublicId));
        Assert.Equal(SalesDeliveryModes.Delivery, child.Terms!.DeliveryMode);
        Assert.Equal($"Adición de {mother.Number} — Faltó una caja", child.Notes);
        Assert.Equal((3m, 3m), (child.Lines[0].QtyOrdered, child.Lines[0].QtyReserved));
        var stored = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == id);
        Assert.Equal((77, 5), (stored.SellerUserId, stored.SalesRouteId));   // copiados de la madre, no recalculados
        var history = await x.F.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == stored.SalesOrderId && h.EntityType!.InternalCode == SalesCodes.EntityType)
            .OrderBy(h => h.EntityStatusHistoryId).Select(h => h.ToStatusCodeId).ToListAsync();
        Assert.Equal(new[] { x.F.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Draft), x.F.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Requested) }, history.ToArray());
        Assert.Contains(x.Publisher.Published, p => p.Event == SalesEventCodes.OrderRequested && p.Key == SalesOrderService.RequestedDedupKey(id));

        // la madre muestra su adición
        var again = await x.Svc.GetAsync(mother.PublicId, default);
        var add = Assert.Single(again.Additions!);
        Assert.Equal((child.Number, "REQUESTED", 24m), (add.Number, add.Status, add.Total));
        var list = await x.Svc.ListAsync(null, null, null, false, null, null, 0, 10, default);
        Assert.True(list.Items.Single(i => i.PublicId == id).IsAddition);
        Assert.False(list.Items.Single(i => i.PublicId == mother.PublicId).IsAddition);
    }

    [Fact]
    public async Task An_addition_of_an_addition_hangs_from_the_root()
    {
        await using var x = await SalesChangeWorld.CreateAsync();
        var mother = await x.RequestedAsync((x.P1, 2m));
        var first = await x.Svc.CreateAdditionCoreAsync(await TrackedAsync(x, mother.PublicId), new[] { new SalesOrderLineRequest(x.P2.PublicId, 1m) }, null, false, false, default);
        var second = await x.Svc.CreateAdditionCoreAsync(await TrackedAsync(x, first), new[] { new SalesOrderLineRequest(x.P3.PublicId, 1m) }, null, false, false, default);
        var dto = await x.Svc.GetAsync(second, default);
        Assert.Equal(mother.PublicId, dto.ParentPublicId);
        Assert.Equal($"Adición de {mother.Number}", dto.Notes);
        Assert.Equal(2, (await x.Svc.GetAsync(mother.PublicId, default)).Additions!.Count);
    }

    [Fact]
    public async Task The_parent_must_be_in_progress_and_a_shortage_is_a_409()
    {
        await using var x = await SalesChangeWorld.CreateAsync(stock: 5m);
        var draft = await x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P1.PublicId, 1m) }), default);
        var draftRow = await TrackedAsync(x, draft.PublicId);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() =>
            x.Svc.CreateAdditionCoreAsync(draftRow, new[] { new SalesOrderLineRequest(x.P2.PublicId, 1m) }, null, false, false, default));
        Assert.Equal(SalesOrderService.AdditionParentNotOpen, ex.Message);

        var mother = await x.RequestedAsync((x.P1, 2m));
        var motherRow = await TrackedAsync(x, mother.PublicId);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            x.Svc.CreateAdditionCoreAsync(motherRow, new[] { new SalesOrderLineRequest(x.P2.PublicId, 50m) }, null, false, false, default));
        // con backorder sí nace, con lo que falta pendiente
        var id = await x.Svc.CreateAdditionCoreAsync(await TrackedAsync(x, mother.PublicId), new[] { new SalesOrderLineRequest(x.P2.PublicId, 7m) }, null, true, false, default);
        var child = await x.Svc.GetAsync(id, default);
        Assert.Equal((5m, 2m), (child.Lines[0].QtyReserved, child.Lines[0].QtyBackordered));
    }
}
