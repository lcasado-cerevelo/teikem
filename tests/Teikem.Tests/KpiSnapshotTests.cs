using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 90 (P12): la foto diaria de indicadores (KpiDailySnapshot), su trabajo nocturno, las barritas de los ríos con historial y la fuente KPI_DAILY.
public sealed class KpiSnapshotTests
{
    [Fact]
    public void Rules_window_series_and_keys()
    {
        var today = new DateOnly(2026, 10, 3);
        var window = KpiRules.Window(today, 4);
        Assert.Equal(new[] { new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), today }, window.ToArray());
        Assert.Equal((7, 1, 400), (KpiRules.ClampDays(0), KpiRules.ClampDays(1), KpiRules.ClampDays(9999)));
        Assert.Equal(new[] { 0m, 5m, 0m, 9m }, KpiRules.Series(window, new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 10, 1)] = 5m, [today] = 9m }).ToArray());
        Assert.Equal(KpiKeys.KindMoney, KpiKeys.Find("cod_collected")!.Kind);
        Assert.Null(KpiKeys.Find("NOPE"));
        Assert.Equal(KpiKeys.All.Count, KpiKeys.All.Select(k => k.Key).Distinct().Count());
    }

    private sealed class World(WmsFixture f, KpiSnapshotService snapshots, KpiHistory history, PulseRiverService rivers, CodService cod, IServiceProvider sp)
    {
        public WmsFixture F { get; } = f;
        public KpiSnapshotService Snapshots { get; } = snapshots;
        public KpiHistory History { get; } = history;
        public PulseRiverService Rivers { get; } = rivers;
        public CodService Cod { get; } = cod;
        public IServiceProvider Sp { get; } = sp;
    }

    private static async Task<World> NewWorldAsync(params string[] modules)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<KpiHistory>();
            s.AddSingleton<PulseRiverService>();
            s.AddSingleton<PulseRadarService>();
            s.AddSingleton<KpiSnapshotService>();
            s.AddSingleton<CodService>();
        });
        f.SetModules(modules.Length == 0 ? new[] { ModuleKeys.LtlGround, ModuleKeys.Cod } : modules);
        return new World(f, f.Get<KpiSnapshotService>(), f.Get<KpiHistory>(), f.Get<PulseRiverService>(), f.Get<CodService>(), f.Services);
    }

    private static async Task<TransportOrder> OrderAsync(World x, string number, string status, decimal? cod = null, int createdDaysAgo = 0)
    {
        var c = await x.F.AddClientAsync("C-" + number);
        var o = await x.F.AddOrderAsync(c.ClientId, number, status);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        t.CreatedAtUtc = DateTime.UtcNow.AddDays(-createdDaysAgo);
        if (cod is not null) (t.CodAmount, t.CodStatusCodeId) = (cod, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Pending));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return t;
    }

    private static async Task<Dictionary<string, decimal>> SavedTodayAsync(World x)
    {
        var rows = await x.F.Db.Set<KpiSnapshot>().AsNoTracking().Where(s => s.SnapshotDate == TenantClock.Default.Today).ToListAsync();
        var map = new Dictionary<string, decimal>();
        foreach (var r in rows) map[(await x.F.Lookups.GetAsync(r.KpiKeyLookupId))!.InternalCode] = r.NumericValue ?? 0m;
        return map;
    }

    [Fact]
    public async Task Capturing_saves_todays_figures_once_per_day_and_indicator_and_running_again_replaces_them()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.Unassigned);
        await OrderAsync(x, "B", OrderStatuses.Assigned);
        await OrderAsync(x, "C", OrderStatuses.InTransit);
        var o = await OrderAsync(x, "D", OrderStatuses.Delivered, cod: 100m);
        await x.Cod.CollectAsync(o.PublicId, new CodCollectionRequest("CASH", 60m), default);

        var (day, saved) = await x.Snapshots.CaptureAsync(default);
        Assert.Equal((TenantClock.Default.Today, KpiKeys.All.Count), (day, saved));
        var first = await SavedTodayAsync(x);
        Assert.Equal(KpiKeys.All.Count, first.Count);
        Assert.Equal((4m, 1m, 2m, 1m), (first[KpiKeys.OrdersCreated], first[KpiKeys.OrdersInWarehouse], first[KpiKeys.OrdersUnassigned], first[KpiKeys.OrdersOnRoute]));
        Assert.Equal((3m, 60m, 40m), (first[KpiKeys.OrdersOpen], first[KpiKeys.CodCollected], first[KpiKeys.CodToCollect]));   // abiertas: A, B, C; D ya entregada con 40 por cobrar

        // otra orden en almacén y se vuelve a capturar: se reemplaza la foto de hoy (misma cantidad de filas)
        await OrderAsync(x, "E", OrderStatuses.Unassigned);
        Assert.Equal(KpiKeys.All.Count, (await x.Snapshots.CaptureAsync(default)).Saved);
        var second = await SavedTodayAsync(x);
        Assert.Equal((2m, 3m), (second[KpiKeys.OrdersInWarehouse], second[KpiKeys.OrdersUnassigned]));
        Assert.Equal(KpiKeys.All.Count, await x.F.Db.Set<KpiSnapshot>().CountAsync());
    }

    [Fact]
    public async Task Only_the_enabled_modules_are_saved()
    {
        var x = await NewWorldAsync(ModuleKeys.Cod);
        await using var _ = x.F;
        await x.Snapshots.CaptureAsync(default);
        var saved = await SavedTodayAsync(x);
        Assert.Equal(KpiKeys.All.Where(k => k.Module == KpiKeys.ModuleCod).Select(k => k.Key).OrderBy(k => k).ToArray(), saved.Keys.OrderBy(k => k).ToArray());

        var none = await NewWorldAsync(ModuleKeys.Purchasing);
        await using var __ = none.F;
        Assert.Equal(0, (await none.Snapshots.CaptureAsync(default)).Saved);
    }

    [Fact]
    public async Task The_history_reads_the_saved_days_and_the_rivers_draw_their_bars_with_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.Unassigned);
        var today = TenantClock.Default.Today;
        var keyId = x.F.LookupId(KpiCodes.KeyDomain, KpiKeys.OrdersInWarehouse);
        x.F.Db.Set<KpiSnapshot>().AddRange(
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-1), KpiKeyLookupId = keyId, NumericValue = 4m },
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-3), KpiKeyLookupId = keyId, NumericValue = 9m },
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-20), KpiKeyLookupId = keyId, NumericValue = 7m });   // fuera de la ventana de 7
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var series = await x.History.SeriesAsync(KpiKeys.OrdersInWarehouse, 7, default);
        Assert.Equal(new[] { 0m, 0m, 0m, 9m, 0m, 4m, 0m }, series.ToArray());   // el último (hoy) todavía no tiene foto
        var river = await x.Rivers.OrdersAsync(default);
        var warehouse = river.Nodes.Single(n => n.Key == "IN_WAREHOUSE");
        Assert.Equal(new[] { 0m, 0m, 0m, 9m, 0m, 4m, 1m }, warehouse.Spark!.ToArray());   // los 6 días anteriores de las fotos y hoy en vivo (1 en almacén)
        Assert.Null(river.Nodes.Single(n => n.Key == "ON_ROUTE").Spark);                   // sin fotos y en cero hoy: no se dibujan siete ceros
        var hist = await x.Snapshots.HistoryAsync(KpiKeys.OrdersInWarehouse, 4, default);
        Assert.Equal(new[] { 9m, 0m, 4m, 0m }, hist.Select(h => h.Value).ToArray());
        Assert.Equal(today.AddDays(-3), hist[0].Day);
    }

    [Fact]
    public async Task The_daily_history_data_source_returns_one_row_per_day_and_indicator_in_the_range()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var today = TenantClock.Default.Today;
        var inWh = x.F.LookupId(KpiCodes.KeyDomain, KpiKeys.OrdersInWarehouse);
        var collected = x.F.LookupId(KpiCodes.KeyDomain, KpiKeys.CodCollected);
        x.F.Db.Set<KpiSnapshot>().AddRange(
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today, KpiKeyLookupId = inWh, NumericValue = 3m },
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-1), KpiKeyLookupId = inWh, NumericValue = 5m },
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-1), KpiKeyLookupId = collected, NumericValue = 120.5m },
            new KpiSnapshot { TenantId = WmsFixture.TenantId, SnapshotDate = today.AddDays(-10), KpiKeyLookupId = inWh, NumericValue = 99m });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var source = new KpiDailyDataSource(x.F.Db, x.F.Lookups, x.F.Tenant, TenantClock.Default);
        Assert.Equal((KpiCodes.DataSource, "SnapshotDate"), (source.Key, source.DateField));
        Assert.Equal(PermissionCatalog.AnalyticsView, PermissionCatalog.DataSourceReadPermission(KpiCodes.DataSource));
        var (from, to) = TenantClock.Default.UtcRange(today.AddDays(-6), today);
        var rows = await source.LoadAsync(new DataQuery { FromUtc = from, ToUtc = to }, default);
        Assert.Equal(3, rows.Count);   // lo de hace 10 días queda fuera
        var latest = rows.First(r => (string?)r["KpiKey"] == KpiKeys.OrdersInWarehouse && (decimal)r["Value"]! == 3m);
        Assert.Equal(("Envíos en almacén", "count"), ((string?)latest["KpiLabel"], (string?)latest["Kind"]));
        Assert.Equal(TenantClock.Default.StartOfDayUtc(today), (DateTime)latest["SnapshotDate"]!);
        Assert.Equal("money", (string?)rows.Single(r => (string?)r["KpiKey"] == KpiKeys.CodCollected)["Kind"]);
        Assert.Equal(4, (await source.LoadAsync(new DataQuery(), default)).Count);   // sin rango: todo
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty(await source.LoadAsync(new DataQuery(), default));
    }

    [Fact]
    public async Task The_nightly_job_runs_the_capture_and_reports_what_it_did()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await OrderAsync(x, "A", OrderStatuses.Unassigned);
        var job = new KpiSnapshotJob();
        Assert.Equal(("KPI_SNAPSHOT", true, new TimeSpan(23, 55, 0)), (job.Key, job.DefaultEnabled, job.DefaultDailyAt!.Value));
        var summary = await job.RunAsync(new JobRunContext(WmsFixture.TenantId, "test", x.Sp, DateTime.UtcNow), default);
        Assert.Contains($"{KpiKeys.All.Count} cifras guardadas", summary);
        Assert.Equal(KpiKeys.All.Count, await x.F.Db.Set<KpiSnapshot>().CountAsync());
    }
}
