using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

// Lote 229: reglas puras de las ofertas — vigencia (también sin fecha de fin), gana el precio más bajo entre negociado y oferta, dos ofertas (la más barata + aviso),
// oferta más cara que la general (aviso), clientes escogidos y los choques al armar la oferta. Lote 231: el kit de oferta (biblioteca) se vende si está en alguna
// oferta vigente; su último uso y la comparación de su composición.
public sealed class OfferRulesTests
{
    private static readonly DateOnly D1 = new(2026, 11, 9);

    private static OfferView O(int id, DateOnly from, DateOnly? to, (int Product, decimal Price)[] items, int[]? clients = null, bool published = true, bool active = true, string? name = null)
        => new(id, name ?? $"Oferta {id}", published, active, clients is null, from, to, clients ?? Array.Empty<int>(),
            items.Select(i => new OfferItemView(i.Product, i.Price)).ToList());

    private static PriceListView L(int id, DateOnly from, DateOnly? to, (int Product, decimal Price)[] items, int[]? clients = null)
        => new(id, $"Lista {id}", true, clients is null, from, to, clients ?? Array.Empty<int>(), items.Select(i => new PriceListItemView(i.Product, i.Price)).ToList());

    [Fact]
    public void An_offer_applies_only_published_active_and_within_its_dates_and_without_end_date_it_never_expires()
    {
        var open = O(1, D1, null, new[] { (10, 1m) });
        Assert.True(OfferRules.InEffect(open, D1));
        Assert.True(OfferRules.InEffect(open, D1.AddYears(5)));   // sin «hasta»: no vence
        Assert.False(OfferRules.InEffect(open, D1.AddDays(-1)));
        Assert.False(OfferRules.InEffect(O(2, D1, D1.AddDays(5), new[] { (10, 1m) }), D1.AddDays(6)));
        Assert.True(OfferRules.InEffect(O(2, D1, D1.AddDays(5), new[] { (10, 1m) }), D1.AddDays(5)));   // extremos incluidos
        Assert.False(OfferRules.InEffect(O(3, D1, null, new[] { (10, 1m) }, published: false), D1));
        Assert.False(OfferRules.InEffect(O(4, D1, null, new[] { (10, 1m) }, active: false), D1));
        Assert.Equal("desde el 09/11/2026, sin fecha de fin", OfferRules.RangeText(D1, null));
        Assert.Equal("del 09/11/2026 al 30/11/2026", OfferRules.RangeText(D1, new DateOnly(2026, 11, 30)));
        Assert.Equal(OfferRules.DateRange, OfferRules.DateRangeError(D1, D1.AddDays(-1)));
        Assert.Null(OfferRules.DateRangeError(D1, null));
    }

    [Fact]
    public void Between_the_negotiated_price_and_the_offer_the_lowest_wins()
    {
        var negotiated = L(1, D1, null, new[] { (10, 5.50m) }, new[] { 100 });
        var general = L(2, D1, null, new[] { (10, 6.99m) });
        var cheaper = O(7, D1, null, new[] { (10, 5m) });
        var pricier = O(8, D1, null, new[] { (10, 6m) });

        var r = OfferRules.Resolve(10, 100, D1, new[] { negotiated, general }, new[] { cheaper }, 7m);
        Assert.Equal((5m, PriceSources.Offer, (int?)7), (r.Price!.Value, r.Source, r.OfferId));
        r = OfferRules.Resolve(10, 100, D1, new[] { negotiated, general }, new[] { pricier }, 7m);
        Assert.Equal((5.50m, PriceSources.ClientList, (int?)1), (r.Price!.Value, r.Source, r.PriceListId));
        // a igual precio se queda el negociado
        r = OfferRules.Resolve(10, 100, D1, new[] { negotiated }, new[] { O(9, D1, null, new[] { (10, 5.50m) }) }, 7m);
        Assert.Equal(PriceSources.ClientList, r.Source);
        // sin negociado: la oferta gana a la lista general si es más barata
        r = OfferRules.Resolve(10, 200, D1, new[] { negotiated, general }, new[] { pricier }, 7m);
        Assert.Equal((6m, PriceSources.Offer), (r.Price!.Value, r.Source));
        Assert.Null(r.Notices);
    }

    [Fact]
    public void Without_lists_nor_offers_it_falls_back_to_the_general_list_then_the_product_price()
    {
        var general = L(2, D1, null, new[] { (10, 6.99m) });
        Assert.Equal(new ResolvedPrice(6.99m, PriceSources.GeneralList, 2), OfferRules.Resolve(10, 100, D1, new[] { general }, Array.Empty<OfferView>(), 7m));
        Assert.Equal(new ResolvedPrice(7m, PriceSources.Product), OfferRules.Resolve(11, 100, D1, new[] { general }, Array.Empty<OfferView>(), 7m));
        Assert.Equal(new ResolvedPrice(null, PriceSources.None), OfferRules.Resolve(11, 100, D1, new[] { general }, Array.Empty<OfferView>(), null));
        // una oferta en borrador, vencida o de otro producto no cuenta
        var others = new[] { O(1, D1, null, new[] { (10, 1m) }, published: false), O(2, D1.AddDays(-9), D1.AddDays(-1), new[] { (10, 1m) }), O(3, D1, null, new[] { (99, 1m) }) };
        Assert.Equal(PriceSources.GeneralList, OfferRules.Resolve(10, 100, D1, new[] { general }, others, 7m).Source);
    }

