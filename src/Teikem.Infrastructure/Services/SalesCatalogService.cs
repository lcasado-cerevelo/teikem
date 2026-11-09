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
/// Lote 221 (P1 del pedido de vendedores) — las ofertas (kits en promoción con foto y vigencia) y el catálogo del pedido por marca. Lote 229: el catálogo lee las
/// ofertas nuevas (entidad Offer, ver OfferService); los métodos de «promo» de abajo son OBSOLETOS (siguen funcionando para el frontend viejo, ya no afectan ventas).
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
    /// El catálogo del pedido a la fecha de hoy, por marca (orden alfabético, sin distinguir mayúsculas; los productos sin marca al final). Lote 229:
    /// - Offers de cada marca = los renglones de las ofertas PUBLICADAS y vigentes que aplican al cliente (uno por producto: el de la oferta más barata), kits de
    ///   oferta incluidos, con el precio resuelto (puede ganar el negociado si es más bajo), las fechas de la oferta y sus avisos.
    /// - Products = la lista completa de la marca (un producto normal en oferta sale también aquí con el precio resuelto); los kits de oferta solo salen arriba, y solo
    ///   mientras su oferta esté publicada y vigente (si no, se tratan como inactivos para la venta).
    /// - Precio (OfferRules.Resolve): entre el negociado y la oferta gana el más bajo; si no, la general (o la oferta si es más baja); si no, el precio de venta; si no,
    ///   PriceSource NONE. Sin cliente: lista general y ofertas para todos.
    /// - Foto ya resuelta: la del renglón (oferta o lista) → la del producto → la de la primera pieza con foto si es kit → null.
    /// Ya no se leen IsPromotion/PromoFrom/PromoTo/PromoImageFileId (Lote 221, obsoletos).
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

        // los kits de oferta solo se venden mientras su oferta esté publicada y vigente
        var owners = await PriceListQueries.OwnerOffersAsync(db, products.Where(p => p.OfferId is not null).Select(p => p.OfferId!.Value), ct);
        products = products.Where(p => p.OfferId is not int oid || OfferRules.KitSellable(owners.GetValueOrDefault(oid), today)).ToList();

        var lists = await PriceListQueries.InEffectAsync(db, clientId, today, null, ct);
        var offers = await PriceListQueries.OffersInEffectAsync(db, clientId, today, null, ct);
        var listIds = lists.Select(x => x.PriceListId).ToList();
        var listInfo = listIds.Count == 0 ? new Dictionary<int, (Guid PublicId, string Name)>()
            : (await db.Set<PriceList>().AsNoTracking().Where(l => listIds.Contains(l.PriceListId)).Select(l => new { l.PriceListId, l.PublicId, l.Name }).ToListAsync(ct))
                .ToDictionary(l => l.PriceListId, l => (l.PublicId, l.Name));
        var offerIds = offers.Select(o => o.OfferId).ToList();
        var offerPublic = offerIds.Count == 0 ? new Dictionary<int, Guid>()
            : await db.Set<Offer>().AsNoTracking().Where(o => offerIds.Contains(o.OfferId)).ToDictionaryAsync(o => o.OfferId, o => o.PublicId, ct);
        var offerById = offers.ToDictionary(o => o.OfferId);
        var resolved = products.ToDictionary(p => p.ProductId, p => OfferRules.Resolve(p.ProductId, clientId, today, lists, offers, p.SalePrice));
        // la oferta que «presenta» al producto (la más barata que lo trae), aunque gane el negociado
        var offerOf = new Dictionary<int, (OfferView Offer, OfferItemView Item)>();
        foreach (var o in offers)
            foreach (var i in o.Items)
                if (!offerOf.TryGetValue(i.ProductId, out var cur) || i.Price < cur.Item.Price
                    || (i.Price == cur.Item.Price && (o.ValidFrom > cur.Offer.ValidFrom || (o.ValidFrom == cur.Offer.ValidFrom && o.OfferId > cur.Offer.OfferId))))
                    offerOf[i.ProductId] = (o, i);

        var productImages = await PriceListQueries.ProductImagesAsync(db, products, kitTypeId, ct);
        var images = await PriceListQueries.FilePublicIdsAsync(db, productImages.Values.Concat(resolved.Values.Select(r => r.ImageFileId)).Concat(offerOf.Values.Select(x => x.Item.ImageFileId)), ct);

        SalesCatalogItemDto Item(Product p, bool asOffer)
        {
            var r = resolved[p.ProductId];
            var isKit = kitTypeId is not null && p.ProductTypeLookupId == kitTypeId;
            (OfferView Offer, OfferItemView Item)? shown = offerOf.TryGetValue(p.ProductId, out var oo) ? oo : null;
            // la foto: la del renglón (de la oferta en la tarjeta; de la lista u oferta que dio el precio en la lista de la marca) → la del producto → la de sus piezas
            var rowImage = asOffer ? shown?.Item.ImageFileId ?? r.ImageFileId : r.ImageFileId;
            var fileId = ProductImageRules.Resolve(rowImage, productImages.GetValueOrDefault(p.ProductId), false, null);
            var info = r.PriceListId is int lid && listInfo.TryGetValue(lid, out var li) ? li : ((Guid PublicId, string Name)?)null;
            var notices = r.Notices?.Select(n => Notice(p, n, offerPublic)).ToList();
            return new SalesCatalogItemDto(p.PublicId, p.Sku, p.Name, p.Brand, isKit, isKit && p.KitAdjustable, shown is not null, shown?.Offer.ValidFrom, shown?.Offer.ValidTo,
                r.Price, r.Source, info?.PublicId, info?.Name, p.SalePrice, fileId is int f && images.TryGetValue(f, out var g) ? g : null,
                shown is { } s1 && offerPublic.TryGetValue(s1.Offer.OfferId, out var op) ? op : null, shown?.Offer.Name, shown?.Offer.ValidFrom, shown?.Offer.ValidTo,
                shown?.Item.Price, p.OfferId is not null, notices is { Count: > 0 } ? notices : null);
        }

        var brands = products
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Brand) ? null : p.Brand.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key is null ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var label = g.Key is null ? null : g.Select(p => p.Brand!.Trim()).GroupBy(b => b).OrderByDescending(b => b.Count()).ThenBy(b => b.Key, StringComparer.Ordinal).First().Key;
                var all = g.OrderBy(p => p.Sku, StringComparer.OrdinalIgnoreCase).ToList();
                var inOffer = all.Where(p => offerOf.ContainsKey(p.ProductId)).Select(p => Item(p, true)).ToList();
                var rest = all.Where(p => p.OfferId is null).Select(p => Item(p, false)).ToList();
                return new SalesCatalogBrandDto(label, inOffer, rest);
            })
            .Where(b => b.Offers.Count > 0 || b.Products.Count > 0)
            .ToList();
        return new SalesCatalogDto(clientPublicId, today, brands);
    }

    /// <summary>Un aviso de precio con su texto, la oferta que ganó y la otra (si hay).</summary>
    internal static SalesPriceNoticeDto Notice(Product p, PriceNotice n, IReadOnlyDictionary<int, Guid> offerPublic)
        => new(n.Code, OfferRules.NoticeMessage(p.Sku, n), p.PublicId, p.Sku, offerPublic.TryGetValue(n.OfferId, out var a) ? a : null, n.OfferName,
            n.OtherOfferId is int o && offerPublic.TryGetValue(o, out var b) ? b : null, n.OtherOfferName);

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
