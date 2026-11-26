using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 247 (Lote C) — «Precios especiales» de la ficha del cliente y de la cadena (módulo SALES_ORDERS). Ver con sales.view o sales.pricelist; agregar, cambiar y
/// quitar con sales.pricelist. Por dentro cada precio especial es un renglón de una lista dedicada del dueño, una por vigencia; el precio es ANTES del IVU (las
/// exenciones del cliente se aplican después). Todas responden la vista completa (SpecialPricesDto); includeExpired = también los vencidos.
/// Códigos: 400 validación, 404 dueño, producto o precio especial, 409 cruce de fechas o alta simultánea, 422 dueño inactivo o producto que no se puede poner.
/// </summary>
[ApiController]
[Route("api/v1/special-prices")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SpecialPricesController(PriceListService lists) : ControllerBase
{
    private const string View = PermissionCatalog.SalesView + "|" + PermissionCatalog.SalesPricelist;

    // ---------------------------------------------------------------- cliente

    /// <summary>Los precios especiales del cliente, los de su cadena (solo lectura), las listas que lo alcanzan y lo que se cobra hoy.</summary>
    [HttpGet("clients/{clientPublicId:guid}"), RequirePermission(View)]
    public Task<SpecialPricesDto> GetClient(Guid clientPublicId, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.GetClientSpecialPricesAsync(clientPublicId, includeExpired, ct);

    /// <summary>Agrega un precio especial al cliente (o cambia el precio si el producto ya lo tiene con esas fechas).</summary>
    [HttpPost("clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> AddClient(Guid clientPublicId, [FromBody] SpecialPriceRequest req, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.AddClientSpecialPriceAsync(clientPublicId, req, ct, includeExpired);

    /// <summary>Cambia el precio o las fechas de un precio especial del cliente (con fechas nuevas pasa a la lista de esas fechas).</summary>
    [HttpPatch("clients/{clientPublicId:guid}/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> UpdateClient(Guid clientPublicId, int itemId, [FromBody] SpecialPricePatchRequest req, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.UpdateClientSpecialPriceAsync(clientPublicId, itemId, req, ct, includeExpired);

    /// <summary>Quita un precio especial del cliente (en lógico).</summary>
    [HttpDelete("clients/{clientPublicId:guid}/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> RemoveClient(Guid clientPublicId, int itemId, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.RemoveClientSpecialPriceAsync(clientPublicId, itemId, ct, includeExpired);

    // ---------------------------------------------------------------- cadena

    /// <summary>Los precios especiales de la cadena, las listas que la incluyen y lo que se cobra hoy a un miembro sin precio propio.</summary>
    [HttpGet("chains/{chainPublicId:guid}"), RequirePermission(View)]
    public Task<SpecialPricesDto> GetChain(Guid chainPublicId, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.GetChainSpecialPricesAsync(chainPublicId, includeExpired, ct);

    /// <summary>Agrega un precio especial a la cadena (o cambia el precio si el producto ya lo tiene con esas fechas).</summary>
    [HttpPost("chains/{chainPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> AddChain(Guid chainPublicId, [FromBody] SpecialPriceRequest req, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.AddChainSpecialPriceAsync(chainPublicId, req, ct, includeExpired);

    /// <summary>Cambia el precio o las fechas de un precio especial de la cadena.</summary>
    [HttpPatch("chains/{chainPublicId:guid}/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> UpdateChain(Guid chainPublicId, int itemId, [FromBody] SpecialPricePatchRequest req, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.UpdateChainSpecialPriceAsync(chainPublicId, itemId, req, ct, includeExpired);

    /// <summary>Quita un precio especial de la cadena (en lógico).</summary>
    [HttpDelete("chains/{chainPublicId:guid}/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<SpecialPricesDto> RemoveChain(Guid chainPublicId, int itemId, [FromQuery] bool includeExpired, CancellationToken ct)
        => lists.RemoveChainSpecialPriceAsync(chainPublicId, itemId, ct, includeExpired);
}
