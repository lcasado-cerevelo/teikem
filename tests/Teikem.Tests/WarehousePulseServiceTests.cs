using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 15 (P3) — franja "Almacén hoy" (WarehousePulseService) sobre InMemory (WmsFixture) con un reloj fijo en hora de Puerto
/// Rico: días locales (un recibo a las 23:59 locales cae en su día, no en el día UTC), neto de recepción con la diferencia de
/// recibo, salida = recolección + cruce de muelle − reversa (transferencias y daños no cuentan), filtro por almacén (lado único
/// del movimiento), almacén desconocido en cero, aislamiento entre compañías, solo conteos activos en Diferencia por su día local,
/// días vacíos en 0, bajo mínimo con la consulta de Productos, el 400 y la consistencia con los indicadores con el mismo rango.
/// La traducción a SQL Server (CASE del día local agrupado en SQL) se prueba sin BD (ToQueryString).
/// </summary>
public class WarehousePulseServiceTests
{
    /// <summary>23:00 del 30/09/2026 en Puerto Rico (03:00Z del 01/10): hoy es el 30 y la franja va del 24 al 30.</summary>
    private static readonly DateTime Now = Utc(2026, 10, 1, 3);

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    private sealed record World(WmsFixture F, Warehouse W1, Warehouse W2, Product P)
    {
        public WarehousePulseService Svc => F.Get<WarehousePulseService>();
        public Task<WarehousePulseDaysDto> DaysAsync(params Guid[] warehouses)
            => Svc.DaysAsync(new WarehousePulseQuery(warehouses.Length == 0 ? null : warehouses), default);
    }

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddSingleton<ProductService>();
            s.AddSingleton<WarehousePulseService>();
            s.AddSingleton<InventoryReadService>();
            s.AddSingleton<IDataSource, CycleCountDataSource>();
            s.AddSingleton<IDataSource, InventoryTransactionDataSource>();
            s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
            s.AddSingleton<AnalyticsEngine>();
            s.AddTransient<AnalyticsService>();
        });
        var w1 = await f.AddWarehouseAsync("W1");
        var w2 = await f.AddWarehouseAsync("W2");
        var w3 = await f.AddWarehouseAsync("W3", tenantId: WmsFixture.OtherTenantId);
        var p = await f.AddProductAsync("P1");
        var q = await f.AddProductAsync("Q1", tenantId: WmsFixture.OtherTenantId);
        var tracked = await f.Db.Products.SingleAsync(x => x.ProductId == p.ProductId);
        tracked.MinQty = 5;   // sin existencia: bajo mínimo
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        long id = 1;
        InventoryTransaction T(string type, string? reason, decimal qty, DateTime at, int? from = null, int? to = null, int? tenant = null, int? product = null) => new()
        {
            InventoryTransactionId = id++, TenantId = tenant ?? WmsFixture.TenantId, TxnTypeLookupId = f.LookupId(LookupDomains.InventoryTxnType, type),
            ReasonLookupId = reason is null ? null : f.LookupId(LookupDomains.AdjustmentReason, reason), ProductId = product ?? p.ProductId,
            FromWarehouseId = from, FromBinId = from is null ? null : 1, ToWarehouseId = to, ToBinId = to is null ? null : 1, Quantity = qty, CreatedAtUtc = at,
        };
        f.Db.InventoryTransactions.AddRange(
            T(InventoryTxnTypes.Receipt, null, 10, Utc(2026, 9, 29, 3, 59), to: w1.WarehouseId),                              // 23:59 del 28 local (UTC ya es el 29)
            T(InventoryTxnTypes.Receipt, null, 5, Utc(2026, 9, 30, 12), to: w1.WarehouseId),                                  // hoy
            T(InventoryTxnTypes.Adjustment, AdjustmentReasons.ReceiptVariance, -2, Utc(2026, 9, 30, 12), from: w1.WarehouseId), // llegaron 3
            T(InventoryTxnTypes.Receipt, null, 7, Utc(2026, 9, 30, 13), to: w2.WarehouseId),                                  // hoy, W2
            T(InventoryTxnTypes.Issue, null, -4, Utc(2026, 9, 30, 14), from: w1.WarehouseId),                                 // recolección
            T(InventoryTxnTypes.CrossDock, null, -1, Utc(2026, 9, 27, 10), from: w1.WarehouseId),                             // cruce, el 27
            T(InventoryTxnTypes.Adjustment, AdjustmentReasons.PickBatchReversal, 1, Utc(2026, 9, 30, 14, 30), to: w1.WarehouseId), // reversa: resta
            T(InventoryTxnTypes.Transfer, null, 6, Utc(2026, 9, 30, 15), from: w1.WarehouseId, to: w2.WarehouseId),          // no cuenta
            T(InventoryTxnTypes.Adjustment, AdjustmentReasons.Damage, -1, Utc(2026, 9, 30, 15), from: w1.WarehouseId),        // no cuenta
            T(InventoryTxnTypes.Receipt, null, 100, Utc(2026, 9, 24, 3, 59), to: w1.WarehouseId),                             // 23:59 del 23 local: fuera
            T(InventoryTxnTypes.Receipt, null, 50, Utc(2026, 9, 30, 12), to: w3.WarehouseId, tenant: WmsFixture.OtherTenantId, product: q.ProductId)); // otra compañía

        var variance = f.StatusId(StatusDomains.CycleCountStatus, CycleCountStatuses.ReconciledVariance);
        var matched = f.StatusId(StatusDomains.CycleCountStatus, CycleCountStatuses.Reconciled);
        var n = 1;
        CycleCount Cc(Warehouse w, int status, DateTime? at, bool active = true, int? tenant = null) => new()
        {
            CycleCountId = n, TenantId = tenant ?? WmsFixture.TenantId, WarehouseId = w.WarehouseId, Number = $"CC-{n++:00000}", StatusCodeId = status,
            CreatedAtUtc = Utc(2026, 9, 20), ReconciledAtUtc = at, IsActive = active,
        };
        f.Db.CycleCounts.AddRange(
            Cc(w1, variance, Utc(2026, 9, 30, 2)),                       // 22:00 del 29 local (UTC ya es el 30): ayer
            Cc(w2, variance, Utc(2026, 9, 30, 16)),                      // hoy, W2
            Cc(w1, matched, Utc(2026, 9, 30, 16)),                       // Concordancia: no cuenta
            Cc(w1, variance, Utc(2026, 9, 30, 16), active: false),       // eliminado: no cuenta
            Cc(w3, variance, Utc(2026, 9, 30, 16), tenant: WmsFixture.OtherTenantId),   // otra compañía
            Cc(w1, variance, Utc(2026, 9, 24, 3)));                      // 23:00 del 23 local: fuera
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w1, w2, p);
    }

    [Fact]
    public async Task All_warehouses_seven_local_days_with_today_and_totals()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var r = await w.DaysAsync();

        Assert.Equal("America/Puerto_Rico", r.TimeZone);
        Assert.Equal(new DateOnly(2026, 9, 30), r.Today);
        Assert.Equal(Utc(2026, 9, 24, 4), r.FromUtc);
        Assert.Equal(Utc(2026, 10, 1, 4), r.ToUtc);
        Assert.Equal(Enumerable.Range(24, 7).Select(d => new DateOnly(2026, 9, d)), r.Days.Select(d => d.Date));

        // Recibidas: 10 el 28 (23:59 local) y hoy 5 − 2 (diferencia de recibo) + 7 (W2) = 10; el recibo del 23 y el de otra compañía no.
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 10m, 0m, 10m }, r.Days.Select(d => d.ReceivedUnits));
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 0, 3 }, r.Days.Select(d => d.ReceivedMovements));
        // Salida: cruce 1 el 27; hoy recolección 4 − reversa 1 = 3 (transferencia y daño no cuentan).
        Assert.Equal(new[] { 0m, 0m, 0m, 1m, 0m, 0m, 3m }, r.Days.Select(d => d.OutboundUnits));
        Assert.Equal(new[] { 0, 0, 0, 1, 0, 0, 2 }, r.Days.Select(d => d.OutboundMovements));
        // Conteos en Diferencia: uno ayer (22:00 local) y uno hoy (W2); Concordancia, eliminado, otra compañía y el del 23 no.
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 1 }, r.Days.Select(d => d.CountsWithVariance));

        Assert.Equal((10m, 20m, 3m, 4m, 1, 2), (r.ReceivedToday, r.ReceivedTotal, r.OutboundToday, r.OutboundTotal, r.CountsWithVarianceToday, r.CountsWithVarianceTotal));
        Assert.Equal(1, r.BelowMinProducts);
        Assert.True(r.CountsAlert);
        Assert.True(r.BelowMinAlert);
    }

    [Fact]
    public async Task Warehouse_filter_uses_the_single_side_of_each_movement()
    {
        var w = await SeedAsync();
        await using var _ = w.F;

        // W1: recibido en W1 (destino) y la diferencia de recibo (origen W1); salida desde W1; la reversa entra a W1 y resta.
        var r1 = await w.DaysAsync(w.W1.PublicId);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 10m, 0m, 3m }, r1.Days.Select(d => d.ReceivedUnits));
        Assert.Equal(new[] { 0m, 0m, 0m, 1m, 0m, 0m, 3m }, r1.Days.Select(d => d.OutboundUnits));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 0 }, r1.Days.Select(d => d.CountsWithVariance));
        Assert.False(r1.CountsAlert);   // hoy no hubo en W1
        Assert.Equal(1, r1.BelowMinProducts);

        // W2: solo su recibo y su conteo de hoy (la transferencia W1 → W2 no es entrada).
        var r2 = await w.DaysAsync(w.W2.PublicId);
        Assert.Equal((7m, 7m, 0m, 0m, 1, 1), (r2.ReceivedToday, r2.ReceivedTotal, r2.OutboundToday, r2.OutboundTotal, r2.CountsWithVarianceToday, r2.CountsWithVarianceTotal));

        // Varios almacenes = la suma.
        var both = await w.DaysAsync(w.W1.PublicId, w.W2.PublicId);
        Assert.Equal((await w.DaysAsync()).Days, both.Days);
    }

    [Fact]
    public async Task Unknown_or_foreign_warehouse_gives_zero_and_never_falls_back_to_all()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var foreign = await w.F.Db.Warehouses.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Code == "W3");
        foreach (var id in new[] { Guid.NewGuid(), foreign.PublicId })
        {
            var r = await w.DaysAsync(id);
            Assert.Equal(7, r.Days.Count);
            Assert.All(r.Days, d => Assert.Equal((0m, 0, 0m, 0, 0), (d.ReceivedUnits, d.ReceivedMovements, d.OutboundUnits, d.OutboundMovements, d.CountsWithVariance)));
            Assert.Equal((0m, 0m, 0, 0, false, false), (r.ReceivedTotal, r.OutboundTotal, r.CountsWithVarianceTotal, r.BelowMinProducts, r.CountsAlert, r.BelowMinAlert));
        }

        // La otra compañía solo ve lo suyo (el TenantId sale del contexto).
        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            w.F.Db.ChangeTracker.Clear();
            var other = await w.DaysAsync();
            Assert.Equal((50m, 0m, 1, 0), (other.ReceivedTotal, other.OutboundTotal, other.CountsWithVarianceTotal, other.BelowMinProducts));
        }
    }

    [Fact]
    public async Task Empty_days_are_zero_and_days_out_of_range_is_400()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var one = await w.Svc.DaysAsync(new WarehousePulseQuery(Days: 1), default);
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(one.Days).Date);
        Assert.Equal(10m, one.ReceivedTotal);
        var fourteen = await w.Svc.DaysAsync(new WarehousePulseQuery(Days: 14), default);
        Assert.Equal(14, fourteen.Days.Count);
        Assert.Equal(120m, fourteen.ReceivedTotal);   // ahora sí entra el recibo de 100 del 23
        Assert.Equal(9, fourteen.Days.Count(d => d.ReceivedUnits == 0 && d.OutboundUnits == 0 && d.CountsWithVariance == 0 && d.ReceivedMovements == 0));

        foreach (var days in new[] { 0, 15, -3 })
        {
            var v = await Assert.ThrowsAsync<ValidationException>(() => w.Svc.DaysAsync(new WarehousePulseQuery(Days: days), default));
            Assert.Equal(400, v.StatusCode);
            Assert.Equal("Los días deben estar entre 1 y 14.", v.Message);
            Assert.Equal(new[] { "Los días deben estar entre 1 y 14." }, v.Errors!["days"]);
        }
        // Sin consulta = 7 días de todos los almacenes.
        Assert.Equal(7, (await w.Svc.DaysAsync(null, default)).Days.Count);
    }

    [Fact]
    public async Task Seven_day_totals_match_the_last7_indicators_and_the_daily_chart_in_the_same_local_days()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var r = await w.DaysAsync();

        // Lote 15: LAST7 de indicadores y gráficos = los mismos 7 días LOCALES de la franja (antes eran días UTC).
        Assert.Equal((r.FromUtc, r.ToUtc), DateRangeResolver.Resolve(DateRangeModes.Last7, null, null, w.F.Get<ITenantClock>()));

        // Definiciones como las sembradas (rango LAST7), evaluadas por AnalyticsService (resolutor + motor reales).
        var id = 9000;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        w.F.Db.LookupCodes.AddRange(L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.AggregateFn, AggregateFns.Count),
            L(LookupDomains.AggregateFn, AggregateFns.Sum), L(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            L(LookupDomains.DateRangeMode, DateRangeModes.Last7), L(LookupDomains.ReportChartType, ChartTypes.Line));
        await w.F.Db.SaveChangesAsync();
        w.F.Lookups.Load(w.F.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        int Id(string domain, string code) => w.F.Lookups.GetIdAsync(domain, code).Result;
        Teikem.Domain.Analytics.IndicatorDefinition Ind(string name, string source, string? field, string fn, string filter) => new()
        {
            TenantId = WmsFixture.TenantId, Name = name, DataSourceKey = source, FieldKey = field, FilterJson = filter, IsSystem = true,
            AggregateFnLookupId = Id(LookupDomains.AggregateFn, fn), BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), DateRangeModeLookupId = Id(LookupDomains.DateRangeMode, DateRangeModes.Last7),
        };
        var counts = Ind(SystemAnalyticsSeeder.CountsWithVarianceIndicatorName, EntityTypes.CycleCount, null, AggregateFns.Count, SystemAnalyticsSeeder.ReconciledCountsWithVarianceFilter);
        var received = Ind(SystemAnalyticsSeeder.ReceivedUnitsIndicatorName, EntityTypes.InventoryTransaction, "Quantity", AggregateFns.Sum, SystemAnalyticsSeeder.ReceiptMovementsFilter);
        var daily = new Teikem.Domain.Analytics.ChartDefinition
        {
            TenantId = WmsFixture.TenantId, Name = "Recibido por día", DataSourceKey = EntityTypes.InventoryTransaction, GroupByField = "Date", FieldKey = "Quantity",
            FilterJson = SystemAnalyticsSeeder.ReceiptMovementsFilter, IsSystem = true, ChartTypeLookupId = Id(LookupDomains.ReportChartType, ChartTypes.Line),
            AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Sum), BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), DateRangeModeLookupId = Id(LookupDomains.DateRangeMode, DateRangeModes.Last7),
        };
        w.F.Db.AddRange(counts, received, daily);
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        var svc = w.F.Get<AnalyticsService>();

        var c = await svc.EvaluateIndicatorAsync(counts.IndicatorDefinitionId, default);
        Assert.Equal((r.FromUtc, r.ToUtc), (c.FromUtc!.Value, c.ToUtc!.Value));
        Assert.Equal(r.CountsWithVarianceTotal, (int)c.Value!.Value);   // 2: el de ayer a las 22:00 locales y el de hoy
        var rec = await svc.EvaluateIndicatorAsync(received.IndicatorDefinitionId, default);
        Assert.Equal(r.ReceivedTotal, rec.Value);                        // 20: el recibo de las 23:59 del 23 local queda fuera

        // Agrupar por día = día LOCAL: el recibo de las 23:59 del 28 (03:59Z del 29) cae en el 28, como en la franja.
        var points = (await svc.EvaluateChartAsync(daily.ChartDefinitionId, default)).Points;
        Assert.Equal(r.Days.Where(d => d.ReceivedUnits != 0).Select(d => (d.Date.ToString("yyyy-MM-dd"), d.ReceivedUnits)),
            points.Select(p => (p.Label, p.Value)));
        Assert.Equal(new[] { "2026-09-28", "2026-09-30" }, points.Select(p => p.Label));

        // Bajo mínimo = la consulta de GET /products?belowMin=true (la del panel Almacén).
        var list = await w.F.Get<ProductService>().ListAsync(new ProductListQuery(BelowMin: true, Take: 1), InventoryScope.Any, default);
        Assert.Equal(list.Total, r.BelowMinProducts);
    }

    // ---------------------------------------------------------------- traducción a SQL Server (sin BD)

    [Fact]
    public void Movement_groups_translate_to_a_group_by_case_in_sql_server()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var days = WarehousePulseRules.Days(Now, LocalDay.DefaultZone);
        var ids = new WarehousePulseService.PulseTxnIds(1, 2, 3, 4, 5, 6);
        foreach (var warehouses in new[] { null, new[] { 10, 11 } })
        {
            var sql = WarehousePulseService.MovementGroupsQuery(db.InventoryTransactions.AsNoTracking(), days, ids, warehouses).ToQueryString();
            Assert.Contains("FROM [InventoryTransaction]", sql);
            // El índice de día es un CASE sobre las medianoches locales y el agrupado va en SQL (EF lo agrupa sobre la subconsulta).
            Assert.Matches(@"CASE\s+WHEN \[i\]\.\[CreatedAtUtc\] < '2026-09-25T04:00:00\.0000000' THEN 0", sql);
            Assert.Contains("END AS [Day]", sql);
            Assert.Contains("GROUP BY [t].[Day], [t].[TxnTypeLookupId], [t].[ReasonLookupId]", sql);
            Assert.Contains("SUM(", sql);
            Assert.Contains("COUNT(*)", sql);
            if (warehouses is not null) Assert.Contains("COALESCE(", sql);
        }
    }
}
