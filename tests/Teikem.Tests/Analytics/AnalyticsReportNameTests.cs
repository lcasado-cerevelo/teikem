using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// Lote F-F2 (Vistas e informes) — defectos del servidor hallados al construir la pantalla: (1) crear o renombrar una vista con el nombre
/// de una ELIMINADA daba 500 (UQ_ReportDefinition cuenta también las dadas de baja); (2) renombrar a un nombre ya usado por otra vista
/// activa daba 500 en vez del 409 de siempre; (3) el tipo de gráfico de la vista no se guardaba (siempre null).
/// AnalyticsService real sobre InMemory (WmsFixture), con una fuente falsa de STOCK_BALANCE.
/// </summary>
public class AnalyticsReportNameTests
{
    private sealed class Cats : IDataSource
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
        public Task<List<DataRow>> LoadAsync(DataQuery query, CancellationToken ct) => Task.FromResult(new List<DataRow>());
    }

    private static readonly string[] Perms = { PermissionCatalog.AnalyticsView, PermissionCatalog.AnalyticsManage, PermissionCatalog.InventoryView };

    private static async Task<WmsFixture> CreateAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<IDataSource>(new Cats());
            s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
            s.AddSingleton<AnalyticsEngine>();
            s.AddTransient<AnalyticsService>();
        });
        var id = 7100;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        f.Db.LookupCodes.AddRange(
            L(LookupDomains.EntityType, EntityTypes.StockBalance),
            L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.ReportVisibility, ReportVisibilities.Private),
            L(LookupDomains.ReportChartType, ChartTypes.Bar), L(LookupDomains.ReportChartType, ChartTypes.Donut), L(LookupDomains.ReportChartType, ChartTypes.Line));
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        f.SetModules(ModuleKeys.Analytics, ModuleKeys.WmsLotSerial);
        f.Tenant.UserId = 1;
        f.SetPermissions(Perms);
        return f;
    }

    private static ReportUpsertRequest Req(string name, string? chartType = null)
        => new(name, null, ReportVisibilities.Private, null, new List<string> { "Category" }, null, null, null, chartType, null, null, null);

    [Fact]
    public async Task A_deleted_view_name_can_be_used_again_to_create_and_to_rename()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<AnalyticsService>();
        var first = await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Ventas"), default);
        await svc.DeleteReportAsync(first.Id, default);
        f.Db.ChangeTracker.Clear();

        var again = await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Ventas"), default);
        Assert.Equal("Ventas", again.Name);
        Assert.NotEqual(first.Id, again.Id);
        // la eliminada conserva su rastro con un nombre que ya no estorba
        var dead = await f.Db.ReportDefinitions.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.ReportDefinitionId == first.Id);
        Assert.Equal($"Ventas (eliminada #{first.Id})", dead.Name);

        // renombrar otra vista al nombre de una eliminada tampoco falla
        var other = await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Otra"), default);
        await svc.DeleteReportAsync(again.Id, default);
        f.Db.ChangeTracker.Clear();
        var renamed = await svc.UpdateReportAsync(other.Id, Req("Ventas"), default);
        Assert.Equal("Ventas", renamed.Name);
    }

    [Fact]
    public async Task Renaming_to_a_name_used_by_another_active_view_is_a_409_with_the_same_message_as_creating()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<AnalyticsService>();
        await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Ventas"), default);
        var other = await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Compras"), default);
        f.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateReportAsync(other.Id, Req("Ventas"), default));
        Assert.Equal($"Ya existe una vista 'Ventas' para {EntityTypes.StockBalance}.", ex.Message);
        f.Db.ChangeTracker.Clear();
        // guardar con su propio nombre sigue permitido
        Assert.Equal("Compras", (await svc.UpdateReportAsync(other.Id, Req("Compras"), default)).Name);
    }

    [Fact]
    public async Task The_chart_type_of_a_view_is_saved_and_an_unknown_one_is_rejected()
    {
        await using var f = await CreateAsync();
        var svc = f.Get<AnalyticsService>();
        var created = await svc.CreateReportAsync(EntityTypes.StockBalance, Req("Con gráfico", "donut"), default);
        Assert.Equal(ChartTypes.Donut, created.ChartType);
        f.Db.ChangeTracker.Clear();
        var updated = await svc.UpdateReportAsync(created.Id, Req("Con gráfico", ChartTypes.Line), default);
        Assert.Equal(ChartTypes.Line, updated.ChartType);
        f.Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateReportAsync(created.Id, Req("Con gráfico", "PIE3D"), default));
        Assert.Equal("Tipo de gráfico: BAR, DONUT o LINE.", ex.Message);
    }
}
