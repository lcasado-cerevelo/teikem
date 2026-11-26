using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (pieza G) — fuente SPECIAL_PRICE del trabajo DOCUMENT_EXPIRY_SCAN, el evento «Precio especial por vencer» y su contenido (InMemory, datos INVENTADOS):
/// solo listas dedicadas activas, publicadas, con «hasta» y con renglones, nunca después de vencer; una renovada no avisa; precio de cadena = la unión de los vendedores
/// de sus clientes activos con tope de 50; la lista desactivada (o en borrador, o con otro «hasta») antes de enviar se salta; con SALES_ORDERS apagado no corre.
/// </summary>
public sealed class SpecialPriceExpirySourceTests
{
    private const int T1 = WmsFixture.TenantId;
    private const int T2 = WmsFixture.OtherTenantId;
    private static readonly DateOnly Today = DocumentExpiryTestSeed.Today;
    private static readonly DateOnly D1 = new(2026, 10, 1);

    private static async Task<(WmsFixture F, ExpiryTestClock Clock)> WorldAsync()
    {
        var clock = new ExpiryTestClock(DocumentExpiryTestSeed.NowUtc);
        var f = await DocumentExpiryTestSeed.CreateAsync(clock, s =>
        {
            s.AddSingleton<INotificationEventProvider, SpecialPriceEventProvider>();
            s.AddSingleton<IExpiryNoticeSource, SpecialPriceExpirySource>();
            s.AddSingleton<SpecialPriceExpirySource>();
            s.AddSingleton<SpecialPriceNoticeContent>();
        });
        f.SetModules(ModuleKeys.Catalog, ModuleKeys.SalesOrders);
        return (f, clock);
    }

