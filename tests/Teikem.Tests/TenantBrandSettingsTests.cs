using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Marca y feriados en los ajustes de la compañía (2026-10): PUT /tenant/settings valida el BrandingJson con BrandingRules (400 con
/// el mensaje exacto y sin guardar nada) y POST /tenant/holidays con una fecha ya registrada es un 409.
/// </summary>
public sealed class TenantBrandSettingsTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<TenantZoneCache>();
        s.AddSingleton<TenantService>();
    });

    private static TenantSettingsUpdateRequest Brand(string? json, string? name = null)
        => new(name, null, null, null, null, null, null, null, null, null, null, null, json);

    private const string Teal = "{\"preset\":\"turquesa\",\"useCustom\":false,\"custom\":{\"flow\":\"#1F6FE5\",\"money\":\"#FF6A1A\",\"neutral\":\"#2B3A5C\"}}";

    [Fact]
    public async Task A_valid_brand_is_saved_returned_and_can_be_cleared()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var saved = await svc.UpdateSettingsAsync(Brand(Teal), default);
        Assert.Equal(Teal, saved.BrandingJson);
        Assert.Equal(Teal, (await svc.GetSettingsAsync(default)).BrandingJson);
        Assert.Null((await svc.UpdateSettingsAsync(Brand(""), default)).BrandingJson);
        // sin el campo no cambia nada
        await svc.UpdateSettingsAsync(Brand(Teal), default);
        Assert.Equal(Teal, (await svc.UpdateSettingsAsync(Brand(null, name: "Otro"), default)).BrandingJson);
    }

    [Theory]
    [InlineData("{", "La marca no es un JSON válido.")]
    [InlineData("{\"logos\":[]}", "La marca trae un campo desconocido: 'logos'.")]
    [InlineData("{\"warn\":\"#FFA500\"}", "Los colores de estado (ok, warn, danger, info) no se pueden personalizar: 'warn'.")]
    [InlineData("{\"useCustom\":true,\"custom\":{\"flow\":\"azul\"}}", "El color 'custom.flow' no es hexadecimal (use #RGB o #RRGGBB).")]
    [InlineData("{\"preset\":\"neon\"}", "El tema predefinido 'neon' no existe.")]
    [InlineData("{\"useCustom\":true,\"custom\":{\"flow\":\"#000000\"}}", "El contraste del color de operación en modo oscuro es 1.34:1; el mínimo es 4.5:1.")]
    [InlineData("{\"useCustom\":true,\"custom\":{\"flow\":\"#1F6FE5\",\"money\":\"#2060E0\"}}", "Los colores de operación y de dinero son demasiado parecidos: 4° de separación y el mínimo es 40°.")]
    public async Task An_invalid_brand_is_a_400_with_the_exact_message_on_brandingJson_and_nothing_is_saved(string json, string message)
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        await svc.UpdateSettingsAsync(Brand(Teal), default);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Brand(json, name: "No debe guardarse"), default));
        Assert.Equal((400, message), (ex.StatusCode, ex.Message));
        Assert.Equal(new[] { message }, ex.Errors!["brandingJson"]);
        f.Db.ChangeTracker.Clear();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        Assert.Equal((Teal, "Tenant de prueba"), (t.BrandingJson, t.Name));
    }

    [Fact]
    public async Task A_json_over_4096_characters_is_a_400()
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<TenantService>().UpdateSettingsAsync(Brand(Teal + new string(' ', 5000)), default));
        Assert.Equal("La marca es demasiado grande (máximo 4096 caracteres); los logos se suben aparte.", ex.Message);
    }

    [Fact]
    public async Task A_repeated_holiday_date_is_a_409_and_does_not_overwrite_the_existing_one()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var day = new DateOnly(2026, 12, 25);
        var first = await svc.AddHolidayAsync(new TenantHolidayRequest(day, "Navidad", true), default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.AddHolidayAsync(new TenantHolidayRequest(day, "Otra cosa", false), default));
        Assert.Equal((409, "Ya hay un feriado en esa fecha."), (ex.StatusCode, ex.Message));
        Assert.Equal(new[] { ex.Message }, ex.Errors!["date"]);

        var list = await svc.GetHolidaysAsync(null, default);
        var only = Assert.Single(list);
        Assert.Equal((first.Id, "Navidad", true), (only.Id, only.Name, only.IsRecurring));
    }

    [Fact]
    public async Task A_removed_holiday_can_be_added_again_and_another_company_may_use_the_same_date()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var day = new DateOnly(2026, 7, 4);
        var first = await svc.AddHolidayAsync(new TenantHolidayRequest(day, "Independencia", true), default);
        await svc.RemoveHolidayAsync(first.Id, default);
        var again = await svc.AddHolidayAsync(new TenantHolidayRequest(day, "Día de la Independencia", false), default);
        Assert.Equal((first.Id, "Día de la Independencia", false), (again.Id, again.Name, again.IsRecurring));   // se reactiva la misma fila

        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            var other = await svc.AddHolidayAsync(new TenantHolidayRequest(day, "Propio", false), default);
            Assert.NotEqual(first.Id, other.Id);
        }
        await Assert.ThrowsAsync<ConflictException>(() => svc.AddHolidayAsync(new TenantHolidayRequest(day, "Otra vez", false), default));
    }
}
