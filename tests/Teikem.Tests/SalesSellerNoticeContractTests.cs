using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas, P8) — contrato con el Lote G: el vendedor de la orden en el correo «Orden de venta solicitada» y en los avisos,
/// con SalesVendorWorld + SalesRouteTestSeed (datos INVENTADOS; el usuario de la sesión de prueba es el 1, «Luis Rivera»):
/// - orden con copia: SELLER = SellerUserId si es persona activa, el nombre es el del usuario y «Capturó: X» cuando la solicitó otra persona;
/// - vendedor suspendido, inactivo o copia vacía → Empty con «La orden no tiene un vendedor con usuario activo en la compañía.» (aunque el cliente tenga
///   representante: la copia manda);
/// - orden anterior al lote (sin copia) → la regla provisional del Lote 243 con la regla de persona activa (un miembro suspendido ya no recibe);
/// - {seller} de los avisos de la orden (solicitada y urgente) = el vendedor copiado; si no hay, quien actúa.
/// </summary>
public sealed class SalesSellerNoticeContractTests
{
    private const int T = WmsFixture.TenantId;
    private static readonly DateOnly D1 = new(2026, 10, 1);

    /// <summary>Ruta Norte con titular 201 (o el que se pida); 202 es representante (con usuario) del cliente C1 en Norte; 1 = quien solicita (la sesión).</summary>
    private sealed record Mundo(SalesVendorWorld X, SalesOrderEmailContent P, SalesRoute Norte, Teikem.Domain.Clients.Client C1, Teikem.Domain.Wms.Product Prod);

