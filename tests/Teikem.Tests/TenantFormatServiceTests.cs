using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Región y formatos de la compañía (2026-10) — servicio y reloj: GET/PUT /tenant/settings con los campos nuevos (parcial: null =
/// sin cambio), la regla de región, el 400 con el mensaje exacto sin guardar nada, las opciones de la pantalla, los permisos de
/// los endpoints, la auditoría de la entidad, y ContextTenantClock: la zona sale del tenant del contexto (Puerto Rico vs Honolulú
/// a la 1:30 UTC del 3 de octubre: 2 de octubre en ambas), con caché por compañía que se invalida al cambiar la zona y la zona
/// por defecto sin tenant.
/// </summary>
public sealed class TenantFormatServiceTests
{
    private static readonly DateTime Oct3At0130Utc = new(2026, 10, 3, 1, 30, 0, DateTimeKind.Utc);

    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<TenantZoneCache>();
        s.AddSingleton<TenantService>();
    });

    private static TenantSettingsUpdateRequest Put(string? regionCode = null, string? timeZoneId = null, string? dateOrder = null, byte? timeFormat = null,
        string? thousandsSeparator = null, string? decimalSeparator = null, string? name = null)
        => new(name, null, null, null, null, null, null, null, null, null, null, null, null,
            RegionCode: regionCode, TimeZoneId: timeZoneId, DateOrder: dateOrder, TimeFormat: timeFormat,
            ThousandsSeparator: thousandsSeparator, DecimalSeparator: decimalSeparator);

    private static ContextTenantClock Clock(WmsFixture f, DateTime? now = null)
        => new(f.Tenant, f.Db, f.Get<TenantZoneCache>(), now is DateTime n ? () => n : null);

    private static async Task SetZoneInDbAsync(WmsFixture f, int tenantId, string zone)
    {
        var t = await f.Db.Tenants.AsTracking().SingleAsync(x => x.TenantId == tenantId);
        t.TimeZoneId = zone;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------ servicio

    [Fact]
    public async Task Settings_expose_the_puerto_rico_defaults()
    {
        await using var f = await FixtureAsync();
        var s = await f.Get<TenantService>().GetSettingsAsync(default);
        Assert.Equal(("PR", "America/Puerto_Rico", "USD", "$", "B", (byte)2), (s.RegionCode, s.TimeZoneId, s.CurrencyCode, s.CurrencySymbol, s.CurrencySymbolPosition, s.CurrencyDecimals));
        Assert.Equal(("MDY", "/", (byte)12, (byte)0, ",", "."), (s.DateOrder, s.DateSeparator, s.TimeFormat, s.WeekStartDay, s.ThousandsSeparator, s.DecimalSeparator));
        Assert.Equal(("+1", "(###) ###-####"), (s.PhoneCountryCode, s.PhoneMask));
        Assert.False(s.IsRegionCustomized);
    }

    [Fact]
    public async Task Region_without_fields_loads_its_defaults_and_with_fields_the_explicit_ones_win()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();

        var us = await svc.UpdateSettingsAsync(Put(regionCode: "US"), default);
        Assert.Equal(("US", "America/New_York", "MDY", (byte)12, false), (us.RegionCode, us.TimeZoneId, us.DateOrder, us.TimeFormat, us.IsRegionCustomized));

        var single = await svc.UpdateSettingsAsync(Put(dateOrder: "DMY"), default);
        Assert.Equal(("US", "America/New_York", "DMY", true), (single.RegionCode, single.TimeZoneId, single.DateOrder, single.IsRegionCustomized));

        var pr = await svc.UpdateSettingsAsync(Put(regionCode: "PR", timeFormat: 24), default);
        Assert.Equal(("PR", "America/Puerto_Rico", "MDY", (byte)24, true), (pr.RegionCode, pr.TimeZoneId, pr.DateOrder, pr.TimeFormat, pr.IsRegionCustomized));

        // Lo guardado es lo devuelto
        f.Db.ChangeTracker.Clear();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        Assert.Equal(TenantFormatRules.Defaults("PR") with { TimeFormat = 24 }, TenantFormatRules.Read(t));
    }

    [Fact]
    public async Task Same_separators_are_a_400_with_the_exact_message_and_nothing_is_saved()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put(thousandsSeparator: ".", name: "Otro nombre"), default));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("El separador de miles y el decimal no pueden ser el mismo", ex.Message);
        Assert.Equal(new[] { ex.Message }, ex.Errors!["decimalSeparator"]);

        f.Db.ChangeTracker.Clear();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        Assert.Equal((",", ".", "Tenant de prueba"), (t.ThousandsSeparator, t.DecimalSeparator, t.Name));
    }

    [Fact]
    public async Task An_unknown_time_zone_is_a_400()
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<TenantService>().UpdateSettingsAsync(Put(timeZoneId: "Mars/Olympus_Mons"), default));
        Assert.Equal(TenantFormatRules.UnknownTimeZone("Mars/Olympus_Mons"), ex.Message);
        Assert.True(ex.Errors!.ContainsKey("timeZoneId"));
    }

    [Fact]
    public async Task Other_settings_without_format_fields_do_not_touch_the_formats()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        await svc.UpdateSettingsAsync(Put(regionCode: "US", dateOrder: "YMD"), default);
        var s = await svc.UpdateSettingsAsync(Put(name: "Nuevo nombre"), default);
        Assert.Equal(("Nuevo nombre", "US", "YMD"), (s.Name, s.RegionCode, s.DateOrder));
    }

    [Fact]
    public void Format_options_list_both_regions_and_the_allowed_values()
    {
        var o = new TenantService(null!, new TenantContext(), null!).GetFormatOptions();
        Assert.Equal(new[] { "PR", "US" }, o.Regions.Select(r => r.RegionCode));
        Assert.Equal("America/New_York", o.Regions.Single(r => r.RegionCode == "US").TimeZoneId);
        Assert.Equal(new[] { 0, 2, 3 }, o.CurrencyDecimals);
        Assert.Equal(new[] { "MDY", "DMY", "YMD" }, o.DateOrders);
        Assert.Equal(new[] { ",", ".", " " }, o.ThousandsSeparators);
        Assert.Equal(new[] { ".", "," }, o.DecimalSeparators);
        Assert.Equal(new[] { 12, 24 }, o.TimeFormats);
        Assert.Equal(new[] { 0, 1 }, o.WeekStartDays);
        Assert.Equal(new[] { "B", "A" }, o.CurrencySymbolPositions);
        Assert.Equal(new[] { "/", "-", "." }, o.DateSeparators);
    }

    [Fact]
    public void Endpoints_keep_their_permissions_and_the_tenant_is_audited()
    {
        var t = typeof(TenantController);
        var put = t.GetMethod(nameof(TenantController.Update))!;
        Assert.Equal("settings", put.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant, put.GetCustomAttribute<RequirePermissionAttribute>()!.Policy);
        var options = t.GetMethod(nameof(TenantController.FormatOptions))!;
        Assert.Equal("format-options", options.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(options.GetCustomAttributes<RequirePermissionAttribute>());

        // Los cambios quedan en la bitácora: la entidad lleva [AuditEntity] y ninguna columna de formato es [NotAudited].
        Assert.Equal(EntityTypes.Tenant, typeof(Tenant).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var p in typeof(TenantFormat).GetProperties())
            Assert.Null(typeof(Tenant).GetProperty(p.Name)!.GetCustomAttribute<NotAuditedAttribute>());
    }

    [Fact]
    public async Task Provisioning_rejects_an_invalid_format_before_creating_the_tenant()
    {
        await using var f = await FixtureAsync();
        var provisioning = new ProvisioningService(f.Db, null!, f.Tenant, f.Lookups, null!, null!, null!, null!, NullLogger<ProvisioningService>.Instance);
        var before = await f.Db.Tenants.IgnoreQueryFilters().CountAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => provisioning.ProvisionAsync(
            new TenantProvisionRequest("Compañía US", null, null, "en", null, "admin@example.com", "Admin", null, RegionCode: "US", ThousandsSeparator: "."), default));
        Assert.Equal(TenantFormatRules.SameSeparators, ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => provisioning.ProvisionAsync(
            new TenantProvisionRequest("Compañía MX", null, null, "es", null, "admin@example.com", "Admin", null, RegionCode: "MX"), default));
        Assert.Equal(before, await f.Db.Tenants.IgnoreQueryFilters().CountAsync());
    }

    // ------------------------------------------------------------------ reloj de la compañía

    [Fact]
    public async Task Each_tenant_counts_today_in_its_own_zone()
    {
        await using var f = await FixtureAsync();
        await SetZoneInDbAsync(f, WmsFixture.OtherTenantId, "Pacific/Honolulu");
        var clock = Clock(f, Oct3At0130Utc);

        // 1:30 UTC del 3 de octubre: 21:30 del 2 en Puerto Rico (UTC−4) y 15:30 del 2 en Honolulú (UTC−10).
        Assert.Equal(new DateOnly(2026, 10, 2), clock.Today);
        Assert.Equal(new DateTime(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc), clock.StartOfDayUtc(new DateOnly(2026, 10, 2)));
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Equal(new DateOnly(2026, 10, 2), clock.Today);
            Assert.Equal(TimeSpan.FromHours(-10), clock.Zone.GetUtcOffset(Oct3At0130Utc));
            Assert.Equal(new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc), clock.StartOfDayUtc(new DateOnly(2026, 10, 2)));
            Assert.Equal(new DateOnly(2026, 10, 2), clock.DayOf(new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc)));
        }
        // 5:00 UTC del 3: ya es el 3 en Puerto Rico, todavía el 2 en Honolulú.
        Assert.Equal(new DateOnly(2026, 10, 3), clock.DayOf(new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("America/Puerto_Rico", WarehousePulseService.ZoneName(clock.Zone));
        using (f.AsTenant(WmsFixture.OtherTenantId)) Assert.Equal("Pacific/Honolulu", WarehousePulseService.ZoneName(clock.Zone));
    }

    [Fact]
    public async Task Without_tenant_or_with_an_unknown_zone_the_default_zone_is_used()
    {
        await using var f = await FixtureAsync();
        var clock = Clock(f);
        f.Tenant.TenantId = null;
        Assert.Same(LocalDay.DefaultZone, clock.Zone);
        f.Tenant.TenantId = 999;   // compañía sin fila: zona por defecto
        Assert.Equal(LocalDay.DefaultZone.BaseUtcOffset, clock.Zone.BaseUtcOffset);
        Assert.InRange(clock.UtcNow, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task The_zone_is_cached_per_tenant_and_a_change_invalidates_it()
    {
        await using var f = await FixtureAsync();
        var zones = f.Get<TenantZoneCache>();
        var clock = Clock(f, Oct3At0130Utc);
        Assert.Equal("America/Puerto_Rico", WarehousePulseService.ZoneName(clock.Zone));

        // Un cambio por fuera del servicio no se ve hasta invalidar (la caché evita leer la base en cada llamada).
        await SetZoneInDbAsync(f, WmsFixture.TenantId, "Pacific/Honolulu");
        Assert.Equal("America/Puerto_Rico", WarehousePulseService.ZoneName(clock.Zone));
        zones.Invalidate(WmsFixture.TenantId);
        Assert.Equal("Pacific/Honolulu", WarehousePulseService.ZoneName(clock.Zone));

        // Guardar los ajustes invalida la caché: el reloj usa la zona nueva de inmediato.
        await f.Get<TenantService>().UpdateSettingsAsync(Put(regionCode: "US"), default);
        Assert.Equal("America/New_York", WarehousePulseService.ZoneName(clock.Zone));
        Assert.Equal(new DateOnly(2026, 10, 2), clock.Today);   // 21:30 del 2 en Nueva York (UTC−4 en octubre)
        await f.Get<TenantService>().UpdateSettingsAsync(Put(timeZoneId: "Pacific/Honolulu"), default);
        Assert.Equal(TimeSpan.FromHours(-10), clock.Zone.GetUtcOffset(Oct3At0130Utc));
    }

    [Fact]
    public async Task The_cache_lives_in_memory_cache_with_a_lifetime()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var zones = new TenantZoneCache(cache);
        var loads = 0;
        string? Load(int _) { loads++; return "Pacific/Honolulu"; }
        Assert.Equal(TimeSpan.FromHours(-10), zones.GetOrLoad(7, Load).BaseUtcOffset);
        zones.GetOrLoad(7, Load);
        Assert.Equal(1, loads);
        zones.Invalidate(7);
        zones.GetOrLoad(7, Load);
        Assert.Equal(2, loads);
        Assert.Equal(TimeSpan.FromMinutes(10), TenantZoneCache.Lifetime);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Work_days_end_on_the_local_today_of_the_tenant()
    {
        await using var f = await FixtureAsync();
        await SetZoneInDbAsync(f, WmsFixture.TenantId, "Pacific/Honolulu");
        // Lunes 5 de octubre de 2026 a las 5:00 UTC = domingo 4 a las 19:00 en Honolulú: el último día hábil (L-V) es el viernes 2;
        // con el día UTC habría sido el lunes 5.
        var svc = new TenantService(f.Db, f.Tenant, f.Lookups, Clock(f, new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc)), f.Get<TenantZoneCache>());
        var days = await svc.LastWorkDaysAsync(1, default);
        Assert.Equal(new[] { new DateOnly(2026, 10, 2) }, days);
    }
}
