using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P5) — pruebas de SERVICIO de la cola (InMemory, WmsFixture): completar PUTAWAY y REPLENISH por
/// WarehouseTaskService con los handlers, el efecto que cierra el recibo y el ledger reales (maestro L301 y L306).
/// El remanente de un completado parcial se crea ANTES del DONE: si se invirtiera, el efecto vería cerrada la última PUTAWAY
/// y pasaría el recibo a PUTAWAY con una tarea abierta (lo detecta Partial_completion_…).
/// </summary>
public sealed class WarehouseTaskServiceTests
{
    private sealed record Setup(WmsFixture F, Warehouse W, WarehouseBin Staging, WarehouseBin Reserve, WarehouseBin Picking, WarehouseBin OtherWarehouseBin,
        Product Product, ReceiptHeader Receipt, WarehouseTask Task);

    private static async Task<Setup> PutawayAsync(decimal qty = 5m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<IWarehouseTaskHandler, ReplenishTaskHandler>();
            s.AddSingleton<WarehouseTaskService>();
            s.AddSingleton<ReplenishmentService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var stg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var pck = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var w2 = await f.AddWarehouseAsync("W2");
        var other = await f.AddBinAsync(await f.AddZoneAsync(w2, "RSV", ZoneTypes.Reserve), "R-01");
        var p = await f.AddProductAsync("PN");
        var receipt = await f.AddReceiptAsync(w, ReceiptStatuses.Received);
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, qty, ToWarehouseId: w.WarehouseId, ToBinId: stg.WarehouseBinId,
            RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId));
        var task = await f.AddTaskAsync(new WarehouseTaskSpec(WarehouseTaskTypes.Putaway, w.WarehouseId, p.ProductId, qty,
            FromBinId: stg.WarehouseBinId, ToBinId: rsv.WarehouseBinId, RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId));
        return new Setup(f, w, stg, rsv, pck, other, p, receipt, task);
    }

    private static async Task<string> ReceiptStatusAsync(Setup s)
        => s.F.StatusCodeOf((await s.F.Db.Set<ReceiptHeader>().AsNoTracking().SingleAsync(r => r.ReceiptHeaderId == s.Receipt.ReceiptHeaderId)).StatusCodeId);

    [Fact]
    public async Task Full_putaway_transfers_from_staging_and_closes_the_receipt()
    {
        var s = await PutawayAsync();
        await using var f = s.F;

        var dto = await f.Get<WarehouseTaskService>().CompleteAsync(s.Task.WarehouseTaskId,
            new TaskCompleteRequest(ToBinId: s.Reserve.WarehouseBinId, Quantity: 5m), default);

        Assert.Equal(WarehouseTaskStatuses.Done, dto.StatusCode);
        var txns = await f.TransactionsAsync();
        var transfer = txns.Last();
        Assert.Equal(2, txns.Count);
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), transfer.TxnTypeLookupId);
        Assert.Equal(5m, transfer.Quantity);
        Assert.Equal(f.LookupId(LookupDomains.EntityType, EntityTypes.WarehouseTask), transfer.RefEntityLookupId);
        Assert.Equal(s.Task.WarehouseTaskId, transfer.RefId);
        Assert.Equal(0m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
        Assert.Equal(5m, await f.OnHandAsync(s.Product.ProductId, s.Reserve.WarehouseBinId));

        var task = await f.TaskAsync(s.Task.WarehouseTaskId);
        Assert.Equal(WarehouseTaskStatuses.Done, f.StatusCodeOf(task.StatusCodeId));
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal(ReceiptStatuses.Putaway, await ReceiptStatusAsync(s));
        Assert.Contains(ReceiptStatuses.Putaway, await f.HistoryCodesAsync(EntityTypes.Receipt, s.Receipt.ReceiptHeaderId));
    }

    [Fact]
    public async Task Partial_completion_creates_the_remainder_before_done_and_keeps_the_receipt_received()
    {
        var s = await PutawayAsync();
        await using var f = s.F;

        await f.Get<WarehouseTaskService>().CompleteAsync(s.Task.WarehouseTaskId, new TaskCompleteRequest(Quantity: 2m), default);

        var done = await f.TaskAsync(s.Task.WarehouseTaskId);
        Assert.Equal(WarehouseTaskStatuses.Done, f.StatusCodeOf(done.StatusCodeId));
        Assert.Equal(2m, done.Quantity);
        var remainder = await f.Db.WarehouseTasks.AsNoTracking().SingleAsync(t => t.WarehouseTaskId != s.Task.WarehouseTaskId);
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf(remainder.StatusCodeId));
        Assert.Equal(3m, remainder.Quantity);
        Assert.Equal(s.Receipt.ReceiptHeaderId, remainder.RefId);
        Assert.Equal(done.RefEntityLookupId, remainder.RefEntityLookupId);
        Assert.Equal(s.Staging.WarehouseBinId, remainder.FromBinId);
        Assert.Equal(s.Reserve.WarehouseBinId, remainder.ToBinId);
        Assert.Equal(ReceiptStatuses.Received, await ReceiptStatusAsync(s));
        Assert.Equal(3m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
        Assert.Equal(2m, await f.OnHandAsync(s.Product.ProductId, s.Reserve.WarehouseBinId));

        // La última PUTAWAY cierra el recibo.
        await f.Get<WarehouseTaskService>().CompleteAsync(remainder.WarehouseTaskId, new TaskCompleteRequest(), default);
        Assert.Equal(ReceiptStatuses.Putaway, await ReceiptStatusAsync(s));
    }

    [Fact]
    public async Task Invalid_destination_or_quantity_writes_nothing()
    {
        var s = await PutawayAsync();
        await using var f = s.F;
        var svc = f.Get<WarehouseTaskService>();

        // Posición de otro almacén → 404 sin oráculo.
        await Assert.ThrowsAsync<NotFoundException>(() => svc.CompleteAsync(s.Task.WarehouseTaskId,
            new TaskCompleteRequest(ToBinId: s.OtherWarehouseBin.WarehouseBinId), default));
        f.Db.ChangeTracker.Clear();

        // Más que la tarea → 400 exacto.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CompleteAsync(s.Task.WarehouseTaskId,
            new TaskCompleteRequest(Quantity: 6m), default));
        Assert.Contains(WarehouseTaskRules.QtyExceeds, ex.Errors!.SelectMany(e => e.Value));
        f.Db.ChangeTracker.Clear();

        // Posición inactiva → la rechaza el ledger (422).
        var inactive = await f.AddBinAsync(await f.AddZoneAsync(s.W, "RS2", ZoneTypes.Reserve), "R-09", isActive: false);
        await Assert.ThrowsAsync<StatusRuleException>(() => svc.CompleteAsync(s.Task.WarehouseTaskId,
            new TaskCompleteRequest(ToBinId: inactive.WarehouseBinId), default));
        f.Db.ChangeTracker.Clear();

        Assert.Single(await f.TransactionsAsync());   // solo el RECEIPT inicial
        Assert.Single(await f.Db.WarehouseTasks.AsNoTracking().ToListAsync());
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf((await f.TaskAsync(s.Task.WarehouseTaskId)).StatusCodeId));
        Assert.Equal(5m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
    }

    [Fact]
    public async Task Replenishment_is_idempotent_by_open_task_and_completes_as_a_transfer()
    {
        var s = await PutawayAsync();
        await using var f = s.F;
        // Producto con posición de picking preferida bajo el mínimo y reserva disponible.
        var p = await f.AddProductAsync("PR", preferredWarehouseId: s.W.WarehouseId, preferredBinId: s.Picking.WarehouseBinId);
        var tracked = await f.Db.Products.SingleAsync(x => x.ProductId == p.ProductId);
        tracked.MinPickQty = 5m;
        tracked.MaxPickQty = 8m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 2m, ToWarehouseId: s.W.WarehouseId, ToBinId: s.Picking.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: s.W.WarehouseId, ToBinId: s.Reserve.WarehouseBinId));

        var run = f.Get<ReplenishmentService>();
        var first = await run.RunAsync(new ReplenishmentRunRequest(s.W.PublicId), default);
        Assert.Equal(1, first.TasksCreated);
        var task = Assert.Single(first.Tasks);
        Assert.Equal(6m, task.Quantity);                  // objetivo MaxPickQty 8 − 2 en picking
        Assert.Equal(s.Reserve.WarehouseBinId, task.FromBinId);
        Assert.Equal(s.Picking.WarehouseBinId, task.ToBinId);

        var second = await run.RunAsync(new ReplenishmentRunRequest(s.W.PublicId), default);
        Assert.Equal(0, second.TasksCreated);
        Assert.Equal(1, second.SkippedWithOpenTask);

        await f.Get<WarehouseTaskService>().CompleteAsync(task.Id, new TaskCompleteRequest(), default);
        Assert.Equal(8m, await f.OnHandAsync(p.ProductId, s.Picking.WarehouseBinId));
        Assert.Equal(4m, await f.OnHandAsync(p.ProductId, s.Reserve.WarehouseBinId));
        var transfer = (await f.TransactionsAsync()).Last();
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.Transfer), transfer.TxnTypeLookupId);
        Assert.Equal(task.Id, transfer.RefId);
    }
    [Fact]
    public async Task Replenishment_measures_the_picking_bin_by_available_not_on_hand()
    {
        // Maestro L306: "cuando el DISPONIBLE baja del mínimo". Picking con 6 en mano y 3 reservados (disponible 3),
        // MinPickQty 5 / MaxPickQty 8 → una REPLENISH por 8 − 3 = 5.
        var s = await PutawayAsync();
        await using var f = s.F;
        var p = await f.AddProductAsync("PR", preferredWarehouseId: s.W.WarehouseId, preferredBinId: s.Picking.WarehouseBinId);
        var tracked = await f.Db.Products.SingleAsync(x => x.ProductId == p.ProductId);
        tracked.MinPickQty = 5m;
        tracked.MaxPickQty = 8m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 6m, ToWarehouseId: s.W.WarehouseId, ToBinId: s.Picking.WarehouseBinId),
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: s.W.WarehouseId, ToBinId: s.Reserve.WarehouseBinId));
        await f.ReserveAsync(new StockReservation(p.ProductId, s.W.WarehouseId, s.Picking.WarehouseBinId, null, 3m));

        var run = await f.Get<ReplenishmentService>().RunAsync(new ReplenishmentRunRequest(s.W.PublicId), default);
        Assert.Equal(1, run.TasksCreated);
        var task = Assert.Single(run.Tasks);
        Assert.Equal(5m, task.Quantity);
        Assert.Equal(s.Reserve.WarehouseBinId, task.FromBinId);
        Assert.Equal(s.Picking.WarehouseBinId, task.ToBinId);
    }

    [Fact]
    public async Task Replenishment_skips_serial_products()
    {
        // D23: el reabasto excluye los productos con serie (una REPLENISH de cantidad no lleva series).
        var s = await PutawayAsync();
        await using var f = s.F;
        var ps = await f.AddProductAsync("PS", tracking: TrackingTypes.Serial,
            preferredWarehouseId: s.W.WarehouseId, preferredBinId: s.Picking.WarehouseBinId);
        var t = await f.Db.Products.SingleAsync(x => x.ProductId == ps.ProductId);
        t.MinPickQty = 5m;
        t.MaxPickQty = 8m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var r = await f.Get<ReplenishmentService>().RunAsync(new ReplenishmentRunRequest(s.W.PublicId), default);
        Assert.Equal(0, r.ProductsEvaluated);   // el SERIAL ni se evalúa
        Assert.Equal(0, r.SkippedNoReserve);
        Assert.Equal(0, r.TasksCreated);
    }

    private const int MemberUserId = 50;
    private const int OtherTenantUserId = 60;
    private const int SuspendedUserId = 70;

    private static async Task SeedMembershipsAsync(WmsFixture f)
    {
        f.Db.StatusCodes.AddRange(
            new Teikem.Domain.Catalogs.StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true, IsInitial = true },
            new Teikem.Domain.Catalogs.StatusCode { StatusCodeId = 901, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Suspended, LabelJson = "{\"es\":\"Suspendido\"}", IsActive = true });
        f.Db.UserTenants.AddRange(
            new Teikem.Domain.Security.UserTenant { UserTenantId = 1, UserId = MemberUserId, TenantId = WmsFixture.TenantId, StatusCodeId = 900 },
            new Teikem.Domain.Security.UserTenant { UserTenantId = 2, UserId = OtherTenantUserId, TenantId = WmsFixture.OtherTenantId, StatusCodeId = 900 },
            new Teikem.Domain.Security.UserTenant { UserTenantId = 3, UserId = SuspendedUserId, TenantId = WmsFixture.TenantId, StatusCodeId = 901 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Assign_to_user_of_other_tenant_or_inactive_is_400_and_to_a_member_is_200()
    {
        // Maestro L308: cola con tipo, prioridad y asignación. Solo a miembros ACTIVOS de la compañía (sin oráculo).
        var s = await PutawayAsync();
        await using var f = s.F;
        await SeedMembershipsAsync(f);
        var queue = f.Get<WarehouseTaskService>();

        foreach (var uid in new[] { OtherTenantUserId, SuspendedUserId, 999 })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => queue.AssignAsync(s.Task.WarehouseTaskId, new TaskAssignRequest(uid), default));
            Assert.Equal(WarehouseTaskRules.AssigneeNotMember, Assert.Single(ex.Errors!["userId"]));
            Assert.Null((await f.TaskAsync(s.Task.WarehouseTaskId)).AssignedToUserId);
        }

        var dto = await queue.AssignAsync(s.Task.WarehouseTaskId, new TaskAssignRequest(MemberUserId), default);
        Assert.Equal(MemberUserId, dto.AssignedToUserId);
        Assert.Equal(MemberUserId, (await f.TaskAsync(s.Task.WarehouseTaskId)).AssignedToUserId);
        // Desasignar (userId null) también se permite con la tarea abierta.
        Assert.Null((await queue.AssignAsync(s.Task.WarehouseTaskId, new TaskAssignRequest(null), default)).AssignedToUserId);
    }

    [Fact]
    public async Task Cancel_last_putaway_from_queue_closes_the_receipt()
    {
        var s = await PutawayAsync();
        await using var f = s.F;
        var dto = await f.Get<WarehouseTaskService>().CancelAsync(s.Task.WarehouseTaskId, new TaskCancelRequest("Se guardará en otra corrida"), default);
        Assert.Equal(WarehouseTaskStatuses.Cancelled, dto.StatusCode);
        var task = await f.TaskAsync(s.Task.WarehouseTaskId);
        Assert.NotNull(task.CompletedAtUtc);
        Assert.Equal(ReceiptStatuses.Putaway, await ReceiptStatusAsync(s));
        // Una tarea cerrada ya no se cancela ni se asigna.
        await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<WarehouseTaskService>().CancelAsync(s.Task.WarehouseTaskId, null, default));
    }

    [Fact]
    public async Task Distribute_moves_the_same_quantity_to_each_bin_in_one_transaction_and_leaves_the_remainder()
    {
        var s = await PutawayAsync(185m);
        await using var f = s.F;
        var rsv2 = await f.AddBinAsync(await f.Db.WarehouseZones.AsNoTracking().FirstAsync(z => z.Code == "RSV" && z.WarehouseId == s.W.WarehouseId), "R-02");

        var dto = await f.Get<WarehouseTaskService>().DistributeAsync(s.Task.WarehouseTaskId,
            new TaskDistributeRequest(20m, new[] { s.Reserve.WarehouseBinId, rsv2.WarehouseBinId }), default);

        Assert.Equal(WarehouseTaskStatuses.Done, dto.StatusCode);
        Assert.Equal(20m, await f.OnHandAsync(s.Product.ProductId, s.Reserve.WarehouseBinId));
        Assert.Equal(20m, await f.OnHandAsync(s.Product.ProductId, rsv2.WarehouseBinId));
        Assert.Equal(145m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
        var done = await f.TaskAsync(s.Task.WarehouseTaskId);
        Assert.Equal(40m, done.Quantity);
        var remainder = await f.Db.WarehouseTasks.AsNoTracking().SingleAsync(t => t.WarehouseTaskId != s.Task.WarehouseTaskId);
        Assert.Equal(145m, remainder.Quantity);
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf(remainder.StatusCodeId));
        Assert.Equal(ReceiptStatuses.Received, await ReceiptStatusAsync(s));
    }

    [Fact]
    public async Task Distribute_rejects_more_bins_than_fit_and_repeated_bins_without_moving_anything()
    {
        var s = await PutawayAsync(45m);
        await using var f = s.F;
        var zone = await f.Db.WarehouseZones.AsNoTracking().FirstAsync(z => z.Code == "RSV" && z.WarehouseId == s.W.WarehouseId);
        var b2 = await f.AddBinAsync(zone, "R-02");
        var b3 = await f.AddBinAsync(zone, "R-03");
        var svc = f.Get<WarehouseTaskService>();

        // 45 de 20 caben 2 posiciones; una tercera se rechaza (los 5 sueltos van aparte).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.DistributeAsync(s.Task.WarehouseTaskId,
            new TaskDistributeRequest(20m, new[] { s.Reserve.WarehouseBinId, b2.WarehouseBinId, b3.WarehouseBinId }), default));
        Assert.Equal(WarehouseTaskRules.DistributeTooManyBins(20m, 45m, 2), Assert.Single(ex.Errors!["quantityPerBin"]));
        var dup = await Assert.ThrowsAsync<ValidationException>(() => svc.DistributeAsync(s.Task.WarehouseTaskId,
            new TaskDistributeRequest(20m, new[] { b2.WarehouseBinId, b2.WarehouseBinId }), default));
        Assert.Equal(WarehouseTaskRules.DistributeBinsDuplicated, Assert.Single(dup.Errors!["toBinIds"]));

        Assert.Equal(45m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf((await f.TaskAsync(s.Task.WarehouseTaskId)).StatusCodeId));
    }

    [Fact]
    public async Task Distribute_fails_atomically_when_one_bin_is_from_another_warehouse()
    {
        var s = await PutawayAsync(60m);
        await using var f = s.F;
        await Assert.ThrowsAsync<NotFoundException>(() => f.Get<WarehouseTaskService>().DistributeAsync(s.Task.WarehouseTaskId,
            new TaskDistributeRequest(20m, new[] { s.Reserve.WarehouseBinId, s.OtherWarehouseBin.WarehouseBinId }), default));
        Assert.Equal(60m, await f.OnHandAsync(s.Product.ProductId, s.Staging.WarehouseBinId));
        Assert.Equal(0m, await f.OnHandAsync(s.Product.ProductId, s.Reserve.WarehouseBinId));
    }
}
