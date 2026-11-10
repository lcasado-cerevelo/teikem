using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 231 — kit de oferta con biblioteca (docs/ofertas-kits-fotos-diseno.md §10–§12): el kit de oferta deja de estar atado a una oferta, queda en la biblioteca y
/// una oferta lo usa con un renglón; se reusa en otra oferta con otro precio (se sugiere el último); su composición no cambia una vez usado; solo se vende mientras
/// esté en una oferta publicada y vigente para el cliente; dos ofertas con el mismo kit = la más barata y aviso; no aparece en Productos e inventario; desde Productos
/// ya no se convierte un producto en kit; solo el kit de oferta cambia piezas al pedir.
/// </summary>
public sealed class OfferKitLibraryTests
{
    private static async Task<(PriceWorld X, Product Strips, Product Monitor)> WorldAsync()
    {
        var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY", stock: 100m);
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY", stock: 100m);
        return (x, strips, monitor);
    }

    private static KitComponentRequest[] Pieces(Product strips, Product monitor, decimal stripsQty = 6m)
        => new[] { new KitComponentRequest(strips.PublicId, stripsQty), new KitComponentRequest(monitor.PublicId, 1m, NotSubstitutable: true) };

    private static async Task<OfferDto> PublishedOfferAsync(PriceWorld x, string name, DateOnly from, DateOnly? to, params (Guid Product, decimal? Price)[] items)
    {
        var o = await x.Offers.CreateAsync(new OfferRequest(name, from, to), default);
        await x.Offers.UpsertItemsAsync(o.PublicId, new OfferItemsRequest(items.Select(i => new OfferItemRequest(i.Product, i.Price)).ToList()), default);
        return await x.Offers.PublishAsync(o.PublicId, default);
    }

