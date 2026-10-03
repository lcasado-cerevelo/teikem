using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Dsl;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 20 — "hoy" en la zona horaria de la compañía en los servicios que lo calculaban en UTC. Reloj fijo: la 1:30 UTC del
/// 3 de octubre de 2026 = 2 de octubre en Puerto Rico (UTC-4) y en Honolulú (UTC-10), 3 de octubre en Nueva York (UTC-4: 21:30 del
/// día 2 → 2) y en Madrid (UTC+2: 3:30 del día 3). Cada familia de servicios prueba que la fecha de NEGOCIO es el día local y que
/// los INSTANTES (CreatedAtUtc, etc.) siguen estampándose con la hora UTC real, no con el reloj de la compañía.
/// </summary>
public sealed class TenantTodayTests
{
    private static readonly DateTime Oct3At0130Utc = new(2026, 10, 3, 1, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Oct2 = new(2026, 10, 2);
    private static readonly DateOnly Oct3 = new(2026, 10, 3);

    private static TenantClock ClockIn(string zone, DateTime? now = null)
        => new(TimeZoneInfo.FindSystemTimeZoneById(zone), () => now ?? Oct3At0130Utc);

    private static TenantClock PuertoRico() => ClockIn("America/Puerto_Rico");

    [Fact]
    public void The_fixed_instant_is_october_2_in_puerto_rico_and_honolulu_and_october_3_in_madrid()
    {
        Assert.Equal(Oct2, PuertoRico().Today);
        Assert.Equal(Oct2, ClockIn("Pacific/Honolulu").Today);
        Assert.Equal(Oct2, ClockIn("America/New_York").Today);
        Assert.Equal(Oct3, ClockIn("Europe/Madrid").Today);
    }

    // ------------------------------------------------------------------ DSL (RuleEvaluator)

    [Fact]
    public void Dsl_date_max_zero_means_today_in_the_company_zone_not_in_utc()
    {
        var rule = """{"max":0}""";
        // Puerto Rico: hoy es el 2; el 3 ya es "posterior" (con UTC se aceptaba).
        Assert.Empty(RuleEvaluator.Validate(new DateTime(2026, 10, 2), rule, PuertoRico()));
        Assert.Equal(new[] { "Fecha posterior a la máxima permitida." }, RuleEvaluator.Validate(new DateTime(2026, 10, 3), rule, PuertoRico()));
        // Madrid: hoy es el 3.
        Assert.Empty(RuleEvaluator.Validate(new DateTime(2026, 10, 3), rule, ClockIn("Europe/Madrid")));
        Assert.Single(RuleEvaluator.Validate(new DateTime(2026, 10, 4), rule, ClockIn("Europe/Madrid")));
    }

    [Fact]
    public void Dsl_date_min_is_relative_to_the_local_today()
    {
        var rule = """{"min":-1}""";   // desde ayer
        Assert.Empty(RuleEvaluator.Validate(new DateTime(2026, 10, 1), rule, PuertoRico()));
        Assert.Equal(new[] { "Fecha anterior a la mínima permitida." }, RuleEvaluator.Validate(new DateTime(2026, 9, 30), rule, PuertoRico()));
        Assert.Single(RuleEvaluator.Validate(new DateTime(2026, 10, 1), rule, ClockIn("Europe/Madrid")));
    }

    [Fact]
    public void Dsl_datetime_fields_compare_the_instant_with_local_midnight_expressed_in_utc()
    {
        var rule = """{"min":0}""";
        var localMidnightUtc = PuertoRico().StartOfDayUtc(Oct2);            // 2026-10-02 04:00 UTC
        Assert.Equal(new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc), localMidnightUtc);
        Assert.Empty(RuleEvaluator.Validate(localMidnightUtc, rule, PuertoRico(), calendarDate: false));
        Assert.Single(RuleEvaluator.Validate(localMidnightUtc.AddSeconds(-1), rule, PuertoRico(), calendarDate: false));
    }

    [Fact]
    public void Dsl_without_clock_falls_back_to_the_default_zone_and_without_date_rules_nothing_changes()
    {
        Assert.Empty(RuleEvaluator.Validate(new DateTime(2000, 1, 1), """{"max":0}"""));
        Assert.Single(RuleEvaluator.Validate(new DateTime(2999, 1, 1), """{"max":0}"""));
        Assert.Empty(RuleEvaluator.Validate(new DateTime(2999, 1, 1), null, PuertoRico()));
    }

