using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P6) — handler de la cola para las tareas COUNT (D41). Las tareas de conteo se INICIAN y se asignan desde la cola
/// (permiso warehouse.count), pero se completan SOLO desde Conteo cíclico: la reconciliación las pasa a DONE en la misma
/// transacción que asienta los ajustes. Por eso NotFromQueueMessage no es nulo y WarehouseTaskService responde 422 antes de
/// llegar a CompleteAsync, que nunca se invoca (si se invocara, responde el mismo 422).
/// LockReferencesAsync bloquea el encabezado del conteo ANTES que la tarea (orden de bloqueo del lote: CycleCount <
/// WarehouseTask), igual que CycleCountService.ReconcileAsync y DeleteAsync.
/// </summary>
public sealed class CountTaskHandler(TeikemDbContext db, ILookupCache lookups) : IWarehouseTaskHandler
{
    public string TaskType => WarehouseTaskTypes.Count;

    public string RequiredPermission => PermissionCatalog.WarehouseCount;

    public string? NotFromQueueMessage => CycleCountRules.CompleteFromCountScreen;

    public async Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct)
    {
        if (snapshot.RefId is not int countId || snapshot.RefEntityLookupId is not int refType) return;
        var cycleCountType = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.CycleCount, ct);
        if (cycleCountType is null || refType != cycleCountType.Value) return;
        await db.LockCycleCountAsync(countId, ct);
    }

    public Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct)
        => throw new StatusRuleException(CycleCountRules.CompleteFromCountScreen);
}
