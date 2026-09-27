using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P9) — efecto del dominio AppointmentStatus sobre el muelle (R19b, D30):
/// - ARRIVED ocupa el muelle: FREE → OCCUPIED (historial WAREHOUSE_DOCK). Un muelle ya OCCUPIED o en MAINTENANCE no cambia.
/// - COMPLETED, NO_SHOW o CANCELLED lo liberan: OCCUPIED → FREE, solo si no queda OTRA cita ARRIVED en ese muelle.
///   MAINTENANCE nunca se toca (lo decide el jefe de almacén a mano).
/// Corre dentro de StatusService.TransitionAsync, en la transacción del llamador y ANTES de que éste asigne el nuevo estatus
/// de la cita: por eso la cita que transiciona se excluye por id. Precondición de bloqueo: DockAppointmentService ya bloqueó
/// el muelle (InventoryQueries); el efecto lo carga tracked (misma instancia) y no guarda: el llamador persiste todo junto.
/// StatusService se resuelve de forma perezosa (evita el ciclo StatusService → efectos → este efecto → StatusService).
/// </summary>
public sealed class DockAppointmentStatusEffect(TeikemDbContext db, IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.AppointmentStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.DockAppointment, StringComparison.OrdinalIgnoreCase)) return;
        var toCode = context.To.InternalCode;
        var occupy = DockScheduleRules.OccupiesDock(toCode);
        var release = DockScheduleRules.ReleasesDock(toCode);
        if (!occupy && !release) return;

        var apptId = context.EntityId;
        var appt = db.Set<DockAppointment>().Local.FirstOrDefault(a => a.DockAppointmentId == apptId)
                   ?? await db.Set<DockAppointment>().AsNoTracking().FirstOrDefaultAsync(a => a.DockAppointmentId == apptId, ct);
        if (appt is null) return;

        // Muelle (hija sin TenantId) por su almacén filtrado; tracked: la instancia ya bloqueada por el llamador.
        var dock = await (from d in db.Set<WarehouseDock>()
                          join w in db.Set<Warehouse>() on d.WarehouseId equals w.WarehouseId
                          where d.WarehouseDockId == appt.WarehouseDockId && d.WarehouseId == appt.WarehouseId
                          select d).FirstOrDefaultAsync(ct);
        if (dock is null) return;
        var dockCode = await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == dock.StatusCodeId)
            .Select(s => s.InternalCode).FirstOrDefaultAsync(ct);

        string? target = null;
        string? comment = null;
        if (occupy && string.Equals(dockCode, DockStatuses.Free, StringComparison.OrdinalIgnoreCase))
        {
            target = DockStatuses.Occupied;
            comment = $"Llegada de la cita {apptId}.";
        }
        else if (release && string.Equals(dockCode, DockStatuses.Occupied, StringComparison.OrdinalIgnoreCase))
        {
            if (await OtherArrivalAsync(appt, ct)) return;
            target = DockStatuses.Free;
            comment = $"Cita {apptId} cerrada ({toCode}).";
        }
        if (target is null) return;

        var statuses = services.GetRequiredService<StatusService>();
        var to = await statuses.TransitionAsync(StatusDomains.DockStatus, EntityTypes.WarehouseDock, dock.WarehouseDockId,
            dock.StatusCodeId, target, comment, ct);
        dock.StatusCodeId = to.StatusCodeId;
    }

    /// <summary>¿Queda otra cita ARRIVED en el muelle? (persistidas con sus valores en memoria + agregadas sin guardar).</summary>
    private async Task<bool> OtherArrivalAsync(DockAppointment appt, CancellationToken ct)
    {
        var arrivedId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.AppointmentStatus && s.InternalCode == AppointmentStatuses.Arrived)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (arrivedId is null) return false;
        var id = appt.DockAppointmentId;
        var dockId = appt.WarehouseDockId;
        var arrived = arrivedId.Value;
        var persisted = await db.Set<DockAppointment>()
            .Where(a => a.DockAppointmentId != id && a.WarehouseDockId == dockId && a.StatusCodeId == arrived)
            .ToListAsync(ct);
        return persisted
            .Concat(db.Set<DockAppointment>().Local.Where(a => a.DockAppointmentId != id && a.WarehouseDockId == dockId))
            .Any(a => a.StatusCodeId == arrived);
    }
}
