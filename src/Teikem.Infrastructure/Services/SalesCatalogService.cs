using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 221 (P1 del pedido de vendedores) — las ofertas (kits en promoción con foto y vigencia) y el catálogo del pedido por marca.
/// - Oferta = un kit marcado «es promoción», con vigencia opcional (desde / hasta) y foto. Solo un kit puede serlo. Fuera de vigencia no se vende; las órdenes ya
///   hechas no cambian.
/// - Catálogo: los productos propios, activos y vendibles (sin serie) agrupados por Product.Brand (los sin marca al final), con sus ofertas vigentes arriba y la
///   lista completa de la marca debajo, cada uno con el precio ya resuelto para el cliente (lista del cliente &gt; lista general &gt; precio de venta del producto).
/// </summary>
public sealed class SalesCatalogService(TeikemDbContext db, ITenantClock clock, ILookupCache lookups, StoredFileService files)
{
    // ---------------------------------------------------------------- ofertas

    /// <summary>Los kits marcados como oferta (también los fuera de vigencia, con InEffect=false), por SKU. Con includeInactive, también los productos dados de baja.</summary>
    public async Task<IReadOnlyList<ProductPromoDto>> ListOffersAsync(bool includeInactive, CancellationToken ct)
    {
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        if (kitTypeId is null) return Array.Empty<ProductPromoDto>();
        var query = db.Set<Product>().AsNoTracking().Where(p => p.IsPromotion && p.ProductTypeLookupId == kitTypeId && p.ClientId == null);
        if (!includeInactive) query = query.Where(p => p.IsActive);
        var rows = await query.OrderBy(p => p.Sku).ToListAsync(ct);
        var images = await FilePublicIdsAsync(rows.Where(p => p.PromoImageFileId is not null).Select(p => p.PromoImageFileId!.Value), ct);
        return rows.Select(p => ToPromoDto(p, true, images)).ToList();
    }

