using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 221 (P1 del pedido de vendedores) — listas de precios y ofertas (módulo SALES_ORDERS). Ver con sales.view; crear, publicar y cambiar con sales.pricelist.
/// Al pedir, el precio sale de la lista vigente del cliente, si no de la general, y si no del precio de venta del producto; el vendedor no teclea precios (otro precio
/// exige sales.price). Las listas se exponen por PublicId y nunca se borran: se desactivan; renglones y clientes se quitan en lógico.
/// </summary>
[ApiController]
[Route("api/v1/price-lists")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class PriceListsController(PriceListService lists, SalesCatalogService catalog, StoredFileService files) : ControllerBase
{
    /// <summary>Las listas (la de vigencia más reciente primero). q = el nombre contiene; state = DRAFT, SCHEDULED, ACTIVE o EXPIRED; includeInactive = también las desactivadas.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<PriceListListItemDto>> List([FromQuery] string? q, [FromQuery] string? state, [FromQuery] bool includeInactive, CancellationToken ct)
        => lists.ListAsync(q, state, includeInactive, ct);

    /// <summary>Las ofertas: kits marcados como promoción, con su vigencia, su foto y si se venden hoy (inEffect). Para la pestaña Precios y ofertas.</summary>
    [HttpGet("offers"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<IReadOnlyList<ProductPromoDto>> Offers([FromQuery] bool includeInactive, CancellationToken ct) => catalog.ListOffersAsync(includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesView)]
    public Task<PriceListDto> Get(Guid publicId, CancellationToken ct) => lists.GetAsync(publicId, ct);

    /// <summary>Crea la lista en borrador. 400: sin nombre, sin «desde», «hasta» antes de «desde».</summary>
    [HttpPost, RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Create([FromBody] PriceListRequest req, CancellationToken ct) => lists.CreateAsync(req, ct);

    /// <summary>Edita la lista (lo que no viene no cambia). 409 si, publicada, se cruza con otra; 422 si está desactivada o al cambiar «aplica a todos» con clientes.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Update(Guid publicId, [FromBody] PriceListRequest req, CancellationToken ct) => lists.UpdateAsync(publicId, req, ct);

    /// <summary>Publica la lista: se aplica en su vigencia. 422 sin renglones o sin clientes (si no es general); 409 si se cruza con otra publicada.</summary>
    [HttpPost("{publicId:guid}/publish"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Publish(Guid publicId, CancellationToken ct) => lists.PublishAsync(publicId, ct);

    /// <summary>La devuelve a borrador: deja de aplicarse a los pedidos nuevos.</summary>
    [HttpPost("{publicId:guid}/unpublish"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Unpublish(Guid publicId, CancellationToken ct) => lists.UnpublishAsync(publicId, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Deactivate(Guid publicId, CancellationToken ct) => lists.DeactivateAsync(publicId, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> Reactivate(Guid publicId, CancellationToken ct) => lists.ReactivateAsync(publicId, ct);

    /// <summary>Agrega renglones (producto o kit → precio) o cambia el precio de los que ya están. Solo productos propios y activos.</summary>
    [HttpPost("{publicId:guid}/items"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> UpsertItems(Guid publicId, [FromBody] PriceListItemsRequest req, CancellationToken ct) => lists.UpsertItemsAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/items/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> SetItemPrice(Guid publicId, int itemId, [FromBody] PriceListItemPriceRequest req, CancellationToken ct) => lists.SetItemPriceAsync(publicId, itemId, req, ct);

    /// <summary>Quita el renglón (en lógico).</summary>
    [HttpDelete("{publicId:guid}/items/{itemId:int}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> RemoveItem(Guid publicId, int itemId, CancellationToken ct) => lists.RemoveItemAsync(publicId, itemId, ct);

    /// <summary>Pone o cambia la foto del renglón (JPG, PNG, WEBP o GIF, hasta Files:MaxMegabytes). Se ve por GET /files/{imagePublicId}.</summary>
    [HttpPost("{publicId:guid}/items/{itemId:int}/image"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesPricelist)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<PriceListDto> SetItemImage(Guid publicId, int itemId, IFormFile? file, CancellationToken ct)
    {
        var bytes = await ReadAsync(file, files, ct);
        return await lists.SetItemImageAsync(publicId, itemId, file!.FileName, file.ContentType, bytes, ct);
    }

    [HttpDelete("{publicId:guid}/items/{itemId:int}/image"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> ClearItemImage(Guid publicId, int itemId, CancellationToken ct) => lists.ClearItemImageAsync(publicId, itemId, ct);

    /// <summary>Agrega clientes a una lista que no es general (precios negociados). 422 si la lista aplica a todos.</summary>
    [HttpPost("{publicId:guid}/clients"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> AddClients(Guid publicId, [FromBody] PriceListClientsRequest req, CancellationToken ct) => lists.AddClientsAsync(publicId, req, ct);

    /// <summary>Quita un cliente de la lista (en lógico). 422 si es el último de una lista publicada.</summary>
    [HttpDelete("{publicId:guid}/clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<PriceListDto> RemoveClient(Guid publicId, Guid clientPublicId, CancellationToken ct) => lists.RemoveClientAsync(publicId, clientPublicId, ct);

    /// <summary>Lee el archivo subido (vacío = 400; más grande que el tope = 400 con el mensaje del almacén).</summary>
    internal static async Task<byte[]> ReadAsync(IFormFile? file, StoredFileService files, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", Teikem.Domain.Sales.PriceListRules.ImageRequired);
        if (file.Length > files.MaxBytes) throw new Teikem.Infrastructure.Exceptions.ValidationException("file", StoredFileRules.TooLarge(files.MaxBytes / 1024 / 1024));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}

/// <summary>
/// Lote 221 — la oferta de un kit (módulo SALES_ORDERS): marcarlo como promoción, su vigencia y su foto. Ver con sales.view o inventory.view; cambiar con
/// sales.pricelist. Solo un kit puede ser oferta (422); fuera de vigencia no se vende, pero las órdenes ya hechas no cambian.
/// </summary>
[ApiController]
[Route("api/v1/products/{publicId:guid}/promo")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class ProductPromoController(SalesCatalogService catalog, StoredFileService files) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.SalesView + "|" + PermissionCatalog.InventoryView)]
    public Task<ProductPromoDto> Get(Guid publicId, CancellationToken ct) => catalog.GetPromoAsync(publicId, ct);

    /// <summary>Marca o desmarca el kit como oferta y fija su vigencia (promoFrom / promoTo opcionales; null = sin esa fecha). 400 si «hasta» va antes de «desde».</summary>
    [HttpPut, RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<ProductPromoDto> Set(Guid publicId, [FromBody] ProductPromoRequest req, CancellationToken ct) => catalog.SetPromoAsync(publicId, req, ct);

    /// <summary>La foto de la oferta (JPG, PNG, WEBP o GIF). 422 si el producto no es un kit marcado como oferta. Se ve por GET /files/{imagePublicId}.</summary>
    [HttpPost("image"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.SalesPricelist)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<ProductPromoDto> SetImage(Guid publicId, IFormFile? file, CancellationToken ct)
    {
        var bytes = await PriceListsController.ReadAsync(file, files, ct);
        return await catalog.SetPromoImageAsync(publicId, file!.FileName, file.ContentType, bytes, ct);
    }

    [HttpDelete("image"), RequirePermission(PermissionCatalog.SalesPricelist)]
    public Task<ProductPromoDto> ClearImage(Guid publicId, CancellationToken ct) => catalog.ClearPromoImageAsync(publicId, ct);
}

/// <summary>
/// Lote 221 — el catálogo del pedido guiado (módulo SALES_ORDERS, sales.view): las marcas (Product.Brand) con productos activos, sus ofertas vigentes con foto y
/// precio, y la lista completa de cada marca con el precio ya resuelto para el cliente.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders/catalog")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesCatalogController(SalesCatalogService catalog) : ControllerBase
{
    /// <summary>clientPublicId = el cliente que compra (sus listas ganan a la general); sin él, los precios de la lista general. 404 cliente; 422 cliente inactivo.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesCatalogDto> Get([FromQuery] Guid? clientPublicId, CancellationToken ct) => catalog.CatalogAsync(clientPublicId, ct);
}
