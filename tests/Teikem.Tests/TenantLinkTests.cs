using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

// Lote 227 (P7 del pedido de vendedores): compañías enlazadas — proponer, aceptar, rechazar y terminar el enlace; con el enlace activo la entrega de un despacho de la
// compañía cliente (1) crea la orden de transporte en la que da el transporte (2) por el camino de la API de clientes; aislamiento (nada cruza sin enlace activo ni entre
// enlaces ajenos: la compañía 3 no ve nada), idempotencia de la orden creada y retorno de estatus y entrega.
public sealed class TenantLinkTests
{
    private const int A = WmsFixture.TenantId, B = WmsFixture.OtherTenantId, C = 3;

    private sealed class World(SalesVendorWorld x, Client bClient, Client bOther, Client cClient, Guid aId, Guid bId, Guid cId) : IAsyncDisposable
    {
        public SalesVendorWorld X { get; } = x;
        public WmsFixture F => X.F;
        public TeikemDbContext Db => X.F.Db;
        public Client BClient { get; } = bClient;
        public Client BOther { get; } = bOther;
        public Client CClient { get; } = cClient;
        public Guid APublicId { get; } = aId;
        public Guid BPublicId { get; } = bId;
        public Guid CPublicId { get; } = cId;
        public TenantLinkService Links => F.Get<TenantLinkService>();
        public TenantLinkBridge Bridge => F.Get<TenantLinkBridge>();
        public SalesDeliveryService Delivery => F.Get<SalesDeliveryService>();
        public ValueTask DisposeAsync() => X.DisposeAsync();
    }

