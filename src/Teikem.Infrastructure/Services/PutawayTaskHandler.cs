using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P5) — handler PUTAWAY de la cola unificada (R10, D41). Permiso: warehouse.receive; se completa desde la cola.
/// - LockReferencesAsync: si la tarea nace de un recibo (Ref RECEIPT), bloquea su encabezado ANTES que la tarea (orden del
///   lote: ReceiptHeader &lt; WarehouseTask &lt; saldos). Así el efecto que cierra el recibo (WarehouseTaskStatusEffect) corre con
///   el recibo ya bloqueado y dos putaway concurrentes del mismo recibo se serializan.
/// - CompleteAsync: destino = el indicado al completar o el sugerido en la tarea (400 'Indique la posición de destino.'),
///   del MISMO almacén (otra posición → 404 'Posición no encontrada.') y distinto del origen. Un TRANSFER por la cantidad
///   completada (una fila por serie en productos con serie) con Ref WAREHOUSE_TASK + id; el ledger bloquea los saldos,
///   re-verifica el disponible (lo reservado para cruce de muelle no se mueve) y la posición activa. Devuelve lo movido;
///   WarehouseTaskService hace el split del remanente y el DONE.
/// </summary>
public sealed class PutawayTaskHandler(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger) : IWarehouseTaskHandler
{
    public string TaskType => WarehouseTaskTypes.Putaway;
    public string RequiredPermission => PermissionCatalog.WarehouseReceive;
    public string? NotFromQueueMessage => null;

    public async Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct)
    {
        if (snapshot.RefId is not int refId || snapshot.RefEntityLookupId is not int refType) return;
        var receiptTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Receipt, ct);
        if (receiptTypeId != refType) return;
        await WarehouseTaskTransfer.LockReceiptAsync(db, refId, ct);
    }

    public Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct)
        => WarehouseTaskTransfer.TransferAsync(db, lookups, ledger, task, req, ct);
}

/// <summary>
/// Lote 6 (P5) — movimiento físico compartido por los handlers PUTAWAY y REPLENISH: TRANSFER de la posición de origen de la
/// tarea a su destino, dentro del mismo almacén, con el lote de la tarea (o el de cada serie). Lo llama el handler dentro de
/// la transacción de WarehouseTaskService (tarea ya bloqueada y re-verificada abierta).
/// </summary>
internal static class WarehouseTaskTransfer
{
    public static async Task<decimal> TransferAsync(TeikemDbContext db, ILookupCache lookups, InventoryLedger ledger,
        WarehouseTask task, TaskCompleteRequest? req, CancellationToken ct)
    {
        req ??= new TaskCompleteRequest();
        var (qty, qtyError) = WarehouseTaskRules.CompletionQuantity(task.Quantity, req.Quantity);
        if (qtyError is not null)
        {
            if (qtyError == WarehouseTaskRules.QuantityMissing) throw new StatusRuleException(qtyError);
            throw new ValidationException("quantity", qtyError);
        }
        if (task.ProductId is not int productId || task.FromBinId is not int fromBinId)
            throw new StatusRuleException(WarehouseTaskRules.OriginMissing);

        var (toBinId, destinationError) = WarehouseTaskRules.Destination(req.ToBinId, task.ToBinId, task.FromBinId);
        if (destinationError is not null) throw new ValidationException("toBinId", destinationError);

        // La posición (hija sin TenantId) se resuelve SIEMPRE dentro del almacén de la tarea, filtrado por tenant:
        // otra posición del almacén de otro tenant o de otro almacén → 404 sin oráculo.
        var toBin = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                           join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                           where b.WarehouseBinId == toBinId && b.WarehouseId == task.WarehouseId
                           select new { b.WarehouseBinId }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Posición", feminine: true);

        var product = await db.Set<Product>().AsNoTracking().Where(p => p.ProductId == productId)
                          .Select(p => new { p.ProductId, p.TrackingTypeLookupId }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Producto");
        var tracking = (await lookups.GetAsync(product.TrackingTypeLookupId, ct))?.InternalCode ?? TrackingTypes.None;

        var postings = new List<InventoryPosting>();
        if (string.Equals(tracking, TrackingTypes.Serial, StringComparison.OrdinalIgnoreCase))
        {
            string? taskSerial = null;
            if (task.SerialId is int sid)
                taskSerial = await (from s in db.Set<InventorySerial>().AsNoTracking()
                                    join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                                    where s.SerialId == sid && s.ProductId == productId
                                    select s.SerialNumber).FirstOrDefaultAsync(ct);
            var (serials, serialError) = WarehouseTaskRules.SerialsForCompletion(qty, taskSerial, req.SerialNumbers);
            if (serialError is not null) throw new ValidationException("serialNumbers", serialError);

            // Cada serie viaja con SU lote (clave del saldo); una serie desconocida la rechaza el ledger (no está en origen).
            var numbers = serials.ToList();
            var known = await (from s in db.Set<InventorySerial>().AsNoTracking()
                               join p in db.Set<Product>().AsNoTracking() on s.ProductId equals p.ProductId
                               where s.ProductId == productId && numbers.Contains(s.SerialNumber)
                               select new { s.SerialId, s.SerialNumber, s.LotId }).ToListAsync(ct);
            foreach (var serial in serials)
            {
                var k = known.FirstOrDefault(x => string.Equals(x.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));
                postings.Add(Posting(task, productId, 1m, k?.LotId ?? task.LotId, k?.SerialId, k?.SerialNumber ?? serial, fromBinId, toBin.WarehouseBinId));
            }
        }
        else
        {
            if (req.SerialNumbers is { Count: > 0 })
                throw new ValidationException("serialNumbers", WarehouseTaskRules.SerialNotOfTask);
            postings.Add(Posting(task, productId, qty, task.LotId, null, null, fromBinId, toBin.WarehouseBinId));
        }

        await ledger.PostAsync(postings, ct);
        // La tarea registra el destino REAL (el operador puede cambiar el sugerido dentro del almacén, D24).
        task.ToBinId = toBin.WarehouseBinId;
        return qty;
    }

    private static InventoryPosting Posting(WarehouseTask task, int productId, decimal qty, int? lotId, int? serialId, string? serialNumber,
        int fromBinId, int toBinId)
        => new(InventoryTxnTypes.Transfer, productId, qty, LotId: lotId, SerialId: serialId, SerialNumber: serialNumber,
            FromWarehouseId: task.WarehouseId, FromBinId: fromBinId, ToWarehouseId: task.WarehouseId, ToBinId: toBinId,
            RefEntityType: EntityTypes.WarehouseTask, RefId: task.WarehouseTaskId);

    // ================================================================ adaptador a la costura de P0 (InventoryQueries)

    /// <summary>Encabezado ReceiptHeader con UPDLOCK, tracked (antes de la tarea en el orden de bloqueo del lote).</summary>
    public static async Task LockReceiptAsync(TeikemDbContext db, int receiptHeaderId, CancellationToken ct)
        => await db.LockReceiptAsync(receiptHeaderId, ct);
}
