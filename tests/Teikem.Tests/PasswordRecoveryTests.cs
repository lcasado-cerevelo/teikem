using Teikem.Domain.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
/// 2026-10-07 — recuperar la contraseña: (1) el administrador pone una contraseña temporal de 10 minutos (si nadie entra con ella vuelve la anterior; si
/// entra, obliga a cambiarla) y (2) «Olvidé mi contraseña» del login (enlace por correo de un solo uso).
/// </summary>
public class PasswordRecoveryTests
{
    private const string OldPassword = "Clave-Anterior-2026";
    private const string NewPassword = "Clave-Nueva-Segura-2026";

    private static async Task<WmsFixture> FixtureAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>(o =>
            {
                // igual que producción (DependencyInjection.cs): longitud ≥ 12 y sin reglas de composición
                o.Password.RequiredLength = 12; o.Password.RequireDigit = false; o.Password.RequireLowercase = false; o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false; o.Password.RequiredUniqueChars = 4;
            }).AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = true }));
            s.AddSingleton<IOptions<PasswordResetOptions>>(Options.Create(new PasswordResetOptions { WebBaseUrl = "https://app.test", PortalWebBaseUrl = "https://portal.test", ReturnLinkInResponse = true }));
            s.AddSingleton<NoEmailSender>();
            s.AddSingleton<ITransactionalEmailSender>(sp => sp.GetRequiredService<NoEmailSender>());
            s.AddSingleton<AuthService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        // 1 = administrador (el usuario del contexto); 2 = la persona que olvidó su contraseña (ya hizo su primer ingreso completo).
        foreach (var (id, mail) in new[] { (1, "admin@t.local"), (2, "ana@t.local") })
        {
            var u = new ApplicationUser
            {
                Id = id, UserName = mail, NormalizedUserName = mail.ToUpperInvariant(), Email = mail, NormalizedEmail = mail.ToUpperInvariant(), FullName = mail,
                SecurityStamp = "s" + id, IsActive = true, OnboardingRequired = false, MustChangePassword = false, EmailVerifiedUtc = DateTime.UtcNow, EmailConfirmed = true,
            };
            u.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(u, OldPassword);
            f.Db.Users.Add(u);
        }
        await f.Db.SaveChangesAsync();
        (await f.Db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.TenantId == WmsFixture.TenantId)).Name = "Advance Solutions";
        foreach (var id in new[] { 1, 2 }) f.Db.UserTenants.Add(new UserTenant { UserId = id, TenantId = WmsFixture.TenantId, StatusCodeId = 900, IsDefault = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    /// <summary>Entró por el camino normal (con o sin segundo factor según la compañía), no por el primer ingreso obligatorio.</summary>
    private static void AssertEntered(AuthResultDto r) => Assert.Contains(r.Status, new[] { "ok", "mfa_required" });

    private static Task<AuthResultDto> Login(WmsFixture f, string password)
        => f.Get<AuthService>().LoginAsync(new LoginRequest("ana@t.local", password, null, null), default);

    [Fact]
    public async Task Temporary_password_works_for_ten_minutes_and_forces_the_user_to_choose_their_own()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var temp = await auth.AdminSetTemporaryPasswordAsync(2, null, default);

        Assert.Equal(AuthService.TempPasswordMinutes, temp.ValidMinutes);
        Assert.True(temp.Password.Length >= 12);
        Assert.InRange((temp.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes, 9, 10.1);
        // Mientras vale, la anterior no entra y la temporal lleva al cambio obligatorio (sin tokens).
        await Assert.ThrowsAsync<UnauthorizedException>(() => Login(f, OldPassword));
        var result = await Login(f, temp.Password);
        Assert.Equal("onboarding_required", result.Status);
        Assert.Null(result.Tokens);
        Assert.True(result.Onboarding!.PasswordChangeRequired);
    }

    [Fact]
    public async Task Unused_temporary_password_expires_and_the_previous_one_comes_back()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var temp = await auth.AdminSetTemporaryPasswordAsync(2, new TemporaryPasswordRequest("Temporal-Del-Admin-77"), default);
        var u = await f.Db.Users.SingleAsync(x => x.Id == 2);
        u.TempPasswordExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<UnauthorizedException>(() => Login(f, temp.Password));
        var back = await Login(f, OldPassword);
        // Todo como estaba: primer ingreso ya hecho, entra directo.
        AssertEntered(back);
        var after = await f.Db.Users.AsNoTracking().SingleAsync(x => x.Id == 2);
        Assert.Null(after.TempPasswordExpiresUtc);
        Assert.Null(after.PreviousPasswordHash);
        Assert.False(after.OnboardingRequired);
        Assert.False(after.MustChangePassword);
    }

    [Fact]
    public async Task A_second_temporary_password_keeps_the_original_password_as_the_one_to_restore()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        await auth.AdminSetTemporaryPasswordAsync(2, new TemporaryPasswordRequest("Primera-Temporal-123"), default);
        await auth.AdminSetTemporaryPasswordAsync(2, new TemporaryPasswordRequest("Segunda-Temporal-456"), default);
        var u = await f.Db.Users.SingleAsync(x => x.Id == 2);
        u.TempPasswordExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        AssertEntered(await Login(f, OldPassword));
    }

    [Fact]
    public async Task Temporary_password_rejects_oneself_and_people_outside_the_company_and_short_passwords()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var self = await Assert.ThrowsAsync<ConflictException>(() => auth.AdminSetTemporaryPasswordAsync(1, null, default));
        Assert.Equal(AuthService.TempPasswordSelfMessage, self.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => auth.AdminSetTemporaryPasswordAsync(99, null, default));
        await Assert.ThrowsAsync<ValidationException>(() => auth.AdminSetTemporaryPasswordAsync(2, new TemporaryPasswordRequest("corta"), default));
        // la contraseña de Ana no cambió
        AssertEntered(await Login(f, OldPassword));
    }

    [Fact]
    public async Task Choosing_a_new_password_after_the_temporary_one_clears_the_restore_data()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var temp = await auth.AdminSetTemporaryPasswordAsync(2, null, default);
        var login = await Login(f, temp.Password);
        var step = await auth.SetOnboardingPasswordAsync(2, WmsFixture.TenantId, null, new OnboardingPasswordRequest(NewPassword), default);
        Assert.False(step.State.PasswordChangeRequired);
        var u = await f.Db.Users.AsNoTracking().SingleAsync(x => x.Id == 2);
        Assert.Null(u.TempPasswordExpiresUtc);
        Assert.Null(u.PreviousPasswordHash);
        Assert.NotNull(login.MfaChallengeToken);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_for_unknown_emails_and_sends_a_link_for_known_ones()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var unknown = await auth.RequestPasswordResetAsync(new ForgotPasswordRequest("nadie@t.local"), default);
        Assert.Equal(AuthService.ForgotPasswordMessage, unknown.Message);
        Assert.Null(unknown.Link);
        Assert.Empty(f.Get<NoEmailSender>().Sent);

        var known = await auth.RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default);
        Assert.Equal(AuthService.ForgotPasswordMessage, known.Message);
        Assert.StartsWith("https://app.test/reset-password?email=ana%40t.local&token=", known.Link);
        var sent = Assert.Single(f.Get<NoEmailSender>().Sent);
        Assert.Equal("ana@t.local", sent.To);
        Assert.Contains("reset-password", sent.Html);
        // 2026-10-08: ana pertenece a UNA sola compañía, así que el remitente es «Advance Solutions»
        Assert.Equal("Advance Solutions", f.Get<NoEmailSender>().Companies.Single());
    }

    [Fact]
    public async Task Forgot_password_uses_the_general_sender_when_the_user_belongs_to_several_companies()
    {
        await using var f = await FixtureAsync();
        f.Db.Tenants.Add(new Tenant { TenantId = 9001, Name = "Advance Depot", IsActive = true });
        f.Db.UserTenants.Add(new UserTenant { UserId = 2, TenantId = 9001, StatusCodeId = 900, IsDefault = false });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        await f.Get<AuthService>().RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default);

        Assert.Null(f.Get<NoEmailSender>().Companies.Single());
    }

    [Fact]
    public async Task The_link_sets_a_new_password_once_and_closes_the_sessions()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var link = (await auth.RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default)).Link!;
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword), default);
        await Assert.ThrowsAsync<UnauthorizedException>(() => Login(f, OldPassword));
        AssertEntered(await Login(f, NewPassword));

        // un solo uso
        var again = await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, "Otra-Clave-Distinta-1"), default));
        Assert.Equal(AuthService.ResetLinkInvalidMessage, again.Message);
    }

    [Theory]
    [InlineData("ana@t.local", "basura")]
    [InlineData("nadie@t.local", "basura")]
    [InlineData("ana@t.local", "")]
    public async Task A_bad_link_is_rejected_with_the_same_message(string email, string token)
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<AuthService>().ResetPasswordWithLinkAsync(new ResetPasswordRequest(email, token, NewPassword), default));
        Assert.Equal(AuthService.ResetLinkInvalidMessage, ex.Message);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected_and_the_link_stays_usable()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var link = (await auth.RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default)).Link!;
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
        await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, "corta"), default));
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword), default);
        AssertEntered(await Login(f, NewPassword));
    }

    // ---------------------------------------------------------------- Lote 219: las personas del portal también recuperan su contraseña, con el enlace del PORTAL

    /// <summary>Convierte a Ana (usuario 2) en una persona del portal del cliente.</summary>
    private static async Task MakeAnaPortalAsync(WmsFixture f)
    {
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 960, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Portal, LabelJson = "{\"es\":\"Portal\"}", IsActive = true });
        await f.Db.SaveChangesAsync();
        var ana = await f.Db.Users.AsTracking().SingleAsync(u => u.Id == 2);
        ana.UserKindLookupId = 960;
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_portal_person_gets_a_link_to_the_PORTAL_not_to_the_staff_web()
    {
        await using var f = await FixtureAsync();
        await MakeAnaPortalAsync(f);
        var known = await f.Get<AuthService>().RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default);
        Assert.Equal(AuthService.ForgotPasswordMessage, known.Message);
        Assert.StartsWith("https://portal.test/reset-password?email=ana%40t.local&token=", known.Link);
        Assert.Equal("ana@t.local", Assert.Single(f.Get<NoEmailSender>().Sent).To);
    }

    [Fact]
    public async Task Without_a_portal_address_nothing_is_sent_and_the_answer_is_the_same()
    {
        await using var f = await FixtureAsync();
        await MakeAnaPortalAsync(f);
        f.Get<IOptions<PasswordResetOptions>>().Value.PortalWebBaseUrl = null;
        var r = await f.Get<AuthService>().RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default);
        Assert.Equal(AuthService.ForgotPasswordMessage, r.Message);
        Assert.Null(r.Link);
        Assert.Empty(f.Get<NoEmailSender>().Sent);
    }

    [Fact]
    public async Task A_portal_person_with_MFA_enrolled_still_needs_the_code_to_reset()
    {
        await using var f = await FixtureAsync();
        await MakeAnaPortalAsync(f);
        var auth = f.Get<AuthService>();
        var (secret, _) = await EnrollAnaAsync(auth);
        var token = await LinkTokenAsync(auth);
        await Assert.ThrowsAsync<MfaCodeRequiredException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword), default));
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, CurrentCode(secret)), default);
        // una persona del portal no entra por el login del personal: se comprueba la contraseña directamente
        var users = f.Get<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync("2"))!, NewPassword));
    }

    // ---------------------------------------------------------------- 2026-10-12: restablecer la contraseña exige el código MFA (docs/ip-preferida-mfa-diseno.md §3.1)

    /// <summary>Inscribe y confirma el TOTP de Ana (usuario 2) con el flujo real; devuelve su secreto y sus códigos de recuperación.</summary>
    private static async Task<(byte[] Secret, IReadOnlyList<string> Recovery)> EnrollAnaAsync(AuthService auth)
    {
        var enrolled = await auth.EnrollTotpAsync(2, default);
        var secret = TotpService.Base32Decode(enrolled.Secret);
        var confirmed = await auth.ConfirmTotpAsync(2, new MfaConfirmRequest(CurrentCode(secret)), default);
        return (secret, confirmed.RecoveryCodes);
    }

    private static string CurrentCode(byte[] secret) => TotpService.ComputeCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static async Task<string> LinkTokenAsync(AuthService auth)
    {
        var link = (await auth.RequestPasswordResetAsync(new ForgotPasswordRequest("ana@t.local"), default)).Link!;
        return Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
    }

    [Fact]
    public async Task With_MFA_enrolled_the_link_alone_is_not_enough_and_the_link_is_not_spent()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var (secret, _) = await EnrollAnaAsync(auth);
        var token = await LinkTokenAsync(auth);

        // sin código: error específico (mfa_required) y la contraseña no cambia
        var missing = await Assert.ThrowsAsync<MfaCodeRequiredException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword), default));
        Assert.Equal("mfa_required", missing.Code);
        Assert.Equal(400, missing.StatusCode);
        Assert.Equal(AuthService.ResetMfaRequiredMessage, missing.Message);
        AssertEntered(await Login(f, OldPassword));

        // el mismo enlace sigue vigente: con el código correcto sí cambia la contraseña y se cierra el enlace
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, CurrentCode(secret)), default);
        AssertEntered(await Login(f, NewPassword));
        await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, "Otra-Clave-Distinta-1", CurrentCode(secret)), default));
    }

    [Fact]
    public async Task A_wrong_MFA_code_is_rejected_on_the_mfaCode_field_and_the_link_stays_usable()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var (secret, _) = await EnrollAnaAsync(auth);
        var token = await LinkTokenAsync(auth);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, "000000"), default));
        Assert.Equal(AuthService.ResetMfaInvalidMessage, bad.Message);
        Assert.Contains("mfaCode", bad.Errors!.Keys);
        AssertEntered(await Login(f, OldPassword)); // no cambió nada
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, CurrentCode(secret)), default);
        AssertEntered(await Login(f, NewPassword));
    }

    [Fact]
    public async Task Wrong_MFA_codes_count_toward_the_account_lockout_like_the_login()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var (secret, _) = await EnrollAnaAsync(auth);
        var token = await LinkTokenAsync(auth);
        // el fixture crea a las personas sin «bloqueo habilitado» (Identity lo enciende al crear un usuario de verdad)
        var ana = await f.Db.Users.AsTracking().SingleAsync(u => u.Id == 2);
        ana.LockoutEnabled = true;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, "000000"), default));
        // bloqueada: ni el código correcto sirve (mismo mensaje, sin revelar el bloqueo) y el login también queda bloqueado
        var locked = await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, CurrentCode(secret)), default));
        Assert.Equal(AuthService.ResetMfaInvalidMessage, locked.Message);
        await Assert.ThrowsAsync<UnauthorizedException>(() => Login(f, OldPassword));
    }

    [Fact]
    public async Task A_recovery_code_is_accepted_once_in_place_of_the_authenticator_code()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var (_, recovery) = await EnrollAnaAsync(auth);
        var token = await LinkTokenAsync(auth);
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, recovery[0]), default);
        AssertEntered(await Login(f, NewPassword));

        // el mismo código de recuperación no vuelve a servir
        var token2 = await LinkTokenAsync(auth);
        await Assert.ThrowsAsync<ValidationException>(() => auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token2, "Otra-Clave-Distinta-1", recovery[0]), default));
    }

    [Fact]
    public async Task Without_MFA_enrolled_the_link_is_the_only_factor_even_if_a_code_is_sent()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var token = await LinkTokenAsync(auth);
        await auth.ResetPasswordWithLinkAsync(new ResetPasswordRequest("ana@t.local", token, NewPassword, "123456"), default);
        AssertEntered(await Login(f, NewPassword));
    }
}
