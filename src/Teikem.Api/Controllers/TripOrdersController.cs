using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P2): órdenes en la ruta (consolidación multi-cliente) y la lista 'Sin asignar' de Sala de despacho. Módulo núcleo
/// LTL_GROUND; leer con trips.view y modificar la ruta con trips.plan. Agregar es atómico (todas o ninguna) y no cambia el
/// estatus de la orden; quitar la libera sin tocar su estatus. Ninguna solicitud lleva TenantId ni ids internos de la ruta.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripOrdersController(TripOrderService orders) : ControllerBase
{
    /// <summary>
    /// Órdenes confirmadas sin ruta vigente, con la zona resuelta por CP o pueblo. Filtros: dispatchZoneId | noZone, postalCode
    /// (prefijo), city (sin acentos), clientPublicId, requestedFrom/requestedTo (inclusivos), search (después de los filtros).
    /// </summary>
    [HttpGet("unassigned-orders"), RequirePermission(PermissionCatalog.TripsView)]
    public Task<UnassignedOrderPageDto> Unassigned([FromQuery] UnassignedOrdersQuery query, CancellationToken ct)
        => orders.ListUnassignedAsync(query, ct);

    /// <summary>Agrega órdenes al final de la ruta (máximo 200 por solicitud; la ruta admite 300 paradas). Devuelve la ficha.</summary>
    [HttpPost("{publicId:guid}/orders"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Add(Guid publicId, [FromBody] TripOrdersAddRequest req, CancellationToken ct)
        => orders.AddOrdersAsync(publicId, req, ct);

    /// <summary>Quita (libera) una orden de la ruta: vuelve a 'Sin asignar' con su estatus; la ruta se resecuencia. Cuerpo opcional (rowVersion).</summary>
    [HttpDelete("{publicId:guid}/orders/{orderPublicId:guid}"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Remove(Guid publicId, Guid orderPublicId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TripOrderRemoveRequest? req, CancellationToken ct)
        => orders.RemoveOrderAsync(publicId, orderPublicId, req, ct);
}
