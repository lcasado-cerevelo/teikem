using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 230 — sustitución de piezas de un kit en una orden de venta (docs/ofertas-kits-fotos-diseno.md §6, §8 y §9). La regla pura vive en
/// <see cref="KitSubstitutionRules"/>; aquí se resuelven los productos y se valida la línea:
/// - En las piezas de la línea (<c>components</c>), una fila con <c>substitutesProductPublicId</c> dice «esta pieza va EN LUGAR de esa pieza del kit». La pieza original
///   debe ser del catálogo del kit; el sustituto, de la misma categoría exacta y el mismo precio de venta, propio del dueño del kit, activo, no kit y que no sea ya
///   otra pieza del kit. La pieza marcada «no sustituible» no se cambia.
/// - Kit FIJO (no ajustable): las piezas que manda el vendedor, deshaciendo las sustituciones, deben ser exactamente las del catálogo (mismas cantidades); el sustituto
///   va en la MISMA cantidad que la pieza. Cualquier otro cambio sigue siendo «no se puede ajustar al pedir».
/// - Kit AJUSTABLE: las piezas siguen siendo libres como siempre (lote 69); una fila marcada como sustitución se valida igual (pieza del catálogo, categoría, precio,
///   no sustituible), pero su cantidad es la que el vendedor ajustó.
/// - La foto de la línea guarda la pieza REAL (ComponentProductId: la que se reserva, se despacha, se costea y se factura) y la original en SubstitutedProductId.
///   El precio de la línea no cambia.
/// </summary>
public sealed partial class SalesOrderService
{
    /// <summary>Las piezas del kit (del catálogo) y los productos válidos para sustituir cada una. Sirve antes de guardar la línea. 422 si el producto no es un kit.</summary>
    public async Task<KitSubstitutesDto> SubstitutesAsync(Guid kitPublicId, CancellationToken ct)
    {
        var kit = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == kitPublicId, ct) ?? throw WmsResolve.ProductNotFound();
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        if (kitTypeId is null || kit.ProductTypeLookupId != kitTypeId) throw new StatusRuleException(KitRules.NotAKit(kit.Sku));

