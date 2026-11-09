using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 229 — la orden de venta y las ofertas: al crear o editar un borrador con líneas, la respuesta dice de dónde salió hoy el precio de cada producto (OFFER,
/// CLIENT_LIST, GENERAL_LIST, PRODUCT o NONE, con la oferta o la lista) y trae los avisos de precio: el producto está en dos ofertas a la vez (gana la más barata) o
/// la oferta sale más cara que la lista general (gana el más bajo). Son avisos, no errores; solo van en esa respuesta (no se guardan).
/// </summary>
public sealed partial class SalesOrderService
{
    private async Task<SalesOrderDto> WithLinePricesAsync(SalesOrderDto dto, IReadOnlyList<PlannedLine>? planned, int clientId, DateOnly date, CancellationToken ct)
    {
        if (planned is null || planned.Count == 0) return dto;
        var products = planned.Select(l => l.Product).GroupBy(p => p.ProductId).Select(g => g.First()).ToList();
        var resolved = await PriceListQueries.ResolveAsync(db, products, clientId, date, ct);
        var offerIds = resolved.Values.SelectMany(r => (r.Notices ?? Array.Empty<PriceNotice>()).SelectMany(n => new[] { (int?)n.OfferId, n.OtherOfferId }).Append(r.OfferId))
            .Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var offers = offerIds.Count == 0 ? new Dictionary<int, (Guid PublicId, string Name)>()
            : (await db.Set<Offer>().AsNoTracking().Where(o => offerIds.Contains(o.OfferId)).Select(o => new { o.OfferId, o.PublicId, o.Name }).ToListAsync(ct))
                .ToDictionary(o => o.OfferId, o => (o.PublicId, o.Name));
        var listIds = resolved.Values.Where(r => r.PriceListId is not null).Select(r => r.PriceListId!.Value).Distinct().ToList();
        var lists = listIds.Count == 0 ? new Dictionary<int, (Guid PublicId, string Name)>()
            : (await db.Set<PriceList>().AsNoTracking().Where(l => listIds.Contains(l.PriceListId)).Select(l => new { l.PriceListId, l.PublicId, l.Name }).ToListAsync(ct))
                .ToDictionary(l => l.PriceListId, l => (l.PublicId, l.Name));
        var offerPublic = offers.ToDictionary(kv => kv.Key, kv => kv.Value.PublicId);

        var result = products.Select(p =>
        {
            var r = resolved[p.ProductId];
            var offer = r.OfferId is int oid && offers.TryGetValue(oid, out var o) ? o : ((Guid PublicId, string Name)?)null;
            var list = r.PriceListId is int lid && lists.TryGetValue(lid, out var l) ? l : ((Guid PublicId, string Name)?)null;
            var notices = (r.Notices ?? Array.Empty<PriceNotice>()).Select(n => SalesCatalogService.Notice(p, n, offerPublic)).ToList();
            return new SalesLinePriceDto(p.PublicId, p.Sku, r.Price, r.Source, offer?.PublicId, offer?.Name, list?.PublicId, list?.Name, notices);
        }).ToList();
        return dto with { LinePrices = result };
    }
}
