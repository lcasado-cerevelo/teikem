using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (P0) — alta y cierre de tareas de almacén con historial de estatus (EntityType WAREHOUSE_TASK). Las tareas nacen
/// PENDING (historial null → PENDING). El pipeline no admite saltos: AdvanceAsync avanza escalonado (PENDING → IN_PROGRESS →
/// DONE). CancelAsync pasa a CANCELLED (entrada lateral desde PENDING o IN_PROGRESS) y ReduceAsync baja la cantidad (cancela si
/// llega a 0). Corre dentro de la transacción del llamador; la tarea que se cierra ya viene bloqueada.
/// </summary>
public sealed class WarehouseTaskWriter(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, StatusService statuses)
{
    /// <summary>Crea la tarea PENDING con historial, la guarda (para tener id) y la devuelve tracked.</summary>
    public async Task<WarehouseTask> CreateAsync(WarehouseTaskSpec spec, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");
        var typeId = await lookups.GetIdAsync(LookupDomains.WarehouseTaskType, spec.TaskType, ct);
        var refId = string.IsNullOrWhiteSpace(spec.RefEntityType) ? (int?)null : await lookups.GetIdAsync(LookupDomains.EntityType, spec.RefEntityType, ct);
        var initial = await statuses.GetInitialAsync(StatusDomains.WarehouseTaskStatus, ct);
        var task = new WarehouseTask
        {
            TenantId = tenantId, WarehouseId = spec.WarehouseId, TaskTypeLookupId = typeId, StatusCodeId = initial.StatusCodeId,
            ProductId = spec.ProductId, LotId = spec.LotId, SerialId = spec.SerialId, Quantity = spec.Quantity,
            FromBinId = spec.FromBinId, ToBinId = spec.ToBinId, RefEntityLookupId = refId, RefId = spec.RefId,
            AssignedToUserId = spec.AssignedToUserId, Priority = spec.Priority, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        db.WarehouseTasks.Add(task);
        await db.SaveChangesAsync(ct);
        var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId, null,
            initial.InternalCode, null, ct);
        task.StatusCodeId = to.StatusCodeId;
        await db.SaveChangesAsync(ct);
        return task;
    }

    /// <summary>
    /// Avanza escalonado hasta el estatus del pipeline indicado (IN_PROGRESS o DONE), con historial por paso. DONE fija
    /// CompletedAtUtc. Una tarea ya en el destino no cambia; una cerrada → 422 'La tarea ya fue completada o cancelada.'.
    /// </summary>
    public async Task AdvanceAsync(WarehouseTask task, string targetCode, string? comment, CancellationToken ct)
    {
        var codes = await PipelineAsync(ct);
        var current = codes.FirstOrDefault(c => c.StatusCodeId == task.StatusCodeId);
        if (current?.InternalCode == targetCode) return;
        if (current is null || current.StageKind?.InternalCode == StageKinds.Terminal) throw new StatusRuleException(TaskClosedMessage);
        var target = codes.FirstOrDefault(c => c.InternalCode == targetCode)
                     ?? throw new StatusRuleException($"El estatus '{targetCode}' no existe o no está habilitado para esta compañía.");
        var steps = codes.Where(c => c.StageKind?.InternalCode != StageKinds.Lateral && c.SortOrder > current.SortOrder && c.SortOrder <= target.SortOrder)
            .OrderBy(c => c.SortOrder).ToList();
        foreach (var step in steps)
        {
            var isLast = step.StatusCodeId == target.StatusCodeId;
            var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
                task.StatusCodeId, step.InternalCode, isLast ? Clean(comment) : null, ct);
            task.StatusCodeId = to.StatusCodeId;
            if (isLast) break;
        }
        if (targetCode == WarehouseTaskStatuses.Done) task.CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Cancela una tarea abierta (PENDING/IN_PROGRESS → CANCELLED) con comentario; fija CompletedAtUtc.</summary>
    public async Task CancelAsync(WarehouseTask task, string? comment, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.WarehouseTaskStatus, EntityTypes.WarehouseTask, task.WarehouseTaskId,
            task.StatusCodeId, WarehouseTaskStatuses.Cancelled, Clean(comment), ct);
        task.StatusCodeId = to.StatusCodeId;
        task.CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Baja la cantidad de una tarea abierta (p. ej. el putaway que se va a cruce de muelle). Si llega a 0 la cancela. No puede
    /// bajar más de lo que tiene (400).
    /// </summary>
    public async Task ReduceAsync(WarehouseTask task, decimal by, string? comment, CancellationToken ct)
    {
        if (by <= 0m) return;
        var qty = task.Quantity ?? 0m;
        if (by > qty) throw new ValidationException("quantity", "La cantidad excede la de la tarea.");
        task.Quantity = qty - by;
        if (task.Quantity == 0m) await CancelAsync(task, comment, ct);
    }

    public const string TaskClosedMessage = "La tarea ya fue completada o cancelada.";

    private async Task<List<StatusCode>> PipelineAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Include(s => s.StageKind)
            .Where(s => s.Entity == StatusDomains.WarehouseTaskStatus && s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(ct);

    private static string? Clean(string? comment) => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}