        var pieces = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                            join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                            where c.KitProductId == kit.ProductId
                            orderby c.SortOrder, c.ProductKitComponentId
                            select new { Product = p, c.Quantity, c.NotSubstitutable }).ToListAsync(ct);
        var pieceIds = pieces.Select(x => x.Product.ProductId).ToList();
        var categoryIds = pieces.Where(x => !x.NotSubstitutable && x.Product.ProductCategoryId != null).Select(x => x.Product.ProductCategoryId!.Value).Distinct().ToList();
        var ownerId = kit.ClientId;
        var candidates = categoryIds.Count == 0 ? new List<Product>()
            : await db.Set<Product>().AsNoTracking()
                .Where(p => p.IsActive && p.ClientId == ownerId && p.ProductCategoryId != null && categoryIds.Contains(p.ProductCategoryId.Value) && p.SalePrice != null
                    && !pieceIds.Contains(p.ProductId) && (p.ProductTypeLookupId == null || p.ProductTypeLookupId != kitTypeId))
                .OrderBy(p => p.Sku).ToListAsync(ct);
        var tree = await db.Set<ProductCategory>().AsNoTracking().Select(c => new { c.ProductCategoryId, c.ParentId, c.Name }).ToListAsync(ct);
        var parents = tree.ToDictionary(c => c.ProductCategoryId, c => c.ParentId);
        var names = tree.ToDictionary(c => c.ProductCategoryId, c => c.Name);

        var components = pieces.Select(x =>
        {
            var piece = ToSubstitution(x.Product, kitTypeId);
            var subs = candidates.Where(c => KitSubstitutionRules.IsValidSubstitute(piece, x.NotSubstitutable, ToSubstitution(c, kitTypeId), ownerId))
                .Select(c => new KitSubstituteDto(c.PublicId, c.Sku, c.Name, c.Brand, c.Model, c.SalePrice)).ToList();
            return new KitComponentSubstitutesDto(x.Product.PublicId, x.Product.Sku, x.Product.Name, x.Quantity, x.NotSubstitutable, x.Product.ProductCategoryId,
                x.Product.ProductCategoryId is int cid ? ProductRules.CategoryPath(cid, parents, names) : null, x.Product.SalePrice, subs);
        }).ToList();
        return new KitSubstitutesDto(kit.PublicId, kit.Sku, kit.Name, kit.KitAdjustable, components);
    }

    private static SubstitutionProduct ToSubstitution(Product p, int? kitTypeId)
        => new(p.ProductId, p.Sku, p.ProductCategoryId, p.SalePrice, p.IsActive, kitTypeId is not null && p.ProductTypeLookupId == kitTypeId, p.ClientId);

    /// <summary>¿La línea trae alguna pieza marcada como sustitución (de otro producto que el suyo)?</summary>
    private static bool HasSubstitutions(IReadOnlyList<KitComponentRequest> custom)
        => custom.Any(c => c.SubstitutesProductPublicId is Guid s && s != c.ProductPublicId);

    /// <summary>
    /// Valida las sustituciones de las piezas de una línea de kit (ya validadas como piezas: existen, sin repetir, activas, no kits, del mismo dueño) y devuelve
    /// pieza real → pieza original. En un kit fijo, además, deshaciendo las sustituciones deben quedar exactamente las piezas del catálogo.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, int>> ValidateSubstitutionsAsync(Product kit, IReadOnlyList<KitComponentRequest> custom, IReadOnlyList<(int ProductId, decimal Quantity)> rows,
        int lineIndex, CancellationToken ct)
    {
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var catalog = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                             join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                             where c.KitProductId == kit.ProductId
                             select new { Product = p, c.Quantity, c.NotSubstitutable }).ToListAsync(ct);
        if (catalog.Count == 0) throw new ValidationException($"lines[{lineIndex}].productPublicId", SalesRules.KitWithoutComponents(kit.Sku));
        var byPublicId = catalog.ToDictionary(c => c.Product.PublicId);
        var catalogIds = catalog.Select(c => c.Product.ProductId).ToHashSet();
        var originalIds = custom.Where(c => c.SubstitutesProductPublicId is Guid s && s != c.ProductPublicId).Select(c => c.SubstitutesProductPublicId!.Value).Distinct().ToList();
        var originals = await db.Set<Product>().AsNoTracking().Where(p => originalIds.Contains(p.PublicId)).ToDictionaryAsync(p => p.PublicId, ct);
        var candidateIds = rows.Select(r => r.ProductId).ToList();
        var candidates = await db.Set<Product>().AsNoTracking().Where(p => candidateIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);

        var substituted = new Dictionary<int, int>();
        var seenOriginals = new HashSet<int>();
        var baseList = new Dictionary<int, decimal>();
        for (var j = 0; j < custom.Count; j++)
        {
            var field = $"lines[{lineIndex}].components[{j}]";
            var (productId, quantity) = rows[j];
            if (custom[j].SubstitutesProductPublicId is not Guid originalPublicId || originalPublicId == custom[j].ProductPublicId)
            {
                baseList[productId] = quantity;
                continue;
            }
            var candidate = candidates[productId];
            var original = originals.GetValueOrDefault(originalPublicId) ?? throw new NotFoundException(KitRules.ComponentNotFound);
            if (!byPublicId.TryGetValue(originalPublicId, out var piece)) throw new ValidationException(field, KitSubstitutionRules.NotAPiece(original.Sku, kit.Sku));
            if (!seenOriginals.Add(piece.Product.ProductId)) throw new ValidationException(field, KitSubstitutionRules.SubstitutedTwice(piece.Product.Sku));
            if (catalogIds.Contains(productId)) throw new ValidationException(field, KitSubstitutionRules.SubstituteIsAnotherPiece(candidate.Sku));
            var error = KitSubstitutionRules.Check(ToSubstitution(piece.Product, kitTypeId), piece.NotSubstitutable, ToSubstitution(candidate, kitTypeId), kit.ClientId);
            if (error is not null) throw new ValidationException(field, error);
            if (!kit.KitAdjustable && quantity != piece.Quantity) throw new ValidationException(field, KitSubstitutionRules.QuantityDiffers(piece.Product.Sku, piece.Quantity));
            substituted[productId] = piece.Product.ProductId;
            baseList[piece.Product.ProductId] = quantity;
        }

        // kit fijo: fuera de las sustituciones, las piezas son las del catálogo, en sus cantidades
        if (!kit.KitAdjustable && (baseList.Count != catalog.Count || catalog.Any(c => !baseList.TryGetValue(c.Product.ProductId, out var q) || q != c.Quantity)))
            throw new ValidationException($"lines[{lineIndex}].components", SalesRules.KitNotAdjustable(kit.Sku));
        return substituted;
    }
}
