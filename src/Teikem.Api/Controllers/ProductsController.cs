using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P2) — maestro de productos (módulo WMS_LOTSERIAL). Lectura con inventory.view y escritura con inventory.manage.
/// Las rutas usan el PublicId del producto; lotes y series se leen SOLO bajo su producto. Todas las lecturas pasan
/// InventoryScope.Any (usuarios internos del tenant); el Portal (Lote 8) pasará el cliente dueño (D44).
/// Baja lógica (deactivate/reactivate), nunca DELETE. Contactos y campos personalizados: PRODUCT (Lote 1).
/// </summary>
[ApiController]
[Route("api/v1/products")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200) con buscador (SKU, nombre, código de barras, dueño) y filtros: categoryIds (con
    /// subcategorías), ownerClientPublicId, ownOnly, activeOnly (selectores), warehousePublicId (totales de ese almacén) y
    /// onlyAvailable (selector de recolección: disponible recolectable &gt; 0).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ProductPageDto> List([FromQuery] string? search, [FromQuery] int[]? categoryIds, [FromQuery] Guid? ownerClientPublicId,
        [FromQuery] bool? ownOnly, [FromQuery] bool activeOnly, [FromQuery] Guid? warehousePublicId, [FromQuery] bool onlyAvailable,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken ct = default)
        => products.ListAsync(new ProductListQuery(search, categoryIds is { Length: > 0 } ? categoryIds : null, ownerClientPublicId, ownOnly,
            activeOnly, warehousePublicId, onlyAvailable, skip, take), InventoryScope.Any, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ProductDetailDto> Get(Guid publicId, CancellationToken ct) => products.GetAsync(publicId, InventoryScope.Any, ct);

    /// <summary>Lotes del producto con existencia en mano y días al vencimiento (orden FEFO).</summary>
    [HttpGet("{publicId:guid}/lots"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<LotDto>> Lots(Guid publicId, CancellationToken ct) => products.ListLotsAsync(publicId, InventoryScope.Any, ct);

    /// <summary>Series del producto con estatus y ubicación actual; status=AVAILABLE,SHIPPED y search dentro del número.</summary>
    [HttpGet("{publicId:guid}/serials"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<SerialDto>> Serials(Guid publicId, [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
        => products.ListSerialsAsync(publicId, status, search, InventoryScope.Any, ct);

    /// <summary>Alta: SKU único por dueño e inmutable; unidad base (default UN) y seguimiento (default NONE) por catálogo.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Create([FromBody] ProductCreateRequest req, CancellationToken ct) => products.CreateAsync(req, ct);

    /// <summary>Edición en línea: null = sin cambio; sku no se edita; seguimiento, unidad y dueño se fijan con el primer movimiento.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Update(Guid publicId, [FromBody] ProductPatchRequest req, CancellationToken ct)
        => products.UpdateAsync(publicId, req, ct);

    /// <summary>Baja lógica: 409 con inventario en mano, recibos abiertos o tareas pendientes.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Deactivate(Guid publicId, CancellationToken ct) => products.DeactivateAsync(publicId, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Reactivate(Guid publicId, CancellationToken ct) => products.ReactivateAsync(publicId, ct);
}