    [Fact]
    public void Two_offers_at_once_the_cheapest_wins_with_a_notice()
    {
        var a = O(1, D1, null, new[] { (10, 5m) }, name: "Navidad");
        var b = O(2, D1, null, new[] { (10, 4.50m) }, name: "Fin de año");
        var r = OfferRules.Resolve(10, 100, D1, Array.Empty<PriceListView>(), new[] { a, b }, 7m);
        Assert.Equal((4.50m, PriceSources.Offer, (int?)2), (r.Price!.Value, r.Source, r.OfferId));
        var n = Assert.Single(r.Notices!);
        Assert.Equal(new PriceNotice(PriceNoticeCodes.TwoOffers, 2, "Fin de año", 4.50m, 1, "Navidad", 5m), n);
        Assert.Equal("El producto 52800 está en dos ofertas a la vez: «Fin de año» a $4.50 y «Navidad» a $5.00. Se aplica la más barata («Fin de año»).",
            OfferRules.NoticeMessage("52800", n));
        // a igual precio, la que empezó más tarde
        var later = O(3, D1.AddDays(-1), null, new[] { (10, 4.50m) });
        Assert.Equal((int?)2, OfferRules.Resolve(10, 100, D1, Array.Empty<PriceListView>(), new[] { later, b }, 7m).OfferId);
    }

    [Fact]
    public void An_offer_pricier_than_the_general_list_loses_with_a_notice()
    {
        var general = L(2, D1, null, new[] { (10, 6.99m) });
        var offer = O(5, D1, null, new[] { (10, 8m) }, name: "Mala");
        var r = OfferRules.Resolve(10, 100, D1, new[] { general }, new[] { offer }, 9m);
        Assert.Equal((6.99m, PriceSources.GeneralList), (r.Price!.Value, r.Source));
        var n = Assert.Single(r.Notices!);
        Assert.Equal(PriceNoticeCodes.OfferAboveGeneral, n.Code);
        Assert.Equal("La oferta «Mala» pone 52800 a $8.00, más caro que la lista general ($6.99): se aplica el precio más bajo.", OfferRules.NoticeMessage("52800", n));
    }

    [Fact]
    public void An_offer_for_chosen_clients_applies_only_to_them()
    {
        var mine = O(1, D1, null, new[] { (10, 3m) }, new[] { 100 });
        Assert.Equal(PriceSources.Offer, OfferRules.Resolve(10, 100, D1, Array.Empty<PriceListView>(), new[] { mine }, 7m).Source);
        Assert.Equal(PriceSources.Product, OfferRules.Resolve(10, 200, D1, Array.Empty<PriceListView>(), new[] { mine }, 7m).Source);
        Assert.Equal(PriceSources.Product, OfferRules.Resolve(10, null, D1, Array.Empty<PriceListView>(), new[] { mine }, 7m).Source);   // sin cliente: solo las de todos
        Assert.True(OfferRules.AppliesTo(O(2, D1, null, new[] { (10, 3m) }), null));
    }

    [Fact]
    public void An_offer_kit_sells_only_while_it_is_in_some_published_offer_in_effect_for_the_client()   // Lote 231 (antes: atado a UNA oferta)
    {
        var o = O(1, D1, D1.AddDays(10), new[] { (10, 41.94m) }, name: "OFERTA1");
        Assert.True(OfferKitRules.IsOnSale(10, null, D1, new[] { o }));
        Assert.False(OfferKitRules.IsOnSale(10, null, D1.AddDays(11), new[] { o }));   // ya terminó
        Assert.False(OfferKitRules.IsOnSale(10, null, D1, new[] { o with { IsPublished = false } }));
        Assert.False(OfferKitRules.IsOnSale(10, null, D1, new[] { o with { IsActive = false } }));
        Assert.False(OfferKitRules.IsOnSale(11, null, D1, new[] { o }));   // no está en la oferta
        Assert.False(OfferKitRules.IsOnSale(10, null, D1, Array.Empty<OfferView>()));
        // en otra oferta que sigue vigente: se vende
        Assert.True(OfferKitRules.IsOnSale(10, null, D1.AddDays(11), new[] { o, O(2, D1.AddDays(11), null, new[] { (10, 39m) }) }));
        // oferta de clientes escogidos: solo para ellos
        var mine = O(3, D1, null, new[] { (10, 40m) }, new[] { 100 });
        Assert.True(OfferKitRules.IsOnSale(10, 100, D1, new[] { mine }));
        Assert.False(OfferKitRules.IsOnSale(10, 200, D1, new[] { mine }));
        Assert.False(OfferKitRules.IsOnSale(10, null, D1, new[] { mine }));
        Assert.Equal("El kit de oferta OFERTA1 no está en ninguna oferta publicada y vigente para este cliente: no se puede vender.", OfferKitRules.NotOnSale("OFERTA1"));
    }

