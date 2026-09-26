using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P4) — panel Órdenes de trabajo de mantenimiento (EntityType WORK_ORDER). Módulo CATALOG; lectura con fleet.view
/// y escritura con fleet.maintenance. Número automático OT-#####; estatus OPEN → IN_PROGRESS → CLOSED (CANCELLED terminal)
/// vía StatusService, con efecto en el estatus y el odómetro del vehículo. Editar el encabezado y las tareas exige la
/// capacidad EDIT_WORK_ORDER (por defecto denegada en CLOSED/CANCELLED → 422). Las tareas se exponen con id entero SOLO
/// bajo su OT (404 si la tarea es de otra OT u otro tenant).
/// Historial: /api/v1/status/history/WORK_ORDER/{id}; capacidades: /api/v1/status/capabilities/WORK_ORDER.
/// </summary>
[ApiController]
[Route("api/v1/maintenance-work-orders")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class MaintenanceWorkOrdersController(MaintenanceWorkOrderService workOrders) : ControllerBase
{
    /// <summary>Lista paginada (más recientes primero). from/to inclusivos sobre la fecha programada (o la de alta si no tiene).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<WorkOrderListItemDto>> List(
        [FromQuery] Guid? vehiclePublicId,
        [FromQuery] string[]? status,
        [FromQuery] string[]? maintenanceType,
        [FromQuery] int? scheduleId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
        => workOrders.ListAsync(new WorkOrderListQuery(vehiclePublicId, NullIfEmpty(status), NullIfEmpty(maintenanceType), scheduleId, from, to,
            includeInactive, skip, take), ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<WorkOrderDetailDto> Get(Guid publicId, CancellationToken ct) => workOrders.GetAsync(publicId, ct);

    /// <summary>Alta: vehículo activo; tipo PREVENTIVE por defecto si trae programa. Nace OPEN con número OT-#####.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> Create([FromBody] WorkOrderCreateRequest req, CancellationToken ct) => workOrders.CreateAsync(req, ct);

    /// <summary>Edición en línea: null = sin cambio; rowVersion opcional (409 si cambió). number y vehiclePublicId no se editan.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> Update(Guid publicId, [FromBody] WorkOrderPatchRequest req, CancellationToken ct) => workOrders.UpdateAsync(publicId, req, ct);

    /// <summary>Cambio de estatus. Para CLOSED: completedDate (hoy por defecto) y odometerKm (obligatorio si el programa es por km).</summary>
    [HttpPost("{publicId:guid}/status"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> TransitionStatus(Guid publicId, [FromBody] WorkOrderStatusRequest req, CancellationToken ct)
        => workOrders.TransitionStatusAsync(publicId, req, ct);

    // ---------------------------------------------------------------- tareas

    [HttpPost("{publicId:guid}/tasks"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> AddTask(Guid publicId, [FromBody] MaintenanceTaskRequest req, CancellationToken ct)
        => workOrders.AddTaskAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/tasks/{taskId:int}"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> UpdateTask(Guid publicId, int taskId, [FromBody] MaintenanceTaskRequest req, CancellationToken ct)
        => workOrders.UpdateTaskAsync(publicId, taskId, req, ct);

    [HttpPost("{publicId:guid}/tasks/{taskId:int}/deactivate"), RequirePermission(PermissionCatalog.FleetMaintenance)]
    public Task<WorkOrderDetailDto> DeactivateTask(Guid publicId, int taskId, CancellationToken ct)
        => workOrders.DeactivateTaskAsync(publicId, taskId, ct);

    private static string[]? NullIfEmpty(string[]? values) => values is { Length: > 0 } ? values : null;
}
