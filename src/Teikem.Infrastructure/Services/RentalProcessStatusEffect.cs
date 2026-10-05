using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 28 (Rentas R2, plan 4.5) — efecto del dominio RentalProcessStatus. Depende de los TERMINALES, no de los pasos intermedios
/// (que la compañía renombra, desactiva y reordena libremente):
/// - READY: libera la reserva del equipo en la posición del proceso (serie IN_PROCESS → AVAILABLE): el disponible vuelve.
/// - SCRAPPED: ADJUSTMENT − del equipo desde la posición del proceso, consumiendo lo reservado, con el motivo DAMAGE y la referencia
///   RENTAL_PROCESS; la serie queda SCRAPPED (dada de baja: no vuelve al inventario).
/// En los dos sella CompletedAtUtc. Corre dentro de StatusService.TransitionAsync, en la transacción del llamador (que ya bloqueó el
/// proceso); toma el proceso del ChangeTracker (o lo carga tracked) y no guarda (lo hace el ledger y luego el llamador).
/// El ledger se resuelve con IServiceProvider: InventoryLedger depende de StatusService y StatusService de los efectos (sin esto se
/// forma el ciclo StatusService ↔ InventoryLedger), igual que WarehouseTaskStatusEffect.
/// </summary>
public sealed class RentalProcessStatusEffect(TeikemDbContext db, IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.RentalProcessStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.RentalProcess, StringComparison.OrdinalIgnoreCase)) return;
        var to = context.To.InternalCode;
        if (to is not (RentalProcessStatuses.Ready or RentalProcessStatuses.Scrapped)) return;

        var processId = context.EntityId;
        var process = db.RentalProcesses.Local.FirstOrDefault(p => p.RentalProcessId == processId)
                      ?? await db.RentalProcesses.FirstOrDefaultAsync(p => p.RentalProcessId == processId, ct);
        if (process is null) return;
        var serial = await db.InventorySerials.AsNoTracking()
            .Where(s => s.SerialId == process.SerialId && s.ProductId == process.ProductId)
            .Select(s => new { s.SerialNumber, s.LotId }).FirstAsync(ct);

        var ledger = services.GetRequiredService<InventoryLedger>();
        if (to == RentalProcessStatuses.Ready)
        {
            await ledger.ReleaseAsync(new[]
            {
                new StockReservation(process.ProductId, process.WarehouseId, process.BinId, serial.LotId, 1m, new[] { serial.SerialNumber },
                    SerialStatuses.InProcess, SerialStatuses.Available),
            }, ct);
        }
        else
        {
            await ledger.PostAsync(new[]
            {
                new InventoryPosting(InventoryTxnTypes.Adjustment, process.ProductId, 1m, LotId: serial.LotId, SerialNumber: serial.SerialNumber,
                    FromWarehouseId: process.WarehouseId, FromBinId: process.BinId, RefEntityType: EntityTypes.RentalProcess, RefId: process.RentalProcessId,
                    ReasonCode: AdjustmentReasons.Damage, Notes: RentalRules.ScrapNotes(process.RentalProcessId), FromReserved: true,
                    ExpectedSerialStatus: SerialStatuses.InProcess, TargetSerialStatus: SerialStatuses.Scrapped),
            }, ct);
        }
        process.CompletedAtUtc ??= DateTime.UtcNow;
    }
}
