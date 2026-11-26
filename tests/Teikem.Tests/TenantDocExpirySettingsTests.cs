using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — ajustes de los avisos de vencimiento en la compañía (GET/PUT /tenant/settings, admin.tenant): umbrales (30, 15 y 5 por omisión;
/// normalizados de mayor a menor; 400 en docExpiryThresholdDays con el mensaje exacto), «Avisar a diario después de vencido» y «Avisar al cliente» (apagados); null = sin
/// cambio; lo audita la compañía.
/// </summary>
public sealed class TenantDocExpirySettingsTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<TenantService>());

    private static TenantSettingsUpdateRequest Put() => new(null, null, null, null, null, null, null, null, null, null, null);

    private static async Task<Tenant> StoredAsync(WmsFixture f)
    {
        f.Db.ChangeTracker.Clear();
        return await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
    }

    [Fact]
    public async Task Defaults_are_30_15_5_with_both_switches_off()
    {
        await using var f = await FixtureAsync();
        var s = await f.Get<TenantService>().GetSettingsAsync(default);
        Assert.Equal(new[] { 30, 15, 5 }, s.DocExpiryThresholdDays);
        Assert.False(s.DocExpiryDailyAfterExpiry);
        Assert.False(s.DocExpiryNotifyClient);
    }

    [Fact]
    public async Task Put_normalizes_the_thresholds_and_saves_the_switches_and_null_changes_nothing()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var saved = await svc.UpdateSettingsAsync(Put() with { DocExpiryThresholdDays = new[] { 5, 30, 15, 0, -1 }, DocExpiryDailyAfterExpiry = true, DocExpiryNotifyClient = true }, default);
        Assert.Equal(new[] { 30, 15, 5, 0, -1 }, saved.DocExpiryThresholdDays);
        Assert.True(saved.DocExpiryDailyAfterExpiry && saved.DocExpiryNotifyClient);
        var row = await StoredAsync(f);
        Assert.Equal(("30,15,5,0,-1", true, true), (row.DocExpiryThresholdDays, row.DocExpiryDailyAfterExpiry, row.DocExpiryNotifyClient));

        var untouched = await svc.UpdateSettingsAsync(Put() with { ProductPhotosEnabled = false }, default);   // otro ajuste: los del lote no cambian
        Assert.Equal(new[] { 30, 15, 5, 0, -1 }, untouched.DocExpiryThresholdDays);
        Assert.True(untouched.DocExpiryDailyAfterExpiry && untouched.DocExpiryNotifyClient);
        var off = await svc.UpdateSettingsAsync(Put() with { DocExpiryNotifyClient = false }, default);
        Assert.Equal((true, false), (off.DocExpiryDailyAfterExpiry, off.DocExpiryNotifyClient));
        var none = await svc.UpdateSettingsAsync(Put() with { DocExpiryThresholdDays = Array.Empty<int>() }, default);   // sin umbrales: no se avisa
        Assert.Empty(none.DocExpiryThresholdDays);
        Assert.Equal(string.Empty, (await StoredAsync(f)).DocExpiryThresholdDays);
    }

    [Fact]
    public async Task Invalid_thresholds_are_a_400_on_their_field_and_nothing_is_saved()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var cases = new (int[] Values, string Message)[]
        {
            (new[] { 500 }, ExpiryNoticeRules.ThresholdOutOfRange),
            (new[] { -366, 5 }, ExpiryNoticeRules.ThresholdOutOfRange),
            (new[] { 15, 15 }, ExpiryNoticeRules.ThresholdRepeated),
            (Enumerable.Range(1, 11).ToArray(), ExpiryNoticeRules.TooManyThresholds),
        };
        foreach (var (values, message) in cases)
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateSettingsAsync(Put() with { DocExpiryThresholdDays = values }, default));
            Assert.Equal(message, ex.Message);
            Assert.Equal(new[] { message }, ex.Errors!["docExpiryThresholdDays"]);
            Assert.Equal(DocumentExpiryCodes.DefaultThresholds, (await StoredAsync(f)).DocExpiryThresholdDays);
        }
    }

    [Fact]
    public void The_settings_are_audited_by_the_company_and_saved_with_admin_tenant()
    {
        Assert.Equal(EntityTypes.Tenant, typeof(Tenant).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var p in new[] { nameof(Tenant.DocExpiryThresholdDays), nameof(Tenant.DocExpiryDailyAfterExpiry), nameof(Tenant.DocExpiryNotifyClient) })
            Assert.Null(typeof(Tenant).GetProperty(p)!.GetCustomAttribute<NotAuditedAttribute>());
        var put = typeof(Teikem.Api.Controllers.TenantController).GetMethods().Single(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpPutAttribute>().Any(a => a.Template == "settings"));
        Assert.Contains(put.GetCustomAttributes<Teikem.Api.Auth.RequirePermissionAttribute>(), a => a.Policy == Teikem.Api.Auth.RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant);
    }
}
