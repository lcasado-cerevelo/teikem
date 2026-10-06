using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P5) — cola unificada de tareas de almacén (R17, D41), putaway dirigido (R10, D24) y reabasto (R15, D23) bajo el
/// módulo WMS_LOTSERIAL. Lectura y sugerencias con inventory.view; asignar y cancelar con warehouse.manage; iniciar y
/// completar con inventory.view aquí + el permiso del handler del tipo en el servicio (PUTAWAY → warehouse.receive,
/// REPLENISH → warehouse.pick, COUNT → warehouse.count, CROSSDOCK → warehouse.crossdock; 403 con PERMISSION_DENIED);
/// correr el reabasto con warehouse.pick. La tarea no tiene PublicId: se expone por id entero y SIEMPRE bajo el filtro de
/// tenant (una tarea de otra compañía es 404). Historial de estatus: /api/v1/status/history/WAREHOUSE_TASK/{id}.
/// </summary>
[ApiController]
[Route("api/v1/warehouse-tasks")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class WarehouseTasksController(WarehouseTaskService tasks, ReplenishmentService replenishment) : ControllerBase
{
    /// <summary>
    /// Cola paginada (take ≤ 200) en orden prioridad → antigüedad. Filtros: warehousePublicId, types (PUTAWAY, REPLENISH,
    /// COUNT, CROSSDOCK...), status (sin filtro: solo abiertas salvo includeClosed), assignedToMe y assignedUserId.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseTaskPageDto> List([FromQuery] Guid? warehousePublicId, [FromQuery] string[]? types, [FromQuery] string[]? status,
        [FromQuery] bool assignedToMe = false, [FromQuery] int? assignedUserId = null, [FromQuery] bool includeClosed = false,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => tasks.ListAsync(new WarehouseTaskQuery(warehousePublicId, types is { Length: > 0 } ? types : null,
            status is { Length: > 0 } ? status : null, assignedToMe, assignedUserId, includeClosed, skip, take), ct);

    /// <summary>
    /// Posiciones sugeridas para guardar: por tarea (taskId) o por producto (productPublicId, warehousePublicId, lotId,
    /// quantity, fromBinId a excluir). Cada una con su razón y la clase de rotación FAST/SLOW (salidas de 30 días). take ≤ 10.
    /// </summary>
    [HttpGet("putaway-suggestions"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<PutawaySuggestionDto>> PutawaySuggestions([FromQuery] int? taskId, [FromQuery] Guid? warehousePublicId,
        [FromQuery] Guid? productPublicId, [FromQuery] int? lotId, [FromQuery] decimal? quantity, [FromQuery] int? fromBinId,
        [FromQuery] int? take, CancellationToken ct)
        => tasks.SuggestPutawayAsync(taskId, warehousePublicId, productPublicId, lotId, quantity, fromBinId, take, ct);

    /// <summary>Ficha de la tarea (completableFromQueue indica si se completa aquí o en su pantalla).</summary>
    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseTaskDto> Get(int id, CancellationToken ct) => tasks.GetAsync(id, ct);

    /// <summary>Asigna la tarea abierta a un miembro activo de la compañía (userId null la desasigna). 400 si no es miembro.</summary>
    [HttpPost("{id:int}/assign"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseTaskDto> Assign(int id, [FromBody] TaskAssignRequest req, CancellationToken ct) => tasks.AssignAsync(id, req, ct);

    /// <summary>Inicia la tarea (PENDING → IN_PROGRESS) con el permiso de su tipo; si no tenía asignado, queda asignada a quien la inicia.</summary>
    [HttpPost("{id:int}/start"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseTaskDto> Start(int id, CancellationToken ct) => tasks.StartAsync(id, ct);

    /// <summary>
    /// Completa la tarea (se permite sin iniciar): destino toBinId (o el sugerido), cantidad (o la de la tarea; el remanente
    /// queda como tarea nueva) y series si aplica. PUTAWAY/REPLENISH asientan un TRANSFER; CROSSDOCK mueve el cruce de muelle.
    /// COUNT se completa en Conteo cíclico (422). Dos completados simultáneos: uno 200 y otro 422.
    /// </summary>
    [HttpPost("{id:int}/complete"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseTaskDto> Complete(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TaskCompleteRequest? req,
        CancellationToken ct)
        => tasks.CompleteAsync(id, req, ct);

    /// <summary>
    /// Reparte una tarea PUTAWAY en varias posiciones de la misma cantidad (quantityPerBin) y en el orden de toBinIds, todo en una
    /// transacción. Solo caben las posiciones llenas (185 de 20 → 9 posiciones; los 5 sueltos quedan como tarea nueva). 400 si la
    /// cantidad o las posiciones no cuadran o se repiten; 422 si la tarea no es PUTAWAY, es de un producto con serie o ya no está abierta.
    /// </summary>
    [HttpPost("{id:int}/distribute"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<WarehouseTaskDto> Distribute(int id, [FromBody] TaskDistributeRequest req, CancellationToken ct)
        => tasks.DistributeAsync(id, req, ct);

    /// <summary>Cancela una PUTAWAY o REPLENISH abierta (el comentario queda en el historial). COUNT y CROSSDOCK se cancelan en su pantalla.</summary>
    [HttpPost("{id:int}/cancel"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<WarehouseTaskDto> Cancel(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TaskCancelRequest? req, CancellationToken ct)
        => tasks.CancelAsync(id, req, ct);

    /// <summary>Reabasto bajo demanda de las posiciones de picking del almacén (o del único activo): crea las REPLENISH; idempotente.</summary>
    [HttpPost("replenishment/run"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<ReplenishmentResultDto> RunReplenishment([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReplenishmentRunRequest? req,
        CancellationToken ct)
        => replenishment.RunAsync(req, ct);
}
