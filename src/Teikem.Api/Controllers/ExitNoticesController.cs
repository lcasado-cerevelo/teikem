using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 143 — avisos de salida (módulo WMS_LOTSERIAL): el dueño de la mercancía avisa qué quiere que el almacén saque. Sin precios. Lectura con inventory.view; crear,
/// modificar, cancelar y atender el pendiente con exitnotice.create; despachar con warehouse.pick. Se exponen por PublicId y nunca se borran: se cancelan.
/// </summary>
[ApiController]
[Route("api/v1/exit-notices")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ExitNoticesController(ExitNoticeService notices) : ControllerBase
{
    /// <summary>Lista paginada (más reciente primero). Filtros: status (REQUESTED, PARTIAL, SHIPPED, CANCELLED), cliente, buscador q (número, referencia, destino, cliente, producto), solo con pendiente y fechas de solicitud.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<PagedResult<ExitNoticeListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] string? q, [FromQuery] bool backorderOnly,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct,
        [FromQuery] bool assignedToMe = false, [FromQuery] int? assignedUserId = null)
        => notices.ListAsync(status, clientPublicId, q, backorderOnly, from, to, skip, take, ct, assignedToMe, assignedUserId);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ExitNoticeDto> Get(Guid publicId, CancellationToken ct) => notices.GetAsync(publicId, ct);

    /// <summary>Crea el aviso (SAL-#####) y reserva en el acto lo que haya con la rotación de la compañía; lo que falte queda pendiente. Solo productos activos del cliente dueño.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> Create([FromBody] ExitNoticeRequest req, CancellationToken ct) => notices.CreateAsync(req, ct);

    /// <summary>Edita referencia, destino, fecha requerida y/o notas de un aviso solicitado (texto vacío lo quita).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> UpdateHeader(Guid publicId, [FromBody] ExitNoticeHeaderRequest req, CancellationToken ct) => notices.UpdateHeaderAsync(publicId, req, ct);

    /// <summary>Cambia la cantidad pedida de una línea de un aviso solicitado (reajusta la reserva).</summary>
    [HttpPatch("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> ModifyLine(Guid publicId, int lineId, [FromBody] ExitNoticeLineModifyRequest req, CancellationToken ct) => notices.ModifyLineAsync(publicId, lineId, req, ct);

    /// <summary>Cancela el aviso: libera lo reservado y borra el pendiente. Uno con algo despachado no se cancela.</summary>
    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> Cancel(Guid publicId, [FromBody] ExitNoticeCommentRequest? req, CancellationToken ct) => notices.CancelAsync(publicId, req?.Comment, ct);

    /// <summary>Líneas con pendiente de los avisos solicitados o parciales, la más antigua primero, con lo disponible hoy y lo que se podría reservar ahora.</summary>
    [HttpGet("backorders"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ExitNoticeBackorderLineDto>> Backorders([FromQuery] string? q, CancellationToken ct) => notices.BackordersAsync(q, ct);

    /// <summary>Reserva todas las líneas con pendiente que se puedan, de la más antigua a la más reciente.</summary>
    [HttpPost("backorders/fulfill"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeFulfillResultDto> FulfillAll(CancellationToken ct) => notices.FulfillAllAsync(ct);

    /// <summary>Reserva para la línea lo que haya (todo o parte de su pendiente).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/fulfill"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> FulfillLine(Guid publicId, int lineId, CancellationToken ct) => notices.FulfillLineAsync(publicId, lineId, ct);

    /// <summary>Cierra la línea con faltante: no espera más inventario (lo ya reservado sigue para despacharse).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/close"), RequirePermission(PermissionCatalog.ExitNoticeCreate)]
    public Task<ExitNoticeDto> CloseLine(Guid publicId, int lineId, [FromBody] ExitNoticeCommentRequest? req, CancellationToken ct) => notices.CloseLineAsync(publicId, lineId, req?.Comment, ct);

    /// <summary>Despacha contra el aviso: consume la reserva, saca el inventario y crea la recolección (por partes; nunca más de lo reservado).</summary>
    /// <summary>Lote 171: asigna (o desasigna con userId null) un aviso de salida abierto. Supervisión.</summary>
    [HttpPost("{publicId:guid}/assign"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<ExitNoticeDto> Assign(Guid publicId, [FromBody] TaskAssignRequest req, CancellationToken ct) => notices.AssignAsync(publicId, req, ct);

    /// <summary>Lote 171: «tomar» un aviso de salida sin asignar. Quien puede recolectar.</summary>
    [HttpPost("{publicId:guid}/take"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<ExitNoticeDto> Take(Guid publicId, [FromBody] TaskTakeRequest? req, CancellationToken ct) => notices.TakeAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/dispatch"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<ExitNoticeDispatchResultDto> Dispatch(Guid publicId, [FromBody] ExitNoticeDispatchRequest? req, CancellationToken ct) => notices.DispatchAsync(publicId, req, ct);
}
