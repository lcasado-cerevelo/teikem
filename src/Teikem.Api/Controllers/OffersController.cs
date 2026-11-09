using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 229 — ofertas (módulo SALES_ORDERS): cosa aparte de las listas de precios, con nombre, foto, vigencia (sin «hasta» = no vence: HasEndDate false), publicada o
/// borrador, para todos o para clientes escogidos, y renglones (producto o kit → precio de oferta; o un kit creado para la oferta). Ver con sales.view; crear y cambiar
/// con sales.pricelist. Al pedir gana el precio más bajo entre el negociado y la oferta; los choques con otras ofertas o con la lista general salen en Warnings (no son
/// errores). Nunca se borran: se desactivan; renglones y clientes se quitan en lógico.
/// </summary>
[ApiController]
[Route("api/v1/offers")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class OffersController(OfferService offers, StoredFileService files) : ControllerBase
{
    /// <summary>Las ofertas (la de vigencia más reciente primero). q = el nombre contiene; state = DRAFT, SCHEDULED, ACTIVE o EXPIRED; includeInactive = también las desactivadas.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<OfferListItemDto>> List([FromQuery] string? q, [FromQuery] string? state, [FromQuery] bool includeInactive, CancellationToken ct)
        => offers.ListAsync(q, state, includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<OfferDto> Get(Guid publicId, CancellationToken ct) => offers.GetAsync(publicId, ct);

    /// <summary>Crea la oferta en borrador. 400: sin nombre, sin «desde», «hasta» antes de «desde». validTo vacío = sin fecha de fin.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Create([FromBody] OfferRequest req, CancellationToken ct) => offers.CreateAsync(req, ct);

    /// <summary>Edita la oferta (lo que no viene no cambia; clearValidTo = sin fecha de fin). 422 si está desactivada o al cambiar «aplica a todos» con clientes.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Update(Guid publicId, [FromBody] OfferRequest req, CancellationToken ct) => offers.UpdateAsync(publicId, req, ct);

    /// <summary>Publica la oferta: se aplica en su vigencia. 422 sin renglones o sin clientes (si no es para todos).</summary>
    [HttpPost("{publicId:guid}/publish"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Publish(Guid publicId, CancellationToken ct) => offers.PublishAsync(publicId, ct);

    /// <summary>La devuelve a borrador: deja de aplicarse y sus kits dejan de venderse.</summary>
    [HttpPost("{publicId:guid}/unpublish"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Unpublish(Guid publicId, CancellationToken ct) => offers.UnpublishAsync(publicId, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Deactivate(Guid publicId, CancellationToken ct) => offers.DeactivateAsync(publicId, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Reactivate(Guid publicId, CancellationToken ct) => offers.ReactivateAsync(publicId, ct);

    /// <summary>La foto de la oferta (JPG, PNG o WEBP, hasta Files:MaxMegabytes; se reduce a 1200 px). Se ve por GET /files/{imagePublicId} con sales.view.</summary>
    [HttpPost("{publicId:guid}/image"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesPricelist)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<OfferDto> SetImage(Guid publicId, IFormFile? file, CancellationToken ct)
    {
        var bytes = await ProductImageController.ReadAsync(file, files, ct);
        return await offers.SetImageAsync(publicId, file!.FileName, bytes, ct);
    }

    [HttpDelete("{publicId:guid}/image"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> ClearImage(Guid publicId, CancellationToken ct) => offers.ClearImageAsync(publicId, ct);

    /// <summary>Agrega renglones (producto o kit → precio de oferta) o cambia el precio de los que ya están. 422 producto de otro dueño, inactivo o kit de otra oferta.</summary>
    [HttpPost("{publicId:guid}/items"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> UpsertItems(Guid publicId, [FromBody] OfferItemsRequest req, CancellationToken ct) => offers.UpsertItemsAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/items/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> SetItemPrice(Guid publicId, int itemId, [FromBody] OfferItemPriceRequest req, CancellationToken ct) => offers.SetItemPriceAsync(publicId, itemId, req, ct);

    /// <summary>Quita el renglón (en lógico).</summary>
    [HttpDelete("{publicId:guid}/items/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> RemoveItem(Guid publicId, int itemId, CancellationToken ct) => offers.RemoveItemAsync(publicId, itemId, ct);

    /// <summary>La foto propia del renglón (JPG, PNG o WEBP). Sin ella se muestra la del producto.</summary>
    [HttpPost("{publicId:guid}/items/{itemId:int}/image"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesPricelist)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<OfferDto> SetItemImage(Guid publicId, int itemId, IFormFile? file, CancellationToken ct)
    {
        var bytes = await ProductImageController.ReadAsync(file, files, ct);
        return await offers.SetItemImageAsync(publicId, itemId, file!.FileName, bytes, ct);
    }

    /// <summary>Quita la foto propia del renglón: vuelve a mostrarse la del producto.</summary>
    [HttpDelete("{publicId:guid}/items/{itemId:int}/image"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> ClearItemImage(Guid publicId, int itemId, CancellationToken ct) => offers.ClearItemImageAsync(publicId, itemId, ct);

    /// <summary>Agrega clientes a una oferta que no es para todos. 422 si la oferta aplica a todos.</summary>
    [HttpPost("{publicId:guid}/clients"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> AddClients(Guid publicId, [FromBody] OfferClientsRequest req, CancellationToken ct) => offers.AddClientsAsync(publicId, req, ct);

    /// <summary>Quita un cliente de la oferta (en lógico). 422 si es el último de una oferta publicada.</summary>
    [HttpDelete("{publicId:guid}/clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> RemoveClient(Guid publicId, Guid clientPublicId, CancellationToken ct) => offers.RemoveClientAsync(publicId, clientPublicId, ct);

    /// <summary>
    /// Crea un kit PARA la oferta (SKU, nombre, piezas con cantidades y «no sustituible», precio de oferta): nace no ajustable y solo se vende mientras la oferta esté
    /// publicada y vigente. 400/409 con los mensajes del producto y del kit (SKU repetido = 409).
    /// </summary>
    [HttpPost("{publicId:guid}/kits"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferKitCreatedDto> CreateKit(Guid publicId, [FromBody] OfferKitRequest req, CancellationToken ct) => offers.CreateKitAsync(publicId, req, ct);

    /// <summary>Cambia el nombre y/o las piezas de un kit de la oferta. 422 si el kit no es de esta oferta.</summary>
    [HttpPut("{publicId:guid}/kits/{productPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> UpdateKit(Guid publicId, Guid productPublicId, [FromBody] OfferKitUpdateRequest req, CancellationToken ct)
        => offers.UpdateKitAsync(publicId, productPublicId, req, ct);
}
