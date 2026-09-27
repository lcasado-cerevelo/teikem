using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P9) — handler CROSSDOCK de la cola unificada (D41). Permiso: warehouse.crossdock; se completa DESDE LA COLA
/// (completar la tarea = Mover la asignación).
/// - LockReferencesAsync: la tarea nace con Ref CROSSDOCK_ALLOCATION; bloquea el plan y luego el recibo de la línea de la
///   asignación ANTES que la tarea (orden del lote: CrossDockPlan &lt; ReceiptHeader &lt; WarehouseTask &lt; saldos).
/// - CompleteAsync: la tarea se completa por la cantidad confirmada exacta (otra cantidad → 400 ExactQty); el movimiento lo
///   hace CrossDockService.MoveCoreAsync (CROSSDOCK con FromReserved, asignación MOVED). WarehouseTaskService deja la tarea
///   DONE. Devuelve la cantidad movida.
/// - Módulo (D28): la cola lleva solo [RequireModule(WMS_LOTSERIAL)]; LockReferencesAsync exige además CROSSDOCK encendido
///   (403 module_disabled). WarehouseTaskService la invoca antes de bloquear la tarea al iniciar y al completar, así que con
///   el módulo apagado ninguna de las dos rutas mueve inventario ni cambia estatus.
/// </summary>
public sealed class CrossDockTaskHandler(ILookupCache lookups, CrossDockService crossDock, ModuleService modules) : IWarehouseTaskHandler
{
    public const string AllocationMissing = "La tarea de cruce de muelle no tiene asignación; no se puede completar.";

    public string TaskType => WarehouseTaskTypes.CrossDock;
    public string RequiredPermission => PermissionCatalog.WarehouseCrossdock;
    public string? NotFromQueueMessage => null;

    public async Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct)
    {
        await modules.EnsureEnabledAsync(ModuleKeys.CrossDock, ct);
        if (await AllocationIdAsync(snapshot, ct) is int allocationId)
            await crossDock.LockAllocationReferencesAsync(allocationId, ct);
    }

    public async Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct)
    {
        var allocationId = await AllocationIdAsync(task, ct) ?? throw new StatusRuleException(AllocationMissing);
        var allocation = await crossDock.TrackedAllocationAsync(allocationId, ct);
        if (req?.Quantity is decimal requested && allocation.ConfirmedQty is decimal confirmed && requested != confirmed)
            throw new ValidationException("quantity", CrossDockRules.ExactQty(confirmed));
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        return await crossDock.MoveCoreAsync(allocation, closeTask: false, comment, ct);
    }

    private async Task<int?> AllocationIdAsync(WarehouseTask task, CancellationToken ct)
    {
        if (task.RefId is not int refId || task.RefEntityLookupId is not int refType) return null;
        var allocationTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.CrossDockAllocation, ct);
        return allocationTypeId == refType ? refId : null;
    }
}
