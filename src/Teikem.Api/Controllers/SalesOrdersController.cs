using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Cuerpo opcional de cancelar: el motivo queda en el historial de estatus.</summary>
public sealed record SalesOrderCancelRequest(string? Comment = null);

/// <summary>
/// Lote 57 (P4, S1) — órdenes de venta (módulo SALES_ORDERS). Lectura con sales.view; crear y editar con sales.create; cancelar con sales.cancel; el precio
/// distinto del de lista exige además sales.price (lo valida el servicio). Las órdenes se exponen por PublicId y nunca se borran: se cancelan.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesOrdersController(SalesOrderService sales) : ControllerBase
{
    /// <summary>Lista paginada (más reciente primero). Filtros: status (DRAFT, REQUESTED, PARTIAL, SHIPPED, CANCELLED), cliente, buscador q (número, cliente, producto), solo con backorder y fechas de solicitud.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<PagedResult<SalesOrderListItemDto>> List([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] string? q, [FromQuery] bool backorderOnly,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct,
        [FromQuery] bool assignedToMe = false, [FromQuery] int? assignedUserId = null)
        => sales.ListAsync(status, clientPublicId, q, backorderOnly, from, to, skip, take, ct, assignedToMe, assignedUserId);

    /// <summary>Lote 195: asigna (o desasigna con userId null) una orden de venta solicitada o despachada en parte a quien la va a despachar. Supervisión.</summary>
    [HttpPost("{publicId:guid}/assign"), RequirePermission(PermissionCatalog.WarehouseManage)]
    public Task<SalesOrderDto> Assign(Guid publicId, [FromBody] TaskAssignRequest req, CancellationToken ct) => sales.AssignAsync(publicId, req, ct);

    /// <summary>Lote 195: «tomar» una orden de venta sin asignar. Quien puede recolectar.</summary>
    [HttpPost("{publicId:guid}/take"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesOrderDto> Take(Guid publicId, [FromBody] TaskTakeRequest? req, CancellationToken ct) => sales.TakeAsync(publicId, req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesOrderDto> Get(Guid publicId, CancellationToken ct) => sales.GetAsync(publicId, ct);

    /// <summary>Crea la orden en borrador (OV-#####). Sin precio en una línea se usa el precio de venta del producto; otro precio exige sales.price.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Create([FromBody] SalesOrderRequest req, CancellationToken ct) => sales.CreateAsync(req, ct);

    /// <summary>Edita una orden en borrador; las líneas, si vienen, reemplazan a las actuales.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Update(Guid publicId, [FromBody] SalesOrderRequest req, CancellationToken ct) => sales.UpdateAsync(publicId, req, ct);

    /// <summary>Qué pasaría si se solicitara hoy: pedido / disponible / faltante por línea y el crédito del cliente. No guarda nada.</summary>
    [HttpGet("{publicId:guid}/availability"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesAvailabilityDto> Availability(Guid publicId, CancellationToken ct) => sales.CheckAsync(publicId, ct);

    /// <summary>Solicita la orden (DRAFT → REQUESTED): valida y reserva en la misma transacción. Faltante = 409; allowBackorder reserva lo disponible; crédito excedido = 422 (overrideCredit con orders.credit_override).</summary>
    [HttpPost("{publicId:guid}/request"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> Request(Guid publicId, [FromBody] SalesOrderRequestRequest? req, CancellationToken ct) => sales.RequestAsync(publicId, req, ct);

    /// <summary>Cambia la cantidad y/o el precio de una línea de una orden solicitada (reajusta la reserva).</summary>
    [HttpPatch("{publicId:guid}/lines/{lineId:int}"), RequirePermission(PermissionCatalog.SalesCreate)]
    public Task<SalesOrderDto> ModifyLine(Guid publicId, int lineId, [FromBody] SalesOrderLineModifyRequest req, CancellationToken ct) => sales.ModifyLineAsync(publicId, lineId, req, ct);

    /// <summary>Líneas con backorder de las órdenes solicitadas, la más antigua primero, con lo disponible hoy y lo que se podría surtir ahora.</summary>
    [HttpGet("backorders"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<SalesBackorderLineDto>> Backorders([FromQuery] string? q, CancellationToken ct) => sales.BackordersAsync(q, ct);

    /// <summary>Surte todas las líneas con backorder que se puedan, de la más antigua a la más reciente.</summary>
    [HttpPost("backorders/fulfill"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesFulfillResultDto> FulfillAll(CancellationToken ct) => sales.FulfillAllAsync(ct);

    /// <summary>Reserva para la línea lo que haya (todo o parte de su backorder).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/fulfill"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesOrderDto> FulfillLine(Guid publicId, int lineId, CancellationToken ct) => sales.FulfillLineAsync(publicId, lineId, ct);

    /// <summary>Cierra la línea con faltante: no espera más inventario (lo reservado sigue para despacharse).</summary>
    [HttpPost("{publicId:guid}/lines/{lineId:int}/close"), RequirePermission(PermissionCatalog.SalesBackorder)]
    public Task<SalesOrderDto> CloseLine(Guid publicId, int lineId, [FromBody] SalesOrderCancelRequest? req, CancellationToken ct) => sales.CloseLineAsync(publicId, lineId, req?.Comment, ct);

    /// <summary>Despacha contra la orden: consume la reserva, saca el inventario y crea la recolección (por partes; nunca más de lo reservado). Con `delivery` también crea la entrega, todo en una transacción.</summary>
    [HttpPost("{publicId:guid}/dispatch"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesDispatchResultDto> Dispatch(Guid publicId, [FromBody] SalesDispatchRequest? req, [FromServices] SalesDeliveryService delivery, CancellationToken ct)
        => req?.Delivery is null ? sales.DispatchAsync(publicId, req, ct) : delivery.DispatchAndDeliverAsync(publicId, req, ct);

    /// <summary>Crea la entrega (orden de transporte) de un despacho de la orden de venta. Cliente y consignatario salen de la orden si no se mandan.</summary>
    [HttpPost("{publicId:guid}/delivery"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<SalesOrderDto> Delivery(Guid publicId, [FromBody] SalesDeliveryRequest? req, [FromServices] SalesDeliveryService delivery, CancellationToken ct) => delivery.CreateAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.SalesCancel)]
    public Task<SalesOrderDto> Cancel(Guid publicId, [FromBody] SalesOrderCancelRequest? req, CancellationToken ct) => sales.CancelAsync(publicId, req?.Comment, ct);
}
