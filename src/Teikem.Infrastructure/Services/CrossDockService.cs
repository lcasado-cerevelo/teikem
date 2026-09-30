using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P9) — cruce de muelle como demo funcional del módulo CROSSDOCK (R20-R22, maestro L316-L320, D29).
/// Un plan XD-##### (OPEN → ALLOCATED → COMPLETED) asigna líneas de recibos del almacén a órdenes de transporte:
/// - (a) recibo ABIERTO: se asigna contra lo recibido de la línea; la asignación queda PLANNED con ConfirmedQty NULL, sin
///   tarea ni reserva. Al confirmar el recibo, CrossDockReceiptParticipant reparte FIFO lo realmente recibido
///   (CrossDockRules.Split): ConfirmedQty por asignación, reserva del staging, tarea CROSSDOCK y faltante outbound visible.
/// - (b) recibo ya confirmado (RECEIVED/PUTAWAY): se asigna contra lo recibido no comprometido, limitado por la PUTAWAY
///   pendiente, que se reduce; el saldo de staging se RESERVA (InventoryLedger.ReserveAsync) y nace la tarea CROSSDOCK.
/// Lo asignado en staging queda reservado: ninguna recolección, putaway ni transferencia lo toma. Mover (desde el plan o
/// desde la cola con CrossDockTaskHandler) asienta un CROSSDOCK que SALE del inventario con FromReserved (libera la reserva
/// y baja lo en mano juntos), deja la tarea DONE y la asignación MOVED. Cancelar una asignación con mercancía confirmada
/// libera la reserva, cancela su tarea y crea una PUTAWAY nueva por lo confirmado (R22).
///
/// Orden de bloqueo (InventoryQueries): todo escritor de asignaciones bloquea Plan → ReceiptHeader de la línea → tarea →
/// saldos (ledger). La confirmación del recibo escribe asignaciones bajo el bloqueo del recibo SIN tocar el plan, así que no
/// hay inversión. Todo corre en RunInTransactionAsync (la estrategia reintenta los 1205 restantes).
/// El TenantId sale del principal; el plan (con TenantId) se expone por id bajo el filtro global y sus hijas sin TenantId
/// (asignación, línea de recibo, zona, posición, lote) se resuelven SIEMPRE por su padre filtrado (404 sin oráculo).
/// Módulos: el controlador exige CROSSDOCK; asignar, cancelar asignación, mover y completar escriben inventario (reserva,
/// ledger, tareas), así que además exigen WMS_LOTSERIAL aquí (403 module_disabled), porque CROSSDOCK no depende de WMS en
/// ModuleDefinition y apagar WMS no lo apaga. La ruta de la cola (CrossDockTaskHandler) ya pasa por el controlador de
/// tareas, que exige WMS_LOTSERIAL.
/// </summary>
public sealed class CrossDockService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    INumberSequenceService numbers,
    InventoryLedger ledger,
    WarehouseTaskWriter taskWriter,
    PutawaySuggester suggester,
    ModuleService modules)
{
    public const string NumberPattern = "XD-#####";
    public const string NumberTakenMessage = "Ya existe un plan de cruce de muelle con ese número; intente de nuevo.";
    public const string WarehouseInactive = "El almacén está inactivo.";
    public const int MaxRows = 200;
    public const int MaxCandidates = 500;

    private const string PlanLabel = "Plan de cruce de muelle";
    private const string AllocationLabel = "Asignación";
    private const string LineLabel = "Línea de recibo";

    // ================================================================ lectura

    public async Task<IReadOnlyList<CrossDockPlanDto>> ListAsync(Guid? warehousePublicId, string[]? status, CancellationToken ct)
    {
        var query = db.Set<CrossDockPlan>().AsNoTracking();
        if (warehousePublicId is Guid wh)
        {
            var warehouseId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == wh)
                                  .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct)
                              ?? throw new NotFoundException("Almacén");
            query = query.Where(p => p.WarehouseId == warehouseId);
        }
        if (status is { Length: > 0 })
        {
            var codes = CrossDockSupport.Codes(status);
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.CrossDockStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(p => ids.Contains(p.StatusCodeId));
        }
        var plans = await query.OrderByDescending(p => p.CrossDockPlanId).Take(MaxRows).ToListAsync(ct);
        return await BuildAsync(plans, ct);
    }

    public async Task<CrossDockPlanDto> GetAsync(int id, CancellationToken ct)
    {
        var plan = await PlanSnapshotAsync(id, ct);
        return (await BuildAsync(new List<CrossDockPlan> { plan }, ct))[0];
    }

    /// <summary>
    /// Líneas asignables del almacén del plan (y de su zona de staging, si la tiene): de recibos abiertos (EXPECTED,
    /// RECEIVING, DISCREPANCY; BaseQty = recibido de la línea, AllocatableOpen) y confirmados (RECEIVED, RECEIVED_VARIANCE)
    /// o PUTAWAY con putaway pendiente (AllocatableConfirmed). Solo las que tienen
    /// algo asignable; tope de 500.
    /// </summary>
    public async Task<IReadOnlyList<CrossDockCandidateDto>> CandidatesAsync(int planId, CancellationToken ct)
    {
        var plan = await PlanSnapshotAsync(planId, ct);
        // Lote 13: abiertos (EXPECTED, RECEIVING, DISCREPANCY) = modo (a); confirmados (RECEIVED, RECEIVED_VARIANCE) o PUTAWAY = modo (b).
        var openIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.OpenCodes, ct);
        var postedIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus,
            ReceiptStatuses.ConfirmedCodes.Append(ReceiptStatuses.Putaway), ct);
        var statusIds = openIds.Concat(postedIds).ToList();

        var rowsQuery = from l in db.Set<ReceiptLine>().AsNoTracking()
                        join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                        where r.WarehouseId == plan.WarehouseId && r.IsActive && statusIds.Contains(r.StatusCodeId)
                        select new { Line = l, r.ReceiptHeaderId, r.Number, r.StatusCodeId };
        if (plan.StagingZoneId is int zoneId)
        {
            var zoneBins = db.Set<WarehouseBin>().Where(b => b.WarehouseZoneId == zoneId && b.WarehouseId == plan.WarehouseId)
                .Select(b => b.WarehouseBinId);
            rowsQuery = rowsQuery.Where(x => x.Line.StagingBinId != null && zoneBins.Contains(x.Line.StagingBinId.Value));
        }
        var rows = await rowsQuery.OrderByDescending(x => x.ReceiptHeaderId).ThenBy(x => x.Line.ReceiptLineId)
            .Take(MaxCandidates).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<CrossDockCandidateDto>();

        var lineIds = rows.Select(x => x.Line.ReceiptLineId).ToList();
        var committed = await CommittedByLineAsync(lineIds, ct);
        var confirmedReceiptIds = rows.Where(x => !openIds.Contains(x.StatusCodeId)).Select(x => x.ReceiptHeaderId).Distinct().ToList();
        var pending = await PendingPutawayAsync(confirmedReceiptIds, ct);

        var productIds = rows.Select(x => x.Line.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku }).ToDictionaryAsync(p => p.ProductId, ct);
        var lots = await LotNumbersAsync(rows.Select(x => x.Line.LotId), productIds, ct);
        var bins = await BinCodesAsync(plan.WarehouseId, rows.Select(x => x.Line.StagingBinId), ct);
        var receiptStatus = await CrossDockSupport.StatusMapAsync(db, tenant, StatusDomains.ReceiptStatus, ct);

        var result = new List<CrossDockCandidateDto>();
        foreach (var x in rows)
        {
            var l = x.Line;
            var product = products.GetValueOrDefault(l.ProductId);
            if (product is null) continue;
            var active = committed.GetValueOrDefault(l.ReceiptLineId);
            decimal allocatable;
            if (openIds.Contains(x.StatusCodeId))
                allocatable = CrossDockRules.AllocatableOpen(l.ReceivedQty, active);
            else
            {
                var key = (x.ReceiptHeaderId, l.ProductId, l.LotId, l.StagingBinId);
                allocatable = CrossDockRules.AllocatableConfirmed(l.ReceivedQty, active, pending.GetValueOrDefault(key));
            }
            if (allocatable <= 0m) continue;
            result.Add(new CrossDockCandidateDto(l.ReceiptLineId, x.Number, receiptStatus.GetValueOrDefault(x.StatusCodeId)?.Code ?? string.Empty,
                product.PublicId, product.Sku, l.LotId is int lid ? lots.GetValueOrDefault(lid) : null,
                l.StagingBinId, l.StagingBinId is int sb ? bins.GetValueOrDefault(sb) : null,
                l.ReceivedQty, active, allocatable));
        }
        return result;
    }

    // ================================================================ alta

    public async Task<CrossDockPlanDto> CreateAsync(CrossDockPlanRequest? req, CancellationToken ct)
    {
        req ??= new CrossDockPlanRequest();
        var warehouse = await CrossDockSupport.ResolveWarehouseOrDefaultAsync(db, req.WarehousePublicId, ct);
        if (!warehouse.IsActive) throw new StatusRuleException(WarehouseInactive);

        int? zoneId = null;
        if (req.StagingZoneId is int requestedZone)
        {
            var zone = await (from z in db.Set<WarehouseZone>().AsNoTracking()
                              join w in db.Set<Warehouse>().AsNoTracking() on z.WarehouseId equals w.WarehouseId
                              where z.WarehouseZoneId == requestedZone && z.WarehouseId == warehouse.WarehouseId
                              select new { z.WarehouseZoneId, z.ZoneTypeLookupId, z.IsActive }).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Zona", feminine: true);
            var zoneType = zone.ZoneTypeLookupId is int zt ? (await lookups.GetAsync(zt, ct))?.InternalCode : null;
            if (!CrossDockRules.IsStagingZoneType(zoneType)) throw new ValidationException("stagingZoneId", CrossDockRules.StagingZoneType);
            if (!zone.IsActive) throw new StatusRuleException(CrossDockRules.StagingZoneInactive);
            zoneId = zone.WarehouseZoneId;
        }

        var tenantId = ((TenantContext)tenant).RequireTenantId();
        await numbers.EnsureAsync(NumberKinds.CrossDock, null, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.CrossDockStatus, ct);

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            // Número XD al final (último bloqueo del orden del lote).
            var seq = await numbers.NextAsync(NumberKinds.CrossDock, null, ct2);
            var plan = new CrossDockPlan
            {
                TenantId = tenantId, WarehouseId = warehouse.WarehouseId, Number = NumberFormat.Resolve(NumberPattern, seq),
                StatusCodeId = initial.StatusCodeId, StagingZoneId = zoneId, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };
            db.Set<CrossDockPlan>().Add(plan);
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);
            var born = await statuses.TransitionAsync(StatusDomains.CrossDockStatus, EntityTypes.CrossDockPlan, plan.CrossDockPlanId,
                null, initial.InternalCode, null, ct2);
            plan.StatusCodeId = born.StatusCodeId;
            await db.SaveGuardedAsync(NumberTakenMessage, ct2);
            return plan.CrossDockPlanId;
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ asignar

    /// <summary>
    /// Asigna una línea de recibo del almacén del plan a una orden activa (404) que admita asignaciones (422). Según el
    /// recibo: OPEN → modo (a), sin tarea ni reserva; RECEIVED/PUTAWAY → modo (b), reduce la PUTAWAY, reserva el staging y
    /// crea la tarea CROSSDOCK. Más de lo asignable → 409 Exceeds. La primera asignación pasa el plan OPEN → ALLOCATED.
    /// </summary>
    public async Task<CrossDockPlanDto> AllocateAsync(int planId, CrossDockAllocationRequest req, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.WmsLotSerial, ct);
        if (req?.ReceiptLineId is not int lineId) throw new ValidationException("receiptLineId", CrossDockRules.ReceiptLineRequired);
        if (req.OrderPublicId is not Guid orderPublicId) throw new ValidationException("orderPublicId", CrossDockRules.OrderRequired);
        var plan = await PlanSnapshotAsync(planId, ct);
        var line = await ResolveLineAsync(plan.WarehouseId, lineId, ct);
        var tracking = await TrackingCodeAsync(line.ProductId, ct);
        if (CrossDockRules.ValidateQuantity(req.Quantity, string.Equals(tracking, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase)) is string qtyError)
            throw new ValidationException("quantity", qtyError);
        var qty = req.Quantity!.Value;

        // Orden activa del tenant por PublicId (404) y que admita asignaciones (422).
        var order = await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == orderPublicId && o.IsActive)
                        .Select(o => new { o.TransportOrderId, o.IsActive, o.StatusCodeId }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException(OrderQueries.OrderLabel);
        var orderStatus = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == order.StatusCodeId)
            .Select(s => new { s.InternalCode, Kind = s.StageKind!.InternalCode }).FirstOrDefaultAsync(ct);
        if (!CrossDockRules.IsOrderShippable(order.IsActive, orderStatus?.InternalCode, orderStatus?.Kind))
            throw new StatusRuleException(CrossDockRules.OrderNotShippable);
        if (req.CargoLineId is int cargoLineId)
        {
            var ok = await db.CargoLines.AsNoTracking()
                .AnyAsync(c => c.CargoLineId == cargoLineId && c.TransportOrderId == order.TransportOrderId && c.IsActive, ct);
            if (!ok) throw new ValidationException("cargoLineId", CrossDockRules.CargoLineNotOfOrder);
        }
        await EnsureLineInPlanZoneAsync(plan, line.StagingBinId, ct);

        var planned = await statuses.GetInitialAsync(StatusDomains.AllocationStatus, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // Plan → ReceiptHeader → tareas → saldos.
            var p = await LockPlanAsync(planId, ct2);
            await EnsurePlanOpenAsync(p, ct2);
            var receipt = await LockReceiptAsync(line.ReceiptHeaderId, ct2);
            if (!receipt.IsActive) throw new StatusRuleException(CrossDockRules.ReceiptNotAllocatable);
            var l = await db.Set<ReceiptLine>().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.ReceiptLineId == lineId && x.ReceiptHeaderId == receipt.ReceiptHeaderId, ct2)
                    ?? throw new NotFoundException(LineLabel, feminine: true);
            var receiptCode = await CrossDockSupport.StatusCodeOfAsync(db, receipt.StatusCodeId, ct2);
            var committed = (await CommittedByLineAsync(new List<int> { l.ReceiptLineId }, ct2)).GetValueOrDefault(l.ReceiptLineId);

            var allocation = new CrossDockAllocation
            {
                CrossDockPlanId = p.CrossDockPlanId, ReceiptLineId = l.ReceiptLineId, TransportOrderId = order.TransportOrderId,
                CargoLineId = req.CargoLineId, AllocatedQty = qty, StatusCodeId = planned.StatusCodeId,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
            };

            if (ReceiptStatusRules.IsOpen(receiptCode))
            {
                // Modo (a): contra lo recibido de la línea abierta; se reparte al confirmar el recibo.
                var allocatable = CrossDockRules.AllocatableOpen(l.ReceivedQty, committed);
                if (qty > allocatable) throw new ConflictException(CrossDockRules.Exceeds(allocatable));
            }
            else if (ReceiptStatusRules.IsPosted(receiptCode))
            {
                // Modo (b): contra lo recibido no comprometido, limitado por la PUTAWAY pendiente (que se reduce).
                if (l.StagingBinId is not int stagingBin) throw new StatusRuleException(CrossDockRules.ReceiptNotAllocatable);
                var putaways = await LockPendingPutawaysAsync(receipt, l, ct2);
                var pendingQty = putaways.Sum(t => t.Quantity ?? 0m);
                var allocatable = CrossDockRules.AllocatableConfirmed(l.ReceivedQty, committed, pendingQty);
                if (qty > allocatable) throw new ConflictException(CrossDockRules.Exceeds(allocatable));
                await ReducePutawaysAsync(putaways, qty, p.Number, ct2);
                var serials = await ReservationSerialsAsync(l, receipt.WarehouseId, stagingBin, SerialStatuses.Available, qty, ct2);
                await ledger.ReserveAsync(new[] { new StockReservation(l.ProductId, receipt.WarehouseId, stagingBin, l.LotId, qty, serials) }, ct2);
                allocation.ConfirmedQty = qty;
            }
            else throw new StatusRuleException(CrossDockRules.ReceiptNotAllocatable);

            db.Set<CrossDockAllocation>().Add(allocation);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            var born = await statuses.TransitionAsync(StatusDomains.AllocationStatus, EntityTypes.CrossDockAllocation,
                allocation.CrossDockAllocationId, null, planned.InternalCode, null, ct2);
            allocation.StatusCodeId = born.StatusCodeId;

            if (allocation.ConfirmedQty is decimal confirmed && confirmed > 0m)
            {
                var task = await CrossDockSupport.CreateTaskAsync(db, taskWriter, CrossDockSupport.CrossDockTaskSpec(receipt.WarehouseId, l, allocation, confirmed), ct2);
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
                allocation.WarehouseTaskId = task.WarehouseTaskId;
            }

            await AdvancePlanToAllocatedAsync(p, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(planId, ct);
    }

    // ================================================================ cancelar asignación

    /// <summary>
    /// Cancela una asignación PLANNED (422 si ya se movió o canceló). Con mercancía confirmada: libera la reserva del
    /// staging, cancela su tarea CROSSDOCK y crea una PUTAWAY nueva por lo confirmado (R22). El comentario queda en el
    /// historial CROSSDOCK_ALLOCATION.
    /// </summary>
    public async Task<CrossDockPlanDto> CancelAllocationAsync(int planId, int allocationId, string? comment, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.WmsLotSerial, ct);
        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength })
            throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        var snap = await AllocationSnapshotAsync(planId, allocationId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await LockPlanAsync(planId, ct2);
            await EnsurePlanOpenAsync(p, ct2);
            var receipt = await LockReceiptAsync(snap.ReceiptHeaderId, ct2);
            var a = await TrackedAllocationAsync(allocationId, planId, ct2);
            await EnsurePlannedAsync(a, ct2);

            if (a.ConfirmedQty is decimal confirmed && confirmed > 0m)
            {
                var l = await db.Set<ReceiptLine>().AsNoTracking()
                            .FirstOrDefaultAsync(x => x.ReceiptLineId == a.ReceiptLineId && x.ReceiptHeaderId == receipt.ReceiptHeaderId, ct2)
                        ?? throw new NotFoundException(LineLabel, feminine: true);
                var task = a.WarehouseTaskId is int tid ? await LockTaskAsync(tid, ct2) : null;
                if (l.StagingBinId is int stagingBin)
                {
                    var serials = await ReservationSerialsAsync(l, receipt.WarehouseId, stagingBin, SerialStatuses.Reserved, confirmed, ct2);
                    await ledger.ReleaseAsync(new[] { new StockReservation(l.ProductId, receipt.WarehouseId, stagingBin, l.LotId, confirmed, serials) }, ct2);
                }
                if (task is not null) await CancelTaskIfOpenAsync(task, comment ?? $"Asignación {a.CrossDockAllocationId} cancelada.", ct2);

                // R22: lo confirmado regresa al flujo normal con una PUTAWAY nueva desde el staging.
                var suggestions = await suggester.SuggestAsync(receipt.WarehouseId, l.ProductId, l.LotId, confirmed, l.StagingBinId, 1, ct2);
                await CrossDockSupport.CreateTaskAsync(db, taskWriter, new WarehouseTaskSpec(
                    TaskType: WarehouseTaskTypes.Putaway, WarehouseId: receipt.WarehouseId, ProductId: l.ProductId, Quantity: confirmed,
                    LotId: l.LotId, FromBinId: l.StagingBinId, ToBinId: suggestions.Count == 0 ? null : suggestions[0].BinId,
                    RefEntityType: EntityTypes.Receipt, RefId: receipt.ReceiptHeaderId), ct2);
            }

            var to = await statuses.TransitionAsync(StatusDomains.AllocationStatus, EntityTypes.CrossDockAllocation, a.CrossDockAllocationId,
                a.StatusCodeId, AllocationStatuses.Cancelled, comment, ct2);
            a.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(planId, ct);
    }

    // ================================================================ mover

    /// <summary>Mueve una asignación desde la pantalla del plan: CROSSDOCK que sale del staging, tarea DONE y asignación MOVED.</summary>
    public async Task<CrossDockPlanDto> MoveAsync(int planId, int allocationId, CrossDockMoveRequest? req, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.WmsLotSerial, ct);
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength })
            throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        var snap = await AllocationSnapshotAsync(planId, allocationId, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await LockPlanAsync(planId, ct2);
            await EnsurePlanOpenAsync(p, ct2);
            await LockReceiptAsync(snap.ReceiptHeaderId, ct2);
            var a = await TrackedAllocationAsync(allocationId, planId, ct2);
            await MoveCoreAsync(a, closeTask: true, comment, ct2);
        }, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>
    /// Núcleo compartido con CrossDockTaskHandler. Precondición: el llamador ya bloqueó el plan y el recibo de la línea.
    /// Re-verifica PLANNED (422), recibo confirmado (422 ReceiptNotConfirmed) y mercancía confirmada (422 NothingConfirmed);
    /// asienta el CROSSDOCK desde el staging por ConfirmedQty con FromReserved (sale del inventario y libera la reserva),
    /// Ref CROSSDOCK_ALLOCATION; enlaza InventoryTransactionId y pasa la asignación a MOVED. Con closeTask la tarea
    /// CROSSDOCK se bloquea ANTES de los saldos y queda DONE; desde la cola la cierra WarehouseTaskService.
    /// Devuelve la cantidad movida.
    /// </summary>
    internal async Task<decimal> MoveCoreAsync(CrossDockAllocation a, bool closeTask, string? comment, CancellationToken ct)
    {
        await EnsurePlannedAsync(a, ct);
        var line = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                          join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                          where l.ReceiptLineId == a.ReceiptLineId
                          select l).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(LineLabel, feminine: true);
        // Tracked: la instancia ya bloqueada por el llamador.
        var receipt = await db.Set<ReceiptHeader>().FirstOrDefaultAsync(r => r.ReceiptHeaderId == line.ReceiptHeaderId, ct)
                      ?? throw new NotFoundException("Recibo");
        var receiptCode = await CrossDockSupport.StatusCodeOfAsync(db, receipt.StatusCodeId, ct);
        if (ReceiptStatusRules.IsOpen(receiptCode) || a.ConfirmedQty is null)
            throw new StatusRuleException(CrossDockRules.ReceiptNotConfirmed);
        var qty = a.ConfirmedQty.Value;
        if (qty <= 0m) throw new StatusRuleException(CrossDockRules.NothingConfirmed);
        if (line.StagingBinId is not int stagingBin) throw new StatusRuleException(CrossDockRules.StagingZone);

        var task = closeTask && a.WarehouseTaskId is int tid ? await LockTaskAsync(tid, ct) : null;

        var postings = await BuildMovePostingsAsync(a, line, receipt.WarehouseId, stagingBin, qty, ct);
        var txnIds = await ledger.PostAsync(postings, ct);
        a.InventoryTransactionId = txnIds.Count > 0 ? txnIds[0] : null;

        if (task is not null) await CompleteTaskIfOpenAsync(task, comment, ct);

        var moved = await statuses.TransitionAsync(StatusDomains.AllocationStatus, EntityTypes.CrossDockAllocation, a.CrossDockAllocationId,
            a.StatusCodeId, AllocationStatuses.Moved, comment, ct);
        a.StatusCodeId = moved.StatusCodeId;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return qty;
    }

    // ================================================================ completar

    /// <summary>Completa el plan (terminal). 422 si alguna asignación sigue PLANNED o si el plan ya estaba completado.</summary>
    public async Task<CrossDockPlanDto> CompleteAsync(int planId, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.WmsLotSerial, ct);
        await PlanSnapshotAsync(planId, ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await LockPlanAsync(planId, ct2);
            await EnsurePlanOpenAsync(p, ct2);
            var statusIds = await db.Set<CrossDockAllocation>().AsNoTracking().Where(a => a.CrossDockPlanId == p.CrossDockPlanId)
                .Select(a => a.StatusCodeId).ToListAsync(ct2);
            var codes = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId))
                .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct2);
            if (!CrossDockRules.CanComplete(statusIds.Select(id => codes.GetValueOrDefault(id))))
                throw new StatusRuleException(CrossDockRules.CannotComplete);
            var to = await statuses.TransitionAsync(StatusDomains.CrossDockStatus, EntityTypes.CrossDockPlan, p.CrossDockPlanId,
                p.StatusCodeId, CrossDockStatuses.Completed, null, ct2);
            p.StatusCodeId = to.StatusCodeId;
            p.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(planId, ct);
    }

    // ================================================================ apoyo para CrossDockTaskHandler

    /// <summary>Bloquea el plan y luego el recibo de la línea de la asignación (orden del lote). Sin asignación: no hace nada.</summary>
    internal async Task LockAllocationReferencesAsync(int allocationId, CancellationToken ct)
    {
        var refs = await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                          join p in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals p.CrossDockPlanId
                          join l in db.Set<ReceiptLine>().AsNoTracking() on a.ReceiptLineId equals l.ReceiptLineId
                          join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                          where a.CrossDockAllocationId == allocationId
                          select new { p.CrossDockPlanId, r.ReceiptHeaderId }).FirstOrDefaultAsync(ct);
        if (refs is null) return;
        await LockPlanAsync(refs.CrossDockPlanId, ct);
        await LockReceiptAsync(refs.ReceiptHeaderId, ct);
    }

    /// <summary>Asignación tracked por id, alcanzada por su plan filtrado (404 'Asignación no encontrada.').</summary>
    internal Task<CrossDockAllocation> TrackedAllocationAsync(int allocationId, CancellationToken ct)
        => TrackedAllocationAsync(allocationId, null, ct);

    // ================================================================ helpers

    private async Task<CrossDockPlan> PlanSnapshotAsync(int id, CancellationToken ct)
        => await db.Set<CrossDockPlan>().AsNoTracking().FirstOrDefaultAsync(p => p.CrossDockPlanId == id, ct)
           ?? throw new NotFoundException(PlanLabel);

    private async Task EnsurePlanOpenAsync(CrossDockPlan plan, CancellationToken ct)
    {
        var code = await CrossDockSupport.StatusCodeOfAsync(db, plan.StatusCodeId, ct);
        if (!CrossDockRules.IsPlanOpen(code)) throw new StatusRuleException(CrossDockRules.PlanNotOpen);
    }

    private async Task EnsurePlannedAsync(CrossDockAllocation a, CancellationToken ct)
    {
        var code = await CrossDockSupport.StatusCodeOfAsync(db, a.StatusCodeId, ct);
        if (!string.Equals(code, AllocationStatuses.Planned, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(CrossDockRules.AllocationNotPlanned);
    }

    private async Task AdvancePlanToAllocatedAsync(CrossDockPlan p, CancellationToken ct)
    {
        var code = await CrossDockSupport.StatusCodeOfAsync(db, p.StatusCodeId, ct);
        if (!string.Equals(code, CrossDockStatuses.Open, StringComparison.OrdinalIgnoreCase)) return;
        var to = await statuses.TransitionAsync(StatusDomains.CrossDockStatus, EntityTypes.CrossDockPlan, p.CrossDockPlanId,
            p.StatusCodeId, CrossDockStatuses.Allocated, null, ct);
        p.StatusCodeId = to.StatusCodeId;
    }

    private sealed record AllocationRefs(int AllocationId, int ReceiptLineId, int ReceiptHeaderId);

    /// <summary>Asignación del plan (404 sin oráculo) con el recibo de su línea, sin bloqueo.</summary>
    private async Task<AllocationRefs> AllocationSnapshotAsync(int planId, int allocationId, CancellationToken ct)
    {
        await PlanSnapshotAsync(planId, ct);
        return await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                      join p in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals p.CrossDockPlanId
                      join l in db.Set<ReceiptLine>().AsNoTracking() on a.ReceiptLineId equals l.ReceiptLineId
                      join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                      where a.CrossDockAllocationId == allocationId && a.CrossDockPlanId == planId
                      select new AllocationRefs(a.CrossDockAllocationId, l.ReceiptLineId, r.ReceiptHeaderId)).FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException(AllocationLabel, feminine: true);
    }

    private async Task<CrossDockAllocation> TrackedAllocationAsync(int allocationId, int? planId, CancellationToken ct)
        => await (from a in db.Set<CrossDockAllocation>()
                  join p in db.Set<CrossDockPlan>() on a.CrossDockPlanId equals p.CrossDockPlanId
                  where a.CrossDockAllocationId == allocationId && (planId == null || a.CrossDockPlanId == planId)
                  select a).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(AllocationLabel, feminine: true);

    private sealed record LineRef(int ReceiptLineId, int ReceiptHeaderId, int ProductId, int? StagingBinId);

    /// <summary>Línea de un recibo activo del almacén del plan (hija sin TenantId, por su recibo filtrado; si no → 404).</summary>
    private async Task<LineRef> ResolveLineAsync(int warehouseId, int lineId, CancellationToken ct)
        => await (from l in db.Set<ReceiptLine>().AsNoTracking()
                  join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                  where l.ReceiptLineId == lineId && r.WarehouseId == warehouseId && r.IsActive
                  select new LineRef(l.ReceiptLineId, l.ReceiptHeaderId, l.ProductId, l.StagingBinId)).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(LineLabel, feminine: true);

    private async Task<string> TrackingCodeAsync(int productId, CancellationToken ct)
    {
        var trackingId = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == productId)
            .Select(p => (int?)p.TrackingTypeLookupId).FirstOrDefaultAsync(ct);
        return trackingId is int t ? (await lookups.GetAsync(t, ct))?.InternalCode ?? TrackingTypes.None : TrackingTypes.None;
    }

    /// <summary>Si el plan fija zona de staging, la posición de recepción de la línea debe estar en ella (400 StagingZone).</summary>
    private async Task EnsureLineInPlanZoneAsync(CrossDockPlan plan, int? stagingBinId, CancellationToken ct)
    {
        if (plan.StagingZoneId is not int zoneId || stagingBinId is not int binId) return;
        var binZone = await CrossDockSupport.BinZoneAsync(db, plan.WarehouseId, binId, ct);
        if (binZone != zoneId) throw new ValidationException("receiptLineId", CrossDockRules.StagingZone);
    }

    /// <summary>
    /// Comprometido por línea: Σ de las asignaciones no canceladas (PLANNED y MOVED) — la confirmada si ya se repartió, si
    /// no la asignada.
    /// </summary>
    private async Task<Dictionary<int, decimal>> CommittedByLineAsync(List<int> lineIds, CancellationToken ct)
    {
        if (lineIds.Count == 0) return new Dictionary<int, decimal>();
        var cancelledId = await db.StatusIdAsync(StatusDomains.AllocationStatus, AllocationStatuses.Cancelled, ct);
        var rows = await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                          join p in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals p.CrossDockPlanId
                          where lineIds.Contains(a.ReceiptLineId) && a.StatusCodeId != cancelledId
                          select new { a.ReceiptLineId, a.AllocatedQty, a.ConfirmedQty }).ToListAsync(ct);
        return rows.GroupBy(x => x.ReceiptLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.ConfirmedQty ?? x.AllocatedQty));
    }

    /// <summary>PUTAWAY abiertas por (recibo, producto, lote, posición de origen) de los recibos indicados.</summary>
    private async Task<Dictionary<(int ReceiptId, int ProductId, int? LotId, int? BinId), decimal>> PendingPutawayAsync(
        List<int> receiptIds, CancellationToken ct)
    {
        var result = new Dictionary<(int, int, int?, int?), decimal>();
        if (receiptIds.Count == 0) return result;
        var (putawayTypeId, receiptRefId, openIds) = await PutawayFilterAsync(ct);
        var rows = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.TaskTypeLookupId == putawayTypeId && t.RefEntityLookupId == receiptRefId && t.RefId != null
                        && receiptIds.Contains(t.RefId.Value) && openIds.Contains(t.StatusCodeId) && t.ProductId != null)
            .Select(t => new { RefId = t.RefId!.Value, ProductId = t.ProductId!.Value, t.LotId, t.FromBinId, t.Quantity })
            .ToListAsync(ct);
        foreach (var g in rows.GroupBy(x => (x.RefId, x.ProductId, x.LotId, x.FromBinId)))
            result[g.Key] = g.Sum(x => x.Quantity ?? 0m);
        return result;
    }

    private async Task<(int PutawayTypeId, int ReceiptRefId, List<int> OpenIds)> PutawayFilterAsync(CancellationToken ct)
    {
        var putawayTypeId = await lookups.GetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway, ct);
        var receiptRefId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);
        var openIds = await CrossDockSupport.OpenTaskStatusIdsAsync(db, ct);
        return (putawayTypeId, receiptRefId, openIds);
    }

    /// <summary>PUTAWAY abiertas de la mercancía de la línea (mismo recibo, producto, lote y staging), bloqueadas en orden de id.</summary>
    private async Task<List<WarehouseTask>> LockPendingPutawaysAsync(ReceiptHeader receipt, ReceiptLine line, CancellationToken ct)
    {
        var (putawayTypeId, receiptRefId, openIds) = await PutawayFilterAsync(ct);
        var receiptId = receipt.ReceiptHeaderId;
        var productId = line.ProductId;
        var lotId = line.LotId;
        var binId = line.StagingBinId;
        var ids = await db.Set<WarehouseTask>().AsNoTracking()
            .Where(t => t.TaskTypeLookupId == putawayTypeId && t.RefEntityLookupId == receiptRefId && t.RefId == receiptId
                        && t.WarehouseId == receipt.WarehouseId && t.ProductId == productId && t.LotId == lotId && t.FromBinId == binId
                        && openIds.Contains(t.StatusCodeId))
            .OrderBy(t => t.WarehouseTaskId).Select(t => t.WarehouseTaskId).ToListAsync(ct);
        var locked = new List<WarehouseTask>(ids.Count);
        foreach (var id in ids)
        {
            var task = await LockTaskAsync(id, ct);
            if (openIds.Contains(task.StatusCodeId)) locked.Add(task);
        }
        return locked;
    }

    /// <summary>
    /// Reduce las PUTAWAY por la cantidad asignada (en orden). Una que llega a 0 se CANCELA sin tocar su cantidad
    /// (CK_WarehouseTask_Qty) con comentario; el efecto de tareas cierra el recibo si era la última.
    /// </summary>
    private async Task ReducePutawaysAsync(List<WarehouseTask> putaways, decimal qty, string planNumber, CancellationToken ct)
    {
        var plan = CrossDockRules.ReducePlan(putaways.Select(t => t.Quantity ?? 0m).ToList(), qty);
        foreach (var (index, reduce) in plan)
        {
            var task = putaways[index];
            var current = task.Quantity ?? 0m;
            if (reduce >= current) await CancelTaskIfOpenAsync(task, $"Asignada a cruce de muelle {planNumber}.", ct);
            else task.Quantity = current - reduce;
        }
    }

    private async Task CancelTaskIfOpenAsync(WarehouseTask task, string? comment, CancellationToken ct)
    {
        var code = await CrossDockSupport.StatusCodeOfAsync(db, task.StatusCodeId, ct);
        if (!IsOpenTask(code)) return;
        var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
            task.StatusCodeId, WarehouseTaskStatuses.Cancelled, comment, ct);
        task.StatusCodeId = to.StatusCodeId;
        task.CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>DONE escalonado (el pipeline no admite saltos): PENDING → IN_PROGRESS → DONE, con historial.</summary>
    private async Task CompleteTaskIfOpenAsync(WarehouseTask task, string? comment, CancellationToken ct)
    {
        var code = await CrossDockSupport.StatusCodeOfAsync(db, task.StatusCodeId, ct);
        if (!IsOpenTask(code)) return;
        if (string.Equals(code, WarehouseTaskStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            var started = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, WarehouseTaskStatuses.InProgress, null, ct);
            task.StatusCodeId = started.StatusCodeId;
        }
        var done = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
            task.StatusCodeId, WarehouseTaskStatuses.Done, comment, ct);
        task.StatusCodeId = done.StatusCodeId;
        task.CompletedAtUtc = DateTime.UtcNow;
        task.AssignedToUserId ??= tenant.UserId;
    }

    private static bool IsOpenTask(string? code)
        => string.Equals(code, WarehouseTaskStatuses.Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(code, WarehouseTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Asientos del movimiento: uno por la cantidad confirmada (con el lote de la línea) o, en productos con serie, uno por
    /// serie disponible en el staging (primero las recibidas en la línea, luego otras del mismo producto y lote en esa posición).
    /// </summary>
    private async Task<List<InventoryPosting>> BuildMovePostingsAsync(CrossDockAllocation a, ReceiptLine line, int warehouseId,
        int stagingBin, decimal qty, CancellationToken ct)
    {
        var tracking = await TrackingCodeAsync(line.ProductId, ct);
        if (!string.Equals(tracking, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase))
            return new List<InventoryPosting>
            {
                new(InventoryTxnTypes.CrossDock, line.ProductId, qty, LotId: line.LotId,
                    FromWarehouseId: warehouseId, FromBinId: stagingBin,
                    RefEntityType: EntityTypes.CrossDockAllocation, RefId: a.CrossDockAllocationId, FromReserved: true),
            };

        // Las series salen RESERVED (las reservó la asignación, L328).
        var productId = line.ProductId;
        var ordered = await CrossDockSupport.SerialsInBinAsync(db, line, warehouseId, stagingBin, SerialStatuses.Reserved, qty, ct);
        if (ordered.Count < qty) throw new StatusRuleException(CrossDockRules.SerialsNotInStaging);
        return ordered.Select(s => new InventoryPosting(InventoryTxnTypes.CrossDock, productId, 1m, LotId: s.LotId,
            SerialId: s.SerialId, SerialNumber: s.SerialNumber, FromWarehouseId: warehouseId, FromBinId: stagingBin,
            RefEntityType: EntityTypes.CrossDockAllocation, RefId: a.CrossDockAllocationId, FromReserved: true)).ToList();
    }

    /// <summary>
    /// Series que nombra una reserva o liberación de la línea (L328): null si el producto no es SERIAL; si no, las primeras
    /// <paramref name="qty"/> series de la posición de staging con el estatus dado (primero las recibidas en la línea).
    /// Menos series que la cantidad → 422 SerialsNotInStaging.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ReservationSerialsAsync(ReceiptLine line, int warehouseId, int stagingBin, string status,
        decimal qty, CancellationToken ct)
    {
        var tracking = await TrackingCodeAsync(line.ProductId, ct);
        if (!string.Equals(tracking, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase)) return null;
        var serials = await CrossDockSupport.SerialsInBinAsync(db, line, warehouseId, stagingBin, status, qty, ct);
        if (serials.Count < qty) throw new StatusRuleException(CrossDockRules.SerialsNotInStaging);
        return serials.Select(s => s.SerialNumber).ToList();
    }

    private async Task<Dictionary<int, string>> LotNumbersAsync(IEnumerable<int?> lotIds, List<int> productIds, CancellationToken ct)
    {
        var ids = lotIds.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await (from l in db.Set<InventoryLot>().AsNoTracking()
                      join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                      where ids.Contains(l.LotId) && productIds.Contains(l.ProductId)
                      select new { l.LotId, l.LotNumber }).ToDictionaryAsync(x => x.LotId, x => x.LotNumber, ct);
    }

    private async Task<Dictionary<int, string>> BinCodesAsync(int warehouseId, IEnumerable<int?> binIds, CancellationToken ct)
    {
        var ids = binIds.Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await (from b in db.Set<WarehouseBin>().AsNoTracking()
                      join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                      where ids.Contains(b.WarehouseBinId) && b.WarehouseId == warehouseId
                      select new { b.WarehouseBinId, b.Code }).ToDictionaryAsync(x => x.WarehouseBinId, x => x.Code, ct);
    }

    /// <summary>DTOs de planes por lotes (sin N+1): almacén, zona, asignaciones con recibo, producto, lote, orden y cliente.</summary>
    private async Task<IReadOnlyList<CrossDockPlanDto>> BuildAsync(IReadOnlyList<CrossDockPlan> plans, CancellationToken ct)
    {
        if (plans.Count == 0) return Array.Empty<CrossDockPlanDto>();
        var planIds = plans.Select(p => p.CrossDockPlanId).ToList();
        var whIds = plans.Select(p => p.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var zoneIds = plans.Where(p => p.StagingZoneId != null).Select(p => p.StagingZoneId!.Value).Distinct().ToList();
        var zones = await db.Set<WarehouseZone>().AsNoTracking().Where(z => zoneIds.Contains(z.WarehouseZoneId) && whIds.Contains(z.WarehouseId))
            .ToDictionaryAsync(z => z.WarehouseZoneId, z => z.Code, ct);

        var allocations = await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                                 join p in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals p.CrossDockPlanId
                                 where planIds.Contains(a.CrossDockPlanId)
                                 orderby a.CreatedAtUtc, a.CrossDockAllocationId
                                 select a).ToListAsync(ct);
        var lineIds = allocations.Select(a => a.ReceiptLineId).Distinct().ToList();
        var lines = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           join r in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals r.ReceiptHeaderId
                           where lineIds.Contains(l.ReceiptLineId)
                           select new { l.ReceiptLineId, l.ProductId, l.LotId, r.Number, r.StatusCodeId })
            .ToDictionaryAsync(x => x.ReceiptLineId, ct);
        var productIds = lines.Values.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku }).ToDictionaryAsync(p => p.ProductId, ct);
        var lots = await LotNumbersAsync(lines.Values.Select(x => x.LotId), productIds, ct);
        var orderIds = allocations.Select(a => a.TransportOrderId).Distinct().ToList();
        var orders = await (from o in db.TransportOrders.AsNoTracking()
                            join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId
                            where orderIds.Contains(o.TransportOrderId)
                            select new { o.TransportOrderId, o.PublicId, o.PackBatchNumber, ClientName = c.Name })
            .ToDictionaryAsync(o => o.TransportOrderId, ct);
        var planStatus = await CrossDockSupport.StatusMapAsync(db, tenant, StatusDomains.CrossDockStatus, ct);
        var allocStatus = await CrossDockSupport.StatusMapAsync(db, tenant, StatusDomains.AllocationStatus, ct);
        var receiptStatus = await CrossDockSupport.StatusMapAsync(db, tenant, StatusDomains.ReceiptStatus, ct);

        var result = new List<CrossDockPlanDto>(plans.Count);
        foreach (var p in plans)
        {
            var items = new List<CrossDockAllocationDto>();
            decimal allocated = 0m, movedQty = 0m, shortQty = 0m;
            var count = 0;
            foreach (var a in allocations.Where(x => x.CrossDockPlanId == p.CrossDockPlanId))
            {
                var s = allocStatus.GetValueOrDefault(a.StatusCodeId);
                var cancelled = string.Equals(s?.Code, AllocationStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
                var moved = string.Equals(s?.Code, AllocationStatuses.Moved, StringComparison.OrdinalIgnoreCase);
                var shortage = cancelled ? 0m : CrossDockRules.ShortQty(a.AllocatedQty, a.ConfirmedQty);
                if (!cancelled)
                {
                    count++;
                    allocated += a.AllocatedQty;
                    shortQty += shortage;
                    if (moved) movedQty += a.ConfirmedQty ?? 0m;
                }
                var line = lines.GetValueOrDefault(a.ReceiptLineId);
                var product = line is null ? null : products.GetValueOrDefault(line.ProductId);
                var order = orders.GetValueOrDefault(a.TransportOrderId);
                items.Add(new CrossDockAllocationDto(a.CrossDockAllocationId, a.ReceiptLineId, line?.Number ?? string.Empty,
                    line is null ? string.Empty : receiptStatus.GetValueOrDefault(line.StatusCodeId)?.Code ?? string.Empty,
                    product?.PublicId ?? Guid.Empty, product?.Sku ?? string.Empty,
                    line?.LotId is int lid ? lots.GetValueOrDefault(lid) : null,
                    order?.PublicId ?? Guid.Empty, order?.PackBatchNumber ?? string.Empty, order?.ClientName ?? string.Empty,
                    a.AllocatedQty, a.ConfirmedQty, shortage, s?.Code ?? string.Empty, s?.Label ?? string.Empty,
                    a.WarehouseTaskId, a.InventoryTransactionId));
            }
            var w = warehouses.GetValueOrDefault(p.WarehouseId);
            var ps = planStatus.GetValueOrDefault(p.StatusCodeId);
            result.Add(new CrossDockPlanDto(p.CrossDockPlanId, p.Number, w?.PublicId ?? Guid.Empty, w?.Code ?? string.Empty,
                p.StagingZoneId, p.StagingZoneId is int zid ? zones.GetValueOrDefault(zid) : null,
                ps?.Code ?? string.Empty, ps?.Label ?? string.Empty, count, allocated, movedQty, shortQty,
                p.CreatedAtUtc, p.CompletedAtUtc, items));
        }
        return result;
    }

    // ================================================================ adaptadores a las costuras de P0 (InventoryQueries)
    // Toda dependencia de las firmas de InventoryQueries de esta pieza vive aquí.

    /// <summary>Encabezado CrossDockPlan con UPDLOCK, tracked (antes del recibo en el orden del lote). De otro tenant → 404.</summary>
    private Task<CrossDockPlan> LockPlanAsync(int planId, CancellationToken ct) => db.LockCrossDockPlanAsync(planId, ct);

    /// <summary>Encabezado ReceiptHeader con UPDLOCK, tracked (después del plan, antes de las tareas y los saldos).</summary>
    private Task<ReceiptHeader> LockReceiptAsync(int receiptHeaderId, CancellationToken ct) => db.LockReceiptAsync(receiptHeaderId, ct);

    /// <summary>Tarea con UPDLOCK, tracked (después de los encabezados, antes de los saldos).</summary>
    private Task<WarehouseTask> LockTaskAsync(int taskId, CancellationToken ct) => db.LockWarehouseTaskAsync(taskId, ct);
}

