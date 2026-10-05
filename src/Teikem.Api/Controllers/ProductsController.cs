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
    /// onlyAvailable (selector de recolección: disponible recolectable &gt; 0). selectorOrder=true = orden de los selectores de
    /// producto (maestro L1207): mercancía de clientes primero (el de más inventario en mano antes) y suministros propios al
    /// final; sin él, por SKU (pantalla Productos e inventario). belowMin=true (Lote 7A) = solo productos bajo mínimo (mismo
    /// cálculo que isBelowMin; con warehousePublicId, el disponible de ese almacén): el Pulso los cuenta con take=1 y total.
    /// Lote 12: warehousePublicIds (varios, combinados con warehousePublicId; acotan totales y cálculos, no la lista),
    /// productPublicIds (selección de SKU), name (contiene, sin distinguir mayúsculas), brands (marca igual, sin distinguir
    /// mayúsculas), serialOnly (rastreo SERIAL o con series registradas) y serialMissing (activos SERIAL con existencia mayor
    /// que sus series en stock: el KPI 'series por capturar' se lee con take=1 y total). onlyOnHand (ajuste del 2026-09-30,
    /// KPI 'Unidades totales') = solo productos con existencia en mano &gt; 0 en todas sus posiciones (o en los almacenes
    /// indicados); se combina con activeOnly para 'activos con existencia'. unavailable (2026-10-05, tableta 'No disponibles') =
    /// activos con disponible = 0 (sin existencia o con todo reservado; se lee con take=1 y total).
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ProductPageDto> List([FromQuery] string? search, [FromQuery] int[]? categoryIds, [FromQuery] Guid? ownerClientPublicId,
        [FromQuery] bool? ownOnly, [FromQuery] bool activeOnly, [FromQuery] Guid? warehousePublicId, [FromQuery] bool onlyAvailable,
        [FromQuery] Guid[]? warehousePublicIds, [FromQuery] Guid[]? productPublicIds, [FromQuery] string? name, [FromQuery] string[]? brands,
        [FromQuery] int skip = 0, [FromQuery] int take = 100, [FromQuery] bool selectorOrder = false, [FromQuery] bool belowMin = false,
        [FromQuery] bool serialOnly = false, [FromQuery] bool serialMissing = false, [FromQuery] bool onlyOnHand = false,
        [FromQuery] bool unavailable = false, CancellationToken ct = default)
        => products.ListAsync(new ProductListQuery(search, NullIfEmpty(categoryIds), ownerClientPublicId, ownOnly,
            activeOnly, warehousePublicId, onlyAvailable, skip, take, selectorOrder, belowMin, NullIfEmpty(warehousePublicIds),
            NullIfEmpty(productPublicIds), name, NullIfEmpty(brands), serialOnly, serialMissing, onlyOnHand, unavailable), InventoryScope.Any, ct);

    /// <summary>
    /// Lote 12 — marcas distintas de los productos del tenant (para el filtro Marca), ordenadas y sin repetir sin distinguir
    /// mayúsculas; search = la marca contiene el texto. Mismo permiso que la lista de productos.
    /// </summary>
    [HttpGet("brands"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<string>> Brands([FromQuery] string? search, CancellationToken ct)
        => products.ListBrandsAsync(search, InventoryScope.Any, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ProductDetailDto> Get(Guid publicId, CancellationToken ct) => products.GetAsync(publicId, InventoryScope.Any, ct);

    /// <summary>
    /// Lote 8A — producto por código escaneado: código de barras exacto y, si no hay, SKU exacto; solo activos (con el mismo
    /// SKU en varios dueños gana el propio). Sin coincidencia → 404 'No hay un producto con ese código.'.
    /// </summary>
    [HttpGet("by-barcode/{code}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<ProductDetailDto> ByBarcode(string code, CancellationToken ct) => products.GetByBarcodeAsync(code, InventoryScope.Any, ct);

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

    /// <summary>Baja lógica: 409 con inventario en mano, recibos abiertos, tareas pendientes, recolecciones eliminables o conteos abiertos.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Deactivate(Guid publicId, CancellationToken ct) => products.DeactivateAsync(publicId, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductDetailDto> Reactivate(Guid publicId, CancellationToken ct) => products.ReactivateAsync(publicId, ct);

    private static T[]? NullIfEmpty<T>(T[]? values) => values is { Length: > 0 } ? values : null;
}
