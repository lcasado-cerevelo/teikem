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
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => sales.ListAsync(status, clientPublicId, q, backorderOnly, from, to, skip, take, ct);

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

    [HttpPost("{publicId:guid}/cancel"), RequirePermission(PermissionCatalog.SalesCancel)]
    public Task<SalesOrderDto> Cancel(Guid publicId, [FromBody] SalesOrderCancelRequest? req, CancellationToken ct) => sales.CancelAsync(publicId, req?.Comment, ct);
}
