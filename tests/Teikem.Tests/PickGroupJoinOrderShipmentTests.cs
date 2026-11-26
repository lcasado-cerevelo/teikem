using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 249 — «Unir al envío de la orden» (POST pick-batches/{id}/join-order-shipment): excepción acotada a la regla del Lote 176. Solo dentro de la familia de la orden
/// (la misma orden, su raíz y las adiciones de la raíz), mismo cliente y almacén; se suma a la maestra sin empacar, o nace una maestra; si el destino está empacado
/// se reabre en la misma llamada (warehouse.reopen-pack y motivo) y la maestra toma el número EMP del envío reabierto.
/// </summary>
public sealed class PickGroupJoinOrderShipmentTests
{
    private const string Reason = "Sale junto con la adición";

    private static async Task<List<PickBatch>> MembersAsync(ReopenWorld w, Guid groupPublicId)
    {
        var id = (await w.Db.Set<PickGroup>().AsNoTracking().SingleAsync(g => g.PublicId == groupPublicId)).PickGroupId;
        return await w.Db.Set<PickBatch>().AsNoTracking().Where(b => b.PickGroupId == id && b.IsActive).OrderBy(b => b.PickBatchId).ToListAsync();
    }

    [Fact]
    public async Task Addition_with_a_loose_collected_batch_of_the_mother_makes_a_new_master_with_a_counter_number()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync();
        var addition = await w.OrderAsync(5m, parent: mother.PublicId);
        var a = await w.DispatchAsync(mother, 3m);
        var b = await w.DispatchAsync(addition, 2m);

