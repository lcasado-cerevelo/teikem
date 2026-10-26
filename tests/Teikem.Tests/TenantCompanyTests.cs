using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-26 — datos de la compañía: teléfono, domicilio y dirección postal (filas de Location), nombre solo para plataforma, alta con módulos base y activar/desactivar.</summary>
public sealed class TenantCompanyTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => { s.AddSingleton<TenantZoneCache>(); s.AddSingleton<TenantService>(); });

    private static TenantSettingsUpdateRequest Put(string? name = null, string? phone = null, TenantAddressInput? address = null, TenantAddressInput? postal = null, bool? postalSame = null)
        => new(name, null, null, null, null, null, null, null, null, null, null, PhoneNumber: phone, Address: address, PostalAddress: postal, PostalSameAsAddress: postalSame);

    private static readonly TenantAddressInput Home = new("Calle Sol 123", "Oficina 4", "San Juan", "PR", "00901", null);
    private static readonly TenantAddressInput Mail = new("PO Box 99", null, "San Juan", "PR", "00902", "PR");

    private static ProvisioningService Provisioning(WmsFixture f)
        => new(f.Db, null!, f.Tenant, f.Lookups, null!, null!, null!, new ReceivingNullSecurityEventWriter(), NullLogger<ProvisioningService>.Instance);

    [Theory]
    [InlineData("787-555-0100", true)]
    [InlineData("(787) 555 0100", true)]
    [InlineData("+1 787 555 0100 ext 22", true)]
    [InlineData("123456", false)]                 // menos de 7 dígitos
    [InlineData("1234567890123456", false)]       // más de 15
    [InlineData("787-555-ABCD", false)]           // letras
    [InlineData("", false)]
    public void Phone_needs_between_7_and_15_digits_and_only_phone_characters(string raw, bool ok)
    {
        var (phone, error) = TenantContactRules.NormalizePhone(raw, required: true);
        Assert.Equal(ok, error is null);
        if (ok) Assert.Equal(raw.Trim(), phone);
    }

    [Fact]
    public void An_empty_phone_is_allowed_when_editing_and_asked_for_when_creating()
    {
        Assert.Equal((null, null), TenantContactRules.NormalizePhone("  ", required: false));
        Assert.Equal(TenantContactRules.PhoneRequiredMessage, TenantContactRules.NormalizePhone(null, required: true).Error);
        Assert.Equal("El teléfono de la compañía es obligatorio.", TenantContactRules.PhoneRequiredMessage);
    }

    [Fact]
    public void An_address_needs_line_1_and_city()
    {
        Assert.Equal(("line1", "La dirección (línea 1) es obligatoria."), TenantContactRules.ValidateAddress(" ", null, "San Juan", null, null));
        Assert.Equal(("city", "La ciudad es obligatoria."), TenantContactRules.ValidateAddress("Calle 1", null, "", null, null));
        Assert.Null(TenantContactRules.ValidateAddress("Calle 1", null, "San Juan", null, null));
        Assert.Equal("postalCode", TenantContactRules.ValidateAddress("Calle 1", null, "San Juan", null, new string('9', 21))!.Value.Field);
    }

    [Fact]
    public async Task Saving_the_phone_and_the_address_creates_a_location_and_a_second_save_corrects_the_same_one()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();

        var saved = await svc.UpdateSettingsAsync(Put(phone: " 787-555-0100 ", address: Home), default);
        Assert.Equal("787-555-0100", saved.PhoneNumber);
        Assert.Equal(("Calle Sol 123", "Oficina 4", "San Juan", "PR", "00901", "PR"), (saved.Address!.Line1, saved.Address.Line2, saved.Address.City, saved.Address.State, saved.Address.PostalCode, saved.Address.Country));
        Assert.Null(saved.PostalAddress);   // sin postal propia: vale el domicilio

        var tenant = await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        var loc = await f.Db.Locations.IgnoreQueryFilters().SingleAsync(l => l.LocationId == tenant.CorporateLocationId);
        Assert.Null(loc.ClientId);                                                   // la localización es de la compañía, no de un cliente
        Assert.Equal(f.LookupId(LookupDomains.LocationType, LocationTypes.Company), loc.LocationTypeLookupId);
        Assert.Equal(tenant.Name, loc.Name);

        var again = await svc.UpdateSettingsAsync(Put(address: Home with { Line1 = "Calle Luna 9" }), default);
        Assert.Equal("Calle Luna 9", again.Address!.Line1);
        Assert.Equal(1, await f.Db.Locations.IgnoreQueryFilters().CountAsync(l => l.LocationTypeLookupId == loc.LocationTypeLookupId && l.TenantId == WmsFixture.TenantId));
        Assert.Equal("787-555-0100", again.PhoneNumber);   // lo que no se manda no cambia
    }

    [Fact]
    public async Task The_postal_address_is_its_own_location_and_same_as_address_takes_it_away_without_deleting()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        await svc.UpdateSettingsAsync(Put(address: Home), default);

        var withPostal = await svc.UpdateSettingsAsync(Put(postal: Mail), default);
        Assert.Equal("PO Box 99", withPostal.PostalAddress!.Line1);
        var billingId = (await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).BillingLocationId!.Value;
        Assert.Equal(f.LookupId(LookupDomains.LocationType, LocationTypes.CompanyPostal), (await f.Db.Locations.IgnoreQueryFilters().SingleAsync(l => l.LocationId == billingId)).LocationTypeLookupId);

        var same = await svc.UpdateSettingsAsync(Put(postalSame: true), default);
        Assert.Null(same.PostalAddress);
        Assert.Null((await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).BillingLocationId);
        Assert.False((await f.Db.Locations.IgnoreQueryFilters().SingleAsync(l => l.LocationId == billingId)).IsActive);   // baja lógica, nunca DELETE
        Assert.NotNull(same.Address);
    }

    [Theory]
    [InlineData("123", "El teléfono debe tener entre 7 y 15 dígitos (puede llevar espacios, guiones, paréntesis, «+» y «ext»).")]
    public async Task A_bad_phone_is_a_400_and_nothing_is_saved(string phone, string message)
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<TenantService>().UpdateSettingsAsync(Put(phone: phone), default));
        Assert.Equal(message, ex.Message);
        Assert.Null((await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).PhoneNumber);
    }

    [Fact]
    public async Task A_bad_address_names_its_field_and_nothing_is_created()
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<TenantService>().UpdateSettingsAsync(Put(address: Home with { City = " " }), default));
        Assert.Equal("La ciudad es obligatoria.", ex.Message);
        Assert.Contains("address.city", ex.Errors.Keys);
        Assert.Null((await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).CorporateLocationId);
    }

    [Fact]
    public async Task Only_the_platform_administrator_renames_the_company_and_the_name_cannot_repeat()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<TenantService>();
        var current = (await svc.GetSettingsAsync(default)).Name;

        f.Tenant.IsPlatformAdmin = false;
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => svc.UpdateSettingsAsync(Put(name: "Otro nombre"), default));
        Assert.Equal("Solo el administrador de la plataforma cambia el nombre de la compañía.", ex.Message);
        await svc.UpdateSettingsAsync(Put(name: current), default);   // mandar el mismo nombre no es un cambio: no estorba al guardar el resto

        f.Tenant.IsPlatformAdmin = true;
        var other = await f.Db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.TenantId != WmsFixture.TenantId).Select(t => t.Name).FirstAsync();
        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.UpdateSettingsAsync(Put(name: other), default));
        Assert.Equal("Ya existe una compañía con ese nombre.", dup.Message);

        await svc.UpdateSettingsAsync(Put(address: Home), default);
        Assert.Equal("Nombre Nuevo", (await svc.UpdateSettingsAsync(Put(name: " Nombre Nuevo "), default)).Name);
        var tenant = await f.Db.Tenants.AsNoTracking().SingleAsync(t => t.TenantId == WmsFixture.TenantId);
        Assert.Equal("Nombre Nuevo", (await f.Db.Locations.IgnoreQueryFilters().SingleAsync(l => l.LocationId == tenant.CorporateLocationId)).Name);   // la dirección lleva el nombre nuevo
    }

    [Fact]
    public async Task Creating_a_company_asks_for_phone_and_address_before_touching_anything()
    {
        await using var f = await FixtureAsync();
        var p = Provisioning(f);
        var before = await f.Db.Tenants.IgnoreQueryFilters().CountAsync();

        var noPhone = await Assert.ThrowsAsync<ValidationException>(() => p.ProvisionAsync(new TenantProvisionRequest("Nueva SA", null, null, "es", null, Address: Home), default));
        Assert.Equal("El teléfono de la compañía es obligatorio.", noPhone.Message);
        var noAddress = await Assert.ThrowsAsync<ValidationException>(() => p.ProvisionAsync(new TenantProvisionRequest("Nueva SA", null, null, "es", null, PhoneNumber: "787-555-0100"), default));
        Assert.Equal("La dirección (línea 1) es obligatoria.", noAddress.Message);
        var badPostal = await Assert.ThrowsAsync<ValidationException>(() => p.ProvisionAsync(new TenantProvisionRequest("Nueva SA", null, null, "es", null, PhoneNumber: "787-555-0100", Address: Home, PostalAddress: Mail with { City = "" }), default));
        Assert.Contains("postalAddress.city", badPostal.Errors.Keys);
        var noName = await Assert.ThrowsAsync<ValidationException>(() => p.ProvisionAsync(new TenantProvisionRequest(" ", null, null, "es", null, PhoneNumber: "787-555-0100", Address: Home), default));
        Assert.Equal("El nombre es obligatorio.", noName.Message);
        var badDomain = await Assert.ThrowsAsync<ValidationException>(() => p.ProvisionAsync(new TenantProvisionRequest("Nueva SA", null, null, "es", null, PhoneNumber: "787-555-0100", Address: Home, CustomDomain: "no es un dominio"), default));
        Assert.Contains("customDomain", badDomain.Errors.Keys);

        Assert.Equal(before, await f.Db.Tenants.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void A_new_company_starts_with_the_base_modules_only_not_transport_warehouse_or_billing()
    {
        Assert.Equal(new[] { ModuleKeys.System, ModuleKeys.Catalog, ModuleKeys.Analytics, ModuleKeys.CustomFields }.OrderBy(x => x), ProvisioningService.BaseModules.OrderBy(x => x));
        Assert.DoesNotContain(ModuleKeys.LtlGround, ProvisioningService.BaseModules);
        Assert.DoesNotContain(ModuleKeys.Billing, ProvisioningService.BaseModules);
    }

    [Fact]
    public async Task Deactivating_closes_everyone_elses_sessions_but_not_the_platform_users_and_activating_brings_it_back()
    {
        await using var f = await FixtureAsync();
        var p = Provisioning(f);
        var target = await f.Db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.TenantId == WmsFixture.OtherTenantId);
        f.Tenant.UserId = 1;
        foreach (var uid in new[] { 1, 2, 3 })
            f.Db.RefreshTokens.Add(new RefreshToken { UserId = uid, TenantId = target.TenantId, TokenHash = "h" + uid, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await f.Db.SaveChangesAsync();

        var off = await p.SetActiveAsync(target.PublicId, false, default);
        Assert.False(off.IsActive);
        var tokens = await f.Db.RefreshTokens.IgnoreQueryFilters().Where(r => r.TenantId == target.TenantId).ToListAsync();
        Assert.Null(tokens.Single(r => r.UserId == 1).RevokedAtUtc);          // la persona de la plataforma que lo hizo sigue dentro para poder reactivarla
        Assert.NotNull(tokens.Single(r => r.UserId == 2).RevokedAtUtc);
        Assert.NotNull(tokens.Single(r => r.UserId == 3).RevokedAtUtc);
        Assert.False(await TenantStatusGuard.IsActiveAsync(f.Db, target.TenantId, default));   // la API ya no deja pasar a nadie más de esa compañía

        var on = await p.SetActiveAsync(target.PublicId, true, default);
        Assert.True(on.IsActive);
        Assert.True(await TenantStatusGuard.IsActiveAsync(f.Db, target.TenantId, default));    // activar la deja pasar al instante (la memoria se olvida)
        await Assert.ThrowsAsync<NotFoundException>(() => p.SetActiveAsync(Guid.NewGuid(), true, default));
    }
}
