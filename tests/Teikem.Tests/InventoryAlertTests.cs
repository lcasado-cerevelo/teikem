using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 141 (2026-10-20) — avisos de inventario: reglas puras (vencido / por vencer / saldo bajo / sin existencia / sin movimiento, clave de no repetición) y la revisión
/// <see cref="InventoryAlertService"/> sobre datos reales: un aviso por evento con el conjunto, sin repetirse si no cambia.
/// </summary>
public class InventoryAlertTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    // ------------------------------------------------------------------ reglas puras

    [Theory]
    [InlineData("2026-10-19", true, false)]    // ayer: vencido
    [InlineData("2026-10-20", false, true)]    // hoy: todavía se puede usar, pero está por vencer
    [InlineData("2026-11-19", false, true)]    // justo en el plazo (30 días)
    [InlineData("2026-11-20", false, false)]   // un día fuera del plazo
    public void Expired_and_expiring_are_exclusive_and_the_expiry_day_still_counts_as_usable(string expiry, bool expired, bool expiring)
    {
        var d = DateOnly.Parse(expiry);
        Assert.Equal(expired, InventoryAlertRules.IsExpired(d, Today));
        Assert.Equal(expiring, InventoryAlertRules.IsExpiring(d, Today, 30));
    }

    [Fact]
    public void Days_to_expiry_is_negative_when_expired()
    {
        Assert.Equal(-3, InventoryAlertRules.DaysToExpiry(Today.AddDays(-3), Today));
        Assert.Equal(12, InventoryAlertRules.DaysToExpiry(Today.AddDays(12), Today));
    }

    [Theory]
    [InlineData(10.0, 9.99, true, true)]
    [InlineData(10.0, 10.0, true, false)]
    [InlineData(null, 0.0, true, false)]    // sin mínimo no hay saldo bajo
    [InlineData(10.0, 0.0, false, false)]   // un producto inactivo no avisa
    public void Low_stock_is_below_the_minimum_of_an_active_product(double? min, double available, bool active, bool expected)
        => Assert.Equal(expected, InventoryAlertRules.IsLow(min is null ? null : (decimal)min, (decimal)available, active));

    [Theory]
    [InlineData(true, 0.0, true)]
    [InlineData(true, 5.0, false)]
    [InlineData(false, 0.0, false)]           // un producto que nunca tuvo movimientos no está «sin existencia»: es nuevo
    public void Out_of_stock_needs_a_past_movement_and_nothing_on_hand(bool hadMovements, double onHand, bool expected)
        => Assert.Equal(expected, InventoryAlertRules.IsOutOfStock(hadMovements, (decimal)onHand));

    [Fact]
    public void Stale_means_stock_on_hand_and_an_old_last_movement()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(InventoryAlertRules.IsStale(now.AddDays(-90), now, 90, 4m));
        Assert.False(InventoryAlertRules.IsStale(now.AddDays(-89), now, 90, 4m));
        Assert.False(InventoryAlertRules.IsStale(now.AddDays(-200), now, 90, 0m));   // sin existencia no es «sin movimiento»
        Assert.False(InventoryAlertRules.IsStale(null, now, 90, 4m));
    }

    [Fact]
    public void Days_settings_have_exact_ranges()
    {
        Assert.True(InventoryAlertRules.ValidExpiryWarnDays(1));
        Assert.True(InventoryAlertRules.ValidExpiryWarnDays(365));
        Assert.False(InventoryAlertRules.ValidExpiryWarnDays(0));
        Assert.False(InventoryAlertRules.ValidExpiryWarnDays(366));
        Assert.True(InventoryAlertRules.ValidNoMovementDays(3650));
        Assert.False(InventoryAlertRules.ValidNoMovementDays(3651));
        Assert.Equal("Los días de aviso de vencimiento deben estar entre 1 y 365.", InventoryAlertRules.ExpiryWarnDaysRange);
        Assert.Equal("Los días sin movimiento deben estar entre 1 y 3650.", InventoryAlertRules.NoMovementDaysRange);
    }

    [Fact]
    public void Dedup_key_is_stable_per_set_and_week_and_changes_with_either()
    {
        var mon = new DateTime(2026, 10, 19, 6, 0, 0, DateTimeKind.Utc);
        var a = InventoryAlertRules.DedupKey("LOW", mon, new[] { "1", "2" });
        Assert.Equal(a, InventoryAlertRules.DedupKey("LOW", mon.AddDays(2), new[] { "2", "1" }));   // mismo conjunto, misma semana, otro orden
        Assert.NotEqual(a, InventoryAlertRules.DedupKey("LOW", mon, new[] { "1", "2", "3" }));      // el conjunto cambió
        Assert.NotEqual(a, InventoryAlertRules.DedupKey("LOW", mon.AddDays(7), new[] { "1", "2" })); // recordatorio de la semana siguiente
        Assert.InRange(a.Length, 1, 100);
    }

    [Fact]
    public void Items_text_is_cut_after_ten_with_the_remainder()
    {
        var items = Enumerable.Range(1, 13).Select(i => $"P{i}").ToList();
        var text = InventoryAlertRules.ItemsText(items).Split('\n');
        Assert.Equal(11, text.Length);
        Assert.Equal("… +3", text[^1]);
        Assert.Equal("P1\nP2", string.Join("\n", InventoryAlertRules.ItemsText(new[] { "P1", "P2" }).Split('\n')));
    }

    // ------------------------------------------------------------------ catálogo y trabajo

    [Fact]
    public void Events_are_defined_for_the_inventory_module_with_known_variables_only()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new InventoryEventProvider() });
        Assert.Equal(InventoryEventCodes.All.OrderBy(x => x), catalog.All.Select(e => e.Code).OrderBy(x => x));
        foreach (var e in catalog.All)
        {
            Assert.InRange(e.Code.Length, 1, 60);
            Assert.Equal(ModuleKeys.WmsLotSerial, e.ModuleKey);
            Assert.True(e.IsOptional);
            var known = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), v => Assert.Contains(v, known));
        }
    }

    [Fact]
    public void The_scan_job_runs_daily_at_six_and_is_on_by_default()
    {
        var job = new InventoryAlertScanJob();
        Assert.Equal((JobKeys.InventoryAlertScan, JobScheduleKinds.Daily, new TimeSpan(6, 0, 0), true), (job.Key, job.DefaultKind, job.DefaultDailyAt, job.DefaultEnabled));
    }

    // ------------------------------------------------------------------ la revisión sobre datos

    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Bin, InventoryAlertService Svc);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddSingleton<INotificationEventProvider, InventoryEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<InventoryAlertService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        return new World(f, w, bin, f.Get<InventoryAlertService>());
    }

    private static Task ReceiveAsync(World x, Product p, decimal qty, int? lotId = null)
        => x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, qty, LotId: lotId, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));

    /// <summary>El texto «items» del aviso (el JSON escapa los «·», por eso se lee ya interpretado).</summary>
    private static string ItemsOf(NotificationOutbox o) => System.Text.Json.JsonDocument.Parse(o.PayloadJson).RootElement.GetProperty("items").GetString() ?? string.Empty;

    private static Task<List<NotificationOutbox>> OutboxAsync(WmsFixture f, string code)
        => f.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == code).OrderBy(o => o.NotificationOutboxId).ToListAsync();

    [Fact]
    public async Task Lots_in_stock_are_split_into_expired_and_expiring_and_the_rest_are_left_alone()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var p = await x.F.AddProductAsync("MED", TrackingTypes.Lot);
        var old = await x.F.AddLotAsync(p, "L-OLD", new DateOnly(2026, 10, 1));
        var soon = await x.F.AddLotAsync(p, "L-SOON", new DateOnly(2026, 11, 5));
        var far = await x.F.AddLotAsync(p, "L-FAR", new DateOnly(2027, 6, 1));
        await x.F.AddLotAsync(p, "L-EMPTY", new DateOnly(2026, 9, 1));   // vencido pero sin existencia: no se avisa
        foreach (var lot in new[] { old, soon, far }) await ReceiveAsync(x, p, 5m, lot.LotId);

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal((1, 1), (r.Expiring, r.Expired));
        var expiring = Assert.Single(await OutboxAsync(x.F, InventoryEventCodes.LotExpiring));
        Assert.Equal("MED · L-SOON · 2026-11-05 · 5 · W1", ItemsOf(expiring));
        Assert.DoesNotContain("L-FAR", ItemsOf(expiring));
        var expired = Assert.Single(await OutboxAsync(x.F, InventoryEventCodes.LotExpired));
        Assert.Contains("L-OLD", ItemsOf(expired));
        Assert.DoesNotContain("L-EMPTY", ItemsOf(expired));
    }

    [Fact]
    public async Task The_same_set_is_not_announced_twice_in_the_same_week_but_a_new_lot_makes_a_new_notice()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var p = await x.F.AddProductAsync("MED", TrackingTypes.Lot);
        await ReceiveAsync(x, p, 5m, (await x.F.AddLotAsync(p, "L-1", new DateOnly(2026, 10, 25))).LotId);

        Assert.Equal(1, (await x.Svc.ScanAsync(default)).Expiring);
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).Expiring);   // mismo conjunto, misma semana
        await ReceiveAsync(x, p, 2m, (await x.F.AddLotAsync(p, "L-2", new DateOnly(2026, 10, 28))).LotId);
        Assert.Equal(1, (await x.Svc.ScanAsync(default)).Expiring);   // entró otro lote al plazo
        Assert.Equal(2, (await OutboxAsync(x.F, InventoryEventCodes.LotExpiring)).Count);
    }

    [Fact]
    public async Task The_company_setting_changes_the_expiry_window()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var p = await x.F.AddProductAsync("MED", TrackingTypes.Lot);
        await ReceiveAsync(x, p, 5m, (await x.F.AddLotAsync(p, "L-45", Today.AddDays(45))).LotId);
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).Expiring);   // 30 días por omisión: faltan 45
        (await x.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).InventoryExpiryWarnDays = 60;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(1, (await x.Svc.ScanAsync(default)).Expiring);
    }

    [Fact]
    public async Task Low_stock_counts_available_in_all_balances_against_the_product_minimum()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var low = await x.F.AddProductAsync("LOW-1");
        var ok = await x.F.AddProductAsync("OK-1");
        var inactive = await x.F.AddProductAsync("OFF-1");
        await ReceiveAsync(x, low, 4m);
        await ReceiveAsync(x, ok, 25m);
        await ReceiveAsync(x, inactive, 1m);
        foreach (var p in new[] { low, ok, inactive }) (await x.F.Db.Products.AsTracking().SingleAsync(r => r.ProductId == p.ProductId)).MinQty = 10m;
        (await x.F.Db.Products.AsTracking().SingleAsync(r => r.ProductId == inactive.ProductId)).IsActive = false;   // se da de baja con existencia: no avisa
        await x.F.Db.SaveChangesAsync();

        Assert.Equal(1, (await x.Svc.ScanAsync(default)).Low);
        var notice = Assert.Single(await OutboxAsync(x.F, InventoryEventCodes.StockLow));
        Assert.Equal("LOW-1 · 4 · 10", ItemsOf(notice));

    }

    [Fact]
    public async Task Out_of_stock_needs_a_past_movement_so_a_brand_new_product_is_not_reported()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var emptied = await x.F.AddProductAsync("GONE-1");
        await x.F.AddProductAsync("NEW-1");
        await ReceiveAsync(x, emptied, 3m);
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, emptied.ProductId, 3m, FromWarehouseId: x.W.WarehouseId, FromBinId: x.Bin.WarehouseBinId));

        Assert.Equal(1, (await x.Svc.ScanAsync(default)).Out);
        var notice = Assert.Single(await OutboxAsync(x.F, InventoryEventCodes.StockOut));
        Assert.Equal("GONE-1", ItemsOf(notice));
    }

    [Fact]
    public async Task No_movement_reports_stocked_products_whose_last_movement_is_older_than_the_setting()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var dusty = await x.F.AddProductAsync("DUST-1");
        var fresh = await x.F.AddProductAsync("FRESH-1");
        await ReceiveAsync(x, dusty, 8m);
        await ReceiveAsync(x, fresh, 8m);
        foreach (var t in await x.F.Db.InventoryTransactions.AsTracking().Where(t => t.ProductId == dusty.ProductId).ToListAsync()) t.CreatedAtUtc = Now.AddDays(-120);
        await x.F.Db.SaveChangesAsync();

        Assert.Equal(1, (await x.Svc.ScanAsync(default)).NoMovement);
        var notice = Assert.Single(await OutboxAsync(x.F, InventoryEventCodes.NoMovement));
        Assert.Equal("DUST-1 · 120 · 8", ItemsOf(notice));
    }

    [Fact]
    public async Task An_empty_company_scans_without_publishing_anything()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).Total);
        Assert.Empty(await x.F.Db.NotificationOutbox.AsNoTracking().ToListAsync());
    }

    
}
