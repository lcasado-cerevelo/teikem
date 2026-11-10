using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 229: la oferta como entidad aparte y el kit creado para ella — solo se vende mientras la oferta esté publicada y vigente, queda inactivo para la venta al
// terminar (las órdenes viejas no cambian) y vuelve si se extiende; precio = el más bajo; avisos en la oferta, en el catálogo y en la orden.
// Lote 231: el kit creado desde la oferta queda en la biblioteca de kits de oferta (IsOfferKit; OfferId ya no se usa). La biblioteca: OfferKitLibraryTests.
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
    public async Task A_kit_created_from_an_offer_is_an_offer_kit_not_adjustable_and_sells_only_while_the_offer_is_published_and_in_effect()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("OFERTA1", x.Today), default);
        Assert.False(offer.HasEndDate);
        Assert.Equal(OfferRules.RangeText(x.Today, null), offer.ValidityText);   // «desde el …, sin fecha de fin»
        Assert.True(offer.IsPublished && offer.InEffect);   // Lote 234: sin borrador, nace publicada y vale desde hoy
        Assert.Equal(PriceListStates.Active, offer.State);

        var created = await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("OFERTA1", "Oferta 1", 41.94m, Pieces(strips, monitor), Brand: "PRODIGY"), default);
        var item = Assert.Single(created.Offer.Items);
        Assert.True(item.IsKit && item.IsOfferKit && !item.KitAdjustable);
        Assert.Equal(41.94m, item.Price);
        Assert.Equal(new[] { ("52800", 6m), ("070120", 1m) }, item.Kit!.Components.Select(c => (c.Sku, c.Quantity)).ToArray());
        var kit = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == created.ProductPublicId);
        Assert.True(kit.IsOfferKit);   // Lote 231: kit de la biblioteca
        Assert.Null(kit.OfferId);   // ya no se usa
        Assert.False(kit.KitAdjustable);

        // en borrador: no se vende (422 con el motivo) ni sale en el catálogo
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(created.ProductPublicId, 1m)));
        Assert.Equal("El kit de oferta OFERTA1 no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.", ex.Message);
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
        Assert.Equal(OfferKitRules.NotOnSale("OFERTA1"), ex.Message);
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
    public async Task An_offer_kit_goes_in_no_price_list_and_once_in_an_offer_only_its_name_changes()   // Lote 231 (antes: atado a su oferta)
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var offer = await x.Offers.CreateAsync(new OfferRequest("OFERTA1", x.Today), default);
        var kit = (await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("OFERTA1", "Oferta 1", 41.94m, Pieces(strips, monitor)), default)).ProductPublicId;
        var list = await x.Lists.CreateAsync(new PriceListRequest("General", x.Today, null), default);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Lists.UpsertItemsAsync(list.PublicId, new PriceListItemsRequest(new[] { new PriceListItemRequest(kit, 40m) }), default));
        Assert.Equal("El kit OFERTA1 es un kit de oferta: su precio va en una oferta, no en una lista de precios.", ex.Message);
        // otra oferta SÍ lo puede usar (biblioteca)
        var other = await x.Offers.CreateAsync(new OfferRequest("Otra", x.Today), default);
        var withKit = await x.Offers.UpsertItemsAsync(other.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, 40m) }), default);
        Assert.True(Assert.Single(withKit.Items).IsOfferKit);
        // un SKU repetido no deja nada a medias
        await Assert.ThrowsAsync<ConflictException>(() => x.Offers.CreateKitAsync(other.PublicId, new OfferKitRequest("OFERTA1", "Repetido", 1m, Pieces(strips, monitor)), default));
        Assert.Single((await x.Offers.GetAsync(other.PublicId, default)).Items);
        // desde la oferta: el nombre sí; las piezas ya no (el kit ya está en una oferta: se crea otro)
        var updated = await x.Offers.UpdateKitAsync(offer.PublicId, kit, new OfferKitUpdateRequest("Oferta 1 nueva"), default);
        Assert.Equal("Oferta 1 nueva", updated.Items[0].ProductName);
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateKitAsync(offer.PublicId, kit, new OfferKitUpdateRequest(null, new[] { new KitComponentRequest(strips.PublicId, 5m) }), default));
        Assert.Equal(OfferKitRules.CompositionLocked("OFERTA1"), ex.Message);
        // desde una oferta que no lo trae: 422
        var third = await x.Offers.CreateAsync(new OfferRequest("Tercera", x.Today), default);
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateKitAsync(third.PublicId, kit, new OfferKitUpdateRequest("x"), default));
        Assert.Equal(OfferKitRules.NotInThisOffer, ex.Message);
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
    public async Task An_offer_for_chosen_clients_only_prices_for_them_and_is_published_from_the_start()
    {
        var (x, strips, _) = await WorldAsync();
        await using var __ = x;
        // Lote 234: la oferta nace publicada; sin clientes escogidos todavía no aplica a nadie
        var offer = await x.Offers.CreateAsync(new OfferRequest("Solo C1", x.Today, null, AppliesToAll: false), default);
        Assert.True(offer.IsPublished);
        await x.Offers.UpsertItemsAsync(offer.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 3m) }), default);
        Assert.Equal(6.99m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        await x.Offers.AddClientsAsync(offer.PublicId, new OfferClientsRequest(new[] { x.C1.PublicId }), default);
        var pub = await x.Offers.PublishAsync(offer.PublicId, default);   // publicar otra vez no es un error (compatibilidad)
        Assert.True(pub.InEffect);
        Assert.Equal(PriceListStates.Active, pub.State);
        Assert.Equal(3m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        Assert.Equal(6.99m, (await x.DraftAsync(x.C2, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        // una oferta que quedó en borrador (de antes del lote 234) sigue exigiendo renglones y clientes para publicarse
        var old = await x.Offers.CreateAsync(new OfferRequest("Vieja", x.Today, null, AppliesToAll: false), default);
        await x.Offers.UnpublishAsync(old.PublicId, default);
        Assert.Equal(OfferRules.PublishNeedsItems, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.PublishAsync(old.PublicId, default))).Message);
        await x.Offers.UpsertItemsAsync(old.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(strips.PublicId, 9m) }), default);
        Assert.Equal(OfferRules.PublishNeedsClients, (await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.PublishAsync(old.PublicId, default))).Message);
        await x.Offers.DeactivateAsync(old.PublicId, default);
        // sin borrador, quitar el último cliente no se rechaza: la oferta deja de aplicar hasta que se escoja otro
        var none = await x.Offers.RemoveClientAsync(offer.PublicId, x.C1.PublicId, default);
        Assert.Empty(none.Clients);
        Assert.Equal(6.99m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(strips.PublicId, 1m))).Lines[0].UnitPrice);
        Assert.Equal(OfferRules.ClientsOnGeneral, (await Assert.ThrowsAsync<StatusRuleException>(async () =>
        {
            var general = await x.Offers.CreateAsync(new OfferRequest("Todos", x.Today), default);
            await x.Offers.AddClientsAsync(general.PublicId, new OfferClientsRequest(new[] { x.C1.PublicId }), default);
        })).Message);
        Assert.Equal(new[] { "Solo C1", "Todos" }, (await x.Offers.ListAsync(null, null, false, default)).Select(o => o.Name).OrderBy(n => n).ToArray());
    }
}
