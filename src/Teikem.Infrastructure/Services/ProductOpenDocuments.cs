using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// ¿El producto está en un documento abierto que todavía lo puede mover? Criterio común de la baja del producto
/// (ProductService.DeactivateAsync) y de la conversión a serie (Lote 26, ProductSerialConversionService): recibos abiertos
/// (Lote 13: EXPECTED/RECEIVING/DISCREPANCY), tareas PENDING o IN_PROGRESS, recolecciones que aún se pueden eliminar
/// (COLLECTED, o PACKED con su orden activa en etapa inicial: su reversa es una ENTRADA) y conteos OPEN/COUNTED con líneas
/// del producto (su reconciliación puede asentar entradas). Las hijas sin TenantId se alcanzan por su encabezado filtrado.
/// 2026-10-11: los despachos manuales (DMA-#####) no cuentan como documento abierto.
/// </summary>
internal static class ProductOpenDocuments
{
    public static async Task<bool> AnyAsync(TeikemDbContext db, int productId, CancellationToken ct)
    {
        var openReceiptIds = await db.StatusIdsAsync(StatusDomains.ReceiptStatus, ReceiptStatuses.OpenCodes, ct);
        var openTaskIds = new List<int>
        {
            await db.StatusIdAsync(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.Pending, ct),
            await db.StatusIdAsync(StatusDomains.WarehouseTaskStatus, WarehouseTaskStatuses.InProgress, ct),
        };
        var collectedPickId = await db.StatusIdAsync(StatusDomains.PickBatchStatus, PickBatchStatuses.Collected, ct);
        var packedPickId = await db.StatusIdAsync(StatusDomains.PickBatchStatus, PickBatchStatuses.Packed, ct);
        var openCountIds = new List<int>
        {
            await db.StatusIdAsync(StatusDomains.CycleCountStatus, CycleCountStatuses.Open, ct),
            await db.StatusIdAsync(StatusDomains.CycleCountStatus, CycleCountStatuses.Counted, ct),
        };

        // ReceiptLine no lleva TenantId: se alcanza por su recibo filtrado.
        var inOpenReceipt = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                                   join h in db.Set<ReceiptHeader>().AsNoTracking() on l.ReceiptHeaderId equals h.ReceiptHeaderId
                                   where l.ProductId == productId && h.IsActive && openReceiptIds.Contains(h.StatusCodeId)
                                   select l.ReceiptLineId).AnyAsync(ct);
        if (inOpenReceipt) return true;
        if (await db.Set<WarehouseTask>().AsNoTracking().AnyAsync(t => t.ProductId == productId && openTaskIds.Contains(t.StatusCodeId), ct))
            return true;

        // PickBatchLine no lleva TenantId: se alcanza por su lote filtrado (criterio de PickBatchRules.CanDelete).
        // 2026-10-11: el despacho manual (DMA, ManualIssueReasonId no nulo) NO cuenta: es una salida definitiva que queda en
        // COLLECTED para siempre y bloquearía la baja sin remedio; tras la baja su eliminación la rechaza el ledger (422).
        var inRevertiblePick = await (from l in db.Set<PickBatchLine>().AsNoTracking()
                                      join b in db.Set<PickBatch>().AsNoTracking() on l.PickBatchId equals b.PickBatchId
                                      where l.ProductId == productId && l.ReversalTxnId == null && b.IsActive && b.ManualIssueReasonId == null
                                            && (b.StatusCodeId == collectedPickId
                                                || (b.StatusCodeId == packedPickId
                                                    && db.TransportOrders.Any(o => o.TransportOrderId == b.TransportOrderId && o.IsActive
                                                        && db.StatusCodes.Any(s => s.StatusCodeId == o.StatusCodeId && s.IsInitial))))
                                      select l.PickBatchLineId).AnyAsync(ct);
        if (inRevertiblePick) return true;

        // Conteo abierto (OPEN/COUNTED) con líneas del producto. La tarea COUNT nace sin ProductId, así que la verificación de
        // tareas no la ve. CycleCountLine se alcanza por su conteo filtrado.
        return await (from cl in db.Set<CycleCountLine>().AsNoTracking()
                      join c in db.Set<CycleCount>().AsNoTracking() on cl.CycleCountId equals c.CycleCountId
                      where cl.ProductId == productId && c.IsActive && openCountIds.Contains(c.StatusCodeId)
                      select cl.CycleCountLineId).AnyAsync(ct);
    }
}
