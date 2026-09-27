using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P9) — citas de muelle (R19b, D30) bajo el módulo CROSSDOCK (demo: apagado para Advance; 403 module_disabled
/// hasta que el tenant lo enciende). Lectura con inventory.view; agendar, reprogramar y cambiar estatus con
/// warehouse.crossdock. La cita no tiene PublicId: se expone por id entero bajo el filtro de tenant (otra compañía → 404).
/// Historial de estatus: /api/v1/status/history/DOCK_APPOINTMENT/{id}.
/// </summary>
[ApiController]
[Route("api/v1/dock-appointments")]
[Authorize]
[RequireModule(ModuleKeys.CrossDock)]
public sealed class DockAppointmentsController(DockAppointmentService appointments) : ControllerBase
{
    /// <summary>Agenda por almacén, muelle, ventana UTC (fromUtc/toUtc) y estatus, en orden de inicio (máximo 500).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<DockAppointmentDto>> List([FromQuery] Guid? warehousePublicId, [FromQuery] int? dockId,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] string[]? status, CancellationToken ct)
        => appointments.ListAsync(new DockAppointmentQuery(warehousePublicId, dockId, fromUtc, toUtc,
            status is { Length: > 0 } ? status : null), ct);

    /// <summary>
    /// Agenda una cita SCHEDULED: muelle del almacén (o del único activo), dirección compatible (400), ventana de ayer a +90
    /// días (400), aviso de llegada o viaje pero no ambos (400). Solapamiento en el muelle → 409.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<DockAppointmentDto> Create([FromBody] DockAppointmentRequest req, CancellationToken ct) => appointments.CreateAsync(req, ct);

    /// <summary>Reprograma una cita SCHEDULED (inicio, fin u otro muelle del mismo almacén); 409 si se solapa, 422 si no está agendada.</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<DockAppointmentDto> Reschedule(int id, [FromBody] DockAppointmentPatchRequest req, CancellationToken ct)
        => appointments.RescheduleAsync(id, req, ct);

    /// <summary>
    /// Cambia el estatus (ARRIVED ocupa el muelle; COMPLETED, NO_SHOW y CANCELLED lo liberan). El motivo de un NO_SHOW va en
    /// el comentario del historial. Transición no permitida → 422.
    /// </summary>
    [HttpPost("{id:int}/status"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<DockAppointmentDto> SetStatus(int id, [FromBody] DockAppointmentStatusRequest req, CancellationToken ct)
        => appointments.SetStatusAsync(id, req, ct);
}
