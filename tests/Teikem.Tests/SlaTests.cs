using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 122 (P12): el SLA de las órdenes (nivel de servicio del contrato) y el aviso «SLA en riesgo» de «Necesita tu atención».
public sealed class SlaTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(48, -10, OrderStatuses.InTransit, SlaStates.OnTrack)]       // lleva 10 h de 48
    [InlineData(48, -35, OrderStatuses.InTransit, SlaStates.OnTrack)]       // le quedan 13 h: fuera del 25 % (12 h)
    [InlineData(48, -37, OrderStatuses.InTransit, SlaStates.AtRisk)]        // le quedan 11 h: dentro del 25 %
    [InlineData(48, -40, OrderStatuses.Assigned, SlaStates.AtRisk)]         // le quedan 8 h (< 25 % de 48)
    [InlineData(48, -49, OrderStatuses.Failed, SlaStates.Breached)]         // ya lo pasó
    [InlineData(2, -1.5, OrderStatuses.InTransit, SlaStates.AtRisk)]        // plazo corto: al menos 1 h de ventana
    [InlineData(4, -2, OrderStatuses.InTransit, SlaStates.OnTrack)]
    public void Open_orders_are_on_track_at_risk_or_breached(int max, double startHoursAgo, string status, string expected)
        => Assert.Equal(expected, SlaRules.Evaluate(max, Now.AddHours(startHoursAgo), null, status, Now).State);

    [Fact]
    public void Delivered_orders_are_met_or_missed_by_when_they_were_delivered()
    {
        var start = Now.AddHours(-100);
        Assert.Equal(SlaStates.Met, SlaRules.Evaluate(48, start, start.AddHours(47), OrderStatuses.Delivered, Now).State);
        Assert.Equal(SlaStates.Met, SlaRules.Evaluate(48, start, start.AddHours(48), OrderStatuses.Delivered, Now).State);
        var late = SlaRules.Evaluate(48, start, start.AddHours(49), OrderStatuses.Delivered, Now);
        Assert.Equal((SlaStates.Missed, (double?)null), (late.State, late.HoursLeft));
    }

    [Theory]
    [InlineData(null, OrderStatuses.InTransit)]      // el servicio no tiene SLA
    [InlineData(0, OrderStatuses.InTransit)]
    [InlineData(48, OrderStatuses.Submitted)]        // todavía no se recoge: el reloj no corre
    [InlineData(48, OrderStatuses.Cancelled)]
    public void Orders_without_a_running_clock_have_no_sla(int? max, string status)
        => Assert.Equal(SlaStates.NoSla, SlaRules.Evaluate(max, Now.AddHours(-5), null, status, Now).State);

    [Fact]
    public void Without_a_start_there_is_no_sla()
        => Assert.Equal(SlaStates.NoSla, SlaRules.Evaluate(48, null, null, OrderStatuses.InTransit, Now).State);

    private sealed class World(WmsFixture f, Client c, SlaService sla, int contractId)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public SlaService Sla { get; } = sla;
        public int ContractId { get; } = contractId;
    }

    private static async Task<World> NewWorldAsync(int? maxHours = 48)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<SlaService>();
        });
        f.SetModules(ModuleKeys.LtlGround);
        var c = await f.AddClientAsync("C1");
        var contract = new Contract
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, ContractNumber = "K-1", Title = "Contrato",
            StartDate = new DateOnly(2026, 1, 1), StatusCodeId = f.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), IsActive = true,
        };
        f.Db.Set<Contract>().Add(contract);
        await f.Db.SaveChangesAsync();
        f.Db.Set<ContractServiceLevel>().Add(new ContractServiceLevel { ContractId = contract.ContractId, ServiceTypeLookupId = 1, MaxTransitHours = maxHours, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, c, f.Get<SlaService>(), contract.ContractId);
    }

    /// <summary>Una orden del contrato en el estatus dado, recogida hace `pickedHoursAgo` horas (con su historial) y entregada `deliveredHoursAgo` horas atrás si se indica.</summary>
    private static async Task<TransportOrder> OrderAsync(World x, string number, string status, double pickedHoursAgo, double? deliveredHoursAgo = null, bool withContract = true)
    {
        var o = await x.F.AddOrderAsync(x.C.ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        if (withContract) t.ContractId = x.ContractId;
        var typeId = x.F.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder);
        x.F.Db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = typeId, EntityId = t.TransportOrderId, ToStatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Pickup), ChangedAtUtc = DateTime.UtcNow.AddHours(-pickedHoursAgo) });
        if (deliveredHoursAgo is double d)
            x.F.Db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = typeId, EntityId = t.TransportOrderId, ToStatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered), ChangedAtUtc = DateTime.UtcNow.AddHours(-d) });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    [Fact]
    public async Task The_service_reads_the_contract_level_and_the_status_history()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var ok = await OrderAsync(x, "A", OrderStatuses.InTransit, pickedHoursAgo: 5);
        var risk = await OrderAsync(x, "B", OrderStatuses.Assigned, pickedHoursAgo: 40);
        var late = await OrderAsync(x, "C", OrderStatuses.InTransit, pickedHoursAgo: 60);
        var met = await OrderAsync(x, "D", OrderStatuses.Delivered, pickedHoursAgo: 60, deliveredHoursAgo: 30);          // 30 h de tránsito
        var missed = await OrderAsync(x, "E", OrderStatuses.Delivered, pickedHoursAgo: 100, deliveredHoursAgo: 10);      // 90 h de tránsito
        var noContract = await OrderAsync(x, "F", OrderStatuses.InTransit, pickedHoursAgo: 60, withContract: false);
        var all = new[] { ok, risk, late, met, missed, noContract };
        var input = all.Select(o => new SlaOrderInput(o.TransportOrderId, o.ContractId == null && o == noContract ? null : x.ContractId, o.ServiceTypeLookupId, o.CreatedAtUtc, null,
            o == met || o == missed ? OrderStatuses.Delivered : o == risk ? OrderStatuses.Assigned : OrderStatuses.InTransit)).ToList();
        input[5] = input[5] with { ContractId = null };

        var res = await x.Sla.EvaluateAsync(input, DateTime.UtcNow, default);
        Assert.Equal(new[] { SlaStates.OnTrack, SlaStates.AtRisk, SlaStates.Breached, SlaStates.Met, SlaStates.Missed, SlaStates.NoSla }, all.Select(o => res[o.TransportOrderId].State).ToArray());
        Assert.Equal(48, res[ok.TransportOrderId].MaxHours);
        Assert.InRange(res[risk.TransportOrderId].HoursLeft!.Value, 7.5, 8.5);
        Assert.Empty(await x.Sla.EvaluateAsync(Array.Empty<SlaOrderInput>(), DateTime.UtcNow, default));
    }

    [Fact]
    public async Task A_service_level_without_max_hours_gives_no_sla()
    {
        var x = await NewWorldAsync(maxHours: null);
        await using var _ = x.F;
        var o = await OrderAsync(x, "A", OrderStatuses.InTransit, pickedHoursAgo: 500);
        var res = await x.Sla.EvaluateAsync(new[] { new SlaOrderInput(o.TransportOrderId, x.ContractId, o.ServiceTypeLookupId, o.CreatedAtUtc, null, OrderStatuses.InTransit) }, DateTime.UtcNow, default);
        Assert.Equal(SlaStates.NoSla, res[o.TransportOrderId].State);
    }

    [Fact]
    public async Task The_notice_is_one_aggregated_row_with_counts_and_goes_away_when_the_order_is_delivered()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.InTransit, pickedHoursAgo: 5);                 // a tiempo: no cuenta
        await OrderAsync(x, "B", OrderStatuses.Assigned, pickedHoursAgo: 40);                 // en riesgo
        var late = await OrderAsync(x, "C", OrderStatuses.InTransit, pickedHoursAgo: 60);     // vencida
        await OrderAsync(x, "D", OrderStatuses.Delivered, pickedHoursAgo: 100, deliveredHoursAgo: 10);   // entregada tarde: ya no es aviso
        await OrderAsync(x, "E", OrderStatuses.InTransit, pickedHoursAgo: 60, withContract: false);      // sin contrato: sin SLA
        var provider = new SlaAtRiskAttentionProvider(x.F.Db, x.Sla);
        var scope = new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.LtlGround });

        var res = await provider.ReadAsync(scope, 5, default);
        var item = Assert.Single(res.Items);
        Assert.Equal((1, 2, "danger", "/orders"), (res.Total, item.Count, item.Tone, item.Route));
        Assert.Equal(("1", "1"), (item.Params["atRisk"], item.Params["breached"]));
        Assert.True(double.Parse(item.Params["minHours"], System.Globalization.CultureInfo.InvariantCulture) < 0);   // la más apretada ya está vencida
        Assert.Equal(("OPERATIONS", PermissionCatalog.OrdersView, ModuleKeys.LtlGround), (provider.BusinessModule, provider.RequiredPermission, provider.TenantModule));

        // la vencida se entrega: queda solo la que está en riesgo (tono warn)
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == late.TransportOrderId);
        t.StatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var after = Assert.Single((await provider.ReadAsync(scope, 5, default)).Items);
        Assert.Equal((1, "warn", "1", "0"), (after.Count, after.Tone, after.Params["atRisk"], after.Params["breached"]));
        Assert.Empty((await provider.ReadAsync(scope, 0, default)).Items);   // take 0
    }

    [Fact]
    public async Task No_notice_when_nothing_is_at_risk()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.InTransit, pickedHoursAgo: 5);
        var res = await new SlaAtRiskAttentionProvider(x.F.Db, x.Sla).ReadAsync(new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.LtlGround }), 5, default);
        Assert.Equal(0, res.Total);
        Assert.Empty(res.Items);
    }
}
