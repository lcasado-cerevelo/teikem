using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F8a — MFA por compañía (Tenant.MfaRequired, ya existía) y por membresía (UserTenant.MfaRequired, nuevo): un
/// administrador puede exigir MFA a una persona en particular sin tener que exigirlo a toda la compañía, aparte de
/// poder resetearle el TOTP si perdió el dispositivo (AuthService.AdminResetMfaAsync). Solo se prueban aquí los
/// caminos que bloquean el login (mfa_required): el de éxito pasa por IssueAsync, que usa ExecuteUpdateAsync para
/// LastLoginUtc (ver su comentario) — el proveedor InMemory de este fixture no lo traduce, así que ese camino se
/// verificó a mano contra la API real (curl), no aquí.
/// </summary>
public class AuthMfaTests
{
    private const string Password = "Clave-Segura1";

    private static async Task<WmsFixture> FixtureAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            // Primer ingreso (2026-09-30) apagado aquí: estas pruebas son del login de siempre (OnboardingTests lo prueba encendido).
            s.AddSingleton<Microsoft.Extensions.Options.IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<Teikem.Infrastructure.Abstractions.ITransactionalEmailSender, NoEmailSender>();
            s.AddSingleton<AuthService>();
            s.AddSingleton<UserAdminService>();
        });
        // Estatus de membresía activa, que SeedCatalogsAsync (compartido entre pruebas WMS) no siembra.
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        // Catálogo que ConfirmedTotpAsync consulta (fuera de SeedCatalogsAsync: se registra a mano, como en AuthLockoutTests).
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        var me = new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", NormalizedEmail = "YO@T.LOCAL", FullName = "Yo Mismo", SecurityStamp = "s1", IsActive = true };
        me.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(me, Password);
        f.Db.Users.Add(me);
        await f.Db.SaveChangesAsync();
        var activeStatusId = await f.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => s.StatusCodeId).SingleAsync();
        f.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = WmsFixture.TenantId, StatusCodeId = activeStatusId, IsDefault = true });
        await f.Db.SaveChangesAsync();
        // TripTestLookups (ILookupCache) es un caché aparte de _lookupIds de WmsFixture: hay que recargarlo para que
        // ConfirmedTotpAsync (que usa ILookupCache, no WmsFixture.LookupId) encuentre el MfaFactorType recién agregado.
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static int MfaFactorTypeTotpId(WmsFixture f) => f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().Single(l => l.Entity == LookupDomains.MfaFactorType && l.InternalCode == MfaFactorTypes.Totp).LookupCodeId;

    private static Domain.Tenancy.Tenant Tenant(WmsFixture f)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.Tenants.IgnoreQueryFilters().AsNoTracking().Single(t => t.TenantId == WmsFixture.TenantId);
    }

    [Fact]
    public async Task Login_is_blocked_with_mfa_required_when_the_tenant_requires_it()
    {
        await using var f = await FixtureAsync();
        // La entidad Tenant.MfaRequired es true por default (Lote F8a): no hace falta fijarlo aquí, ya viene así.
        Assert.True(Tenant(f).MfaRequired);
        var auth = f.Get<AuthService>();

        var result = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, WmsFixture.TenantId, null), default);

        Assert.Equal("mfa_required", result.Status);
        Assert.Null(result.Tokens);
        Assert.True(result.MfaEnrollmentRequired);
        Assert.NotNull(result.MfaChallengeToken);
    }

    [Fact]
    public async Task Login_is_not_blocked_when_neither_the_tenant_nor_the_membership_require_mfa()
    {
        await using var f = await FixtureAsync();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.MfaRequired = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var auth = f.Get<AuthService>();

        // IssueAsync (más allá del portón de MFA) usa ExecuteUpdateAsync, que el proveedor InMemory no traduce; lo que
        // importa aquí es que el portón de MFA NO se dispare (nunca llega a lanzar 'mfa_required').
        var ex = await Record.ExceptionAsync(() => auth.LoginAsync(new LoginRequest("yo@t.local", Password, WmsFixture.TenantId, null), default));
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task Login_is_blocked_when_only_this_membership_requires_mfa_even_if_the_tenant_does_not()
    {
        await using var f = await FixtureAsync();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.MfaRequired = false;
        var m = await f.Db.UserTenants.SingleAsync(x => x.UserId == 1 && x.TenantId == WmsFixture.TenantId);
        m.MfaRequired = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var auth = f.Get<AuthService>();

        var result = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, WmsFixture.TenantId, null), default);

        Assert.Equal("mfa_required", result.Status);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task UserAdminService_SetMfaRequiredAsync_toggles_the_membership_flag_and_gates_the_next_login()
    {
        await using var f = await FixtureAsync();
        var t = await f.Db.Tenants.SingleAsync(x => x.TenantId == WmsFixture.TenantId);
        t.MfaRequired = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var users = f.Get<UserAdminService>();
        var auth = f.Get<AuthService>();

        var afterOn = await users.SetMfaRequiredAsync(1, true, default);
        Assert.True(afterOn.MfaRequired);
        var blocked = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, WmsFixture.TenantId, null), default);
        Assert.Equal("mfa_required", blocked.Status);

        var afterOff = await users.SetMfaRequiredAsync(1, false, default);
        Assert.False(afterOff.MfaRequired);
        Assert.False((await f.Db.UserTenants.AsNoTracking().SingleAsync(x => x.UserId == 1 && x.TenantId == WmsFixture.TenantId)).MfaRequired);
    }

    [Fact]
    public async Task AdminResetMfaAsync_disables_the_confirmed_totp_and_the_user_can_enroll_again()
    {
        await using var f = await FixtureAsync();
        f.Db.UserMfaFactors.Add(new UserMfaFactor { UserId = 1, FactorTypeLookupId = MfaFactorTypeTotpId(f), IsActive = true, IsConfirmed = true, SecretEnc = [1, 2, 3] });
        var user = await f.Db.Users.SingleAsync(u => u.Id == 1);
        user.TwoFactorEnabled = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var auth = f.Get<AuthService>();

        await auth.AdminResetMfaAsync(1, default);

        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Users.AsNoTracking().Where(u => u.Id == 1).Select(u => u.TwoFactorEnabled).SingleAsync());
        Assert.All(await f.Db.UserMfaFactors.AsNoTracking().Where(x => x.UserId == 1).ToListAsync(), x => Assert.False(x.IsConfirmed));
        // 2026-10-01: el usuario recibe un aviso por correo del reinicio (no da acceso; si no lo pidió, se entera).
        var mail = Assert.Single(((NoEmailSender)f.Get<Teikem.Infrastructure.Abstractions.ITransactionalEmailSender>()).Sent);
        Assert.Equal("yo@t.local", mail.To);
        Assert.Equal("Su verificación en dos pasos de Teikem fue reiniciada", mail.Subject);
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_replaces_the_old_ones_and_needs_a_confirmed_totp()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        // Sin MFA confirmado → 409.
        var none = await Assert.ThrowsAsync<ConflictException>(() => auth.RegenerateRecoveryCodesAsync(default));
        Assert.Equal(AuthService.NoMfaForRecoveryCodesMessage, none.Message);

        f.Db.UserMfaFactors.Add(new UserMfaFactor { UserId = 1, FactorTypeLookupId = MfaFactorTypeTotpId(f), IsActive = true, IsConfirmed = true, SecretEnc = [1, 2, 3] });
        f.Db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = 1, CodeHash = TotpService.Hash("viejo-1") });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var result = await auth.RegenerateRecoveryCodesAsync(default);

        f.Db.ChangeTracker.Clear();
        var hashes = await f.Db.MfaRecoveryCodes.AsNoTracking().Where(c => c.UserId == 1).Select(c => c.CodeHash).ToListAsync();
        Assert.Equal(result.RecoveryCodes.Count, hashes.Count);
        Assert.DoesNotContain(TotpService.Hash("viejo-1"), hashes);
        Assert.All(result.RecoveryCodes, c => Assert.Contains(TotpService.Hash(c), hashes));
    }
}
