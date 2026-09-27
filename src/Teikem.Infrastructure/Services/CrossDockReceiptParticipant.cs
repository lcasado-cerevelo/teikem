using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P9) — participante de la confirmación del recibo (IReceiptConfirmationParticipant, D29 modo a). Lo llama
/// ReceiptService dentro de la transacción de confirmación, con el recibo YA bloqueado, después del ledger (la mercancía ya
/// está en staging) y antes de crear las PUTAWAY. NO bloquea el plan: escribe asignaciones bajo el bloqueo del recibo, así
/// que no invierte el orden Plan → ReceiptHeader de los escritores del plan.
/// Por línea con asignaciones PLANNED todavía sin repartir (ConfirmedQty NULL):
/// - valida que la posición de recepción esté en la zona de staging del plan (400 con Errors por línea);
/// - reparte FIFO lo recibido (CrossDockRules.Split, orden CreatedAtUtc/Id): ConfirmedQty por asignación (0 = faltante
///   total, visible), reserva en el ledger lo cubierto sobre el saldo de staging y crea una tarea CROSSDOCK por asignación
///   con mercancía (Ref CROSSDOCK_ALLOCATION);
/// - devuelve Σ ConfirmedQty por ReceiptLineId: ReceiptService descuenta eso del putaway.
/// Sin asignaciones devuelve un diccionario vacío (sin escribir nada).
/// Excepción documentada al módulo (D28): NO exige CROSSDOCK encendido. Confirmar un recibo es WMS_LOTSERIAL, y las
/// asignaciones PLANNED solo existen si el módulo estuvo encendido al asignarlas; apagarlo no debe impedir recibir. Las
/// reservas y tareas CROSSDOCK que nazcan aquí quedan en espera: moverlas (pantalla o cola) exige el módulo encendido.
/// </summary>
public sealed class CrossDockReceiptParticipant(
    TeikemDbContext db,
    InventoryLedger ledger,
    WarehouseTaskWriter taskWriter) : IReceiptConfirmationParticipant
{
    public async Task<IReadOnlyDictionary<int, decimal>> OnReceiptConfirmedAsync(ReceiptHeader receipt, IReadOnlyList<ReceiptLine> lines, CancellationToken ct)
    {
        var result = new Dictionary<int, decimal>();
        if (lines.Count == 0) return result;
        var lineIds = lines.Select(l => l.ReceiptLineId).ToList();
        var plannedId = await db.StatusIdAsync(StatusDomains.AllocationStatus, AllocationStatuses.Planned, ct);

        // Asignaciones (hijas sin TenantId) alcanzadas por su plan filtrado; tracked para guardar ConfirmedQty.
        var rows = await (from a in db.Set<CrossDockAllocation>()
                          join p in db.Set<CrossDockPlan>() on a.CrossDockPlanId equals p.CrossDockPlanId
                          where lineIds.Contains(a.ReceiptLineId) && a.StatusCodeId == plannedId && a.ConfirmedQty == null
                                && p.WarehouseId == receipt.WarehouseId
                          select new { Allocation = a, p.StagingZoneId }).ToListAsync(ct);
        if (rows.Count == 0) return result;

        // Zona de staging del plan: la posición de recepción de la línea debe estar en ella.
        var byLineId = lines.ToDictionary(l => l.ReceiptLineId);
        var binIds = lines.Where(l => l.StagingBinId != null).Select(l => l.StagingBinId!.Value).Distinct().ToList();
        var binZones = await (from b in db.Set<WarehouseBin>().AsNoTracking()
                              join w in db.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.WarehouseId
                              where binIds.Contains(b.WarehouseBinId) && b.WarehouseId == receipt.WarehouseId
                              select new { b.WarehouseBinId, b.WarehouseZoneId }).ToDictionaryAsync(x => x.WarehouseBinId, x => x.WarehouseZoneId, ct);
        var errors = new Dictionary<string, string[]>();
        foreach (var row in rows)
        {
            if (row.StagingZoneId is not int zoneId) continue;
            var line = byLineId[row.Allocation.ReceiptLineId];
            if (line.ReceivedQty <= 0m) continue;   // nada que mover: el faltante total no exige posición
            var zone = line.StagingBinId is int bin ? binZones.GetValueOrDefault(bin) : (int?)null;
            if (zone != zoneId) errors[$"lines[{line.ReceiptLineId}]"] = new[] { CrossDockRules.StagingZone };
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        // Reparto FIFO por línea con lo recibido final.
        var reservations = new List<StockReservation>();
        var withGoods = new List<(CrossDockAllocation Allocation, ReceiptLine Line, decimal Qty)>();
        foreach (var group in rows.GroupBy(r => r.Allocation.ReceiptLineId))
        {
            var line = byLineId[group.Key];
            var ordered = group.Select(r => r.Allocation).OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.CrossDockAllocationId).ToList();
            var split = CrossDockRules.Split(line.ReceivedQty, ordered.Select(a => new CrossDockSplitInput(a.CrossDockAllocationId, a.AllocatedQty)).ToList());
            // Con serie (L328): cada asignación reserva su tramo de las series recibidas en la línea (en su orden).
            var lineSerials = CrossDockSupport.ParseSerials(line.SerialNumbersJson);
            var serialCursor = 0;
            foreach (var share in split.Shares)
            {
                var allocation = ordered.First(a => a.CrossDockAllocationId == share.AllocationId);
                allocation.ConfirmedQty = share.ConfirmedQty;
                if (share.ConfirmedQty <= 0m || line.StagingBinId is not int stagingBin) continue;
                IReadOnlyList<string>? serials = null;
                if (lineSerials.Count > 0)
                {
                    serials = lineSerials.Skip(serialCursor).Take((int)share.ConfirmedQty).ToList();
                    serialCursor += (int)share.ConfirmedQty;
                }
                reservations.Add(new StockReservation(line.ProductId, receipt.WarehouseId, stagingBin, line.LotId, share.ConfirmedQty, serials));
                withGoods.Add((allocation, line, share.ConfirmedQty));
            }
            if (split.TotalConfirmed > 0m) result[line.ReceiptLineId] = split.TotalConfirmed;
        }

        // Lo cubierto queda RESERVADO en staging (el ledger ordena por clave y verifica el disponible: 409 si no alcanza).
        if (reservations.Count > 0) await ledger.ReserveAsync(reservations, ct);

        // Una tarea CROSSDOCK por asignación con mercancía; se completa desde la cola (Mover).
        var created = new List<(CrossDockAllocation Allocation, WarehouseTask Task)>();
        foreach (var (allocation, line, qty) in withGoods)
        {
            var task = await CrossDockSupport.CreateTaskAsync(db, taskWriter,
                CrossDockSupport.CrossDockTaskSpec(receipt.WarehouseId, line, allocation, qty), ct);
            created.Add((allocation, task));
        }
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        foreach (var (allocation, task) in created) allocation.WarehouseTaskId = task.WarehouseTaskId;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return result;
    }
}
