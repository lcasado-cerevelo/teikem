using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 125 (P12): alertas operacionales de «Necesita tu atención» (intento fallido, reclamaciones, documentos de flota, facturas vencidas) y el SLA como indicador/gráfico.
public sealed class OperationalAlertTests
{
    private static readonly AttentionScope Scope = new(WmsFixture.TenantId, 1, "es", new HashSet<string> { ModuleKeys.LtlGround, ModuleKeys.Billing, ModuleKeys.Catalog });

    private static async Task<(WmsFixture F, Client C)> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<SlaService>();
        });
        f.SetModules(ModuleKeys.LtlGround, ModuleKeys.Billing);
        return (f, await f.AddClientAsync("C1"));
    }

    [Fact]
    public async Task Failed_orders_are_one_row_dated_from_the_failed_attempt_and_urgent_after_two_days()
    {
        var (f, c) = await NewWorldAsync();
        await using var _ = f;
        var provider = new OrdersFailedAttentionProvider(f.Db, f.Get<ILookupCache>());
        Assert.Empty((await provider.ReadAsync(Scope, 5, default)).Items);              // sin fallidas: sin fila

        var o = await f.AddOrderAsync(c.ClientId, "A", OrderStatuses.Failed);
        await f.AddOrderAsync(c.ClientId, "B", OrderStatuses.InTransit);                // otra orden: no cuenta
        f.Db.EntityStatusHistories.Add(new EntityStatusHistory
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = f.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder), EntityId = o.TransportOrderId,
            ToStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Failed), ChangedAtUtc = DateTime.UtcNow.AddDays(-3),
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var item = Assert.Single((await provider.ReadAsync(Scope, 5, default)).Items);
        Assert.Equal((1, "danger", "/orders", "3"), (item.Count, item.Tone, item.Route, item.Params["oldestDays"]));
        Assert.Equal(OrderStatuses.Failed, item.Query!["statuses"]);
        Assert.Equal(("OPERATIONS", PermissionCatalog.OrdersView, ModuleKeys.LtlGround), (provider.BusinessModule, provider.RequiredPermission, provider.TenantModule));
        Assert.Empty((await provider.ReadAsync(Scope, 0, default)).Items);              // take 0
    }

    [Fact]
    public async Task Open_claims_count_only_the_unresolved_and_turn_urgent_after_a_week()
    {
        var (f, c) = await NewWorldAsync();
        await using var _ = f;
        var provider = new ClaimsOpenAttentionProvider(f.Db);
        var order = await f.AddOrderAsync(c.ClientId, "A", OrderStatuses.Delivered);
        Claim Make(string n, int daysAgo, string? resolution, decimal? value) => new()
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Number = n, TransportOrderId = order.TransportOrderId, ClientId = c.ClientId, StatusCodeId = 1,
            Description = "1 × Caja", DeclaredValue = value, Resolution = resolution, CreatedAtUtc = DateTime.UtcNow.AddDays(-daysAgo), IsActive = true,
        };
        f.Db.Set<Claim>().AddRange(Make("REC-1", 2, null, 50m), Make("REC-2", 9, null, 25.5m), Make("REC-3", 30, ClaimResolutions.Paid, 100m));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var item = Assert.Single((await provider.ReadAsync(Scope, 5, default)).Items);
        Assert.Equal((2, "danger", "/ops/claims"), (item.Count, item.Tone, item.Route));            // la resuelta no cuenta; la de 9 días vuelve urgente
        Assert.Equal(("9", "75.50"), (item.Params["oldestDays"], item.Params["amount"]));
        Assert.Equal(PermissionCatalog.ClaimsView, provider.RequiredPermission);
    }

    [Fact]
    public async Task Overdue_invoices_are_one_danger_row_with_the_oldest_in_days()
    {
        var (f, c) = await NewWorldAsync();
        await using var _ = f;
        var provider = new InvoicesOverdueAttentionProvider(f.Db, TenantClock.Default);
        var today = TenantClock.Default.Today;
        Invoice Make(string n, string status, DateOnly due) => new()
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = n, StatusCodeId = f.StatusId(BillingCodes.InvoiceStatusDomain, status),
            IssueDate = due.AddDays(-30), DueDate = due, CurrencyLookupId = 1, Subtotal = 100m, TaxAmount = 10m, IsActive = true,
        };
        f.Db.Set<Invoice>().AddRange(Make("FAC-1", InvoiceStatuses.Overdue, today.AddDays(-12)), Make("FAC-2", InvoiceStatuses.Overdue, today.AddDays(-3)), Make("FAC-3", InvoiceStatuses.Issued, today.AddDays(5)));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var item = Assert.Single((await provider.ReadAsync(Scope, 5, default)).Items);
        Assert.Equal((2, "danger", "/money/billing", "12"), (item.Count, item.Tone, item.Route, item.Params["oldestDays"]));
        Assert.Equal(("ACCOUNTING", PermissionCatalog.BillingView, ModuleKeys.Billing), (provider.BusinessModule, provider.RequiredPermission, provider.TenantModule));
    }

    [Fact]
    public async Task Fleet_documents_alert_is_silent_without_documents_and_gated_by_fleet_view()
    {
        var (f, _) = await NewWorldAsync();
        await using var _f = f;
        var provider = new FleetDocumentsDueAttentionProvider(f.Db, TenantClock.Default);
        Assert.Empty((await provider.ReadAsync(Scope, 5, default)).Items);
        Assert.Equal((PermissionCatalog.FleetView, "/catalog/fleet"), (provider.RequiredPermission, FleetDocumentsDueAttentionProvider.Route));
    }

    [Fact]
    public async Task The_sla_data_source_fields_and_the_seeded_filters_agree_and_the_percentage_averages_met_vs_missed()
    {
        var (f, c) = await NewWorldAsync();
        await using var _ = f;
        // el campo está declarado y los filtros del contenido de fábrica solo usan campos que la fuente expone
        var fields = new TransportOrderDataSource(null!, null!, null!).Fields.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var json in new[] { SystemAnalyticsSeeder.OrdersSlaRiskFilter, SystemAnalyticsSeeder.OrdersSlaDeliveredFilter, SystemAnalyticsSeeder.OrdersWithSlaFilter })
            foreach (Match m in Regex.Matches(json, "\"field\":\"(\\w+)\""))
                Assert.Contains(m.Groups[1].Value, fields);
        Assert.Contains("SlaMetPct", fields);

        // un contrato con SLA de 48 h: una orden entregada a las 30 h (cumplida) y otra a las 90 h (fuera) → rows con 100 y 0
        var contract = new Contract { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, ContractNumber = "K-1", Title = "K", StartDate = new DateOnly(2026, 1, 1), StatusCodeId = f.StatusId(StatusDomains.ContractStatus, ContractStatuses.Active), IsActive = true };
        f.Db.Set<Contract>().Add(contract);
        await f.Db.SaveChangesAsync();
        f.Db.Set<ContractServiceLevel>().Add(new ContractServiceLevel { ContractId = contract.ContractId, ServiceTypeLookupId = 1, MaxTransitHours = 48, IsActive = true });
        await f.Db.SaveChangesAsync();
        var typeId = f.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder);
        foreach (var (n, picked, delivered) in new[] { ("A", 60, 30), ("B", 100, 10) })
        {
            var o = await f.AddOrderAsync(c.ClientId, n, OrderStatuses.Delivered);
            var t = await f.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
            t.ContractId = contract.ContractId;
            f.Db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = typeId, EntityId = t.TransportOrderId, ToStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Pickup), ChangedAtUtc = DateTime.UtcNow.AddHours(-picked) });
            f.Db.EntityStatusHistories.Add(new EntityStatusHistory { TenantId = WmsFixture.TenantId, EntityTypeLookupId = typeId, EntityId = t.TransportOrderId, ToStatusCodeId = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered), ChangedAtUtc = DateTime.UtcNow.AddHours(-delivered) });
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();

        var source = new TransportOrderDataSource(f.Db, f.Get<ILookupCache>(), f.Tenant, null, f.Get<SlaService>());
        var rows = (await source.LoadAsync(new DataQuery(), default)).ToDictionary(r => (string)r["OrderNumber"]!);
        Assert.Equal((SlaStates.Met, 100m), ((string)rows["A"]["SlaState"]!, (decimal)rows["A"]["SlaMetPct"]!));
        Assert.Equal((SlaStates.Missed, 0m), ((string)rows["B"]["SlaState"]!, (decimal)rows["B"]["SlaMetPct"]!));
    }
}
