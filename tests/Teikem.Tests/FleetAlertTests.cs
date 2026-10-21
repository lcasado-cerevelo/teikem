using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Notifications;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 168 (2026-10-21) — avisos de flota: reglas puras (documento vencido / por vencer), catálogo de eventos, trabajo diario y la revisión
/// <see cref="FleetAlertService"/> sobre datos: un aviso por evento con el conjunto, sin repetirse si no cambia.
/// </summary>
public class FleetAlertTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------------ reglas puras

    [Theory]
    [InlineData("2026-10-19", true, false)]    // ayer: vencido
    [InlineData("2026-10-20", false, true)]    // hoy: todavía vale, pero está por vencer
    [InlineData("2026-11-19", false, true)]    // justo en el plazo (30 días)
    [InlineData("2026-11-20", false, false)]   // un día fuera del plazo
    public void Expired_and_expiring_are_exclusive(string expiry, bool expired, bool expiring)
    {
        var d = DateOnly.Parse(expiry);
        Assert.Equal(expired, FleetAlertRules.IsExpired(d, Today));
        Assert.Equal(expiring, FleetAlertRules.IsExpiring(d, Today, 30));
    }

    [Fact]
    public void A_document_without_expiry_never_alerts()
    {
        Assert.False(FleetAlertRules.IsExpired(null, Today));
        Assert.False(FleetAlertRules.IsExpiring(null, Today, 30));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void The_warning_days_are_between_1_and_365(int days, bool valid) => Assert.Equal(valid, FleetAlertRules.ValidDocWarnDays(days));

    // ------------------------------------------------------------------ catálogo y trabajo

    [Fact]
    public void Events_are_optional_with_known_variables_only()
    {
        var catalog = new NotificationEventCatalog(new INotificationEventProvider[] { new FleetEventProvider() });
        Assert.Equal(FleetEventCodes.All.OrderBy(x => x), catalog.All.Select(e => e.Code).OrderBy(x => x));
        foreach (var e in catalog.All)
        {
            Assert.InRange(e.Code.Length, 1, 60);
            Assert.Equal(ModuleKeys.Catalog, e.ModuleKey);
            Assert.True(e.IsOptional);
            var known = e.Variables.Select(v => v.Name).ToHashSet();
            foreach (var text in new[] { e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn })
                Assert.All(NotificationRules.VariablesOf(text), v => Assert.Contains(v, known));
        }
    }

    [Fact]
    public void The_scan_job_runs_daily_and_is_on_by_default()
    {
        var job = new FleetAlertScanJob();
        Assert.Equal((JobKeys.FleetAlertScan, JobScheduleKinds.Daily, true), (job.Key, job.DefaultKind, job.DefaultEnabled));
    }

    // ------------------------------------------------------------------ la revisión sobre datos

    private sealed record World(WmsFixture F, FleetAlertService Svc, int InsuranceType, int LicenseClass, int TimeTrigger);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => Now));
            s.AddSingleton<INotificationEventProvider, FleetEventProvider>();
            s.AddSingleton<NotificationEventCatalog>();
            s.AddSingleton<INotificationPublisher, NotificationPublisher>();
            s.AddSingleton<FleetDocumentService>();
            s.AddSingleton<MaintenanceScheduleService>();
            s.AddSingleton<FleetAlertService>();
        });
        int Lookup(int id, string entity, string code)
        {
            f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = id, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true });
            return id;
        }
        var ins = Lookup(9001, LookupDomains.VehicleDocType, "INSURANCE");
        var lic = Lookup(9002, LookupDomains.LicenseClass, "CLASS_B");
        var time = Lookup(9003, LookupDomains.MaintenanceTrigger, MaintenanceTriggers.Time);
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9101, Entity = StatusDomains.WorkOrderStatus, InternalCode = WorkOrderStatuses.Closed, LabelJson = "{}", SortOrder = 9, IsActive = true });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9102, Entity = StatusDomains.WorkOrderStatus, InternalCode = WorkOrderStatuses.Open, LabelJson = "{}", SortOrder = 1, IsActive = true });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9103, Entity = StatusDomains.WorkOrderStatus, InternalCode = WorkOrderStatuses.InProgress, LabelJson = "{}", SortOrder = 2, IsActive = true });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9104, Entity = StatusDomains.TripStatus, InternalCode = TripStatuses.Cancelled, LabelJson = "{}", SortOrder = 9, IsActive = true });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 9105, Entity = StatusDomains.TripStatus, InternalCode = TripStatuses.Planned, LabelJson = "{}", SortOrder = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(await f.Db.LookupCodes.AsNoTracking().ToListAsync());
        return new World(f, f.Get<FleetAlertService>(), ins, lic, time);
    }

    private static async Task<Vehicle> AddVehicleAsync(World x, string code, int id)
    {
        var v = new Vehicle { VehicleId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = code, StatusCodeId = 1, IsActive = true, CurrentOdometerKm = 1000m };
        x.F.Db.Vehicles.Add(v);
        await x.F.Db.SaveChangesAsync();
        return v;
    }

    private static async Task AddInsuranceAsync(World x, Vehicle v, int id, DateOnly? expiry)
    {
        x.F.Db.VehicleDocuments.Add(new VehicleDocument { VehicleDocumentId = id, VehicleId = v.VehicleId, DocTypeLookupId = x.InsuranceType, DocNumber = "S-" + id, ExpiryDate = expiry, IsActive = true });
        await x.F.Db.SaveChangesAsync();
    }

    private static string ItemsOf(NotificationOutbox o) => System.Text.Json.JsonDocument.Parse(o.PayloadJson).RootElement.GetProperty("items").GetString() ?? string.Empty;

    private static Task<List<NotificationOutbox>> OutboxAsync(WmsFixture f, string code)
        => f.Db.NotificationOutbox.AsNoTracking().Where(o => o.EventCode == code).OrderBy(o => o.NotificationOutboxId).ToListAsync();

    [Fact]
    public async Task Documents_are_split_into_expired_and_expiring_and_the_rest_are_left_alone()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var v1 = await AddVehicleAsync(x, "V-01", 1);
        var v2 = await AddVehicleAsync(x, "V-02", 2);
        var v3 = await AddVehicleAsync(x, "V-03", 3);
        var v4 = await AddVehicleAsync(x, "V-04", 4);
        await AddInsuranceAsync(x, v1, 1, Today.AddDays(-5));    // vencido
        await AddInsuranceAsync(x, v2, 2, Today.AddDays(10));    // por vencer
        await AddInsuranceAsync(x, v3, 3, Today.AddDays(200));   // lejos
        await AddInsuranceAsync(x, v4, 4, null);                 // sin vencimiento

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal((1, 1), (r.DocsExpiring, r.DocsExpired));
        Assert.Equal("V V-02 · INSURANCE · 2026-10-30 · 10", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.DocExpiring))));
        Assert.Equal("V V-01 · INSURANCE · 2026-10-15 · 5", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.DocExpired))));
    }

    [Fact]
    public async Task A_renewed_document_hides_the_old_one_and_a_driver_license_is_included()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var v = await AddVehicleAsync(x, "V-01", 1);
        await AddInsuranceAsync(x, v, 1, Today.AddDays(-30));    // el seguro viejo ya se renovó
        await AddInsuranceAsync(x, v, 2, Today.AddDays(300));
        x.F.Db.Drivers.Add(new Driver { DriverId = 1, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = "C-01", FullName = "Ana", StatusCodeId = 1, IsActive = true });
        x.F.Db.DriverLicenses.Add(new DriverLicense { DriverLicenseId = 1, DriverId = 1, LicenseClassLookupId = x.LicenseClass, LicenseNumber = "L-1", ExpiryDate = Today.AddDays(3), IsActive = true });
        await x.F.Db.SaveChangesAsync();

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal((1, 0), (r.DocsExpiring, r.DocsExpired));
        Assert.StartsWith("C C-01 · CLASS_B", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.DocExpiring))));
    }

    [Fact]
    public async Task The_same_set_is_not_announced_twice_in_the_same_week_but_a_new_document_makes_a_new_notice()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await AddInsuranceAsync(x, await AddVehicleAsync(x, "V-01", 1), 1, Today.AddDays(5));
        Assert.Equal(1, (await x.Svc.ScanAsync(default)).DocsExpiring);
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).DocsExpiring);   // mismo conjunto, misma semana
        await AddInsuranceAsync(x, await AddVehicleAsync(x, "V-02", 2), 2, Today.AddDays(7));
        Assert.Equal(1, (await x.Svc.ScanAsync(default)).DocsExpiring);   // entró otro documento al plazo
        Assert.Equal(2, (await OutboxAsync(x.F, FleetEventCodes.DocExpiring)).Count);
    }

    [Fact]
    public async Task The_company_setting_changes_the_window()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await AddInsuranceAsync(x, await AddVehicleAsync(x, "V-01", 1), 1, Today.AddDays(45));
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).DocsExpiring);   // 30 días por omisión: faltan 45
        (await x.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).FleetDocWarnDays = 60;
        await x.F.Db.SaveChangesAsync();
        Assert.Equal(1, (await x.Svc.ScanAsync(default)).DocsExpiring);
    }

    [Fact]
    public async Task Maintenance_coming_up_and_overdue_are_announced_apart()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var v1 = await AddVehicleAsync(x, "V-01", 1);
        var v2 = await AddVehicleAsync(x, "V-02", 2);
        var v3 = await AddVehicleAsync(x, "V-03", 3);
        // Cada 100 días: último servicio hace 95 (quedan 5 = por vencer), hace 120 (vencido) y hace 10 (al día).
        void Sched(int id, Vehicle v, int daysAgo) => x.F.Db.MaintenanceSchedules.Add(new MaintenanceSchedule
        {
            MaintenanceScheduleId = id, TenantId = WmsFixture.TenantId, VehicleId = v.VehicleId, Name = "Aceite", TriggerLookupId = x.TimeTrigger,
            IntervalDays = 100, LastServiceDate = Today.AddDays(-daysAgo), IsActive = true,
        });
        Sched(1, v1, 95); Sched(2, v2, 120); Sched(3, v3, 10);
        await x.F.Db.SaveChangesAsync();

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal((1, 1), (r.MaintenanceDueSoon, r.MaintenanceOverdue));
        Assert.Equal("V-01 · Aceite · faltan 5 días", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.MaintenanceDueSoon))));
        Assert.Equal("V-02 · Aceite · pasado por 20 días", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.MaintenanceOverdue))));
    }

    // ------------------------------------------------------------------ Lote 179 (G2): reglas puras

    [Theory]
    [InlineData(14, false)]   // exactamente 14 días: todavía no
    [InlineData(15, true)]
    [InlineData(0, false)]
    public void A_work_order_is_stale_after_more_than_14_days(int daysAgo, bool stale)
        => Assert.Equal(stale, FleetAlertRules.IsWorkOrderStale(Today.AddDays(-daysAgo), Today));

    [Fact]
    public void Idle_counts_from_the_last_trip_or_from_the_hire_date_when_there_is_none()
    {
        Assert.Null(FleetAlertRules.IdleDaysOf(Today.AddDays(-14), Today.AddDays(-100), Today));          // 14: todavía no
        Assert.Equal(15, FleetAlertRules.IdleDaysOf(Today.AddDays(-15), Today.AddDays(-100), Today));
        Assert.Equal(40, FleetAlertRules.IdleDaysOf(null, Today.AddDays(-40), Today));                    // nunca viajó: desde su alta
        Assert.Null(FleetAlertRules.IdleDaysOf(null, Today.AddDays(-3), Today));                           // recién dado de alta
        Assert.Null(FleetAlertRules.IdleDaysOf(Today.AddDays(-30), Today.AddDays(-2), Today));            // un viaje anterior a su alta no cuenta
    }

    [Fact]
    public void Fuel_economy_is_out_of_range_when_it_is_over_30_percent_from_the_average_of_the_previous()
    {
        var prev = new List<decimal> { 7m, 7.2m, 6.8m };
        var bad = FleetAlertRules.FuelDeviation(4.2m, prev)!.Value;
        Assert.Equal((7m, -40m, true), (bad.Mean, bad.DeviationPct, bad.Out));
        Assert.False(FleetAlertRules.FuelDeviation(6.5m, prev)!.Value.Out);
        Assert.True(FleetAlertRules.FuelDeviation(9.5m, prev)!.Value.Out);                       // mucho mejor también es raro (+35 %)
        Assert.Null(FleetAlertRules.FuelDeviation(4m, new List<decimal> { 7m, 7m }));           // poca historia: no se juzga
        // solo pesan las últimas 5
        Assert.Equal(10m, FleetAlertRules.FuelDeviation(10m, new List<decimal> { 1m, 1m, 10m, 10m, 10m, 10m, 10m })!.Value.Mean);
    }

    // ------------------------------------------------------------------ Lote 179 (G2): la revisión sobre datos

    [Fact]
    public async Task Work_orders_open_for_more_than_14_days_are_announced_and_closed_or_recent_ones_are_not()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var v = await AddVehicleAsync(x, "V-01", 1);
        void Wo(int id, int statusId, int daysAgo) => x.F.Db.MaintenanceWorkOrders.Add(new MaintenanceWorkOrder
        {
            WorkOrderId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, VehicleId = v.VehicleId, Number = $"OT-{id:00000}", MaintenanceTypeLookupId = x.TimeTrigger,
            StatusCodeId = statusId, IsActive = true, CreatedAtUtc = Now.AddDays(-daysAgo),
        });
        Wo(1, 9102, 20);   // abierta hace 20 días
        Wo(2, 9103, 15);   // en proceso hace 15
        Wo(3, 9102, 5);    // reciente
        Wo(4, 9101, 40);   // cerrada
        await x.F.Db.SaveChangesAsync();

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal(1, r.WorkOrdersStale);
        Assert.Equal("OT-00001 · V-01 · abierta hace 20 días\nOT-00002 · V-01 · abierta hace 15 días", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.WorkOrderStale))).Replace("\r", ""));
        Assert.Equal(0, (await x.Svc.ScanAsync(default)).WorkOrdersStale);   // mismo conjunto, misma semana
    }

    [Fact]
    public async Task Fuel_that_is_far_from_the_vehicle_average_is_announced_once_there_is_enough_history()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var v = await AddVehicleAsync(x, "V-01", 1);
        var ok = await AddVehicleAsync(x, "V-02", 2);
        // cada carga recorre 700 km: 100 litros = 7 km/L; la última, 175 litros = 4 km/L
        void Fill(int id, Vehicle veh, int daysAgo, decimal odometer, decimal liters) => x.F.Db.FuelLogs.Add(new FuelLog
        {
            FuelLogId = id, TenantId = WmsFixture.TenantId, VehicleId = veh.VehicleId, FillDateUtc = Now.AddDays(-daysAgo), OdometerKm = odometer, Liters = liters, TotalCost = liters * 2m, IsActive = true,
        });
        Fill(1, v, 100, 1000m, 50m);
        Fill(2, v, 80, 1700m, 100m);
        Fill(3, v, 60, 2400m, 100m);
        Fill(4, v, 40, 3100m, 100m);
        Fill(5, v, 3, 3800m, 175m);     // 4 km/L contra 7: -43 %
        Fill(6, ok, 100, 1000m, 50m);
        Fill(7, ok, 80, 1700m, 100m);
        Fill(8, ok, 60, 2400m, 100m);
        Fill(9, ok, 40, 3100m, 100m);
        Fill(10, ok, 3, 3800m, 100m);   // igual que siempre
        await x.F.Db.SaveChangesAsync();

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal(1, r.FuelOutOfRange);
        Assert.Equal("V-01 · 4 km/L · promedio 7 · -42.9 %", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.FuelOutOfRange))));
    }

    [Fact]
    public async Task Vehicles_and_drivers_without_a_trip_for_more_than_14_days_are_announced_apart()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var busy = await AddVehicleAsync(x, "V-01", 1);
        var idle = await AddVehicleAsync(x, "V-02", 2);
        var cancelledOnly = await AddVehicleAsync(x, "V-03", 3);
        foreach (var v in new[] { busy, idle, cancelledOnly }) v.CreatedAtUtc = Now.AddDays(-100);
        void Drv(int id, string code) => x.F.Db.Drivers.Add(new Driver { DriverId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, EmployeeCode = code, FullName = "Nombre " + code, StatusCodeId = 1, IsActive = true, CreatedAtUtc = Now.AddDays(-100) });
        Drv(1, "C-01"); Drv(2, "C-02");
        void Trip(int id, int vehicleId, int driverId, int statusId, int daysAgo) => x.F.Db.Trips.Add(new Trip
        {
            TripId = id, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = $"T-{id}", PlanDate = Today.AddDays(-daysAgo), VehicleId = vehicleId, DriverId = driverId, StatusCodeId = statusId, IsActive = true, CreatedAtUtc = Now,
        });
        Trip(1, busy.VehicleId, 1, 9105, 3);                  // V-01 y C-01 viajaron hace 3 días
        Trip(2, idle.VehicleId, 2, 9105, 30);                 // V-02 y C-02, hace 30
        Trip(3, cancelledOnly.VehicleId, 2, 9104, 1);         // un viaje cancelado no cuenta
        await x.F.Db.SaveChangesAsync();

        var r = await x.Svc.ScanAsync(default);
        Assert.Equal((1, 1), (r.VehiclesIdle, r.DriversIdle));   // un aviso con V-02 y V-03 y otro con el chofer C-02
        var vehicles = ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.VehicleIdle)));
        Assert.Contains("V-02 · sin viaje desde 2026-09-20 · 30 días", vehicles);
        Assert.Contains("V-03 · nunca ha tenido viaje · 100 días", vehicles);
        Assert.DoesNotContain("V-01", vehicles);
        Assert.Equal("C-02 Nombre C-02 · sin viaje desde 2026-09-20 · 30 días", ItemsOf(Assert.Single(await OutboxAsync(x.F, FleetEventCodes.DriverIdle))));
    }
}
