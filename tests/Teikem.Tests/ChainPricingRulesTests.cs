using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P1) — reglas puras de los precios especiales y de las ofertas por cliente y por cadena: gana el más bajo sin jerarquía (cliente, cadena, oferta,
/// general), el aviso del precio especial más caro que la general, el público de las ofertas (incluir / excluir con cadenas), los cruces de listas y ofertas, el nombre
/// de la lista dedicada, la validación de un precio especial y la copia del origen del precio en la línea.
/// </summary>
public sealed class ChainPricingRulesTests
{
    private static readonly DateOnly D1 = new(2026, 12, 1);
    private const int Client = 100;
    private const int Chain = 70;

    private static PriceListView L(int id, (int Product, decimal Price)[] items, int[]? clients = null, int[]? chains = null, bool all = false,
        DateOnly? from = null, bool special = false, string? name = null)
        => new(id, name ?? $"Lista {id}", true, all, from ?? D1, null, clients ?? Array.Empty<int>(), items.Select(i => new PriceListItemView(i.Product, i.Price)).ToList(),
            chains, special);

    private static PriceListView General(int id, decimal price, int product = 10) => L(id, new[] { (product, price) }, all: true, name: "General");

    private static OfferView O(int id, (int Product, decimal Price)[] items, string mode = OfferAudienceModes.All, int[]? clients = null, int[]? chains = null)
        => new(id, $"Oferta {id}", true, true, mode == OfferAudienceModes.All, D1, null, clients ?? Array.Empty<int>(),
            items.Select(i => new OfferItemView(i.Product, i.Price)).ToList(), mode, chains);

    // ---------------------------------------------------------------- quién gana

    [Fact]
    public void Lowest_wins_among_client_chain_offer_and_general_without_hierarchy()
    {
        var lists = new[] { L(1, new[] { (10, 8m) }, clients: new[] { Client }), L(2, new[] { (10, 7m) }, chains: new[] { Chain }), General(4, 9m) };
        var offers = new[] { O(3, new[] { (10, 7.50m) }) };
        var r = OfferRules.Resolve(10, Client, D1, lists, offers, 12m, Chain);
        Assert.Equal((7m, PriceSources.ChainList, (int?)2, (int?)null), (r.Price!.Value, r.Source, r.PriceListId, r.OfferId));
        // la misma respuesta solo con listas
        Assert.Equal(new ResolvedPrice(7m, PriceSources.ChainList, 2), PriceListRules.Resolve(10, Client, D1, lists, 12m, Chain));
        // la oferta, si es la más baja
        r = OfferRules.Resolve(10, Client, D1, lists, new[] { O(3, new[] { (10, 6.50m) }) }, 12m, Chain);
        Assert.Equal((6.50m, PriceSources.Offer, (int?)3), (r.Price!.Value, r.Source, r.OfferId));
    }

    [Fact]
    public void A_special_price_above_the_general_list_loses_and_warns()
    {
        var clientList = L(1, new[] { (10, 10m) }, clients: new[] { Client }, special: true, name: "Precio especial — Farmacia Uno");
        var r = OfferRules.Resolve(10, Client, D1, new[] { clientList, General(4, 9m) }, Array.Empty<OfferView>(), 12m, Chain);
        Assert.Equal((9m, PriceSources.GeneralList, (int?)4), (r.Price!.Value, r.Source, r.PriceListId));
        var n = Assert.Single(r.Notices!);
        Assert.Equal((PriceNoticeCodes.SpecialAboveGeneral, (int?)1, "Precio especial — Farmacia Uno", 10m, (decimal?)9m),
            (n.Code, n.PriceListId, n.PriceListName, n.OfferPrice, n.OtherPrice));
        Assert.Equal("El precio especial «Precio especial — Farmacia Uno» pone SKU-1 a $10.00, más caro que la lista general ($9.00): se aplica el de la lista general. Corrija el precio especial.",
            OfferRules.NoticeMessage("SKU-1", n));

        // lo mismo con el de la cadena
        var chainList = L(2, new[] { (10, 9.25m) }, chains: new[] { Chain }, special: true, name: "Precio especial — Walgreens");
        r = OfferRules.Resolve(10, Client, D1, new[] { chainList, General(4, 9m) }, Array.Empty<OfferView>(), 12m, Chain);
        Assert.Equal((9m, PriceSources.GeneralList), (r.Price!.Value, r.Source));
        n = Assert.Single(r.Notices!);
        Assert.Equal((PriceNoticeCodes.SpecialAboveGeneral, (int?)2, 9.25m), (n.Code, n.PriceListId, n.OfferPrice));

        // los dos niveles más caros: un aviso por nivel
        r = OfferRules.Resolve(10, Client, D1, new[] { clientList, chainList, General(4, 9m) }, Array.Empty<OfferView>(), 12m, Chain);
        Assert.Equal(new int?[] { 1, 2 }, r.Notices!.Select(x => x.PriceListId).ToArray());

        Assert.Equal("Precio especial más caro que la lista general: SKU-1 a $10.00 y en la lista general a $9.00. Se cobra el más bajo; corrija el precio especial.",
            PriceListRules.SpecialAboveGeneral("SKU-1", 10m, 9m));
    }

    [Fact]
    public void Ties_prefer_client_then_chain_then_offer_then_general()
    {
        var client = L(1, new[] { (10, 7m) }, clients: new[] { Client });
        var chain = L(2, new[] { (10, 7m) }, chains: new[] { Chain });
        var general = General(4, 7m);
        var offer = new[] { O(3, new[] { (10, 7m) }) };
        Assert.Equal(PriceSources.ClientList, OfferRules.Resolve(10, Client, D1, new[] { general, chain, client }, offer, 9m, Chain).Source);
        Assert.Equal(PriceSources.ChainList, OfferRules.Resolve(10, Client, D1, new[] { general, chain }, offer, 9m, Chain).Source);
        Assert.Equal(PriceSources.Offer, OfferRules.Resolve(10, Client, D1, new[] { general }, offer, 9m, Chain).Source);
        Assert.Equal(PriceSources.GeneralList, OfferRules.Resolve(10, Client, D1, new[] { general }, Array.Empty<OfferView>(), 9m, Chain).Source);
        Assert.Equal(PriceSources.ClientList, PriceListRules.Resolve(10, Client, D1, new[] { general, chain, client }, 9m, Chain).Source);
        Assert.Equal(PriceSources.ChainList, PriceListRules.Resolve(10, Client, D1, new[] { general, chain }, 9m, Chain).Source);
    }

    [Fact]
    public void Within_a_level_the_lowest_wins_and_ties_go_to_the_latest()
    {
        // la dedicada de la cadena (6) y una lista normal con la cadena (5): conviven y gana la más baja
        var dedicated = L(1, new[] { (10, 6m) }, chains: new[] { Chain }, from: D1.AddDays(-10), special: true);
        var normal = L(2, new[] { (10, 5m) }, chains: new[] { Chain }, from: D1.AddDays(-20));
        var levels = PriceListRules.BestByLevel(10, Client, Chain, D1, new[] { dedicated, normal });
        Assert.Equal(2, levels.Chain!.List.PriceListId);
        Assert.Null(levels.Client);
        Assert.Null(levels.General);
        // a igual precio: la que empezó más tarde; a igual fecha, la más nueva
        var later = L(3, new[] { (10, 5m) }, chains: new[] { Chain }, from: D1.AddDays(-5));
        Assert.Equal(3, PriceListRules.BestByLevel(10, Client, Chain, D1, new[] { normal, later }).Chain!.List.PriceListId);
        var sameDay = L(4, new[] { (10, 5m) }, chains: new[] { Chain }, from: D1.AddDays(-5));
        Assert.Equal(4, PriceListRules.BestByLevel(10, Client, Chain, D1, new[] { sameDay, normal, later }).Chain!.List.PriceListId);
        // lo mismo en el nivel del cliente
        var c1 = L(5, new[] { (10, 4m) }, clients: new[] { Client }, special: true);
        var c2 = L(6, new[] { (10, 3m) }, clients: new[] { Client });
        Assert.Equal(new ResolvedPrice(3m, PriceSources.ClientList, 6), PriceListRules.Resolve(10, Client, D1, new[] { c1, c2 }, null));
    }

    [Fact]
    public void Without_general_list_a_pricier_special_price_still_beats_the_product_price()
    {
        var special = L(1, new[] { (10, 12m) }, clients: new[] { Client }, special: true);
        var r = OfferRules.Resolve(10, Client, D1, new[] { special }, Array.Empty<OfferView>(), 10m, Chain);
        Assert.Equal((12m, PriceSources.ClientList), (r.Price!.Value, r.Source));
        Assert.Null(r.Notices);
        var chainOnly = L(2, new[] { (10, 11m) }, chains: new[] { Chain }, special: true);
        Assert.Equal(new ResolvedPrice(11m, PriceSources.ChainList, 2), PriceListRules.Resolve(10, Client, D1, new[] { chainOnly }, 10m, Chain));
    }

    [Fact]
    public void Chain_lists_apply_only_with_that_chain_and_null_chain_means_none()
    {
        var chain = L(1, new[] { (10, 5m) }, chains: new[] { Chain });
        var lists = new[] { chain, General(2, 8m) };
        Assert.Equal(PriceSources.ChainList, PriceListRules.Resolve(10, Client, D1, lists, 9m, Chain).Source);
        Assert.Equal(PriceSources.GeneralList, PriceListRules.Resolve(10, Client, D1, lists, 9m, Chain + 1).Source);   // otra cadena
        Assert.Equal(PriceSources.GeneralList, PriceListRules.Resolve(10, Client, D1, lists, 9m).Source);               // sin cadena (o inactiva)
        Assert.Equal(new ResolvedPrice(9m, PriceSources.Product), PriceListRules.Resolve(10, Client, D1, new[] { chain }, 9m));
        // sin cliente pero con cadena (no ocurre al cotizar, pero la regla es la misma): la de la cadena
        Assert.Equal(PriceSources.ChainList, PriceListRules.Resolve(10, null, D1, lists, 9m, Chain).Source);
        // una lista de clientes que no incluye al cliente no aplica por la cadena
        Assert.Equal(PriceSources.Product, PriceListRules.Resolve(10, Client, D1, new[] { L(3, new[] { (10, 1m) }, clients: new[] { 101 }) }, 9m, Chain).Source);
    }

    // ---------------------------------------------------------------- público de las ofertas

    [Fact]
    public void Offer_audience_matrix()
    {
        var all = O(1, new[] { (10, 1m) });
        Assert.True(OfferRules.AppliesTo(all, Client, Chain));
        Assert.True(OfferRules.AppliesTo(all, null));

        var include = O(2, new[] { (10, 1m) }, OfferAudienceModes.Include, new[] { Client }, new[] { Chain });
        Assert.True(OfferRules.AppliesTo(include, Client));          // por el cliente
        Assert.True(OfferRules.AppliesTo(include, 101, Chain));      // por su cadena
        Assert.False(OfferRules.AppliesTo(include, 101, 71));
        Assert.False(OfferRules.AppliesTo(include, 101));
        Assert.True(OfferRules.AppliesTo(include, null, Chain));     // sin cliente: solo si trae la cadena
        Assert.False(OfferRules.AppliesTo(include, null));

        var exclude = O(3, new[] { (10, 1m) }, OfferAudienceModes.Exclude, new[] { Client }, new[] { Chain });
        Assert.False(OfferRules.AppliesTo(exclude, Client, 71));     // excluido él
        Assert.False(OfferRules.AppliesTo(exclude, 101, Chain));     // excluida su cadena
        Assert.True(OfferRules.AppliesTo(exclude, 101, 71));
        Assert.True(OfferRules.AppliesTo(exclude, 101));
        Assert.True(OfferRules.AppliesTo(exclude, null));            // sin cliente: aplica si no excluye la cadena
        Assert.False(OfferRules.AppliesTo(exclude, null, Chain));
        Assert.True(OfferRules.AppliesTo(O(4, new[] { (10, 1m) }, OfferAudienceModes.Exclude), Client, Chain));   // EXCLUDE sin escogidos = todos

        // y en el precio: la EXCLUDE no le llega al excluido, la INCLUDE por cadena sí al miembro
        var r = OfferRules.Resolve(10, Client, D1, new[] { General(9, 5m) }, new[] { exclude }, 9m, 71);
        Assert.Equal(PriceSources.GeneralList, r.Source);
        r = OfferRules.Resolve(10, 101, D1, new[] { General(9, 5m) }, new[] { include }, 9m, Chain);
        Assert.Equal((1m, PriceSources.Offer), (r.Price!.Value, r.Source));
    }

    [Fact]
    public void Offer_view_without_audience_mode_falls_back_to_applies_to_all()
    {
        var legacyClients = new OfferView(1, "Vieja", true, true, false, D1, null, new[] { Client }, new[] { new OfferItemView(10, 1m) });
        var legacyAll = legacyClients with { AppliesToAll = true, ClientIds = Array.Empty<int>() };
        Assert.Equal(OfferAudienceModes.Include, legacyClients.Mode);
        Assert.Equal(OfferAudienceModes.All, legacyAll.Mode);
        Assert.True(OfferRules.AppliesTo(legacyClients, Client));
        Assert.False(OfferRules.AppliesTo(legacyClients, 101));
        Assert.False(OfferRules.AppliesTo(legacyClients, null));
        Assert.True(OfferRules.AppliesTo(legacyAll, null));
        Assert.Equal(OfferAudienceModes.Exclude, (legacyClients with { AudienceMode = OfferAudienceModes.Exclude }).Mode);
    }

    [Fact]
    public void Audiences_overlap_considers_chains_exclusions_and_the_client_chain_map()
    {
        var map = new Dictionary<int, int> { [Client] = Chain };
        var all = O(1, new[] { (10, 1m) });
        var emptyInclude = O(2, new[] { (10, 1m) }, OfferAudienceModes.Include);
        var incClient = O(3, new[] { (10, 1m) }, OfferAudienceModes.Include, new[] { Client });
        var incChain = O(4, new[] { (10, 1m) }, OfferAudienceModes.Include, chains: new[] { Chain });
        var incOtherChain = O(5, new[] { (10, 1m) }, OfferAudienceModes.Include, chains: new[] { 71 });
        var excClient = O(6, new[] { (10, 1m) }, OfferAudienceModes.Exclude, new[] { Client });
        var excChain = O(7, new[] { (10, 1m) }, OfferAudienceModes.Exclude, chains: new[] { Chain });

        Assert.False(OfferRules.AudiencesOverlap(emptyInclude, all));        // no alcanza a nadie
        Assert.False(OfferRules.AudiencesOverlap(all, emptyInclude));
        Assert.True(OfferRules.AudiencesOverlap(all, incClient));
        Assert.True(OfferRules.AudiencesOverlap(incChain, incChain with { OfferId = 8 }));
        Assert.False(OfferRules.AudiencesOverlap(incChain, incOtherChain));
        Assert.False(OfferRules.AudiencesOverlap(incClient, incChain));      // sin mapa no se sabe que el cliente es de la cadena
        Assert.True(OfferRules.AudiencesOverlap(incClient, incChain, map));
        Assert.True(OfferRules.AudiencesOverlap(incChain, incClient, map));
        Assert.False(OfferRules.AudiencesOverlap(incClient, excClient));     // el único incluido está excluido
        Assert.True(OfferRules.AudiencesOverlap(incClient, excChain));       // sin mapa: el cliente no queda excluido
        Assert.False(OfferRules.AudiencesOverlap(incClient, excChain, map)); // con mapa: lo excluye su cadena
        Assert.False(OfferRules.AudiencesOverlap(excChain, incChain));
        Assert.True(OfferRules.AudiencesOverlap(incChain, excClient));       // otros miembros de la cadena no están excluidos
        Assert.True(OfferRules.AudiencesOverlap(excClient, excChain));       // dos EXCLUDE alcanzan a casi todos
        Assert.True(OfferRules.AudiencesOverlap(all, excClient));
    }

    [Fact]
    public void Find_cheaper_overlaps_skips_offers_whose_audiences_cannot_meet()
    {
        var map = new Dictionary<int, int> { [Client] = Chain };
        var mine = O(1, new[] { (10, 5m) }, OfferAudienceModes.Include, chains: new[] { Chain });
        var otherChain = O(2, new[] { (10, 4m) }, OfferAudienceModes.Include, chains: new[] { 71 });
        var excludesMyChain = O(3, new[] { (10, 4m) }, OfferAudienceModes.Exclude, chains: new[] { Chain });
        var memberClient = O(4, new[] { (10, 4m) }, OfferAudienceModes.Include, new[] { Client });
        Assert.Empty(OfferRules.FindCheaperOverlaps(mine, new[] { otherChain, excludesMyChain }, map));
        Assert.Empty(OfferRules.FindCheaperOverlaps(mine, new[] { memberClient }));
        var hit = Assert.Single(OfferRules.FindCheaperOverlaps(mine, new[] { otherChain, excludesMyChain, memberClient }, map));
        Assert.Equal((4, 10, 5m, 4m), (hit.OtherOfferId, hit.ProductId, hit.MyPrice, hit.OtherPrice));
    }

    [Fact]
    public void Offer_kit_is_on_sale_through_a_chain_included_offer_and_not_for_an_excluded_chain()
    {
        var byChain = O(1, new[] { (50, 30m) }, OfferAudienceModes.Include, chains: new[] { Chain });
        Assert.True(OfferKitRules.IsOnSale(50, 101, D1, new[] { byChain }, Chain));
        Assert.False(OfferKitRules.IsOnSale(50, 101, D1, new[] { byChain }, 71));
        Assert.False(OfferKitRules.IsOnSale(50, 101, D1, new[] { byChain }));
        var excluding = O(2, new[] { (50, 30m) }, OfferAudienceModes.Exclude, chains: new[] { Chain });
        Assert.False(OfferKitRules.IsOnSale(50, 101, D1, new[] { excluding }, Chain));
        Assert.True(OfferKitRules.IsOnSale(50, 101, D1, new[] { excluding }, 71));
    }

    [Fact]
    public void Audience_mode_normalizes_rejects_unknown_and_SetAudienceMode_keeps_AppliesToAll_in_sync()
    {
        Assert.Equal((OfferAudienceModes.Include, (string?)null), OfferAudienceModes.Normalize(" include "));
        Assert.Equal(((string?)null, (string?)null), OfferAudienceModes.Normalize("  "));
        Assert.Equal(((string?)null, (string?)null), OfferAudienceModes.Normalize(null));
        Assert.Equal(((string?)null, OfferRules.AudienceInvalid), OfferAudienceModes.Normalize("SOME"));
        Assert.Equal(OfferAudienceModes.All, OfferAudienceModes.FromAppliesToAll(true));
        Assert.Equal(OfferAudienceModes.Include, OfferAudienceModes.FromAppliesToAll(false));
        Assert.Equal(new[] { "ALL", "INCLUDE", "EXCLUDE" }, OfferAudienceModes.Values);

        var o = new Offer();
        Assert.Equal((OfferAudienceModes.All, true), (o.AudienceMode, o.AppliesToAll));
        o.SetAudienceMode(OfferAudienceModes.Exclude);
        Assert.Equal((OfferAudienceModes.Exclude, false), (o.AudienceMode, o.AppliesToAll));
        o.AppliesToAll = false;   // el «aplica a todos» de antes no deshace un EXCLUDE
        Assert.Equal(OfferAudienceModes.Exclude, o.AudienceMode);
        o.SetAudienceMode(OfferAudienceModes.All);
        Assert.Equal((OfferAudienceModes.All, true), (o.AudienceMode, o.AppliesToAll));
        o.AppliesToAll = false;   // compatibilidad: false = INCLUDE
        Assert.Equal((OfferAudienceModes.Include, false), (o.AudienceMode, o.AppliesToAll));
        o.AppliesToAll = true;
        Assert.Equal(OfferAudienceModes.All, o.AudienceMode);
        Assert.Equal(OfferAudienceModes.Include, new Offer { AppliesToAll = false }.AudienceMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => o.SetAudienceMode("SOME"));
        Assert.Equal(OfferAudienceModes.All, o.AudienceMode);
    }

    // ---------------------------------------------------------------- cruces de listas

    [Fact]
    public void Special_lists_do_not_conflict_with_normal_lists_but_do_with_same_owner_specials()
    {
        var special = L(1, new[] { (10, 5m) }, clients: new[] { Client }, special: true);
        var normal = L(2, new[] { (10, 6m) }, clients: new[] { Client });
        var sameOwner = L(3, new[] { (10, 4m) }, clients: new[] { Client }, special: true);
        var otherOwner = L(4, new[] { (10, 4m) }, clients: new[] { 101 }, special: true);
        var chainSpecial = L(5, new[] { (10, 4m) }, chains: new[] { Chain }, special: true);
        Assert.False(PriceListRules.SameAudience(special, normal));
        Assert.True(PriceListRules.SameAudience(special, sameOwner));
        Assert.False(PriceListRules.SameAudience(special, otherOwner));
        Assert.False(PriceListRules.SameAudience(special, chainSpecial));   // el del cliente y el de su cadena conviven: gana el más bajo
        Assert.Equal(new[] { 3 }, PriceListRules.FindConflicts(special, new[] { normal, sameOwner, otherOwner, chainSpecial }).Select(c => c.OtherPriceListId).ToArray());
    }

    [Fact]
    public void Lists_sharing_a_chain_conflict_but_a_client_list_and_its_chain_list_coexist()
    {
        var a = L(1, new[] { (10, 5m) }, chains: new[] { Chain, 71 });
        var b = L(2, new[] { (10, 6m) }, chains: new[] { 71 });
        var c = L(3, new[] { (10, 6m) }, chains: new[] { 72 });
        var clientList = L(4, new[] { (10, 6m) }, clients: new[] { Client });
        var general = General(5, 6m);
        Assert.True(PriceListRules.SameAudience(a, b));
        Assert.False(PriceListRules.SameAudience(a, c));
        Assert.False(PriceListRules.SameAudience(a, clientList));
        Assert.False(PriceListRules.SameAudience(a, general));
        Assert.Equal(new[] { 2 }, PriceListRules.FindConflicts(a, new[] { b, c, clientList, general }).Select(x => x.OtherPriceListId).ToArray());
        Assert.Equal(new[] { 6 }, PriceListRules.FindConflicts(general, new[] { a, General(6, 7m) }).Select(x => x.OtherPriceListId).ToArray());
    }

    [Fact]
    public void The_new_and_changed_messages_say_clients_or_chains()
    {
        Assert.Equal("Escoja al menos un cliente o una cadena, o marque que la lista aplica a todos los clientes.", PriceListRules.PublishNeedsClients);
        Assert.Equal("La lista tiene clientes o cadenas escogidos: quítelos antes de hacerla general (aplica a todos).", PriceListRules.GeneralWithClients);
        Assert.Equal("Una lista admite como máximo 500 cadenas.", PriceListRules.TooManyChains);
        Assert.Equal("Esta lista es el precio especial de «Walgreens»: el nombre, las fechas y a quién aplica se cambian en «Precios especiales» de su ficha.",
            PriceListRules.DedicatedLocked("Walgreens"));
        Assert.Equal("Escoja al menos un cliente o una cadena, o marque que la oferta aplica a todos los clientes.", OfferRules.PublishNeedsClients);
        Assert.Equal("La oferta tiene clientes o cadenas escogidos: quítelos antes de hacerla para todos.", OfferRules.GeneralWithClients);
        Assert.Equal("Una oferta admite como máximo 500 cadenas.", OfferRules.TooManyChains);
        Assert.Equal(500, PriceListCodes.MaxChains);
        Assert.Equal(500, OfferCodes.MaxChains);
        Assert.Equal(2000, PriceListCodes.MaxClients);
    }

    // ---------------------------------------------------------------- precios especiales

    [Fact]
    public void Dedicated_name_mentions_owner_and_dates_and_never_passes_120()
    {
        Assert.Equal("Precio especial — Walgreens (del 01/12/2026 al 31/12/2026)", SpecialPriceRules.DedicatedName("Walgreens", D1, new DateOnly(2026, 12, 31)));
        Assert.Equal("Precio especial — Farmacia Uno (desde el 01/12/2026, sin fecha de fin)", SpecialPriceRules.DedicatedName(" Farmacia Uno ", D1, null));
        var longName = SpecialPriceRules.DedicatedName(new string('x', 300), D1, null);
        Assert.Equal(PriceListCodes.MaxNameLength, longName.Length);
        Assert.StartsWith("Precio especial — xxx", longName);
        Assert.EndsWith("… (desde el 01/12/2026, sin fecha de fin)", longName);
        Assert.Null(PriceListRules.NormalizeName(longName).Error);
    }

    [Fact]
    public void Row_error_rejects_past_valid_to_only_on_create()
    {
        var p = Guid.NewGuid();
        var today = D1;
        Assert.Null(SpecialPriceRules.RowError(p, 5m, today, null, today, isCreate: true));
        Assert.Equal(("productPublicId", PriceListRules.ItemProductRequired), SpecialPriceRules.RowError(null, 5m, today, null, today, true));
        Assert.Equal(("price", PriceListRules.ItemPriceRequired), SpecialPriceRules.RowError(p, null, today, null, today, true));
        Assert.Equal(("validFrom", SpecialPriceRules.ValidFromRequired), SpecialPriceRules.RowError(p, 5m, null, null, today, true));
        Assert.Equal(("validTo", PriceListRules.DateRange), SpecialPriceRules.RowError(p, 5m, today, today.AddDays(-1), today, true));
        Assert.Equal(("validTo", SpecialPriceRules.ValidToPast), SpecialPriceRules.RowError(p, 5m, today.AddDays(-10), today.AddDays(-1), today, true));
        Assert.Null(SpecialPriceRules.RowError(p, 5m, today.AddDays(-10), today.AddDays(-1), today, isCreate: false));   // al editar se puede cerrar
        Assert.Null(SpecialPriceRules.RowError(p, 5m, today.AddDays(-10), today, today, true));                           // vence hoy: vale
        Assert.Equal("La fecha «hasta» ya pasó: un precio especial nuevo tiene que valer hoy o después.", SpecialPriceRules.ValidToPast);

        Assert.Null(SpecialPriceRules.DaysToExpire(null, today));
        Assert.Equal(0, SpecialPriceRules.DaysToExpire(today, today));
        Assert.Equal(30, SpecialPriceRules.DaysToExpire(today.AddDays(30), today));
        Assert.Equal(-2, SpecialPriceRules.DaysToExpire(today.AddDays(-2), today));
        Assert.Equal("SPECIAL_PRICE", SpecialPriceCodes.DataSourceKey);
        Assert.Equal(SpecialPriceCodes.DataSourceKey, SpecialPriceCodes.EntityType);
    }

    [Fact]
    public void Price_origin_copy_keeps_chain_only_for_chain_list()
    {
        Assert.Equal(new PriceOrigin(PriceSources.ChainList, 2, null, Chain, 7m), PriceOriginRules.Copy(new ResolvedPrice(7m, PriceSources.ChainList, 2), Chain));
        Assert.Equal(new PriceOrigin(PriceSources.ClientList, 1, null, null, 8m), PriceOriginRules.Copy(new ResolvedPrice(8m, PriceSources.ClientList, 1), Chain));
        Assert.Equal(new PriceOrigin(PriceSources.Offer, null, 3, null, 6m), PriceOriginRules.Copy(new ResolvedPrice(6m, PriceSources.Offer, null, null, 3), Chain));
        Assert.Equal(new PriceOrigin(PriceSources.Product, null, null, null, 9m), PriceOriginRules.Copy(new ResolvedPrice(9m, PriceSources.Product), Chain));
        Assert.Equal(new PriceOrigin(PriceSources.None, null, null, null, null), PriceOriginRules.Copy(new ResolvedPrice(null, PriceSources.None), null));
    }
}