    public async Task<ProductPromoDto> GetPromoAsync(Guid productPublicId, CancellationToken ct)
    {
        var p = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct) ?? throw WmsResolve.ProductNotFound();
        var isKit = await IsKitAsync(p, ct);
        var images = await FilePublicIdsAsync(p.PromoImageFileId is int f ? new[] { f } : Array.Empty<int>(), ct);
        return ToPromoDto(p, isKit, images);
    }

    /// <summary>Marca (o desmarca) el kit como oferta y fija su vigencia (null = sin esa fecha). Solo un kit puede ser oferta.</summary>
    public async Task<ProductPromoDto> SetPromoAsync(Guid productPublicId, ProductPromoRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var datesError = KitPromoRules.DatesError(req.PromoFrom, req.PromoTo);
        if (datesError is not null) throw new ValidationException("promoTo", datesError);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await db.Set<Product>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct2) ?? throw WmsResolve.ProductNotFound();
            var wantPromo = req.IsPromotion ?? p.IsPromotion;
            if (wantPromo && !await IsKitAsync(p, ct2)) throw new StatusRuleException(KitPromoRules.OnlyKits);
            p.IsPromotion = wantPromo;
            p.PromoFrom = req.PromoFrom;
            p.PromoTo = req.PromoTo;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        return await GetPromoAsync(productPublicId, ct);
    }

    /// <summary>La foto de la oferta: una imagen, guardada como archivo del producto (se ve con permiso de ver productos). Reemplaza la anterior.</summary>
    public async Task<ProductPromoDto> SetPromoImageAsync(Guid productPublicId, string? fileName, string? contentType, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        PriceListService.EnsureImage(content);
        var p = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct) ?? throw WmsResolve.ProductNotFound();
        if (!await IsKitAsync(p, ct)) throw new StatusRuleException(KitPromoRules.OnlyKits);
        if (!p.IsPromotion) throw new StatusRuleException(KitPromoRules.ImageNeedsPromotion);
        var file = await files.AttachAsync(EntityTypes.Product, p.ProductId, StoredFileCodes.KindDocument, fileName, contentType, content, ct, enforceOwnerWrite: false);
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == file.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        await SetImageIdAsync(productPublicId, fileId, ct);
        return await GetPromoAsync(productPublicId, ct);
    }

    public async Task<ProductPromoDto> ClearPromoImageAsync(Guid productPublicId, CancellationToken ct)
    {
        await SetImageIdAsync(productPublicId, null, ct);
        return await GetPromoAsync(productPublicId, ct);
    }

    private async Task SetImageIdAsync(Guid productPublicId, int? fileId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var p = await db.Set<Product>().AsTracking().FirstOrDefaultAsync(x => x.PublicId == productPublicId, ct2) ?? throw WmsResolve.ProductNotFound();
            p.PromoImageFileId = fileId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ---------------------------------------------------------------- catálogo del pedido

    /// <summary>
    /// El catálogo del pedido a la fecha de hoy: por marca (orden alfabético, sin distinguir mayúsculas; los productos sin marca al final), las ofertas vigentes de la
    /// marca y la lista completa de sus productos y kits que no son oferta, por SKU. Precio resuelto para el cliente; sin cliente, el de la lista general. Las ofertas
    /// fuera de vigencia no salen. Un producto sin precio sale con PriceSource NONE.
    /// </summary>
    public async Task<SalesCatalogDto> CatalogAsync(Guid? clientPublicId, CancellationToken ct)
    {
        int? clientId = null;
        if (clientPublicId is Guid cp)
        {
            var client = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => new { c.ClientId, c.IsActive }).FirstOrDefaultAsync(ct)
                         ?? throw new NotFoundException("Cliente");
            if (!client.IsActive) throw new StatusRuleException(SalesRules.ClientInactive);
            clientId = client.ClientId;
        }
        var today = clock.Today;
        var serialId = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.Serial, ct);
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        var products = await db.Set<Product>().AsNoTracking().Where(p => p.IsActive && p.ClientId == null && p.TrackingTypeLookupId != serialId).ToListAsync(ct);
        products = products.Where(p => KitPromoRules.IsSellable(p.IsPromotion, p.PromoFrom, p.PromoTo, today)).ToList();

        var lists = await PriceListQueries.InEffectAsync(db, clientId, today, null, ct);
        var listIds = lists.Select(x => x.PriceListId).ToList();
        var listInfo = listIds.Count == 0 ? new Dictionary<int, (Guid PublicId, string Name)>()
            : (await db.Set<PriceList>().AsNoTracking().Where(l => listIds.Contains(l.PriceListId)).Select(l => new { l.PriceListId, l.PublicId, l.Name }).ToListAsync(ct))
                .ToDictionary(l => l.PriceListId, l => (l.PublicId, l.Name));
        var resolved = products.ToDictionary(p => p.ProductId, p => PriceListRules.Resolve(p.ProductId, clientId, today, lists, p.SalePrice));
        var fileIds = products.Where(p => p.PromoImageFileId is not null).Select(p => p.PromoImageFileId!.Value)
            .Concat(resolved.Values.Where(r => r.ImageFileId is not null).Select(r => r.ImageFileId!.Value));
        var images = await FilePublicIdsAsync(fileIds, ct);

        SalesCatalogItemDto Item(Product p)
        {
            var r = resolved[p.ProductId];
            var isKit = kitTypeId is not null && p.ProductTypeLookupId == kitTypeId;
            var isOffer = isKit && p.IsPromotion;
            // la foto: la de la oferta y, si no tiene, la del renglón de la lista que dio el precio
            Guid? image = isOffer && p.PromoImageFileId is int pf && images.TryGetValue(pf, out var pg) ? pg
                : r.ImageFileId is int rf && images.TryGetValue(rf, out var rg) ? rg : null;
            var info = r.PriceListId is int lid && listInfo.TryGetValue(lid, out var li) ? li : ((Guid PublicId, string Name)?)null;
            return new SalesCatalogItemDto(p.PublicId, p.Sku, p.Name, p.Brand, isKit, isKit && p.KitAdjustable, isOffer, isOffer ? p.PromoFrom : null, isOffer ? p.PromoTo : null,
                r.Price, r.Source, info?.PublicId, info?.Name, p.SalePrice, image);
        }

        var brands = products
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Brand) ? null : p.Brand.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key is null ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var label = g.Key is null ? null : g.Select(p => p.Brand!.Trim()).GroupBy(b => b).OrderByDescending(b => b.Count()).ThenBy(b => b.Key, StringComparer.Ordinal).First().Key;
                var all = g.OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();
                var offers = all.Where(p => p.IsPromotion && kitTypeId is not null && p.ProductTypeLookupId == kitTypeId).Select(Item).ToList();
                var rest = all.Where(p => !(p.IsPromotion && kitTypeId is not null && p.ProductTypeLookupId == kitTypeId)).Select(Item).ToList();
                return new SalesCatalogBrandDto(label, offers, rest);
            }).ToList();
        return new SalesCatalogDto(clientPublicId, today, brands);
    }

    // ---------------------------------------------------------------- piezas

    private async Task<bool> IsKitAsync(Product p, CancellationToken ct)
    {
        var kitTypeId = await lookups.TryGetIdAsync(KitCodes.ProductTypeDomain, ProductTypes.Kit, ct);
        return kitTypeId is not null && p.ProductTypeLookupId == kitTypeId;
    }

    private ProductPromoDto ToPromoDto(Product p, bool isKit, IReadOnlyDictionary<int, Guid> images)
        => new(p.PublicId, p.Sku, p.Name, p.Brand, isKit, p.IsActive, p.IsPromotion, p.PromoFrom, p.PromoTo,
            p.IsPromotion && isKit && KitPromoRules.IsSellable(true, p.PromoFrom, p.PromoTo, clock.Today),
            p.PromoImageFileId is int f && images.TryGetValue(f, out var g) ? g : null, p.SalePrice);

    private async Task<Dictionary<int, Guid>> FilePublicIdsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, Guid>();
        return await db.Set<StoredFile>().AsNoTracking().Where(f => list.Contains(f.StoredFileId) && f.IsActive).ToDictionaryAsync(f => f.StoredFileId, f => f.PublicId, ct);
    }
}