    // ------------------------------------------------------------------ rutas y trips

    [Fact]
    public async Task Trips_monitor_and_dispatch_default_to_the_company_local_day()
    {
        await using var f = await TripServiceFixture.CreateAsync(PuertoRico());
        await f.SeedTripAsync("2026-0002", TripStatuses.Draft, f.Zone1, planDate: Oct2);
        await f.SeedTripAsync("2026-0003", TripStatuses.Draft, f.Zone1, planDate: Oct3);

        var monitor = await f.Get<TripMonitorService>().ListAsync(new MonitorQuery(), default);
        Assert.Equal(Oct2, monitor.Date);

        var dispatchable = await f.Get<TripDispatchService>().ListDispatchableAsync(null, default);
        var item = Assert.Single(dispatchable);
        Assert.Equal(Oct2, item.Trip.PlanDate);
    }

    [Fact]
    public async Task Trip_plan_date_range_is_counted_from_the_local_today()
    {
        await using var f = await TripServiceFixture.CreateAsync(PuertoRico());
        var svc = f.Get<TripService>();
        // Hoy local = 2 de octubre: el máximo es hoy + 60 = 1 de diciembre; el 2 de diciembre (hoy UTC + 60) se rechaza.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new TripCreateRequest(Oct2.AddDays(61)), default));
        var ok = await svc.CreateAsync(new TripCreateRequest(Oct2.AddDays(60)), default);
        Assert.Equal(Oct2.AddDays(60), ok.PlanDate);
    }

    [Fact]
    public async Task Trips_use_the_zone_of_the_company_east_of_utc()
    {
        await using var f = await TripServiceFixture.CreateAsync(ClockIn("Europe/Madrid"));
        var monitor = await f.Get<TripMonitorService>().ListAsync(new MonitorQuery(), default);
        Assert.Equal(Oct3, monitor.Date);
    }

    // ------------------------------------------------------------------ flota (controlador de disponibilidad)

    [Fact]
    public async Task Fleet_availability_without_date_asks_for_the_company_local_day()
    {
        var controller = new FleetController(null!, new AlwaysAvailable(), PuertoRico());
        var dto = await controller.Availability(null, false, default);
        Assert.Equal(Oct2, dto.Date);

        var other = await new FleetController(null!, new AlwaysAvailable(), ClockIn("Europe/Madrid")).Availability(null, false, default);
        Assert.Equal(Oct3, other.Date);

        var explicitDate = await controller.Availability(new DateOnly(2026, 11, 5), false, default);
        Assert.Equal(new DateOnly(2026, 11, 5), explicitDate.Date);
    }

    // ------------------------------------------------------------------ compras

    [Fact]
    public async Task Purchase_order_default_date_is_local_and_the_creation_instant_stays_utc()
    {
        await using var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(PuertoRico());
            s.AddSingleton<PurchaseOrderService>();
        });
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = "Proveedor", IsActive = true };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        var warehouse = await f.AddWarehouseAsync("W1");
        var product = await f.AddProductAsync("PA");

        var before = DateTime.UtcNow;
        var po = await f.Get<PurchaseOrderService>().CreateAsync(new PurchaseOrderCreateRequest(supplier.SupplierId, warehouse.PublicId,
            Lines: new[] { new PurchaseOrderLineRequest(product.PublicId, 4m, 2m) }), default);
        var after = DateTime.UtcNow;

        Assert.Equal(Oct2, po.OrderDate);                       // fecha de negocio: día local, no el 3 UTC
        var stored = await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.PublicId == po.PublicId);
        Assert.InRange(stored.CreatedAtUtc, before, after);      // instante: hora UTC real, no el reloj fijo de la compañía
    }

    // ------------------------------------------------------------------ servicios sin tenant (respaldo)

    [Fact]
    public void Default_clock_without_tenant_context_works_for_services_without_tenant()
    {
        // Workers, efectos y seeders no tienen tenant: el respaldo es la zona por defecto (Puerto Rico), nunca una excepción.
        var clock = new TenantClock(LocalDay.DefaultZone, () => Oct3At0130Utc);
        Assert.Equal(Oct2, clock.Today);
        Assert.Equal(Oct2, TenantClock.Default.DayOf(Oct3At0130Utc));
    }
}
