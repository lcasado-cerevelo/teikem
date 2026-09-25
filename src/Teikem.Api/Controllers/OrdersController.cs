using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 3 (P2/P3): órdenes de transporte. Módulo núcleo LTL_GROUND (DECISIÓN 12). Todas las acciones pasan OrderScope.Any
/// (operador interno); el controlador del portal (Lote 8) pasará el ClientId del principal (DECISIÓN 19).
/// No hay DELETE físico: 'eliminar' es baja lógica solo en la etapa inicial (DECISIÓN 6) y exige orders.cancel.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class OrdersController(OrderService orders, OrderReadService reader) : ControllerBase
{
    /// <summary>Listado paginado (Empaque primera columna). Filtros parciales por orden/factura/empaque/consignatario; from inclusivo, to exclusivo sobre CreatedAtUtc.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.OrdersView)]
    public Task<OrderPageDto> List(
        [FromQuery] Guid? clientId,
        [FromQuery] string? status,
        [FromQuery] string? orderNumber,
        [FromQuery] string? invoice,
        [FromQuery] string? packBatch,
        [FromQuery] string? consignee,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
        => reader.GetListAsync(new OrderListQuery(clientId, status, orderNumber, invoice, packBatch, consignee, search, from, to, includeInactive, skip, take), OrderScope.Any, ct);

    /// <summary>Buscar o escanear: coincidencia EXACTA por número de orden, empaque o factura (todas las coincidencias).</summary>
    [HttpGet("lookup"), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<OrderLookupDto> Lookup([FromQuery] string? code, CancellationToken ct)
        => reader.LookupAsync(code ?? string.Empty, OrderScope.Any, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersView)]
    public Task<OrderDetailDto> Get(Guid publicId, CancellationToken ct) => reader.GetAsync(publicId, OrderScope.Any, ct);

    /// <summary>Entrada rápida, detallada o especial. confirmNow=true crea y confirma en una sola transacción (DECISIÓN 26).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.OrdersCreate)]
    public Task<OrderDetailDto> Create([FromBody] OrderCreateRequest req, CancellationToken ct)
        => orders.CreateAsync(req, OrderScope.Any, null, ct);

    /// <summary>Edición de carga (EDIT_CARGO, por defecto solo en DRAFT). Los campos fijos al crear responden 400.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersEdit)]
    public Task<OrderDetailDto> Update(Guid publicId, [FromBody] OrderPatchRequest req, CancellationToken ct)
        => orders.UpdateAsync(publicId, req, OrderScope.Any, ct);

    /// <summary>Baja lógica solo en la etapa inicial; libera el número de orden y el de empaque. Después use /cancel.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.OrdersCancel)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await orders.DeleteAsync(publicId, OrderScope.Any, ct);
        return NoContent();
    }
}
