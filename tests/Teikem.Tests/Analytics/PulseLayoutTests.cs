using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Analytics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests.Analytics;

/// <summary>
/// Lote F8a (P1) — Pulso del día por paneles: permisos pulse.* por panel, regla de lectura de indicadores y gráficos (fuente
/// legible por EntityType y módulo de negocio encendido, loteF8-plan.md §2.2), orden en dos niveles (compañía / usuario) y los
/// mensajes exactos del plan. AnalyticsService real sobre InMemory (WmsFixture: PermissionService y ModuleService reales leídos
/// de la caché), con fuentes de datos falsas de 3 filas (COUNT = 3). Los 8 casos que exige el plan, en orden.
/// </summary>
public class PulseLayoutTests
{
    private const int Me = 1;
    private const int Other = 2;

    /// <summary>Fuente de datos de prueba: la clave es el código EntityType (como las reales) y devuelve 3 filas.</summary>
    private sealed class FakeSource(string key, string module) : IDataSource
    {
        public string Key => key;
        public string LabelEs => key;
        public string LabelEn => key;
        public string? EntityTypeCode => null;
        public string? DateField => null;
        public string IdField => "Id";
        public string DefaultBusinessModule => module;
        public IReadOnlyList<DataField> Fields { get; } = new[] { new DataField("Label", "Etiqueta", "Label", DataFieldType.Text) };
        public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

        public Task<List<DataRow>> LoadAsync(DataQuery query, CancellationToken ct)
            => Task.FromResult(Enumerable.Range(1, 3).Select(i => new DataRow { ["Id"] = i, ["Label"] = i == 3 ? "B" : "A" }).ToList());
    }

    private sealed class Fx : IAsyncDisposable
    {
        public required WmsFixture F { get; init; }
        public AnalyticsService Svc => F.Get<AnalyticsService>();

        public static async Task<Fx> CreateAsync()
        {
            var f = await WmsFixture.CreateAsync(s =>
            {
                s.AddSingleton<IDataSource>(new FakeSource(EntityTypes.TransportOrder, BusinessModules.Operations));
                s.AddSingleton<IDataSource>(new FakeSource(EntityTypes.StockBalance, BusinessModules.Warehouse));
                s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
                s.AddSingleton<AnalyticsEngine>();
                s.AddTransient<AnalyticsService>();
            });
            var id = 7000;
            LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            f.Db.LookupCodes.AddRange(
                L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.ReportVisibility, ReportVisibilities.Private),
                L(LookupDomains.AggregateFn, AggregateFns.Count), L(LookupDomains.ReportChartType, ChartTypes.Bar),
                L(LookupDomains.BusinessModule, BusinessModules.Operations), L(LookupDomains.BusinessModule, BusinessModules.Warehouse),
                L(LookupDomains.BusinessModule, BusinessModules.Accounting),
                L(LookupDomains.DateRangeMode, DateRangeModes.Last7), L(LookupDomains.DateRangeMode, DateRangeModes.Last30));
            await f.Db.SaveChangesAsync();
            f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
            f.Db.ChangeTracker.Clear();
            f.SetModules(ModuleKeys.Analytics, ModuleKeys.WmsLotSerial, ModuleKeys.LtlGround);
            return new Fx { F = f };
        }

        /// <summary>Solo estos permisos para el usuario activo (deja de ser administrador de plataforma).</summary>
        public void As(int userId, params string[] perms)
        {
            F.Tenant.UserId = userId;
            F.SetPermissions(perms);
            F.Db.ChangeTracker.Clear();
        }

