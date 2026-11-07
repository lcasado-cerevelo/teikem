using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 221 (P1 del pedido de vendedores): ofertas = kits con vigencia y foto. Solo un kit puede ser oferta; fuera de vigencia no se vende; las órdenes ya hechas no
// cambian; el catálogo del pedido las muestra arriba de su marca con el precio resuelto.
public sealed class KitPromoTests
{
    private static readonly DateOnly D1 = new(2026, 11, 1);

    [Fact]
    public void Only_a_promotion_has_dates_and_both_ends_are_optional_and_included()
    {
        Assert.True(KitPromoRules.IsSellable(false, D1.AddDays(1), D1.AddDays(2), D1));   // no es oferta: siempre
        Assert.True(KitPromoRules.IsSellable(true, null, null, D1));
        Assert.True(KitPromoRules.IsSellable(true, D1, D1, D1));
        Assert.False(KitPromoRules.IsSellable(true, D1.AddDays(1), null, D1));
        Assert.False(KitPromoRules.IsSellable(true, null, D1.AddDays(-1), D1));
        Assert.Null(KitPromoRules.DatesError(D1, null));
        Assert.Equal(KitPromoRules.DateRange, KitPromoRules.DatesError(D1, D1.AddDays(-1)));
    }

    [Fact]
    public void The_out_of_date_message_tells_the_dates()
    {
        Assert.Equal("La oferta OFERTA1 no está vigente hoy (vale del 01/11/2026 al 30/11/2026): no se puede vender.", KitPromoRules.NotInEffect("OFERTA1", D1, new DateOnly(2026, 11, 30)));
        Assert.Equal("La oferta OFERTA1 no está vigente hoy (vale desde el 01/11/2026): no se puede vender.", KitPromoRules.NotInEffect("OFERTA1", D1, null));
        Assert.Equal("La oferta OFERTA1 no está vigente hoy (vale hasta el 01/11/2026): no se puede vender.", KitPromoRules.NotInEffect("OFERTA1", null, D1));
    }

