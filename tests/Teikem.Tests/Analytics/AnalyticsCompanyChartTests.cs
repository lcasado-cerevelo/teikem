using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Analytics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests.Analytics;

/// <summary>
/// Lote 15 (P2, D9–D11, D14) — gráfico DE LA COMPAÑÍA (no de sistema y sin dueño, como los 2 gráficos de almacén sembrados):
/// lo edita y elimina quien tenga analytics.manage (canEdit lo refleja; sin el permiso, false), uno de sistema sigue con su 403 y
/// uno con dueño sigue con "Solo el dueño…". "Otras" en el motor: con más de 8 grupos en dona o barras con SUM/COUNT, los 7
/// mayores y "Otras" con la suma exacta del resto, en el idioma del usuario; en líneas y con AVG no cambia nada.
/// AnalyticsService real sobre InMemory (WmsFixture) con una fuente falsa de 9 categorías.
/// </summary>
public class AnalyticsCompanyChartTests
{
    private const int Me = 1;
    private const int Other = 2;

    /// <summary>Fuente falsa STOCK_BALANCE (legible con inventory.view, módulo Almacén): 9 categorías A…I con montos 9…1.</summary>
    private sealed class NineCategories : IDataSource
    {
        public string Key => EntityTypes.StockBalance;
        public string LabelEs => "Saldos";
        public string LabelEn => "Stock";
        public string? EntityTypeCode => null;
        public string? DateField => null;
        public string IdField => "Id";
        public string DefaultBusinessModule => BusinessModules.Warehouse;
        public IReadOnlyList<DataField> Fields { get; } = new[]
        {
            new DataField("Id", "Id", "Id", DataFieldType.Number), new DataField("Category", "Categoría", "Category", DataFieldType.Text),
            new DataField("CostValue", "Valor", "Value", DataFieldType.Number, IsMoney: true),
        };
        public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

        public Task<List<DataRow>> LoadAsync(DataQuery query, CancellationToken ct)
            => Task.FromResult(Enumerable.Range(0, 9).Select(i => new DataRow { ["Id"] = i + 1, ["Category"] = ((char)('A' + i)).ToString(), ["CostValue"] = 9m - i }).ToList());
    }

    private sealed class Fx : IAsyncDisposable
    {
        public required WmsFixture F { get; init; }
        public AnalyticsService Svc => F.Get<AnalyticsService>();

        public static async Task<Fx> CreateAsync()
        {
            var f = await WmsFixture.CreateAsync(s =>
            {
                s.AddSingleton<IDataSource>(new NineCategories());
                s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
                s.AddSingleton<AnalyticsEngine>();
                s.AddTransient<AnalyticsService>();
            });
            var id = 7000;
            LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
            f.Db.LookupCodes.AddRange(
                L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.ReportVisibility, ReportVisibilities.Private),
                L(LookupDomains.AggregateFn, AggregateFns.Count), L(LookupDomains.AggregateFn, AggregateFns.Sum), L(LookupDomains.AggregateFn, AggregateFns.Avg),
                L(LookupDomains.ReportChartType, ChartTypes.Bar), L(LookupDomains.ReportChartType, ChartTypes.Donut), L(LookupDomains.ReportChartType, ChartTypes.Line),
                L(LookupDomains.BusinessModule, BusinessModules.Warehouse), L(LookupDomains.BusinessModule, BusinessModules.Operations),
                L(LookupDomains.DateRangeMode, DateRangeModes.Last7));
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

        public async Task<int> ChartAsync(string name, bool isSystem, int? owner, string type = ChartTypes.Donut, string fn = AggregateFns.Sum,
            string? seedKey = null)
        {
            var c = new ChartDefinition
            {
                TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = EntityTypes.StockBalance, GroupByField = "Category",
                FieldKey = fn == AggregateFns.Count ? null : "CostValue", IsMoney = true, IsSystem = isSystem, OwnerUserId = owner, SeedKey = seedKey,
                ShowInPulse = true, SortOrder = 1,
                AggregateFnLookupId = Id(LookupDomains.AggregateFn, fn), ChartTypeLookupId = Id(LookupDomains.ReportChartType, type),
                BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
                VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant),
            };
            F.Db.ChartDefinitions.Add(c);
            await F.Db.SaveChangesAsync();
            F.Db.ChangeTracker.Clear();
            return c.ChartDefinitionId;
        }

