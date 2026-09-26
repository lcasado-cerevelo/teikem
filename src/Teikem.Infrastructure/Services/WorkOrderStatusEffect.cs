using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio WorkOrderStatus (Lote 4, P4): mueve el vehículo y el programa preventivo según la OT.
/// - → IN_PROGRESS: si el vehículo está ACTIVE lo pasa a MAINTENANCE ('{número} en proceso').
/// - → CLOSED/CANCELLED: si el vehículo está en MAINTENANCE y no queda otra OT IN_PROGRESS activa, lo regresa a ACTIVE
///   ('{número} cerrada/cancelada'), aunque el MAINTENANCE se hubiera puesto a mano.
/// - → CLOSED además: el odómetro del vehículo sube a la lectura de cierre (máximo monotónico, FleetRules.RaiseOdometer) y,
///   si el programa es por vehículo, su último servicio (km y fecha) avanza a los de la OT.
/// - Un vehículo terminal (baja definitiva) no se toca.
/// El vehículo se carga SIEMPRE con FleetQueries.LockVehicleAsync (UPDLOCK, ROWLOCK, TenantId): el llamador corre dentro de
/// una transacción, así que esto no compite con cargas de combustible ni con la corrección manual del odómetro.
/// StatusService se resuelve de forma perezosa (como OrderStatusEffect): inyectarlo formaría un ciclo en DI
/// (StatusService → efectos → este efecto → StatusService). Corre antes de que el llamador guarde: todo queda en la misma
/// unidad de trabajo.
/// </summary>
public sealed class WorkOrderStatusEffect(TeikemDbContext db, IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.WorkOrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.WorkOrder, StringComparison.OrdinalIgnoreCase)) return;
        var toCode = context.To.InternalCode;
        var starting = string.Equals(toCode, WorkOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
        var closing = string.Equals(toCode, WorkOrderStatuses.Closed, StringComparison.OrdinalIgnoreCase);
        var cancelling = string.Equals(toCode, WorkOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
        if (!starting && !closing && !cancelling) return;

        // La entidad tracked del llamador (con CompletedDate/OdometerKm ya fijados al cerrar); si no está, se carga tracked
        // bajo el filtro global de tenant.
        var wo = db.MaintenanceWorkOrders.Local.FirstOrDefault(w => w.WorkOrderId == context.EntityId)
                 ?? await db.MaintenanceWorkOrders.FirstOrDefaultAsync(w => w.WorkOrderId == context.EntityId, ct);
        if (wo is null) return; // fuera del tenant: el llamador ya habría respondido 404

        if (closing) await AdvanceScheduleAsync(wo.MaintenanceScheduleId, wo.VehicleId, wo.OdometerKm, wo.CompletedDate, ct);

        var vehicle = await db.LockVehicleAsync(wo.VehicleId, ct);
        if (vehicle is null) return;
        if (await db.IsTerminalAsync(vehicle.StatusCodeId, ct)) return;

        if (closing) vehicle.CurrentOdometerKm = FleetRules.RaiseOdometer(vehicle.CurrentOdometerKm, wo.OdometerKm);

        var statuses = services.GetRequiredService<StatusService>();
        if (starting)
        {
            var activeId = await db.StatusIdAsync(StatusDomains.VehicleStatus, VehicleStatuses.Active, ct);
            if (vehicle.StatusCodeId != activeId) return;
            // Si el tenant deshabilitó MAINTENANCE, la OT avanza igual y el vehículo se queda como está.
            if (!await IsEnabledAsync(statuses, VehicleStatuses.Maintenance, ct)) return;
            var to = await statuses.TransitionAsync(StatusDomains.VehicleStatus, EntityTypes.Vehicle, vehicle.VehicleId, vehicle.StatusCodeId,
                VehicleStatuses.Maintenance, $"{wo.Number} en proceso", ct);
            vehicle.StatusCodeId = to.StatusCodeId;
            return;
        }

        // CLOSED / CANCELLED: regresa a ACTIVE si ninguna otra OT activa del vehículo sigue en proceso.
        var maintenanceId = await db.StatusIdAsync(StatusDomains.VehicleStatus, VehicleStatuses.Maintenance, ct);
        if (vehicle.StatusCodeId != maintenanceId) return;
        var inProgressId = await db.StatusIdAsync(StatusDomains.WorkOrderStatus, WorkOrderStatuses.InProgress, ct);
        var otherInProgress = await db.MaintenanceWorkOrders.AnyAsync(w => w.VehicleId == vehicle.VehicleId && w.IsActive
                                                                           && w.StatusCodeId == inProgressId && w.WorkOrderId != wo.WorkOrderId, ct);
        if (otherInProgress) return;
        var back = await statuses.TransitionAsync(StatusDomains.VehicleStatus, EntityTypes.Vehicle, vehicle.VehicleId, vehicle.StatusCodeId,
            VehicleStatuses.Active, $"{wo.Number} {(closing ? "cerrada" : "cancelada")}", ct);
        vehicle.StatusCodeId = back.StatusCodeId;
    }

    /// <summary>
    /// Programa por vehículo: su último servicio avanza a la lectura y la fecha de cierre de la OT (nunca retrocede).
    /// Un programa por tipo no guarda último servicio: se toma de sus OT cerradas (MaintenanceScheduleService.GetDueAsync).
    /// </summary>
    private async Task AdvanceScheduleAsync(int? scheduleId, int vehicleId, decimal? odometerKm, DateOnly? completedDate, CancellationToken ct)
    {
        if (scheduleId is not int sid) return;
        var schedule = await db.MaintenanceSchedules.FirstOrDefaultAsync(s => s.MaintenanceScheduleId == sid, ct);
        if (schedule is null || schedule.VehicleId != vehicleId) return;
        schedule.LastServiceKm = FleetRules.RaiseOdometer(schedule.LastServiceKm, odometerKm);
        if (completedDate is DateOnly done && (schedule.LastServiceDate is null || done > schedule.LastServiceDate)) schedule.LastServiceDate = done;
    }

    private static async Task<bool> IsEnabledAsync(StatusService statuses, string code, CancellationToken ct)
    {
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.VehicleStatus, includeDisabled: false, ct);
        return pipeline.Any(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}