    private static async Task<Mundo> MundoAsync(int primaryUserId = 201)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:WebBaseUrl"] = "https://app.teikem.test/" }).Build();
        var x = await SalesVendorWorld.CreateAsync(s =>
        {
            s.AddSingleton<IConfiguration>(config);
            s.AddSingleton<SalesOrderEmailContent>();
        });
        var f = x.F;
        await f.AddActiveMemberAsync(1, "Luis Rivera");
        await f.AddActiveMemberAsync(201, "Titular Norte");
        await f.AddActiveMemberAsync(202, "Ana Representante");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, primaryUserId, true, D1);
        var rep = await f.AddRepAsync("Ana (ruta norte)", norte.SalesRouteId, 202);
        var c1 = await x.ClientAsync("C1");
        await f.SetClientSalesAsync(c1.ClientId, routeId: norte.SalesRouteId, repId: rep.SalesRepId);
        var prod = await x.ProductAsync("MED", 10m);
        return new Mundo(x, x.F.Get<SalesOrderEmailContent>(), norte, c1, prod);
    }

    private static Dictionary<string, string> ValuesOf(SalesOrderDto o) => new() { ["orderNumber"] = o.Number, ["orderPublicId"] = o.PublicId.ToString("N") };

    private static Task<NotificationContextResolution> SellerRecipientsAsync(Mundo m, SalesOrderDto o)
        => m.P.RecipientsAsync(T, SalesEventCodes.OrderRequested, SalesContextRecipients.Seller, ValuesOf(o), default);

    private static async Task<SalesOrderSellerInfo> SellerAsync(Mundo m, SalesOrderDto o)
    {
        var order = await m.X.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(r => r.PublicId == o.PublicId);
        return await SalesOrderSeller.ResolveAsync(m.X.F.Db, T, order, default);
    }

    private static async Task<SalesOrderDto> RequestedAsync(Mundo m, Teikem.Domain.Clients.Client c, SalesOrderRequest? extra = null)
        => await m.X.Sales.RequestAsync((await m.X.DraftAsync(c, m.Prod, 1m, extra)).PublicId, null, default);

    private static async Task<Dictionary<string, string>> PayloadAsync(Mundo m, string eventCode, SalesOrderDto o)
    {
        var rows = await m.X.F.Db.NotificationOutbox.AsNoTracking().Where(r => r.EventCode == eventCode).ToListAsync();
        return rows.Select(r => NotificationRules.ParseValues(r.PayloadJson)).Single(v => v.GetValueOrDefault("orderNumber") == o.Number);
    }

    private static async Task SuspendAsync(WmsFixture f, int userId)
    {
        var suspended = await SalesRouteTestSeed.MembershipStatusIdAsync(f, MembershipStatuses.Suspended);
        var row = await f.Db.UserTenants.IgnoreQueryFilters().AsTracking().SingleAsync(u => u.UserId == userId && u.TenantId == T);
        row.StatusCodeId = suspended;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task DeactivateUserAsync(WmsFixture f, int userId)
    {
        var u = await f.Db.Users.AsTracking().SingleAsync(x => x.Id == userId);
        u.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task LegacyAsync(WmsFixture f, SalesOrderDto o)
    {
        var row = await f.Db.Set<SalesOrder>().AsTracking().SingleAsync(r => r.PublicId == o.PublicId);
        (row.SalesRouteId, row.SellerUserId, row.SalesCopyAtUtc) = (null, null, null);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Orden_con_copia_el_vendedor_es_el_copiado_con_su_nombre_y_Capturo_quien_la_solicito()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var r = await RequestedAsync(m, m.C1);
        Assert.Equal(201, r.SellerUserId);

        var info = await SellerAsync(m, r);
        Assert.Equal(("Titular Norte", "Luis Rivera"), (info.Name, info.CapturedByName));
        Assert.Equal(new int?[] { 201 }, info.Users.Select(u => u.UserId).ToArray());

        // SOLO el vendedor de la orden: ni quien la solicitó ni el representante del cliente
        var res = await SellerRecipientsAsync(m, r);
        Assert.Null(res.EmptyReason);
        var person = Assert.Single(res.People);
        Assert.Equal((201, "Titular Norte", "titular.norte.201@x.test", NotificationAudience.Staff), (person.UserId, person.Name, person.Email, person.Audience));

        // el correo: {seller} = el vendedor copiado y «Capturó: Luis Rivera»
        var content = await m.P.BuildAsync(new NotificationContentRequest(T, SalesEventCodes.OrderRequested, ValuesOf(r), NotificationAudience.Staff, "es"), default);
        Assert.Equal("Titular Norte", content!.Values["seller"]);
        Assert.Contains("Capturó: Luis Rivera", content.Blocks["orderDetail"].Html);

        // el titular cambia después: la orden sigue con su vendedor copiado
        await m.X.F.AddActiveMemberAsync(301, "Titular Nuevo");
        var nuevo = await m.X.F.AddRouteAsync("Metro");
        await m.X.F.AddAssignmentAsync(nuevo.SalesRouteId, 301, true, D1);
        await m.X.F.SetClientSalesAsync(m.C1.ClientId, routeId: nuevo.SalesRouteId, preferredUserId: 301);
        Assert.Equal(new int?[] { 201 }, (await SellerRecipientsAsync(m, r)).People.Select(p => p.UserId).ToArray());
    }

    [Fact]
    public async Task Si_quien_la_solicita_es_el_vendedor_no_sale_Capturo()
    {
        var m = await MundoAsync(primaryUserId: 1);
        await using var x = m.X;
        var r = await RequestedAsync(m, m.C1);

        var info = await SellerAsync(m, r);
        Assert.Equal(("Luis Rivera", (string?)null), (info.Name, info.CapturedByName));
        Assert.Equal(new int?[] { 1 }, (await SellerRecipientsAsync(m, r)).People.Select(p => p.UserId).ToArray());
    }

    [Fact]
    public async Task Vendedor_suspendido_inactivo_o_copia_vacia_da_Empty_con_el_motivo()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var f = x.F;

        // vendedor suspendido después de solicitar: el nombre se conserva, pero no recibe (SKIPPED con el motivo)
        var suspended = await RequestedAsync(m, m.C1);
        await SuspendAsync(f, 201);
        var res = await SellerRecipientsAsync(m, suspended);
        Assert.Empty(res.People);
        Assert.Equal("La orden no tiene un vendedor con usuario activo en la compañía.", res.EmptyReason);
        Assert.Equal(SalesOrderEmailContent.NoSellerReason, res.EmptyReason);
        Assert.Equal("Titular Norte", (await SellerAsync(m, suspended)).Name);

        // vendedor con el usuario desactivado: igual
        await f.AddActiveMemberAsync(205, "Titular Sur");
        var sur = await f.AddRouteAsync("Sur");
        await f.AddAssignmentAsync(sur.SalesRouteId, 205, true, D1);
        var c3 = await x.ClientAsync("C3");
        await f.SetClientSalesAsync(c3.ClientId, routeId: sur.SalesRouteId);
        var inactive = await RequestedAsync(m, c3);
        Assert.Equal(205, inactive.SellerUserId);
        await DeactivateUserAsync(f, 205);
        Assert.Equal(SalesOrderEmailContent.NoSellerReason, (await SellerRecipientsAsync(m, inactive)).EmptyReason);

        // copia vacía («Sin vendedor»): un cliente sin ruta, solicitado por la oficina. Aunque tenga representante con usuario activo, la copia manda
        var c4 = await x.ClientAsync("C4");
        var rep = await f.AddRepAsync("Ana sin ruta", null, 202);
        await f.SetClientSalesAsync(c4.ClientId, repId: rep.SalesRepId);
        var empty = await RequestedAsync(m, c4);
        var row = await f.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == empty.PublicId);
        Assert.Null(row.SellerUserId);
        Assert.NotNull(row.SalesCopyAtUtc);
        res = await SellerRecipientsAsync(m, empty);
        Assert.Empty(res.People);
        Assert.Equal(SalesOrderEmailContent.NoSellerReason, res.EmptyReason);
        var info = await SellerAsync(m, empty);
        Assert.Equal(((string?)null, "Luis Rivera"), (info.Name, info.CapturedByName));
    }

    [Fact]
    public async Task Orden_anterior_al_lote_conserva_la_regla_provisional_con_la_regla_de_persona_activa()
    {
        var m = await MundoAsync();
        await using var x = m.X;
        var r = await RequestedAsync(m, m.C1);
        await LegacyAsync(x.F, r);

        // sin copia: quien la solicitó y el usuario del representante del cliente, con el nombre del representante
        var info = await SellerAsync(m, r);
        Assert.Equal(("Ana (ruta norte)", "Luis Rivera"), (info.Name, info.CapturedByName));
        Assert.Equal(new int?[] { 1, 202 }, (await SellerRecipientsAsync(m, r)).People.Select(p => p.UserId).ToArray());

        // un miembro suspendido ya no recibe (la misma regla que el vendedor propuesto)
        await SuspendAsync(x.F, 202);
        Assert.Equal(new int?[] { 1 }, (await SellerRecipientsAsync(m, r)).People.Select(p => p.UserId).ToArray());
        await SuspendAsync(x.F, 1);
        var res = await SellerRecipientsAsync(m, r);
        Assert.Empty(res.People);
        Assert.Equal(SalesOrderEmailContent.NoSellerReason, res.EmptyReason);
    }

    [Fact]
    public async Task Seller_de_los_avisos_es_el_vendedor_copiado_y_si_no_hay_quien_actua()
    {
        var m = await MundoAsync();
        await using var x = m.X;

        var copied = await RequestedAsync(m, m.C1, new SalesOrderRequest(IsUrgent: true, Notes: "Antes del mediodía"));
        Assert.Equal("Titular Norte", (await PayloadAsync(m, SalesEventCodes.OrderUrgent, copied))["seller"]);
        Assert.Equal("Titular Norte", (await PayloadAsync(m, SalesEventCodes.OrderRequested, copied))["seller"]);

        // sin vendedor copiado (cliente sin ruta ni representante): {seller} = quien actúa
        var c5 = await x.ClientAsync("C5");
        var noSeller = await RequestedAsync(m, c5, new SalesOrderRequest(IsUrgent: true));
        Assert.Null(noSeller.SellerUserId);
        Assert.Equal("Luis Rivera", (await PayloadAsync(m, SalesEventCodes.OrderUrgent, noSeller))["seller"]);
        Assert.Equal("Luis Rivera", (await PayloadAsync(m, SalesEventCodes.OrderRequested, noSeller))["seller"]);
    }
}