    [Fact]
    public void The_last_use_of_an_offer_kit_is_the_newest_row_still_in_an_active_offer_and_its_composition_compares_pieces_quantities_and_marks()   // Lote 231
    {
        Assert.Null(OfferKitRules.LastUse(Array.Empty<OfferKitUse>()));
        var nov = new OfferKitUse(5, 1, "Noviembre", 41.94m, true, true);
        var jan = new OfferKitUse(9, 2, "Enero", 39.99m, true, true);
        Assert.Equal(jan, OfferKitRules.LastUse(new[] { nov, jan }));
        Assert.Equal(nov, OfferKitRules.LastUse(new[] { nov, jan with { ItemActive = false } }));   // el renglón quitado no manda si hay otro puesto
        Assert.Equal(nov, OfferKitRules.LastUse(new[] { nov, jan with { OfferActive = false } }));
        Assert.Equal(jan with { ItemActive = false }, OfferKitRules.LastUse(new[] { nov with { ItemActive = false }, jan with { ItemActive = false } }));   // todos quitados: el más nuevo

        var a = new[] { new OfferKitPiece(1, 6m, false), new OfferKitPiece(2, 1m, true) };
        Assert.False(OfferKitRules.CompositionChanges(a, false, new[] { new OfferKitPiece(1, 6.000m, false), new OfferKitPiece(2, 1m, true) }, false));
        Assert.True(OfferKitRules.CompositionChanges(a, false, new[] { new OfferKitPiece(1, 5m, false), new OfferKitPiece(2, 1m, true) }, false));   // cantidad
        Assert.True(OfferKitRules.CompositionChanges(a, false, new[] { new OfferKitPiece(1, 6m, false), new OfferKitPiece(2, 1m, false) }, false));   // «no sustituible»
        Assert.False(OfferKitRules.CompositionChanges(a, false, new[] { new OfferKitPiece(2, 1m, true), new OfferKitPiece(1, 6m, false) }, false));   // solo el orden: no cambia
        Assert.True(OfferKitRules.CompositionChanges(a, false, new[] { new OfferKitPiece(1, 6m, false) }, false));   // una pieza menos
        Assert.True(OfferKitRules.CompositionChanges(a, false, a, true));   // «ajustable al pedir»
    }

    [Fact]
    public void Building_an_offer_warns_about_another_published_offer_with_the_same_product_equal_or_cheaper()
    {
        var mine = O(1, D1, D1.AddDays(30), new[] { (10, 5m), (11, 2m) }, published: false);
        var cheaper = O(2, D1.AddDays(10), null, new[] { (10, 4m), (11, 3m) }, name: "Otra");
        var later = O(3, D1.AddDays(31), null, new[] { (10, 1m) });                 // no se cruza en fechas
        var otherClients = O(4, D1, null, new[] { (10, 1m) }, new[] { 300 });        // para todos (la mía) se cruza con cualquiera
        var draft = O(5, D1, null, new[] { (10, 1m) }, published: false);            // en borrador no cuenta
        var found = OfferRules.FindCheaperOverlaps(mine, new[] { cheaper, later, otherClients, draft });
        Assert.Equal(new[] { (2, 10, 4m), (4, 10, 1m) }.OrderBy(t => t.Item3).ToArray(),
            found.Select(f => (f.OtherOfferId, f.ProductId, f.OtherPrice)).OrderBy(t => t.OtherPrice).ToArray());
        Assert.Equal("El producto 52800 también está en la oferta «Otra» (desde el 19/11/2026, sin fecha de fin) a $4.00, igual o más barata que aquí ($5.00): mientras las dos estén vigentes se aplica el precio más bajo.",
            OfferRules.CheaperOfferMessage("52800", found.Single(f => f.OtherOfferId == 2)));
        // dos ofertas de clientes escogidos sin clientes en común no chocan
        Assert.Empty(OfferRules.FindCheaperOverlaps(mine with { AppliesToAll = false, ClientIds = new[] { 100 } }, new[] { otherClients }));
    }

    [Fact]
    public void The_update_sql_section_only_adds_and_is_idempotent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var start = sql.IndexOf("2026-11-09 — Lote 229", StringComparison.Ordinal);
        Assert.True(start > 0);
        var section = SqlSections.From(sql, start);
        foreach (var t in new[] { "Offer", "OfferItem", "OfferClient" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{t}', N'U') IS NULL CREATE TABLE dbo.{t} (", section);
        foreach (var c in new[] { "ImageFileId", "OfferId" })
            Assert.Contains($"IF COL_LENGTH(N'dbo.Product', N'{c}') IS NULL", section);
        Assert.Contains("WHERE ImageFileId IS NULL AND PromoImageFileId IS NOT NULL", section);
        Assert.Contains("N'OFFER'", section);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", section, StringComparison.OrdinalIgnoreCase);
    }
}
