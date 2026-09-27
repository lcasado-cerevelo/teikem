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
/// Lote 6 (P9, D29; maestro L318-L320 y L543) — pruebas de SERVICIO del cruce de muelle sobre InMemory (WmsFixture) con el
/// CrossDockReceiptParticipant REAL (no el fake de ReceiptServiceTests), ReceiptService, CrossDockService,
/// CrossDockTaskHandler, WarehouseTaskService, DockAppointmentService, StatusService e InventoryLedger reales:
/// - confirmar un recibo con asignaciones PLANNED reparte FIFO (ConfirmedQty/ShortQty), reserva el staging, crea las tareas
///   CROSSDOCK y descuenta el putaway;
/// - mover (desde la cola y desde el plan) asienta un CROSSDOCK negativo con FromReserved: en mano y reservado bajan juntos;
/// - cancelar una asignación confirmada libera la reserva, cancela su tarea y crea una PUTAWAY nueva (R22);
/// - con CROSSDOCK apagado la cola no mueve (403 module_disabled, D28);
/// - citas solapadas en un muelle → 409.
/// </summary>
public sealed class CrossDockServiceTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseZone StagingZone, WarehouseBin Staging, WarehouseBin OtherStaging,
        WarehouseBin Reserve, Product P, Teikem.Domain.Orders.TransportOrder O1, Teikem.Domain.Orders.TransportOrder O2, WarehouseDock Dock);

    private static async Task<World> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IStatusTransitionEffect, WarehouseTaskStatusEffect>();
            s.AddSingleton<IStatusTransitionEffect, DockAppointmentStatusEffect>();
            s.AddSingleton<IWarehouseTaskHandler, PutawayTaskHandler>();
            s.AddSingleton<IWarehouseTaskHandler, CrossDockTaskHandler>();
            s.AddSingleton<IPurchaseOrderReceiving, ReceivingFakePurchaseOrders>();
            s.AddSingleton<IReceiptConfirmationParticipant, CrossDockReceiptParticipant>();
            s.AddSingleton<AsnService>();
            s.AddSingleton<ReceiptService>();
            s.AddSingleton<CrossDockService>();
            s.AddSingleton<WarehouseTaskService>();
            s.AddSingleton<DockAppointmentService>();
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<TraceabilityService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var stgZone = await f.AddZoneAsync(w, "STG", ZoneTypes.Staging);
        var stg = await f.AddBinAsync(stgZone, "STG-01");
        var otherStg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG2", ZoneTypes.Staging), "STG-02");
        var rsv = await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var p = await f.AddProductAsync("PX");
        var client = await f.AddClientAsync("C1");
        var o1 = await f.AddOrderAsync(client.ClientId, "O-1");
        var o2 = await f.AddOrderAsync(client.ClientId, "O-2");
        var dock = await f.AddDockAsync(w, "D1", DockTypes.Both);
        return new World(f, w, stgZone, stg, otherStg, rsv, p, o1, o2, dock);
    }

    private static async Task<ReceiptDetailDto> BlindReceiptAsync(World x, decimal qty)
        => await x.F.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind,
            StagingBinId: x.Staging.WarehouseBinId, Lines: new[] { new ReceiptLineRequest(x.P.PublicId, qty) }), default);

    private static async Task<List<WarehouseTask>> TasksAsync(World x, string type)
    {
        var typeId = x.F.LookupId(LookupDomains.WarehouseTaskType, type);
        return await x.F.Db.WarehouseTasks.AsNoTracking().Where(t => t.TaskTypeLookupId == typeId).OrderBy(t => t.WarehouseTaskId).ToListAsync();
    }

    private static Task<List<CrossDockAllocation>> AllocationsAsync(World x)
        => x.F.Db.Set<CrossDockAllocation>().AsNoTracking().OrderBy(a => a.CrossDockAllocationId).ToListAsync();

    [Fact]
    public async Task Confirming_an_open_receipt_splits_fifo_reserves_staging_and_moves_from_queue_and_plan()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipt = await BlindReceiptAsync(x, 10m);
        var line = Assert.Single(receipt.Lines);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);

        // Modo (a): asignaciones contra el recibo abierto, sin reserva ni tarea.
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(line.Id, x.O1.PublicId, 4m), default);
        await Assert.ThrowsAsync<ConflictException>(() => xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(line.Id, x.O2.PublicId, 7m), default));
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(line.Id, x.O2.PublicId, 6m), default);
        Assert.All(await AllocationsAsync(x), a => Assert.Null(a.ConfirmedQty));
        Assert.Empty(await TasksAsync(x, WarehouseTaskTypes.CrossDock));

        // Llegan 8: O1 cubierta (4), O2 con 4 y faltante outbound 2; todo a cruce de muelle → sin PUTAWAY, recibo a PUTAWAY.
        var receipts = f.Get<ReceiptService>();
        await receipts.UpdateLineAsync(receipt.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 8m), default);
        var confirmed = await receipts.ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default);
        Assert.Equal(ReceiptStatuses.Putaway, confirmed.Header.StatusCode);
        Assert.Empty(await TasksAsync(x, WarehouseTaskTypes.Putaway));

        var allocations = await AllocationsAsync(x);
        Assert.Equal(new decimal?[] { 4m, 4m }, allocations.Select(a => a.ConfirmedQty).ToArray());
        var dto = await xd.GetAsync(plan.Id, default);
        Assert.Equal(new[] { 0m, 2m }, dto.Allocations.OrderBy(a => a.Id).Select(a => a.ShortQty).ToArray());
        var tasks = await TasksAsync(x, WarehouseTaskTypes.CrossDock);
        Assert.Equal(new decimal?[] { 4m, 4m }, tasks.Select(t => t.Quantity).ToArray());
        Assert.All(allocations, a => Assert.NotNull(a.WarehouseTaskId));
        var balance = await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId);
        Assert.Equal(8m, balance!.QtyOnHand);
        Assert.Equal(8m, balance.QtyReserved);

        // Con CROSSDOCK apagado la cola no mueve (403 module_disabled) y nada cambia.
        var queue = f.Get<WarehouseTaskService>();
        var o1Task = allocations[0].WarehouseTaskId!.Value;
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing);
        await Assert.ThrowsAsync<ModuleDisabledException>(() => queue.CompleteAsync(o1Task, new TaskCompleteRequest(), default));
        await Assert.ThrowsAsync<ModuleDisabledException>(() => queue.StartAsync(o1Task, default));
        f.Db.ChangeTracker.Clear();
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Purchasing, ModuleKeys.CrossDock);

        // Desde la cola: otra cantidad que la confirmada → 400 ExactQty; la confirmada → CROSSDOCK −4 con FromReserved.
        var exact = await Assert.ThrowsAsync<ValidationException>(() => queue.CompleteAsync(o1Task, new TaskCompleteRequest(Quantity: 3m), default));
        Assert.Contains(CrossDockRules.ExactQty(4m), exact.Errors!.SelectMany(e => e.Value));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(WarehouseTaskStatuses.Done, (await queue.CompleteAsync(o1Task, new TaskCompleteRequest(), default)).StatusCode);

        var txns = await f.TransactionsAsync();
        var move = txns.Last();
        Assert.Equal(f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.CrossDock), move.TxnTypeLookupId);
        Assert.Equal(-4m, move.Quantity);
        Assert.Equal(x.Staging.WarehouseBinId, move.FromBinId);
        Assert.Null(move.ToWarehouseId);
        Assert.Equal(f.LookupId(LookupDomains.EntityType, EntityTypes.CrossDockAllocation), move.RefEntityLookupId);
        balance = await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId);
        Assert.Equal(4m, balance!.QtyOnHand);
        Assert.Equal(4m, balance.QtyReserved);
        var o1Alloc = (await AllocationsAsync(x))[0];
        Assert.Equal(AllocationStatuses.Moved, f.StatusCodeOf(o1Alloc.StatusCodeId));
        Assert.Equal(move.InventoryTransactionId, o1Alloc.InventoryTransactionId);

        // Desde el plan: O2 por lo confirmado (4); el staging queda en cero sin reserva.
        await xd.MoveAsync(plan.Id, allocations[1].CrossDockAllocationId, new CrossDockMoveRequest(), default);
        balance = await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId);
        Assert.Equal(0m, balance!.QtyOnHand);
        Assert.Equal(0m, balance.QtyReserved);
        Assert.Equal(WarehouseTaskStatuses.Done, f.StatusCodeOf((await f.TaskAsync(allocations[1].WarehouseTaskId!.Value)).StatusCodeId));
        Assert.All(await AllocationsAsync(x), a => Assert.Equal(AllocationStatuses.Moved, f.StatusCodeOf(a.StatusCodeId)));
        Assert.Equal(CrossDockStatuses.Completed, (await xd.CompleteAsync(plan.Id, default)).StatusCode);
    }

    [Fact]
    public async Task Cross_dock_keeps_lot_and_serial_from_inbound_to_outbound()
    {
        // Maestro L321: trazabilidad completa del cruce de muelle, manteniendo lote/serie de inbound a outbound.
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipts = f.Get<ReceiptService>();
        var pl = await f.AddProductAsync("PL", TrackingTypes.Lot);
        var ps = await f.AddProductAsync("PS", TrackingTypes.Serial);

        // 1) Recibo ciego abierto: PL 3 con lote L-1 y PS 2 con series S1/S2, ambos en STG-01.
        var receipt = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind,
            StagingBinId: x.Staging.WarehouseBinId, Lines: new[]
            {
                new ReceiptLineRequest(pl.PublicId, 3m, Lot: new LotInput("L-1")),
                new ReceiptLineRequest(ps.PublicId, 2m, SerialNumbers: new[] { "S1", "S2" }),
            }), default);
        var lotLine = receipt.Lines.Single(l => l.ProductPublicId == pl.PublicId);
        var serialLine = receipt.Lines.Single(l => l.ProductPublicId == ps.PublicId);
        var lotId = lotLine.LotId!.Value;

        // 2) Asignar las dos líneas a O1 con el recibo abierto y confirmar: la reserva del staging lleva el lote.
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(lotLine.Id, x.O1.PublicId, 3m), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(serialLine.Id, x.O1.PublicId, 2m), default);
        await receipts.ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default);
        var lotBalance = await f.BalanceAsync(pl.ProductId, x.Staging.WarehouseBinId, lotId);
        Assert.Equal(3m, lotBalance!.QtyReserved);
        Assert.Equal(2m, (await f.BalanceAsync(ps.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
        // Maestro L328: las series asignadas quedan RESERVED (no AVAILABLE) mientras esperan el movimiento.
        foreach (var n in new[] { "S1", "S2" })
            Assert.Equal(SerialStatuses.Reserved, f.StatusCodeOf((await f.SerialAsync(ps.ProductId, n)).StatusCodeId!.Value));
        var allocations = await AllocationsAsync(x);
        var lotAlloc = allocations.Single(a => a.ReceiptLineId == lotLine.Id);
        var serialAlloc = allocations.Single(a => a.ReceiptLineId == serialLine.Id);

        // 3) Mover el lote: un CROSSDOCK −3 con el LotId de L-1.
        await xd.MoveAsync(plan.Id, lotAlloc.CrossDockAllocationId, new CrossDockMoveRequest(), default);
        var crossDockType = f.LookupId(LookupDomains.InventoryTxnType, InventoryTxnTypes.CrossDock);
        var lotMove = Assert.Single(await f.TransactionsAsync(), t => t.TxnTypeLookupId == crossDockType);
        Assert.Equal(pl.ProductId, lotMove.ProductId);
        Assert.Equal(lotId, lotMove.LotId);
        Assert.Equal(-3m, lotMove.Quantity);
        Assert.Equal(0m, (await f.BalanceAsync(pl.ProductId, x.Staging.WarehouseBinId, lotId))!.QtyOnHand);

        // 5) Negativo: una serie que ya no está RESERVED en el staging → 422 SerialsNotInStaging, sin movimiento.
        var s2 = await f.Db.InventorySerials.SingleAsync(s => s.ProductId == ps.ProductId && s.SerialNumber == "S2");
        var reservedId = s2.StatusCodeId;
        s2.StatusCodeId = f.StatusId(StatusDomains.SerialStatus, SerialStatuses.Shipped);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var txnsBefore = (await f.TransactionsAsync()).Count;
        var notInStaging = await Assert.ThrowsAsync<StatusRuleException>(() =>
            xd.MoveAsync(plan.Id, serialAlloc.CrossDockAllocationId, new CrossDockMoveRequest(), default));
        Assert.Equal(CrossDockRules.SerialsNotInStaging, notInStaging.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(txnsBefore, (await f.TransactionsAsync()).Count);
        Assert.Equal(AllocationStatuses.Planned, f.StatusCodeOf((await AllocationsAsync(x)).Single(a => a.ReceiptLineId == serialLine.Id).StatusCodeId));
        s2 = await f.Db.InventorySerials.SingleAsync(s => s.SerialId == s2.SerialId);
        s2.StatusCodeId = reservedId;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        // 3b) Mover la serie: dos asientos de −1 con SerialId, SerialNumber y el lote de la serie; ambas quedan SHIPPED.
        await xd.MoveAsync(plan.Id, serialAlloc.CrossDockAllocationId, new CrossDockMoveRequest(), default);
        var serialMoves = (await f.TransactionsAsync()).Where(t => t.TxnTypeLookupId == crossDockType && t.ProductId == ps.ProductId).ToList();
        Assert.Equal(2, serialMoves.Count);
        Assert.All(serialMoves, t => Assert.Equal(-1m, t.Quantity));
        var s1 = await f.SerialAsync(ps.ProductId, "S1");
        var s2After = await f.SerialAsync(ps.ProductId, "S2");
        Assert.Equal(new[] { s1.SerialId, s2After.SerialId }.OrderBy(i => i), serialMoves.Select(t => t.SerialId!.Value).OrderBy(i => i));
        Assert.All(serialMoves, t => Assert.Equal(t.SerialId == s1.SerialId ? s1.LotId : s2After.LotId, t.LotId));
        Assert.Equal(SerialStatuses.Shipped, f.StatusCodeOf(s1.StatusCodeId!.Value));
        Assert.Equal(SerialStatuses.Shipped, f.StatusCodeOf(s2After.StatusCodeId!.Value));
        var kardex = await f.Get<InventoryReadService>().KardexAsync(new KardexQuery(Types: new[] { InventoryTxnTypes.CrossDock },
            ProductPublicIds: new[] { ps.PublicId }), InventoryScope.Any, default);
        Assert.Equal(new[] { "S1", "S2" }, kardex.Items.Select(r => r.SerialNumber).OrderBy(n => n).ToArray());

        // 4) Genealogía del lote y rastro de la serie: la asignación y la orden O1 como destino.
        var trace = f.Get<TraceabilityService>();
        var genealogy = await trace.LotGenealogyAsync(lotId, InventoryScope.Any, default);
        Assert.Equal(3m, genealogy.QtyIn);
        Assert.Equal(3m, genealogy.QtyOut);
        Assert.Equal(0m, genealogy.QtyOnHand);
        Assert.Contains(genealogy.Movements, m => m.TypeCode == InventoryTxnTypes.CrossDock && m.Quantity == -3m);
        var destination = Assert.Single(genealogy.Destinations);
        Assert.Equal(EntityTypes.CrossDockAllocation, destination.RefEntityCode);
        Assert.Equal(lotAlloc.CrossDockAllocationId, destination.RefId);
        Assert.Equal(x.O1.PublicId, destination.OrderPublicId);
        Assert.Equal(3m, destination.Quantity);

        var serialTrace = await trace.SerialTraceAsync(ps.PublicId, "S1", InventoryScope.Any, default);
        var outbound = Assert.Single(serialTrace.Movements, m => m.TypeCode == InventoryTxnTypes.CrossDock);
        Assert.Equal(EntityTypes.CrossDockAllocation, outbound.RefEntityCode);
        Assert.Equal(serialAlloc.CrossDockAllocationId, outbound.RefId);
        Assert.Equal(SerialStatuses.Shipped, serialTrace.Serial.StatusCode);
        Assert.Contains(SerialStatuses.Shipped, serialTrace.StatusHistory.Select(h => h.ToCode));
    }

    [Fact]
    public async Task Inventory_writers_of_the_plan_require_the_wms_module()
    {
        // CROSSDOCK no depende de WMS_LOTSERIAL en ModuleDefinition: apagar WMS no lo apaga. Asignar, cancelar, mover y
        // completar escriben inventario (reserva, ledger, tareas), así que exigen además WMS_LOTSERIAL (403 module_disabled).
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipt = await BlindReceiptAsync(x, 5m);
        await f.Get<ReceiptService>().ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(receipt.Lines[0].Id, x.O1.PublicId, 3m), default);
        var allocation = Assert.Single(await AllocationsAsync(x));
        var txnsBefore = (await f.TransactionsAsync()).Count;

        f.SetModules(ModuleKeys.CrossDock, ModuleKeys.Purchasing);
        var expected = new ModuleDisabledException(ModuleKeys.WmsLotSerial).Message;
        Assert.Equal(expected, (await Assert.ThrowsAsync<ModuleDisabledException>(() =>
            xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(receipt.Lines[0].Id, x.O2.PublicId, 1m), default))).Message);
        Assert.Equal(expected, (await Assert.ThrowsAsync<ModuleDisabledException>(() =>
            xd.MoveAsync(plan.Id, allocation.CrossDockAllocationId, new CrossDockMoveRequest(), default))).Message);
        Assert.Equal(expected, (await Assert.ThrowsAsync<ModuleDisabledException>(() =>
            xd.CancelAllocationAsync(plan.Id, allocation.CrossDockAllocationId, null, default))).Message);
        Assert.Equal(expected, (await Assert.ThrowsAsync<ModuleDisabledException>(() => xd.CompleteAsync(plan.Id, default))).Message);

        // Nada cambió: la reserva sigue, no hay movimiento nuevo y la asignación sigue PLANNED.
        Assert.Equal(3m, (await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
        Assert.Equal(txnsBefore, (await f.TransactionsAsync()).Count);
        Assert.Equal(AllocationStatuses.Planned, f.StatusCodeOf(Assert.Single(await AllocationsAsync(x)).StatusCodeId));
        // La lectura del plan sigue disponible con solo CROSSDOCK encendido.
        Assert.Equal(plan.Id, (await xd.GetAsync(plan.Id, default)).Id);
    }

    [Fact]
    public async Task Allocating_a_confirmed_receipt_reduces_putaway_and_cancelling_releases_and_recreates_it()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipt = await BlindReceiptAsync(x, 5m);
        await f.Get<ReceiptService>().ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default);
        var putaway = Assert.Single(await TasksAsync(x, WarehouseTaskTypes.Putaway));
        Assert.Equal(5m, putaway.Quantity);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);

        // Modo (b): la PUTAWAY baja a 2, se reservan 3 en staging y nace la tarea CROSSDOCK por 3.
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(receipt.Lines[0].Id, x.O1.PublicId, 3m), default);
        Assert.Equal(2m, (await f.TaskAsync(putaway.WarehouseTaskId)).Quantity);
        Assert.Equal(3m, (await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
        var allocation = Assert.Single(await AllocationsAsync(x));
        Assert.Equal(3m, allocation.ConfirmedQty);
        var crossTask = Assert.Single(await TasksAsync(x, WarehouseTaskTypes.CrossDock));
        Assert.Equal(3m, crossTask.Quantity);

        // Cancelar: libera la reserva, cancela la CROSSDOCK y devuelve 3 a putaway con una PUTAWAY nueva (R22).
        await xd.CancelAllocationAsync(plan.Id, allocation.CrossDockAllocationId, "Se canceló la orden", default);
        Assert.Equal(0m, (await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
        Assert.Equal(5m, (await f.BalanceAsync(x.P.ProductId, x.Staging.WarehouseBinId))!.QtyOnHand);
        Assert.Equal(WarehouseTaskStatuses.Cancelled, f.StatusCodeOf((await f.TaskAsync(crossTask.WarehouseTaskId)).StatusCodeId));
        Assert.Equal(AllocationStatuses.Cancelled, f.StatusCodeOf(Assert.Single(await AllocationsAsync(x)).StatusCodeId));
        var putaways = await TasksAsync(x, WarehouseTaskTypes.Putaway);
        Assert.Equal(2, putaways.Count);
        Assert.Equal(3m, putaways[1].Quantity);
        Assert.Equal(WarehouseTaskStatuses.Pending, f.StatusCodeOf(putaways[1].StatusCodeId));
        Assert.Equal(receipt.Header.Id, putaways[1].RefId);
        // Reservar no mueve inventario: solo el RECEIPT del recibo.
        Assert.Single(await f.TransactionsAsync());
    }

    [Fact]
    public async Task Serial_allocation_on_a_confirmed_receipt_reserves_the_serials_and_cancelling_releases_them()
    {
        // Maestro L328 (disponible/reservado/despachado) con el modo (b) del cruce de muelle.
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipts = f.Get<ReceiptService>();
        var ps = await f.AddProductAsync("PS", TrackingTypes.Serial);
        var receipt = await receipts.CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind,
            StagingBinId: x.Staging.WarehouseBinId, Lines: new[] { new ReceiptLineRequest(ps.PublicId, 3m, SerialNumbers: new[] { "T1", "T2", "T3" }) }), default);
        await receipts.ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        async Task<string[]> StatusesAsync()
        {
            var list = new List<string>();
            foreach (var n in new[] { "T1", "T2", "T3" }) list.Add(f.StatusCodeOf((await f.SerialAsync(ps.ProductId, n)).StatusCodeId!.Value));
            return list.ToArray();
        }

        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(receipt.Lines[0].Id, x.O1.PublicId, 2m), default);
        Assert.Equal(new[] { SerialStatuses.Reserved, SerialStatuses.Reserved, SerialStatuses.Available }, await StatusesAsync());
        Assert.Equal(2m, (await f.BalanceAsync(ps.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);

        var allocation = Assert.Single(await AllocationsAsync(x));
        await xd.CancelAllocationAsync(plan.Id, allocation.CrossDockAllocationId, null, default);
        Assert.Equal(new[] { SerialStatuses.Available, SerialStatuses.Available, SerialStatuses.Available }, await StatusesAsync());
        Assert.Equal(0m, (await f.BalanceAsync(ps.ProductId, x.Staging.WarehouseBinId))!.QtyReserved);
    }

    [Fact]
    public async Task Candidates_list_open_and_confirmed_lines_of_the_plan_zone_with_what_is_left()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipts = f.Get<ReceiptService>();
        var open = await BlindReceiptAsync(x, 5m);
        var confirmed = await BlindReceiptAsync(x, 4m);
        await receipts.ConfirmAsync(confirmed.Header.PublicId, new ReceiptConfirmRequest(), default);
        var outside = await x.F.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(WarehousePublicId: x.W.PublicId, Type: ReceiptTypes.Blind,
            StagingBinId: x.OtherStaging.WarehouseBinId, Lines: new[] { new ReceiptLineRequest(x.P.PublicId, 7m) }), default);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(open.Lines[0].Id, x.O1.PublicId, 2m), default);

        var candidates = await xd.CandidatesAsync(plan.Id, default);
        var openCand = Assert.Single(candidates, c => c.ReceiptLineId == open.Lines[0].Id);
        Assert.Equal((ReceiptStatuses.Open, 5m, 2m, 3m), (openCand.ReceiptStatusCode, openCand.BaseQty, openCand.AllocatedQty, openCand.Allocatable));
        var confirmedCand = Assert.Single(candidates, c => c.ReceiptLineId == confirmed.Lines[0].Id);
        Assert.Equal((ReceiptStatuses.Received, 4m), (confirmedCand.ReceiptStatusCode, confirmedCand.Allocatable));
        Assert.Equal("STG-01", confirmedCand.StagingBinCode);
        Assert.DoesNotContain(candidates, c => c.ReceiptLineId == outside.Lines[0].Id);   // fuera de la zona del plan
    }

    [Fact]
    public async Task Rescheduling_keeps_duration_rechecks_overlap_and_only_applies_to_scheduled()
    {
        // Maestro L317 y D30: agenda sin solapamiento por muelle.
        var x = await CreateAsync();
        await using var f = x.F;
        var appts = f.Get<DockAppointmentService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var first = await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start, start.AddHours(1)), default);
        var second = await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start.AddHours(2), start.AddHours(2.5)), default);

        // Reprogramar sobre la otra cita → 409, sin cambios.
        var overlap = await Assert.ThrowsAsync<ConflictException>(() =>
            appts.RescheduleAsync(second.Id, new DockAppointmentPatchRequest(ScheduledStartUtc: start.AddMinutes(30)), default));
        Assert.Equal(DockScheduleRules.Overlap, overlap.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(start.AddHours(2), (await appts.GetAsync(second.Id, default)).ScheduledStartUtc);

        // Solo el inicio: conserva la duración (30 minutos).
        var moved = await appts.RescheduleAsync(second.Id, new DockAppointmentPatchRequest(ScheduledStartUtc: start.AddHours(4)), default);
        Assert.Equal((start.AddHours(4), start.AddHours(4.5)), (moved.ScheduledStartUtc, moved.ScheduledEndUtc));

        // Una cita que ya llegó no se reprograma (422).
        await appts.SetStatusAsync(first.Id, new DockAppointmentStatusRequest(AppointmentStatuses.Arrived), default);
        var arrived = await Assert.ThrowsAsync<StatusRuleException>(() =>
            appts.RescheduleAsync(first.Id, new DockAppointmentPatchRequest(ScheduledStartUtc: start.AddHours(6)), default));
        Assert.Equal(DockScheduleRules.NotReschedulable, arrived.Message);
    }

    [Fact]
    public async Task Status_changes_occupy_and_free_the_dock_keep_the_no_show_reason_and_recheck_overlap_on_return()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var appts = f.Get<DockAppointmentService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var a = await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start, start.AddHours(1)), default);

        var arrived = await appts.SetStatusAsync(a.Id, new DockAppointmentStatusRequest("arrived"), default);
        Assert.Equal((AppointmentStatuses.Arrived, DockStatuses.Occupied), (arrived.StatusCode, arrived.DockStatusCode));
        var completed = await appts.SetStatusAsync(a.Id, new DockAppointmentStatusRequest(AppointmentStatuses.Completed), default);
        Assert.Equal((AppointmentStatuses.Completed, DockStatuses.Free), (completed.StatusCode, completed.DockStatusCode));

        // NO_SHOW con motivo en el comentario del historial (D30).
        var b = await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start.AddHours(3), start.AddHours(4)), default);
        await appts.SetStatusAsync(b.Id, new DockAppointmentStatusRequest(AppointmentStatuses.NoShow, "El transportista no llegó"), default);
        var apptType = f.LookupId(LookupDomains.EntityType, EntityTypes.DockAppointment);
        var noShowId = f.StatusId(StatusDomains.AppointmentStatus, AppointmentStatuses.NoShow);
        var history = await f.Db.EntityStatusHistories.AsNoTracking()
            .SingleAsync(h => h.EntityTypeLookupId == apptType && h.EntityId == b.Id && h.ToStatusCodeId == noShowId);
        Assert.Equal("El transportista no llegó", history.Comment);

        // Otra cita ocupa ahora su hueco: volver a SCHEDULED re-verifica el solapamiento → 409.
        await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start.AddHours(3), start.AddHours(4)), default);
        var back = await Assert.ThrowsAsync<ConflictException>(() =>
            appts.SetStatusAsync(b.Id, new DockAppointmentStatusRequest(AppointmentStatuses.Scheduled), default));
        Assert.Equal(DockScheduleRules.Overlap, back.Message);
    }

    [Fact]
    public async Task Receipt_or_line_with_cross_dock_cannot_be_removed()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipts = f.Get<ReceiptService>();
        var receipt = await BlindReceiptAsync(x, 4m);
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(receipt.Lines[0].Id, x.O1.PublicId, 2m), default);

        var line = await Assert.ThrowsAsync<ConflictException>(() => receipts.RemoveLineAsync(receipt.Header.PublicId, receipt.Lines[0].Id, default));
        Assert.Equal(ReceiptRules.LineHasCrossDock, line.Message);
        f.Db.ChangeTracker.Clear();
        var whole = await Assert.ThrowsAsync<ConflictException>(() => receipts.DeleteAsync(receipt.Header.PublicId, default));
        Assert.Equal(ReceiptRules.ReceiptHasCrossDock, whole.Message);
        f.Db.ChangeTracker.Clear();
        Assert.Single((await receipts.GetAsync(receipt.Header.PublicId, default)).Lines);
    }

    [Fact]
    public async Task Receipt_line_outside_the_plan_zone_is_rejected_at_confirmation()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var xd = f.Get<CrossDockService>();
        var receipts = f.Get<ReceiptService>();
        var receipt = await BlindReceiptAsync(x, 4m);
        var line = receipt.Lines[0];
        var plan = await xd.CreateAsync(new CrossDockPlanRequest(x.W.PublicId, x.StagingZone.WarehouseZoneId), default);
        await xd.AllocateAsync(plan.Id, new CrossDockAllocationRequest(line.Id, x.O1.PublicId, 2m), default);
        await receipts.UpdateLineAsync(receipt.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(StagingBinId: x.OtherStaging.WarehouseBinId), default);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => receipts.ConfirmAsync(receipt.Header.PublicId, new ReceiptConfirmRequest(), default));
        Assert.Contains(CrossDockRules.StagingZone, ex.Errors!.SelectMany(e => e.Value));
    }

    [Fact]
    public async Task Overlapping_appointment_on_the_same_dock_is_a_conflict()
    {
        var x = await CreateAsync();
        await using var f = x.F;
        var appts = f.Get<DockAppointmentService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start, start.AddHours(1)), default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => appts.CreateAsync(
            new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start.AddMinutes(30)), default));
        Assert.Equal(DockScheduleRules.Overlap, ex.Message);

        // Contigua (semiabierto): se permite.
        await appts.CreateAsync(new DockAppointmentRequest(x.W.PublicId, x.Dock.WarehouseDockId, DockDirections.Inbound, start.AddHours(1)), default);
    }
}
