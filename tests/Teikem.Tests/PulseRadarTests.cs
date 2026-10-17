using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Analytics;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 89 (P12): Radar de órdenes abiertas y los avisos «órdenes sin chofer» y «COD sin cuadrar» de «Necesita tu atención».
public sealed class PulseRadarTests
{
    private sealed class World(WmsFixture f, Client c, PulseRadarService radar, PulseRiverService rivers, CodService cod)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public PulseRadarService Radar { get; } = radar;
        public PulseRiverService Rivers { get; } = rivers;
        public CodService Cod { get; } = cod;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<PulseRadarService>();
            s.AddSingleton<PulseRiverService>();
            s.AddSingleton<CodService>();
        });
        f.SetModules(ModuleKeys.LtlGround, ModuleKeys.Cod);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<PulseRadarService>(), f.Get<PulseRiverService>(), f.Get<CodService>());
    }

    /// <summary>Una orden con su parada de entrega en `town`, el COD indicado y los días desde que se creó.</summary>
    private static async Task<TransportOrder> OrderAsync(World x, string number, string status, string town, decimal? cod = null, Client? client = null, int createdDaysAgo = 0, bool active = true)
    {
        var o = await x.F.AddOrderAsync((client ?? x.C).ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.IsActive, t.CreatedAtUtc) = (active, DateTime.UtcNow.AddDays(-createdDaysAgo));
        if (cod is not null) (t.CodAmount, t.CodStatusCodeId) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending));
        x.F.Db.Set<OrderStop>().Add(new OrderStop { TransportOrderId = t.TransportOrderId, StopTypeLookupId = x.F.LookupId(LookupDomains.StopType, StopTypes.Delivery), Sequence = 2, SnapLine1 = "Calle 1", SnapCity = town, StatusCodeId = 1 });
        x.F.Db.Set<OrderStop>().Add(new OrderStop { TransportOrderId = t.TransportOrderId, StopTypeLookupId = x.F.LookupId(LookupDomains.StopType, StopTypes.Pickup), Sequence = 1, SnapLine1 = "Origen", SnapCity = "San Juan", StatusCodeId = 1 });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public void Rules_group_by_and_take_are_normalized()
    {
        Assert.Equal("TOWN", RadarRules.NormalizeGroupBy(null));
        Assert.Equal("TOWN", RadarRules.NormalizeGroupBy(" town "));
        Assert.Equal("CLIENT", RadarRules.NormalizeGroupBy("client"));
        Assert.Null(RadarRules.NormalizeGroupBy("zip"));
        Assert.Equal((6, 1, 20), (RadarRules.ClampTake(0), RadarRules.ClampTake(1), RadarRules.ClampTake(500)));
        Assert.DoesNotContain(OrderStatuses.Draft, RadarRules.OpenStatuses);
        Assert.DoesNotContain(OrderStatuses.Delivered, RadarRules.OpenStatuses);
        Assert.DoesNotContain(OrderStatuses.Cancelled, RadarRules.OpenStatuses);
    }

    [Fact]
    public async Task The_radar_counts_only_open_orders_and_groups_them_by_town_or_client()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("C2");
        await OrderAsync(x, "A", OrderStatuses.Confirmed, "Ponce", cod: 30m);
        await OrderAsync(x, "B", OrderStatuses.InTransit, "Ponce", cod: 20m);
        await OrderAsync(x, "C", OrderStatuses.Inbound, "Caguas", client: other);
        await OrderAsync(x, "D", OrderStatuses.Arrived, "Ponce", client: other);
        await OrderAsync(x, "E", OrderStatuses.Delivered, "Ponce", cod: 99m);       // entregada: no estorba
        await OrderAsync(x, "F", OrderStatuses.Draft, "Ponce");                       // borrador: todavía no es una orden en curso
        await OrderAsync(x, "G", OrderStatuses.Cancelled, "Ponce");
        await OrderAsync(x, "H", OrderStatuses.Confirmed, "Ponce", active: false);   // dada de baja

        var byTown = await x.Radar.RadarAsync(null, 0, default);
        Assert.Equal(("TOWN", 4, 50m), (byTown.GroupBy, byTown.OpenCount, byTown.CodAmount));
        Assert.Equal(new[] { ("Ponce", 3, 50m), ("Caguas", 1, 0m) }, byTown.Groups.Select(g => (g.Key, g.Count, g.CodAmount)).ToArray());
        Assert.Contains(OrderStatuses.InTransit, byTown.OpenStatuses);

        var byClient = await x.Radar.RadarAsync("client", 6, default);
        Assert.Equal(new[] { ("Cliente C1", 2, 50m), ("Cliente C2", 2, 0m) }, byClient.Groups.OrderBy(g => g.Label).Select(g => (g.Label, g.Count, g.CodAmount)).ToArray());
        Assert.All(byClient.Groups, g => Assert.True(Guid.TryParse(g.Key, out var guid) && guid != Guid.Empty));   // la clave es el publicId del cliente

        Assert.Single((await x.Radar.RadarAsync("TOWN", 1, default)).Groups);   // el tope de grupos
        Assert.Equal(RadarRules.GroupByInvalid, await MsgOf(() => x.Radar.RadarAsync("zip", 6, default)));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Equal(0, (await x.Radar.RadarAsync(null, 6, default)).OpenCount);
    }

    [Fact]
    public async Task The_no_driver_notice_is_one_aggregated_row_and_goes_away_when_the_order_has_a_route_with_driver()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var a = await OrderAsync(x, "A", OrderStatuses.Inbound, "Ponce", createdDaysAgo: 3);
        await OrderAsync(x, "B", OrderStatuses.Planned, "Ponce");
        var provider = new OrdersNoDriverAttentionProvider(x.Rivers);
        var scope = new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.LtlGround });

        var res = await provider.ReadAsync(scope, 5, default);
        var item = Assert.Single(res.Items);
        Assert.Equal((1, 2, "danger", "/ops/dispatch"), (res.Total, item.Count, item.Tone, item.Route));   // la más vieja lleva 3 días
        Assert.Equal(("2", "3"), (item.Params["count"], item.Params["oldestDays"]));
        Assert.Equal(("OPERATIONS", PermissionCatalog.TripsDispatch, ModuleKeys.LtlGround), (provider.BusinessModule, provider.RequiredPermission, provider.TenantModule));

        // A entra a una ruta despachada con chofer: queda solo B (más nueva, tono warn)
        var dispatched = x.F.StatusId(StatusDomains.TripStatus, TripStatuses.Dispatched);
        x.F.Db.Set<Trip>().Add(new Trip { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "T-1", PlanDate = TenantClock.Default.Today, StatusCodeId = dispatched, DriverId = 7, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        var trip = await x.F.Db.Set<Trip>().AsNoTracking().SingleAsync();
        x.F.Db.Set<TripOrder>().Add(new TripOrder { TenantId = WmsFixture.TenantId, TripId = trip.TripId, TransportOrderId = a.TransportOrderId, IsCurrent = true });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var after = Assert.Single((await provider.ReadAsync(scope, 5, default)).Items);
        Assert.Equal((1, "warn"), (after.Count, after.Tone));
        Assert.Empty((await new OrdersNoDriverAttentionProvider(x.Rivers).ReadAsync(scope, 0, default)).Items);   // take 0
    }

    [Fact]
    public async Task The_cod_unreconciled_notice_adds_up_what_was_collected_and_not_yet_reconciled()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var provider = new CodUnreconciledAttentionProvider(x.F.Db);
        var scope = new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.Cod });
        Assert.Equal(0, (await provider.ReadAsync(scope, 5, default)).Total);   // nada cobrado

        var a = await OrderAsync(x, "A", OrderStatuses.Delivered, "Ponce", cod: 100m);
        var b = await OrderAsync(x, "B", OrderStatuses.Delivered, "Ponce", cod: 50m);
        await x.Cod.CollectAsync(a.PublicId, new CodCollectionRequest("CASH", 60m), default);
        await x.Cod.CollectAsync(a.PublicId, new CodCollectionRequest("CHECK", 40m, CheckNumber: "5521"), default);
        await x.Cod.CollectAsync(b.PublicId, new CodCollectionRequest("CASH", 50m, CollectedAtUtc: DateTime.UtcNow.AddDays(-4)), default);

        var res = await provider.ReadAsync(scope, 5, default);
        var item = Assert.Single(res.Items);
        Assert.Equal(("150.00", "3", "2", "4"), (item.Params["amount"], item.Params["collections"], item.Params["orders"], item.Params["oldestDays"]));
        Assert.Equal(("danger", 3, "/money/cod", "reconcile"), (item.Tone, item.Count, item.Route, item.Query!["tab"]));   // el cobro más viejo lleva 4 días

        await x.Cod.ReconcileAsync(new CodReconcileRequest(new[] { new CodReconcileCode("A") }), default);   // A queda cuadrada
        var left = Assert.Single((await provider.ReadAsync(scope, 5, default)).Items);
        Assert.Equal(("50.00", "1"), (left.Params["amount"], left.Params["collections"]));
        Assert.Equal(("ACCOUNTING", PermissionCatalog.CodReconcile, ModuleKeys.Cod), (provider.BusinessModule, provider.RequiredPermission, provider.TenantModule));
    }

    [Fact]
    public void Summary_rows_go_first_in_the_attention_feed_and_then_the_oldest_ones()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new AttentionItemDto("RENTAL_DUE", "WAREHOUSE", "danger", 1, new Dictionary<string, string>(), null, null, old),
            new AttentionItemDto("RENTAL_DUE", "WAREHOUSE", "danger", 1, new Dictionary<string, string>(), null, null, old.AddDays(1)),
            new AttentionItemDto("ORDERS_NO_DRIVER", "OPERATIONS", "warn", 8, new Dictionary<string, string>(), null, null, old.AddDays(300), Summary: true),
        };
        var top = AttentionFeedService.Oldest(rows, 2);
        Assert.Equal(new[] { "ORDERS_NO_DRIVER", "RENTAL_DUE" }, top.Select(i => i.Code).ToArray());
        Assert.Equal(old, top[1].SinceUtc);   // tras el resumen, la más antigua
    }

    [Fact]
    public void The_radar_panel_and_endpoint_are_registered_with_the_orders_permissions()
    {
        var radar = PulsePanels.Find("RADAR")!;
        Assert.Equal((PermissionCatalog.PulseOrders, 60, ModuleKeys.LtlGround), (radar.Permission, radar.DefaultSortOrder, radar.Module));
        Assert.Equal(new[] { PermissionCatalog.OrdersView }, radar.DataPermissions);
        var m = typeof(PulseRiversController).GetMethod(nameof(PulseRiversController.Radar))!;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.OrdersView, m.GetCustomAttributes<RequirePermissionAttribute>().Single().Policy);
        Assert.Equal(ModuleKeys.LtlGround, m.GetCustomAttributes<RequireModuleAttribute>().Single().ModuleKey);
    }
}
