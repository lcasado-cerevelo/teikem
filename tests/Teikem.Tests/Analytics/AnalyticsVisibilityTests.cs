using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Analytics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests.Analytics;

/// <summary>
/// Lote 169 (F, 2026-10-21) — quién ve cada indicador, gráfico y vista, también los «Por defecto» (IsSystem): nacen para toda la compañía pero respetan lo que se les ponga
/// (privado / compartido con personas y roles). Quien tiene analytics.manage siempre los ve para poder devolverlos; el administrador de plataforma, también. Un «Por defecto» no se
/// edita ni se elimina: solo se le cambia quién lo ve (<c>PUT …/visibility</c>). AnalyticsService real sobre InMemory (WmsFixture).
/// </summary>
public class AnalyticsVisibilityTests
{
    private const int Admin = 1;
    private const int Viewer = 2;
    private const int Third = 3;
    private const int ViewerRole = 50;

    private sealed class OneSource : IDataSource
    {
        public string Key => EntityTypes.StockBalance;
        public string LabelEs => "Saldos";
        public string LabelEn => "Stock";
        public string? EntityTypeCode => null;
        public string? DateField => null;
        public string IdField => "Id";
        public string DefaultBusinessModule => BusinessModules.Warehouse;
        public IReadOnlyList<DataField> Fields { get; } = new[] { new DataField("Id", "Id", "Id", DataFieldType.Number), new DataField("Category", "Categoría", "Category", DataFieldType.Text) };
        public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();
        public Task<List<DataRow>> LoadAsync(DataQuery query, CancellationToken ct) => Task.FromResult(new List<DataRow> { new() { ["Id"] = 1, ["Category"] = "A" } });
    }

    private sealed class Fx : IAsyncDisposable
    {
        public required WmsFixture F { get; init; }
        public AnalyticsService Svc => F.Get<AnalyticsService>();

        public static async Task<Fx> CreateAsync()
        {
            var f = await WmsFixture.CreateAsync(s =>
            {
                s.AddSingleton<IDataSource>(new OneSource());
                s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
                s.AddSingleton<AnalyticsEngine>();
                s.AddTransient<AnalyticsService>();
            });
            var id = 7000;
            LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            f.Db.LookupCodes.AddRange(
                L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.ReportVisibility, ReportVisibilities.Private), L(LookupDomains.ReportVisibility, ReportVisibilities.Shared),
                L(LookupDomains.AggregateFn, AggregateFns.Count), L(LookupDomains.ReportChartType, ChartTypes.Bar),
                L(LookupDomains.BusinessModule, BusinessModules.Warehouse), L(LookupDomains.DateRangeMode, DateRangeModes.Last7));
            f.Db.UserTenants.AddRange(new UserTenant { UserId = Viewer, TenantId = WmsFixture.TenantId }, new UserTenant { UserId = Third, TenantId = WmsFixture.TenantId });
            f.Db.AppRoles.Add(new Role { RoleId = ViewerRole, TenantId = WmsFixture.TenantId, Name = "Lectores" });
            f.Db.AppUserRoles.Add(new UserRole { UserId = Third, RoleId = ViewerRole, TenantId = WmsFixture.TenantId });
            await f.Db.SaveChangesAsync();
            f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
            f.Db.ChangeTracker.Clear();
            f.SetModules(ModuleKeys.Analytics, ModuleKeys.WmsLotSerial);
            return new Fx { F = f };
        }

        public void As(int userId, params string[] perms)
        {
            F.Tenant.UserId = userId;
            F.SetPermissions(perms);
            F.Db.ChangeTracker.Clear();
        }

        private int Id(string domain, string code) => F.Lookups.GetIdAsync(domain, code).Result;

