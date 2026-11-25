using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): el panel Ventas del Pulso del día (pulse.sales; datos sales.view; módulo SALES_ORDERS) — pedidos de hoy, urgentes abiertos,
// clientes por aprobar y órdenes que esperan la aprobación del cliente.
public sealed class PulseSalesPanelTests
{
    [Fact]
    public void The_panel_is_in_the_pulse_registry_with_its_permission_data_and_module()
    {
        var panel = PulsePanels.Find("sales")!;
        Assert.Equal((PulsePanels.Sales, PermissionCatalog.PulseSales, ModuleKeys.SalesOrders, 13), (panel.Key, panel.Permission, panel.Module, panel.DefaultSortOrder));
        Assert.Equal(new[] { PermissionCatalog.SalesView }, panel.DataPermissions);
        Assert.Single(PermissionCatalog.All, p => p.Code == PermissionCatalog.PulseSales && p.Category == "PULSE");
        Assert.True(PulsePanels.CanSee(panel, c => c is PermissionCatalog.PulseSales or PermissionCatalog.SalesView, m => m == ModuleKeys.SalesOrders));
        Assert.False(PulsePanels.CanSee(panel, c => c == PermissionCatalog.PulseSales, _ => true));   // sin sales.view no se ve
    }

    [Fact]
    public async Task The_panel_counts_today_urgent_prospects_and_orders_waiting_for_approval()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var pulse = x.F.Get<SalesPulseService>();
        Assert.Equal((0, 0m, 0, 0, 0), Counts(await pulse.GetAsync(default)));

        var urgent = await x.Sales.RequestAsync((await x.DraftAsync(c, p, 2m, new SalesOrderRequest(IsUrgent: true))).PublicId, null, default);   // $20
        await x.DraftAsync(c, p, 1m);   // borrador de hoy: cuenta como pedido de hoy, no como urgente abierto
        var cancelled = await x.DraftAsync(c, p, 5m);
        await x.Sales.CancelAsync(cancelled.PublicId, "no", default);   // los cancelados no cuentan
        var prospect = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var prospectRow = await x.F.Db.Clients.AsNoTracking().SingleAsync(r => r.PublicId == prospect.PublicId);
        await x.Sales.RequestAsync((await x.DraftAsync(prospectRow, p, 3m)).PublicId, null, default);   // $30, espera aprobación

        var dto = await pulse.GetAsync(default);
        Assert.Equal((3, 60m, 1, 1, 1), Counts(dto));
        Assert.Null(dto.InvoicesToQuickBooks);   // lo suma la parte de QuickBooks
        Assert.Null(dto.ConsignmentUnpaid);      // lo suma la parte de consignación
        Assert.Equal("/clients", dto.Routes["clientsToApprove"]);
        Assert.Equal(SalesOrderStatuses.Requested, urgent.Status);

        await x.Prospects.ApproveAsync(prospect.PublicId, null, default);
        Assert.Equal((3, 60m, 1, 0, 0), Counts(await pulse.GetAsync(default)));
    }

    [Fact]
    public async Task Without_sales_view_the_panel_data_is_forbidden()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        x.F.SetPermissions(PermissionCatalog.PulseSales);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.F.Get<SalesPulseService>().GetAsync(default));
    }

    [Fact]
    public async Task With_route_quotas_the_month_quota_is_the_sum_of_the_active_route_quotas()
    {
        // Lote 244: el Pulso suma las cuotas según el ajuste QuotaBy (REP: representantes activos; ROUTE: rutas activas)
        await using var x = await SalesVendorWorld.CreateAsync(s =>
        {
            s.AddTransient<SalesAnalysisLineReader>();
            s.AddTransient<SalesAnalysisService>();
            s.AddTransient<SalesAnalysisSetupService>();
        });
        var setup = x.F.Get<SalesAnalysisSetupService>();
        var norte = await x.F.AddRouteAsync("Norte");
        var metro = await x.F.AddRouteAsync("Metro");
        var old = await x.F.AddRouteAsync("Vieja", isActive: false);
        var rep = await x.F.AddRepAsync("Ana", norte.SalesRouteId, null);
        var month = SalesAnalysisRules.MonthKey(SalesAnalysisRules.MonthStart(TenantClock.Default.Today));
        await setup.SetQuotasAsync(month, new SalesQuotaBulkRequest(new[]
        {
            new SalesQuotaItemRequest(rep.SalesRepId, 100m), new SalesQuotaItemRequest(null, 300m, norte.SalesRouteId),
            new SalesQuotaItemRequest(null, 200m, metro.SalesRouteId), new SalesQuotaItemRequest(null, 999m, old.SalesRouteId),
        }), default);
        Assert.Equal(100m, (await x.F.Get<SalesAnalysisService>().MonthSalesVsQuotaAsync(default)).Quota);   // REP, lo de siempre

        await setup.UpdateSettingsAsync(new SalesAnalysisSettingsRequest(QuotaBy: SalesRouteCodes.QuotaByRoute), default);
        Assert.Equal(500m, (await x.F.Get<SalesPulseService>().GetAsync(default)).MonthQuota);   // la ruta inactiva no cuenta
    }

    private static (int, decimal, int, int, int) Counts(PulseSalesPanelDto d) => (d.OrdersToday, d.OrdersTodayAmount, d.UrgentOpen, d.ClientsToApprove, d.AwaitingApproval);
}
