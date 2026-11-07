using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

// Lote 221 (P1 del pedido de vendedores): reglas puras de las listas de precios — vigencia, estado, cruces, quién gana y el permiso de precio.
public sealed class PriceListRulesTests
{
    private static readonly DateOnly D1 = new(2026, 11, 1);

    private static PriceListView L(int id, bool all, DateOnly from, DateOnly? to, IReadOnlyCollection<int>? clients = null, bool published = true, params (int Product, decimal Price)[] items)
        => new(id, "Lista " + id, published, all, from, to, clients ?? Array.Empty<int>(), items.Select(i => new PriceListItemView(i.Product, i.Price)).ToList());

    [Fact]
    public void Validity_includes_both_ends_and_an_open_end_never_expires()
    {
        Assert.True(PriceListRules.InEffect(D1, D1.AddDays(9), D1));
        Assert.True(PriceListRules.InEffect(D1, D1.AddDays(9), D1.AddDays(9)));
        Assert.False(PriceListRules.InEffect(D1, D1.AddDays(9), D1.AddDays(10)));
        Assert.False(PriceListRules.InEffect(D1, null, D1.AddDays(-1)));
        Assert.True(PriceListRules.InEffect(D1, null, D1.AddYears(5)));
        Assert.Null(PriceListRules.DateRangeError(D1, null));
        Assert.Null(PriceListRules.DateRangeError(D1, D1));
        Assert.Equal(PriceListRules.DateRange, PriceListRules.DateRangeError(D1, D1.AddDays(-1)));
    }

    [Fact]
    public void The_shown_state_comes_from_published_and_the_dates()
    {
        Assert.Equal(PriceListStates.Draft, PriceListRules.StateOf(false, D1, null, D1));
        Assert.Equal(PriceListStates.Scheduled, PriceListRules.StateOf(true, D1, null, D1.AddDays(-1)));
        Assert.Equal(PriceListStates.Active, PriceListRules.StateOf(true, D1, D1.AddDays(30), D1.AddDays(30)));
        Assert.Equal(PriceListStates.Expired, PriceListRules.StateOf(true, D1, D1.AddDays(30), D1.AddDays(31)));
    }

    [Fact]
    public void Name_and_notes_are_trimmed_and_validated()
    {
        Assert.Equal(("Temporada", (string?)null), PriceListRules.NormalizeName("  Temporada "));
        Assert.Equal(PriceListRules.NameRequired, PriceListRules.NormalizeName("   ").Error);
        Assert.Equal(PriceListRules.NameTooLong, PriceListRules.NormalizeName(new string('x', PriceListCodes.MaxNameLength + 1)).Error);
        Assert.Equal(((string?)null, (string?)null), PriceListRules.NormalizeNotes(" "));
        Assert.Equal(PriceListRules.NotesTooLong, PriceListRules.NormalizeNotes(new string('x', 501)).Error);
    }

    [Fact]
    public void Dates_overlap_including_the_ends_and_open_ranges()
    {
        Assert.True(PriceListRules.DatesOverlap(D1, D1.AddDays(10), D1.AddDays(10), D1.AddDays(20)));
        Assert.False(PriceListRules.DatesOverlap(D1, D1.AddDays(10), D1.AddDays(11), D1.AddDays(20)));
        Assert.True(PriceListRules.DatesOverlap(D1, null, D1.AddYears(1), null));
        Assert.True(PriceListRules.DatesOverlap(D1.AddDays(5), null, D1, D1.AddDays(5)));
        Assert.False(PriceListRules.DatesOverlap(D1.AddDays(6), null, D1, D1.AddDays(5)));
    }

    [Fact]
    public void Two_general_lists_conflict_only_on_shared_products_in_overlapping_dates()
    {
        var a = L(1, true, D1, D1.AddDays(30), items: new[] { (10, 1m), (11, 2m) });
        var b = L(2, true, D1.AddDays(15), null, items: new[] { (11, 3m), (12, 4m) });
        var c = L(3, true, D1.AddDays(31), null, items: new[] { (10, 5m) });   // empieza después de que A vence
        var d = L(4, true, D1, null, published: false, items: new[] { (10, 6m) });   // borrador: no cuenta
        var conflicts = PriceListRules.FindConflicts(a, new[] { a, b, c, d });
        var only = Assert.Single(conflicts);
        Assert.Equal(2, only.OtherPriceListId);
        Assert.Equal(new[] { 11 }, only.ProductIds.ToArray());
    }

    [Fact]
    public void Client_lists_conflict_only_when_they_share_a_client_and_never_with_a_general_list()
    {
        var mine = L(1, false, D1, null, new[] { 100, 101 }, items: new[] { (10, 1m) });
        var sharesClient = L(2, false, D1, null, new[] { 101 }, items: new[] { (10, 2m) });
        var otherClient = L(3, false, D1, null, new[] { 200 }, items: new[] { (10, 3m) });
        var general = L(4, true, D1, null, items: new[] { (10, 4m) });
        var conflicts = PriceListRules.FindConflicts(mine, new[] { sharesClient, otherClient, general });
        Assert.Equal(new[] { 2 }, conflicts.Select(c => c.OtherPriceListId).ToArray());
        Assert.False(PriceListRules.SameAudience(mine, general));
    }