/// <summary>
/// Lote 6 (P9) — apoyo compartido por CrossDockService, CrossDockReceiptParticipant y CrossDockTaskHandler: estatus con
/// etiqueta del tenant, tareas CROSSDOCK vía WarehouseTaskWriter (única vía de alta) y lecturas por padre filtrado.
/// </summary>
internal static class CrossDockSupport
{
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record StatusInfo(string Code, string Label, string? Color);

    public static async Task<Dictionary<int, StatusInfo>> StatusMapAsync(TeikemDbContext db, ITenantContext tenant, string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c =>
        {
            var o = overrides.GetValueOrDefault(c.StatusCodeId);
            return new StatusInfo(c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang),
                o?.CustomColorHex ?? c.ColorHex);
        });
    }

    public static async Task<string?> StatusCodeOfAsync(TeikemDbContext db, int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct);

    public static async Task<List<int>> OpenTaskStatusIdsAsync(TeikemDbContext db, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus
                        && (s.InternalCode == WarehouseTaskStatuses.Pending || s.InternalCode == WarehouseTaskStatuses.InProgress))
            .Select(s => s.StatusCodeId).ToListAsync(ct);

    /// <summary>Zona de una posición del almacén (hija sin TenantId, por su almacén filtrado); null si no existe.</summary>
    public static async Task<int?> BinZoneAsync(TeikemDbContext db, int warehouseId, int binId, CancellationToken ct)
        => await (from b in db.Set<WarehouseBin>().AsNoTracking()
                  join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                  where b.WarehouseBinId == binId && b.WarehouseId == warehouseId
                  select (int?)b.WarehouseZoneId).FirstOrDefaultAsync(ct);

    /// <summary>Tarea CROSSDOCK de una asignación: desde el staging de la línea por lo confirmado, Ref CROSSDOCK_ALLOCATION.</summary>
    public static WarehouseTaskSpec CrossDockTaskSpec(int warehouseId, ReceiptLine line, CrossDockAllocation allocation, decimal qty)
        => new(TaskType: WarehouseTaskTypes.CrossDock, WarehouseId: warehouseId, ProductId: line.ProductId, Quantity: qty,
            LotId: line.LotId, FromBinId: line.StagingBinId, ToBinId: null,
            RefEntityType: EntityTypes.CrossDockAllocation, RefId: allocation.CrossDockAllocationId);

    /// <summary>
    /// Crea la tarea con WarehouseTaskWriter (historial null → PENDING) y devuelve la entidad nueva que quedó en el
    /// ChangeTracker (el llamador guarda y toma su id).
    /// </summary>
    public static async Task<WarehouseTask> CreateTaskAsync(TeikemDbContext db, WarehouseTaskWriter writer, WarehouseTaskSpec spec, CancellationToken ct)
    {
        var before = new HashSet<object>(db.ChangeTracker.Entries<WarehouseTask>().Select(e => (object)e.Entity), ReferenceEqualityComparer.Instance);
        await writer.CreateAsync(spec, ct);
        return db.ChangeTracker.Entries<WarehouseTask>().Select(e => e.Entity).Single(t => !before.Contains(t));
    }

    /// <summary>Almacén por PublicId (404) o, sin él, el único activo del tenant (ninguno → 422; más de uno → 400).</summary>
    public static Task<Warehouse> ResolveWarehouseOrDefaultAsync(TeikemDbContext db, Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    public sealed record SerialInBin(int SerialId, string SerialNumber, int? LotId);

    /// <summary>
    /// Hasta <paramref name="qty"/> series del producto y lote de la línea en la posición, con el estatus dado; primero las
    /// recibidas en la línea (en su orden), luego las demás por número. Hijas sin TenantId: por su producto filtrado.
    /// </summary>
    public static async Task<List<SerialInBin>> SerialsInBinAsync(TeikemDbContext db, ReceiptLine line, int warehouseId, int binId,
        string status, decimal qty, CancellationToken ct)
    {
        var statusId = await db.StatusIdAsync(StatusDomains.SerialStatus, status, ct);
        var productId = line.ProductId;
        var lotId = line.LotId;
        var inBin = await (from s in db.Set<InventorySerial>().AsNoTracking()
                           join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                           where s.ProductId == productId && s.CurrentWarehouseId == warehouseId && s.CurrentBinId == binId
                                 && s.StatusCodeId == statusId && (lotId == null || s.LotId == lotId)
                           orderby s.SerialNumber
                           select new SerialInBin(s.SerialId, s.SerialNumber, s.LotId)).ToListAsync(ct);
        var preferred = ParseSerials(line.SerialNumbersJson);
        return inBin
            .OrderBy(s =>
            {
                var i = preferred.FindIndex(n => string.Equals(n, s.SerialNumber, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(s => s.SerialNumber, StringComparer.OrdinalIgnoreCase)
            .Take((int)qty).ToList();
    }

    public static List<string> ParseSerials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    public static List<string> Codes(IEnumerable<string> raw)
        => raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
}
