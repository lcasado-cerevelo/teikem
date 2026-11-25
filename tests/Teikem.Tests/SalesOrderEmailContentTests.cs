using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Files;
using Teikem.Domain.Identity;
using Teikem.Domain.Notifications;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (G3) — el contenido del aviso «Orden de venta solicitada» (SalesOrderEmailContent), llamado directo sobre WmsFixture (InMemory): la huella y
/// la clave de no repetición al solicitar, los destinatarios del evento (vendedor y cliente), el contenido con la tabla y el IVU por componente, la firma
/// adjunta, los motivos de SKIPPED, el documento real de la vista previa y que nada quede rastreado en el DbContext de quien llama.
/// </summary>
public sealed class SalesOrderEmailContentTests
{
    private const int Tenant = WmsFixture.TenantId;

    private static async Task<(SalesVendorWorld W, SalesOrderEmailContent P)> WorldAsync(Action<IServiceCollection>? extra = null, string? baseUrl = "https://app.teikem.test/")
    {
        var settings = new Dictionary<string, string?>();
        if (baseUrl is not null) settings["Notifications:WebBaseUrl"] = baseUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var w = await SalesVendorWorld.CreateAsync(s =>
        {
            s.AddSingleton<IConfiguration>(config);
            s.AddSingleton<SalesOrderEmailContent>();
            extra?.Invoke(s);
        });
        return (w, w.F.Get<SalesOrderEmailContent>());
    }

    private static async Task<int> UserAsync(SalesVendorWorld w, int id, string name, bool active = true, bool member = true, int tenantId = Tenant, string membership = MembershipStatuses.Active)
    {
        var mail = $"{name.ToLowerInvariant().Replace(' ', '.')}@x.test";
        var membershipId = member ? await MembershipStatusIdAsync(w, membership) : 0;
        w.F.Db.Users.Add(new ApplicationUser { Id = id, UserName = mail, Email = mail, FullName = name, IsActive = active });
        if (member) w.F.Db.UserTenants.Add(new UserTenant { UserId = id, TenantId = tenantId, StatusCodeId = membershipId });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        return id;
    }

