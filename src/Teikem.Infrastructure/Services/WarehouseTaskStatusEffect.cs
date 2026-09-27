using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P5) — efecto del dominio WarehouseTaskStatus (R10): cuando una PUTAWAY con Ref RECEIPT llega a DONE o CANCELLED
/// y el recibo ya no tiene otra PUTAWAY abierta (PENDING/IN_PROGRESS), el recibo pasa RECEIVED → PUTAWAY (terminal). Si el
/// recibo no está en RECEIVED (OPEN, o ya en PUTAWAY) no cambia. Cualquier otro tipo de tarea no tiene efecto.
/// - Corre dentro de StatusService.TransitionAsync, en la transacción del llamador y ANTES de que éste asigne el nuevo
///   StatusCodeId de la tarea: por eso la tarea que transiciona se excluye por id. Las demás tareas se leen con tracking
///   (valores en memoria) más las recién agregadas del ChangeTracker, así un remanente creado en la misma unidad de trabajo
///   cuenta como abierto.
/// - Precondición de bloqueo: el llamador ya bloqueó el encabezado del recibo (PutawayTaskHandler.LockReferencesAsync en la
///   cola; el plan de cruce de muelle en P9), en el orden ReceiptHeader &lt; WarehouseTask. El efecto no bloquea: carga el
///   recibo tracked (EF devuelve la misma instancia bloqueada) y no guarda; el llamador persiste todo junto.
/// StatusService se resuelve de forma perezosa (evita el ciclo StatusService → efectos → este efecto → StatusService).
/// </summary>
public sealed class WarehouseTaskStatusEffect(TeikemDbContext db, IServiceProvider services, ILookupCache lookups) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.WarehouseTaskStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.WarehouseTask, StringComparison.OrdinalIgnoreCase)) return;
        var toCode = context.To.InternalCode;
        if (!string.Equals(toCode, WarehouseTaskStatuses.Done, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(toCode, WarehouseTaskStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)) return;

        var taskId = context.EntityId;
        var task = db.Set<WarehouseTask>().Local.FirstOrDefault(t => t.WarehouseTaskId == taskId)
                   ?? await db.Set<WarehouseTask>().AsNoTracking().FirstOrDefaultAsync(t => t.WarehouseTaskId == taskId, ct);
        if (task?.RefId is not int receiptId || task.RefEntityLookupId is not int refType) return;

        var putawayTypeId = await lookups.TryGetIdAsync(LookupDomains.WarehouseTaskType, WarehouseTaskTypes.Putaway, ct);
        var receiptEntityId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);
        if (putawayTypeId is null || receiptEntityId is null) return;
        if (task.TaskTypeLookupId != putawayTypeId.Value || refType != receiptEntityId.Value) return;

        // ¿Queda otra PUTAWAY abierta del mismo recibo? (persistidas con sus valores en memoria + agregadas sin guardar)
        var openCodes = new List<string> { WarehouseTaskStatuses.Pending, WarehouseTaskStatuses.InProgress };
        var openIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus && openCodes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var ptId = putawayTypeId.Value;
        var reId = receiptEntityId.Value;
        var siblings = await db.Set<WarehouseTask>()
            .Where(t => t.WarehouseTaskId != taskId && t.TaskTypeLookupId == ptId && t.RefEntityLookupId == reId && t.RefId == receiptId)
            .ToListAsync(ct);
        var pending = siblings.Concat(db.Set<WarehouseTask>().Local.Where(t => t.WarehouseTaskId != taskId
                                          && t.TaskTypeLookupId == ptId && t.RefEntityLookupId == reId && t.RefId == receiptId))
            .Any(t => openIds.Contains(t.StatusCodeId));
        if (pending) return;

        // Recibo del mismo almacén, bajo el filtro de tenant (tracked: la instancia ya bloqueada por el llamador).
        var receipt = await db.Set<ReceiptHeader>().FirstOrDefaultAsync(r => r.ReceiptHeaderId == receiptId && r.WarehouseId == task.WarehouseId, ct);
        if (receipt is null) return;
        var receivedId = await db.StatusIdAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.Received, ct);
        if (receipt.StatusCodeId != receivedId) return;

        var statuses = services.GetRequiredService<StatusService>();
        var done = await statuses.TransitionAsync(StatusDomains.ReceiptStatus, EntityTypes.Receipt, receipt.ReceiptHeaderId,
            receipt.StatusCodeId, ReceiptStatuses.Putaway, null, ct);
        receipt.StatusCodeId = done.StatusCodeId;
    }
}
