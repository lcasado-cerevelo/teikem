using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 221 (y 229: ofertas y fotos) — lecturas de las listas de precios que comparten la orden de venta (precio de la línea) y el catálogo del pedido. La compañía la pone el filtro global;
/// las reglas (quién gana) son puras, en <see cref="PriceListRules"/>.
/// </summary>
internal static class PriceListQueries
{
    /// <summary>
    /// Las listas PUBLICADAS, activas y vigentes en la fecha que alcanzan al cliente (las generales siempre; las de clientes, solo si lo incluyen), con sus renglones
    /// activos (solo los de esos productos si se indican). Sin cliente, solo las generales.
    /// </summary>
    public static async Task<IReadOnlyList<PriceListView>> InEffectAsync(TeikemDbContext db, int? clientId, DateOnly date, IReadOnlyCollection<int>? productIds, CancellationToken ct)
    {
        var query = db.Set<PriceList>().AsNoTracking().Where(l => l.IsActive && l.IsPublished && l.ValidFrom <= date && (l.ValidTo == null || l.ValidTo >= date));
        if (clientId is int c)
            query = query.Where(l => l.AppliesToAll || db.Set<PriceListClient>().Any(pc => pc.PriceListId == l.PriceListId && pc.IsActive && pc.ClientId == c));
        else
            query = query.Where(l => l.AppliesToAll);
        var lists = await query.Select(l => new { l.PriceListId, l.Name, l.AppliesToAll, l.ValidFrom, l.ValidTo }).ToListAsync(ct);
        if (lists.Count == 0) return Array.Empty<PriceListView>();

        var ids = lists.Select(l => l.PriceListId).ToList();
        var itemQuery = db.Set<PriceListItem>().AsNoTracking().Where(i => ids.Contains(i.PriceListId) && i.IsActive);
        if (productIds is not null)
        {
            var pids = productIds.Distinct().ToList();
            itemQuery = itemQuery.Where(i => pids.Contains(i.ProductId));
        }
        var items = (await itemQuery.Select(i => new { i.PriceListId, i.ProductId, i.Price, i.ImageFileId }).ToListAsync(ct))
            .GroupBy(i => i.PriceListId).ToDictionary(g => g.Key, g => (IReadOnlyList<PriceListItemView>)g.Select(i => new PriceListItemView(i.ProductId, i.Price, i.ImageFileId)).ToList());
        IReadOnlyCollection<int> mine = clientId is int cid ? new[] { cid } : Array.Empty<int>();
        return lists.Select(l => new PriceListView(l.PriceListId, l.Name, true, l.AppliesToAll, l.ValidFrom, l.ValidTo, l.AppliesToAll ? Array.Empty<int>() : mine,
            items.GetValueOrDefault(l.PriceListId) ?? Array.Empty<PriceListItemView>())).ToList();
    }

    /// <summary>
    /// El precio resuelto de cada producto para el cliente en la fecha. Lote 229: entre el negociado (lista del cliente) y la oferta vigente gana el más bajo; sin
    /// negociado, entre la oferta y la lista general el más bajo; si no, la lista general y si no el precio de venta del producto (<see cref="OfferRules.Resolve"/>).
    /// </summary>
    public static async Task<Dictionary<int, ResolvedPrice>> ResolveAsync(TeikemDbContext db, IEnumerable<Product> products, int? clientId, DateOnly date, CancellationToken ct)
    {
        var list = products.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();
        if (list.Count == 0) return new Dictionary<int, ResolvedPrice>();
        var ids = list.Select(p => p.ProductId).ToList();
        var lists = await InEffectAsync(db, clientId, date, ids, ct);
        var offers = await OffersInEffectAsync(db, clientId, date, ids, ct);
        return list.ToDictionary(p => p.ProductId, p => OfferRules.Resolve(p.ProductId, clientId, date, lists, offers, p.SalePrice));
    }

