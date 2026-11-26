using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Clients;
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
    /// Lote 247: también las de su cadena <paramref name="chainId"/> (fila PriceListChain activa; null = sin cadena o cadena inactiva: quien llama la resuelve con
    /// <see cref="ClientChainQueries.ActiveChainOfClientAsync"/>). La vista lleva ClientIds o ChainIds según por qué coincidió (el cliente, la cadena o los dos) e
    /// IsSpecial (es el precio especial, lista dedicada).
    /// </summary>
    public static async Task<IReadOnlyList<PriceListView>> InEffectAsync(TeikemDbContext db, int? clientId, DateOnly date, IReadOnlyCollection<int>? productIds, CancellationToken ct,
        int? chainId = null)
    {
        var hasClient = clientId is not null;
        var cid = clientId ?? 0;
        var hasChain = chainId is not null;
        var chid = chainId ?? 0;
        var lists = await db.Set<PriceList>().AsNoTracking()
            .Where(l => l.IsActive && l.IsPublished && l.ValidFrom <= date && (l.ValidTo == null || l.ValidTo >= date))
            .Select(l => new
            {
                l.PriceListId, l.Name, l.AppliesToAll, l.ValidFrom, l.ValidTo,
                IsSpecial = l.SpecialClientId != null || l.SpecialChainId != null,
                ByClient = hasClient && db.Set<PriceListClient>().Any(pc => pc.PriceListId == l.PriceListId && pc.IsActive && pc.ClientId == cid),
                ByChain = hasChain && db.Set<PriceListChain>().Any(pc => pc.PriceListId == l.PriceListId && pc.IsActive && pc.ChainId == chid),
            })
            .Where(l => l.AppliesToAll || l.ByClient || l.ByChain)
            .ToListAsync(ct);
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
        IReadOnlyCollection<int> mine = hasClient ? new[] { cid } : Array.Empty<int>();
        IReadOnlyCollection<int> myChain = hasChain ? new[] { chid } : Array.Empty<int>();
        return lists.Select(l => new PriceListView(l.PriceListId, l.Name, true, l.AppliesToAll, l.ValidFrom, l.ValidTo,
            !l.AppliesToAll && l.ByClient ? mine : Array.Empty<int>(),
            items.GetValueOrDefault(l.PriceListId) ?? Array.Empty<PriceListItemView>(),
            !l.AppliesToAll && l.ByChain ? myChain : null, l.IsSpecial)).ToList();
    }

    /// <summary>
    /// El precio resuelto de cada producto para el cliente en la fecha (<see cref="OfferRules.Resolve"/>). Lote 247: sin jerarquía, gana el MÁS BAJO entre la lista
    /// del cliente, la de su cadena (si está activa), la oferta vigente más barata que le aplique y la lista general; si ninguna lo trae, el precio de venta del
    /// producto. La cadena se lee UNA vez aquí (una cadena inactiva cuenta como sin cadena).
    /// </summary>
    public static async Task<Dictionary<int, ResolvedPrice>> ResolveAsync(TeikemDbContext db, IEnumerable<Product> products, int? clientId, DateOnly date, CancellationToken ct)
    {
        var chainId = await ClientChainQueries.ActiveChainOfClientAsync(db, clientId, ct);
        return await ResolveAsync(db, products, clientId, chainId, date, ct);
    }

    /// <summary>
    /// Lote 247 — como <see cref="ResolveAsync(TeikemDbContext, IEnumerable{Product}, int?, DateOnly, CancellationToken)"/>, con la cadena ACTIVA ya leída
    /// (<paramref name="chainId"/>; null = sin cadena) para no repetir la consulta. Sin cliente y con cadena = el precio de los clientes de esa cadena que no
    /// tienen precio propio (lo usa «se cobra hoy» de la ficha de la cadena).
    /// </summary>
    public static async Task<Dictionary<int, ResolvedPrice>> ResolveAsync(TeikemDbContext db, IEnumerable<Product> products, int? clientId, int? chainId, DateOnly date,
        CancellationToken ct)
    {
        var list = products.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();
        if (list.Count == 0) return new Dictionary<int, ResolvedPrice>();
        var ids = list.Select(p => p.ProductId).ToList();
        var lists = await InEffectAsync(db, clientId, date, ids, ct, chainId);
        var offers = await OffersInEffectAsync(db, clientId, date, ids, ct, chainId);
        return list.ToDictionary(p => p.ProductId, p => OfferRules.Resolve(p.ProductId, clientId, date, lists, offers, p.SalePrice, chainId));
    }

    /// <summary>
    /// Lote 229 — las ofertas PUBLICADAS, activas y vigentes en la fecha que alcanzan al cliente, con sus renglones activos (solo los de esos productos si se
    /// indican). Lote 247, según el público de la oferta (Offer.AudienceMode) y la cadena ACTIVA del cliente <paramref name="chainId"/>:
    /// - ALL: siempre;
    /// - INCLUDE: si tiene fila activa del cliente o de su cadena;
    /// - EXCLUDE: si no tiene fila activa ni del cliente ni de su cadena.
    /// Sin cliente: las ALL, las INCLUDE que traen la cadena y las EXCLUDE que no la excluyen (sin cadena: ALL y EXCLUDE, DECISIÓN 9). La vista lleva AudienceMode y
    /// las filas que coincidieron (ClientIds / ChainIds).
    /// </summary>
    public static async Task<IReadOnlyList<OfferView>> OffersInEffectAsync(TeikemDbContext db, int? clientId, DateOnly date, IReadOnlyCollection<int>? productIds, CancellationToken ct,
        int? chainId = null)
    {
        var hasClient = clientId is not null;
        var cid = clientId ?? 0;
        var hasChain = chainId is not null;
        var chid = chainId ?? 0;
        const string all = OfferAudienceModes.All, include = OfferAudienceModes.Include, exclude = OfferAudienceModes.Exclude;
        var offers = await db.Set<Offer>().AsNoTracking()
            .Where(o => o.IsActive && o.IsPublished && o.ValidFrom <= date && (o.ValidTo == null || o.ValidTo >= date))
            .Select(o => new
            {
                o.OfferId, o.Name, o.AppliesToAll, o.AudienceMode, o.ValidFrom, o.ValidTo,
                ByClient = hasClient && db.Set<OfferClient>().Any(oc => oc.OfferId == o.OfferId && oc.IsActive && oc.ClientId == cid),
                ByChain = hasChain && db.Set<OfferChain>().Any(oc => oc.OfferId == o.OfferId && oc.IsActive && oc.ChainId == chid),
            })
            .Where(o => o.AudienceMode == all
                || (o.AudienceMode == include && (o.ByClient || o.ByChain))
                || (o.AudienceMode == exclude && !o.ByClient && !o.ByChain))
            .ToListAsync(ct);
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
        IReadOnlyCollection<int> mine = hasClient ? new[] { cid } : Array.Empty<int>();
        IReadOnlyCollection<int> myChain = hasChain ? new[] { chid } : Array.Empty<int>();
        return offers.Select(o => new OfferView(o.OfferId, o.Name, true, true, o.AppliesToAll, o.ValidFrom, o.ValidTo, o.ByClient ? mine : Array.Empty<int>(),
            items.GetValueOrDefault(o.OfferId) ?? Array.Empty<OfferItemView>(), o.AudienceMode, o.ByChain ? myChain : null)).ToList();
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

    /// <summary>Lote 240: ¿la compañía maneja fotos de productos? (Tenant.ProductPhotosEnabled; sin fila de compañía = sí, como era).</summary>
    public static async Task<bool> PhotosEnabledAsync(TeikemDbContext db, CancellationToken ct)
        => await db.Tenants.AsNoTracking().Where(t => t.TenantId == db.CurrentTenantId).Select(t => (bool?)t.ProductPhotosEnabled).FirstOrDefaultAsync(ct) ?? true;

    /// <summary>Lote 240: antes de subir o cambiar una foto (producto, oferta o renglón de lista): con las fotos apagadas es 422.</summary>
    public static async Task EnsurePhotosEnabledAsync(TeikemDbContext db, CancellationToken ct)
    {
        if (!await PhotosEnabledAsync(db, ct)) throw new Teikem.Infrastructure.Exceptions.StatusRuleException(Teikem.Domain.Wms.ProductImageRules.PhotosDisabled);
    }

    /// <summary>El PublicId de los archivos vigentes (fotos) por su id.</summary>
    public static async Task<Dictionary<int, Guid>> FilePublicIdsAsync(TeikemDbContext db, IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, Guid>();
        // Lote 240: con las fotos apagadas en la compañía NINGUNA foto sale del API (este es el único punto por donde pasan los identificadores de foto de productos, ofertas y listas)
        if (!await PhotosEnabledAsync(db, ct)) return new Dictionary<int, Guid>();
        return await db.Set<Teikem.Domain.Files.StoredFile>().AsNoTracking().Where(f => list.Contains(f.StoredFileId) && f.IsActive).ToDictionaryAsync(f => f.StoredFileId, f => f.PublicId, ct);
    }
}
