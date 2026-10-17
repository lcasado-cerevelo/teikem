using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Analytics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 88 (P12): los dos ríos del Pulso — «Paquetes en la calle» (órdenes) y «Dinero COD».
public sealed class PulseRiverTests
{
    private sealed class World(WmsFixture f, Client c, PulseRiverService rivers, CodService cod, CodRemittanceService rem)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public PulseRiverService Rivers { get; } = rivers;
        public CodService Cod { get; } = cod;
        public CodRemittanceService Rem { get; } = rem;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<PulseRiverService>();
            s.AddSingleton<CodService>();
            s.AddSingleton<ExportTemplateService>();
            s.AddSingleton<CodRemittanceService>();
        });
        f.SetModules(ModuleKeys.LtlGround, ModuleKeys.Cod);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<PulseRiverService>(), f.Get<CodService>(), f.Get<CodRemittanceService>());
    }

    private static async Task<TransportOrder> OrderAsync(World x, string number, string status, int createdDaysAgo = 0, bool active = true, Client? client = null)
    {
        var o = await x.F.AddOrderAsync((client ?? x.C).ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.CreatedAtUtc, t.IsActive) = (DateTime.UtcNow.AddDays(-createdDaysAgo), active);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static PulseRiverNodeDto Node(PulseRiverDto r, string key) => r.Nodes.Single(n => n.Key == key);
    private static decimal Detail(PulseRiverNodeDto n, string key) => n.Details.Single(d => d.Key == key).Value;

    [Fact]
    public async Task The_orders_river_counts_each_point_of_the_flow_and_keeps_the_seven_day_bars()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.Confirmed);                       // entrada de hoy, esperando
        await OrderAsync(x, "B", OrderStatuses.Pickup);                          // entrada de hoy, esperando
        var c = await OrderAsync(x, "C", OrderStatuses.Inbound, createdDaysAgo: 3);   // en almacén, sin chofer
        var d = await OrderAsync(x, "D", OrderStatuses.Inbound, createdDaysAgo: 3);   // en almacén, con ruta y chofer
        await OrderAsync(x, "E", OrderStatuses.Planned, createdDaysAgo: 5);      // planificada sin ruta
        await OrderAsync(x, "F", OrderStatuses.InTransit, createdDaysAgo: 1);
        await OrderAsync(x, "G", OrderStatuses.Arrived, createdDaysAgo: 1);
        var h = await OrderAsync(x, "H", OrderStatuses.Delivered, createdDaysAgo: 1);   // entregada hoy
        var i = await OrderAsync(x, "I", OrderStatuses.Delivered, createdDaysAgo: 4);   // entregada hace 2 días
        await OrderAsync(x, "J", OrderStatuses.Cancelled);                       // cancelada: no cuenta como entrada
        await OrderAsync(x, "K", OrderStatuses.Confirmed, active: false);        // dada de baja

        // una ruta despachada con chofer (D va en ella) y otra sin chofer
        var dispatched = x.F.StatusId(StatusDomains.TripStatus, TripStatuses.Dispatched);
        x.F.Db.Set<Trip>().AddRange(
            new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "T-1", PlanDate = TenantClock.Default.Today, StatusCodeId = dispatched, DriverId = 7, IsActive = true },
            new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "T-2", PlanDate = TenantClock.Default.Today, StatusCodeId = dispatched, DriverId = null, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        var tripWithDriver = await x.F.Db.Set<Trip>().AsNoTracking().SingleAsync(t => t.Code == "T-1");
        x.F.Db.Set<TripOrder>().Add(new TripOrder { TenantId = WmsFixture.TenantId, TripId = tripWithDriver.TripId, TransportOrderId = d.TransportOrderId, IsCurrent = true });
        var entityType = x.F.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder);
        var deliveredId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered);
        x.F.Db.EntityStatusHistories.AddRange(
            new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = entityType, EntityId = h.TransportOrderId, ToStatusCodeId = deliveredId, ChangedAtUtc = DateTime.UtcNow },
            new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = entityType, EntityId = i.TransportOrderId, ToStatusCodeId = deliveredId, ChangedAtUtc = DateTime.UtcNow.AddDays(-2) });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.NotNull(c);

        var river = await x.Rivers.OrdersAsync(default);
        Assert.Equal(("ORDERS", TenantClock.Default.Today), (river.River, river.Day));
        Assert.Equal(new[] { "ENTRY", "IN_WAREHOUSE", "TO_DISPATCH", "ON_ROUTE", "DELIVERED" }, river.Nodes.Select(n => n.Key).ToArray());
        var entry = Node(river, "ENTRY");
        Assert.Equal((2m, 2m), (entry.Value, Detail(entry, "waiting")));   // A y B hoy; A y B esperan
        Assert.Equal(PulseRiverService.SparkDays, entry.Spark!.Count);
        Assert.Equal(2m, entry.Spark[^1]);   // hoy
        Assert.Equal(2m, entry.Spark[^4]);   // hace 3 días (C y D)
        Assert.Equal(2m, Node(river, "IN_WAREHOUSE").Value);   // C y D
        Assert.Equal(2m, Node(river, "TO_DISPATCH").Value);    // C y E (D ya tiene ruta con chofer)
        var route = Node(river, "ON_ROUTE");
        Assert.Equal((2m, 1m), (route.Value, Detail(route, "drivers")));   // F y G; el único chofer con ruta en curso
        var delivered = Node(river, "DELIVERED");
        Assert.Equal(1m, delivered.Value);       // H hoy
        Assert.Equal(1m, delivered.Spark![^1]);
        Assert.Equal(1m, delivered.Spark[^3]);   // I hace 2 días
    }

    [Fact]
    public async Task The_cod_river_follows_the_money_from_to_collect_to_the_remittance_ready_to_pay()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("C2");
        async Task<TransportOrder> CodOrder(string number, string status, decimal cod, Client? client = null)
        {
            var o = await OrderAsync(x, number, status, client: client);
            var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
            (t.CodAmount, t.CodStatusCodeId) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending));
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();
            return t;
        }
        var o1 = await CodOrder("O1", OrderStatuses.Delivered, 100m);   // entregada, sin cobrar
        var o2 = await CodOrder("O2", OrderStatuses.Arrived, 50m);       // en destino, cobrada en parte
        var o3 = await CodOrder("O3", OrderStatuses.Delivered, 60m);     // cobrada con cheque, sin cuadrar
        var o4 = await CodOrder("O4", OrderStatuses.Delivered, 40m);     // cuadrada, sin remesa
        var o5 = await CodOrder("O5", OrderStatuses.Delivered, 30m, other);   // cuadrada y en una remesa
        await OrderAsync(x, "O6", OrderStatuses.InTransit);               // sin COD: no cuenta
        await x.Cod.CollectAsync(o2.PublicId, new CodCollectionRequest("CASH", 20m), default);
        await x.Cod.CollectAsync(o3.PublicId, new CodCollectionRequest("CHECK", 60m, CheckNumber: "5521"), default);
        await x.Cod.CollectAsync(o4.PublicId, new CodCollectionRequest("CASH", 40m), default);
        await x.Cod.CollectAsync(o5.PublicId, new CodCollectionRequest("CASH", 30m), default);
        await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("O4"), new CodReconcileCode("O5") }), default);
        await x.Rem.CreateAsync(new CodRemittanceRequest(other.PublicId, TenantClock.Default.Today.AddDays(-1), TenantClock.Default.Today.AddDays(1)), default);
        Assert.NotNull(o1);

        var river = await x.Rivers.CodAsync(default);
        Assert.Equal("COD", river.River);
        Assert.Equal(new[] { "TO_COLLECT", "COLLECTED", "RECONCILED", "REMITTANCE" }, river.Nodes.Select(n => n.Key).ToArray());
        var toCollect = Node(river, "TO_COLLECT");
        Assert.Equal(("count", 2m, 130m), (toCollect.Kind, toCollect.Value, Detail(toCollect, "amount")));   // O1 (100) y lo que falta de O2 (30)
        var collected = Node(river, "COLLECTED");
        Assert.Equal(("money", 80m, 20m, 60m, 0m), (collected.Kind, collected.Value, Detail(collected, "cash"), Detail(collected, "checks"), Detail(collected, "other")));   // O2 20 efectivo + O3 60 cheque
        var reconciled = Node(river, "RECONCILED");
        Assert.Equal((40m, 1m), (reconciled.Value, Detail(reconciled, "clients")));   // O4; O5 ya está en una remesa
        var remittance = Node(river, "REMITTANCE");
        Assert.Equal((27.6m, 1m, 1m), (remittance.Value, Detail(remittance, "remittances"), Detail(remittance, "clients")));   // 30 − 8 % de comisión
    }

    [Fact]
    public void The_panels_and_the_endpoints_are_registered_with_their_permissions_and_modules()
    {
        var orders = PulsePanels.Find("ORDERS_RIVER")!;
        Assert.Equal((PermissionCatalog.PulseOrders, 10, ModuleKeys.LtlGround), (orders.Permission, orders.DefaultSortOrder, orders.Module));
        Assert.Equal(new[] { PermissionCatalog.OrdersView }, orders.DataPermissions);
        var cod = PulsePanels.Find("COD_RIVER")!;
        Assert.Equal((PermissionCatalog.PulseCod, 11, ModuleKeys.Cod), (cod.Permission, cod.DefaultSortOrder, cod.Module));
        Assert.Equal(new[] { PermissionCatalog.CodView }, cod.DataPermissions);
        Assert.False(PulsePanels.CanSee(cod, p => p == PermissionCatalog.PulseCod, _ => true));   // sin cod.view no
        Assert.True(PulsePanels.CanSee(cod, p => p is PermissionCatalog.PulseCod or PermissionCatalog.CodView, m => m == ModuleKeys.Cod));

        string Policy(string action) => typeof(PulseRiversController).GetMethod(action)!.GetCustomAttributes<RequirePermissionAttribute>().Single().Policy;
        string Module(string action) => typeof(PulseRiversController).GetMethod(action)!.GetCustomAttributes<RequireModuleAttribute>().Single().ModuleKey;
        Assert.Equal((RequirePermissionAttribute.Prefix + PermissionCatalog.OrdersView, ModuleKeys.LtlGround), (Policy(nameof(PulseRiversController.Orders)), Module(nameof(PulseRiversController.Orders))));
        Assert.Equal((RequirePermissionAttribute.Prefix + PermissionCatalog.CodView, ModuleKeys.Cod), (Policy(nameof(PulseRiversController.Cod)), Module(nameof(PulseRiversController.Cod))));
    }

    [Fact]
    public async Task Another_company_sees_empty_rivers()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.Confirmed);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var orders = await x.Rivers.OrdersAsync(default);
            Assert.All(orders.Nodes, n => Assert.Equal(0m, n.Value));
            var cod = await x.Rivers.CodAsync(default);
            Assert.All(cod.Nodes, n => Assert.Equal(0m, n.Value));
        }
    }
}
