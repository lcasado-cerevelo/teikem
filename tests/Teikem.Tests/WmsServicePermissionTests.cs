using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 — permisos que exige SOLO el servicio (el controlador pide uno más débil o distinto), con un usuario que no es
/// administrador de plataforma (WmsFixture.SetPermissions). Si alguien quita una de estas llamadas a EnsureAsync, la prueba
/// falla aunque el smoke no pase por ahí:
/// - la cola (start/complete piden inventory.view) exige el permiso del handler (D41): PUTAWAY → warehouse.receive;
/// - empacar exige orders.create (D27: el Operador recolecta pero no empaca) y eliminar una recolección PACKED, orders.cancel;
/// - REORDER de un faltante exige purchasing.manage (el controlador pide inventory.adjust);
/// - recibir contra una OC exige purchasing.receive (el controlador pide warehouse.receive).
/// </summary>
public sealed class WmsServicePermissionTests
{
    private static string Missing(string permission) => $"Falta el permiso '{permission}'.";

    [Fact]
    public async Task Queue_start_and_complete_require_the_handler_permission()
    {
        await using var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<WarehouseTaskService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var stg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var p = await f.AddProductAsync("PN");
        var receipt = await f.AddReceiptAsync(w, ReceiptStatuses.Received);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 5m, ToWarehouseId: w.WarehouseId, ToBinId: stg.WarehouseBinId,
            RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId));
        var task = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w.WarehouseId, p.ProductId, 5m,
            FromBinId: stg.WarehouseBinId, ToBinId: rsv.WarehouseBinId, RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId));
        var queue = f.Get<WarehouseTaskService>();

        // Solo lectura: tiene inventory.view (lo que pide el controlador) y hasta warehouse.pick, pero no warehouse.receive.
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick);
        var start = await Assert.ThrowsAsync<ForbiddenException>(() => queue.StartAsync(task.WarehouseTaskId, default));
        Assert.Equal(Missing(PermissionCatalog.WarehouseReceive), start.Message);
        var complete = await Assert.ThrowsAsync<ForbiddenException>(() =>
            queue.CompleteAsync(task.WarehouseTaskId, new TaskCompleteRequest(ToBinId: rsv.WarehouseBinId), default));
        Assert.Equal(Missing(PermissionCatalog.WarehouseReceive), complete.Message);
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf((await f.TaskAsync(task.WarehouseTaskId)).StatusCodeId));
        Assert.Single(await f.TransactionsAsync());   // solo el RECEIPT inicial: ningún TRANSFER

        // Control positivo: con warehouse.receive se completa.
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehouseReceive);
        var done = await queue.CompleteAsync(task.WarehouseTaskId, new TaskCompleteRequest(ToBinId: rsv.WarehouseBinId), default);
        Assert.Equal(WarehouseTaskStatuses.Done, done.StatusCode);
    }

    private static Task<WmsFixture> PickBatchFixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
        sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
        sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(),
        sp.GetRequiredService<InventoryLedger>(), null!)));

    [Fact]
    public async Task Packing_requires_orders_create()
    {
        await using var f = await PickBatchFixtureAsync();
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Get<PickBatchService>().PackAsync(Guid.NewGuid(), new PickBatchPackRequest(null), default));
        Assert.Equal(Missing(PermissionCatalog.OrdersCreate), ex.Message);
    }

    [Fact]
    public async Task Deleting_a_packed_batch_requires_orders_cancel_and_changes_nothing()
    {
        await using var f = await PickBatchFixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PN");
        var client = await f.AddClientAsync("C1");
        var order = await f.AddOrderAsync(client.ClientId, "2026-000777", packBatchNumber: "EMP-00009");
        var batch = new PickBatch
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = "EMP-00009",
            StatusCodeId = f.StatusId(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed), TransportOrderId = order.TransportOrderId,
            CollectedAtUtc = DateTime.UtcNow, IsActive = true,
        };
        f.Db.Set<PickBatch>().Add(batch);
        await f.Db.SaveChangesAsync();
        f.Db.Set<PickBatchLine>().Add(new PickBatchLine { PickBatchId = batch.PickBatchId, ProductId = p.ProductId, FromBinId = bin.WarehouseBinId, Quantity = 1m, IssueTxnId = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        // El Operador de almacén (warehouse.pick, sin orders.cancel) no elimina una recolección empacada.
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehousePick);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Get<PickBatchService>().DeleteAsync(batch.PublicId, new PickBatchDeleteRequest(), default));
        Assert.Equal(Missing(PermissionCatalog.OrdersCancel), ex.Message);
        f.Db.ChangeTracker.Clear();
        var after = await f.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PickBatchId == batch.PickBatchId);
        Assert.True(after.IsActive);
        Assert.Equal(PickBatchStatuses.Packed, f.StatusCodeOf(after.StatusCodeId));
        Assert.True((await f.Db.TransportOrders.AsNoTracking().SingleAsync(o => o.TransportOrderId == order.TransportOrderId)).IsActive);
        Assert.Empty(await f.TransactionsAsync());
    }

    [Fact]
    public async Task Reorder_of_a_shortage_requires_purchasing_manage()
    {
        await using var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<PurchaseOrderService>();
            s.AddSingleton<PurchaseShortageService>();
        });
        // Tiene inventory.adjust (lo que pide el controlador) y purchasing.view, pero no purchasing.manage.
        f.SetPermissions(PermissionCatalog.InventoryAdjust, PermissionCatalog.PurchasingView);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Get<PurchaseShortageService>().ResolveAsync(Guid.NewGuid(), 1, new ShortageResolveRequest(ShortageActions.Reorder), default));
        Assert.Equal(Missing(PermissionCatalog.PurchasingManage), ex.Message);
    }

    [Fact]
    public async Task Receiving_against_a_purchase_order_requires_purchasing_receive()
    {
        await using var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IPurchaseOrderReceiving, ReceivingFakePurchaseOrders>();
            s.AddSingleton<IReceiptConfirmationParticipant, ReceivingFakeCrossDock>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
            s.AddReceiptDamageDeps();
        });
        // Tiene warehouse.receive (lo que pide el controlador), pero no purchasing.receive.
        f.SetPermissions(PermissionCatalog.InventoryView, PermissionCatalog.WarehouseReceive);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: Guid.NewGuid()), default));
        Assert.Equal(Missing(PermissionCatalog.PurchasingReceive), ex.Message);
        Assert.Empty(await f.Db.Set<ReceiptHeader>().AsNoTracking().ToListAsync());
    }
}
