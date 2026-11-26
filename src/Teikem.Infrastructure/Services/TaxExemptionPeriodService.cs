using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Tax;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 248 — exención de producto por temporada (tax.manage + módulo TAX, lo exige el controlador). Mientras rige, las líneas de producto gravables o de tasa cero
/// que cubre salen sin impuesto con el motivo EXEMPT_PRODUCT_PERIOD (<see cref="TaxService.CalculateAsync"/>).
/// - Programado (empieza después de hoy): se edita todo; la selección se REEMPLAZA (lo quitado queda con IsActive = 0, lo que vuelve se reactiva; nunca DELETE).
/// - Ya empezado: solo se mueve la fecha final (de hoy en adelante) o se da de baja; cualquier otro cambio da 409.
/// - Un producto o una categoría de otra compañía no existe (404): el DbSet está filtrado por compañía.
/// El estado (programado, vigente, terminado, inactivo) se deriva de la fecha; no hay StatusCode.
/// </summary>
public sealed class TaxExemptionPeriodService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock)
{
    private TenantContext T => (TenantContext)tenant;
    private DateOnly Today => clock.Today;

    public async Task<IReadOnlyList<TaxExemptionPeriodDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Set<TaxExemptionPeriod>().AsNoTracking().Include(p => p.Products).Include(p => p.Categories).AsQueryable();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        var rows = await q.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.TaxExemptionPeriodId).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<TaxExemptionPeriodDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var p = await db.Set<TaxExemptionPeriod>().AsNoTracking().Include(x => x.Products).Include(x => x.Categories).FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                ?? throw new NotFoundException("Período de exención");
        return (await ToDtosAsync(new[] { p }, ct))[0];
    }

    public async Task<TaxExemptionPeriodDto> CreateAsync(TaxExemptionPeriodRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var name = NormalizeName(req.Name);
        var reason = NormalizeReason(req.Reason);
        if (req.ValidFrom is not DateOnly from) throw new ValidationException("validFrom", TaxExemptionPeriodRules.ValidFromRequired);
        if (req.ValidTo is not DateOnly to) throw new ValidationException("validTo", TaxExemptionPeriodRules.ValidToRequired);
        ValidateDates(from, to);
        var scope = TaxExemptionPeriodRules.NormalizeScope(req.Scope) ?? throw new ValidationException("scope", TaxExemptionPeriodRules.ScopeInvalid);
        var (productIds, categoryIds) = await ResolveSelectionAsync(scope, req.ProductPublicIds, req.CategoryIds, ct);

        var p = new TaxExemptionPeriod
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, Reason = reason, ValidFrom = from, ValidTo = to, Scope = scope, IsActive = true,
            CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId,
        };
        foreach (var id in productIds)
            p.Products.Add(new TaxExemptionPeriodProduct { TenantId = tenantId, ProductId = id, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId });
        foreach (var id in categoryIds)
            p.Categories.Add(new TaxExemptionPeriodCategory { TenantId = tenantId, ProductCategoryId = id, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId });
        db.Set<TaxExemptionPeriod>().Add(p);
        await db.SaveGuardedAsync(TaxExemptionPeriodRules.Concurrent, ct);
        return await GetAsync(p.PublicId, ct);
    }

    public async Task<TaxExemptionPeriodDto> UpdateAsync(Guid publicId, TaxExemptionPeriodPatchRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var tenantId = T.RequireTenantId();
        var p = await db.Set<TaxExemptionPeriod>().AsTracking().Include(x => x.Products).Include(x => x.Categories).FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                ?? throw new NotFoundException("Período de exención");
        db.ApplyRowVersion(p, req.RowVersion);

        var name = req.Name is null ? p.Name : NormalizeName(req.Name);
        var reason = req.Reason is null ? p.Reason : NormalizeReason(req.Reason);
        var from = req.ValidFrom ?? p.ValidFrom;
        var to = req.ValidTo ?? p.ValidTo;
        var scope = req.Scope is null ? p.Scope : TaxExemptionPeriodRules.NormalizeScope(req.Scope) ?? throw new ValidationException("scope", TaxExemptionPeriodRules.ScopeInvalid);
        var selectionSent = req.ProductPublicIds is not null || req.CategoryIds is not null;
        var currentProducts = p.Products.Where(x => x.IsActive).Select(x => x.ProductId).ToHashSet();
        var currentCategories = p.Categories.Where(x => x.IsActive).Select(x => x.ProductCategoryId).ToHashSet();
        IReadOnlyCollection<int> productIds = currentProducts;
        IReadOnlyCollection<int> categoryIds = currentCategories;
        if (selectionSent || scope != p.Scope)
            (productIds, categoryIds) = await ResolveSelectionAsync(scope,
                req.ProductPublicIds ?? await ProductPublicIdsAsync(currentProducts, ct), req.CategoryIds ?? currentCategories.ToList(), ct);

        var started = p.ValidFrom <= Today;
        if (started)
        {
            // ya empezó: lo único que se mueve es la fecha final (lo que viene igual a lo que hay no cuenta como cambio)
            var changed = name != p.Name || reason != p.Reason || from != p.ValidFrom || scope != p.Scope
                          || !currentProducts.SetEquals(productIds) || !currentCategories.SetEquals(categoryIds);
            if (changed) throw new ConflictException(TaxExemptionPeriodRules.StartedOnlyEnd);
            if (to != p.ValidTo)
            {
                if (to < Today) throw new ValidationException("validTo", TaxExemptionPeriodRules.EndBeforeToday);
                if (to < p.ValidFrom) throw new ValidationException("validTo", TaxRules.DatesOrder);
            }
        }
        else
        {
            ValidateDates(from, to);
            ReplaceSelection(p, tenantId, productIds, categoryIds);
        }
        (p.Name, p.Reason, p.ValidFrom, p.ValidTo, p.Scope) = (name, reason, from, to, scope);
        p.UpdatedAtUtc = DateTime.UtcNow;
        p.UpdatedBy = tenant.UserId;
        await db.SaveGuardedAsync(TaxExemptionPeriodRules.Concurrent, ct);
        return await GetAsync(p.PublicId, ct);
    }

    /// <summary>Dar de baja o reactivar (siempre se puede; el estado del período sigue saliendo de sus fechas).</summary>
    public async Task<TaxExemptionPeriodDto> SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var p = await db.Set<TaxExemptionPeriod>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct)
                ?? throw new NotFoundException("Período de exención");
        if (p.IsActive != active)
        {
            p.IsActive = active;
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = tenant.UserId;
            await db.SaveGuardedAsync(TaxExemptionPeriodRules.Concurrent, ct);
        }
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- ayudantes

    private static string NormalizeName(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) throw new ValidationException("name", TaxExemptionPeriodRules.NameRequired);
        if (v.Length > TaxExemptionPeriodRules.MaxNameLength) throw new ValidationException("name", TaxExemptionPeriodRules.NameTooLong);
        return v;
    }

    private static string NormalizeReason(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length == 0) throw new ValidationException("reason", TaxExemptionPeriodRules.ReasonRequired);
        if (v.Length > TaxExemptionPeriodRules.MaxReasonLength) throw new ValidationException("reason", TaxExemptionPeriodRules.ReasonTooLong);
        return v;
    }

    /// <summary>Hasta ≥ desde y hasta ≥ hoy (un período que ya terminó no se crea ni se programa).</summary>
    private void ValidateDates(DateOnly from, DateOnly to)
    {
        if (to < from) throw new ValidationException("validTo", TaxRules.DatesOrder);
        if (to < Today) throw new ValidationException("validTo", TaxExemptionPeriodRules.EndBeforeToday);
    }

    /// <summary>
    /// La selección validada: con ALL va vacía (lo escogido antes se da de baja); con SELECTED al menos un producto o una categoría, con los topes. Un producto o
    /// una categoría que la compañía no tiene da 404.
    /// </summary>
    private async Task<(IReadOnlyCollection<int> Products, IReadOnlyCollection<int> Categories)> ResolveSelectionAsync(string scope, IReadOnlyList<Guid>? productPublicIds,
        IReadOnlyList<int>? categoryIds, CancellationToken ct)
    {
        if (scope == TaxExemptionPeriodScopes.All) return (Array.Empty<int>(), Array.Empty<int>());
        var wantedProducts = (productPublicIds ?? Array.Empty<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        var wantedCategories = (categoryIds ?? Array.Empty<int>()).Distinct().ToList();
        if (wantedProducts.Count == 0 && wantedCategories.Count == 0) throw new ValidationException("productPublicIds", TaxExemptionPeriodRules.SelectionRequired);
        if (wantedProducts.Count > TaxExemptionPeriodRules.MaxProducts || wantedCategories.Count > TaxExemptionPeriodRules.MaxCategories)
            throw new ValidationException("productPublicIds", TaxExemptionPeriodRules.TooManyTargets);

        var products = wantedProducts.Count == 0 ? new Dictionary<Guid, int>()
            : await db.Set<Product>().AsNoTracking().Where(x => wantedProducts.Contains(x.PublicId)).ToDictionaryAsync(x => x.PublicId, x => x.ProductId, ct);
        if (wantedProducts.Any(g => !products.ContainsKey(g))) throw new NotFoundException("Producto");
        var categories = wantedCategories.Count == 0 ? new List<int>()
            : await db.Set<ProductCategory>().AsNoTracking().Where(x => wantedCategories.Contains(x.ProductCategoryId)).Select(x => x.ProductCategoryId).ToListAsync(ct);
        if (wantedCategories.Any(c => !categories.Contains(c))) throw new NotFoundException("Categoría", feminine: true);
        return (products.Values.ToHashSet(), categories.ToHashSet());
    }

    private async Task<IReadOnlyList<Guid>> ProductPublicIdsAsync(IReadOnlyCollection<int> productIds, CancellationToken ct)
        => productIds.Count == 0 ? Array.Empty<Guid>()
            : await db.Set<Product>().AsNoTracking().Where(x => productIds.Contains(x.ProductId)).Select(x => x.PublicId).ToListAsync(ct);

    /// <summary>Reemplaza la selección: lo que ya no está queda con IsActive = 0; lo que vuelve reactiva su fila (la más reciente); lo nuevo se inserta.</summary>
    private void ReplaceSelection(TaxExemptionPeriod p, int tenantId, IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> categoryIds)
    {
        foreach (var row in p.Products.Where(x => x.IsActive && !productIds.Contains(x.ProductId))) row.IsActive = false;
        foreach (var id in productIds)
        {
            if (p.Products.Any(x => x.IsActive && x.ProductId == id)) continue;
            var old = p.Products.Where(x => x.ProductId == id).OrderByDescending(x => x.TaxExemptionPeriodProductId).FirstOrDefault();
            if (old is not null) old.IsActive = true;
            else p.Products.Add(new TaxExemptionPeriodProduct { TenantId = tenantId, ProductId = id, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId });
        }
        foreach (var row in p.Categories.Where(x => x.IsActive && !categoryIds.Contains(x.ProductCategoryId))) row.IsActive = false;
        foreach (var id in categoryIds)
        {
            if (p.Categories.Any(x => x.IsActive && x.ProductCategoryId == id)) continue;
            var old = p.Categories.Where(x => x.ProductCategoryId == id).OrderByDescending(x => x.TaxExemptionPeriodCategoryId).FirstOrDefault();
            if (old is not null) old.IsActive = true;
            else p.Categories.Add(new TaxExemptionPeriodCategory { TenantId = tenantId, ProductCategoryId = id, IsActive = true, CreatedAtUtc = DateTime.UtcNow, CreatedBy = tenant.UserId });
        }
    }

    private async Task<IReadOnlyList<TaxExemptionPeriodDto>> ToDtosAsync(IReadOnlyList<TaxExemptionPeriod> rows, CancellationToken ct)
    {
        var productIds = rows.SelectMany(r => r.Products).Where(x => x.IsActive).Select(x => x.ProductId).Distinct().ToList();
        var categoryIds = rows.SelectMany(r => r.Categories).Where(x => x.IsActive).Select(x => x.ProductCategoryId).Distinct().ToList();
        var products = productIds.Count == 0 ? new Dictionary<int, TaxExemptionPeriodProductDto>()
            : (await db.Set<Product>().AsNoTracking().Where(x => productIds.Contains(x.ProductId)).Select(x => new { x.ProductId, x.PublicId, x.Sku, x.Name }).ToListAsync(ct))
                .ToDictionary(x => x.ProductId, x => new TaxExemptionPeriodProductDto(x.PublicId, x.ProductId, x.Sku, x.Name));
        var categories = categoryIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<ProductCategory>().AsNoTracking().Where(x => categoryIds.Contains(x.ProductCategoryId)).ToDictionaryAsync(x => x.ProductCategoryId, x => x.Name, ct);
        return rows.Select(p => new TaxExemptionPeriodDto(p.PublicId, p.Name, p.Reason, p.ValidFrom, p.ValidTo, p.Scope, p.IsActive,
                TaxExemptionPeriodRules.State(p.IsActive, p.ValidFrom, p.ValidTo, Today),
                p.Products.Where(x => x.IsActive && products.ContainsKey(x.ProductId)).Select(x => products[x.ProductId]).OrderBy(x => x.Sku, StringComparer.OrdinalIgnoreCase).ToList(),
                p.Categories.Where(x => x.IsActive).Select(x => new TaxExemptionPeriodCategoryDto(x.ProductCategoryId, categories.GetValueOrDefault(x.ProductCategoryId, string.Empty)))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                p.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null))
            .ToList();
    }
}
