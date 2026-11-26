using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (P2) — lo que las pruebas del lote arman directo en la base (en memoria) para no depender de los servicios de P3/P4: cadenas, clientes en una cadena,
/// listas de cadena, precios especiales (listas dedicadas de cliente o de cadena) y ofertas con público ALL / INCLUDE / EXCLUDE.
/// </summary>
internal static class ChainPriceSetup
{
    public static async Task<ClientChain> ChainAsync(this PriceWorld x, string name, bool isActive = true)
    {
        var c = new ClientChain { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Name = name, IsActive = isActive, CreatedAtUtc = DateTime.UtcNow };
        x.F.Db.Set<ClientChain>().Add(c);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return c;
    }

    /// <summary>Un cliente nuevo de la compañía, en la cadena indicada (null = sin cadena).</summary>
    public static async Task<Client> ClientInAsync(this PriceWorld x, string code, ClientChain? chain)
    {
        var c = await x.F.AddClientAsync(code);
        if (chain is not null) await x.MoveToChainAsync(c, chain);
        return c;
    }

    public static async Task MoveToChainAsync(this PriceWorld x, Client client, ClientChain? chain)
    {
        var row = await x.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == client.ClientId);
        row.ChainId = chain?.ClientChainId;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    public static async Task SetChainActiveAsync(this PriceWorld x, ClientChain chain, bool isActive)
    {
        var row = await x.F.Db.Set<ClientChain>().AsTracking().SingleAsync(c => c.ClientChainId == chain.ClientChainId);
        row.IsActive = isActive;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    /// <summary>Lista publicada de una cadena: normal (special = false) o el precio especial (lista dedicada) de la cadena.</summary>
    public static Task<PriceList> ChainListAsync(this PriceWorld x, string name, DateOnly from, DateOnly? to, ClientChain chain, bool special, params (Product Product, decimal Price)[] items)
        => x.RawListAsync(name, from, to, items, l =>
        {
            if (special) l.SpecialChainId = chain.ClientChainId;
            l.Chains.Add(new PriceListChain { TenantId = WmsFixture.TenantId, ChainId = chain.ClientChainId, CreatedAtUtc = DateTime.UtcNow });
        });

    /// <summary>El precio especial (lista dedicada publicada) de un cliente, con su fila PriceListClient.</summary>
    public static Task<PriceList> ClientSpecialAsync(this PriceWorld x, Client client, DateOnly from, DateOnly? to, params (Product Product, decimal Price)[] items)
        => x.RawListAsync($"Precio especial — {client.Name}", from, to, items, l =>
        {
            l.SpecialClientId = client.ClientId;
            l.Clients.Add(new PriceListClient { TenantId = WmsFixture.TenantId, ClientId = client.ClientId, CreatedAtUtc = DateTime.UtcNow });
        });

    private static async Task<PriceList> RawListAsync(this PriceWorld x, string name, DateOnly from, DateOnly? to, (Product Product, decimal Price)[] items, Action<PriceList> audience)
    {
        var l = new PriceList
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Name = name, ValidFrom = from, ValidTo = to, IsPublished = true, AppliesToAll = false,
            CreatedAtUtc = DateTime.UtcNow,
        };
        foreach (var (p, price) in items) l.Items.Add(new PriceListItem { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, Price = price, CreatedAtUtc = DateTime.UtcNow });
        audience(l);
        x.F.Db.Set<PriceList>().Add(l);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return l;
    }

    /// <summary>Oferta publicada y vigente con su público: los clientes y cadenas son los incluidos (INCLUDE) o los excluidos (EXCLUDE).</summary>
    public static async Task<Offer> OfferAsync(this PriceWorld x, string name, string mode, IReadOnlyList<Client>? clients, IReadOnlyList<ClientChain>? chains,
        params (Product Product, decimal Price)[] items)
    {
        var o = new Offer { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Name = name, ValidFrom = x.Today.AddDays(-1), IsPublished = true, CreatedAtUtc = DateTime.UtcNow };
        o.SetAudienceMode(mode);
        foreach (var (p, price) in items) o.Items.Add(new OfferItem { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, Price = price, CreatedAtUtc = DateTime.UtcNow });
        foreach (var c in clients ?? Array.Empty<Client>()) o.Clients.Add(new OfferClient { TenantId = WmsFixture.TenantId, ClientId = c.ClientId, CreatedAtUtc = DateTime.UtcNow });
        foreach (var c in chains ?? Array.Empty<ClientChain>()) o.Chains.Add(new OfferChain { TenantId = WmsFixture.TenantId, ChainId = c.ClientChainId, CreatedAtUtc = DateTime.UtcNow });
        x.F.Db.Set<Offer>().Add(o);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return o;
    }

