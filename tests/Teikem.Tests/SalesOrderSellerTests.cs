using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;
using K = Teikem.Domain.Sales.SalesRouteCodes;
using R = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas, P5) — el vendedor de la orden de venta, con SalesVendorWorld + SalesRouteTestSeed (datos INVENTADOS):
/// - borrador: SellerUserId NULL y el DTO trae el PROPUESTO (preferido activo → titular activo → quien crea, solo si es de la ruta → ninguno);
/// - escoger en el borrador solo entre el preferido y las personas vigentes de la ruta (400 SellerNotAllowed exacto); cambiar el cliente vacía la elección que ya no vale;
/// - solicitar CONGELA ruta, vendedor y SalesCopyAtUtc; después nada de la ruta, el titular o el preferido la cambia; cancelar la conserva; PATCH de una solicitada = 422;
/// - filtros routeId / sellerUserId de la lista (sin órdenes de otra compañía) y las opciones de seller-options.
/// </summary>
public sealed class SalesOrderSellerTests
{
    private const int T = WmsFixture.TenantId;
    private static readonly DateOnly D1 = new(2026, 10, 1);

    /// <summary>Ruta Norte con titular 201 y apoyo 202 (activos), apoyo 203 (suspendido), oficina 204 (activa, sin ruta); cliente C1 en Norte.</summary>
    private sealed record Mundo(SalesVendorWorld X, SalesRoute Norte, Teikem.Domain.Clients.Client C1, Teikem.Domain.Wms.Product P);

    private static async Task<Mundo> MundoAsync(int? preferred = null, bool withPrimary = true)
    {
        var x = await SalesVendorWorld.CreateAsync();
        var f = x.F;
        await f.AddActiveMemberAsync(201, "Titular Norte");
        await f.AddActiveMemberAsync(202, "Apoyo Norte");
        await f.AddSuspendedMemberAsync(203, "Apoyo Suspendido");
        await f.AddActiveMemberAsync(204, "Oficina");
        var norte = await f.AddRouteAsync("Norte");
        if (withPrimary) await f.AddAssignmentAsync(norte.SalesRouteId, 201, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 203, false, D1);
        var c1 = await x.ClientAsync("C1");
        await f.SetClientSalesAsync(c1.ClientId, routeId: norte.SalesRouteId, preferredUserId: preferred);
        var p = await x.ProductAsync("P1", 10m);
        return new Mundo(x, norte, c1, p);
    }