        public async Task<int> IndicatorAsync(string name, string source, string module, bool showInPulse = true, int sort = 100)
        {
            var i = new IndicatorDefinition
            {
                TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = source, IsSystem = true, ShowInPulse = showInPulse, SortOrder = sort,
                AggregateFnLookupId = F.Lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Count).Result,
                BusinessModuleLookupId = F.Lookups.GetIdAsync(LookupDomains.BusinessModule, module).Result,
                VisibilityLookupId = F.Lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Tenant).Result,
            };
            F.Db.IndicatorDefinitions.Add(i);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return i.IndicatorDefinitionId;
        }

        public async Task<int> ChartAsync(string name, string source, string module, bool showInPulse = true, int sort = 100)
        {
            var c = new ChartDefinition
            {
                TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = source, GroupByField = "Label", IsSystem = true, ShowInPulse = showInPulse, SortOrder = sort,
                AggregateFnLookupId = F.Lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Count).Result,
                ChartTypeLookupId = F.Lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Bar).Result,
                BusinessModuleLookupId = F.Lookups.GetIdAsync(LookupDomains.BusinessModule, module).Result,
                VisibilityLookupId = F.Lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Tenant).Result,
            };
            F.Db.ChartDefinitions.Add(c);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return c.ChartDefinitionId;
        }

        public async Task<PulseDto> PulseAsync()
        {
            F.Db.ChangeTracker.Clear();
            return await Svc.GetPulseAsync(default);
        }

        public async Task<PulseDto> SaveAsync(string scope, IList<PulseLayoutItem>? items = null, IList<PulseLayoutPanel>? panels = null)
        {
            F.Db.ChangeTracker.Clear();
            try { return await Svc.SaveLayoutAsync(scope, new PulseLayoutRequest(items, panels), default); }
            finally { F.Db.ChangeTracker.Clear(); }
        }

        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static readonly string[] AllPulse =
    {
        PermissionCatalog.PulseIndicators, PermissionCatalog.PulseCharts, PermissionCatalog.PulseWarehouse, PermissionCatalog.PulseActivity,
        PermissionCatalog.InventoryView, PermissionCatalog.AnalyticsView, PermissionCatalog.OrdersView,
    };

    // ================================================================ (1)

    [Fact]
    public async Task Case1_user_with_pulse_warehouse_but_without_inventory_view_does_not_get_the_warehouse_panel()
    {
        await using var x = await Fx.CreateAsync();

        x.As(Me, PermissionCatalog.PulseWarehouse);
        Assert.DoesNotContain((await x.PulseAsync()).Panels, p => p.Key == PulsePanels.Warehouse);

        x.As(Me, PermissionCatalog.PulseWarehouse, PermissionCatalog.InventoryView);
        var panel = Assert.Single((await x.PulseAsync()).Panels);
        Assert.Equal(new PulsePanelDto(PulsePanels.Warehouse, true, 40, PulseSources.Default), panel);

        // Con WMS_LOTSERIAL apagado tampoco; sin ANALYTICS queda solo WAREHOUSE aunque tenga todo lo demás.
        x.F.SetModules(ModuleKeys.Analytics, ModuleKeys.LtlGround);
        Assert.Empty((await x.PulseAsync()).Panels);
        x.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.LtlGround);
        x.As(Me, AllPulse);
        Assert.Equal(new[] { PulsePanels.Warehouse }, (await x.PulseAsync()).Panels.Select(p => p.Key));

        // Registro del plan §2.3.
        Assert.Equal(new[] { ("INDICATORS", "pulse.indicators", 20), ("CHARTS", "pulse.charts", 30), ("WAREHOUSE", "pulse.warehouse", 40), ("ACTIVITY", "pulse.activity", 50) },
            PulsePanels.All.Select(p => (p.Key, p.Permission, p.DefaultSortOrder)));
        Assert.Equal(new[] { PermissionCatalog.InventoryView }, PulsePanels.Find("warehouse")!.DataPermissions);
        Assert.Equal(new[] { PermissionCatalog.AnalyticsView }, PulsePanels.Find(PulsePanels.Activity)!.DataPermissions);
        Assert.Equal(ModuleKeys.WmsLotSerial, PulsePanels.Find(PulsePanels.Warehouse)!.Module);
    }

    // ================================================================ (2)

    [Fact]
    public async Task Case2_indicator_over_transport_orders_is_not_shown_without_orders_view_even_in_the_company_pulse()
    {
        await using var x = await Fx.CreateAsync();
        var orders = await x.IndicatorAsync("Órdenes en curso", EntityTypes.TransportOrder, BusinessModules.Operations, showInPulse: true, sort: 10);
        var stock = await x.IndicatorAsync("Inventario disponible", EntityTypes.StockBalance, BusinessModules.Warehouse, showInPulse: true, sort: 20);
        var ordersChart = await x.ChartAsync("Órdenes por estatus", EntityTypes.TransportOrder, BusinessModules.Operations);

        x.As(Me, PermissionCatalog.PulseIndicators, PermissionCatalog.PulseCharts, PermissionCatalog.InventoryView);
        var pulse = await x.PulseAsync();
        var only = Assert.Single(pulse.Indicators);
        Assert.Equal((stock, 3m, BusinessModules.Warehouse), (only.Id, only.Value!.Value, only.BusinessModule));
        Assert.Empty(pulse.Charts);
        // La misma regla en Indicadores/Gráficos (lista y lectura por id: 404, no se revela).
        Assert.Equal(new[] { stock }, (await x.Svc.GetIndicatorsAsync(default)).Select(i => i.Id));
        Assert.Empty(await x.Svc.GetChartsAsync(default));
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.EvaluateIndicatorAsync(orders, default));
        Assert.Equal(404, nf.StatusCode);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.EvaluateChartAsync(ordersChart, default));

        // Con orders.view sí; y un módulo de negocio apagado lo saca aunque la fuente sea legible (WAREHOUSE → WMS_LOTSERIAL).
        x.As(Me, PermissionCatalog.PulseIndicators, PermissionCatalog.PulseCharts, PermissionCatalog.InventoryView, PermissionCatalog.OrdersView);
        pulse = await x.PulseAsync();
        Assert.Equal(new[] { orders, stock }, pulse.Indicators.Select(i => i.Id));
        Assert.Equal(new[] { ordersChart }, pulse.Charts.Select(c => c.Id));
        x.F.SetModules(ModuleKeys.Analytics, ModuleKeys.LtlGround);
        Assert.Equal(new[] { orders }, (await x.PulseAsync()).Indicators.Select(i => i.Id));

        // Diccionario EntityType → permiso de lectura (§2.2 regla 3).
        Assert.Equal(PermissionCatalog.InventoryView, PermissionCatalog.DataSourceReadPermission(EntityTypes.StockBalance));
        Assert.Equal(PermissionCatalog.InventoryView, PermissionCatalog.DataSourceReadPermission(EntityTypes.InventoryTransaction));
        Assert.Equal(PermissionCatalog.InventoryView, PermissionCatalog.DataSourceReadPermission(EntityTypes.Receipt));
        Assert.Equal(PermissionCatalog.OrdersView, PermissionCatalog.DataSourceReadPermission(EntityTypes.TransportOrder));
        Assert.Equal(PermissionCatalog.TripsView, PermissionCatalog.DataSourceReadPermission(EntityTypes.Trip));
        Assert.Equal(PermissionCatalog.PurchasingView, PermissionCatalog.DataSourceReadPermission(EntityTypes.PurchaseOrder));
        Assert.Equal(PermissionCatalog.AdminAudit, PermissionCatalog.DataSourceReadPermission(EntityTypes.AuditLog));
        Assert.Null(PermissionCatalog.DataSourceReadPermission(null));
        Assert.True(PulsePanels.IsBusinessModuleEnabled(BusinessModules.Accounting, k => k == ModuleKeys.Cod));
        Assert.False(PulsePanels.IsBusinessModuleEnabled(BusinessModules.Operations, k => k == ModuleKeys.Cod));
    }

    // ================================================================ (3)

    [Fact]
    public async Task Case3_resolution_is_user_then_company_then_default_with_its_source()
    {
        await using var x = await Fx.CreateAsync();
        var ind = await x.IndicatorAsync("Productos activos", EntityTypes.StockBalance, BusinessModules.Warehouse, showInPulse: true, sort: 50);
        x.As(Me, AllPulse.Append(PermissionCatalog.PulseOrganizeCompany).ToArray());

        var pulse = await x.PulseAsync();
        Assert.Equal(new[] { "INDICATORS:20:default", "CHARTS:30:default", "WAREHOUSE:40:default", "ACTIVITY:50:default" },
            pulse.Panels.Select(p => $"{p.Key}:{p.SortOrder}:{p.Source}"));
        Assert.Equal(("company", 50, true), (pulse.Indicators[0].Source, pulse.Indicators[0].SortOrder, pulse.Indicators[0].IsVisible));
        Assert.False(pulse.HasPersonalLayout);
        Assert.True(pulse.CanOrganizeCompany);

        // Compañía: Actividad arriba y el indicador oculto (se escribe la definición).
        pulse = await x.SaveAsync(AnalyticsService.ScopeCompany,
            new[] { new PulseLayoutItem("indicator", ind, 70, false) }, new[] { new PulseLayoutPanel("ACTIVITY", 5, true) });
        Assert.Equal("ACTIVITY:5:company", $"{pulse.Panels[0].Key}:{pulse.Panels[0].SortOrder}:{pulse.Panels[0].Source}");
        var item = Assert.Single(pulse.Indicators);
        Assert.Equal(("company", 70, false, (decimal?)null), (item.Source, item.SortOrder, item.IsVisible, item.Value));   // oculto: sin calcular
        Assert.False(pulse.HasPersonalLayout);

        // Otro usuario (sin fila propia) ve el de la compañía.
        x.As(Other, AllPulse);
        pulse = await x.PulseAsync();
        Assert.Equal(("ACTIVITY", "company"), (pulse.Panels[0].Key, pulse.Panels[0].Source));
        Assert.False(pulse.CanOrganizeCompany);

        // Usuario: su fila pisa la de la compañía (panel e indicador); los demás paneles siguen en default.
        pulse = await x.SaveAsync(AnalyticsService.ScopeMine,
            new[] { new PulseLayoutItem("INDICATOR", ind, 1, true) }, new[] { new PulseLayoutPanel("activity", 90, false) });
        Assert.Equal(new[] { "INDICATORS:20:default", "CHARTS:30:default", "WAREHOUSE:40:default", "ACTIVITY:90:user" },
            pulse.Panels.Select(p => $"{p.Key}:{p.SortOrder}:{p.Source}"));
        Assert.False(pulse.Panels[3].IsVisible);
        item = Assert.Single(pulse.Indicators);
        Assert.Equal(("user", 1, true, (decimal?)3m), (item.Source, item.SortOrder, item.IsVisible, item.Value));
        Assert.True(pulse.HasPersonalLayout);

        // El primero sigue viendo el de la compañía; la fila de compañía no cambió.
        x.As(Me, AllPulse);
        pulse = await x.PulseAsync();
        Assert.Equal("ACTIVITY:5:company", $"{pulse.Panels[0].Key}:{pulse.Panels[0].SortOrder}:{pulse.Panels[0].Source}");
        Assert.Equal(("company", false), (pulse.Indicators[0].Source, pulse.Indicators[0].IsVisible));

        // Resolución pura.
        var def = PulsePanels.Find(PulsePanels.Charts)!;
        var company = new PulsePanelSetting { PanelKey = "CHARTS", SortOrder = 7, IsVisible = false };
        var mine = new PulsePanelSetting { PanelKey = "CHARTS", SortOrder = 3, IsVisible = true };
        Assert.Equal((true, 3, "user"), PulsePanels.ResolvePanel(def, mine, company));
        Assert.Equal((false, 7, "company"), PulsePanels.ResolvePanel(def, null, company));
        Assert.Equal((true, 30, "default"), PulsePanels.ResolvePanel(def, null, null));
    }

    // ================================================================ (4)

    [Fact]
    public async Task Case4_partial_order_one_item_with_pulse_sort_order_and_the_rest_without()
    {
        await using var x = await Fx.CreateAsync();
        var a = await x.IndicatorAsync("A", EntityTypes.StockBalance, BusinessModules.Warehouse, sort: 10);
        var b = await x.IndicatorAsync("B", EntityTypes.StockBalance, BusinessModules.Warehouse, sort: 20);
        var c = await x.IndicatorAsync("C", EntityTypes.StockBalance, BusinessModules.Warehouse, sort: 30);
        var d = await x.IndicatorAsync("D (empate)", EntityTypes.StockBalance, BusinessModules.Warehouse, sort: 20);
        x.As(Me, AllPulse);

        var pulse = await x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("indicator", c, 15, true) });
        Assert.Equal(new[] { a, c, b, d }, pulse.Indicators.Select(i => i.Id));   // 10, 15 (propio), 20 "B", 20 "D"
        Assert.Equal(new[] { "company", "user", "company", "company" }, pulse.Indicators.Select(i => i.Source));
        Assert.Equal(new[] { 10, 15, 20, 20 }, pulse.Indicators.Select(i => i.SortOrder));

        // Solo lo que viene se toca: mover B no cambia C.
        pulse = await x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("indicator", b, 5, true) });
        Assert.Equal(new[] { b, a, c, d }, pulse.Indicators.Select(i => i.Id));
        // Idempotente.
        pulse = await x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("indicator", b, 5, true) });
        Assert.Equal(new[] { b, a, c, d }, pulse.Indicators.Select(i => i.Id));
        Assert.Equal(2, await x.F.Db.UserAnalyticsPreferences.CountAsync(p => p.UserId == Me));

        Assert.Equal((false, 15, "user"), PulsePanels.ResolveItem(15, false, 30, true));
        Assert.Equal((true, 30, "user"), PulsePanels.ResolveItem(null, true, 30, false));
        Assert.Equal((true, 30, "company"), PulsePanels.ResolveItem(null, null, 30, true));
    }

    // ================================================================ (5)

    [Fact]
    public async Task Case5_company_scope_without_pulse_organize_company_is_403()
    {
        await using var x = await Fx.CreateAsync();
        x.As(Me, AllPulse);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => x.SaveAsync(AnalyticsService.ScopeCompany, panels: new[] { new PulseLayoutPanel("CHARTS", 1, true) }));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(0, await x.F.Db.Set<PulsePanelSetting>().CountAsync());
        Assert.False((await x.PulseAsync()).CanOrganizeCompany);
        // mine no exige permiso.
        await x.SaveAsync(AnalyticsService.ScopeMine, panels: new[] { new PulseLayoutPanel("CHARTS", 1, true) });
        Assert.Equal(1, await x.F.Db.Set<PulsePanelSetting>().CountAsync(s => s.UserId == Me));

        // El controlador no lleva permiso ni módulo propio (el inicio siempre existe): lo decide el servicio por ?scope.
        var t = typeof(PulseController);
        Assert.Equal("api/v1/analytics/pulse", t.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Empty(t.GetCustomAttributes<RequirePermissionAttribute>(inherit: true));
        Assert.Empty(t.GetCustomAttributes<RequireModuleAttribute>(inherit: true));
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.Empty(m.GetCustomAttributes<RequirePermissionAttribute>());
            Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.DoesNotContain(m.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
        Assert.DoesNotContain(typeof(AnalyticsController).GetMethods(), m => m.GetCustomAttribute<HttpGetAttribute>()?.Template == "pulse");
    }

    // ================================================================ (6)

    [Fact]
    public async Task Case6_unknown_key_is_400_with_the_exact_message()
    {
        await using var x = await Fx.CreateAsync();
        var ind = await x.IndicatorAsync("Órdenes en curso", EntityTypes.TransportOrder, BusinessModules.Operations);
        x.As(Me, PermissionCatalog.PulseIndicators, PermissionCatalog.InventoryView);

        var v = await Assert.ThrowsAsync<ValidationException>(() => x.SaveAsync(AnalyticsService.ScopeMine,
            panels: new[] { new PulseLayoutPanel("INDICATORS", 1, true), new PulseLayoutPanel("RADAR", 2, true) }));
        Assert.Equal(400, v.StatusCode);
        Assert.Equal("Panel de Pulso desconocido: RADAR.", v.Message);
        Assert.Equal(new[] { "Panel de Pulso desconocido: RADAR." }, v.Errors!["panels[1].key"]);

        v = await Assert.ThrowsAsync<ValidationException>(() => x.SaveAsync("todos"));
        Assert.Equal(new[] { "Alcance inválido: use mine o company." }, v.Errors!["scope"]);
        v = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SaveLayoutAsync(null, null, default));
        Assert.Equal("Alcance inválido: use mine o company.", v.Message);
        v = await Assert.ThrowsAsync<ValidationException>(() => x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("report", 1, 1, true) }));
        Assert.Equal(new[] { "Tipo inválido: use indicator o chart." }, v.Errors!["items[0].kind"]);

        // Panel que existe pero cuyo permiso no tiene → 404 (no se revela); elemento que no puede leer → 404 'Indicador'.
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => x.SaveAsync(AnalyticsService.ScopeMine, panels: new[] { new PulseLayoutPanel("WAREHOUSE", 1, true) }));
        Assert.Equal(404, nf.StatusCode);
        nf = await Assert.ThrowsAsync<NotFoundException>(() => x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("indicator", ind, 1, true) }));
        Assert.StartsWith("Indicador", nf.Message);
        nf = await Assert.ThrowsAsync<NotFoundException>(() => x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("chart", 999, 1, true) }));
        Assert.StartsWith("Gráfico", nf.Message);
        // Nada se escribió.
        Assert.Equal(0, await x.F.Db.Set<PulsePanelSetting>().CountAsync());
        Assert.Equal(0, await x.F.Db.UserAnalyticsPreferences.CountAsync());
    }

    // ================================================================ (7)

    [Fact]
    public async Task Case7_delete_mine_keeps_the_own_date_range()
    {
        await using var x = await Fx.CreateAsync();
        var ind = await x.IndicatorAsync("Inventario disponible", EntityTypes.StockBalance, BusinessModules.Warehouse, sort: 10);
        x.As(Me, AllPulse);

        await x.Svc.SetIndicatorMyDateRangeAsync(ind, new DateRangeRequest(DateRangeModes.Last30, null, null), default);
        x.F.Db.ChangeTracker.Clear();
        var pulse = await x.SaveAsync(AnalyticsService.ScopeMine, new[] { new PulseLayoutItem("indicator", ind, 99, false) },
            new[] { new PulseLayoutPanel("CHARTS", 1, false) });
        Assert.True(pulse.HasPersonalLayout);

        await x.Svc.ResetMyLayoutAsync(default);
        x.F.Db.ChangeTracker.Clear();
        var pref = await x.F.Db.UserAnalyticsPreferences.AsNoTracking().SingleAsync(p => p.UserId == Me && p.IndicatorDefinitionId == ind);
        Assert.Equal(await x.F.Lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last30), pref.DateRangeModeLookupId);
        Assert.Null(pref.PulseSortOrder);
        Assert.Null(pref.ShowInPulse);
        Assert.Equal(0, await x.F.Db.Set<PulsePanelSetting>().CountAsync(s => s.UserId == Me));

        pulse = await x.PulseAsync();
        Assert.False(pulse.HasPersonalLayout);
        Assert.All(pulse.Panels, p => Assert.Equal(PulseSources.Default, p.Source));
        var item = Assert.Single(pulse.Indicators);
        Assert.Equal(("company", 10, true), (item.Source, item.SortOrder, item.IsVisible));
        // El rango propio sigue: la definición no tiene rango (fuente sin fecha), pero la preferencia se conserva en su fila.
        Assert.Equal(DateRangeModes.Last30, (await x.Svc.GetIndicatorAsync(ind, default)).EffectiveDateRangeMode);
    }

    // ================================================================ (8)

    [Fact]
    public async Task Case8_permission_seeder_adds_the_pulse_permissions_to_the_templates()
    {
        // Plantillas del plan §2.1.
        var t = PermissionCatalog.RoleTemplates;
        string[] Pulse(string role) => t[role].Where(c => c.StartsWith("pulse.", StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "pulse.activity", "pulse.charts", "pulse.indicators", "pulse.organize_company", "pulse.warehouse" }, Pulse("TenantAdmin"));
        Assert.Equal(new[] { "pulse.activity", "pulse.charts", "pulse.indicators", "pulse.warehouse" }, Pulse("WarehouseOperator"));
        foreach (var role in new[] { "Dispatcher", "Billing", "ReadOnly" })
            Assert.Equal(new[] { "pulse.activity", "pulse.charts", "pulse.indicators" }, Pulse(role));
        Assert.Empty(Pulse("Driver"));
        Assert.Equal(65, PermissionCatalog.All.Count);
        Assert.All(PermissionCatalog.All.Where(p => p.Code.StartsWith("pulse.", StringComparison.Ordinal)), p => Assert.Equal("PULSE", p.Category));

        // PermissionSeeder real sobre InMemory: plantillas y un rol clonado de un tenant sin los pulse.* (versión anterior).
        var tenant = new TenantContext { TenantId = null, UserId = null };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("pulse-seeder-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new TeikemDbContext(options, tenant);
        var lookups = new TripTestLookups();
        var categories = PermissionCatalog.All.Select(p => p.Category).Distinct().Select((c, n) => new LookupCode
        {
            LookupCodeId = 100 + n, Entity = LookupDomains.PermissionCategory, InternalCode = c, LabelJson = "{}", IsActive = true,
        }).ToList();
        db.LookupCodes.AddRange(categories);
        lookups.Load(categories);
        var old = PermissionCatalog.All.Where(p => !p.Code.StartsWith("pulse.", StringComparison.Ordinal))
            .Select(p => new Permission { Code = p.Code, CategoryLookupId = categories.First(c => c.InternalCode == p.Category).LookupCodeId, LabelJson = "{}" }).ToList();
        db.Permissions.AddRange(old);
        await db.SaveChangesAsync();
        foreach (var (name, templateCodes) in t)
            db.AppRoles.Add(MakeRole(null, name, templateCodes.Where(c => !c.StartsWith("pulse.", StringComparison.Ordinal)), old));
        db.AppRoles.Add(MakeRole(1, "WarehouseOperator", t["WarehouseOperator"].Where(c => !c.StartsWith("pulse.", StringComparison.Ordinal)), old));
        db.AppRoles.Add(MakeRole(1, "Almacén propio", new[] { PermissionCatalog.InventoryView }, old));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new PermissionSeeder(db, tenant, lookups, NullLogger<PermissionSeeder>.Instance).SeedAsync(default);
        db.ChangeTracker.Clear();

        var codes = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.PermissionId, p => p.Code);
        var roles = await db.AppRoles.IgnoreQueryFilters().AsNoTracking().Include(r => r.Permissions).ToListAsync();
        string[] PulseOf(int? tenantId, string name) => roles.Single(r => r.TenantId == tenantId && r.Name == name).Permissions
            .Select(rp => codes[rp.PermissionId]).Where(c => c.StartsWith("pulse.", StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        foreach (var name in t.Keys) Assert.Equal(Pulse(name), PulseOf(null, name));
        Assert.Equal(new[] { "pulse.activity", "pulse.charts", "pulse.indicators", "pulse.warehouse" }, PulseOf(1, "WarehouseOperator"));   // propagado
        Assert.Empty(PulseOf(1, "Almacén propio"));   // un rol propio no es plantilla: no se toca
        var pulsePerm = Assert.Single(await db.Permissions.AsNoTracking().Where(p => p.Code == PermissionCatalog.PulseOrganizeCompany).ToListAsync());
        Assert.Equal(categories.Single(c => c.InternalCode == "PULSE").LookupCodeId, pulsePerm.CategoryLookupId);
    }

    private static Role MakeRole(int? tenantId, string name, IEnumerable<string> codes, IReadOnlyList<Permission> perms)
    {
        var r = new Role { TenantId = tenantId, Name = name, IsSystem = tenantId is null, IsActive = true };
        foreach (var c in codes.Distinct()) r.Permissions.Add(new RolePermission { PermissionId = perms.Single(p => p.Code == c).PermissionId });
        return r;
    }
}
