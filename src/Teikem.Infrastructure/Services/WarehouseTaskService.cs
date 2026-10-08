using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P5) — cola unificada de tareas de almacén (R17, D41). Cada tipo se completa con su IWarehouseTaskHandler
/// (PUTAWAY/REPLENISH aquí, COUNT en P6, CROSSDOCK en P9); el handler declara su permiso y si se completa desde la cola.
/// Un tipo sin handler (PICK, PACK, LOAD) responde 422 'Las tareas de tipo {tipo} no se completan desde la cola.'.
/// - Lista y ficha con inventory.view (el controlador). CompletableFromQueue = hay handler y no pide otra pantalla.
/// - Asignar y cancelar: warehouse.manage (el controlador); el asignado debe ser miembro ACTIVO del tenant (400).
/// - Iniciar y completar: inventory.view en el controlador + el permiso del handler aquí (403 con PERMISSION_DENIED).
/// - Completar (se permite sin iniciar), dentro de RunInTransactionAsync y con el ORDEN DE BLOQUEO del lote:
///   1. snapshot sin bloqueo; 2. handler (422 si no hay o si se completa en otra pantalla); 3. permiso del handler;
///   4. handler.LockReferencesAsync (encabezados: recibo, plan...); 5. bloqueo de la tarea; 6. re-verificación de que sigue
///   abierta (el segundo de dos completados concurrentes recibe 422); 7. movimiento del handler; 8. remanente como tarea
///   nueva con el mismo Ref si se completó menos; 9. transición escalonada a DONE (PENDING → IN_PROGRESS → DONE).
/// - Cancelar desde la cola solo PUTAWAY y REPLENISH (sin efectos que deshacer); COUNT se cancela al eliminar el conteo y
///   CROSSDOCK al cancelar su asignación (libera la reserva).
/// El TenantId sale del principal: toda tarea se alcanza bajo el filtro global; las posiciones, lotes y series (hijas sin
/// TenantId) se leen por su almacén o producto ya filtrado.
/// </summary>
public sealed class WarehouseTaskService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    PermissionService permissions,
    WarehouseTaskWriter taskWriter,
    PutawaySuggester suggester,
    IEnumerable<IWarehouseTaskHandler> handlers)
{
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";

    // ================================================================ lectura

    public async Task<WarehouseTaskPageDto> ListAsync(WarehouseTaskQuery? q, CancellationToken ct)
    {
        q ??= new WarehouseTaskQuery();
        var (skip, take) = WarehouseTaskRules.Page(q.Skip, q.Take);
        var query = db.Set<WarehouseTask>().AsNoTracking();

        if (q.WarehousePublicId is Guid wh)
        {
            var warehouseId = await db.Set<Warehouse>().AsNoTracking().Where(w => w.PublicId == wh)
                                  .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct)
                              ?? throw new NotFoundException("Almacén");
            query = query.Where(t => t.WarehouseId == warehouseId);
        }
        if (q.Types is { Length: > 0 })
        {
            var codes = Codes(q.Types);
            var ids = await db.LookupCodes.AsNoTracking()
                .Where(l => l.Entity == LookupDomains.WarehouseTaskType && codes.Contains(l.InternalCode))
                .Select(l => l.LookupCodeId).ToListAsync(ct);
            query = query.Where(t => ids.Contains(t.TaskTypeLookupId));
        }
        // Estatus: los indicados; sin filtro, solo las abiertas salvo includeClosed.
        IReadOnlyList<string>? statusCodes = q.Status is { Length: > 0 } ? Codes(q.Status)
            : q.IncludeClosed ? null : WarehouseTaskRules.OpenStatuses;
        if (statusCodes is not null)
        {
            var codes = statusCodes.ToList();
            var ids = await db.StatusCodes.AsNoTracking()
                .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus && codes.Contains(s.InternalCode))
                .Select(s => s.StatusCodeId).ToListAsync(ct);
            query = query.Where(t => ids.Contains(t.StatusCodeId));
        }
        if (q.AssignedToMe)
        {
            var me = tenant.UserId ?? -1;
            query = query.Where(t => t.AssignedToUserId == me);
        }
        if (q.AssignedUserId is int assigned) query = query.Where(t => t.AssignedToUserId == assigned);

        var total = await query.CountAsync(ct);
        // Orden de la cola (WarehouseTaskRules.QueueOrder): prioridad, antigüedad, id.
        var page = await query.OrderBy(t => t.Priority).ThenBy(t => t.CreatedAtUtc).ThenBy(t => t.WarehouseTaskId)
            .Skip(skip).Take(take).ToListAsync(ct);
        var items = await WarehouseTaskReads.BuildAsync(db, tenant, lookups, handlers, page, ct);
        return new WarehouseTaskPageDto(total, skip, take, items);
    }

    public async Task<WarehouseTaskDto> GetAsync(int id, CancellationToken ct)
    {
        var task = await SnapshotAsync(id, ct);
        return (await WarehouseTaskReads.BuildAsync(db, tenant, lookups, handlers, new List<WarehouseTask> { task }, ct))[0];
    }

    // ================================================================ asignar

    /// <summary>Asigna (o desasigna con userId null) una tarea abierta. El usuario debe ser miembro ACTIVO del tenant.</summary>
    public async Task<WarehouseTaskDto> AssignAsync(int id, TaskAssignRequest? req, CancellationToken ct)
    {
        var userId = req?.UserId;
        await SnapshotAsync(id, ct);
        if (userId is int uid)
        {
            // UserTenant está bajo el filtro de tenant: un usuario de otra compañía no aparece (400, sin oráculo).
            var activeId = await db.StatusIdAsync(StatusDomains.MembershipStatus, MembershipStatuses.Active, ct);
            var member = await db.UserTenants.AsNoTracking().AnyAsync(m => m.UserId == uid && m.StatusCodeId == activeId, ct);
            if (!member) throw new ValidationException("userId", WarehouseTaskRules.AssigneeNotMember);
        }

        await db.RunInTransactionAsync(async ct2 =>
        {
            var task = await LockTaskAsync(id, ct2);
            await EnsureOpenAsync(task, ct2);
            task.AssignedToUserId = userId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ iniciar

    /// <summary>
    /// PENDING → IN_PROGRESS con el permiso del handler (403 si falta; 422 si el tipo no tiene handler). Si la tarea no tenía
    /// asignado, queda asignada a quien la inicia. Iniciar una COUNT desde la cola se permite (se completa en Conteo cíclico).
    /// </summary>
    public async Task<WarehouseTaskDto> StartAsync(int id, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(id, ct);
        var type = await TypeCodeAsync(snapshot, ct);
        var handler = HandlerFor(type) ?? throw new StatusRuleException(WarehouseTaskRules.NoHandler(type));
        await permissions.EnsureAsync(handler.RequiredPermission, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            await handler.LockReferencesAsync(snapshot, ct2);
            var task = await LockTaskAsync(id, ct2);
            var code = await EnsureOpenAsync(task, ct2);
            if (string.Equals(code, WarehouseTaskStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
                throw new StatusRuleException(WarehouseTaskRules.AlreadyInProgress);
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, WarehouseTaskStatuses.InProgress, null, ct2);
            task.StatusCodeId = to.StatusCodeId;
            task.AssignedToUserId ??= tenant.UserId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ completar

    public async Task<WarehouseTaskDto> CompleteAsync(int id, TaskCompleteRequest? req, CancellationToken ct)
    {
        req ??= new TaskCompleteRequest();
        if (req.Comment is { Length: > StatusService.CommentMaxLength })
            throw new ValidationException("comment", StatusService.CommentTooLongMessage);

        // 1-3. Snapshot sin bloqueo, handler y su permiso.
        var snapshot = await SnapshotAsync(id, ct);
        var type = await TypeCodeAsync(snapshot, ct);
        var handler = HandlerFor(type) ?? throw new StatusRuleException(WarehouseTaskRules.NoHandler(type));
        if (handler.NotFromQueueMessage is string notFromQueue) throw new StatusRuleException(notFromQueue);
        await permissions.EnsureAsync(handler.RequiredPermission, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            // 4-6. Encabezados del documento, la tarea y re-verificación bajo bloqueo.
            await handler.LockReferencesAsync(snapshot, ct2);
            var task = await LockTaskAsync(id, ct2);
            var code = await EnsureOpenAsync(task, ct2);
            var taskQty = task.Quantity;
            var suggestedTo = task.ToBinId;

            // 7. Movimiento del handler (devuelve lo completado).
            var completed = await handler.CompleteAsync(task, req, ct2);

            // 8. Remanente como tarea nueva con el mismo Ref (antes del DONE: el efecto la ve abierta y no cierra el recibo).
            if (taskQty is decimal tq)
            {
                if (completed <= 0m || completed > tq) throw new ValidationException("quantity", WarehouseTaskRules.QtyExceeds);
                var remainder = WarehouseTaskRules.Split(tq, completed);
                if (remainder > 0m)
                {
                    task.Quantity = completed;
                    await CreateRemainderAsync(task, remainder, suggestedTo, ct2);
                }
            }

            // 9. DONE escalonado.
            await AdvanceToDoneAsync(task, code, req.Comment, ct2);
            task.CompletedAtUtc = DateTime.UtcNow;
            task.AssignedToUserId ??= tenant.UserId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ repartir por posición

    /// <summary>
    /// Acomodo repartido: la misma cantidad en cada posición dada (un TRANSFER por posición) dentro de UNA transacción; si algo
    /// falla no se mueve nada. El remanente (lo que no cupo en posiciones llenas) queda como tarea nueva, igual que un completado
    /// parcial. Solo PUTAWAY y sin series (400/422 con los mensajes de WarehouseTaskRules).
    /// </summary>
    public async Task<WarehouseTaskDto> DistributeAsync(int id, TaskDistributeRequest? req, CancellationToken ct)
    {
        req ??= new TaskDistributeRequest(0m);
        if (req.Comment is { Length: > StatusService.CommentMaxLength })
            throw new ValidationException("comment", StatusService.CommentTooLongMessage);
        var binIds = req.ToBinIds ?? Array.Empty<int>();
        if (binIds.Distinct().Count() != binIds.Count) throw new ValidationException("toBinIds", WarehouseTaskRules.DistributeBinsDuplicated);

        var snapshot = await SnapshotAsync(id, ct);
        var type = await TypeCodeAsync(snapshot, ct);
        if (!string.Equals(type, WarehouseTaskTypes.Putaway, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(WarehouseTaskRules.DistributeOnlyPutaway);
        var handler = HandlerFor(type) ?? throw new StatusRuleException(WarehouseTaskRules.NoHandler(type));
        await permissions.EnsureAsync(handler.RequiredPermission, ct);

        await db.RunInTransactionAsync(async ct2 =>
        {
            await handler.LockReferencesAsync(snapshot, ct2);
            var task = await LockTaskAsync(id, ct2);
            var code = await EnsureOpenAsync(task, ct2);
            var taskQty = task.Quantity;
            var suggestedTo = task.ToBinId;

            if (task.ProductId is int productId)
            {
                var trackingId = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == productId)
                    .Select(p => p.TrackingTypeLookupId).FirstOrDefaultAsync(ct2);
                var tracking = (await lookups.GetAsync(trackingId, ct2))?.InternalCode;
                if (string.Equals(tracking, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase))
                    throw new StatusRuleException(WarehouseTaskRules.DistributeNoSerials);
            }

            var (chunks, planError) = WarehouseTaskRules.DistributionPlan(taskQty, req.QuantityPerBin, binIds.Count);
            if (planError is not null)
                throw planError == WarehouseTaskRules.QuantityMissing ? new StatusRuleException(planError) : new ValidationException("quantityPerBin", planError);

            // Todas las posiciones deben ser del almacén de la tarea ANTES de mover nada (otra → 404 sin oráculo, como al completar).
            var inWarehouse = await db.Set<WarehouseBin>().AsNoTracking()
                .Where(b => binIds.Contains(b.WarehouseBinId) && b.WarehouseId == task.WarehouseId)
                .Select(b => b.WarehouseBinId).ToListAsync(ct2);
            if (inWarehouse.Count != binIds.Count) throw new NotFoundException("Posición", feminine: true);

            decimal moved = 0m;
            for (var i = 0; i < binIds.Count; i++)
                moved += await handler.CompleteAsync(task, new TaskCompleteRequest(ToBinId: binIds[i], Quantity: chunks[i]), ct2);

            var tq = taskQty!.Value;
            var remainder = WarehouseTaskRules.Split(tq, moved);
            if (remainder > 0m)
            {
                task.Quantity = moved;
                await CreateRemainderAsync(task, remainder, suggestedTo, ct2);
            }
            await AdvanceToDoneAsync(task, code, req.Comment, ct2);
            task.CompletedAtUtc = DateTime.UtcNow;
            task.AssignedToUserId ??= tenant.UserId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ cancelar

    public async Task<WarehouseTaskDto> CancelAsync(int id, TaskCancelRequest? req, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        var snapshot = await SnapshotAsync(id, ct);
        var type = await TypeCodeAsync(snapshot, ct);
        if (!WarehouseTaskRules.IsCancelableFromQueue(type)) throw new StatusRuleException(WarehouseTaskRules.CancelNotFromQueue(type));
        var handler = HandlerFor(type);

        await db.RunInTransactionAsync(async ct2 =>
        {
            if (handler is not null) await handler.LockReferencesAsync(snapshot, ct2);
            var task = await LockTaskAsync(id, ct2);
            await EnsureOpenAsync(task, ct2);
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, WarehouseTaskStatuses.Cancelled, comment, ct2);
            task.StatusCodeId = to.StatusCodeId;
            task.CompletedAtUtc = DateTime.UtcNow;   // CompletedAtUtc = cierre de la tarea (DONE o CANCELLED)
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ putaway dirigido

    /// <summary>
    /// Posiciones sugeridas para guardar (D24): por tarea (taskId: su almacén, producto, lote, cantidad y origen) o por
    /// producto (productPublicId + almacén o el único activo, lote, cantidad y posición de origen a excluir). Cada sugerencia
    /// trae su razón y la clase de rotación (FAST/SLOW por salidas de 30 días).
    /// </summary>
    public async Task<IReadOnlyList<PutawaySuggestionDto>> SuggestPutawayAsync(int? taskId, Guid? warehousePublicId, Guid? productPublicId,
        int? lotId, decimal? quantity, int? fromBinId, int? take, CancellationToken ct)
    {
        var n = take is int t && t > 0 ? Math.Min(t, WarehouseTaskRules.MaxSuggestions) : WarehouseTaskRules.DefaultSuggestions;
        int warehouseId, productId;
        int? lot, exclude;
        decimal qty;

        if (taskId is int tid)
        {
            var task = await SnapshotAsync(tid, ct);
            if (task.ProductId is not int pid) throw new ValidationException("taskId", WarehouseTaskRules.SuggestionInput);
            warehouseId = task.WarehouseId;
            productId = pid;
            lot = task.LotId;
            qty = task.Quantity is decimal tq && tq > 0m ? tq : 1m;
            exclude = task.FromBinId;
        }
        else if (productPublicId is Guid ppid)
        {
            var product = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == ppid)
                              .Select(p => new { p.ProductId }).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException("Producto");
            warehouseId = (await ResolveWarehouseOrDefaultAsync(db, warehousePublicId, ct)).WarehouseId;
            productId = product.ProductId;
            if (lotId is int lid)
            {
                var exists = await (from l in db.Set<InventoryLot>().AsNoTracking()
                                    join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                                    where l.LotId == lid && l.ProductId == productId
                                    select l.LotId).AnyAsync(ct);
                if (!exists) throw new NotFoundException("Lote");
            }
            lot = lotId;
            if (quantity is decimal rq && rq <= 0m) throw new ValidationException("quantity", WarehouseTaskRules.QtyPositive);
            qty = quantity ?? 1m;
            exclude = fromBinId;
        }
        else throw new ValidationException("taskId", WarehouseTaskRules.SuggestionInput);

        var suggestions = await suggester.SuggestAsync(warehouseId, productId, lot, qty, exclude, n, ct);
        return suggestions.Select(ToDto).ToList();
    }

    // ================================================================ apoyo

    /// <summary>Almacén por PublicId (404) o, sin él, el único activo del tenant (ninguno → 422; más de uno → 400).</summary>
    internal static Task<Warehouse> ResolveWarehouseOrDefaultAsync(TeikemDbContext db, Guid? publicId, CancellationToken ct)
        => WmsResolve.ResolveWarehouseOrDefaultAsync(db, publicId, ct); // única implementación (D26): 404 / 400 con más de uno / 422 sin ninguno

    private IWarehouseTaskHandler? HandlerFor(string taskType)
        => handlers.FirstOrDefault(h => string.Equals(h.TaskType, taskType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tarea del tenant sin bloqueo (404 'Tarea no encontrada.').</summary>
    private async Task<WarehouseTask> SnapshotAsync(int id, CancellationToken ct)
        => await db.Set<WarehouseTask>().AsNoTracking().FirstOrDefaultAsync(t => t.WarehouseTaskId == id, ct)
           ?? throw new NotFoundException("Tarea", feminine: true);

    private async Task<string> TypeCodeAsync(WarehouseTask task, CancellationToken ct)
        => (await lookups.GetAsync(task.TaskTypeLookupId, ct))?.InternalCode ?? string.Empty;

    /// <summary>Re-verificación bajo bloqueo: la tarea sigue abierta (si no, 422 'La tarea ya fue completada o cancelada.').</summary>
    private async Task<string> EnsureOpenAsync(WarehouseTask task, CancellationToken ct)
    {
        var code = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == task.StatusCodeId)
            .Select(s => s.InternalCode).FirstOrDefaultAsync(ct);
        if (!WarehouseTaskRules.IsOpen(code)) throw new StatusRuleException(WarehouseTaskRules.TaskNotOpen);
        return code!;
    }

    /// <summary>Transición escalonada a DONE (el pipeline no admite saltos): PENDING → IN_PROGRESS → DONE, con historial.</summary>
    private async Task AdvanceToDoneAsync(WarehouseTask task, string currentCode, string? comment, CancellationToken ct)
    {
        if (string.Equals(currentCode, WarehouseTaskStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            var started = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, WarehouseTaskStatuses.InProgress, null, ct);
            task.StatusCodeId = started.StatusCodeId;
        }
        var done = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
            task.StatusCodeId, WarehouseTaskStatuses.Done, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), ct);
        task.StatusCodeId = done.StatusCodeId;
    }

    private static List<string> Codes(IEnumerable<string> raw)
        => raw.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();

    // ================================================================ adaptadores a las costuras de P0
    // Toda dependencia de las firmas de InventoryQueries, WarehouseTaskWriter y PutawaySuggester vive aquí.

    /// <summary>Tarea con UPDLOCK, tracked (después de los encabezados de documento, antes de los saldos).</summary>
    private Task<WarehouseTask> LockTaskAsync(int id, CancellationToken ct) => db.LockWarehouseTaskAsync(id, ct);

    /// <summary>
    /// Remanente de un completado parcial: tarea nueva (WarehouseTaskWriter: historial null → PENDING) con el mismo tipo,
    /// producto, lote, origen, destino sugerido y Ref; conserva prioridad y asignado.
    /// </summary>
    private async Task CreateRemainderAsync(WarehouseTask task, decimal remainder, int? suggestedToBinId, CancellationToken ct)
    {
        var type = await TypeCodeAsync(task, ct);
        var refType = task.RefEntityLookupId is int rt ? (await lookups.GetAsync(rt, ct))?.InternalCode : null;
        var created = await WarehouseTaskWrites.CreateAsync(db, taskWriter, new WarehouseTaskSpec(
            TaskType: type, WarehouseId: task.WarehouseId, ProductId: task.ProductId, Quantity: remainder,
            LotId: task.LotId, FromBinId: task.FromBinId, ToBinId: suggestedToBinId,
            RefEntityType: refType, RefId: task.RefId), ct);
        created.Priority = task.Priority;
        created.AssignedToUserId = task.AssignedToUserId;
    }

    /// <summary>Lote 16: con el cupo de la posición y su espacio libre (cupo − existencia; null sin cupo).</summary>
    private static PutawaySuggestionDto ToDto(PutawaySuggestion s)
        => new(s.BinId, s.BinCode, s.ZoneCode, s.ZoneTypeCode, s.ReasonCode, s.Reason, s.RotationClass,
            s.MaxCapacityQty, ReceivingModeRules.FreeQty(s.MaxCapacityQty, s.BinQty, 0m));
}

/// <summary>Lote 6 (P5) — alta de tareas vía WarehouseTaskWriter (única vía de creación), devolviendo la entidad tracked.</summary>
internal static class WarehouseTaskWrites
{
    /// <summary>
    /// Crea la tarea con el writer (historial null → PENDING) y devuelve la entidad nueva que quedó en el ChangeTracker, de
    /// modo que el llamador pueda ajustar prioridad o asignado dentro de la misma unidad de trabajo.
    /// </summary>
    public static async Task<WarehouseTask> CreateAsync(TeikemDbContext db, WarehouseTaskWriter writer, WarehouseTaskSpec spec, CancellationToken ct)
    {
        var before = new HashSet<object>(db.ChangeTracker.Entries<WarehouseTask>().Select(e => (object)e.Entity), ReferenceEqualityComparer.Instance);
        await writer.CreateAsync(spec, ct);
        return db.ChangeTracker.Entries<WarehouseTask>().Select(e => e.Entity).Single(t => !before.Contains(t));
    }
}

/// <summary>
/// Lote 6 (P5) — armado de WarehouseTaskDto por lotes (sin N+1), compartido por la cola, el reabasto y la fuente
/// WAREHOUSE_TASK. Almacén, producto, posiciones, lote, serie, asignado, estatus (con etiqueta del tenant) y origen legible
/// (KardexRules.RefLabel: 'Recibo REC-00001', 'Conteo CC-00001', 'Cruce de muelle XD-00001', el SKU para PRODUCT).
/// </summary>
internal static class WarehouseTaskReads
{
    public sealed record StatusInfo(string Code, string Label, string? Color);

    public static async Task<IReadOnlyList<WarehouseTaskDto>> BuildAsync(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups,
        IEnumerable<IWarehouseTaskHandler> handlers, IReadOnlyList<WarehouseTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return Array.Empty<WarehouseTaskDto>();
        var whIds = tasks.Select(t => t.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .Select(w => new { w.WarehouseId, w.PublicId, w.Code }).ToDictionaryAsync(w => w.WarehouseId, ct);
        var productIds = tasks.Where(t => t.ProductId != null).Select(t => t.ProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToDictionaryAsync(p => p.ProductId, ct);
        var moves = await MovesAsync(db, lookups, tasks, ct);
        var binIds = tasks.SelectMany(t => new[] { t.FromBinId, t.ToBinId }).Concat(moves.Values.SelectMany(m => m).SelectMany(m => new[] { m.FromBinId, m.ToBinId }))
            .Where(b => b != null).Select(b => b!.Value).Distinct().ToList();
        var bins = await db.Set<WarehouseBin>().AsNoTracking()
            .Where(b => binIds.Contains(b.WarehouseBinId) && whIds.Contains(b.WarehouseId))
            .ToDictionaryAsync(b => b.WarehouseBinId, b => b.Code, ct);
        var lotIds = tasks.Where(t => t.LotId != null).Select(t => t.LotId!.Value)
            .Concat(moves.Values.SelectMany(m => m).Where(m => m.LotId != null).Select(m => m.LotId!.Value)).Distinct().ToList();
        var lots = await db.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.LotId) && productIds.Contains(l.ProductId))
            .ToDictionaryAsync(l => l.LotId, l => l.LotNumber, ct);
        var serialIds = tasks.Where(t => t.SerialId != null).Select(t => t.SerialId!.Value).Distinct().ToList();
        var serials = await db.Set<InventorySerial>().AsNoTracking().Where(s => serialIds.Contains(s.SerialId) && productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.SerialId, s => s.SerialNumber, ct);
        var userIds = tasks.Where(t => t.AssignedToUserId != null).Select(t => t.AssignedToUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        var statusMap = await StatusMapAsync(db, tenant, StatusDomains.WarehouseTaskStatus, ct);
        var refLabels = await RefLabelsAsync(db, lookups, tasks, ct);
        var handlerList = handlers.ToList();

        var result = new List<WarehouseTaskDto>(tasks.Count);
        foreach (var t in tasks)
        {
            var type = await lookups.GetAsync(t.TaskTypeLookupId, ct);
            var typeCode = type?.InternalCode ?? string.Empty;
            var handler = handlerList.FirstOrDefault(h => string.Equals(h.TaskType, typeCode, StringComparison.OrdinalIgnoreCase));
            var refCode = t.RefEntityLookupId is int rl ? (await lookups.GetAsync(rl, ct))?.InternalCode : null;
            var w = warehouses.GetValueOrDefault(t.WarehouseId);
            var p = t.ProductId is int pid ? products.GetValueOrDefault(pid) : null;
            var s = statusMap.GetValueOrDefault(t.StatusCodeId);
            result.Add(new WarehouseTaskDto(t.WarehouseTaskId, typeCode,
                type is null ? typeCode : MultilingualText.Resolve(type.LabelJson, tenant.Lang),
                s?.Code ?? string.Empty, s?.Label ?? string.Empty, t.Priority,
                w?.PublicId ?? Guid.Empty, w?.Code ?? string.Empty,
                p?.PublicId, p?.Sku, p?.Name,
                t.LotId, t.LotId is int lid ? lots.GetValueOrDefault(lid) : null,
                t.SerialId is int sid ? serials.GetValueOrDefault(sid) : null,
                t.Quantity,
                t.FromBinId, t.FromBinId is int fb ? bins.GetValueOrDefault(fb) : null,
                t.ToBinId, t.ToBinId is int tb ? bins.GetValueOrDefault(tb) : null,
                refCode, t.RefId, refLabels.GetValueOrDefault(t.WarehouseTaskId),
                t.AssignedToUserId, t.AssignedToUserId is int u ? users.GetValueOrDefault(u) : null,
                handler is not null && handler.NotFromQueueMessage is null,
                t.CreatedAtUtc, t.CompletedAtUtc,
                moves.TryGetValue(t.WarehouseTaskId, out var taskMoves)
                    ? taskMoves.Select(m => new WarehouseTaskMoveDto(m.TransactionId, m.FromBinId is int mf ? bins.GetValueOrDefault(mf) : null,
                        m.ToBinId is int mt ? bins.GetValueOrDefault(mt) : null, m.Quantity, m.LotId is int ml ? lots.GetValueOrDefault(ml) : null, m.CreatedAtUtc)).ToList()
                    : Array.Empty<WarehouseTaskMoveDto>()));
        }
        return result;
    }

    private sealed record TaskMove(long TransactionId, int? FromBinId, int? ToBinId, decimal Quantity, int? LotId, DateTime CreatedAtUtc);

    /// <summary>
    /// Movimientos del ledger que hizo cada tarea (referencia WAREHOUSE_TASK + id de la tarea), en el orden en que se registraron. Una sola consulta
    /// para todas las tareas de la página. Un acomodo repartido deja una línea por posición.
    /// </summary>
    private static async Task<Dictionary<int, List<TaskMove>>> MovesAsync(TeikemDbContext db, ILookupCache lookups, IReadOnlyList<WarehouseTask> tasks,
        CancellationToken ct)
    {
        var result = new Dictionary<int, List<TaskMove>>();
        var taskEntityId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.WarehouseTask, ct);
        if (taskEntityId is null) return result;
        var ids = tasks.Select(t => t.WarehouseTaskId).ToList();
        var rows = await db.Set<InventoryTransaction>().AsNoTracking()
            .Where(x => x.RefEntityLookupId == taskEntityId && x.RefId != null && ids.Contains(x.RefId.Value))
            .OrderBy(x => x.InventoryTransactionId)
            .Select(x => new { x.RefId, x.InventoryTransactionId, x.FromBinId, x.ToBinId, x.Quantity, x.LotId, x.CreatedAtUtc })
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            if (!result.TryGetValue(r.RefId!.Value, out var list)) result[r.RefId.Value] = list = new List<TaskMove>();
            list.Add(new TaskMove(r.InventoryTransactionId, r.FromBinId, r.ToBinId, r.Quantity, r.LotId, r.CreatedAtUtc));
        }
        return result;
    }

    /// <summary>Origen legible por tarea: número del documento referido (consultas agrupadas por tipo) o fallback 'TIPO·id'.</summary>
    public static async Task<Dictionary<int, string?>> RefLabelsAsync(TeikemDbContext db, ILookupCache lookups,
        IReadOnlyList<WarehouseTask> tasks, CancellationToken ct)
    {
        var result = new Dictionary<int, string?>();
        var byType = new Dictionary<string, List<WarehouseTask>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tasks)
        {
            if (t.RefEntityLookupId is not int rl || t.RefId is null) continue;
            var code = (await lookups.GetAsync(rl, ct))?.InternalCode;
            if (code is null) continue;
            if (!byType.TryGetValue(code, out var list)) byType[code] = list = new List<WarehouseTask>();
            list.Add(t);
        }

        foreach (var (code, list) in byType)
        {
            var ids = list.Select(t => t.RefId!.Value).Distinct().ToList();
            Dictionary<int, string> numbers = code.ToUpperInvariant() switch
            {
                EntityTypes.Receipt => await db.Set<ReceiptHeader>().AsNoTracking().Where(r => ids.Contains(r.ReceiptHeaderId))
                    .ToDictionaryAsync(r => r.ReceiptHeaderId, r => r.Number, ct),
                EntityTypes.CycleCount => await db.Set<CycleCount>().AsNoTracking().Where(c => ids.Contains(c.CycleCountId))
                    .ToDictionaryAsync(c => c.CycleCountId, c => c.Number, ct),
                EntityTypes.PickBatch => await db.Set<PickBatch>().AsNoTracking().Where(b => ids.Contains(b.PickBatchId))
                    .ToDictionaryAsync(b => b.PickBatchId, b => b.Number, ct),
                EntityTypes.CrossDockPlan => await db.Set<CrossDockPlan>().AsNoTracking().Where(pl => ids.Contains(pl.CrossDockPlanId))
                    .ToDictionaryAsync(pl => pl.CrossDockPlanId, pl => pl.Number, ct),
                // La asignación (sin TenantId) se alcanza por su plan filtrado.
                EntityTypes.CrossDockAllocation => await (from a in db.Set<CrossDockAllocation>().AsNoTracking()
                                                          join pl in db.Set<CrossDockPlan>().AsNoTracking() on a.CrossDockPlanId equals pl.CrossDockPlanId
                                                          where ids.Contains(a.CrossDockAllocationId)
                                                          select new { a.CrossDockAllocationId, pl.Number })
                    .ToDictionaryAsync(x => x.CrossDockAllocationId, x => x.Number, ct),
                EntityTypes.Product => await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct),
                _ => new Dictionary<int, string>(),
            };
            foreach (var t in list)
                result[t.WarehouseTaskId] = KardexRules.RefLabel(code, t.RefId, numbers.GetValueOrDefault(t.RefId!.Value));
        }
        return result;
    }

    /// <summary>Estatus del dominio con la etiqueta y el color personalizados del tenant si existen.</summary>
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
}
