using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 229: la oferta como entidad aparte y el kit creado para ella — solo se vende mientras la oferta esté publicada y vigente, queda inactivo para la venta al
// terminar (las órdenes viejas no cambian) y vuelve si se extiende; precio = el más bajo; avisos en la oferta, en el catálogo y en la orden.
public sealed class OfferKitTests
{
    private static async Task<(PriceWorld X, Product Strips, Product Monitor)> WorldAsync()
    {
        var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY", stock: 100m);
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY", stock: 100m);
        return (x, strips, monitor);
    }

    private static KitComponentRequest[] Pieces(Product strips, Product monitor) => new[] { new KitComponentRequest(strips.PublicId, 6m), new KitComponentRequest(monitor.PublicId, 1m) };

    [Fact]
    public async Task A_kit_created_for_an_offer_is_tied_to_it_not_adjustable_and_sells_only_while_the_offer_is_published_and_in_effect()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("OFERTA1", x.Today), default);
        Assert.False(offer.HasEndDate);
        Assert.Equal(OfferRules.RangeText(x.Today, null), offer.ValidityText);   // «desde el …, sin fecha de fin»
        Assert.Equal(PriceListStates.Draft, offer.State);

        var created = await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("OFERTA1", "Oferta 1", 41.94m, Pieces(strips, monitor), Brand: "PRODIGY"), default);
        var item = Assert.Single(created.Offer.Items);
        Assert.True(item.IsKit && item.IsOfferKit && !item.KitAdjustable);
        Assert.Equal(41.94m, item.Price);
        Assert.Equal(new[] { ("52800", 6m), ("070120", 1m) }, item.Kit!.Components.Select(c => (c.Sku, c.Quantity)).ToArray());
        var kit = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == created.ProductPublicId);
        Assert.NotNull(kit.OfferId);
        Assert.False(kit.KitAdjustable);

        // en borrador: no se vende (422 con el motivo) ni sale en el catálogo
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(created.ProductPublicId, 1m)));
        Assert.Equal("El kit OFERTA1 es de la oferta «OFERTA1», que está en borrador: no se puede vender.", ex.Message);
        Assert.DoesNotContain((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands.SelectMany(b => b.Offers.Concat(b.Products)), p => p.Sku == "OFERTA1");

        await x.Offers.PublishAsync(offer.PublicId, default);
        var order = await x.DraftAsync(x.C1, new SalesOrderLineRequest(created.ProductPublicId, 2m));
        Assert.Equal((41.94m, 83.88m), (order.Lines[0].UnitPrice, order.Subtotal));
        var price = Assert.Single(order.LinePrices!);
        Assert.Equal((PriceSources.Offer, offer.PublicId, "OFERTA1"), (price.PriceSource, price.OfferPublicId!.Value, price.OfferName));
    }

    [Fact]
    public async Task When_the_offer_ends_its_kit_stops_selling_old_orders_stay_and_extending_the_offer_brings_it_back()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("OFERTA1", x.Today.AddDays(-5), x.Today.AddDays(5)), default);
        var kit = (await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("OFERTA1", "Oferta 1", 41.94m, Pieces(strips, monitor)), default)).ProductPublicId;
        await x.Offers.PublishAsync(offer.PublicId, default);
        var made = await x.Sales.RequestAsync((await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 2m))).PublicId, null, default);

        // la oferta termina ayer
        await x.Offers.UpdateAsync(offer.PublicId, new OfferRequest(ValidTo: x.Today.AddDays(-1)), default);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m)));
        Assert.Equal(OfferRules.KitNotSellable("OFERTA1", new OfferView(0, "OFERTA1", true, true, true, x.Today.AddDays(-5), x.Today.AddDays(-1), Array.Empty<int>(),
            Array.Empty<OfferItemView>()), x.Today), ex.Message);
        Assert.DoesNotContain((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands.SelectMany(b => b.Offers.Concat(b.Products)), p => p.Sku == "OFERTA1");
        var still = await x.Sales.GetAsync(made.PublicId, default);   // la orden ya hecha no cambia
        Assert.Equal((41.94m, 2m, 2m), (still.Lines[0].UnitPrice, still.Lines[0].QtyOrdered, still.Lines[0].QtyReserved));
        Assert.True((await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == kit)).IsActive);   // no se borra ni se desactiva el producto

        // se le quita la fecha de fin: vuelve
        await x.Offers.UpdateAsync(offer.PublicId, new OfferRequest(ClearValidTo: true), default);
        Assert.Equal(41.94m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m))).Lines[0].UnitPrice);
        // desactivada: tampoco se vende
        await x.Offers.DeactivateAsync(offer.PublicId, default);
        await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m)));
    }

    [Fact]
    public async Task A_kit_of_an_offer_goes_neither_in_a_price_list_nor_in_another_offer()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("OFERTA1", x.Today), default);
        var kit = (await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("OFERTA1", "Oferta 1", 41.94m, Pieces(strips, monitor)), default)).ProductPublicId;
        var list = await x.Lists.CreateAsync(new PriceListRequest("General", x.Today, null), default);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.UpsertItemsAsync(list.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(kit, 40m) }), default));
        Assert.Equal(OfferRules.OfferKitNotInLists("OFERTA1", "OFERTA1"), ex.Message);
        var other = await x.Offers.CreateAsync(new OfferRequest("Otra", x.Today), default);
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpsertItemsAsync(other.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, 40m) }), default));
        Assert.Equal(OfferRules.ItemKitOfOtherOffer("OFERTA1", "OFERTA1"), ex.Message);
        // un SKU repetido no deja nada a medias
        await Assert.ThrowsAsync<ConflictException>(() => x.Offers.CreateKitAsync(other.PublicId, new OfferKitRequest("OFERTA1", "Repetido", 1m, Pieces(strips, monitor)), default));
        Assert.Empty((await x.Offers.GetAsync(other.PublicId, default)).Items);
        // el kit de la oferta se cambia desde la oferta (piezas y nombre); desde otra oferta no
        var updated = await x.Offers.UpdateKitAsync(offer.PublicId, kit, new OfferKitUpdateRequest("Oferta 1 nueva", new[] { new KitComponentRequest(strips.PublicId, 5m) }), default);
        Assert.Equal(("Oferta 1 nueva", 5m), (updated.Items[0].ProductName, Assert.Single(updated.Items[0].Kit!.Components).Quantity));
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateKitAsync(other.PublicId, kit, new OfferKitUpdateRequest("x"), default));
    }

    [Fact]
    public async Task The_lowest_price_wins_and_clashing_offers_bring_warnings_in_the_offer_the_catalog_and_the_order()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        await x.PublishedListAsync("General", x.Today, null, null, (strips, 6.50m), (monitor, 7m));
        await x.PublishedListAsync("Negociada C1", x.Today, null, new[] { x.C1 }, (strips, 5m));

        var navidad = await x.Offers.CreateAsync(new OfferRequest("Navidad", x.Today, x.Today.AddDays(30)), default);
        await x.Offers.UpsertItemsAsync(navidad.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 5.25m), new OfferItemRequest(monitor.PublicId, 7.50m) }), default);
        var published = await x.Offers.PublishAsync(navidad.PublicId, default);
        // el monitor sale más caro que la lista general: aviso
        var above = Assert.Single(published.Warnings);
        Assert.Equal((PriceNoticeCodes.OfferAboveGeneral, "070120", OfferRules.AboveGeneralMessage("070120", 7.50m, 7m)), (above.Code, above.Sku, above.Message));

        // otra oferta más barata que se cruza: aviso al armarla (en la que es más cara)
        var finDeAno = await x.Offers.CreateAsync(new OfferRequest("Fin de año", x.Today.AddDays(-1), null), default);
        var withItem = await x.Offers.UpsertItemsAsync(finDeAno.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 5.75m) }), default);
        var clash = Assert.Single(withItem.Warnings);
        Assert.Equal((PriceNoticeCodes.CheaperOffer, "Navidad", navidad.PublicId, 5.25m), (clash.Code, clash.OtherOfferName, clash.OtherOfferPublicId!.Value, clash.OtherPrice!.Value));
        await x.Offers.PublishAsync(finDeAno.PublicId, default);

        // C1: negociado 5.00 gana a las ofertas (5.25 / 5.75); C2: la oferta más barata (5.25) gana a la general (6.50) y se avisa que está en dos
        var c1 = await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 1m));
        Assert.Equal((5m, PriceSources.ClientList), (c1.Lines[0].UnitPrice, c1.LinePrices![0].PriceSource));
        var c2 = await x.DraftAsync(x.C2, new SalesOrderLineRequest(strips.PublicId, 1m), new SalesOrderLineRequest(monitor.PublicId, 1m));
        Assert.Equal(new[] { 5.25m, 7m }, c2.Lines.Select(l => l.UnitPrice).ToArray());
        var stripsPrice = c2.LinePrices!.Single(p => p.Sku == "52800");
        Assert.Equal((PriceSources.Offer, "Navidad"), (stripsPrice.PriceSource, stripsPrice.OfferName));
        var two = Assert.Single(stripsPrice.Notices);
        Assert.Equal((PriceNoticeCodes.TwoOffers, "Navidad", "Fin de año"), (two.Code, two.OfferName, two.OtherOfferName));
        var monitorPrice = c2.LinePrices!.Single(p => p.Sku == "070120");
        Assert.Equal((PriceSources.GeneralList, PriceNoticeCodes.OfferAboveGeneral), (monitorPrice.PriceSource, Assert.Single(monitorPrice.Notices).Code));

        // el catálogo: el producto normal en oferta sale arriba (tarjeta) y también en la lista de su marca, con el precio resuelto y los avisos
        var cat = await x.Catalog.CatalogAsync(x.C2.PublicId, default);
        var prodigy = Assert.Single(cat.Brands);
        var card = prodigy.Offers.Single(o => o.Sku == "52800");
        Assert.Equal((5.25m, PriceSources.Offer, "Navidad", 5.25m, true), (card.Price!.Value, card.PriceSource, card.OfferName, card.OfferPrice!.Value, card.IsPromotion));
        Assert.Equal(PriceNoticeCodes.TwoOffers, Assert.Single(card.Notices!).Code);
        Assert.Contains(prodigy.Products, p => p.Sku == "52800" && p.Price == 5.25m && p.PriceSource == PriceSources.Offer);
        var forC1 = Assert.Single((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands).Offers.Single(o => o.Sku == "52800");
        Assert.Equal((5m, PriceSources.ClientList, 5.25m), (forC1.Price!.Value, forC1.PriceSource, forC1.OfferPrice!.Value));   // la tarjeta, al precio negociado (más bajo)
    }

    [Fact]
    public async Task An_offer_for_chosen_clients_only_prices_for_them_and_publishing_needs_items_and_clients()
    {
        var (x, strips, _) = await WorldAsync();
        await using var __ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("Solo C1", x.Today, null, AppliesToAll: false), default);
        Assert.Equal(OfferRules.PublishNeedsItems, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.PublishAsync(offer.PublicId, default))).Message);
        await x.Offers.UpsertItemsAsync(offer.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 3m) }), default);
        Assert.Equal(OfferRules.PublishNeedsClients, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.PublishAsync(offer.PublicId, default))).Message);
        await x.Offers.AddClientsAsync(offer.PublicId, new OfferClientsRequest(new[] { x.C1.PublicId }), default);
        var pub = await x.Offers.PublishAsync(offer.PublicId, default);
        Assert.True(pub.InEffect);
        Assert.Equal(PriceListStates.Active, pub.State);
        Assert.Equal(3m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        Assert.Equal(6.99m, (await x.DraftAsync(x.C2, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        Assert.Equal(OfferRules.PublishNeedsClients, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.RemoveClientAsync(offer.PublicId, x.C1.PublicId, default))).Message);
        Assert.Equal(OfferRules.ClientsOnGeneral, (await Assert.ThrowsAsync<StatusRuleException>(async () =>
        {
            var general = await x.Offers.CreateAsync(new OfferRequest("Todos", x.Today), default);
            await x.Offers.AddClientsAsync(general.PublicId, new OfferClientsRequest(new[] { x.C1.PublicId }), default);
        })).Message);
        Assert.Equal(new[] { "Solo C1", "Todos" }, (await x.Offers.ListAsync(null, null, false, default)).Select(o => o.Name).OrderBy(n => n).ToArray());
    }
}