    private static async Task<World> NewWorldAsync()
    {
        var x = await SalesVendorWorld.CreateAsync(s =>
        {
            s.AddSingleton<INotificationEventProvider, TenantLinkEventProvider>();
            s.AddSingleton<OrderPriceVisibility>();
            s.AddSingleton<OrderReadService>();
            s.AddSingleton<OrderStatusEffect>();
            s.AddSingleton<IStatusTransitionEffect>(sp => sp.GetRequiredService<OrderStatusEffect>());
            s.AddSingleton<OrderStatusService>();
            s.AddSingleton(sp => new OrderService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
                sp.GetRequiredService<StatusService>(), sp.GetRequiredService<LocationService>(), sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<OrderStatusService>(),
                sp.GetRequiredService<OrderReadService>(), sp.GetRequiredService<PermissionService>(), null!, sp.GetRequiredService<ITenantClock>()));
            s.AddSingleton<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());
            s.AddSingleton(sp => new PickBatchService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
                sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(), sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(),
                sp.GetRequiredService<OrderService>(), sp.GetRequiredService<ISalesDispatchReversal>()));
            s.AddSingleton<LinkedTenantScope>();
            s.AddSingleton<TenantLinkService>();
            s.AddSingleton<TenantLinkBridge>();
            s.AddSingleton<SalesDeliveryService>();
        });
        var f = x.F;
        var db = f.Db;
        var id = 6000;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        var standard = L(LookupDomains.ServiceType, "STANDARD");
        var box = L(LookupDomains.PackageType, "BOX");
        db.LookupCodes.AddRange(standard, box, L(LookupDomains.OrderPriority, "HIGH"), L(LookupDomains.OrderPriority, "NORMAL"),
            L(LookupDomains.RateComponentType, RateComponentTypes.BaseFreight), L(LookupDomains.RateComponentType, RateComponentTypes.ExtraPiece),
            L(LookupDomains.EntityType, EntityTypes.OrderStop), L(LookupDomains.Capability, Capabilities.Reprice), L(LookupDomains.Capability, Capabilities.Cancel),
            L(LookupDomains.EntityType, TenantLinkCodes.EntityType), L(LookupDomains.EntityType, TenantLinkCodes.OrderEntityType));
        var pipe = f.LookupId(LookupDomains.StageKind, StageKinds.Pipeline);
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 6100, Entity = StatusDomains.StopStatus, InternalCode = StopStatuses.Pending, LabelJson = "{\"es\":\"Pendiente\"}", StageKindLookupId = pipe, SortOrder = 1, IsInitial = true, IsActive = true },
            new StatusCode { StatusCodeId = 6101, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activa\"}", StageKindLookupId = pipe, SortOrder = 1, IsInitial = true, IsActive = true });
        db.Tenants.Add(new Tenant { TenantId = C, Name = "Tercera compañía", IsActive = true });
        await db.SaveChangesAsync();
        foreach (var t in await db.Tenants.AsTracking().ToListAsync())
        {
            t.DefaultServiceTypeLookupId = standard.LookupCodeId;
            t.DefaultPackageTypeLookupId = box.LookupCodeId;
            t.Name = t.TenantId switch { A => "Advance Solutions", B => "Advance Logistics", _ => t.Name };
        }
        await db.SaveChangesAsync();
        f.Lookups.Load(await db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        db.ChangeTracker.Clear();

        f.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Billing, ModuleKeys.Intercompany, ModuleKeys.Integrations, ModuleKeys.LtlGround);
        using (f.AsTenant(B)) f.SetModules(ModuleKeys.Intercompany, ModuleKeys.Integrations, ModuleKeys.LtlGround);
        using (f.AsTenant(C)) f.SetModules(ModuleKeys.Intercompany, ModuleKeys.Integrations, ModuleKeys.LtlGround);
        var bClient = await f.AddClientAsync("SOL", B);
        var bOther = await f.AddClientAsync("OTRO", B);
        var cClient = await f.AddClientAsync("SOL3", C);
        await ContractAsync(f, B, bClient.ClientId);
        var ids = await db.Tenants.AsNoTracking().ToDictionaryAsync(t => t.TenantId, t => t.PublicId);
        return new World(x, bClient, bOther, cClient, ids[A], ids[B], ids[C]);
    }

    private static int _contracts = 7000;

    /// <summary>Contrato activo con tarifa STANDARD/BOX del cliente (la orden se cotiza al nacer).</summary>
    private static async Task ContractAsync(WmsFixture f, int tenantId, int clientId)
    {
        var k = new Contract
        {
            ContractId = Interlocked.Increment(ref _contracts), PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = clientId, ContractNumber = "K-" + clientId, Title = "Marco",
            StartDate = new DateOnly(2026, 1, 1), StatusCodeId = f.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), BillPerService = true, IsActive = true,
        };
        f.Db.Set<Contract>().Add(k);
        f.Db.RateComponents.Add(new RateComponent
        {
            RateComponentId = Interlocked.Increment(ref _contracts), TenantId = tenantId, ContractId = k.ContractId,
            ComponentTypeLookupId = await f.Lookups.GetIdAsync(LookupDomains.RateComponentType, RateComponentTypes.BaseFreight),
            ServiceTypeLookupId = await f.Lookups.GetIdAsync(LookupDomains.ServiceType, "STANDARD"), PackageTypeLookupId = await f.Lookups.GetIdAsync(LookupDomains.PackageType, "BOX"),
            FlatAmount = 12m, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true,
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    /// <summary>La compañía 1 propone «somos cliente de la 2» y la 2 acepta con su cliente SOL.</summary>
    private static async Task<TenantLinkDto> LinkAsync(World w)
    {
        var proposed = await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "client", Note: "Para las farmacias"), default);
        using (w.F.AsTenant(B)) return await w.Links.AcceptAsync(proposed.PublicId, new TenantLinkAcceptRequest(w.BClient.PublicId), default);
    }

    /// <summary>Una orden de venta de la compañía 1 (con consignatario) solicitada y despachada: la orden y su recolección.</summary>
    private static async Task<(SalesOrderDto Order, Guid Batch)> DispatchedAsync(World w, bool urgent = false, string sku = "MED")
    {
        var p = await w.X.ProductAsync(sku, 10m);
        var c = await w.X.ClientAsync("F-" + sku);
        await ContractAsync(w.F, A, c.ClientId);   // por si la entrega se crea aquí
        var loc = await w.X.ConsigneeAsync(c);
        var draft = await w.X.DraftAsync(c, p, 2m, consignee: loc.PublicId);
        if (urgent)
        {
            var row = await w.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == draft.PublicId);
            row.IsUrgent = true;
            await w.Db.SaveChangesAsync();
            w.Db.ChangeTracker.Clear();
        }
        var requested = await w.X.Sales.RequestAsync(draft.PublicId, null, default);
        var d = await w.X.Sales.DispatchAsync(draft.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(requested.Lines[0].Id, 2m) }), default);
        return (d.Order, d.PickBatchPublicId);
    }

    private static SalesDeliveryRequest DeliveryReq(Guid batch, string? packageType = "BOX")
        => new(batch, new OrderCreateRequest(null, null, null, null, null, null, new List<OrderPackageLineRequest> { new(packageType, "Caja de tirillas", 2) }, null, null, null, null, null, "Frágil", null));

    private static Task<List<TransportOrder>> OrdersOfAsync(World w, int tenantId)
        => w.Db.TransportOrders.IgnoreQueryFilters().AsNoTracking().Where(o => o.TenantId == tenantId).ToListAsync();

    private static Task<List<Teikem.Domain.Notifications.NotificationOutbox>> OutboxAsync(World w, int tenantId, string code)
        => w.Db.NotificationOutbox.IgnoreQueryFilters().AsNoTracking().Where(o => o.TenantId == tenantId && o.EventCode == code).ToListAsync();

    // ================================================================ reglas, catálogo y SQL

    [Fact]
    public void Rules_module_permissions_and_codes()
    {
        Assert.Equal("INTERCOMPANY", ModuleKeys.Intercompany);
        Assert.Equal(new[] { "PROPOSED", "ACTIVE", "REJECTED", "ENDED" }, TenantLinkStatuses.All);
        Assert.Equal(new[] { "PENDING", "CREATED", "FAILED" }, TenantLinkOrderStates.All);
        Assert.Equal("CLIENT", TenantLinkRules.NormalizeRole(" client "));
        Assert.Null(TenantLinkRules.NormalizeRole("OTRO"));
        Assert.Equal((null, TenantLinkRules.NoteTooLong), TenantLinkRules.NormalizeNote(new string('x', 251)));
        Assert.Equal(TimeSpan.FromMinutes(1), TenantLinkRules.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(8), TenantLinkRules.Backoff(4));
        Assert.Equal(TimeSpan.FromMinutes(60), TenantLinkRules.Backoff(30));
        Assert.True(TenantLinkRules.IsFinal(true, true));
        Assert.True(TenantLinkRules.IsFinal(false, false));
        Assert.False(TenantLinkRules.IsFinal(false, true));
        foreach (var code in new[] { PermissionCatalog.IntercompanyView, PermissionCatalog.IntercompanyManage })
        {
            Assert.Equal("ADMIN", Assert.Single(PermissionCatalog.All, p => p.Code == code).Category);
            Assert.Contains(code, PermissionCatalog.RoleTemplates["TenantAdmin"]);
            Assert.Contains(code, PermissionDisplayOrder.Codes);
        }
        var manage = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.IntercompanyManage };
        PermissionCatalog.ExpandImplied(manage);
        Assert.Contains(PermissionCatalog.IntercompanyView, manage);
        Assert.Contains(PermissionCatalog.IntegrationsView, manage);   // los enlaces viven en Integraciones / API
        Assert.All(TenantLinkEventCodes.All, c => Assert.Equal(NotificationEventCategories.Integrations, NotificationEventCategories.Of(c)));
        Assert.Equal(TenantLinkEventCodes.All.Length, new TenantLinkEventProvider().Events.Count());
        Assert.Equal(typeof(Teikem.Api.Controllers.TenantLinksController).GetCustomAttributes(typeof(Teikem.Api.Auth.RequireModuleAttribute), true)
            .Cast<Teikem.Api.Auth.RequireModuleAttribute>().Select(a => a.ModuleKey), new[] { ModuleKeys.Intercompany });
        Assert.Equal("INTERCOMPANY_SYNC", new TenantLinkSyncJob().Key);
        Assert.Equal(120, new TenantLinkSyncJob().DefaultIntervalSeconds);
    }

    [Fact]
    public void Update_sql_has_the_idempotent_section_at_the_end()
    {
        var sql = File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");
        var start = sql.IndexOf("2026-11-07 — Lote 227 (P7 del pedido de vendedores)", StringComparison.Ordinal);
        Assert.True(start > sql.IndexOf("2026-11-07 — Lote 224 (P6 del pedido de vendedores)", StringComparison.Ordinal), "La sección del Lote 227 va al final.");
        var section = SqlSections.From(sql, start);
        Assert.Contains("('INTERCOMPANY', N'Compañías enlazadas', N'Linked companies'", section);
        Assert.Contains("'INTEGRATIONS', 135)", section);
        Assert.Contains("IF OBJECT_ID(N'dbo.TenantLink', N'U') IS NULL CREATE TABLE dbo.TenantLink (", section);
        Assert.Contains("IF OBJECT_ID(N'dbo.TenantLinkOrder', N'U') IS NULL CREATE TABLE dbo.TenantLinkOrder (", section);
        Assert.Contains("CREATE UNIQUE INDEX UX_TenantLinkOrder_PickBatch ON dbo.TenantLinkOrder(PickBatchId);", section);
        Assert.Contains("CREATE UNIQUE INDEX UX_TenantLink_ClientOpen ON dbo.TenantLink(ClientTenantId) WHERE Status IN ('PROPOSED','ACTIVE');", section);
        Assert.Contains("(N'TENANT_LINK_ORDER',", section);
        Assert.Contains("'L227_INTERCOMPANY'", section);
        Assert.Contains("JOIN dbo.Permission pm ON pm.Code IN ('intercompany.view', 'intercompany.manage')", section);
        Assert.DoesNotContain("dbo.TenantModule", section, StringComparison.Ordinal);   // solo define el módulo
        Assert.DoesNotContain("DELETE", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", section, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ proponer, aceptar, rechazar, terminar

    [Fact]
    public async Task Propose_and_accept_needs_both_companies_and_the_provider_chooses_its_client()
    {
        await using var w = await NewWorldAsync();
        var proposed = await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "CLIENT", Note: " Para las farmacias "), default);
        Assert.Equal(("CLIENT", "PROPOSED", "Advance Logistics", true, false, false), (proposed.Role, proposed.Status, proposed.OtherCompanyName, proposed.ProposedByUs, proposed.CanRespond, proposed.CanEnd));
        Assert.Equal("Para las farmacias", proposed.Note);
        // quien propuso no acepta su propia propuesta
        Assert.Equal(TenantLinkRules.CannotRespondOwn, await MsgOf(() => w.Links.AcceptAsync(proposed.PublicId, new TenantLinkAcceptRequest(w.BClient.PublicId), default)));
        // con la propuesta abierta no se propone otra
        Assert.Equal(TenantLinkRules.AlreadyOpen, await MsgOf(() => w.Links.ProposeAsync(new TenantLinkProposeRequest(w.CPublicId, "CLIENT"), default)));
        // a la otra compañía le llega el aviso (en su propio contexto)
        Assert.Single(await OutboxAsync(w, B, TenantLinkEventCodes.LinkChanged));
        Assert.Empty(await OutboxAsync(w, A, TenantLinkEventCodes.LinkChanged));

        using (w.F.AsTenant(B))
        {
            var seen = Assert.Single(await w.Links.ListAsync(default));
            Assert.Equal(("PROVIDER", "Advance Solutions", true, false), (seen.Role, seen.OtherCompanyName, seen.CanRespond, seen.ProposedByUs));
            Assert.Equal(TenantLinkRules.ClientRequired, await MsgOf(() => w.Links.AcceptAsync(proposed.PublicId, new TenantLinkAcceptRequest(), default)));
            Assert.Equal(TenantLinkRules.ClientNotUsable, await MsgOf(() => w.Links.AcceptAsync(proposed.PublicId, new TenantLinkAcceptRequest(w.CClient.PublicId), default)));   // un cliente de otra compañía «no existe»
            var accepted = await w.Links.AcceptAsync(proposed.PublicId, new TenantLinkAcceptRequest(w.BClient.PublicId, "Bienvenidos"), default);
            Assert.Equal(("ACTIVE", true, w.BClient.PublicId, "Cliente SOL"), (accepted.Status, accepted.CanEnd, accepted.ProviderClientPublicId, accepted.ProviderClientName));
        }
        var mine = await w.Links.GetAsync(proposed.PublicId, default);
        Assert.Equal(("ACTIVE", "Bienvenidos"), (mine.Status, mine.ResponseNote));
        Assert.Null(mine.ProviderClientPublicId);   // el cliente de allá solo lo ve el proveedor
        Assert.Single(await OutboxAsync(w, A, TenantLinkEventCodes.LinkChanged));
        Assert.Equal(TenantLinkRules.NotProposed, await MsgOf(() => w.Links.RejectAsync(proposed.PublicId, new TenantLinkReasonRequest("tarde"), default)));
    }

    [Fact]
    public async Task The_provider_can_propose_with_its_client_and_the_client_company_accepts()
    {
        await using var w = await NewWorldAsync();
        TenantLinkDto proposed;
        using (w.F.AsTenant(B))
        {
            Assert.Equal(TenantLinkRules.ClientRequired, await MsgOf(() => w.Links.ProposeAsync(new TenantLinkProposeRequest(w.APublicId, "PROVIDER"), default)));
            proposed = await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.APublicId, "PROVIDER", w.BClient.PublicId), default);
            Assert.Equal(("PROVIDER", w.BClient.PublicId), (proposed.Role, proposed.ProviderClientPublicId));
        }
        var accepted = await w.Links.AcceptAsync(proposed.PublicId, null, default);   // la compañía cliente no elige cliente
        Assert.Equal(("CLIENT", "ACTIVE"), (accepted.Role, accepted.Status));
        Assert.NotNull(await w.Bridge.ActiveAsClientAsync(default));
    }

    [Fact]
    public async Task Reject_and_end_need_a_reason_and_leave_no_active_link()
    {
        await using var w = await NewWorldAsync();
        var proposed = await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "CLIENT"), default);
        using (w.F.AsTenant(B))
        {
            Assert.Equal(TenantLinkRules.ReasonRequired, await MsgOf(() => w.Links.RejectAsync(proposed.PublicId, new TenantLinkReasonRequest("  "), default)));
            var rejected = await w.Links.RejectAsync(proposed.PublicId, new TenantLinkReasonRequest("No damos servicio en esa zona"), default);
            Assert.Equal(("REJECTED", "No damos servicio en esa zona"), (rejected.Status, rejected.ResponseNote));
        }
        Assert.Null(await w.Bridge.ActiveAsClientAsync(default));
        Assert.Equal(TenantLinkRules.NotActive, await MsgOf(() => w.Links.EndAsync(proposed.PublicId, new TenantLinkReasonRequest("x"), default)));

        // rechazado no estorba: se propone otro, se acepta y se termina
        var link = await LinkAsync(w);
        Assert.Equal(TenantLinkRules.ReasonRequired, await MsgOf(() => w.Links.EndAsync(link.PublicId, null, default)));
        var ended = await w.Links.EndAsync(link.PublicId, new TenantLinkReasonRequest("Cambiamos de transportista"), default);
        Assert.Equal(("ENDED", true, "Cambiamos de transportista"), (ended.Status, ended.EndedByUs, ended.EndReason));
        Assert.Null(await w.Bridge.ActiveAsClientAsync(default));
        Assert.Equal(2, (await w.Links.ListAsync(default)).Count);
    }

    [Fact]
    public async Task Only_companies_the_user_belongs_to_can_be_linked_and_a_client_of_the_provider_represents_one_company()
    {
        await using var w = await NewWorldAsync();
        w.F.Tenant.IsPlatformAdmin = false;   // un usuario normal: solo sus compañías
        Assert.Empty((await w.Links.OptionsAsync(default)).Companies);
        Assert.Equal(TenantLinkRules.CompanyNotAvailable, await MsgOf(() => w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "CLIENT"), default)));
        Assert.Equal(TenantLinkRules.SameCompany, await MsgOf(() => w.Links.ProposeAsync(new TenantLinkProposeRequest(w.APublicId, "CLIENT"), default)));
        Assert.Equal(TenantLinkRules.RoleInvalid, await MsgOf(() => w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "ambos"), default)));

        w.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = B, StatusCodeId = 6101, JoinedAtUtc = DateTime.UtcNow });
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        var options = await w.Links.OptionsAsync(default);
        var company = Assert.Single(options.Companies);
        Assert.Equal((w.BPublicId, "Advance Logistics", true, true), (company.PublicId, company.Name, company.HasModule, company.HasTransport));
        await LinkAsync(w);

        // el cliente SOL de la 2 ya representa a la 1: no puede representar a la 3
        w.F.Tenant.IsPlatformAdmin = true;
        using (w.F.AsTenant(C))
        {
            var p3 = await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "CLIENT"), default);
            using (w.F.AsTenant(B))
                Assert.Equal(TenantLinkRules.ClientAlreadyLinked, await MsgOf(() => w.Links.AcceptAsync(p3.PublicId, new TenantLinkAcceptRequest(w.BClient.PublicId), default)));
        }
    }

    // ================================================================ aislamiento

    [Fact]
    public async Task A_third_company_sees_nothing_of_a_link_between_two_others()
    {
        await using var w = await NewWorldAsync();
        var link = await LinkAsync(w);
        var (order, batch) = await DispatchedAsync(w);
        await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default);
        var delivery = Assert.Single((await w.Bridge.ListAsync(null, null, 0, 50, default)).Items);
        using (w.F.AsTenant(C))
        {
            Assert.Empty(await w.Links.ListAsync(default));
            await Assert.ThrowsAsync<NotFoundException>(() => w.Links.GetAsync(link.PublicId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => w.Links.EndAsync(link.PublicId, new TenantLinkReasonRequest("x"), default));
            Assert.Empty((await w.Bridge.ListAsync(null, null, 0, 50, default)).Items);
            await Assert.ThrowsAsync<NotFoundException>(() => w.Bridge.GetAsync(delivery.PublicId, default));
            Assert.Null(await w.Bridge.ActiveAsClientAsync(default));
            Assert.Empty(await w.Db.TransportOrders.AsNoTracking().ToListAsync());
        }
        // la compañía que da el transporte ve su orden normal, pero no la fila de la compañía cliente
        using (w.F.AsTenant(B))
        {
            Assert.Empty(await w.Db.Set<TenantLinkOrder>().AsNoTracking().ToListAsync());
            Assert.Empty((await w.Bridge.ListAsync(null, null, 0, 50, default)).Items);
            Assert.Single(await w.Db.TransportOrders.AsNoTracking().ToListAsync());
        }
        Assert.Empty(await OrdersOfAsync(w, C));
    }

    [Fact]
    public async Task Without_an_active_link_the_delivery_is_created_here_as_always_and_nothing_crosses()
    {
        await using var w = await NewWorldAsync();
        // solo propuesto (falta la aceptación de la otra compañía): no cruza nada
        await w.Links.ProposeAsync(new TenantLinkProposeRequest(w.BPublicId, "CLIENT"), default);
        var (order, batch) = await DispatchedAsync(w);
        var after = await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default);
        Assert.Null(Assert.Single(after.Dispatches!).LinkedDelivery);
        // la entrega nació aquí, enlazada a la orden de venta (con InMemory las transacciones anidadas limpian el seguimiento y el empaque no queda marcado en la recolección:
        // se mira la orden de transporte, que es lo que importa aquí)
        var soId = (await w.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(s => s.PublicId == order.PublicId)).SalesOrderId;
        Assert.Equal(soId, Assert.Single(await OrdersOfAsync(w, A)).SalesOrderId);
        Assert.Empty(await OrdersOfAsync(w, B));
        Assert.Empty(await w.Db.Set<TenantLinkOrder>().IgnoreQueryFilters().AsNoTracking().ToListAsync());

        // con el módulo apagado aquí, tampoco (aunque el enlace esté activo)
        await using var w2 = await NewWorldAsync();
        await LinkAsync(w2);
        w2.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Billing, ModuleKeys.LtlGround);
        var (o2, b2) = await DispatchedAsync(w2);
        await w2.Delivery.CreateAsync(o2.PublicId, DeliveryReq(b2), default);
        Assert.Single(await OrdersOfAsync(w2, A));
        Assert.Empty(await OrdersOfAsync(w2, B));
    }

    // ================================================================ la entrega por la compañía enlazada

    [Fact]
    public async Task With_the_link_the_delivery_creates_the_transport_order_in_the_other_company_for_its_client()
    {
        await using var w = await NewWorldAsync();
        await LinkAsync(w);
        var (order, batch) = await DispatchedAsync(w, urgent: true);
        var after = await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch, packageType: "TUBO"), default);

        var info = Assert.Single(after.Dispatches!);
        Assert.Equal("PACKED", info.Status);
        Assert.Null(info.TransportOrderPublicId);   // aquí no hay orden de transporte
        var linked = info.LinkedDelivery!;
        Assert.Equal(("CREATED", "Advance Logistics", OrderStatuses.Submitted, false, order.Number), (linked.State, linked.ProviderCompanyName, linked.ProviderStatus, linked.IsFinal, linked.SalesOrderNumber));
        Assert.Empty(await OrdersOfAsync(w, A));

        var there = Assert.Single(await OrdersOfAsync(w, B));
        Assert.Equal((w.BClient.ClientId, linked.ProviderOrderPublicId, linked.ProviderOrderNumber), (there.ClientId, (Guid?)there.PublicId, there.OrderNumber));
        Assert.Equal(w.F.Lookups.GetIdAsync(LookupDomains.EntityType, TenantLinkCodes.OrderEntityType).Result, there.SourceEntityTypeLookupId);
        Assert.Equal(w.F.Lookups.GetIdAsync(LookupDomains.OrderPriority, "HIGH").Result, there.PriorityLookupId);   // urgente → prioridad alta
        Assert.Contains($"orden de venta {order.Number}", there.Notes);
        Assert.Contains("Frágil", there.Notes);
        // el consignatario (la farmacia) nació en el directorio del cliente de allá
        var consignee = await w.Db.Locations.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.TenantId == B);
        Assert.Equal(("Farmacia F-MED", "Calle 1", "Ponce", w.BClient.ClientId), (consignee.Name, consignee.Line1, consignee.City, consignee.ClientId));
        // el tipo de paquete que allá no existe toma el de la compañía; la descripción se queda
        var cargo = await w.Db.CargoLines.IgnoreQueryFilters().AsNoTracking().Where(l => l.TransportOrderId == there.TransportOrderId).ToListAsync();
        Assert.NotEmpty(cargo);
        var box = await w.F.Lookups.GetIdAsync(LookupDomains.PackageType, "BOX");
        Assert.All(cargo, l => Assert.Equal(("Caja de tirillas", (int?)box), (l.Description, l.PackageTypeLookupId)));
        // el transporte de allá tiene constancia del urgente
        Assert.Single(await OutboxAsync(w, B, TenantLinkEventCodes.UrgentOrder));
    }

    [Fact]
    public async Task Creating_the_order_is_idempotent()
    {
        await using var w = await NewWorldAsync();
        await LinkAsync(w);
        var (order, batch) = await DispatchedAsync(w);
        await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default);
        var row = await w.Db.Set<TenantLinkOrder>().AsNoTracking().SingleAsync();

        // la misma entrega otra vez: la recolección ya no está recolectada (422) y no nace otra orden
        await Assert.ThrowsAsync<StatusRuleException>(() => w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default));
        var pick = await w.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PublicId == batch);
        var link = (await w.Bridge.ActiveAsClientAsync(default))!;
        Assert.Equal(row.TenantLinkOrderId, await w.Bridge.CreateLinkedDeliveryAsync(link, row.SalesOrderId, pick.PickBatchId, DeliveryReq(batch).Order!, default));
        // mandar otra vez no hace nada
        Assert.False(await w.Bridge.PushAsync(row.TenantLinkOrderId, DateTime.UtcNow, default));
        // se cayó después de crear allá y antes de anotarlo aquí: el siguiente intento la encuentra por su marca de origen y no la duplica
        var tracked = await w.Db.Set<TenantLinkOrder>().AsTracking().SingleAsync();
        (tracked.State, tracked.ProviderOrderId, tracked.ProviderOrderNumber) = (TenantLinkOrderStates.Pending, null, null);
        await w.Db.SaveChangesAsync();
        w.Db.ChangeTracker.Clear();
        Assert.True(await w.Bridge.PushAsync(row.TenantLinkOrderId, DateTime.UtcNow, default));
        var there = Assert.Single(await OrdersOfAsync(w, B));
        Assert.Equal(there.TransportOrderId, (await w.Db.Set<TenantLinkOrder>().AsNoTracking().SingleAsync()).ProviderOrderId);
        Assert.Single(await w.Db.Set<TenantLinkOrder>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Status_and_delivery_come_back_with_a_notice_and_stop_once_final()
    {
        await using var w = await NewWorldAsync();
        await LinkAsync(w);
        var (order, batch) = await DispatchedAsync(w);
        await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default);
        var row = await w.Db.Set<TenantLinkOrder>().AsNoTracking().SingleAsync();

        // nada cambió allá: revisar no avisa
        Assert.Equal("Entregas por compañía enlazada: 0 de 0 mandada(s); 1 revisada(s), 0 con estatus nuevo.", await w.Bridge.RunDueAsync(DateTime.UtcNow, default));
        Assert.Empty(await OutboxAsync(w, A, TenantLinkEventCodes.DeliveryStatus));

        // allá la entregan (estatus y comprobante, en su compañía)
        using (w.F.AsTenant(B))
        {
            var there = await w.Db.TransportOrders.AsTracking().SingleAsync();
            there.StatusCodeId = w.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered);
            w.Db.Set<DeliveryProof>().Add(new DeliveryProof { PublicId = Guid.NewGuid(), TenantId = B, TransportOrderId = there.TransportOrderId, DeliveredAtUtc = new DateTime(2026, 11, 7, 15, 0, 0, DateTimeKind.Utc),
                RecipientName = "María Rivera", RecordedBy = 1, SignedByUserId = 1, IsActive = true });
            await w.Db.SaveChangesAsync();
            w.Db.ChangeTracker.Clear();
        }
        var job = new TenantLinkSyncJob();
        var msg = await job.RunAsync(new JobRunContext(A, JobTriggers.Schedule, w.F.Services, DateTime.UtcNow), default);
        Assert.Equal("Entregas por compañía enlazada: 0 de 0 mandada(s); 1 revisada(s), 1 con estatus nuevo.", msg);
        var got = await w.Bridge.GetAsync(row.PublicId, default);
        Assert.Equal((OrderStatuses.Delivered, true, "María Rivera", new DateTime(2026, 11, 7, 15, 0, 0, DateTimeKind.Utc)), (got.ProviderStatus, got.IsFinal, got.DeliveredTo, got.DeliveredAtUtc));
        var notice = Assert.Single(await OutboxAsync(w, A, TenantLinkEventCodes.DeliveryStatus));
        Assert.Contains(order.Number, notice.PayloadJson);
        Assert.Contains("Rivera", notice.PayloadJson);
        Assert.Empty(await OutboxAsync(w, B, TenantLinkEventCodes.DeliveryStatus));
        // final: ya no se revisa
        Assert.Equal("Entregas por compañía enlazada: 0 de 0 mandada(s); 0 revisada(s), 0 con estatus nuevo.", await w.Bridge.RunDueAsync(DateTime.UtcNow, default));
        var dispatch = Assert.Single((await w.X.Sales.GetAsync(order.PublicId, default)).Dispatches!);
        Assert.Equal(OrderStatuses.Delivered, dispatch.LinkedDelivery!.ProviderStatus);
    }

    [Fact]
    public async Task After_the_link_ends_nothing_is_sent_nor_read_and_a_rejected_order_fails_with_its_message()
    {
        await using var w = await NewWorldAsync();
        var link = await LinkAsync(w);

        // la otra compañía rechaza la orden (su cliente suspendido): queda FAILED con su mensaje y un aviso aquí; reintentar después de arreglarlo
        using (w.F.AsTenant(B))
        {
            var sol = await w.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == w.BClient.ClientId);
            sol.StatusCodeId = w.X.SuspendedStatusId;
            await w.Db.SaveChangesAsync();
            w.Db.ChangeTracker.Clear();
        }
        var (order, batch) = await DispatchedAsync(w);
        await w.Delivery.CreateAsync(order.PublicId, DeliveryReq(batch), default);
        var failed = Assert.Single((await w.Bridge.ListAsync("FAILED", null, 0, 50, default)).Items);
        Assert.Equal((OrderService.ClientSuspendedMessage, 1), (failed.Error, failed.Attempts));
        Assert.Single(await OutboxAsync(w, A, TenantLinkEventCodes.DeliveryFailed));
        Assert.Empty(await OrdersOfAsync(w, B));
        using (w.F.AsTenant(B))
        {
            var sol = await w.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == w.BClient.ClientId);
            sol.StatusCodeId = w.F.StatusId(StatusDomains.ClientStatus, ClientStatuses.Active);
            await w.Db.SaveChangesAsync();
            w.Db.ChangeTracker.Clear();
        }
        Assert.Equal(TenantLinkRules.NotCreatedYet, await MsgOf(() => w.Bridge.SyncNowAsync(failed.PublicId, default)));
        var retried = await w.Bridge.RetryAsync(failed.PublicId, default);
        Assert.Equal(("CREATED", null), (retried.State, retried.Error));
        Assert.Equal(TenantLinkRules.RetryOnlyFailed, await MsgOf(() => w.Bridge.RetryAsync(failed.PublicId, default)));

        // otro despacho queda pendiente (se mandará después) y en eso terminan el enlace
        var (o2, b2) = await DispatchedAsync(w, sku: "VIT");
        var link2 = (await w.Bridge.ActiveAsClientAsync(default))!;
        var pick2 = await w.Db.Set<PickBatch>().AsNoTracking().SingleAsync(b => b.PublicId == b2);
        var pendingId = await w.Bridge.CreateLinkedDeliveryAsync(link2, (await w.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(s => s.PublicId == o2.PublicId)).SalesOrderId, pick2.PickBatchId, DeliveryReq(b2).Order! with { ConsigneeLocationPublicId = o2.ConsigneePublicId }, default);
        using (w.F.AsTenant(B)) await w.Links.EndAsync(link.PublicId, new TenantLinkReasonRequest("Fin del contrato"), default);
        using (w.F.AsTenant(B))
        {
            var there = await w.Db.TransportOrders.AsTracking().SingleAsync();
            there.StatusCodeId = w.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.InTransit);
            await w.Db.SaveChangesAsync();
            w.Db.ChangeTracker.Clear();
        }
        Assert.False(await w.Bridge.PushAsync(pendingId, DateTime.UtcNow, default));
        var stopped = await w.Db.Set<TenantLinkOrder>().AsNoTracking().SingleAsync(o => o.TenantLinkOrderId == pendingId);
        Assert.Equal((TenantLinkOrderStates.Failed, TenantLinkRules.LinkNotActive("Advance Logistics")), (stopped.State, stopped.LastError));
        Assert.Single(await OrdersOfAsync(w, B));   // la pendiente no cruzó
        // ni se lee lo de allá: el estatus nuevo (en ruta) no llega
        Assert.Equal(TenantLinkRules.LinkInactive("Advance Logistics"), await MsgOf(() => w.Bridge.SyncNowAsync(failed.PublicId, default)));
        await w.Bridge.RunDueAsync(DateTime.UtcNow, default);
        Assert.Equal(OrderStatuses.Submitted, (await w.Bridge.GetAsync(failed.PublicId, default)).ProviderStatus);
        // y la siguiente entrega ya se crea aquí, como siempre
        var (o3, b3) = await DispatchedAsync(w, sku: "GUA");
        var after = await w.Delivery.CreateAsync(o3.PublicId, DeliveryReq(b3), default);
        Assert.Null(Assert.Single(after.Dispatches!).LinkedDelivery);
        Assert.Single(await OrdersOfAsync(w, A));
        Assert.Single(await OrdersOfAsync(w, B));
    }

    [Fact]
    public async Task The_delivery_needs_a_consignee_and_valid_packages_before_anything_is_saved()
    {
        await using var w = await NewWorldAsync();
        await LinkAsync(w);
        var p = await w.X.ProductAsync("MED", 10m);
        var c = await w.X.ClientAsync("SIN");
        var draft = await w.X.DraftAsync(c, p, 1m);   // sin consignatario
        var requested = await w.X.Sales.RequestAsync(draft.PublicId, null, default);
        var d = await w.X.Sales.DispatchAsync(draft.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(requested.Lines[0].Id, 1m) }), default);
        Assert.Equal(TenantLinkRules.ConsigneeRequired, await MsgOf(() => w.Delivery.CreateAsync(draft.PublicId, DeliveryReq(d.PickBatchPublicId), default)));
        var empty = new SalesDeliveryRequest(d.PickBatchPublicId, new OrderCreateRequest(null, null, new OrderConsigneeCreateRequest("Farmacia", "Calle 2", null, "Ponce", null, null, "PR", null),
            null, null, null, new List<OrderPackageLineRequest>(), null, null, null, null, null, null, null));
        Assert.Equal(OrderRules.PackagesRequiredMessage, await MsgOf(() => w.Delivery.CreateAsync(draft.PublicId, empty, default)));
        Assert.Empty(await w.Db.Set<TenantLinkOrder>().AsNoTracking().ToListAsync());
        Assert.Equal("COLLECTED", Assert.Single((await w.X.Sales.GetAsync(draft.PublicId, default)).Dispatches!).Status);

        // la otra compañía apagó el módulo: no se puede entregar por ella (y tampoco se crea aquí a escondidas)
        using (w.F.AsTenant(B)) w.F.SetModules(ModuleKeys.Integrations, ModuleKeys.LtlGround);
        var withConsignee = new SalesDeliveryRequest(d.PickBatchPublicId, empty.Order! with { Packages = new List<OrderPackageLineRequest> { new("BOX", "Caja") } });
        Assert.Equal(TenantLinkRules.ProviderUnavailable("Advance Logistics"), await MsgOf(() => w.Delivery.CreateAsync(draft.PublicId, withConsignee, default)));
        Assert.Empty(await OrdersOfAsync(w, A));
    }
}