    [Fact]
    public void The_overlap_message_names_the_other_list_and_up_to_five_skus()
    {
        var msg = PriceListRules.Overlap("Navidad", new[] { "A", "B", "C", "D", "E", "F", "G" });
        Assert.Equal("La lista se cruza con «Navidad» en las mismas fechas y para los mismos clientes en: A, B, C, D, E y 2 más. Cambie las fechas o quite esos productos de una de las dos.", msg);
    }

    [Fact]
    public void The_client_list_wins_over_the_general_list_which_wins_over_the_product_price()
    {
        var general = L(1, true, D1, null, items: new[] { (10, 6.99m), (11, 1.25m) });
        var negotiated = L(2, false, D1, null, new[] { 100 }, items: new[] { (10, 5.50m) });
        var lists = new[] { general, negotiated };

        Assert.Equal(new ResolvedPrice(5.50m, PriceSources.ClientList, 2), PriceListRules.Resolve(10, 100, D1, lists, 7m));
        Assert.Equal(new ResolvedPrice(6.99m, PriceSources.GeneralList, 1), PriceListRules.Resolve(10, 200, D1, lists, 7m));   // otro cliente: la general
        Assert.Equal(new ResolvedPrice(6.99m, PriceSources.GeneralList, 1), PriceListRules.Resolve(10, null, D1, lists, 7m));   // sin cliente: la general
        Assert.Equal(new ResolvedPrice(1.25m, PriceSources.GeneralList, 1), PriceListRules.Resolve(11, 100, D1, lists, 2m));   // la del cliente no lo trae
        Assert.Equal(new ResolvedPrice(9m, PriceSources.Product), PriceListRules.Resolve(12, 100, D1, lists, 9m));   // ninguna lo trae
        Assert.Equal(new ResolvedPrice(null, PriceSources.None), PriceListRules.Resolve(12, 100, D1, lists, null));
    }

    [Fact]
    public void Drafts_and_lists_out_of_their_dates_do_not_price()
    {
        var draft = L(1, true, D1, null, published: false, items: new[] { (10, 1m) });
        var expired = L(2, true, D1.AddDays(-30), D1.AddDays(-1), items: new[] { (10, 2m) });
        var future = L(3, false, D1.AddDays(1), null, new[] { 100 }, items: new[] { (10, 3m) });
        Assert.Equal(PriceSources.Product, PriceListRules.Resolve(10, 100, D1, new[] { draft, expired, future }, 7m).Source);
        Assert.Equal(3m, PriceListRules.Resolve(10, 100, D1.AddDays(1), new[] { draft, expired, future }, 7m).Price);
    }

    [Fact]
    public void A_kit_in_a_list_is_priced_like_any_product_and_carries_the_item_photo()
    {
        var list = new PriceListView(1, "Ofertas", true, true, D1, null, Array.Empty<int>(), new[] { new PriceListItemView(50, 41.94m, ImageFileId: 77) });
        Assert.Equal(new ResolvedPrice(41.94m, PriceSources.GeneralList, 1, 77), PriceListRules.Resolve(50, 100, D1, new[] { list }, 49.93m));
    }

    [Fact]
    public void With_old_overlapping_data_the_list_that_started_later_wins()
    {
        var older = L(1, true, D1, null, items: new[] { (10, 1m) });
        var newer = L(2, true, D1.AddDays(5), null, items: new[] { (10, 2m) });
        var sameDayNewer = L(3, true, D1.AddDays(5), null, items: new[] { (10, 3m) });
        Assert.Equal(2m, PriceListRules.Resolve(10, null, D1.AddDays(6), new[] { newer, older }, null).Price);
        Assert.Equal(3m, PriceListRules.Resolve(10, null, D1.AddDays(6), new[] { sameDayNewer, older, newer }, null).Price);
    }

    [Fact]
    public void The_update_sql_section_only_adds_and_is_idempotent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Teikem.sln"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var start = sql.IndexOf("2026-11-07 — Lote 221", StringComparison.Ordinal);
        Assert.True(start > 0);
        var section = SqlSections.From(sql, start);
        foreach (var t in new[] { "PriceList", "PriceListItem", "PriceListClient" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{t}', N'U') IS NULL CREATE TABLE dbo.{t} (", section);
        foreach (var c in new[] { "IsPromotion", "PromoFrom", "PromoTo", "PromoImageFileId" })
            Assert.Contains($"IF COL_LENGTH(N'dbo.Product', N'{c}') IS NULL", section);
        Assert.Contains("N'PRICE_LIST'", section);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'sales.pricelist')", section);
        Assert.Contains("'L221_SALES_PRICELIST'", section);   // los roles ya clonados lo reciben una sola vez
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Price_permission_and_reprice_on_client_change()
    {
        Assert.False(PriceListRules.NeedsPricePermission(5m, 5m, null));   // el de lista
        Assert.False(PriceListRules.NeedsPricePermission(4m, 5m, 4m));     // el que la línea ya tenía
        Assert.True(PriceListRules.NeedsPricePermission(4m, 5m, null));
        Assert.True(PriceListRules.NeedsPricePermission(4m, null, null));  // sin precio resuelto: escribirlo exige el permiso

        Assert.Equal(5.5m, PriceListRules.RepriceOnClientChange(6.99m, 6.99m, 5.5m));   // tenía el resuelto: toma el del nuevo cliente
        Assert.Equal(3m, PriceListRules.RepriceOnClientChange(3m, 6.99m, 5.5m));         // excepción con sales.price: se queda
        Assert.Equal(6.99m, PriceListRules.RepriceOnClientChange(6.99m, 6.99m, null));   // el nuevo no tiene precio: se queda
    }
}