        var group = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(Note: "Mismo camión"), default);

        Assert.Equal("COLLECTED", group.StatusCode);
        Assert.NotEqual(a.Number, group.Number);
        Assert.NotEqual(b.Number, group.Number);
        Assert.Equal("Mismo camión", group.Note);
        Assert.Equal(new[] { a.PublicId, b.PublicId }, (await MembersAsync(w, group.PublicId)).Select(m => m.PublicId));
        Assert.Equal(new[] { mother.Number, addition.Number }, group.Documents.Select(d => d.Number).OrderBy(n => n));
        Assert.True(group.CanPack);
    }

    [Fact]
    public async Task Joins_an_unpacked_master_of_the_family()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync();
        var first = await w.OrderAsync(5m, parent: mother.PublicId);
        var second = await w.OrderAsync(5m, parent: mother.PublicId);
        var a = await w.DispatchAsync(mother, 3m);
        var b = await w.DispatchAsync(first, 2m);
        var c = await w.DispatchAsync(second, 1m);
        var group = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default);   // a y c son posibles: se elige a

        // c (adición de la misma raíz): el único destino es la maestra (a + b), cuenta una sola vez
        var joined = await w.Groups.JoinOrderShipmentAsync(c.PublicId, new PickBatchJoinOrderRequest(), default);

        Assert.Equal(group.PublicId, joined.PublicId);
        Assert.Equal(group.Number, joined.Number);
        Assert.Equal(new[] { a.PublicId, b.PublicId, c.PublicId }, (await MembersAsync(w, group.PublicId)).Select(m => m.PublicId));
    }

    [Fact]
    public async Task A_second_batch_of_the_same_order_joins_too()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var order = await w.OrderAsync();
        var a = await w.DispatchAsync(order, 3m);
        var b = await w.DispatchAsync(order, 2m);
        var group = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(), default);
        Assert.Equal(new[] { a.PublicId, b.PublicId }, (await MembersAsync(w, group.PublicId)).Select(m => m.PublicId));
    }

    [Fact]
    public async Task Packed_mother_is_reopened_and_the_master_takes_the_shipment_number()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync();
        var addition = await w.OrderAsync(5m, parent: mother.PublicId);
        var a = await w.DispatchAsync(mother, 3m);
        var shipment = await w.PackAsync(a.PublicId);
        var b = await w.DispatchAsync(addition, 2m);

        // sin «Reabrir el empaque» → 422
        Assert.Equal((422, PickGroupRules.JoinNeedsReopen(a.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(), default)));
        // con la casilla pero sin motivo → 400
        Assert.Equal((400, PickBatchRules.ReopenReasonRequired),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(ReopenPack: true), default)));
        // sin el permiso → 403
        w.F.SetPermissions(PermissionCatalog.WarehousePick);
        Assert.Equal((403, PickBatchRules.ReopenPermission),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(ReopenPack: true, Reason: Reason), default)));
        Assert.Equal(PickBatchStatuses.Packed, w.Code((await w.BatchAsync(a.PublicId)).StatusCodeId));

        w.F.SetPermissions(PermissionCatalog.WarehousePick, PermissionCatalog.WarehouseReopenPack);
        var group = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(ReopenPack: true, Reason: Reason), default);

        Assert.Equal(shipment.PackBatchNumber, group.Number);   // el número que ya veía el cliente
        Assert.Equal(a.Number, group.Number);
        Assert.Equal("COLLECTED", group.StatusCode);
        Assert.False((await w.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == shipment.TransportOrderId)).IsActive);
        var members = await MembersAsync(w, group.PublicId);
        Assert.Equal(new[] { a.PublicId, b.PublicId }, members.Select(m => m.PublicId));
        Assert.All(members, m => Assert.Equal(PickBatchStatuses.Collected, w.Code(m.StatusCodeId)));
        Assert.Single(await w.OutboxAsync(SalesChangeEventCodes.PackReopened));

        // Empacar la maestra: UN envío con ese número (el anulado ya no lo ocupa)
        var packed = await w.PackAsync(a.PublicId, groupPublicId: group.PublicId);
        Assert.Equal(a.Number, packed.PackBatchNumber);
        Assert.Single(await w.Db.TransportOrders.AsNoTracking().Where(o => o.PackBatchNumber == a.Number && o.IsActive).ToListAsync());
    }

    [Fact]
    public async Task Packed_master_of_the_family_is_reopened_and_keeps_its_number()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync();
        var addition = await w.OrderAsync(5m, parent: mother.PublicId);
        var a1 = await w.DispatchAsync(mother, 3m);
        var a2 = await w.DispatchAsync(mother, 2m);
        var group = await w.Groups.JoinOrderShipmentAsync(a2.PublicId, new PickBatchJoinOrderRequest(), default);
        await w.PackAsync(a1.PublicId, groupPublicId: group.PublicId);
        var b = await w.DispatchAsync(addition, 1m);

        Assert.Equal((422, PickGroupRules.JoinNeedsReopen(group.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(), default)));
        var joined = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(ReopenPack: true, Reason: Reason), default);
        Assert.Equal(group.PublicId, joined.PublicId);
        Assert.Equal("COLLECTED", joined.StatusCode);
        Assert.Equal(3, (await MembersAsync(w, group.PublicId)).Count);
    }

    [Fact]
    public async Task Exact_messages_for_each_block()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync(20m);
        var addition = await w.OrderAsync(5m, parent: mother.PublicId);
        var stranger = await w.OrderAsync(5m);   // misma compañía y cliente, pero otra familia
        var a = await w.DispatchAsync(mother, 3m);
        var s = await w.DispatchAsync(stranger, 1m);
        var b = await w.DispatchAsync(addition, 1m);

        // otra familia (destino elegido a mano)
        Assert.Equal((422, PickGroupRules.JoinNotFamily(b.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(s.PublicId), default)));
        // consigo misma
        Assert.Equal((400, PickGroupRules.JoinSelf(b.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(b.PublicId), default)));
        // una orden sin familia (la suelta del extraño) no tiene con quién salir
        Assert.Equal((422, PickGroupRules.JoinNoTarget(stranger.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(s.PublicId, new PickBatchJoinOrderRequest(), default)));

        // varios destinos sin escoger → 400
        var a2 = await w.DispatchAsync(mother, 2m);
        Assert.Equal((400, PickGroupRules.JoinChooseTarget(mother.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(), default)));

        // destino cuyo envío ya salió
        var shipped = await w.PackAsync(a2.PublicId, OrderStatuses.Pickup);
        Assert.Equal((422, PickGroupRules.JoinTargetLeft(a2.Number, OrderStatuses.Pickup)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a2.PublicId), default)));
        _ = shipped;

        // la que se une ya está empacada → no recolectada
        var packedJoin = await w.DispatchAsync(addition, 1m);
        await w.PackAsync(packedJoin.PublicId);
        Assert.Equal((422, PickGroupRules.JoinNotCollected(packedJoin.Number, PickBatchStatuses.Packed)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(packedJoin.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default)));

        // ya está en una maestra
        var group = await w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default);
        Assert.Equal((422, PickGroupRules.JoinAlreadyInGroup(b.Number, group.Number)),
            await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default)));

        // no existe o es de otra compañía → 404
        Assert.Equal(404, (await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(Guid.NewGuid(), new PickBatchJoinOrderRequest(), default))).Status);
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            w.F.SetModules(ModuleKeys.SalesOrders);
            Assert.Equal(404, (await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(a2.PublicId, new PickBatchJoinOrderRequest(), default))).Status);
        }
    }

    [Fact]
    public async Task Other_client_or_warehouse_in_the_family_is_rejected_with_the_master_messages()
    {
        await using var w = await ReopenWorld.CreateAsync();
        var mother = await w.OrderAsync();
        var addition = await w.OrderAsync(5m, parent: mother.PublicId);
        var a = await w.DispatchAsync(mother, 3m);
        var b = await w.DispatchAsync(addition, 2m);

        // a mano: la adición queda de otro cliente (no pasa por la API; prueba la guarda)
        var other = await w.X.ClientAsync("OTRO");
        var row = await w.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == addition.PublicId);
        var originalClient = row.ClientId;
        row.ClientId = other.ClientId;
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        var (status, message) = await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default));
        Assert.Equal(400, status);
        Assert.Equal(PickGroupRules.OtherClient($"{mother.Number} ({w.Client.Name})", $"{addition.Number} ({other.Name})"), message);

        // y de otro almacén
        var w2 = await w.F.AddWarehouseAsync("W2");
        row = await w.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == addition.PublicId);
        (row.ClientId, row.WarehouseId) = (originalClient, w2.WarehouseId);
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        (status, message) = await ReopenWorld.FailAsync(() => w.Groups.JoinOrderShipmentAsync(b.PublicId, new PickBatchJoinOrderRequest(a.PublicId), default));
        Assert.Equal(400, status);
        Assert.Equal(PickGroupRules.OtherWarehouse($"{mother.Number} ({w.X.W.Code})", $"{addition.Number} ({w2.Code})"), message);
        Assert.Empty(await w.Db.Set<PickGroup>().AsNoTracking().ToListAsync());
    }
}