    /// <summary>El estatus de membresía (WmsFixture no lo siembra): «Vendedor de la orden» solo cuenta la membresía ACTIVE.</summary>
    private static async Task<int> MembershipStatusIdAsync(SalesVendorWorld w, string code)
    {
        var id = await w.F.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == code).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync();
        if (id is int found) return found;
        var next = await w.F.Db.StatusCodes.AsNoTracking().MaxAsync(s => s.StatusCodeId) + 1;
        w.F.Db.StatusCodes.Add(new Teikem.Domain.Catalogs.StatusCode
        {
            StatusCodeId = next, Entity = StatusDomains.MembershipStatus, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", StageKindLookupId = 1, SortOrder = 1, IsActive = true,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        return next;
    }

    private static async Task<int> RepAsync(SalesVendorWorld w, Client c, string name, int? userId, bool active = true)
    {
        var rep = new SalesRep { TenantId = Tenant, Name = name, UserId = userId, IsActive = active };
        w.F.Db.Set<SalesRep>().Add(rep);
        await w.F.Db.SaveChangesAsync();
        var row = await w.F.Db.Clients.AsTracking().SingleAsync(x => x.ClientId == c.ClientId);
        row.SalesRepId = rep.SalesRepId;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        return rep.SalesRepId;
    }

    private static async Task<SalesOrderDto> RequestedAsync(SalesVendorWorld w, Client c, Product p, decimal qty = 4m)
    {
        var d = await w.DraftAsync(c, p, qty);
        return await w.Sales.RequestAsync(d.PublicId, null, default);
    }

    private static Dictionary<string, string> ValuesOf(SalesOrderDto o, string? fingerprint = null)
    {
        var v = new Dictionary<string, string> { ["orderNumber"] = o.Number, ["orderPublicId"] = o.PublicId.ToString("N") };
        if (fingerprint is not null) v["orderFingerprint"] = fingerprint;
        return v;
    }

    private static NotificationContentRequest Req(SalesOrderDto o, NotificationAudience audience = NotificationAudience.Staff, string lang = "es", string? fingerprint = null, int tenantId = Tenant)
        => new(tenantId, SalesEventCodes.OrderRequested, ValuesOf(o, fingerprint), audience, lang);

    // ---------------------------------------------------------------- publicar al solicitar

    [Fact]
    public async Task Solicitar_anota_un_solo_aviso_con_su_huella_y_la_clave_REQ()
    {
        var (w, _) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);

        var row = Assert.Single(await w.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == SalesEventCodes.OrderRequested).ToListAsync());
        Assert.Equal($"REQ-{r.PublicId:N}", row.DedupKey);
        Assert.Equal(SalesOrderService.RequestedDedupKey(r.PublicId), row.DedupKey);
        var values = NotificationRules.ParseValues(row.PayloadJson);
        Assert.Equal(r.PublicId.ToString("N"), values["orderPublicId"]);
        var lines = await w.F.Db.Set<SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == w.F.Db.Set<SalesOrder>().Single(o => o.PublicId == r.PublicId).SalesOrderId).ToListAsync();
        Assert.Equal(SalesOrderEmailRules.Fingerprint(lines.Select(l => (l.LineNo, l.ProductId, l.QtyOrdered, l.UnitPrice)), r.Total), values["orderFingerprint"]);

        // un segundo intento de publicar la misma solicitud no duplica
        var again = await w.F.Get<INotificationPublisher>().PublishAsync(SalesEventCodes.OrderRequested, new Dictionary<string, string?> { ["orderNumber"] = r.Number },
            SalesOrderService.RequestedDedupKey(r.PublicId), default);
        await w.F.Db.SaveChangesAsync();
        Assert.False(again);
        Assert.Single(await w.F.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == SalesEventCodes.OrderRequested).ToListAsync());
    }

    // ---------------------------------------------------------------- destinatarios del evento

    [Fact]
    public async Task Vendedor_es_quien_la_solicito_mas_el_usuario_del_representante_sin_repetir_y_solo_activos()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        await UserAsync(w, 1, "Luis Rivera");   // quien solicita (el usuario de la sesión de prueba)
        await UserAsync(w, 2, "Ana Perez");
        await UserAsync(w, 3, "Pedro Inactivo", active: false);
        await UserAsync(w, 4, "Otra Compania", member: false);
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        await RepAsync(w, c, "Ana Pérez (ruta norte)", 2);
        var r = await RequestedAsync(w, c, p);

        var res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Seller, ValuesOf(r), default);
        Assert.Null(res.EmptyReason);
        Assert.Equal(new int?[] { 1, 2 }, res.People.Select(x => x.UserId).ToArray());
        Assert.All(res.People, x => Assert.Equal(NotificationAudience.Staff, x.Audience));
        Assert.Equal("ana.perez@x.test", res.People[1].Email);

        var info = await SellerAsync(w, r);
        Assert.Equal(("Ana Pérez (ruta norte)", "Luis Rivera"), (info.Name, info.CapturedByName));

        // el representante es la misma persona que solicitó: una sola, sin «Capturó»
        await RepAsync(w, c, "Luis R.", 1);
        res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, "seller", ValuesOf(r), default);
        Assert.Equal(new int?[] { 1 }, res.People.Select(x => x.UserId).ToArray());
        Assert.Null((await SellerAsync(w, r)).CapturedByName);

        // un representante inactivo, uno sin usuario activo o de otra compañía no recibe
        await RepAsync(w, c, "Pedro", 3);
        Assert.Equal(new int?[] { 1 }, (await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, "SELLER", ValuesOf(r), default)).People.Select(x => x.UserId).ToArray());
        await RepAsync(w, c, "Otra", 4);
        Assert.Equal(new int?[] { 1 }, (await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, "SELLER", ValuesOf(r), default)).People.Select(x => x.UserId).ToArray());
        await RepAsync(w, c, "Sin usuario", null, active: false);
        Assert.Equal("Luis Rivera", (await SellerAsync(w, r)).Name);   // el representante inactivo no cuenta: el nombre es el de quien la solicitó
    }

    [Fact]
    public async Task Sin_vendedor_con_usuario_activo_da_el_motivo()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        await UserAsync(w, 1, "Luis Rivera", active: false);
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);

        var res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Seller, ValuesOf(r), default);
        Assert.Empty(res.People);
        Assert.Equal("La orden no tiene un vendedor con usuario activo en la compañía.", res.EmptyReason);
        // una clave que el evento no tiene, otro evento o una orden que no existe: nadie y sin motivo
        Assert.Null((await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, "FOO", ValuesOf(r), default)).EmptyReason);
        Assert.Empty((await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderShipped, "SELLER", ValuesOf(r), default)).People);
        Assert.Empty((await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, "SELLER", new Dictionary<string, string> { ["orderPublicId"] = Guid.NewGuid().ToString("N") }, default)).People);
    }

    [Fact]
    public async Task Un_usuario_con_la_membresia_suspendida_no_es_vendedor_de_la_orden()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        await UserAsync(w, 1, "Luis Rivera", membership: MembershipStatuses.Suspended);   // quien solicita, suspendido en la compañía
        await UserAsync(w, 2, "Ana Suspendida", membership: MembershipStatuses.Suspended);
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        await RepAsync(w, c, "Ana Suspendida", 2);
        var r = await RequestedAsync(w, c, p);

        var res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Seller, ValuesOf(r), default);
        Assert.Empty(res.People);
        Assert.Equal(SalesOrderEmailContent.NoSellerReason, res.EmptyReason);
    }

    private static async Task<SalesOrderSellerInfo> SellerAsync(SalesVendorWorld w, SalesOrderDto r)
    {
        var order = await w.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == r.PublicId);
        return await SalesOrderSeller.ResolveAsync(w.F.Db, Tenant, order, default);
    }

    private static ContactPointUpsertRequest Email(string v, bool primary = true) => new("EMAIL", v, null, null, primary);

    [Fact]
    public async Task Cliente_es_el_correo_del_contacto_principal_mas_los_marcados_activos_con_correo()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1", x => x.NoticeLang = "en");
        var p = await w.ProductAsync("MED", 10m);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Principal", null, true, new[] { Email("principal@farmacia.test") }), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Marcado", null, false, new[] { Email("marcado@farmacia.test") }, ReceivesEmailNotifications: true), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Repetido", null, false, new[] { Email("PRINCIPAL@farmacia.test") }, ReceivesEmailNotifications: true), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("No marcado", null, false, new[] { Email("nomarcado@farmacia.test") }), default);
        await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Sin correo", null, false, null, ReceivesEmailNotifications: true), default);
        var inactive = await w.Clients.AddContactAsync(c.PublicId, new ClientContactCreateRequest("Inactivo", null, false, new[] { Email("inactivo@farmacia.test") }, ReceivesEmailNotifications: true), default);
        await w.Clients.UpdateContactAsync(c.PublicId, inactive.Id, new ClientContactUpdateRequest(null, null, null, false), default);
        var r = await RequestedAsync(w, c, p);

        var res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Client, ValuesOf(r), default);
        Assert.Null(res.EmptyReason);
        Assert.Equal(new[] { "principal@farmacia.test", "marcado@farmacia.test" }, res.People.Select(x => x.Email).ToArray());
        Assert.All(res.People, x => Assert.Equal((NotificationAudience.Customer, "en", (int?)null), (x.Audience, x.Lang, x.UserId)));
    }

    [Fact]
    public async Task Cliente_sin_correos_da_el_motivo_y_sin_idioma_propio_toma_el_de_la_compania()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);
        var res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Client, ValuesOf(r), default);
        Assert.Empty(res.People);
        Assert.Equal("El cliente no tiene correo principal ni contactos marcados con correo.", res.EmptyReason);

        // el correo del propio cliente es el respaldo
        w.F.Db.ContactPoints.Add(new ContactPoint
        {
            TenantId = Tenant, OwnerEntityLookupId = w.Lookup(LookupDomains.EntityType, EntityTypes.Client), OwnerId = c.ClientId,
            ContactTypeLookupId = w.Lookup(LookupDomains.ContactType, "EMAIL"), Value = "compras@farmacia.test", IsPrimary = true, IsActive = true,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        res = await prov.RecipientsAsync(Tenant, SalesEventCodes.OrderRequested, SalesContextRecipients.Client, ValuesOf(r), default);
        var person = Assert.Single(res.People);
        Assert.Equal(("compras@farmacia.test", "es"), (person.Email, person.Lang));
    }

    // ---------------------------------------------------------------- el contenido

    [Fact]
    public async Task El_contenido_lleva_la_tabla_el_IVU_por_componente_las_variables_y_el_enlace()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        await UserAsync(w, 1, "Luis Rivera");
        var c = await w.ClientAsync("FARM", x => { x.Name = "Farmacia Central"; x.MerchantRegistration = "SURI-777"; });
        var med = await w.ProductAsync("MED-500", 10m);
        var vit = await w.ProductAsync("VIT-C", 2m);
        var d = await w.Sales.CreateAsync(new SalesOrderRequest(w.W.PublicId, c.PublicId, Notes: "Nota <b>prueba</b>",
            Lines: new[] { new SalesOrderLineRequest(med.PublicId, 4m), new SalesOrderLineRequest(vit.PublicId, 1m) }), default);
        // la línea 2 lleva los componentes de un kit (la foto) y el IVU guardado de cada línea con la tasa de 11.5 % (10.5 + 1)
        var rate = new TaxRate
        {
            TenantId = Tenant, PublicId = Guid.NewGuid(), Code = "IVU", Name = "IVU", Percent = 11.5m, EffectiveFrom = new DateOnly(2020, 1, 1),
            Components = { new TaxRateComponent { TenantId = Tenant, Code = "STATE", Name = "IVU estatal", Percent = 10.5m, SortOrder = 1 }, new TaxRateComponent { TenantId = Tenant, Code = "CITY", Name = "IVU municipal", Percent = 1m, SortOrder = 2 } },
        };
        w.F.Db.TaxRates.Add(rate);
        await w.F.Db.SaveChangesAsync();
        var order = await w.F.Db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).SingleAsync(o => o.PublicId == d.PublicId);
        foreach (var l in order.Lines) { l.TaxRateId = rate.TaxRateId; l.TaxRatePct = 11.5m; }
        order.Lines.Single(l => l.LineNo == 1).TaxAmount = 4.60m;
        order.Lines.Single(l => l.LineNo == 2).TaxAmount = 0.23m;
        order.TaxAmount = 4.83m;
        var line2 = order.Lines.Single(l => l.LineNo == 2);
        var piece = await w.ProductAsync("VEN-01", 1m, stock: 0m);
        var original = await w.ProductAsync("VEN-00", 1m, stock: 0m);
        w.F.Db.Set<SalesOrderLineComponent>().Add(new SalesOrderLineComponent { TenantId = Tenant, SalesOrderLineId = line2.SalesOrderLineId, ComponentProductId = piece.ProductId, Quantity = 2m, SubstitutedProductId = original.ProductId });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var r = await w.Sales.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        var published = (await prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, r.Number, default))["orderFingerprint"];

        var content = await prov.BuildAsync(Req(r, fingerprint: published), default);
        Assert.NotNull(content);
        Assert.Null(content!.SkipReason);
        var html = content.Blocks["orderDetail"].Html;
        foreach (var expected in new[]
                 {
                     $"Orden de venta {r.Number}", "Tenant de prueba", "Farmacia Central", "(FARM)", "Registro de comerciante: SURI-777", "Vendedor", "Luis Rivera",
                     "MED-500", "VIT-C", "↳ VEN-01", "2 por kit (2)", "en lugar de VEN-00", "IVU estatal (10.5 %)", "$4.41", "IVU municipal (1 %)", "$0.42", "Total", "$46.83",
                     "Nota &lt;b&gt;prueba&lt;/b&gt;", "Abrir la orden en Teikem", $"https://app.teikem.test/sales-orders?order={r.PublicId:N}", "Recogido en",
                 })
            Assert.Contains(expected, html);
        Assert.DoesNotContain("<b>prueba", html);
        Assert.DoesNotContain("Esta orden cambió", html);   // misma huella
        Assert.Equal("2 líneas · Total $46.83 · Recogido", content.Blocks["orderDetail"].Text);
        Assert.Equal(r.Number, content.Values["orderNumber"]);
        Assert.Equal("$46.83", content.Values["total"]);
        Assert.Equal("Luis Rivera", content.Values["seller"]);
        Assert.Equal("Recogido", content.Values["deliveryMode"]);
        Assert.Equal($"https://app.teikem.test/sales-orders?order={r.PublicId:N}", content.Values["orderLink"]);
        Assert.Equal(r.PublicId.ToString("N"), content.Values["orderPublicId"]);
        Assert.Matches(@"^\d{2}/\d{2}/\d{4}$", content.Values["orderDate"]);
        Assert.Empty(content.Attachments);   // sin firma

        // la copia al cliente: sin enlace ni notas; en inglés con lang en
        var customer = await prov.BuildAsync(Req(r, NotificationAudience.Customer, "en", published), default);
        var chtml = customer!.Blocks["orderDetail"].Html;
        Assert.DoesNotContain("Open the order", chtml);
        Assert.DoesNotContain("Nota", chtml);
        Assert.DoesNotContain("&lt;b&gt;prueba", chtml);
        Assert.Contains($"Sales order {r.Number}", chtml);
        Assert.Equal(string.Empty, customer.Values["orderLink"]);

        // la orden cambió después de solicitarse: el personal lo ve arriba
        var changed = await prov.BuildAsync(Req(r, fingerprint: "0000000000000000"), default);
        Assert.Contains("Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.", changed!.Blocks["orderDetail"].Html);
    }

    [Fact]
    public async Task Sin_direccion_web_configurada_el_correo_sale_sin_boton()
    {
        var (w, prov) = await WorldAsync(baseUrl: null);
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);
        var content = await prov.BuildAsync(Req(r), default);
        Assert.DoesNotContain("Abrir la orden", content!.Blocks["orderDetail"].Html);
        Assert.Equal(string.Empty, content.Values["orderLink"]);
        Assert.Null(prov.OrderLink(r.PublicId));
    }

    [Fact]
    public async Task Otra_compania_sin_usuario_devuelve_null_y_nada_queda_rastreado()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);
        w.F.Db.ChangeTracker.Clear();

        // el mismo aviso pedido para OTRA compañía: la orden no es suya
        Assert.Null(await prov.BuildAsync(Req(r, tenantId: WmsFixture.OtherTenantId), default));
        Assert.Empty((await prov.RecipientsAsync(WmsFixture.OtherTenantId, SalesEventCodes.OrderRequested, "SELLER", ValuesOf(r), default)).People);
        await Assert.ThrowsAsync<NotFoundException>(() => prov.ValuesForReferenceAsync(WmsFixture.OtherTenantId, SalesEventCodes.OrderRequested, r.Number, default));
        // una orden que no existe tampoco; y un evento que no es el suyo
        Assert.Null(await prov.BuildAsync(new NotificationContentRequest(Tenant, SalesEventCodes.OrderRequested, new Dictionary<string, string> { ["orderPublicId"] = Guid.NewGuid().ToString("N") }, NotificationAudience.Staff, "es"), default));
        Assert.Null(await prov.BuildAsync(new NotificationContentRequest(Tenant, SalesEventCodes.OrderShipped, ValuesOf(r), NotificationAudience.Staff, "es"), default));

        // con la orden propia el contenido sale, y el DbContext compartido (InMemory) no queda rastreando nada
        Assert.NotNull(await prov.BuildAsync(Req(r), default));
        Assert.Empty(w.F.Db.ChangeTracker.Entries());
        // el contexto de quien llama queda como estaba (compañía, usuario e idioma)
        Assert.Equal((Tenant, (int?)1, "es"), (w.F.Tenant.TenantId!.Value, w.F.Tenant.UserId, w.F.Tenant.Lang));
        Assert.Equal(QueryTrackingBehavior.TrackAll, w.F.Db.ChangeTracker.QueryTrackingBehavior);
    }

    [Fact]
    public async Task Una_orden_avisos_viejos_sin_identificador_se_busca_por_numero()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);
        var content = await prov.BuildAsync(new NotificationContentRequest(Tenant, SalesEventCodes.OrderRequested, new Dictionary<string, string> { ["orderNumber"] = r.Number }, NotificationAudience.Staff, "es"), default);
        Assert.Contains(r.Number, content!.Blocks["orderDetail"].Html);
        Assert.DoesNotContain("Esta orden cambió", content.Blocks["orderDetail"].Html);   // sin huella publicada no se avisa
    }

    [Fact]
    public async Task Una_orden_cancelada_o_en_borrador_da_el_motivo_para_SKIPPED()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);
        await w.Sales.CancelAsync(r.PublicId, "El cliente la anuló.", default);
        var skipped = await prov.BuildAsync(Req(r), default);
        Assert.Equal($"La orden {r.Number} se canceló antes de enviar el aviso.", skipped!.SkipReason);
        Assert.Empty(skipped.Blocks);

        var draft = await w.DraftAsync(c, p, 1m);
        Assert.Equal($"La orden {draft.Number} sigue en borrador.", (await prov.BuildAsync(Req(draft), default))!.SkipReason);
    }

    // ---------------------------------------------------------------- firma

    private static async Task SignAsync(SalesVendorWorld w, SalesOrderDto d, string name = "María López")
    {
        var store = w.F.Get<IFileStore>();
        var publicId = Guid.NewGuid();
        var key = store.IsConfigured ? await store.SaveAsync(Tenant, publicId, SalesVendorWorld.Png, default) : "no-existe";
        var file = new StoredFile
        {
            TenantId = Tenant, PublicId = publicId, OwnerEntityLookupId = w.Lookup(LookupDomains.EntityType, StoredFileCodes.EntityType), OwnerId = 1, Kind = StoredFileCodes.KindSignature,
            FileName = "firma.png", ContentType = "image/png", SizeBytes = SalesVendorWorld.Png.Length, Sha256 = "x", StorageKey = key, IsActive = true,
        };
        w.F.Db.Set<StoredFile>().Add(file);
        await w.F.Db.SaveChangesAsync();
        var order = await w.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == d.PublicId);
        order.SignatureFileId = file.StoredFileId;
        order.AuthorizedByName = name;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task La_firma_va_adjunta_como_imagen()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var d = await w.DraftAsync(c, p, 2m);
        await SignAsync(w, d);
        var r = await w.Sales.RequestAsync(d.PublicId, null, default);

        var content = await prov.BuildAsync(Req(r), default);
        var att = Assert.Single(content!.Attachments);
        Assert.Equal(($"firma-{r.Number}.png", "image/png"), (att.FileName, att.ContentType));
        Assert.Equal(SalesVendorWorld.Png, att.Content);
        Assert.Contains("Autorizó: María López — firma adjunta", content.Blocks["orderDetail"].Html);
    }

    [Fact]
    public async Task Sin_almacen_de_archivos_la_firma_se_omite_sin_fallar()
    {
        var (w, prov) = await WorldAsync(s => s.AddSingleton<IFileStore>(new NoFileStore()));
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var d = await w.DraftAsync(c, p, 2m);
        await SignAsync(w, d);
        var r = await w.Sales.RequestAsync(d.PublicId, null, default);

        var content = await prov.BuildAsync(Req(r), default);
        Assert.Empty(content!.Attachments);
        Assert.Contains("Autorizó: María López — sin firma", content.Blocks["orderDetail"].Html);
    }

    [Fact]
    public async Task Una_firma_que_no_se_puede_leer_tampoco_detiene_el_correo()
    {
        var (w, prov) = await WorldAsync(s => s.AddSingleton<IFileStore>(new BrokenFileStore()));
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var d = await w.DraftAsync(c, p, 2m);
        await SignAsync(w, d);
        var r = await w.Sales.RequestAsync(d.PublicId, null, default);
        Assert.Empty((await prov.BuildAsync(Req(r), default))!.Attachments);
    }

    private sealed class NoFileStore : IFileStore
    {
        public bool IsConfigured => false;
        public Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct) => throw new InvalidOperationException();
        public Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct) => throw new InvalidOperationException();
        public Task DeleteAsync(int tenantId, string storageKey, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BrokenFileStore : IFileStore
    {
        public bool IsConfigured => true;
        public Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct) => Task.FromResult("roto");
        public Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct) => throw new IOException("disco no disponible");
        public Task DeleteAsync(int tenantId, string storageKey, CancellationToken ct) => Task.CompletedTask;
    }

    // ---------------------------------------------------------------- documento real (vista previa) y ejemplo

    [Fact]
    public async Task Valores_de_un_documento_real_por_numero_y_por_PublicId_e_inexistente_da_404()
    {
        var (w, prov) = await WorldAsync();
        await using var _w = w;
        var c = await w.ClientAsync("C1");
        var p = await w.ProductAsync("MED", 10m);
        var r = await RequestedAsync(w, c, p);

        var byNumber = await prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, r.Number.ToLowerInvariant(), default);
        Assert.Equal((r.Number, "Cliente C1", r.PublicId.ToString("N"), "1", "Quedó todo reservado."),
            (byNumber["orderNumber"], byNumber["client"], byNumber["orderPublicId"], byNumber["lineCount"], byNumber["detail"]));
        Assert.Equal(w.W.Name, byNumber["warehouse"]);
        var byId = await prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, r.PublicId.ToString(), default);
        Assert.Equal(byNumber["orderFingerprint"], byId["orderFingerprint"]);
        Assert.Equal(byNumber["orderNumber"], (await prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, r.PublicId.ToString("N"), default))["orderNumber"]);

        var missing = await Assert.ThrowsAsync<NotFoundException>(() => prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, "OV-99999", default));
        Assert.Equal("Orden de venta 'OV-99999' no encontrada.", missing.Message);
        var missingId = await Assert.ThrowsAsync<NotFoundException>(() => prov.ValuesForReferenceAsync(Tenant, SalesEventCodes.OrderRequested, Guid.NewGuid().ToString(), default));
        Assert.Equal("Orden de venta no encontrada.", missingId.Message);
        Assert.Equal(404, missing.StatusCode);
    }

    [Fact]
    public void El_ejemplo_es_Farmacia_Central_con_kit_IVU_por_componente_y_firma()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:PasswordReset:WebBaseUrl"] = "https://web.teikem.test" }).Build();
        var prov = new SalesOrderEmailContent(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SalesOrderEmailContent>.Instance);
        var s = prov.Sample(SalesEventCodes.OrderRequested, NotificationAudience.Staff, "es");
        var html = s.Blocks["orderDetail"].Html;
        foreach (var expected in new[] { "Farmacia Central", "MED-500", "VIT-C", "KIT-BOT", "↳ VEN-01", "↳ ALC-70", "IVU estatal (10.5 %)", "$8.40", "IVU municipal (1 %)", "$0.80", "$89.20",
                     "firma adjunta", "https://web.teikem.test/sales-orders?order=" })
            Assert.Contains(expected, html);
        var att = Assert.Single(s.Attachments);
        Assert.Equal("firma-OV-00012.png", att.FileName);
        Assert.Null(s.SkipReason);
        Assert.Equal("3 líneas · Total $89.20 · Entrega · Crédito 30 días", s.Blocks["orderDetail"].Text);

        var customer = prov.Sample(SalesEventCodes.OrderRequested, NotificationAudience.Customer, "en");
        Assert.DoesNotContain("Open the order", customer.Blocks["orderDetail"].Html);
        Assert.Contains("State tax", customer.Blocks["orderDetail"].Html);
        Assert.Equal(string.Empty, customer.Values["orderLink"]);

        Assert.Equal(new[] { SalesEventCodes.OrderRequested }, prov.EventCodes);
        Assert.Equal((PermissionCatalog.SalesView, ModuleKeys.SalesOrders), (prov.ReferencePermission, prov.ReferenceModule));
    }
}