    /// <summary>
    /// Lote 229 — las ofertas PUBLICADAS, activas y vigentes en la fecha que alcanzan al cliente (las de todos siempre; las de clientes escogidos, solo si lo
    /// incluyen), con sus renglones activos (solo los de esos productos si se indican). Sin cliente, solo las de todos.
    /// </summary>
    public static async Task<IReadOnlyList<OfferView>> OffersInEffectAsync(TeikemDbContext db, int? clientId, DateOnly date, IReadOnlyCollection<int>? productIds, CancellationToken ct)
    {
        var query = db.Set<Offer>().AsNoTracking().Where(o => o.IsActive && o.IsPublished && o.ValidFrom <= date && (o.ValidTo == null || o.ValidTo >= date));
        if (clientId is int c)
            query = query.Where(o => o.AppliesToAll || db.Set<OfferClient>().Any(oc => oc.OfferId == o.OfferId && oc.IsActive && oc.ClientId == c));
        else
            query = query.Where(o => o.AppliesToAll);
        var offers = await query.Select(o => new { o.OfferId, o.Name, o.AppliesToAll, o.ValidFrom, o.ValidTo }).ToListAsync(ct);
        if (offers.Count == 0) return Array.Empty<OfferView>();
        var ids = offers.Select(o => o.OfferId).ToList();
        var itemQuery = db.Set<OfferItem>().AsNoTracking().Where(i => ids.Contains(i.OfferId) && i.IsActive);
        if (productIds is not null)
        {
            var pids = productIds.Distinct().ToList();
            itemQuery = itemQuery.Where(i => pids.Contains(i.ProductId));
        }
        var items = (await itemQuery.Select(i => new { i.OfferId, i.ProductId, i.Price, i.ImageFileId }).ToListAsync(ct))
            .GroupBy(i => i.OfferId).ToDictionary(g => g.Key, g => (IReadOnlyList<OfferItemView>)g.Select(i => new OfferItemView(i.ProductId, i.Price, i.ImageFileId)).ToList());
        IReadOnlyCollection<int> mine = clientId is int cid ? new[] { cid } : Array.Empty<int>();
        return offers.Select(o => new OfferView(o.OfferId, o.Name, true, true, o.AppliesToAll, o.ValidFrom, o.ValidTo, o.AppliesToAll ? Array.Empty<int>() : mine,
            items.GetValueOrDefault(o.OfferId) ?? Array.Empty<OfferItemView>())).ToList();
    }

    /// <summary>
    /// Lote 229 — la foto «del producto» de cada uno, según la regla: la suya y, si es un kit sin foto, la de la primera de sus piezas (por orden) que tenga foto.
    /// (La del renglón, si la hay, la antepone quien llama: <see cref="ProductImageRules.Resolve"/>.)
    /// </summary>
    public static async Task<Dictionary<int, int?>> ProductImagesAsync(TeikemDbContext db, IEnumerable<Product> products, int? kitTypeId, CancellationToken ct)
    {
        var list = products.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();
        var kitsWithout = list.Where(p => p.ImageFileId is null && kitTypeId is not null && p.ProductTypeLookupId == kitTypeId).Select(p => p.ProductId).ToList();
        var fromPieces = new Dictionary<int, int>();
        if (kitsWithout.Count > 0)
        {
            var rows = await (from c in db.Set<ProductKitComponent>().AsNoTracking()
                              join p in db.Set<Product>().AsNoTracking() on c.ComponentProductId equals p.ProductId
                              where kitsWithout.Contains(c.KitProductId) && p.ImageFileId != null
                              select new { c.KitProductId, c.SortOrder, c.ProductKitComponentId, p.ImageFileId }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.KitProductId))
                fromPieces[g.Key] = g.OrderBy(r => r.SortOrder).ThenBy(r => r.ProductKitComponentId).First().ImageFileId!.Value;
        }
        return list.ToDictionary(p => p.ProductId, p => ProductImageRules.Resolve(null, p.ImageFileId,
            kitTypeId is not null && p.ProductTypeLookupId == kitTypeId, fromPieces.TryGetValue(p.ProductId, out var f) ? f : null));
    }

    /// <summary>El PublicId de los archivos vigentes (fotos) por su id.</summary>
    public static async Task<Dictionary<int, Guid>> FilePublicIdsAsync(TeikemDbContext db, IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, Guid>();
        return await db.Set<Teikem.Domain.Files.StoredFile>().AsNoTracking().Where(f => list.Contains(f.StoredFileId) && f.IsActive).ToDictionaryAsync(f => f.StoredFileId, f => f.PublicId, ct);
    }
}