        public async Task<int> IndicatorAsync(string name, bool isSystem, int? owner = null)
        {
            var i = new IndicatorDefinition
            {
                TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = EntityTypes.StockBalance, IsSystem = isSystem, OwnerUserId = owner, ShowInPulse = true, SortOrder = 1,
                AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Count), BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
                VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant),
            };
            F.Db.IndicatorDefinitions.Add(i);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return i.IndicatorDefinitionId;
        }

        public async Task<int> ChartAsync(string name, bool isSystem)
        {
            var c = new ChartDefinition
            {
                TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = EntityTypes.StockBalance, GroupByField = "Category", IsSystem = isSystem, ShowInPulse = true, SortOrder = 1,
                AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Count), ChartTypeLookupId = Id(LookupDomains.ReportChartType, ChartTypes.Bar),
                BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse), VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant),
            };
            F.Db.ChartDefinitions.Add(c);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return c.ChartDefinitionId;
        }

        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static readonly string[] Manage = { PermissionCatalog.AnalyticsView, PermissionCatalog.AnalyticsManage, PermissionCatalog.InventoryView };
    private static readonly string[] ViewOnly = { PermissionCatalog.AnalyticsView, PermissionCatalog.InventoryView };

    private static async Task<bool> SeesIndicatorAsync(Fx x, int id)
    {
        x.F.Db.ChangeTracker.Clear();
        return (await x.Svc.GetIndicatorsAsync(default)).Any(i => i.Id == id);
    }

    [Fact]
    public async Task A_default_indicator_is_born_for_the_whole_company()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Viewer, ViewOnly);
        Assert.True(await SeesIndicatorAsync(x, id));
    }

    [Fact]
    public async Task A_private_default_indicator_is_hidden_from_a_viewer_but_the_manager_still_sees_it()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);

        x.As(Admin, Manage);
        var dto = await x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Private, null), default);
        Assert.Equal((true, ReportVisibilities.Private), (dto.IsSystem, dto.Visibility));
        Assert.True(await SeesIndicatorAsync(x, id));   // quien administra siempre lo ve, para poder devolverlo

        x.As(Viewer, ViewOnly);
        Assert.False(await SeesIndicatorAsync(x, id));
        x.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetIndicatorAsync(id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.EvaluateIndicatorAsync(id, default));
    }

    [Fact]
    public async Task A_shared_default_indicator_reaches_only_the_people_and_roles_it_was_shared_with()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Admin, Manage);
        await x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Shared, new List<ShareDto> { new(Viewer, null, false), new(null, ViewerRole, false) }), default);

        x.As(Viewer, ViewOnly);
        Assert.True(await SeesIndicatorAsync(x, id));      // por persona
        x.As(Third, ViewOnly);
        Assert.True(await SeesIndicatorAsync(x, id));      // por rol
        x.As(99, ViewOnly);
        Assert.False(await SeesIndicatorAsync(x, id));     // ni persona ni rol
    }

    [Fact]
    public async Task Returning_a_default_to_the_whole_company_makes_it_visible_again_and_drops_the_shares()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Admin, Manage);
        await x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Shared, new List<ShareDto> { new(Viewer, null, false) }), default);
        x.F.Db.ChangeTracker.Clear();
        var dto = await x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Tenant, new List<ShareDto> { new(Viewer, null, false) }), default);
        Assert.Equal((ReportVisibilities.Tenant, 0), (dto.Visibility, dto.Shares.Count));
        x.As(99, ViewOnly);
        Assert.True(await SeesIndicatorAsync(x, id));
    }

    [Fact]
    public async Task A_default_is_never_edited_or_deleted_only_its_visibility_changes()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Admin, Manage);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.UpdateIndicatorAsync(id, new IndicatorUpsertRequest("Otro nombre", null, EntityTypes.StockBalance, null, AggregateFns.Count, null, null, null, null, null, null, null, null, null, null), default));
        x.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.DeleteIndicatorAsync(id, default));
    }

    [Fact]
    public async Task An_unknown_visibility_is_a_400_with_its_message()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Admin, Manage);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest("TODOS", null), default));
        Assert.Contains("La visibilidad debe ser PRIVATE, TENANT o SHARED.", ex.Errors.SelectMany(e => e.Value));
    }

    [Fact]
    public async Task A_private_default_chart_is_hidden_from_a_viewer_in_the_list_and_in_the_pulse()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.ChartAsync("Movimientos por tipo", isSystem: true);
        x.As(Admin, Manage);
        var dto = await x.Svc.SetChartVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Private, null), default);
        Assert.Equal(ReportVisibilities.Private, dto.Visibility);

        x.As(Viewer, ViewOnly);
        Assert.DoesNotContain(await x.Svc.GetChartsAsync(default), c => c.Id == id);
        x.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.GetChartAsync(id, default));
        x.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.EvaluateChartAsync(id, default));
    }

    [Fact]
    public async Task The_platform_administrator_still_sees_everything()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.IndicatorAsync("Descuadres pendientes", isSystem: true);
        x.As(Admin, Manage);
        await x.Svc.SetIndicatorVisibilityAsync(id, new VisibilityRequest(ReportVisibilities.Private, null), default);
        x.As(Viewer, ViewOnly);
        x.F.Tenant.IsPlatformAdmin = true;
        Assert.True(await SeesIndicatorAsync(x, id));
    }

    [Theory]
    [InlineData(nameof(AnalyticsController.ReportVisibility))]
    [InlineData(nameof(AnalyticsController.IndicatorVisibility))]
    [InlineData(nameof(AnalyticsController.ChartVisibility))]
    public void Changing_who_sees_an_element_needs_analytics_manage(string action)
    {
        var attr = typeof(AnalyticsController).GetMethod(action)!.GetCustomAttribute<RequirePermissionAttribute>()!;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AnalyticsManage, attr.Policy);
    }
}
