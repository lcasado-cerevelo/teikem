using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — ClientDocumentNoticeContent (InMemory, datos INVENTADOS): CLIENT_SELLERS con NoticeUsersAsync y su motivo, CLIENT con el interruptor de la compañía, la
/// fila del cliente, principal + marcados + extra y el idioma del cliente; BuildAsync salta el aviso que ya no aplica; otra compañía no resuelve nada; el ChangeTracker
/// de quien llama queda vacío; la vista previa con un documento real.
/// </summary>
public sealed class ClientDocumentNoticeContentTests
{
    private const int T1 = WmsFixture.TenantId;
    private const int T2 = WmsFixture.OtherTenantId;
    private static readonly DateOnly Today = DocumentExpiryTestSeed.Today;
    private static readonly DateOnly D1 = new(2026, 10, 1);

    private static async Task<(WmsFixture F, ClientDocumentNoticeContent P)> WorldAsync()
    {
        var f = await DocumentExpiryTestSeed.CreateAsync(new ExpiryTestClock(DocumentExpiryTestSeed.NowUtc));
        return (f, f.Get<ClientDocumentNoticeContent>());
    }

    private static Dictionary<string, string> ValuesOf(ClientDocument d, DateOnly? validTo = null)
        => new()
        {
            ["documentPublicId"] = d.PublicId.ToString("N"),
            ["validToIso"] = (validTo ?? d.ValidTo)?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["days"] = "15",
            ["threshold"] = "15",
        };

    private static NotificationContentRequest Req(ClientDocument d, NotificationAudience audience = NotificationAudience.Staff, string lang = "es", Dictionary<string, string>? values = null,
        string code = ClientDocumentEventCodes.Expiring, int tenantId = T1)
        => new(tenantId, code, values ?? ValuesOf(d), audience, lang);