        public async Task<AnalyticsDefinitionDto> UpdateAsync(int id, string name, string type = ChartTypes.Donut)
        {
            F.Db.ChangeTracker.Clear();
            try
            {
                return await Svc.UpdateChartAsync(id, new ChartUpsertRequest(name, null, EntityTypes.StockBalance, "Category", "CostValue", AggregateFns.Sum, type,
                    null, BusinessModules.Warehouse, true, null, null, null, null, null, null, null), default);
            }
            finally { F.Db.ChangeTracker.Clear(); }
        }

        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static readonly string[] Manage = { PermissionCatalog.AnalyticsView, PermissionCatalog.AnalyticsManage, PermissionCatalog.InventoryView };
    private static readonly string[] ViewOnly = { PermissionCatalog.AnalyticsView, PermissionCatalog.InventoryView };

    [Fact]
    public async Task Company_chart_is_edited_and_deleted_by_whoever_has_analytics_manage()
    {
        await using var x = await Fx.CreateAsync();
        var id = await x.ChartAsync("Valor de inventario por categoría", isSystem: false, owner: null, seedKey: ChartSeedKeys.InventoryValue);

        x.As(Me, Manage);
        var dto = await x.Svc.GetChartAsync(id, default);
        Assert.Equal((false, (int?)null, true, true), (dto.IsSystem, dto.OwnerUserId, dto.CanEdit, dto.CanChangeDate));
        Assert.True(AnalyticsService.IsCompanyOwned(dto.IsSystem, dto.OwnerUserId));

        // Sin analytics.manage: se lee pero no se edita (canEdit false; el PUT/DELETE lo corta el [RequirePermission]).
        x.As(Other, ViewOnly);
        dto = await x.Svc.GetChartAsync(id, default);
        Assert.Equal((false, false), (dto.CanEdit, dto.CanChangeDate));
        var put = typeof(AnalyticsController).GetMethod(nameof(AnalyticsController.UpdateChart))!.GetCustomAttribute<RequirePermissionAttribute>()!;
        var del = typeof(AnalyticsController).GetMethod(nameof(AnalyticsController.DeleteChart))!.GetCustomAttribute<RequirePermissionAttribute>()!;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AnalyticsManage, put.Policy);
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AnalyticsManage, del.Policy);

        // Otro usuario con analytics.manage (no es dueño de nada) lo renombra, cambia el tipo y lo borra; sigue sin dueño.
        x.As(Other, Manage);
        var updated = await x.UpdateAsync(id, "Valor por categoría", ChartTypes.Bar);
        Assert.Equal(("Valor por categoría", ChartTypes.Bar, false, (int?)null, true), (updated.Name, updated.ChartType, updated.IsSystem, updated.OwnerUserId, updated.CanEdit));
        await x.Svc.DeleteChartAsync(id, default);
        x.F.Db.ChangeTracker.Clear();
        var row = await x.F.Db.ChartDefinitions.AsNoTracking().SingleAsync(c => c.ChartDefinitionId == id);
        Assert.False(row.IsActive);
        Assert.Equal(ChartSeedKeys.InventoryValue, row.SeedKey);   // la clave queda: la siembra no lo vuelve a crear
    }

    [Fact]
    public async Task System_chart_keeps_its_403_and_an_owned_chart_keeps_only_the_owner()
    {
        await using var x = await Fx.CreateAsync();
        var system = await x.ChartAsync("Cambios por acción", isSystem: true, owner: null);
        var mine = await x.ChartAsync("Mi gráfico", isSystem: false, owner: Me);

        x.As(Other, Manage);
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => x.UpdateAsync(system, "X"));
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Equal("Los elementos por default de la plataforma no se editan ni se eliminan.", forbidden.Message);
        Assert.False((await x.Svc.GetChartAsync(system, default)).CanEdit);

        forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => x.UpdateAsync(mine, "Y"));
        Assert.Equal("Solo el dueño puede editar o eliminar este elemento.", forbidden.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.DeleteChartAsync(mine, default));
        Assert.False((await x.Svc.GetChartAsync(mine, default)).CanEdit);
        Assert.False(AnalyticsService.IsCompanyOwned(true, null));
        Assert.False(AnalyticsService.IsCompanyOwned(false, Me));

        x.As(Me, Manage);
        Assert.True((await x.Svc.GetChartAsync(mine, default)).CanEdit);
        Assert.Equal("Z", (await x.UpdateAsync(mine, "Z")).Name);
    }

    [Fact]
    public async Task Donut_and_bar_with_more_than_eight_groups_show_the_seven_largest_and_others()
    {
        await using var x = await Fx.CreateAsync();
        var donut = await x.ChartAsync("Dona", false, null);
        var bar = await x.ChartAsync("Barras", false, null, ChartTypes.Bar);
        var line = await x.ChartAsync("Línea", false, null, ChartTypes.Line);
        var avg = await x.ChartAsync("Promedio", false, null, ChartTypes.Donut, AggregateFns.Avg);
        var count = await x.ChartAsync("Conteo", false, null, ChartTypes.Donut, AggregateFns.Count);
        x.As(Me, ViewOnly);

        var d = await x.Svc.EvaluateChartAsync(donut, default);
        Assert.Equal(new[] { "A", "B", "C", "D", "E", "F", "G", "Otras" }, d.Points.Select(p => p.Label));
        Assert.Equal(new[] { 9m, 8m, 7m, 6m, 5m, 4m, 3m, 3m }, d.Points.Select(p => p.Value));   // Otras = H (2) + I (1)
        Assert.Equal(45m, d.Points.Sum(p => p.Value));                                          // la suma de la dona es el total
        Assert.Equal(AnalyticsEngine.OthersKey, d.Points[^1].Key);
        Assert.Equal(d.Points.Select(p => (p.Label, p.Value)), (await x.Svc.EvaluateChartAsync(bar, default)).Points.Select(p => (p.Label, p.Value)));
        var c = await x.Svc.EvaluateChartAsync(count, default);
        Assert.Equal(("Otras", 2m), (c.Points[^1].Label, c.Points[^1].Value));

        // En inglés, "Others"; en líneas y con AVG no hay "Otras".
        x.F.Tenant.Lang = "en";
        Assert.Equal("Others", (await x.Svc.EvaluateChartAsync(donut, default)).Points[^1].Label);
        Assert.DoesNotContain((await x.Svc.EvaluateChartAsync(line, default)).Points, p => p.Key as string == AnalyticsEngine.OthersKey);
        var a = await x.Svc.EvaluateChartAsync(avg, default);
        Assert.Equal(8, a.Points.Count);
        Assert.DoesNotContain(a.Points, p => p.Label == "Others");

        // Regla pura: con 8 o menos no cambia nada; con 9, 7 + "Otras".
        var eight = Enumerable.Range(1, 8).Select(i => new ChartPoint("G" + i, i, i)).ToList();
        Assert.Equal(eight.OrderByDescending(p => p.Value), AnalyticsEngine.FoldOthers(eight, 8, "Otras"));
        var nine = eight.Append(new ChartPoint("G9", 9, 0.5m)).ToList();
        var folded = AnalyticsEngine.FoldOthers(nine, 8, "Otras");
        Assert.Equal(8, folded.Count);
        Assert.Equal(("Otras", 1.5m), (folded[^1].Label, folded[^1].Value));   // G1 (1) + G9 (0.5)
    }
}
