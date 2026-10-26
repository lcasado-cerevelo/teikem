using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-12 — duración de la sesión en MINUTOS (la pantalla la pide en horas con decimales: 24 = 1 día, 0.25 = 15 minutos). Mientras no se configure vale lo de siempre
/// (días × 1440); al guardar minutos los días quedan equivalentes (hacia arriba); fuera de 1 minuto a 365 días → 400.
/// </summary>
public sealed class SessionDurationTests
{
    [Theory]
    [InlineData(null, 43200)]    // sin configurar: 30 días por omisión
    [InlineData(15, 15)]         // lo configurado manda
    [InlineData(0, 43200)]       // 0 = no válido: vale el valor por omisión
    public void Effective_minutes_use_the_configured_value_or_the_default(int? configured, int expected)
        => Assert.Equal(expected, SessionRules.Minutes(configured));

    [Theory]
    [InlineData(1, true)]
    [InlineData(525600, true)]
    [InlineData(0, false)]
    [InlineData(525601, false)]
    [InlineData(-5, false)]
    public void Valid_range_is_one_minute_to_365_days(int minutes, bool ok) => Assert.Equal(ok, SessionRules.IsValid(minutes));

    private static TenantSettingsUpdateRequest Req(int? sessionMinutes = null, int? deviceMinutes = null)
        => new(null, null, null, null, null, null, null, null, null, null, null, SessionMinutes: sessionMinutes, DeviceSessionMinutes: deviceMinutes);

    [Fact]
    public async Task Saving_minutes_returns_them()
    {
        await using var f = await WmsFixture.CreateAsync(s => { s.AddSingleton<TenantZoneCache>(); s.AddSingleton<TenantService>(); });
        var svc = f.Get<TenantService>();

        var before = await svc.GetSettingsAsync(default);
        Assert.Equal(SessionRules.DefaultMinutes, before.SessionMinutes);   // sin configurar: 30 días

        var saved = await svc.UpdateSettingsAsync(Req(sessionMinutes: 90, deviceMinutes: 15), default);   // 1.5 h y 0.25 h
        Assert.Equal((90, 15), (saved.SessionMinutes, saved.DeviceSessionMinutes));
        var again = await svc.GetSettingsAsync(default);
        Assert.Equal((90, 15), (again.SessionMinutes, again.DeviceSessionMinutes));

        var days = await svc.UpdateSettingsAsync(Req(sessionMinutes: 2 * 1440 + 1), default);   // 49 h y 1 min
        Assert.Equal(2881, days.SessionMinutes);
        Assert.Equal(15, days.DeviceSessionMinutes);   // lo que no se manda no cambia
    }

    [Theory]
    [InlineData(0)]
    [InlineData(525601)]
    public async Task Minutes_outside_the_range_are_a_400_and_nothing_is_saved(int minutes)
    {
        await using var f = await WmsFixture.CreateAsync(s => { s.AddSingleton<TenantZoneCache>(); s.AddSingleton<TenantService>(); });
        var svc = f.Get<TenantService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Req(sessionMinutes: minutes), default));
        Assert.Equal((400, SessionRules.MinutesRange), (ex.StatusCode, ex.Message));
        Assert.Contains("sessionMinutes", ex.Errors!.Keys);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Req(deviceMinutes: minutes), default));
        Assert.Contains("deviceSessionMinutes", ex2.Errors!.Keys);
    }
}