    [Fact]
    public async Task A_library_kit_is_created_without_an_offer_and_while_unused_its_pieces_can_still_change()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor), Brand: "PRODIGY"), default);
        Assert.Equal(("OFERTA1", false, (decimal?)null, (string?)null, 0, false), (kit.Sku, kit.Adjustable, kit.LastPrice, kit.LastOfferName, kit.OfferCount, kit.CompositionLocked));
        Assert.Equal(new[] { ("52800", 6m, false), ("070120", 1m, true) }, kit.Components.Select(c => (c.Sku, c.Quantity, c.NotSubstitutable)).ToArray());
        var row = await x.F.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == kit.ProductPublicId);
        Assert.True(row.IsOfferKit && row.OfferId is null && row.IsActive);
        Assert.Equal(kit.ProductPublicId, Assert.Single(await x.Offers.ListKitsAsync("Oferta 1", false, default)).ProductPublicId);   // busca por nombre
        Assert.Single(await x.Offers.ListKitsAsync("FERTA1", false, default));   // o por SKU
        Assert.Empty(await x.Offers.ListKitsAsync("NADA", false, default));

        // sin usar: la composición se puede corregir (y el nombre), y «ajustable»
        var fixedKit = await x.Offers.UpdateLibraryKitAsync(kit.ProductPublicId, new OfferKitUpdateRequest("Oferta uno", Pieces(strips, monitor, 5m), Adjustable: true), default);
        Assert.Equal(("Oferta uno", 5m, true), (fixedKit.Name, fixedKit.Components[0].Quantity, fixedKit.Adjustable));

        // no está en ninguna oferta: no se vende
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit.ProductPublicId, 1m)));
        Assert.Equal(OfferKitRules.NotOnSale("OFERTA1"), ex.Message);
        // 404 de lo que no es kit de oferta
        Assert.Equal("Kit de oferta no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() => x.Offers.GetKitAsync(strips.PublicId, default))).Message);
        // un SKU repetido: 409 sin dejar nada a medias
        await Assert.ThrowsAsync<ConflictException>(() => x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Otra", Pieces(strips, monitor)), default));
        Assert.Single(await x.Offers.ListKitsAsync(null, false, default));
    }

    [Fact]
    public async Task A_library_kit_is_reused_in_another_offer_with_another_price_and_the_last_price_is_suggested()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = (await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor)), default)).ProductPublicId;

        // nunca usado: sin precio no entra
        var nov = await x.Offers.CreateAsync(new OfferRequest("Noviembre", x.Today.AddDays(-20), x.Today.AddDays(-1)), default);
        var ve = await Assert.ThrowsAsync<ValidationException>(() => x.Offers.UpsertItemsAsync(nov.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, null) }), default));
        Assert.Equal(OfferRules.ItemPriceRequired, ve.Message);
        await x.Offers.UpsertItemsAsync(nov.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, 41.94m) }), default);
        var lib = await x.Offers.GetKitAsync(kit, default);
        Assert.Equal((41.94m, "Noviembre", nov.PublicId, 1, true), (lib.LastPrice!.Value, lib.LastOfferName, lib.LastOfferPublicId!.Value, lib.OfferCount, lib.CompositionLocked));

        // enero: el mismo kit con otro precio
        var jan = await PublishedOfferAsync(x, "Enero", x.Today, null, (kit, 39.99m));
        Assert.Equal(39.99m, Assert.Single(jan.Items).Price);
        lib = await x.Offers.GetKitAsync(kit, default);
        Assert.Equal((39.99m, "Enero", 2), (lib.LastPrice!.Value, lib.LastOfferName, lib.OfferCount));

        // una tercera oferta sin precio: toma el último (39.99)
        var feb = await x.Offers.CreateAsync(new OfferRequest("Febrero", x.Today.AddDays(30)), default);
        var withLast = await x.Offers.UpsertItemsAsync(feb.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, null) }), default);
        Assert.Equal(39.99m, Assert.Single(withLast.Items).Price);

        // se vende al precio de la oferta vigente (enero); la de noviembre ya terminó
        var order = await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m));
        Assert.Equal((39.99m, PriceSources.Offer, "Enero"), (order.Lines[0].UnitPrice, order.LinePrices![0].PriceSource, order.LinePrices[0].OfferName));
    }

    [Fact]
    public async Task Once_used_the_composition_of_a_library_kit_does_not_change_but_its_name_does()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = (await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor)), default)).ProductPublicId;
        var offer = await x.Offers.CreateAsync(new OfferRequest("Noviembre", x.Today), default);
        var withKit = await x.Offers.UpsertItemsAsync(offer.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, 41.94m) }), default);
        await x.Offers.RemoveItemAsync(offer.PublicId, Assert.Single(withKit.Items).Id, default);   // aunque se quite el renglón, ya se usó

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateLibraryKitAsync(kit, new OfferKitUpdateRequest(null, Pieces(strips, monitor, 5m)), default));
        Assert.Equal("La composición del kit OFERTA1 ya no se puede cambiar porque ya se usó en una oferta o en un pedido: cree un kit nuevo con las piezas que quiera.", ex.Message);
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateLibraryKitAsync(kit, new OfferKitUpdateRequest(null, null, Adjustable: true), default));
        Assert.Equal(OfferKitRules.CompositionLocked("OFERTA1"), ex.Message);
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Offers.UpdateLibraryKitAsync(kit,
            new OfferKitUpdateRequest(null, new[] { new KitComponentRequest(strips.PublicId, 6m), new KitComponentRequest(monitor.PublicId, 1m, NotSubstitutable: false) }), default));
        Assert.Equal(OfferKitRules.CompositionLocked("OFERTA1"), ex.Message);   // la marca «no sustituible» también es composición

        // la misma composición (aunque en otro orden) y el nombre: sí
        var same = await x.Offers.UpdateLibraryKitAsync(kit, new OfferKitUpdateRequest("Oferta 1 de noviembre", Pieces(strips, monitor).Reverse().ToList()), default);
        Assert.Equal(("Oferta 1 de noviembre", true), (same.Name, same.CompositionLocked));
        Assert.Equal(new[] { (6m, false), (1m, true) }, same.Components.OrderBy(c => c.Sku == "52800" ? 0 : 1).Select(c => (c.Quantity, c.NotSubstitutable)).ToArray());
        // desde Productos tampoco
        ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Kits.SetFromProductsAsync(kit, new KitSetRequest(true, null, Pieces(strips, monitor, 5m)), default));
        Assert.Equal(OfferKitRules.ManagedInOffers("OFERTA1"), ex.Message);
    }

    [Fact]
    public async Task A_library_kit_sells_only_while_in_a_published_offer_in_effect_for_the_client_and_old_orders_stay()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = (await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor), Brand: "PRODIGY"), default)).ProductPublicId;
        var offer = await x.Offers.CreateAsync(new OfferRequest("Solo C1", x.Today.AddDays(-5), x.Today.AddDays(5), AppliesToAll: false), default);
        await x.Offers.UpsertItemsAsync(offer.PublicId, new OfferItemsRequest(new[] { new OfferItemRequest(kit, 41.94m) }), default);
        await x.Offers.AddClientsAsync(offer.PublicId, new OfferClientsRequest(new[] { x.C1.PublicId }), default);

        // Lote 234: la oferta nace publicada; devuelta a borrador (compatibilidad) no se vende
        await x.Offers.UnpublishAsync(offer.PublicId, default);
        await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m)));
        Assert.DoesNotContain((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands.SelectMany(b => b.Offers.Concat(b.Products)), p => p.Sku == "OFERTA1");

        await x.Offers.PublishAsync(offer.PublicId, default);
        var made = await x.Sales.RequestAsync((await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 2m))).PublicId, null, default);
        var card = Assert.Single((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands).Offers.Single(o => o.Sku == "OFERTA1");
        Assert.Equal((41.94m, true, true), (card.Price!.Value, card.IsOfferKit, card.IsKit));
        Assert.DoesNotContain(Assert.Single((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands).Products, p => p.Sku == "OFERTA1");   // solo como tarjeta
        // a C2 la oferta no le aplica: ni catálogo ni pedido
        Assert.Equal(OfferKitRules.NotOnSale("OFERTA1"), (await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C2, new SalesOrderLineRequest(kit, 1m)))).Message);
        Assert.DoesNotContain((await x.Catalog.CatalogAsync(x.C2.PublicId, default)).Brands.SelectMany(b => b.Offers.Concat(b.Products)), p => p.Sku == "OFERTA1");

        // la oferta termina: ya no se vende; la orden hecha no cambia
        await x.Offers.UpdateAsync(offer.PublicId, new OfferRequest(ValidTo: x.Today.AddDays(-1)), default);
        Assert.Equal(OfferKitRules.NotOnSale("OFERTA1"), (await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m)))).Message);
        var still = await x.Sales.GetAsync(made.PublicId, default);
        Assert.Equal(("OFERTA1", 41.94m, 2m), (still.Lines[0].Sku, still.Lines[0].UnitPrice, still.Lines[0].QtyReserved));
        // una oferta permanente (sin fecha de fin) lo vuelve a poner a la venta
        await PublishedOfferAsync(x, "Permanente", x.Today, null, (kit, 45m));
        Assert.Equal(45m, (await x.DraftAsync(x.C2, new SalesOrderLineRequest(kit, 1m))).Lines[0].UnitPrice);
    }

    [Fact]
    public async Task The_same_kit_in_two_offers_sells_at_the_cheaper_one_with_a_notice()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = (await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor), Brand: "PRODIGY"), default)).ProductPublicId;
        await PublishedOfferAsync(x, "Navidad", x.Today, x.Today.AddDays(30), (kit, 41.94m));
        var second = await PublishedOfferAsync(x, "Fin de año", x.Today.AddDays(-1), null, (kit, 39.50m));
        Assert.Empty(second.Warnings);   // la más barata no avisa; la otra sí
        var navidad = (await x.Offers.ListAsync("Navidad", null, false, default)).Single();
        Assert.Equal(PriceNoticeCodes.CheaperOffer, Assert.Single((await x.Offers.GetAsync(navidad.PublicId, default)).Warnings).Code);

        var order = await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m));
        Assert.Equal(39.50m, order.Lines[0].UnitPrice);
        var price = Assert.Single(order.LinePrices!);
        Assert.Equal((PriceSources.Offer, "Fin de año"), (price.PriceSource, price.OfferName));
        var notice = Assert.Single(price.Notices);
        Assert.Equal((PriceNoticeCodes.TwoOffers, "Fin de año", "Navidad"), (notice.Code, notice.OfferName, notice.OtherOfferName));
        var card = Assert.Single((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands).Offers.Single(o => o.Sku == "OFERTA1");
        Assert.Equal((39.50m, "Fin de año", PriceNoticeCodes.TwoOffers), (card.Price!.Value, card.OfferName, Assert.Single(card.Notices!).Code));
    }

    [Fact]
    public async Task Offer_kits_do_not_appear_in_products_and_inventory_but_answer_by_id()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var kit = await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor), Brand: "KITMARCA"), default);
        var products = x.F.Get<ProductService>();
        var page = await products.ListAsync(new ProductListQuery(), InventoryScope.Any, default);
        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { "070120", "52800" }, page.Items.Select(i => i.Sku).ToArray());
        Assert.Equal(0, (await products.ListAsync(new ProductListQuery(Search: "OFERTA1"), InventoryScope.Any, default)).Total);
        Assert.Equal(0, (await products.ListAsync(new ProductListQuery(ProductPublicIds: new[] { kit.ProductPublicId }), InventoryScope.Any, default)).Total);
        Assert.DoesNotContain("KITMARCA", await products.ListBrandsAsync(null, InventoryScope.Any, default));
        // el selector de una orden de venta los puede pedir
        Assert.Contains((await products.ListAsync(new ProductListQuery(IncludeOfferKits: true, OwnOnly: true), InventoryScope.Any, default)).Items, i => i.Sku == "OFERTA1" && i.IsKit);
        // la ficha por id sigue respondiendo (la nombran órdenes y facturas)
        Assert.Equal("Oferta 1", (await products.GetAsync(kit.ProductPublicId, InventoryScope.Any, default)).Product.Name);
    }

    [Fact]
    public async Task Products_no_longer_converts_into_a_kit_and_only_an_offer_kit_changes_pieces_when_ordering()
    {
        var (x, strips, monitor) = await WorldAsync();
        await using var _ = x;
        var normal = await x.ProductAsync("NORMAL", 10m, "PRODIGY");
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Kits.SetFromProductsAsync(normal.PublicId, new KitSetRequest(true, null, Pieces(strips, monitor)), default));
        Assert.Equal("Un producto ya no se convierte en kit desde Productos: un kit que se arma al pedir se crea como kit de oferta en Contabilidad → Precios y ofertas.", ex.Message);

        // un kit viejo (no de oferta): se vende con sus piezas, sin sustituir ni ajustar
        var old = await x.KitAsync("VIEJO", 20m, "PRODIGY", (strips, 2m), (monitor, 1m));
        await x.Kits.SetFromProductsAsync(old.PublicId, new KitSetRequest(true, true, Pieces(strips, monitor, 3m)), default);   // desde Productos se sigue editando
        var ve = await Assert.ThrowsAsync<ValidationException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(old.PublicId, 1m,
            Components: new[] { new KitComponentRequest(strips.PublicId, 4m), new KitComponentRequest(monitor.PublicId, 1m) })));
        Assert.Equal("El kit VIEJO no admite cambiar ni ajustar sus piezas al pedir: eso solo se puede en un kit de oferta.", ve.Message);
        Assert.Equal(20m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(old.PublicId, 1m))).Lines[0].UnitPrice);
        var subs = await x.Sales.SubstitutesAsync(old.PublicId, default);
        Assert.Equal((false, false), (subs.IsOfferKit, subs.Adjustable));
        Assert.All(subs.Components, c => Assert.Empty(c.Substitutes));

        // el kit de oferta sí admite ajustar (si es ajustable)
        var kit = (await x.Offers.CreateLibraryKitAsync(new OfferKitLibraryRequest("OFERTA1", "Oferta 1", Pieces(strips, monitor), Adjustable: true), default)).ProductPublicId;
        await PublishedOfferAsync(x, "Permanente", x.Today, null, (kit, 41.94m));
        var order = await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit, 1m, Components: new[] { new KitComponentRequest(strips.PublicId, 8m), new KitComponentRequest(monitor.PublicId, 1m) }));
        Assert.Equal(41.94m, order.Lines[0].UnitPrice);
        Assert.True((await x.Sales.SubstitutesAsync(kit, default)).IsOfferKit);
    }
}
