using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P5) — handler REPLENISH de la cola unificada (R15, D23, D41). Permiso: warehouse.pick; se completa desde la cola.
/// - LockReferencesAsync: sin encabezado que bloquear (la tarea referencia al producto, que no se modifica).
/// - CompleteAsync: TRANSFER de la posición de reserva (FromBin) a la de picking (ToBin, o la indicada dentro del mismo
///   almacén) con el lote de la tarea, por la cantidad completada. El ledger solo mueve el DISPONIBLE: lo reservado no se
///   toca (409 insufficient_stock si la reserva ya no alcanza). Un reabasto parcial deja el remanente como tarea nueva.
/// </summary>
public sealed class ReplenishTaskHandler(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger) : IWarehouseTaskHandler
{
    public string TaskType => WarehouseTaskTypes.Replenish;
    public string RequiredPermission => PermissionCatalog.WarehousePick;
    public string? NotFromQueueMessage => null;

    public Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct) => Task.CompletedTask;

    public Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct)
        => WarehouseTaskTransfer.TransferAsync(db, lookups, ledger, task, req, ct);
}
