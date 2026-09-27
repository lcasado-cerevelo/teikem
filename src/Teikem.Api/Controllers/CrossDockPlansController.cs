using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P9) — planes de cruce de muelle XD-##### (R20-R22, D29) bajo el módulo CROSSDOCK (demo). Lectura con
/// inventory.view; crear, asignar, cancelar asignaciones, mover y completar con warehouse.crossdock. Las asignaciones
/// (hijas sin TenantId) se alcanzan SIEMPRE dentro de su plan: una asignación de otro plan → 404. El movimiento también se
/// hace desde la cola de tareas (POST /api/v1/warehouse-tasks/{id}/complete sobre la tarea CROSSDOCK).
/// </summary>
[ApiController]
[Route("api/v1/cross-dock-plans")]
[Authorize]
[RequireModule(ModuleKeys.CrossDock)]
public sealed class CrossDockPlansController(CrossDockService crossDock) : ControllerBase
{
    /// <summary>Planes del tenant (máximo 200, más recientes primero) por almacén y estatus, con sus asignaciones.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<CrossDockPlanDto>> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? status, CancellationToken ct)
        => crossDock.ListAsync(warehousePublicId, status is { Length: > 0 } ? status : null, ct);

    /// <summary>Ficha del plan: asignaciones con lo asignado, lo confirmado y el faltante outbound (shortQty).</summary>
    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<CrossDockPlanDto> Get(int id, CancellationToken ct) => crossDock.GetAsync(id, ct);

    /// <summary>Líneas asignables: de recibos abiertos (se reparten al confirmar) y confirmados con putaway pendiente.</summary>
    [HttpGet("{id:int}/candidates"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<CrossDockCandidateDto>> Candidates(int id, CancellationToken ct) => crossDock.CandidatesAsync(id, ct);

    /// <summary>Crea un plan OPEN en el almacén (o el único activo) con zona de staging CROSSDOCK o STAGING opcional.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<CrossDockPlanDto> Create([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CrossDockPlanRequest? req, CancellationToken ct)
        => crossDock.CreateAsync(req, ct);

    /// <summary>
    /// Asigna una línea de recibo a una orden: recibo abierto → se reparte al confirmar; recibo confirmado → reduce la
    /// PUTAWAY, reserva el staging y crea la tarea CROSSDOCK. Más de lo disponible → 409.
    /// </summary>
    [HttpPost("{id:int}/allocations"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<CrossDockPlanDto> Allocate(int id, [FromBody] CrossDockAllocationRequest req, CancellationToken ct)
        => crossDock.AllocateAsync(id, req, ct);

    /// <summary>Cancela una asignación PLANNED: libera la reserva y devuelve lo confirmado a putaway (R22). El comentario va al historial.</summary>
    [HttpDelete("{id:int}/allocations/{allocationId:int}"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<CrossDockPlanDto> CancelAllocation(int id, int allocationId, [FromQuery] string? comment, CancellationToken ct)
        => crossDock.CancelAllocationAsync(id, allocationId, comment, ct);

    /// <summary>Mueve la asignación: CROSSDOCK que sale del inventario por lo confirmado; 422 con el recibo abierto o sin mercancía.</summary>
    [HttpPost("{id:int}/allocations/{allocationId:int}/move"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<CrossDockPlanDto> Move(int id, int allocationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CrossDockMoveRequest? req, CancellationToken ct)
        => crossDock.MoveAsync(id, allocationId, req, ct);

    /// <summary>Completa el plan (terminal); 422 si queda alguna asignación sin mover ni cancelar.</summary>
    [HttpPost("{id:int}/complete"), RequirePermission(PermissionCatalog.WarehouseCrossdock)]
    public Task<CrossDockPlanDto> Complete(int id, CancellationToken ct) => crossDock.CompleteAsync(id, ct);
}