    private static async Task<SalesOrder> RowAsync(SalesVendorWorld x, Guid publicId)
        => await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == publicId);

    private static async Task SuspendAsync(WmsFixture f, int userId)
    {
        var suspended = await SalesRouteTestSeed.MembershipStatusIdAsync(f, MembershipStatuses.Suspended);
        var m = await f.Db.UserTenants.IgnoreQueryFilters().AsTracking().SingleAsync(u => u.UserId == userId && u.TenantId == T);
        m.StatusCodeId = suspended;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Borrador_nuevo_sin_eleccion_trae_el_preferido_como_propuesto()
    {
        var m = await MundoAsync(preferred: 202);
        await using var x = m.X;
        var dto = await x.DraftAsync(m.C1, m.P, 1m);

        Assert.Null((await RowAsync(x, dto.PublicId)).SellerUserId);
        Assert.Null((await RowAsync(x, dto.PublicId)).SalesCopyAtUtc);
        Assert.Equal(202, dto.SellerUserId);
        Assert.Equal("Apoyo Norte", dto.SellerName);
        Assert.True(dto.SellerProposed);
        Assert.Equal(m.Norte.SalesRouteId, dto.SalesRouteId);
        Assert.Equal("Norte", dto.SalesRouteName);
        Assert.Null(dto.SalesCopyAtUtc);
    }

    [Fact]
    public async Task Preferido_suspendido_propone_al_titular()
    {
        var m = await MundoAsync(preferred: 203);
        await using var x = m.X;
        var dto = await x.DraftAsync(m.C1, m.P, 1m);
        Assert.Equal(201, dto.SellerUserId);
        Assert.True(dto.SellerProposed);
    }

    [Fact]
    public async Task Sin_preferido_ni_titular_propone_al_apoyo_que_la_crea_y_nada_a_la_oficina()
    {
        var m = await MundoAsync(withPrimary: false);
        await using var x = m.X;

        using (x.F.Tenant.As(T, 202))
        {
            var bySupport = await x.DraftAsync(m.C1, m.P, 1m);
            Assert.Equal(202, bySupport.SellerUserId);
            Assert.True(bySupport.SellerProposed);
        }
        using (x.F.Tenant.As(T, 204))
        {
            var byOffice = await x.DraftAsync(m.C1, m.P, 1m);
            Assert.Null(byOffice.SellerUserId);
            Assert.Null(byOffice.SellerName);
            Assert.False(byOffice.SellerProposed);
            Assert.Equal("Norte", byOffice.SalesRouteName);

            // al solicitar queda «Sin vendedor» (la oficina no queda como vendedora), con la ruta copiada
            var requested = await x.Sales.RequestAsync(byOffice.PublicId, null, default);
            Assert.Null(requested.SellerUserId);
            Assert.Equal(m.Norte.SalesRouteId, requested.SalesRouteId);
            Assert.NotNull(requested.SalesCopyAtUtc);
        }
    }

    [Fact]
    public async Task Solicitar_congela_la_copia_y_despues_nada_la_cambia_ni_cancelar()
    {
        var m = await MundoAsync(preferred: 202);
        await using var x = m.X;
        var f = x.F;
        var draft = await x.DraftAsync(m.C1, m.P, 1m);

        var requested = await x.Sales.RequestAsync(draft.PublicId, null, default);
        Assert.Equal(SalesOrderStatuses.Requested, requested.Status);
        var row = await RowAsync(x, draft.PublicId);
        Assert.Equal(m.Norte.SalesRouteId, row.SalesRouteId);
        Assert.Equal(202, row.SellerUserId);
        Assert.NotNull(row.SalesCopyAtUtc);
        Assert.Equal(202, requested.SellerUserId);
        Assert.Equal("Apoyo Norte", requested.SellerName);
        Assert.Equal("Norte", requested.SalesRouteName);
        Assert.False(requested.SellerProposed);
        Assert.Equal(row.SalesCopyAtUtc, requested.SalesCopyAtUtc);

        // cambia todo alrededor: otra ruta para el cliente, otro preferido, el titular cerrado
        await f.AddActiveMemberAsync(301, "Titular Sur");
        var sur = await f.AddRouteAsync("Sur");
        await f.AddAssignmentAsync(sur.SalesRouteId, 301, true, D1);
        await f.SetClientSalesAsync(m.C1.ClientId, routeId: sur.SalesRouteId, preferredUserId: 301);
        await SuspendAsync(f, 202);

        var again = await x.Sales.GetAsync(draft.PublicId, default);
        Assert.Equal(m.Norte.SalesRouteId, again.SalesRouteId);
        Assert.Equal(202, again.SellerUserId);
        Assert.False(again.SellerProposed);

        // PATCH de una solicitada = 422 y no toca la copia
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(SellerUserId: 301), default));
        await Assert.ThrowsAsync<StatusRuleException>(() => x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(ClearSeller: true), default));

        // cancelar conserva la copia
        var cancelled = await x.Sales.CancelAsync(draft.PublicId, "prueba", default);
        Assert.Equal(SalesOrderStatuses.Cancelled, cancelled.Status);
        var after = await RowAsync(x, draft.PublicId);
        Assert.Equal(m.Norte.SalesRouteId, after.SalesRouteId);
        Assert.Equal(202, after.SellerUserId);
        Assert.Equal(row.SalesCopyAtUtc, after.SalesCopyAtUtc);
    }

    [Fact]
    public async Task Patch_con_vendedor_fuera_de_la_ruta_da_400_exacto_y_con_un_apoyo_lo_guarda()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var draft = await x.DraftAsync(m.C1, m.P, 1m);
        Assert.Equal(201, draft.SellerUserId);   // propuesto: el titular
        Assert.True(draft.SellerProposed);

        var office = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(SellerUserId: 204), default));
        Assert.Equal(R.SellerNotAllowed, office.Message);
        Assert.Equal("El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.", office.Message);
        var suspended = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(SellerUserId: 203), default));
        Assert.Equal(R.SellerNotAllowed, suspended.Message);
        Assert.Null((await RowAsync(x, draft.PublicId)).SellerUserId);

        var chosen = await x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(SellerUserId: 202), default);
        Assert.Equal(202, chosen.SellerUserId);
        Assert.False(chosen.SellerProposed);
        Assert.Equal(202, (await RowAsync(x, draft.PublicId)).SellerUserId);

        // un PATCH que no lo menciona no lo cambia; ClearSeller vuelve a la propuesta
        var notes = await x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(Notes: "sin cambio de vendedor"), default);
        Assert.Equal(202, notes.SellerUserId);
        var cleared = await x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(ClearSeller: true), default);
        Assert.Null((await RowAsync(x, draft.PublicId)).SellerUserId);
        Assert.Equal(201, cleared.SellerUserId);
        Assert.True(cleared.SellerProposed);

        // al crear también se valida
        var bad = await Assert.ThrowsAsync<ValidationException>(() => x.DraftAsync(m.C1, m.P, 1m, new SalesOrderRequest(SellerUserId: 204)));
        Assert.Equal(R.SellerNotAllowed, bad.Message);
        var good = await x.DraftAsync(m.C1, m.P, 1m, new SalesOrderRequest(SellerUserId: 202));
        Assert.Equal(202, (await RowAsync(x, good.PublicId)).SellerUserId);
        Assert.False(good.SellerProposed);
    }

    [Fact]
    public async Task Cambiar_el_cliente_vacia_la_eleccion_que_ya_no_vale()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var f = x.F;
        await f.AddActiveMemberAsync(301, "Titular Sur");
        var sur = await f.AddRouteAsync("Sur");
        await f.AddAssignmentAsync(sur.SalesRouteId, 301, true, D1);
        await f.AddAssignmentAsync(sur.SalesRouteId, 201, false, D1);   // el titular de Norte también apoya en Sur
        var c2 = await x.ClientAsync("C2");
        await f.SetClientSalesAsync(c2.ClientId, routeId: sur.SalesRouteId);
        var c3 = await x.ClientAsync("C3");
        await f.SetClientSalesAsync(c3.ClientId, routeId: sur.SalesRouteId);

        var draft = await x.DraftAsync(m.C1, m.P, 1m, new SalesOrderRequest(SellerUserId: 202));
        var moved = await x.Sales.UpdateAsync(draft.PublicId, new SalesOrderRequest(ClientPublicId: c2.PublicId), default);
        Assert.Null((await RowAsync(x, draft.PublicId)).SellerUserId);   // 202 no es de Sur: vuelve a la propuesta sin error
        Assert.Equal(301, moved.SellerUserId);
        Assert.True(moved.SellerProposed);
        Assert.Equal("Sur", moved.SalesRouteName);

        // una elección que sí vale en el cliente nuevo se conserva
        var other = await x.DraftAsync(m.C1, m.P, 1m, new SalesOrderRequest(SellerUserId: 201));
        var kept = await x.Sales.UpdateAsync(other.PublicId, new SalesOrderRequest(ClientPublicId: c3.PublicId), default);
        Assert.Equal(201, (await RowAsync(x, other.PublicId)).SellerUserId);
        Assert.Equal(201, kept.SellerUserId);
        Assert.False(kept.SellerProposed);
    }

    [Fact]
    public async Task La_eleccion_suspendida_antes_de_solicitar_congela_la_propuesta()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var draft = await x.DraftAsync(m.C1, m.P, 1m, new SalesOrderRequest(SellerUserId: 202));
        await SuspendAsync(x.F, 202);

        var shown = await x.Sales.GetAsync(draft.PublicId, default);   // el borrador ya muestra lo que se congelaría
        Assert.Equal(201, shown.SellerUserId);
        Assert.True(shown.SellerProposed);

        var requested = await x.Sales.RequestAsync(draft.PublicId, null, default);
        Assert.Equal(201, requested.SellerUserId);
        Assert.Equal(201, (await RowAsync(x, draft.PublicId)).SellerUserId);
    }

    [Fact]
    public async Task Filtros_por_ruta_y_vendedor_sin_ordenes_de_otra_compania()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var f = x.F;
        await f.AddActiveMemberAsync(301, "Titular Sur");
        var sur = await f.AddRouteAsync("Sur");
        await f.AddAssignmentAsync(sur.SalesRouteId, 301, true, D1);
        var c2 = await x.ClientAsync("C2");
        await f.SetClientSalesAsync(c2.ClientId, routeId: sur.SalesRouteId);

        var norteOrder = await x.Sales.RequestAsync((await x.DraftAsync(m.C1, m.P, 1m)).PublicId, null, default);
        var surOrder = await x.Sales.RequestAsync((await x.DraftAsync(c2, m.P, 1m)).PublicId, null, default);
        var draftOnly = await x.DraftAsync(m.C1, m.P, 1m);   // borrador sin elección: sin ruta ni vendedor guardados

        // una orden de otra compañía con la misma ruta y vendedor (no debe aparecer)
        f.Db.Set<SalesOrder>().Add(new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, Number = "OV-OTRA", WarehouseId = x.W.WarehouseId, ClientId = m.C1.ClientId,
            RequestedDate = D1, StatusCodeId = (await RowAsync(x, norteOrder.PublicId)).StatusCodeId, SalesRouteId = m.Norte.SalesRouteId, SellerUserId = 201,
            SalesCopyAtUtc = DateTime.UtcNow,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var byNorte = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, routeId: m.Norte.SalesRouteId);
        var item = Assert.Single(byNorte.Items);
        Assert.Equal(norteOrder.PublicId, item.PublicId);
        Assert.Equal("Norte", item.SalesRouteName);
        Assert.Equal("Titular Norte", item.SellerName);

        var bySeller = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default, sellerUserId: 301);
        Assert.Equal(surOrder.PublicId, Assert.Single(bySeller.Items).PublicId);

        var all = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default);
        Assert.Equal(3, all.Total);
        var draftItem = all.Items.Single(i => i.PublicId == draftOnly.PublicId);
        Assert.Null(draftItem.SalesRouteName);
        Assert.Null(draftItem.SellerName);
    }

    [Fact]
    public async Task Opciones_de_vendedor_primero_la_ruta_sin_suspendidos_y_el_preferido_fuera_de_ruta_al_final()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        await x.F.AddActiveMemberAsync(401, "Preferida Fuera");
        await x.F.SetClientSalesAsync(m.C1.ClientId, routeId: m.Norte.SalesRouteId, preferredUserId: 401);   // dato viejo: ya no es de la ruta (no bloquea)

        var o = await x.Sales.SellerOptionsAsync(m.C1.PublicId, default);
        Assert.Equal(m.Norte.SalesRouteId, o.RouteId);
        Assert.Equal("Norte", o.RouteName);
        Assert.False(o.RouteFromRep);
        Assert.Equal(401, o.ProposedUserId);   // el preferido activo se propone primero
        Assert.Equal("Preferida Fuera", o.ProposedName);
        Assert.Equal(K.SellerKindPreferred, o.ProposedKind);
        Assert.Equal(new[] { 201, 202, 401 }, o.Options.Select(p => p.UserId).ToArray());
        Assert.Equal(new[] { K.SellerKindPrimary, K.SellerKindSupport, K.SellerKindPreferred }, o.Options.Select(p => p.Kind).ToArray());
        Assert.True(o.Options.Single(p => p.UserId == 401).IsPreferred);
        Assert.True(o.Options.Single(p => p.UserId == 401).IsProposed);
        Assert.DoesNotContain(o.Options, p => p.UserId == 203);   // suspendido
        Assert.DoesNotContain(o.Options, p => p.Name.Contains('@'));   // sin correos

        await Assert.ThrowsAsync<ValidationException>(() => x.Sales.SellerOptionsAsync(null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Sales.SellerOptionsAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void RequestAsync_congela_antes_de_la_transicion_y_dentro_del_reintento()
    {
        var path = Path.Combine(TripCatalogTests.RepoRoot(), "src", "Teikem.Infrastructure", "Services", "SalesOrderService.Request.cs");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var start = text.IndexOf("public async Task<SalesOrderDto> RequestAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = Regex.Match(text[(start + 10)..], @"\n    (public|private|internal|protected)\b").Index + start + 10;
        var body = text[start..end];

        var loop = body.IndexOf("for (var attempt", StringComparison.Ordinal);
        var tx = body.IndexOf("RunInTransactionAsync(", StringComparison.Ordinal);
        var freeze = body.IndexOf("FreezeSellerAsync(", StringComparison.Ordinal);
        var transition = body.IndexOf("statuses.TransitionAsync(", StringComparison.Ordinal);
        var notify = body.IndexOf("await NotifyRequestedAsync(", StringComparison.Ordinal);
        var retry = body.IndexOf("catch (InsufficientStockException)", StringComparison.Ordinal);
        Assert.True(loop >= 0 && tx > loop, "solicitar reintenta dentro de una transacción");
        Assert.True(freeze > tx, "la copia se congela dentro de la transacción (y del reintento)");
        Assert.True(freeze < transition, "FreezeSellerAsync va antes de TransitionAsync");
        Assert.True(transition < notify && notify < retry, "el aviso del Lote G va después de la transición y dentro del intento");
        Assert.Equal(1, Regex.Matches(text, @"FreezeSellerAsync\(").Count);   // un solo lugar la congela
    }
}
