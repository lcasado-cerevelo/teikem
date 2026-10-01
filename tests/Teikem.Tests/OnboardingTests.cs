using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
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
/// Primer ingreso obligatorio (pedido de Luis, 2026-09-30): verificar el correo con un código de 6 dígitos → poner una
/// contraseña propia → configurar el MFA. Hasta completarlo el login no emite tokens.
/// </summary>
public class OnboardingTests
{
    private const string AdminGivenPassword = "Clave-Del-Admin-2026";

    private static async Task<WmsFixture> FixtureAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = true, ReturnEmailCodeInResponse = true }));
            s.AddSingleton<NoEmailSender>();
            s.AddSingleton<ITransactionalEmailSender>(sp => sp.GetRequiredService<NoEmailSender>());
            s.AddSingleton<AuthService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        // Usuario recién creado por un administrador: primer ingreso pendiente (los valores por defecto de la entidad).
        var me = new ApplicationUser { Id = 1, UserName = "nuevo@t.local", NormalizedUserName = "NUEVO@T.LOCAL", Email = "nuevo@t.local", NormalizedEmail = "NUEVO@T.LOCAL", FullName = "Nuevo Usuario", SecurityStamp = "s1", IsActive = true };
        me.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(me, AdminGivenPassword);
        f.Db.Users.Add(me);
        await f.Db.SaveChangesAsync();
        f.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = WmsFixture.TenantId, StatusCodeId = 900, IsDefault = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    [Fact]
    public void New_users_start_with_the_first_login_pending()
    {
        var u = new ApplicationUser();
        Assert.True(u.OnboardingRequired);
        Assert.True(u.MustChangePassword);
        Assert.Null(u.EmailVerifiedUtc);
        Assert.Equal("n***@t.local", AuthService.MaskEmail("nuevo@t.local"));
    }

    [Fact]
    public async Task Login_with_the_first_login_pending_gives_no_tokens_and_says_what_is_missing()
    {
        await using var f = await FixtureAsync();
        var result = await f.Get<AuthService>().LoginAsync(new LoginRequest("nuevo@t.local", AdminGivenPassword, null, null), default);

        Assert.Equal("onboarding_required", result.Status);
        Assert.Null(result.Tokens);
        Assert.NotNull(result.MfaChallengeToken);
        Assert.True(result.MfaEnrollmentRequired);
        Assert.Equal(new OnboardingStateDto("n***@t.local", false, true, false), result.Onboarding);
    }

    [Fact]
    public async Task The_steps_go_in_order_email_then_password_and_mfa_cannot_finish_before()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var tid = WmsFixture.TenantId;

        // Contraseña antes del correo → 409; MFA antes de correo y contraseña → 403.
        var early = await Assert.ThrowsAsync<ConflictException>(() => auth.SetOnboardingPasswordAsync(1, tid, null, new OnboardingPasswordRequest("Mi-Clave-Propia-2026"), default));
        Assert.Equal(AuthService.OnboardingEmailFirstMessage, early.Message);
        var mfaEarly = await Assert.ThrowsAsync<ForbiddenException>(() => auth.VerifyMfaAsync(1, tid, null, new MfaVerifyRequest("123456", null), default));
        Assert.Equal(AuthService.OnboardingIncompleteMessage, mfaEarly.Message);

        // Correo: el código llega por correo (y en desarrollo en la respuesta); uno equivocado no sirve.
        var sent = await auth.SendOnboardingEmailCodeAsync(1, tid, default);
        Assert.Equal("n***@t.local", sent.Email);
        Assert.Matches("^[0-9]{6}$", sent.DevCode!);
        Assert.Contains(sent.DevCode!, f.Get<NoEmailSender>().Sent.Single().Html);
        var wrong = await Assert.ThrowsAsync<ValidationException>(() => auth.VerifyOnboardingEmailAsync(1, tid, new OnboardingCodeRequest(sent.DevCode == "000000" ? "111111" : "000000"), default));
        Assert.Contains(AuthService.InvalidEmailCodeMessage, wrong.Message);
        var emailOk = await auth.VerifyOnboardingEmailAsync(1, tid, new OnboardingCodeRequest(sent.DevCode), default);
        Assert.True(emailOk.State.EmailVerified);

        // Contraseña: no puede ser la que dio el administrador; la nueva rota el sello y trae un challenge nuevo.
        var same = await Assert.ThrowsAsync<ValidationException>(() => auth.SetOnboardingPasswordAsync(1, tid, null, new OnboardingPasswordRequest(AdminGivenPassword), default));
        Assert.Contains(AuthService.SamePasswordMessage, same.Message);
        // El API tiene el sello viejo en caché (60 s): tras cambiar la contraseña se debe quitar para aceptar el challenge nuevo.
        var cache = f.Get<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        cache.Set(JwtTokenService.StampCacheKey(1), "sello-viejo");
        var pwd = await auth.SetOnboardingPasswordAsync(1, tid, null, new OnboardingPasswordRequest("Mi-Clave-Propia-2026"), default);
        Assert.False(cache.TryGetValue(JwtTokenService.StampCacheKey(1), out _));
        Assert.False(pwd.State.PasswordChangeRequired);
        Assert.NotNull(pwd.MfaChallengeToken);

        // Falta solo el MFA: el login sigue pidiendo el primer ingreso, ya con correo y contraseña hechos.
        f.Db.ChangeTracker.Clear();
        var again = await auth.LoginAsync(new LoginRequest("nuevo@t.local", "Mi-Clave-Propia-2026", null, null), default);
        Assert.Equal("onboarding_required", again.Status);
        Assert.Equal(new OnboardingStateDto("n***@t.local", true, false, false), again.Onboarding);
        // La contraseña del administrador ya no entra.
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("nuevo@t.local", AdminGivenPassword, null, null), default));
    }
}