    [Fact]
    public async Task Vendedores_del_cliente_titular_apoyo_y_preferido_sin_repetir_y_solo_activos()
    {
        var (f, p) = await WorldAsync();
        await using var _f = f;
        await f.AddActiveMemberAsync(201, "Titular Norte");
        await f.AddActiveMemberAsync(202, "Apoyo Norte");
        await f.AddSuspendedMemberAsync(203, "Apoyo Suspendido");
        await f.AddActiveMemberAsync(204, "Preferida");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 201, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, false, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 203, false, D1);
        var a = await f.AddClientAsync("A");
        await f.SetClientSalesAsync(a.ClientId, routeId: norte.SalesRouteId, preferredUserId: 201);
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(10));

        var res = await p.RecipientsAsync(T1, ClientDocumentEventCodes.Expiring, ClientDocumentContextRecipients.ClientSellers, ValuesOf(doc), default);
        Assert.Null(res.EmptyReason);
        Assert.Equal(new int?[] { 201, 202 }, res.People.Select(x => x.UserId).ToArray());
        Assert.Equal(("Titular Norte", NotificationAudience.Staff), (res.People[0].Name, res.People[0].Audience));
        Assert.Contains("@x.test", res.People[0].Email);

        // cliente sin ruta ni preferido: el motivo exacto
        var b = await f.AddClientAsync("B");
        var docB = await DocumentExpiryTestSeed.DocAsync(f, b, Today.AddDays(10));
        var empty = await p.RecipientsAsync(T1, ClientDocumentEventCodes.Expired, "client_sellers", ValuesOf(docB), default);
        Assert.Empty(empty.People);
        Assert.Equal("El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.", empty.EmptyReason);

        // una clave que el evento no tiene, u otro evento: nada
        Assert.Null((await p.RecipientsAsync(T1, ClientDocumentEventCodes.Expiring, "FOO", ValuesOf(doc), default)).EmptyReason);
        Assert.Empty((await p.RecipientsAsync(T1, SalesEventCodes.OrderRequested, ClientDocumentContextRecipients.ClientSellers, ValuesOf(doc), default)).People);
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Cliente_respeta_el_interruptor_de_la_compania_la_fila_del_cliente_y_toma_principal_marcados_y_extra_en_su_idioma()
    {
        var (f, p) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        await DocumentExpiryTestSeed.UpdateClientAsync(f, a, c => c.NoticeLang = "en");
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(10));
        Task<NotificationContextResolution> ClientAsync() => p.RecipientsAsync(T1, ClientDocumentEventCodes.Expiring, ClientDocumentContextRecipients.Client, ValuesOf(doc), default);

        // 1) la compañía no lo tiene encendido (por omisión)
        Assert.Equal("La compañía no tiene encendido «Avisar al cliente» (Ajustes → Avisos de vencimiento).", (await ClientAsync()).EmptyReason);

        // 2) encendido, pero el cliente no tiene correos
        await DocumentExpiryTestSeed.TenantAsync(f, T1, notifyClient: true);
        Assert.Equal("El cliente no tiene correo principal ni contactos marcados «Recibe avisos por correo».", (await ClientAsync()).EmptyReason);

        // 3) principal + marcados (sin repetir) + extra de la fila DOCUMENT_EXPIRING, en el idioma del cliente
        await DocumentExpiryTestSeed.ContactAsync(f, a, "Principal", "principal@farmacia.test", primary: true);
        await DocumentExpiryTestSeed.ContactAsync(f, a, "Marcado", "marcado@farmacia.test", flagged: true);
        await DocumentExpiryTestSeed.ContactAsync(f, a, "No marcado", "nomarcado@farmacia.test");
        var setting = new ClientNotificationSetting { TenantId = T1, ClientId = a.ClientId, Kind = ClientNotificationKinds.DocumentExpiring, EmailEnabled = true, ExtraEmails = "extra@farmacia.test, MARCADO@farmacia.test" };
        f.Db.Set<ClientNotificationSetting>().Add(setting);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var res = await ClientAsync();
        Assert.Null(res.EmptyReason);
        Assert.Equal(new[] { "principal@farmacia.test", "marcado@farmacia.test", "extra@farmacia.test" }, res.People.Select(x => x.Email).ToArray());
        Assert.All(res.People, x => Assert.Equal((NotificationAudience.Customer, "en", (int?)null), (x.Audience, x.Lang, x.UserId)));

        // 4) la fila del cliente con el correo apagado lo apaga para él
        var row = await f.Db.Set<ClientNotificationSetting>().AsTracking().SingleAsync();
        row.EmailEnabled = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("El cliente tiene apagado el aviso de documentos por vencer.", (await ClientAsync()).EmptyReason);

        // sin idioma propio toma el de la compañía
        await DocumentExpiryTestSeed.UpdateClientAsync(f, a, c => c.NoticeLang = null);
        f.Db.Set<ClientNotificationSetting>().Remove(await f.Db.Set<ClientNotificationSetting>().AsTracking().SingleAsync());
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.All((await ClientAsync()).People, x => Assert.Equal("es", x.Lang));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task BuildAsync_arma_las_variables_en_el_idioma_y_salta_el_aviso_que_ya_no_aplica()
    {
        var (f, p) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(10), "E-77");
        await DocumentExpiryTestSeed.ExemptionAsync(f, doc);

        var staffEn = await p.BuildAsync(Req(doc, lang: "en"), default);
        Assert.NotNull(staffEn);
        Assert.Null(staffEn!.SkipReason);
        Assert.Equal(("State exemption", "E-77", "Cliente A", "A", "15"), (staffEn.Values["documentType"], staffEn.Values["documentNumber"], staffEn.Values["client"], staffEn.Values["clientCode"], staffEn.Values["days"]));
        Assert.Equal("It backs a sales tax (IVU) exemption: once it expires, sales carry IVU again.", staffEn.Values["taxNote"]);
        Assert.Equal(Teikem.Domain.Tenancy.TenantFormatText.Date(Today.AddDays(10), Teikem.Domain.Tenancy.TenantFormatRules.Read(await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == T1))),
            staffEn.Values["validTo"]);
        var customerEs = await p.BuildAsync(Req(doc, NotificationAudience.Customer), default);
        Assert.Equal(("Exención estatal", "Este documento respalda su exención de IVU: envíenos el documento renovado antes de que venza para seguir exento."),
            (customerEs!.Values["documentType"], customerEs.Values["taxNote"]));

        // la fecha «hasta» cambió desde que se publicó
        Assert.Equal("La fecha «hasta» del documento cambió: este aviso ya no aplica.", (await p.BuildAsync(Req(doc, values: ValuesOf(doc, Today.AddDays(9))), default))!.SkipReason);

        // renovado
        await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddYears(1), "E-78");
        Assert.Equal("El documento ya se renovó: no se avisa.", (await p.BuildAsync(Req(doc), default))!.SkipReason);

        // cliente dado de baja
        var b = await f.AddClientAsync("B");
        var docB = await DocumentExpiryTestSeed.DocAsync(f, b, Today.AddDays(5));
        await DocumentExpiryTestSeed.UpdateClientAsync(f, b, c => c.IsActive = false);
        Assert.Equal("El cliente está dado de baja: no se avisa.", (await p.BuildAsync(Req(docB, code: ClientDocumentEventCodes.Expired), default))!.SkipReason);

        // documento quitado
        var c = await f.AddClientAsync("C");
        var docC = await DocumentExpiryTestSeed.DocAsync(f, c, Today.AddDays(5));
        await DocumentExpiryTestSeed.UpdateDocAsync(f, docC, d => d.IsActive = false);
        Assert.Equal("El documento ya se quitó: no se avisa.", (await p.BuildAsync(Req(docC), default))!.SkipReason);

        // otro evento: no lo atiende
        Assert.Null(await p.BuildAsync(Req(docC, code: SalesEventCodes.OrderRequested), default));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Un_documento_de_otra_compania_no_resuelve_nada()
    {
        var (f, p) = await WorldAsync();
        await using var _f = f;
        var z = await f.AddClientAsync("Z", T2);
        ClientDocument other;
        using (f.AsTenant(T2)) other = await DocumentExpiryTestSeed.DocAsync(f, z, Today.AddDays(5));

        Assert.Null(await p.BuildAsync(Req(other), default));
        Assert.Empty((await p.RecipientsAsync(T1, ClientDocumentEventCodes.Expiring, ClientDocumentContextRecipients.ClientSellers, ValuesOf(other), default)).People);
        await Assert.ThrowsAsync<NotFoundException>(() => p.ValuesForReferenceAsync(T1, ClientDocumentEventCodes.Expiring, other.PublicId.ToString(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => p.ValuesForReferenceAsync(T1, ClientDocumentEventCodes.Expiring, "no-es-un-guid", default));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Vista_previa_con_un_documento_real_y_ejemplo()
    {
        var (f, p) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        var doc = await DocumentExpiryTestSeed.DocAsync(f, a, Today.AddDays(12), "E-5");

        var v = await p.ValuesForReferenceAsync(T1, ClientDocumentEventCodes.Expiring, doc.PublicId.ToString("N"), default);
        Assert.Equal((doc.PublicId.ToString("N"), a.PublicId.ToString("N"), "2026-12-09", "12", "Exención estatal", string.Empty),
            (v["documentPublicId"], v["clientPublicId"], v["validToIso"], v["days"], v["documentType"], v["taxNote"]));
        Assert.Equal((PermissionCatalog.ClientsRead, ModuleKeys.Catalog), (p.ReferencePermission, p.ReferenceModule));

        // con esas variables el contenido no se salta (misma fecha «hasta»)
        Assert.Null((await p.BuildAsync(new NotificationContentRequest(T1, ClientDocumentEventCodes.Expiring, v, NotificationAudience.Staff, "es"), default))!.SkipReason);

        var sample = p.Sample(ClientDocumentEventCodes.Expired, NotificationAudience.Staff, "en");
        Assert.Equal(("Farmacia Central", "State sales tax (IVU) exemption certificate", "3"), (sample.Values["client"], sample.Values["documentType"], sample.Values["days"]));
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new ClientDocumentEventProvider() });
        foreach (var e in catalog.All)
            Assert.All(e.Variables, x => Assert.True(p.Sample(e.Code, NotificationAudience.Customer, "es").Values.ContainsKey(x.Name), $"{e.Code} sin la variable {x.Name} en el ejemplo"));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }
}
