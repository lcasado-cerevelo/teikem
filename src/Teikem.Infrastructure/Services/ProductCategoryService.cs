using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 6 (P2) — categorías de producto jerárquicas (R23). ProductCategory lleva TenantId: todo se lee bajo el filtro global
/// de tenant, así que un padre de otro tenant simplemente no existe (404 'Categoría no encontrada.').
/// - Sin ciclos (400 CategoryCycle) y con a lo sumo ProductRules.MaxCategoryDepth niveles (400 CategoryDepth), contando el
///   subárbol que se mueve.
/// - Nombre único entre las activas del mismo nivel (409 CategoryNameTaken; última línea UX_ProductCategory_Name).
/// - Baja lógica (IsActive = 0), nunca DELETE: 409 si tiene productos activos o subcategorías activas. Reactivar exige el
///   padre activo y el nombre libre en su nivel.
/// </summary>
public sealed class ProductCategoryService(TeikemDbContext db, ITenantContext tenant)
{
    private sealed record Tree(Dictionary<int, int?> Parents, Dictionary<int, string> Names, Dictionary<int, bool> Active);

    // ---------------------------------------------------------------- lista

    /// <summary>Categorías del tenant con su ruta ('Raíz / Hija') y los productos activos que tienen directamente; orden por ruta.</summary>
    public async Task<IReadOnlyList<ProductCategoryDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var tree = await LoadTreeAsync(ct);
        var counts = await ProductCountsAsync(null, ct);
        return tree.Parents.Keys
            .Where(id => includeInactive || tree.Active[id])
            .Select(id => ToDto(id, tree, counts))
            .OrderBy(d => d.Path, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id)
            .ToList();
    }

    public async Task<ProductCategoryDto> GetAsync(int id, CancellationToken ct)
    {
        var tree = await LoadTreeAsync(ct);
        if (!tree.Parents.ContainsKey(id)) throw NotFound();
        return ToDto(id, tree, await ProductCountsAsync(id, ct));
    }

    // ---------------------------------------------------------------- alta

    public async Task<ProductCategoryDto> CreateAsync(ProductCategoryRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (name, nameError) = ProductRules.NormalizeCategoryName(req.Name);
        if (nameError is not null) throw new ValidationException("name", nameError);

        var tree = await LoadTreeAsync(ct);
        if (req.ParentId is int parentId)
        {
            if (!tree.Parents.ContainsKey(parentId)) throw NotFound();
            if (!tree.Active[parentId]) throw new ValidationException("parentId", ProductRules.CategoryParentInactive);
            if (ProductRules.ExceedsDepth(null, parentId, tree.Parents)) throw new ValidationException("parentId", ProductRules.CategoryDepth);
        }
        await EnsureNameFreeAsync(null, req.ParentId, name!, ct);

        var category = new ProductCategory { TenantId = tenantId, ParentId = req.ParentId, Name = name!, IsActive = true };
        db.Set<ProductCategory>().Add(category);
        await db.SaveGuardedAsync(ProductRules.CategoryNameTaken, ct);
        return await GetAsync(category.ProductCategoryId, ct);
    }

    // ---------------------------------------------------------------- edición

    /// <summary>PATCH: name y parentId (null = sin cambio); clearParent = la vuelve raíz.</summary>
    public async Task<ProductCategoryDto> UpdateAsync(int id, ProductCategoryPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var category = await db.Set<ProductCategory>().AsTracking().FirstOrDefaultAsync(c => c.ProductCategoryId == id, ct) ?? throw NotFound();

        string? name = null;
        if (req.Name is not null)
        {
            var (n, nameError) = ProductRules.NormalizeCategoryName(req.Name);
            if (nameError is not null) throw new ValidationException("name", nameError);
            name = n;
        }

        var newParent = req.ClearParent == true ? null : req.ParentId ?? category.ParentId;
        if (newParent != category.ParentId)
        {
            var tree = await LoadTreeAsync(ct);
            if (newParent is int parentId)
            {
                if (!tree.Parents.ContainsKey(parentId)) throw NotFound();
                if (ProductRules.CreatesCycle(id, parentId, tree.Parents)) throw new ValidationException("parentId", ProductRules.CategoryCycle);
                if (!tree.Active[parentId] && category.IsActive) throw new ValidationException("parentId", ProductRules.CategoryParentInactive);
            }
            if (ProductRules.ExceedsDepth(id, newParent, tree.Parents)) throw new ValidationException("parentId", ProductRules.CategoryDepth);
        }

        var finalName = name ?? category.Name;
        if (category.IsActive && (newParent != category.ParentId || !string.Equals(finalName, category.Name, StringComparison.Ordinal)))
            await EnsureNameFreeAsync(id, newParent, finalName, ct);

        category.Name = finalName;
        category.ParentId = newParent;
        await db.SaveGuardedAsync(ProductRules.CategoryNameTaken, ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- baja y reactivación

    /// <summary>Baja lógica: 409 si tiene productos activos (CategoryHasProducts) o subcategorías activas (CategoryHasChildren).</summary>
    public async Task<ProductCategoryDto> DeactivateAsync(int id, CancellationToken ct)
    {
        var category = await db.Set<ProductCategory>().AsTracking().FirstOrDefaultAsync(c => c.ProductCategoryId == id, ct) ?? throw NotFound();
        if (!category.IsActive) return await GetAsync(id, ct);
        if (await db.Set<Product>().AnyAsync(p => p.IsActive && p.ProductCategoryId == id, ct))
            throw new ConflictException(ProductRules.CategoryHasProducts);
        if (await db.Set<ProductCategory>().AnyAsync(c => c.IsActive && c.ParentId == id, ct))
            throw new ConflictException(ProductRules.CategoryHasChildren);
        category.IsActive = false;
        await db.SaveGuardedAsync(ProductRules.CategoryNameTaken, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Reactivar: el padre debe estar activo (400) y el nombre libre en su nivel (409).</summary>
    public async Task<ProductCategoryDto> ReactivateAsync(int id, CancellationToken ct)
    {
        var category = await db.Set<ProductCategory>().AsTracking().FirstOrDefaultAsync(c => c.ProductCategoryId == id, ct) ?? throw NotFound();
        if (category.IsActive) return await GetAsync(id, ct);
        if (category.ParentId is int parentId
            && !await db.Set<ProductCategory>().AnyAsync(c => c.ProductCategoryId == parentId && c.IsActive, ct))
            throw new ValidationException("parentId", ProductRules.CategoryParentInactive);
        await EnsureNameFreeAsync(id, category.ParentId, category.Name, ct);
        category.IsActive = true;
        await db.SaveGuardedAsync(ProductRules.CategoryNameTaken, ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- helpers

    private static NotFoundException NotFound() => new("Categoría", feminine: true);

    /// <summary>409 CategoryNameTaken si otra categoría ACTIVA del mismo nivel (mismo padre) ya usa el nombre.</summary>
    private async Task EnsureNameFreeAsync(int? selfId, int? parentId, string name, CancellationToken ct)
    {
        if (await db.Set<ProductCategory>().AnyAsync(c => c.IsActive && c.ParentId == parentId && c.Name == name
                                                          && (selfId == null || c.ProductCategoryId != selfId), ct))
            throw new ConflictException(ProductRules.CategoryNameTaken);
    }

    private async Task<Tree> LoadTreeAsync(CancellationToken ct)
    {
        var rows = await db.Set<ProductCategory>().AsNoTracking()
            .Select(c => new { c.ProductCategoryId, c.ParentId, c.Name, c.IsActive }).ToListAsync(ct);
        return new Tree(rows.ToDictionary(r => r.ProductCategoryId, r => r.ParentId),
            rows.ToDictionary(r => r.ProductCategoryId, r => r.Name),
            rows.ToDictionary(r => r.ProductCategoryId, r => r.IsActive));
    }

    /// <summary>Productos activos por categoría (directos). Con onlyId, solo esa categoría.</summary>
    private async Task<Dictionary<int, int>> ProductCountsAsync(int? onlyId, CancellationToken ct)
        => await db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive && p.ProductCategoryId != null && (onlyId == null || p.ProductCategoryId == onlyId))
            .GroupBy(p => p.ProductCategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

    private static ProductCategoryDto ToDto(int id, Tree tree, IReadOnlyDictionary<int, int> counts)
        => new(id, tree.Names[id], tree.Parents[id], ProductRules.CategoryPath(id, tree.Parents, tree.Names), tree.Active[id],
            counts.GetValueOrDefault(id));
}
