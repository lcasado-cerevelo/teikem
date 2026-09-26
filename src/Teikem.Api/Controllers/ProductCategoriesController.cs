using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P2) — categorías de producto jerárquicas (módulo WMS_LOTSERIAL). Lectura con inventory.view y escritura con
/// inventory.manage. Las categorías se exponen por id entero bajo el filtro de tenant (una de otro tenant da 404).
/// Baja lógica (deactivate/reactivate), nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/product-categories")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class ProductCategoriesController(ProductCategoryService categories) : ControllerBase
{
    /// <summary>Árbol aplanado con ruta ('Raíz / Hija') y productos activos por categoría; includeInactive muestra las dadas de baja.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<ProductCategoryDto>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => categories.ListAsync(includeInactive, ct);

    [HttpPost, RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductCategoryDto> Create([FromBody] ProductCategoryRequest req, CancellationToken ct) => categories.CreateAsync(req, ct);

    /// <summary>Renombrar o mover (parentId / clearParent): sin ciclos y con a lo sumo 5 niveles.</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductCategoryDto> Update(int id, [FromBody] ProductCategoryPatchRequest req, CancellationToken ct)
        => categories.UpdateAsync(id, req, ct);

    /// <summary>Baja lógica: 409 si tiene productos activos o subcategorías activas.</summary>
    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductCategoryDto> Deactivate(int id, CancellationToken ct) => categories.DeactivateAsync(id, ct);

    [HttpPost("{id:int}/reactivate"), RequirePermission(PermissionCatalog.InventoryManage)]
    public Task<ProductCategoryDto> Reactivate(int id, CancellationToken ct) => categories.ReactivateAsync(id, ct);
}
