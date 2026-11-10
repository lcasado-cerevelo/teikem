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
/// borrador, para todos o para clientes escogidos, y renglones (producto o kit → precio de oferta; Lote 231: los kits de oferta viven en la biblioteca /offers/kits y
/// una oferta los usa con un renglón). Ver con sales.view; crear y cambiar
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

    // ---------------------------------------------------------------- Lote 231 — biblioteca de kits de oferta

    /// <summary>
    /// La biblioteca de kits de oferta (por SKU, hasta 500): q = el SKU o el nombre contiene; includeInactive = también los dados de baja. Cada kit con sus piezas, si es
    /// ajustable, la última oferta en que se usó y su último precio (el que se sugiere al ponerlo en otra oferta), en cuántas ofertas está y si su composición ya no se
    /// puede cambiar (compositionLocked).
    /// </summary>
    [HttpGet("kits"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<OfferKitLibraryItemDto>> ListKits([FromQuery] string? q, [FromQuery] bool includeInactive, CancellationToken ct)
        => offers.ListKitsAsync(q, includeInactive, ct);

    /// <summary>Un kit de la biblioteca. 404 «Kit de oferta no encontrado.».</summary>
    [HttpGet("kits/{productPublicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<OfferKitLibraryItemDto> GetKit(Guid productPublicId, CancellationToken ct) => offers.GetKitAsync(productPublicId, ct);

    /// <summary>
    /// Crea un kit en la biblioteca (sin ponerlo en una oferta): SKU, nombre, piezas con cantidad y «no sustituible», ajustable (por omisión no). 400/404/409 con los
    /// mensajes del producto y del kit (SKU repetido = 409). Para usarlo: POST /offers/{id}/items con su productPublicId y el precio.
    /// </summary>
    [HttpPost("kits"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferKitLibraryItemDto> CreateLibraryKit([FromBody] OfferKitLibraryRequest req, CancellationToken ct) => offers.CreateLibraryKitAsync(req, ct);

    /// <summary>
    /// Cambia un kit de la biblioteca: el nombre siempre; las piezas y «ajustable» solo si nunca se usó en una oferta ni en un pedido (si no, 422: cree un kit nuevo).
    /// </summary>
    [HttpPatch("kits/{productPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferKitLibraryItemDto> UpdateLibraryKit(Guid productPublicId, [FromBody] OfferKitUpdateRequest req, CancellationToken ct)
        => offers.UpdateLibraryKitAsync(productPublicId, req, ct);

    /// <summary>Crea la oferta, ya publicada (lote 234: las ofertas no tienen borrador). 400: sin nombre, sin «desde», «hasta» antes de «desde». validTo vacío = sin fecha de fin.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Create([FromBody] OfferRequest req, CancellationToken ct) => offers.CreateAsync(req, ct);

    /// <summary>Edita la oferta (lo que no viene no cambia; clearValidTo = sin fecha de fin). 422 si está desactivada o al cambiar «aplica a todos» con clientes.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Update(Guid publicId, [FromBody] OfferRequest req, CancellationToken ct) => offers.UpdateAsync(publicId, req, ct);

    /// <summary>Publica una oferta que quedó en borrador (las de antes del lote 234). 422 sin renglones o sin clientes (si no es para todos).</summary>
    [HttpPost("{publicId:guid}/publish"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> Publish(Guid publicId, CancellationToken ct) => offers.PublishAsync(publicId, ct);

    /// <summary>Compatibilidad (la web ya no lo ofrece): la devuelve a borrador; deja de aplicarse y sus kits dejan de venderse.</summary>
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

    /// <summary>
    /// Agrega renglones (producto o kit → precio de oferta) o cambia el precio de los que ya están. 422 producto de otro dueño o inactivo. Lote 231: un kit de oferta de
    /// la biblioteca se agrega igual (también a varias ofertas); sin precio toma el último con que se usó (400 si nunca se usó).
    /// </summary>
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

    /// <summary>Quita un cliente de la oferta (en lógico). Sin clientes, la oferta no aplica a nadie hasta que se escoja otro.</summary>
    [HttpDelete("{publicId:guid}/clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> RemoveClient(Guid publicId, Guid clientPublicId, CancellationToken ct) => offers.RemoveClientAsync(publicId, clientPublicId, ct);

    /// <summary>
    /// Crea un kit de oferta NUEVO y lo pone en esta oferta (SKU, nombre, piezas con cantidades y «no sustituible», precio de oferta; ajustable opcional, por omisión
    /// no). Lote 231: el kit queda en la biblioteca (otras ofertas lo pueden usar) y se vende mientras esté en alguna oferta publicada y vigente. 400/409 con los
    /// mensajes del producto y del kit (SKU repetido = 409).
    /// </summary>
    [HttpPost("{publicId:guid}/kits"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferKitCreatedDto> CreateKit(Guid publicId, [FromBody] OfferKitRequest req, CancellationToken ct) => offers.CreateKitAsync(publicId, req, ct);

    /// <summary>
    /// Cambia el nombre y/o las piezas de un kit de oferta que está en esta oferta (lo mismo que PATCH /offers/kits/{kit}; las piezas solo si nunca se usó). 422 si el kit
    /// no está en esta oferta.
    /// </summary>
    [HttpPut("{publicId:guid}/kits/{productPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<OfferDto> UpdateKit(Guid publicId, Guid productPublicId, [FromBody] OfferKitUpdateRequest req, CancellationToken ct)
        => offers.UpdateKitAsync(publicId, productPublicId, req, ct);
}
