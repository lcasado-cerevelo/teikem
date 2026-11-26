using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

// Lote 247 (P2, DECISIÓN 12): el ORIGEN del precio se guarda en la línea de la orden cada vez que se escribe su precio (alta del borrador, cambio de cliente del
// borrador, cambio de precio de una SOLICITADA). Se guardan ids; los nombres se leen al mostrar. Las líneas de antes del lote (columnas en NULL) no tienen origen.
public sealed class SalesLinePriceOriginTests
{
    private static async Task<SalesOrderLine> StoredLineAsync(PriceWorld x, Guid orderPublicId, int lineNo = 1)
    {
        x.F.Db.ChangeTracker.Clear();
        var id = await x.F.Db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == orderPublicId).Select(o => o.SalesOrderId).SingleAsync();
        return await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == id && l.LineNo == lineNo);
    }

    [Fact]
    public async Task The_draft_line_stores_source_list_chain_and_resolved_price()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        var general = await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m), (b, 9m));
        var chainList = await x.ChainListAsync("Walgreens", x.Today.AddDays(-1), null, walgreens, special: true, (a, 7m));

        var o = await x.DraftAsync(w1, new SalesOrderLineRequest(a.PublicId, 2m), new SalesOrderLineRequest(b.PublicId, 1m));
        var la = await StoredLineAsync(x, o.PublicId, 1);
        Assert.Equal((PriceSources.ChainList, chainList.PriceListId, (int?)null, walgreens.ClientChainId, 7m, 7m),
            (la.PriceSource, la.PriceListId!.Value, la.OfferId, la.PriceChainId!.Value, la.ResolvedUnitPrice!.Value, la.UnitPrice));
        var lb = await StoredLineAsync(x, o.PublicId, 2);
        var generalId = await x.F.Db.Set<PriceList>().Where(l => l.PublicId == general.PublicId).Select(l => l.PriceListId).SingleAsync();
        Assert.Equal((PriceSources.GeneralList, generalId, (int?)null, 9m), (lb.PriceSource, lb.PriceListId!.Value, lb.PriceChainId, lb.ResolvedUnitPrice!.Value));   // la cadena solo con CHAIN_LIST
        Assert.All(o.Lines, l => Assert.False(l.IsManualPrice));

        // una oferta guarda la oferta, sin lista
        var offer = await x.OfferAsync("Oferta B", OfferAudienceModes.All, null, null, (b, 5m));
        var o2 = await x.DraftAsync(x.C1, new SalesOrderLineRequest(b.PublicId, 1m));
        var lo = await StoredLineAsync(x, o2.PublicId);
        Assert.Equal((PriceSources.Offer, (int?)null, offer.OfferId, 5m), (lo.PriceSource, lo.PriceListId, lo.OfferId!.Value, lo.ResolvedUnitPrice!.Value));
        Assert.Equal(("Oferta B", offer.PublicId), (o2.Lines[0].OfferName, o2.Lines[0].OfferPublicId!.Value));
        // y el precio del producto, sin lista ni oferta
        var c = await x.ProductAsync("C", 3.25m);
        var o3 = await x.DraftAsync(x.C1, new SalesOrderLineRequest(c.PublicId, 1m));
        Assert.Equal((PriceSources.Product, (Guid?)null, (Guid?)null, 3.25m), (o3.Lines[0].PriceSource, o3.Lines[0].PriceListPublicId, o3.Lines[0].OfferPublicId, o3.Lines[0].ResolvedUnitPrice!.Value));
    }

    [Fact]
    public async Task A_manual_price_keeps_the_resolved_price_and_is_marked_manual()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 20m);
        var noPrice = await x.ProductAsync("SIN", null);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m));
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate, PermissionCatalog.SalesPrice);

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m, UnitPrice: 8.25m), new SalesOrderLineRequest(noPrice.PublicId, 1m, UnitPrice: 4m));
        Assert.Equal((8.25m, 10m, true, PriceSources.GeneralList), (o.Lines[0].UnitPrice, o.Lines[0].ResolvedUnitPrice!.Value, o.Lines[0].IsManualPrice, o.Lines[0].PriceSource!));
        Assert.Equal((4m, (decimal?)null, true, PriceSources.None), (o.Lines[1].UnitPrice, o.Lines[1].ResolvedUnitPrice, o.Lines[1].IsManualPrice, o.Lines[1].PriceSource!));
        var stored = await StoredLineAsync(x, o.PublicId);
        Assert.Equal((8.25m, 10m), (stored.UnitPrice, stored.ResolvedUnitPrice!.Value));
        // el vendedor que manda el precio de lista no es manual
        var same = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m, UnitPrice: 10m));
        Assert.False(same.Lines[0].IsManualPrice);
    }

    [Fact]
    public async Task Changing_the_client_recalculates_the_copy()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 20m);
        var b = await x.ProductAsync("B", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m), (b, 10m));
        var special = await x.ClientSpecialAsync(x.C2, x.Today.AddDays(-1), null, (a, 6m), (b, 6m));

        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m), new SalesOrderLineRequest(b.PublicId, 1m, UnitPrice: 12m));
        Assert.Equal(new[] { PriceSources.GeneralList, PriceSources.GeneralList }, o.Lines.Select(l => l.PriceSource!).ToArray());
        var moved = await x.Sales.UpdateAsync(o.PublicId, new SalesOrderRequest(ClientPublicId: x.C2.PublicId), default);
        Assert.Equal(new[] { 6m, 12m }, moved.Lines.Select(l => l.UnitPrice).ToArray());
        Assert.All(moved.Lines, l => Assert.Equal((PriceSources.ClientList, special.PublicId, 6m), (l.PriceSource!, l.PriceListPublicId!.Value, l.ResolvedUnitPrice!.Value)));
        Assert.Equal(new[] { false, true }, moved.Lines.Select(l => l.IsManualPrice).ToArray());
        var stored = await StoredLineAsync(x, o.PublicId, 2);
        Assert.Equal((PriceSources.ClientList, special.PriceListId, 6m), (stored.PriceSource, stored.PriceListId!.Value, stored.ResolvedUnitPrice!.Value));
    }

    [Fact]
    public async Task Modifying_a_requested_line_updates_the_copy_and_the_request_keeps_it()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 20m, stock: 20m);
        var list = await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m));
        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m));
        o = await x.Sales.RequestAsync(o.PublicId, null, default);
        Assert.Equal((PriceSources.GeneralList, 10m), (o.Lines[0].PriceSource!, o.Lines[0].ResolvedUnitPrice!.Value));   // solicitar no reescribe la copia

        // la lista baja y aparece un precio especial más barato; la orden solicitada conserva su precio y su copia
        await x.Lists.SetItemPriceAsync(list.PublicId, list.Items[0].Id, new PriceListItemPriceRequest(9m), default);
        var special = await x.ClientSpecialAsync(x.C1, x.Today.AddDays(-1), null, (a, 8m));
        var before = await x.Sales.GetAsync(o.PublicId, default);
        Assert.Equal((10m, 10m, PriceSources.GeneralList), (before.Lines[0].UnitPrice, before.Lines[0].ResolvedUnitPrice!.Value, before.Lines[0].PriceSource!));

        // pasarla al precio de hoy actualiza la copia con la resolución de hoy
        var mod = await x.Sales.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(UnitPrice: 8m), default);
        Assert.Equal((8m, 8m, PriceSources.ClientList, special.PublicId, false), (mod.Lines[0].UnitPrice, mod.Lines[0].ResolvedUnitPrice!.Value, mod.Lines[0].PriceSource!,
            mod.Lines[0].PriceListPublicId!.Value, mod.Lines[0].IsManualPrice));
        // un precio a mano (con sales.price) deja la resolución de hoy al lado
        var manual = await x.Sales.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(UnitPrice: 7.5m), default);
        Assert.Equal((7.5m, 8m, true), (manual.Lines[0].UnitPrice, manual.Lines[0].ResolvedUnitPrice!.Value, manual.Lines[0].IsManualPrice));
        // cambiar solo la cantidad no toca la copia
        var qty = await x.Sales.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(Quantity: 2m), default);
        Assert.Equal((7.5m, 8m, PriceSources.ClientList), (qty.Lines[0].UnitPrice, qty.Lines[0].ResolvedUnitPrice!.Value, qty.Lines[0].PriceSource!));
    }

    [Fact]
    public async Task A_requested_order_shows_its_origin_even_if_the_list_changes()
    {
        await using var x = await PriceWorld.CreateAsync();
        var walgreens = await x.ChainAsync("Walgreens");
        var w1 = await x.ClientInAsync("W1", walgreens);
        var a = await x.ProductAsync("A", 20m, stock: 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m));
        var chainList = await x.ChainListAsync("Walgreens", x.Today.AddDays(-1), null, walgreens, special: true, (a, 7m));
        var o = await x.DraftAsync(w1, new SalesOrderLineRequest(a.PublicId, 1m));
        o = await x.Sales.RequestAsync(o.PublicId, null, default);

        // la lista se desactiva y se le cambia el nombre; el cliente sale de la cadena: la orden sigue diciendo de dónde salió (con el nombre de hoy)
        var row = await x.F.Db.Set<PriceList>().AsTracking().SingleAsync(l => l.PriceListId == chainList.PriceListId);
        (row.IsActive, row.Name) = (false, "Walgreens 2026");
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        await x.MoveToChainAsync(w1, null);

        var got = await x.Sales.GetAsync(o.PublicId, default);
        var l = got.Lines[0];
        Assert.Equal((7m, PriceSources.ChainList, chainList.PublicId, "Walgreens 2026", walgreens.PublicId, "Walgreens", 7m, false),
            (l.UnitPrice, l.PriceSource!, l.PriceListPublicId!.Value, l.PriceListName, l.PriceChainPublicId!.Value, l.PriceChainName, l.ResolvedUnitPrice!.Value, l.IsManualPrice));
    }

    [Fact]
    public async Task A_line_with_null_columns_has_no_origin()
    {
        await using var x = await PriceWorld.CreateAsync();
        var a = await x.ProductAsync("A", 20m);
        await x.PublishedListAsync("General", x.Today.AddDays(-5), null, null, (a, 10m));
        var o = await x.DraftAsync(x.C1, new SalesOrderLineRequest(a.PublicId, 1m));
        // como una línea de antes del Lote 247
        var id = await x.F.Db.Set<SalesOrder>().Where(s => s.PublicId == o.PublicId).Select(s => s.SalesOrderId).SingleAsync();
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync(l => l.SalesOrderId == id);
        (line.PriceSource, line.PriceListId, line.OfferId, line.PriceChainId, line.ResolvedUnitPrice, line.UnitPrice) = (null, null, null, null, null, 9.99m);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var l = (await x.Sales.GetAsync(o.PublicId, default)).Lines[0];
        Assert.Equal(((string?)null, (Guid?)null, (string?)null, (Guid?)null, (Guid?)null, (decimal?)null, false),
            (l.PriceSource, l.PriceListPublicId, l.PriceListName, l.OfferPublicId, l.PriceChainPublicId, l.ResolvedUnitPrice, l.IsManualPrice));
    }
}