    private static async Task<ClientChain> ChainAsync(WmsFixture f, string name, int tenantId = T1)
    {
        var c = new ClientChain { PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        f.Db.Set<ClientChain>().Add(c);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return c;
    }

    /// <summary>Lista dedicada (precio especial) de un cliente o de una cadena, con <paramref name="items"/> renglones activos.</summary>
    private static async Task<PriceList> SpecialAsync(WmsFixture f, DateOnly? to, int? clientId = null, int? chainId = null, DateOnly? from = null, bool published = true,
        bool active = true, int items = 1, int tenantId = T1, string name = "Precio especial")
    {
        var l = new PriceList
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Name = name, ValidFrom = from ?? Today.AddDays(-20), ValidTo = to, IsPublished = published, AppliesToAll = false,
            SpecialClientId = clientId, SpecialChainId = chainId, IsActive = active, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<PriceList>().Add(l);
        await f.Db.SaveChangesAsync();
        for (var i = 0; i < items; i++)
        {
            var p = await f.AddProductAsync($"SP-{l.PriceListId}-{i}", tenantId: tenantId);
            f.Db.Set<PriceListItem>().Add(new PriceListItem { TenantId = tenantId, PriceListId = l.PriceListId, ProductId = p.ProductId, Price = 5m, IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return l;
    }

    private static async Task UpdateListAsync(WmsFixture f, PriceList list, Action<PriceList> change)
    {
        var row = await f.Db.Set<PriceList>().IgnoreQueryFilters().AsTracking().SingleAsync(l => l.PriceListId == list.PriceListId);
        change(row);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static Dictionary<string, string> ValuesOf(PriceList l, DateOnly? validTo = null)
        => new()
        {
            ["priceListPublicId"] = l.PublicId.ToString("N"),
            ["validToIso"] = (validTo ?? l.ValidTo)?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["days"] = "10",
            ["threshold"] = "15",
        };

    private static NotificationContentRequest Req(PriceList l, string lang = "es", Dictionary<string, string>? values = null, int tenantId = T1)
        => new(tenantId, SpecialPriceEventCodes.Expiring, values ?? ValuesOf(l), NotificationAudience.Staff, lang);

    private static Task<List<Teikem.Domain.Notifications.NotificationOutbox>> SpOutboxAsync(WmsFixture f)
        => f.Db.NotificationOutbox.IgnoreQueryFilters().AsNoTracking().Where(o => o.EventCode == SpecialPriceEventCodes.Expiring).OrderBy(o => o.NotificationOutboxId).ToListAsync();

    [Fact]
    public async Task Solo_listas_dedicadas_activas_publicadas_con_hasta_y_renglones_y_nunca_despues_de_vencer()
    {
        var (f, clock) = await WorldAsync();
        await using var _f = f;
        await DocumentExpiryTestSeed.TenantAsync(f, T1, thresholds: "30,15,5,0,-3", daily: true);   // los umbrales negativos y el diario no aplican a esta fuente
        var a = await f.AddClientAsync("A");
        var live = await SpecialAsync(f, Today.AddDays(10), clientId: a.ClientId, items: 2, name: "Precio especial — Cliente A");
        var dueToday = await SpecialAsync(f, Today, clientId: (await f.AddClientAsync("H")).ClientId);
        await SpecialAsync(f, Today.AddDays(10), clientId: (await f.AddClientAsync("B")).ClientId, published: false);   // borrador
        await SpecialAsync(f, Today.AddDays(10), clientId: (await f.AddClientAsync("C")).ClientId, active: false);      // desactivada
        await SpecialAsync(f, null, clientId: (await f.AddClientAsync("D")).ClientId);                                  // sin «hasta»
        await SpecialAsync(f, Today.AddDays(-1), clientId: (await f.AddClientAsync("E")).ClientId);                     // ya venció
        await SpecialAsync(f, Today.AddDays(10), clientId: (await f.AddClientAsync("G")).ClientId, items: 0);           // sin renglones
        await SpecialAsync(f, Today.AddDays(40), clientId: (await f.AddClientAsync("I")).ClientId);                     // fuera de los 30 días
        var inactiveOwner = await f.AddClientAsync("J");
        await SpecialAsync(f, Today.AddDays(10), clientId: inactiveOwner.ClientId);
        await DocumentExpiryTestSeed.UpdateClientAsync(f, inactiveOwner, c => c.IsActive = false);                      // dueño dado de baja
        f.Db.Set<PriceList>().Add(new PriceList { PublicId = Guid.NewGuid(), TenantId = T1, Name = "Lista normal", ValidFrom = Today.AddDays(-5), ValidTo = Today.AddDays(10),
            IsPublished = true, AppliesToAll = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow });                                    // no es dedicada
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var z = await f.AddClientAsync("Z", T2);
        await SpecialAsync(f, Today.AddDays(10), clientId: z.ClientId, tenantId: T2);                                   // otra compañía

        var candidates = await f.Get<SpecialPriceExpirySource>().CandidatesAsync(Today, 30, 400, default);
        Assert.Equal(new[] { dueToday.PriceListId, live.PriceListId }, candidates.Select(c => c.EntityId).ToArray());
        Assert.All(candidates, c => Assert.Equal((SpecialPriceEventCodes.Expiring, (string?)null), (c.ExpiringEvent, c.ExpiredEvent)));

        var r1 = await f.Get<DocumentExpiryScanService>().ScanAsync(default);
        Assert.Equal(new DocumentExpiryScanResult(2, 0, 0, 0), r1);
        var rows = await SpOutboxAsync(f);
        Assert.Equal(new[] { $"SP:{dueToday.PriceListId}:{Today:yyyyMMdd}:T0", $"SP:{live.PriceListId}:{Today.AddDays(10):yyyyMMdd}:T15" }, rows.Select(r => r.DedupKey).ToArray());
        Assert.All(rows, r => Assert.Equal(T1, r.TenantId));
        var v = NotificationRules.ParseValues(rows[1].PayloadJson);
        Assert.Equal(("Cliente A", "Cliente", "Precio especial — Cliente A", "2", "10", "15"), (v["owner"], v["ownerKind"], v["priceList"], v["itemCount"], v["days"], v["threshold"]));
        Assert.Equal((live.PublicId.ToString("N"), "2026-12-07", $"/catalog/clients?client={a.PublicId:D}"), (v["priceListPublicId"], v["validToIso"], v["ownerLink"]));

        // al día siguiente lo que venció hoy NO se avisa como vencido (la fuente no avisa después de vencer) y lo demás ya estaba avisado
        clock.NextDay();
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 1, 0), await f.Get<DocumentExpiryScanService>().ScanAsync(default));
        Assert.Equal(2, (await SpOutboxAsync(f)).Count);
        Assert.Empty(f.Db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added));
    }

    [Theory]
    [InlineData(11, 40, true)]      // empieza al día siguiente y termina después
    [InlineData(5, null, true)]     // se encima y no tiene «hasta»
    [InlineData(12, 40, false)]     // deja un hueco de un día
    [InlineData(1, 10, false)]      // termina el mismo día: no la renueva
    [InlineData(1, 9, false)]       // termina antes
    public void Renovada_es_otra_lista_del_dueno_que_empieza_a_mas_tardar_el_dia_siguiente_y_termina_despues(int fromDays, int? toDays, bool renewed)
    {
        var validTo = Today.AddDays(10);
        DateOnly? to = toDays is int t ? Today.AddDays(t) : null;
        Assert.Equal(renewed, SpecialPriceExpiryRules.IsRenewed(1, validTo, new[] { (2, Today.AddDays(fromDays), to) }));
        Assert.False(SpecialPriceExpiryRules.IsRenewed(1, validTo, new[] { (1, Today.AddDays(fromDays), to) }));   // ella misma no cuenta
    }

    [Fact]
    public async Task Una_renovada_no_avisa_y_la_renovacion_en_borrador_o_de_otro_dueno_no_cuenta()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var a = await f.AddClientAsync("A");
        var b = await f.AddClientAsync("B");
        var chain = await ChainAsync(f, "Walgreens");
        await SpecialAsync(f, Today.AddDays(10), clientId: a.ClientId);
        await SpecialAsync(f, Today.AddDays(40), clientId: a.ClientId, from: Today.AddDays(11));                       // renueva la de A
        var bList = await SpecialAsync(f, Today.AddDays(10), clientId: b.ClientId);
        await SpecialAsync(f, Today.AddDays(40), clientId: b.ClientId, from: Today.AddDays(11), published: false);     // renovación en borrador: no cuenta
        var chainList = await SpecialAsync(f, Today.AddDays(10), chainId: chain.ClientChainId);
        await SpecialAsync(f, Today.AddDays(40), clientId: a.ClientId, from: Today.AddDays(11));                       // otro dueño: no renueva la de la cadena

        var candidates = await f.Get<SpecialPriceExpirySource>().CandidatesAsync(Today, 30, 0, default);
        Assert.Equal(new[] { bList.PriceListId, chainList.PriceListId }.OrderBy(x => x), candidates.Select(c => c.EntityId).OrderBy(x => x));
        var chainValues = candidates.Single(c => c.EntityId == chainList.PriceListId).Values;
        Assert.Equal(("Walgreens", "Cadena", $"/catalog/chains?id={chain.PublicId:D}"), (chainValues["owner"], chainValues["ownerKind"], chainValues["ownerLink"]));
    }

    [Fact]
    public async Task Precio_de_cliente_y_de_cadena_van_a_los_vendedores_y_la_cadena_une_los_de_sus_clientes_activos_con_tope_de_50()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var p = f.Get<SpecialPriceNoticeContent>();
        await f.AddActiveMemberAsync(201, "Preferida");
        await f.AddActiveMemberAsync(202, "Titular Norte");
        await f.AddActiveMemberAsync(205, "De cliente inactivo");
        var norte = await f.AddRouteAsync("Norte");
        await f.AddAssignmentAsync(norte.SalesRouteId, 202, true, D1);
        await f.AddAssignmentAsync(norte.SalesRouteId, 201, false, D1);

        var chain = await ChainAsync(f, "Walgreens");
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var c3 = await f.AddClientAsync("C3");
        var c4 = await f.AddClientAsync("C4");
        foreach (var c in new[] { c1, c2, c3, c4 }) await DocumentExpiryTestSeed.UpdateClientAsync(f, c, x => x.ChainId = chain.ClientChainId);
        await f.SetClientSalesAsync(c1.ClientId, preferredUserId: 201);
        await f.SetClientSalesAsync(c2.ClientId, routeId: norte.SalesRouteId);             // titular 202 y apoyo 201 (repetida)
        await f.SetClientSalesAsync(c4.ClientId, preferredUserId: 205);
        await DocumentExpiryTestSeed.UpdateClientAsync(f, c4, x => x.IsActive = false);    // cliente inactivo: sus vendedores no cuentan
        var chainList = await SpecialAsync(f, Today.AddDays(10), chainId: chain.ClientChainId);

        var res = await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(chainList), default);
        Assert.Null(res.EmptyReason);
        Assert.Equal(new int?[] { 201, 202 }, res.People.Select(x => x.UserId).ToArray());
        Assert.All(res.People, x => Assert.Equal(NotificationAudience.Staff, x.Audience));
        Assert.Equal("Preferida", res.People[0].Name);