    [Fact]
    public async Task Only_a_kit_can_be_a_promotion_and_a_kit_turned_back_to_a_product_stops_being_one()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY");
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.Catalog.SetPromoAsync(strips.PublicId, new ProductPromoRequest(true), default));
        Assert.Equal(KitPromoRules.OnlyKits, ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => x.Catalog.SetPromoAsync(strips.PublicId, new ProductPromoRequest(true, x.Today, x.Today.AddDays(-1)), default));

        var kit = await x.KitAsync("OFERTA1", 41.94m, "PRODIGY", (strips, 6m), (monitor, 1m));
        var promo = await x.Catalog.SetPromoAsync(kit.PublicId, new ProductPromoRequest(true, x.Today, x.Today.AddDays(30)), default);
        Assert.True(promo.IsPromotion && promo.IsKit && promo.InEffect);
        Assert.Single(await x.Catalog.ListOffersAsync(false, default));

        await x.Kits.SetAsync(kit.PublicId, new KitSetRequest(false, null, null), default);   // ningún pedido lo usó: vuelve a ser producto normal
        Assert.False((await x.Catalog.GetPromoAsync(kit.PublicId, default)).IsPromotion);
    }

    [Fact]
    public async Task An_offer_out_of_its_dates_is_not_sold_but_orders_already_made_do_not_change()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY", stock: 100m);
        var monitor = await x.ProductAsync("070120", 7.99m, "PRODIGY", stock: 100m);
        var kit = await x.KitAsync("OFERTA1", 49.93m, "PRODIGY", (strips, 6m), (monitor, 1m));
        await x.Catalog.SetPromoAsync(kit.PublicId, new ProductPromoRequest(true, x.Today.AddDays(-5), x.Today.AddDays(5)), default);
        await x.PublishedListAsync("Ofertas", x.Today.AddDays(-5), null, null, (kit, 41.94m));   // el kit en la lista: su precio de oferta

        var made = await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit.PublicId, 2m));
        made = await x.Sales.RequestAsync(made.PublicId, null, default);
        Assert.Equal((41.94m, 83.88m), (made.Lines[0].UnitPrice, made.Subtotal));

        // la oferta vence
        await x.Catalog.SetPromoAsync(kit.PublicId, new ProductPromoRequest(true, x.Today.AddDays(-5), x.Today.AddDays(-1)), default);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit.PublicId, 1m)));
        Assert.Equal(KitPromoRules.NotInEffect("OFERTA1", x.Today.AddDays(-5), x.Today.AddDays(-1)), ex.Message);
        var still = await x.Sales.GetAsync(made.PublicId, default);
        Assert.Equal((41.94m, 2m, 2m), (still.Lines[0].UnitPrice, still.Lines[0].QtyOrdered, still.Lines[0].QtyReserved));
        Assert.Equal(new[] { 6m, 1m }, still.Lines[0].Components!.Select(c => c.QtyPerKit).ToArray());

        // y se puede vender de nuevo si se le alarga la vigencia
        await x.Catalog.SetPromoAsync(kit.PublicId, new ProductPromoRequest(true, null, null), default);
        Assert.Equal(41.94m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(kit.PublicId, 1m))).Lines[0].UnitPrice);
    }

    [Fact]
    public async Task The_order_catalog_groups_by_brand_with_offers_on_top_and_client_prices()
    {
        await using var x = await PriceWorld.CreateAsync();
        var strips = await x.ProductAsync("52800", 6.99m, "PRODIGY");
        var monitor = await x.ProductAsync("070120", 7.99m, "Prodigy");
        var gloves = await x.ProductAsync("NECH-1001", 9m, "PADIMONT");
        var loose = await x.ProductAsync("SUELTO", null);
        await x.F.AddProductAsync("3PL", ownerClientId: x.C1.ClientId);   // de un cliente 3PL: no se vende
        await x.ProductAsync("VIEJO", 1m, "PRODIGY");
        var old = await x.F.Db.Set<Product>().AsTracking().SingleAsync(p => p.Sku == "VIEJO");
        old.IsActive = false;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var offer = await x.KitAsync("OFERTA1", 49.93m, "PRODIGY", (strips, 6m), (monitor, 1m));
        await x.Catalog.SetPromoAsync(offer.PublicId, new ProductPromoRequest(true, x.Today, x.Today.AddDays(30)), default);
        var expired = await x.KitAsync("OFERTA0", 40m, "PRODIGY", (strips, 5m));
        await x.Catalog.SetPromoAsync(expired.PublicId, new ProductPromoRequest(true, x.Today.AddDays(-30), x.Today.AddDays(-1)), default);
        var general = await x.PublishedListAsync("General", x.Today, null, null, (offer, 41.94m), (strips, 6.50m));
        await x.PublishedListAsync("Guantes C1", x.Today, null, new[] { x.C1 }, (gloves, 7m));

        var cat = await x.Catalog.CatalogAsync(x.C1.PublicId, default);
        Assert.Equal(new[] { "PADIMONT", "PRODIGY", null }, cat.Brands.Select(b => b.Brand).ToArray());   // los sin marca al final
        var prodigy = cat.Brands[1];
        var o = Assert.Single(prodigy.Offers);   // la vencida no sale
        Assert.Equal(("OFERTA1", 41.94m, PriceSources.GeneralList, general.PublicId, true), (o.Sku, o.Price!.Value, o.PriceSource, o.PriceListPublicId!.Value, o.IsKit));
        Assert.Equal(new[] { ("070120", 7.99m, PriceSources.Product), ("52800", 6.50m, PriceSources.GeneralList) },
            prodigy.Products.Select(p => (p.Sku, p.Price!.Value, p.PriceSource)).ToArray());   // sin el inactivo ni la oferta
        Assert.Equal((7m, PriceSources.ClientList), (cat.Brands[0].Products[0].Price!.Value, cat.Brands[0].Products[0].PriceSource));
        Assert.Null(cat.Brands[2].Products[0].Price);
        Assert.Equal(PriceSources.None, cat.Brands[2].Products[0].PriceSource);
        Assert.DoesNotContain(cat.Brands.SelectMany(b => b.Products), p => p.Sku == "3PL");

        var other = await x.Catalog.CatalogAsync(x.C2.PublicId, default);   // otro cliente: los guantes al precio del producto
        Assert.Equal((9m, PriceSources.Product), (other.Brands[0].Products[0].Price!.Value, other.Brands[0].Products[0].PriceSource));
        Assert.Equal(loose.PublicId, other.Brands[2].Products[0].ProductPublicId);
    }
}
