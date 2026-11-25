using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Infrastructure.Exceptions;
using Xunit;
using R = Teikem.Domain.Clients.ClientChainRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan, P5) — columna y filtro «Cadena» en Clientes y en Órdenes de venta, en memoria con SalesVendorWorld (datos INVENTADOS):
/// - GET /clients: chainPublicId (solo esa cadena), noChain (solo «Sin cadena»), ninguno (todos, con su cadena); los dos a la vez → 400 exacto; una cadena de otra
///   compañía o inexistente → lista vacía (sin 404, sin revelar nada);
/// - la ficha trae ChainPublicId, ChainName y ChainIsActive (también con la cadena desactivada);
/// - GET /sales-orders: chainPublicId repetible (cualquiera de ellas), la cadena ACTUAL del cliente en vivo (mover al cliente cambia lo que muestra la orden) y
///   una lista de cadenas ajenas da resultado vacío.
/// </summary>
public sealed class ClientChainListFilterTests
{
    private sealed record Mundo(SalesVendorWorld X, ClientChain Walgreens, ClientChain Cvs, ClientChain Vieja, ClientChain Ajena,
        Client W1, Client W2Inactivo, Client C1, Client N1, Client V1);

    private static async Task<Mundo> MundoAsync()
    {
        var x = await SalesVendorWorld.CreateAsync();
        var db = x.F.Db;
        ClientChain Chain(int tenantId, string name, bool active = true)
        {
            var c = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, IsActive = active, CreatedAtUtc = DateTime.UtcNow };
            db.Set<ClientChain>().Add(c);
            return c;
        }
        var walgreens = Chain(WmsFixture.TenantId, "Walgreens");
        var cvs = Chain(WmsFixture.TenantId, "CVS");
        var vieja = Chain(WmsFixture.TenantId, "Cadena vieja", active: false);
        var ajena = Chain(WmsFixture.OtherTenantId, "Walgreens de otra compañía");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var w1 = await x.ClientAsync("W1", c => c.ChainId = walgreens.ClientChainId);
        var w2 = await x.ClientAsync("W2", c => { c.ChainId = walgreens.ClientChainId; c.IsActive = false; });
        var c1 = await x.ClientAsync("C1", c => c.ChainId = cvs.ClientChainId);
        var n1 = await x.ClientAsync("N1");
        var v1 = await x.ClientAsync("V1", c => c.ChainId = vieja.ClientChainId);
        return new Mundo(x, walgreens, cvs, vieja, ajena, w1, w2, c1, n1, v1);
    }

    private static string[] Codes(IEnumerable<Teikem.Infrastructure.Contracts.ClientListItemDto> items) => items.Select(i => i.Code).OrderBy(c => c).ToArray();

    [Fact]
    public async Task Lista_de_clientes_filtra_por_cadena_sin_cadena_y_ninguno()
    {
        var m = await MundoAsync();
        await using var x = m.X;

        var all = await x.Clients.GetListAsync(null, false, default);
        Assert.Equal(new[] { "C1", "N1", "V1", "W1" }, Codes(all));
        var w1 = all.Single(i => i.Code == "W1");
        Assert.Equal(m.Walgreens.PublicId, w1.ChainPublicId);
        Assert.Equal("Walgreens", w1.ChainName);
        Assert.Equal("Cadena vieja", all.Single(i => i.Code == "V1").ChainName);   // la desactivada se sigue viendo
        var n1 = all.Single(i => i.Code == "N1");
        Assert.Null(n1.ChainPublicId);
        Assert.Null(n1.ChainName);

        Assert.Equal(new[] { "W1" }, Codes(await x.Clients.GetListAsync(null, false, default, chainPublicId: m.Walgreens.PublicId)));
        Assert.Equal(new[] { "W1", "W2" }, Codes(await x.Clients.GetListAsync(null, true, default, chainPublicId: m.Walgreens.PublicId)));   // el inactivo conserva su cadena
        Assert.Equal(new[] { "V1" }, Codes(await x.Clients.GetListAsync(null, false, default, chainPublicId: m.Vieja.PublicId)));
        Assert.Equal(new[] { "N1" }, Codes(await x.Clients.GetListAsync(null, false, default, noChain: true)));
        // el buscador se combina con el filtro
        Assert.Empty(await x.Clients.GetListAsync("C1", false, default, chainPublicId: m.Walgreens.PublicId));
    }

    [Fact]
    public async Task Cadena_y_sin_cadena_a_la_vez_da_400_y_una_cadena_ajena_da_lista_vacia()
    {
        var m = await MundoAsync();
        await using var x = m.X;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Clients.GetListAsync(null, false, default, chainPublicId: m.Walgreens.PublicId, noChain: true));
        Assert.Equal(R.ChainOrNoChain, ex.Message);
        Assert.Equal("Escoja una cadena o «Sin cadena», no las dos.", ex.Message);
        Assert.Equal(400, ex.StatusCode);

        Assert.Empty(await x.Clients.GetListAsync(null, true, default, chainPublicId: m.Ajena.PublicId));
        Assert.Empty(await x.Clients.GetListAsync(null, true, default, chainPublicId: Guid.NewGuid()));
    }

    [Fact]
    public async Task Ficha_trae_la_cadena_y_si_esta_activa()
    {
        var m = await MundoAsync();
        await using var x = m.X;

        var w1 = await x.Clients.GetAsync(m.W1.PublicId, default);
        Assert.Equal(m.Walgreens.PublicId, w1.ChainPublicId);
        Assert.Equal("Walgreens", w1.ChainName);
        Assert.True(w1.ChainIsActive);

        var v1 = await x.Clients.GetAsync(m.V1.PublicId, default);
        Assert.Equal("Cadena vieja", v1.ChainName);
        Assert.False(v1.ChainIsActive);

        var n1 = await x.Clients.GetAsync(m.N1.PublicId, default);
        Assert.Null(n1.ChainPublicId);
        Assert.Null(n1.ChainName);
        Assert.False(n1.ChainIsActive);
    }

    [Fact]
    public async Task Ordenes_de_venta_filtran_por_varias_cadenas_y_muestran_la_cadena_actual_en_vivo()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var p = await x.ProductAsync("P1", 10m);
        var ow = await x.DraftAsync(m.W1, p, 1m);
        var oc = await x.DraftAsync(m.C1, p, 1m);
        var on = await x.DraftAsync(m.N1, p, 1m);

        var all = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default);
        Assert.Equal(3, all.Total);
        var itemW = all.Items.Single(i => i.PublicId == ow.PublicId);
        Assert.Equal("Walgreens", itemW.ChainName);
        Assert.Equal(m.Walgreens.PublicId, itemW.ChainPublicId);
        Assert.Null(all.Items.Single(i => i.PublicId == on.PublicId).ChainName);

        var byW = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Walgreens.PublicId });
        Assert.Equal(ow.PublicId, Assert.Single(byW.Items).PublicId);
        Assert.Equal(1, byW.Total);

        var byBoth = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Walgreens.PublicId, m.Cvs.PublicId });
        Assert.Equal(new[] { ow.PublicId, oc.PublicId }.OrderBy(g => g), byBoth.Items.Select(i => i.PublicId).OrderBy(g => g));

        // una cadena ajena o inexistente: resultado vacío (no «todas»)
        var foreign = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Ajena.PublicId, Guid.NewGuid() });
        Assert.Empty(foreign.Items);
        Assert.Equal(0, foreign.Total);
        // una ajena junto a una propia: vale la propia
        Assert.Single((await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Ajena.PublicId, m.Cvs.PublicId })).Items);

        // en vivo: mover a W1 a CVS cambia lo que muestra (y filtra) su orden, sin copia en la orden
        var row = await x.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == m.W1.ClientId);
        row.ChainId = m.Cvs.ClientChainId;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var moved = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Cvs.PublicId });
        Assert.Equal(2, moved.Total);
        Assert.All(moved.Items, i => Assert.Equal("CVS", i.ChainName));
        Assert.Empty((await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, chainPublicIds: new[] { m.Walgreens.PublicId })).Items);
    }

    [Fact]
    public async Task Otra_compania_no_ve_las_cadenas_de_la_primera()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var otherClient = await x.F.AddClientAsync("B1", WmsFixture.OtherTenantId);
        var row = await x.F.Db.Clients.IgnoreQueryFilters().AsTracking().SingleAsync(c => c.ClientId == otherClient.ClientId);
        row.ChainId = m.Ajena.ClientChainId;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Empty(await x.Clients.GetListAsync(null, true, default, chainPublicId: m.Walgreens.PublicId));
            var mine = Assert.Single(await x.Clients.GetListAsync(null, true, default, chainPublicId: m.Ajena.PublicId));
            Assert.Equal("B1", mine.Code);
            Assert.Equal("Walgreens de otra compañía", mine.ChainName);
        }
    }
}
