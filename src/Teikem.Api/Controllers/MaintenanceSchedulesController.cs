using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P4) — panel Mantenimiento preventivo. Módulo CATALOG; lectura con fleet.view y escritura con fleet.maintenance.
/// Un programa aplica a un vehículo o a un tipo de vehículo (no ambos) con disparador MILEAGE, TIME o BOTH. 'due' evalúa
/// cada programa activo sobre sus vehículos activos: OK (Al día), DUE_SOON (Por vencer), OVERDUE (Vencido) o
/// NO_BASELINE (Sin historial). Nunca DELETE: deactivate/reactivate.
/// </summary>
[ApiController]
[Route("api/v1/maintenance-schedules")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class MaintenanceSchedulesController(MaintenanceScheduleService schedules) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<MaintenanceScheduleDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => schedules.ListAsync(includeInactive, ct);

    /// <summary>Panel: estado de cada programa por vehículo. Filtros vehiclePublicId y status (OK, DUE_SOON, OVERDUE, NO_BASELINE; ?status=A&amp;status=B o A,B).</summary>
    [HttpGet("due"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<MaintenanceDueDto>> Due([FromQuery] Guid? vehiclePublicId, [FromQuery] string[]? status, CancellationToken ct)
        => schedules.GetDueAsync(new MaintenanceDueQuery(vehiclePublicId, status is { Length: > 0 } ? status : null), ct);

    [HttpPost, RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<MaintenanceScheduleDto> Create([FromBody] MaintenanceScheduleRequest req, CancellationToken ct) => schedules.CreateAsync(req, ct);

    /// <summary>Edición en línea: null = sin cambio; clearIntervalKm/clearIntervalDays quitan un intervalo. Se revalida contra el disparador.</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<MaintenanceScheduleDto> Update(int id, [FromBody] MaintenanceScheduleRequest req, CancellationToken ct) => schedules.UpdateAsync(id, req, ct);

    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct) { await schedules.SetActiveAsync(id, false, ct); return NoContent(); }

    [HttpPost("{id:int}/reactivate"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public async Task<IActionResult> Reactivate(int id, CancellationToken ct) { await schedules.SetActiveAsync(id, true, ct); return NoContent(); }
}
