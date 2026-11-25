using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — la ficha general del cliente (GET /clients/{publicId}, ClientService.GetAsync) muestra la ruta EFECTIVA: la directa o, si
/// no tiene, la de su representante (SalesRouteFromRep = true), más la ruta directa y el vendedor preferido. Los campos del Lote 238/241 (representante) siguen igual.
/// Datos INVENTADOS.
/// </summary>
public sealed class ClientSalesDetailTests
{
    private static readonly DateOnly D1 = new(2026, 10, 1);

    [Fact]
    public async Task Ficha_con_ruta_directa_representante_y_preferido()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var f = w.F;
        await f.AddActiveMemberAsync(9101, "Vendedora Preferida");
        var norte = await f.AddRouteAsync("Norte");
        var metro = await f.AddRouteAsync("Metro");
        await f.AddAssignmentAsync(norte.SalesRouteId, 9101, true, D1);
        var rep = await f.AddRepAsync("Rep Metro", metro.SalesRouteId, null);
        var c = await w.ClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, routeId: norte.SalesRouteId, repId: rep.SalesRepId, preferredUserId: 9101);

        var d = await w.Clients.GetAsync(c.PublicId, default);
        Assert.Equal("Norte", d.SalesRouteName);   // la directa gana sobre la del representante
        Assert.False(d.SalesRouteFromRep);
        Assert.Equal(norte.SalesRouteId, d.DirectSalesRouteId);
        Assert.Equal(9101, d.PreferredSalesUserId);
        Assert.Equal("Vendedora Preferida", d.PreferredSalesUserName);
        Assert.Equal(rep.SalesRepId, d.SalesRepId);   // Lote 238: el representante sigue igual
        Assert.Equal("Rep Metro", d.SalesRepName);
    }

    [Fact]
    public async Task Ficha_sin_ruta_directa_hereda_la_del_representante_y_sin_nada_queda_vacia()
    {
        await using var w = await SalesVendorWorld.CreateAsync();
        var f = w.F;
        var metro = await f.AddRouteAsync("Metro");
        var rep = await f.AddRepAsync("Rep Metro", metro.SalesRouteId, null);
        var c = await w.ClientAsync("C1");
        await f.SetClientSalesAsync(c.ClientId, repId: rep.SalesRepId);

        var d = await w.Clients.GetAsync(c.PublicId, default);
        Assert.Equal("Metro", d.SalesRouteName);
        Assert.True(d.SalesRouteFromRep);
        Assert.Null(d.DirectSalesRouteId);
        Assert.Null(d.PreferredSalesUserId);
        Assert.Null(d.PreferredSalesUserName);
        Assert.Equal("Rep Metro", d.SalesRepName);

        var vacio = await w.ClientAsync("C2");
        var e = await w.Clients.GetAsync(vacio.PublicId, default);
        Assert.Null(e.SalesRouteName);
        Assert.False(e.SalesRouteFromRep);
        Assert.Null(e.SalesRepId);
        Assert.Null(e.SalesRepName);
    }
}