    /// <summary>El renglón del catálogo (lista de la marca) de un producto para el cliente.</summary>
    public static async Task<SalesCatalogItemDto> CatalogItemAsync(this PriceWorld x, Client? client, Product p)
        => (await x.Catalog.CatalogAsync(client?.PublicId, default)).Brands.SelectMany(b => b.Products).Single(i => i.ProductPublicId == p.PublicId);
}

// Lote 247 (P2): la cadena entra al ÚNICO punto de resolución del precio (PriceListQueries): catálogo, asistente y orden cobran el MÁS BAJO entre el precio del
// cliente, el de su cadena (activa), las ofertas que le aplican (incluir / excluir, también por cadena) y la lista general.
public sealed class ChainPriceResolutionTests
{
    [Fact]
    public async Task Catalog_for_a_chain_client_takes_the_chain_special_price_and_names_the_chain()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", walgreens);
        var strips = await x.ProductAsync("52800", 12m, "PRODIGY");
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (strips, 10m));
        var special = await x.ChainListAsync("Precio especial — Walgreens", x.Today.AddDays(-1), null, walgreens, special: true, (strips, 7m));

        var item = await x.CatalogItemAsync(member, strips);
        Assert.Equal((7m, PriceSources.ChainList), (item.Price, item.PriceSource));
        Assert.Equal((special.PublicId, special.Name), (item.PriceListPublicId!.Value, item.PriceListName));
        Assert.Equal((walgreens.PublicId, "Walgreens"), (item.ChainPublicId!.Value, item.ChainName));
        Assert.Null(item.Notices);

        // quien no es de la cadena (y el catálogo sin cliente) sigue con la lista general y sin cadena
        var outsider = await x.CatalogItemAsync(x.C1, strips);
        Assert.Equal((10m, PriceSources.GeneralList, (Guid?)null, (string?)null), (outsider.Price, outsider.PriceSource, outsider.ChainPublicId, outsider.ChainName));
        Assert.Equal((10m, PriceSources.GeneralList), ((await x.CatalogItemAsync(null, strips)).Price, (await x.CatalogItemAsync(null, strips)).PriceSource));
    }

    [Fact]
    public async Task Order_line_takes_the_lowest_between_client_and_chain_and_line_prices_show_the_origin()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m), (b, 10m));
        var chainList = await x.ChainListAsync("Walgreens", x.Today.AddDays(-1), null, walgreens, special: true, (a, 7m), (b, 7m));
        var clientList = await x.ClientSpecialAsync(member, x.Today.AddDays(-1), null, (a, 8m), (b, 6m));

        var o = await x.DraftAsync(member, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m));
        Assert.Equal(new[] { 7m, 6m }, o.Lines.Select(l => l.UnitPrice).ToArray());   // A: la cadena (7 < 8); B: el cliente (6 < 7)
        var pa = o.LinePrices!.Single(p => p.ProductPublicId == a.PublicId);
        Assert.Equal((PriceSources.ChainList, chainList.PublicId, walgreens.PublicId, "Walgreens"), (pa.PriceSource, pa.PriceListPublicId!.Value, pa.ChainPublicId!.Value, pa.ChainName));
        var pb = o.LinePrices!.Single(p => p.ProductPublicId == b.PublicId);
        Assert.Equal((PriceSources.ClientList, clientList.PublicId, (Guid?)null, (string?)null), (pb.PriceSource, pb.PriceListPublicId!.Value, pb.ChainPublicId, pb.ChainName));
        // la línea guardada dice lo mismo
        Assert.Equal((PriceSources.ChainList, walgreens.PublicId, "Walgreens"), (o.Lines[0].PriceSource, o.Lines[0].PriceChainPublicId!.Value, o.Lines[0].PriceChainName));
        Assert.Equal((PriceSources.ClientList, clientList.Name), (o.Lines[1].PriceSource, o.Lines[1].PriceListName));
    }

    [Fact]
    public async Task Client_special_price_above_general_is_charged_the_general_and_the_draft_brings_the_notice()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A-10", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 9m));
        var special = await x.ClientSpecialAsync(x.C1, x.Today.AddDays(-1), null, (a, 10.5m));

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m));
        Assert.Equal(9m, o.Lines[0].UnitPrice);
        var lp = Assert.Single(o.LinePrices!);
        Assert.Equal(PriceSources.GeneralList, lp.PriceSource);
        var n = Assert.Single(lp.Notices);
        Assert.Equal(PriceNoticeCodes.SpecialAboveGeneral, n.Code);
        Assert.Equal($"El precio especial «{special.Name}» pone A-10 a $10.50, más caro que la lista general ($9.00): se aplica el de la lista general. Corrija el precio especial.", n.Message);
        Assert.Equal((special.PublicId, special.Name, (Guid?)null), (n.PriceListPublicId!.Value, n.PriceListName, n.OfferPublicId));
        // datos estructurados para que la web arme el texto en su idioma: precios, lista dedicada con su dueño y fechas
        Assert.Equal((10.5m, 9m, true, x.C1.Name, special.ValidFrom, special.ValidTo), (n.Price, n.OtherPrice, n.PriceListIsSpecial, n.PriceListOwnerName, n.PriceListValidFrom, n.PriceListValidTo));
        // el catálogo trae el mismo aviso
        var item = await x.CatalogItemAsync(x.C1, a);
        Assert.Equal((9m, PriceSources.GeneralList), (item.Price, item.PriceSource));
        Assert.Equal(special.PublicId, Assert.Single(item.Notices!).PriceListPublicId);
    }

    [Fact]
    public async Task Special_list_and_normal_group_list_for_same_client_coexist_and_the_lowest_wins()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        var group = await x.PublishedListAsync("Grupo farmacias", x.Today.AddDays(-3), null, new[] { x.C1, x.C2 }, (a, 8m), (b, 6m));
        var special = await x.ClientSpecialAsync(x.C1, x.Today.AddDays(-1), null, (a, 7.5m), (b, 7m));

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m));
        Assert.Equal(new[] { 7.5m, 6m }, o.Lines.Select(l => l.UnitPrice).ToArray());
        Assert.Equal(new[] { special.Name, group.Name }, o.Lines.Select(l => l.PriceListName).ToArray());
        Assert.All(o.Lines, l => Assert.Equal(PriceSources.ClientList, l.PriceSource));
        // el otro cliente del grupo no recibe el precio especial de C1
        Assert.Equal(8m, (await x.DraftAsync(x.C2, new SalesOrderLineRequest(a.PublicId, 1m))).Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Inactive_chain_or_client_without_chain_gets_no_chain_price()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var member = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m));
        await x.ChainListAsync("Walgreens normal", x.Today.AddDays(-1), null, walgreens, special: false, (a, 7m));

        Assert.Equal(7m, (await x.DraftAsync(member, new SalesOrderLineRequest(a.PublicId, 1m))).Lines[0].UnitPrice);
        Assert.Equal(10m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m))).Lines[0].UnitPrice);   // sin cadena

        await x.SetChainActiveAsync(walgreens, false);   // cadena inactiva = sin cadena
        var o = await x.DraftAsync(member, new SalesOrderLineRequest(a.PublicId, 1m));
        Assert.Equal((10m, PriceSources.GeneralList, (Guid?)null), (o.Lines[0].UnitPrice, o.Lines[0].PriceSource, o.Lines[0].PriceChainPublicId));
        var item = await x.CatalogItemAsync(member, a);
        Assert.Equal((10m, PriceSources.GeneralList, (string?)null), (item.Price, item.PriceSource, item.ChainName));
    }

    [Fact]
    public async Task Exclude_offer_skips_the_excluded_client_and_chain_members_and_include_by_chain_reaches_members()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var cvs = await x.ChainAsync("CVS");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var cv1 = await x.ClientInAsync("CV1", cvs);
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m), (b, 10m));
        var all = await x.OfferAsync("Todos menos Walgreens y C2", OfferAudienceModes.Exclude, new[] { x.C2 }, new[] { walgreens }, (a, 5m));
        var onlyCvs = await x.OfferAsync("Solo CVS", OfferAudienceModes.Include, null, new[] { cvs }, (b, 4m));

        async Task<(decimal A, decimal B)> PricesOf(Client c)
        {
            var o = await x.DraftAsync(c, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m));
            return (o.Lines[0].UnitPrice, o.Lines[1].UnitPrice);
        }
        Assert.Equal((5m, 10m), await PricesOf(x.C1));    // no excluido, no es de CVS
        Assert.Equal((10m, 10m), await PricesOf(x.C2));   // excluido por nombre
        Assert.Equal((10m, 10m), await PricesOf(w1));     // excluido por su cadena
        Assert.Equal((5m, 4m), await PricesOf(cv1));      // la de exclusión y la de CVS
        var o2 = await x.DraftAsync(cv1, new SalesOrderLineRequest(b.PublicId, 1m));
        Assert.Equal((PriceSources.Offer, onlyCvs.PublicId, onlyCvs.Name), (o2.Lines[0].PriceSource, o2.Lines[0].OfferPublicId!.Value, o2.Lines[0].OfferName));
        // catálogo sin cliente: la de exclusión vale para todos; la de solo CVS no
        Assert.Equal(5m, (await x.CatalogItemAsync(null, a)).Price);
        Assert.Equal(10m, (await x.CatalogItemAsync(null, b)).Price);
        Assert.Equal(all.PublicId, (await x.CatalogItemAsync(x.C1, a)).OfferPublicId);
    }

    [Fact]
    public async Task Offer_kit_included_by_chain_is_sellable_only_to_members()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var strips = await x.ProductAsync("52800", 7.50m, "PRODIGY", stock: 50m);
        var offer = await x.Offers.CreateAsync(new OfferRequest("Walgreens kit", x.Today.AddDays(-1), x.Today.AddDays(30)), default);
        var kit = await x.Offers.CreateKitAsync(offer.PublicId, new OfferKitRequest("KIT-W", "Kit Walgreens", 30m, new[] { new KitComponentRequest(strips.PublicId, 5m) }, Brand: "PRODIGY"), default);
        // la oferta pasa a «solo los escogidos» con la cadena (directo en la base: el editor de público es de P3) y se publica
        var row = await x.F.Db.Set<Offer>().AsTracking().SingleAsync(o => o.PublicId == offer.PublicId);
        row.SetAudienceMode(OfferAudienceModes.Include);
        row.IsPublished = true;
        x.F.Db.Set<OfferChain>().Add(new OfferChain { TenantId = WmsFixture.TenantId, OfferId = row.OfferId, ChainId = walgreens.ClientChainId, CreatedAtUtc = DateTime.UtcNow });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var ok = await x.DraftAsync(w1, new SalesOrderLineRequest(kit.ProductPublicId, 1m));
        Assert.Equal((30m, PriceSources.Offer), (ok.Lines[0].UnitPrice, ok.Lines[0].PriceSource));
        Assert.Contains((await x.Catalog.CatalogAsync(w1.PublicId, default)).Brands.SelectMany(b => b.Offers), i => i.ProductPublicId == kit.ProductPublicId);

        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => x.DraftAsync(x.C1, new SalesOrderLineRequest(kit.ProductPublicId, 1m)));
        Assert.Equal(OfferKitRules.NotOnSale("KIT-W"), ex.Message);
        Assert.DoesNotContain((await x.Catalog.CatalogAsync(x.C1.PublicId, default)).Brands.SelectMany(b => b.Offers), i => i.ProductPublicId == kit.ProductPublicId);
    }

    [Fact]
    public async Task Changing_the_client_of_a_draft_reprices_with_the_new_clients_chain()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m), (b, 10m));
        var chainList = await x.ChainListAsync("Walgreens", x.Today.AddDays(-1), null, walgreens, special: true, (a, 7m), (b, 7m));

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m, UnitPrice: 15m));   // B: excepción con sales.price
        Assert.Equal(new[] { 10m, 15m }, o.Lines.Select(l => l.UnitPrice).ToArray());
        var moved = await x.Sales.UpdateAsync(o.PublicId, new SalesOrderRequest(ClientPublicId: w1.PublicId), default);
        Assert.Equal(new[] { 7m, 15m }, moved.Lines.Select(l => l.UnitPrice).ToArray());   // A toma el de la cadena; la excepción de B se queda
        Assert.All(moved.Lines, l => Assert.Equal((PriceSources.ChainList, chainList.PublicId, walgreens.PublicId, 7m), (l.PriceSource!, l.PriceListPublicId!.Value, l.PriceChainPublicId!.Value, l.ResolvedUnitPrice!.Value)));
        Assert.Equal(new[] { false, true }, moved.Lines.Select(l => l.IsManualPrice).ToArray());
    }

    [Fact]
    public async Task Expired_special_price_returns_to_the_normal_price()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-30), null, null, (a, 10m));
        await x.ChainListAsync("Walgreens vencida", x.Today.AddDays(-30), x.Today.AddDays(-1), walgreens, special: true, (a, 7m));
        await x.ClientSpecialAsync(w1, x.Today.AddDays(-30), x.Today.AddDays(-1), (a, 6m));
        await x.ClientSpecialAsync(x.C1, x.Today.AddDays(1), null, (a, 5m));   // todavía no empieza

        var o = await x.DraftAsync(w1, new SalesOrderLineRequest(a.PublicId, 1m));
        Assert.Equal((10m, PriceSources.GeneralList), (o.Lines[0].UnitPrice, o.Lines[0].PriceSource));
        Assert.Equal(10m, (await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m))).Lines[0].UnitPrice);
        // y sin lista general, el precio de venta del producto
        await using var y = await PriceWorld.CreateAsync();
        var p = await y.ProductAsync("P", 12m);
        await y.ClientSpecialAsync(y.C1, y.Today.AddDays(-30), y.Today.AddDays(-1), (p, 6m));
        var o2 = await y.DraftAsync(y.C1, new SalesOrderLineRequest(p.PublicId, 1m));
        Assert.Equal((12m, PriceSources.Product), (o2.Lines[0].UnitPrice, o2.Lines[0].PriceSource));
    }
}
