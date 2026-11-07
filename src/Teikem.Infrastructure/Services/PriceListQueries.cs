using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 221 — lecturas de las listas de precios que comparten la orden de venta (precio de la línea) y el catálogo del pedido. La compañía la pone el filtro global;
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

    /// <summary>El precio resuelto de cada producto para el cliente en la fecha (lista del cliente &gt; lista general &gt; precio de venta del producto).</summary>
    public static async Task<Dictionary<int, ResolvedPrice>> ResolveAsync(TeikemDbContext db, IEnumerable<Product> products, int? clientId, DateOnly date, CancellationToken ct)
    {
        var list = products.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();
        if (list.Count == 0) return new Dictionary<int, ResolvedPrice>();
        var lists = await InEffectAsync(db, clientId, date, list.Select(p => p.ProductId).ToList(), ct);
        return list.ToDictionary(p => p.ProductId, p => PriceListRules.Resolve(p.ProductId, clientId, date, lists, p.SalePrice));
    }
}