        // precio de un cliente: NoticeUsersAsync de ese cliente; sin vendedores, el motivo exacto
        var clientList = await SpecialAsync(f, Today.AddDays(10), clientId: c2.ClientId);
        Assert.Equal(new int?[] { 202, 201 }, (await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, "client_sellers", ValuesOf(clientList), default)).People.Select(x => x.UserId).ToArray());
        var lonely = await SpecialAsync(f, Today.AddDays(10), clientId: c3.ClientId);
        Assert.Equal("El cliente no tiene ruta de venta ni vendedor preferido con usuario activo.",
            (await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(lonely), default)).EmptyReason);
        var emptyChain = await ChainAsync(f, "Sin vendedores");
        var emptyChainList = await SpecialAsync(f, Today.AddDays(10), chainId: emptyChain.ClientChainId);
        Assert.Equal("Ningún cliente activo de la cadena tiene ruta de venta ni vendedor preferido con usuario activo.",
            (await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(emptyChainList), default)).EmptyReason);

        // una clave que el evento no tiene (el cliente nunca recibe este aviso) u otro evento: nada
        Assert.Empty((await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, "CLIENT", ValuesOf(chainList), default)).People);
        Assert.Empty((await p.RecipientsAsync(T1, ClientDocumentEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(chainList), default)).People);

        // tope de 50: 55 clientes activos de la cadena, cada uno con su vendedor preferido
        var big = await ChainAsync(f, "Grande");
        for (var i = 0; i < 55; i++)
        {
            var uid = 300 + i;
            await f.AddActiveMemberAsync(uid, "Vendedor " + i);
            var c = await f.AddClientAsync("G" + i);
            await DocumentExpiryTestSeed.UpdateClientAsync(f, c, x => x.ChainId = big.ClientChainId);
            await f.SetClientSalesAsync(c.ClientId, preferredUserId: uid);
        }
        var bigList = await SpecialAsync(f, Today.AddDays(10), chainId: big.ClientChainId);
        var capped = await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(bigList), default);
        Assert.Equal(SpecialPriceNoticeContent.MaxChainRecipients, capped.People.Count);
        Assert.Equal(Enumerable.Range(300, 50).Select(x => (int?)x), capped.People.Select(x => x.UserId));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Al_enviar_se_salta_si_la_lista_se_desactivo_volvio_a_borrador_cambio_su_hasta_se_renovo_o_el_dueno_se_dio_de_baja()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var p = f.Get<SpecialPriceNoticeContent>();
        var chain = await ChainAsync(f, "Walgreens");
        var list = await SpecialAsync(f, Today.AddDays(10), chainId: chain.ClientChainId, items: 3, name: "Precio especial — Walgreens");

        var en = await p.BuildAsync(Req(list, "en"), default);
        Assert.NotNull(en);
        Assert.Null(en!.SkipReason);
        Assert.Equal(("Walgreens", "Chain", "3", "10"), (en.Values["owner"], en.Values["ownerKind"], en.Values["itemCount"], en.Values["days"]));
        Assert.Equal(Teikem.Domain.Tenancy.TenantFormatText.Date(Today.AddDays(10), Teikem.Domain.Tenancy.TenantFormatRules.Read(await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == T1))),
            en.Values["validTo"]);
        Assert.Equal("Cadena", (await p.BuildAsync(Req(list), default))!.Values["ownerKind"]);

        // cambió el «hasta» desde que se publicó
        Assert.Equal("La fecha «hasta» del precio especial cambió: este aviso ya no aplica.", (await p.BuildAsync(Req(list, values: ValuesOf(list, Today.AddDays(9))), default))!.SkipReason);

        // volvió a borrador
        await UpdateListAsync(f, list, l => l.IsPublished = false);
        Assert.Equal("El precio especial volvió a borrador: no se avisa.", (await p.BuildAsync(Req(list), default))!.SkipReason);
        await UpdateListAsync(f, list, l => l.IsPublished = true);

        // se desactivó
        await UpdateListAsync(f, list, l => l.IsActive = false);
        Assert.Equal("El precio especial se desactivó: no se avisa.", (await p.BuildAsync(Req(list), default))!.SkipReason);
        await UpdateListAsync(f, list, l => l.IsActive = true);

        // la cadena se dio de baja
        var row = await f.Db.Set<ClientChain>().AsTracking().SingleAsync(c => c.ClientChainId == chain.ClientChainId);
        row.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("El cliente o la cadena del precio especial está dado de baja: no se avisa.", (await p.BuildAsync(Req(list), default))!.SkipReason);
        row = await f.Db.Set<ClientChain>().AsTracking().SingleAsync(c => c.ClientChainId == chain.ClientChainId);
        row.IsActive = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        // se renovó
        await SpecialAsync(f, Today.AddDays(60), chainId: chain.ClientChainId, from: Today.AddDays(11));
        Assert.Equal("El precio especial ya se renovó: no se avisa.", (await p.BuildAsync(Req(list), default))!.SkipReason);

        // otro evento: no lo atiende
        Assert.Null(await p.BuildAsync(new NotificationContentRequest(T1, ClientDocumentEventCodes.Expiring, ValuesOf(list), NotificationAudience.Staff, "es"), default));
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Una_lista_de_otra_compania_no_resuelve_nada_y_la_vista_previa_usa_una_lista_real()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        var p = f.Get<SpecialPriceNoticeContent>();
        var z = await f.AddClientAsync("Z", T2);
        var other = await SpecialAsync(f, Today.AddDays(5), clientId: z.ClientId, tenantId: T2);
        Assert.Null(await p.BuildAsync(Req(other), default));
        Assert.Empty((await p.RecipientsAsync(T1, SpecialPriceEventCodes.Expiring, SpecialPriceContextRecipients.ClientSellers, ValuesOf(other), default)).People);
        await Assert.ThrowsAsync<NotFoundException>(() => p.ValuesForReferenceAsync(T1, SpecialPriceEventCodes.Expiring, other.PublicId.ToString(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => p.ValuesForReferenceAsync(T1, SpecialPriceEventCodes.Expiring, "no-es-un-guid", default));

        var a = await f.AddClientAsync("A");
        var mine = await SpecialAsync(f, Today.AddDays(12), clientId: a.ClientId, name: "Precio especial — Cliente A");
        var v = await p.ValuesForReferenceAsync(T1, SpecialPriceEventCodes.Expiring, mine.PublicId.ToString(), default);
        Assert.Equal((mine.PublicId.ToString("N"), "2026-12-09", "12", "Cliente A", "Precio especial — Cliente A", "1"),
            (v["priceListPublicId"], v["validToIso"], v["days"], v["owner"], v["priceList"], v["itemCount"]));
        Assert.Equal((PermissionCatalog.SalesView, ModuleKeys.SalesOrders), (p.ReferencePermission, p.ReferenceModule));
        Assert.Null((await p.BuildAsync(new NotificationContentRequest(T1, SpecialPriceEventCodes.Expiring, v, NotificationAudience.Staff, "es"), default))!.SkipReason);
        Assert.Empty(f.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Con_ordenes_de_venta_apagado_la_fuente_no_corre()
    {
        var (f, _) = await WorldAsync();
        await using var _f = f;
        await SpecialAsync(f, Today.AddDays(3), clientId: (await f.AddClientAsync("A")).ClientId);
        f.SetModules(ModuleKeys.Catalog);
        Assert.Equal(new DocumentExpiryScanResult(0, 0, 0, 0), await f.Get<DocumentExpiryScanService>().ScanAsync(default));
        Assert.Empty(await SpOutboxAsync(f));

        f.SetModules(ModuleKeys.Catalog, ModuleKeys.SalesOrders);
        Assert.Equal(new DocumentExpiryScanResult(1, 0, 0, 0), await f.Get<DocumentExpiryScanService>().ScanAsync(default));
        Assert.Single(await SpOutboxAsync(f));
    }

    [Fact]
    public void El_evento_se_arma_cae_en_ventas_va_solo_a_los_vendedores_y_el_ejemplo_trae_todas_sus_variables()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new SpecialPriceEventProvider(), new ClientDocumentEventProvider() });
        var e = Assert.IsType<NotificationEventDef>(catalog.Find(SpecialPriceEventCodes.Expiring));
        NotificationEventDefRules.Validate(e);
        Assert.Equal((ModuleKeys.SalesOrders, true, "{ownerLink}"), (e.ModuleKey, e.IsOptional, e.LinkTemplate));
        Assert.Equal(NotificationEventCategories.Sales, NotificationEventCategories.Of(e.Code));
        var recipient = Assert.Single(e.ContextRecipients!);
        Assert.Equal((SpecialPriceContextRecipients.ClientSellers, true, NotificationAudience.Staff), (recipient.Key, recipient.IsDefault, recipient.Audience));
        Assert.Equal(new[] { "owner", "ownerKind", "priceList", "validTo", "days", "threshold", "itemCount", "priceListPublicId", "validToIso", "ownerLink" }, e.Variables.Select(x => x.Name));
        var allowed = e.Variables.Select(x => x.Name).ToHashSet();
        foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn, e.LinkTemplate })
            Assert.All(NotificationRules.VariablesOf(text), x => Assert.Contains(x, allowed));

        var p = new SpecialPriceNoticeContent(null!);
        foreach (var lang in new[] { "es", "en" })
        {
            var sample = p.Sample(e.Code, NotificationAudience.Staff, lang);
            Assert.All(e.Variables, x => Assert.True(sample.Values.ContainsKey(x.Name), $"el ejemplo no trae la variable {x.Name}"));
        }
        Assert.Equal("Chain", p.Sample(e.Code, NotificationAudience.Staff, "en").Values["ownerKind"]);
        Assert.Equal(new[] { SpecialPriceEventCodes.Expiring }, p.EventCodes);
        Assert.True(SpecialPriceEventCodes.Expiring.Length <= 60);
    }
}
