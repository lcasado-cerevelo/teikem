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
/// Lote 100: candado antifuerza bruta común (espera que crece: 1, 5, 15 y 60 min; sin enumeración de usuarios) y computadoras de confianza (30 días; una computadora nueva se confirma por correo).
/// </summary>
public class LoginHardeningTests
{
    private const string Password = "Clave-Segura-2026x";
    private const string Token1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Token2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static async Task<WmsFixture> FixtureAsync(bool trustedBrowsers = false, bool emailConfigured = true)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>().AddDefaultTokenProviders();
            s.Configure<IdentityOptions>(o => { o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); });
            s.AddSingleton<ISecurityEventWriter>(_ => new ReceivingNullSecurityEventWriter());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<IOptions<PasswordResetOptions>>(Options.Create(new PasswordResetOptions { WebBaseUrl = "https://app.test" }));
            s.AddSingleton<IOptions<TrustedBrowserOptions>>(Options.Create(new TrustedBrowserOptions { Enabled = trustedBrowsers }));
            s.AddSingleton(new NoEmailSender { Configured = emailConfigured });
            s.AddSingleton<ITransactionalEmailSender>(sp => sp.GetRequiredService<NoEmailSender>());
            s.AddSingleton<TrustedBrowserService>();
            s.AddSingleton<AuthService>();
        });
        f.Db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        var me = new ApplicationUser
        {
            Id = 1, UserName = "yo@t.local", NormalizedUserName = "YO@T.LOCAL", Email = "yo@t.local", NormalizedEmail = "YO@T.LOCAL", FullName = "Yo Mismo", SecurityStamp = "s1", IsActive = true, LockoutEnabled = true,
            OnboardingRequired = false, MustChangePassword = false, EmailVerifiedUtc = DateTime.UtcNow,
        };
        me.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(me, Password);
        f.Db.Users.Add(me);
        await f.Db.SaveChangesAsync();
        foreach (var tn in await f.Db.Tenants.IgnoreQueryFilters().ToListAsync()) tn.MfaRequired = false;   // estas pruebas son de la contraseña y la computadora, no del MFA
        f.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = WmsFixture.TenantId, StatusCodeId = 900, IsDefault = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    /// <summary>InMemory no traduce el ExecuteUpdate con que IssueAsync anota el último ingreso: lo que entra se prueba con la revisión de la computadora (la que decide si se emiten tokens).</summary>
    private static async Task<bool> TrustedAsync(WmsFixture f, string token)
        => (await f.Get<TrustedBrowserService>().CheckAsync(await f.Db.Users.AsNoTracking().SingleAsync(u => u.Id == 1), token, null, default)).Trusted;

    private static ApplicationUser User(WmsFixture f) => f.Db.Users.AsNoTracking().Single(u => u.Id == 1);

    private static async Task FailAsync(AuthService auth, int times)
    {
        for (var i = 0; i < times; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("yo@t.local", "equivocada", null, null), default));
    }

    // ---------------------------------------------------------------- espera que crece

    [Fact]
    public void Lockout_policy_grows_1_5_15_60_and_starts_over_after_24_hours()
    {
        Assert.Equal(new[] { 1, 5, 15, 60 }, LockoutPolicy.Minutes);
        Assert.Equal(new[] { 1, 5, 15, 60, 60, 60 }, Enumerable.Range(0, 6).Select(LockoutPolicy.MinutesFor));
        var now = DateTime.UtcNow;
        Assert.Equal(0, LockoutPolicy.LevelFor(3, null, now));
        Assert.Equal(2, LockoutPolicy.LevelFor(2, now.AddHours(-23), now));
        Assert.Equal(0, LockoutPolicy.LevelFor(2, now.AddHours(-25), now));
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_for_one_minute_then_five_then_fifteen_and_the_owner_is_told_by_email()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var mail = f.Get<NoEmailSender>();

        await FailAsync(auth, 5);
        var first = User(f);
        Assert.Equal(1, first.LockoutLevel);
        Assert.InRange((first.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalSeconds, 30, 65);
        Assert.Contains(mail.Sent, m => m.To == "yo@t.local" && m.Subject.Contains("bloque"));

        // con la cuenta bloqueada, ni la contraseña correcta entra (mismo mensaje que una equivocada)
        var locked = await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default));
        Assert.Equal("Credenciales inválidas.", locked.Message);

        // pasado el primer minuto, otros 5 fallos: ahora 5 minutos
        f.Db.ChangeTracker.Clear();
        var u = await f.Db.Users.SingleAsync(x => x.Id == 1);
        u.LockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await FailAsync(auth, 5);
        var second = User(f);
        Assert.Equal(2, second.LockoutLevel);
        Assert.InRange((second.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalMinutes, 4.4, 5.1);

        // tercer bloqueo: 15 minutos
        f.Db.ChangeTracker.Clear();
        u = await f.Db.Users.SingleAsync(x => x.Id == 1);
        u.LockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await FailAsync(auth, 5);
        Assert.InRange((User(f).LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalMinutes, 14.4, 15.1);
    }

    [Fact]
    public async Task After_24_hours_without_a_lockout_the_wait_starts_again_at_one_minute()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var u = await f.Db.Users.SingleAsync(x => x.Id == 1);
        u.LockoutLevel = 3;
        u.LockoutLevelAtUtc = DateTime.UtcNow.AddHours(-25);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await FailAsync(auth, 5);
        var after = User(f);
        Assert.Equal(1, after.LockoutLevel);
        Assert.InRange((after.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalSeconds, 30, 65);
    }

    [Fact]
    public async Task An_unknown_user_gets_the_same_message_as_a_wrong_password()
    {
        await using var f = await FixtureAsync();
        var auth = f.Get<AuthService>();
        var none = await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("nadie@t.local", "lo-que-sea-1234", null, null), default));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("yo@t.local", "equivocada", null, null), default));
        Assert.Equal(wrong.Message, none.Message);
        Assert.Equal(401, none.StatusCode);
    }

    // ---------------------------------------------------------------- computadoras de confianza

    [Fact]
    public void Browser_label_is_readable_and_tokens_are_checked_for_length()
    {
        Assert.Equal("Chrome en Windows", TrustedBrowserRules.Describe("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"));
        Assert.Equal("Edge en Windows", TrustedBrowserRules.Describe("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36 Edg/120.0"));
        Assert.Equal("Safari en iPhone", TrustedBrowserRules.Describe("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605 Version/17 Safari/604"));
        Assert.Equal("Navegador", TrustedBrowserRules.Describe(null));
        Assert.True(TrustedBrowserRules.IsValidToken(Token1));
        Assert.False(TrustedBrowserRules.IsValidToken("corto"));
        Assert.False(TrustedBrowserRules.IsValidToken(null));
    }

    [Fact]
    public async Task The_feature_is_off_by_default()
    {
        Assert.False(new TrustedBrowserOptions().Enabled);
        await using var f = await FixtureAsync(trustedBrowsers: false);
        Assert.False(f.Get<TrustedBrowserService>().Enabled);
    }

    [Fact]
    public async Task A_new_computer_asks_for_email_confirmation_and_after_confirming_it_enters_and_is_trusted_for_30_days()
    {
        await using var f = await FixtureAsync(trustedBrowsers: true);
        f.Tenant.UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36";
        f.Tenant.IpAddress = "10.1.2.3";
        var auth = f.Get<AuthService>();
        var browsers = f.Get<TrustedBrowserService>();
        var mail = f.Get<NoEmailSender>();

        // primera vez desde esta computadora: sin tokens, y el correo trae el enlace con el navegador y la IP
        var first = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token1), default);
        Assert.Equal("browser_confirmation_required", first.Status);
        Assert.Null(first.Tokens);
        Assert.True(first.Browser!.Sent);
        Assert.Equal("y***@t.local", first.Browser.Email);
        var html = Assert.Single(mail.Sent).Html;
        Assert.Contains("https://app.test/confirm-browser?token=", html);
        Assert.Contains("Chrome en Windows", html);
        Assert.Contains("10.1.2.3", html);
        var secret = System.Text.RegularExpressions.Regex.Match(html, "confirm-browser\\?token=([0-9a-f]{64})").Groups[1].Value;
        Assert.Equal(64, secret.Length);
        var pending = Assert.Single(f.Db.TrustedBrowsers);
        Assert.Null(pending.ConfirmedAtUtc);
        Assert.NotEqual(Token1, pending.TokenHash);   // solo el resumen
        Assert.NotEqual(secret, pending.ConfirmHash);

        // volver a intentar enseguida no manda otro correo (tope de reenvío)
        f.Db.ChangeTracker.Clear();
        var again = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token1), default);
        Assert.Equal("browser_confirmation_required", again.Status);
        Assert.Single(mail.Sent);

        // un enlace inventado no sirve; el verdadero confirma esa computadora
        var bad = await Assert.ThrowsAsync<ValidationException>(() => browsers.ConfirmAsync(new string('0', 64), default));
        Assert.Equal(TrustedBrowserRules.LinkInvalidMessage, bad.Message);
        await browsers.ConfirmAsync(secret, default);
        var confirmed = f.Db.TrustedBrowsers.AsNoTracking().Single();
        Assert.NotNull(confirmed.ConfirmedAtUtc);
        Assert.Null(confirmed.ConfirmHash);
        Assert.InRange((confirmed.ExpiresAtUtc!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);
        // el enlace es de un solo uso
        await Assert.ThrowsAsync<ValidationException>(() => browsers.ConfirmAsync(secret, default));

        // ahora entra
        f.Db.ChangeTracker.Clear();
        Assert.True(await TrustedAsync(f, Token1));

        // otra computadora (otra marca) vuelve a pedir confirmación
        f.Db.ChangeTracker.Clear();
        var other = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token2), default);
        Assert.Equal("browser_confirmation_required", other.Status);
        Assert.Equal(2, mail.Sent.Count);

        // y sin marca (un cliente que no la manda) no entra ni manda correo
        f.Db.ChangeTracker.Clear();
        var none = await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, null), default);
        Assert.Equal("browser_confirmation_required", none.Status);
        Assert.False(none.Browser!.Sent);
        Assert.Equal(2, mail.Sent.Count);
    }

    [Fact]
    public async Task Trust_runs_out_after_30_days_without_use_and_each_login_renews_the_window()
    {
        await using var f = await FixtureAsync(trustedBrowsers: true);
        var auth = f.Get<AuthService>();
        var browsers = f.Get<TrustedBrowserService>();
        await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token1), default);
        var secret = System.Text.RegularExpressions.Regex.Match(f.Get<NoEmailSender>().Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value;
        await browsers.ConfirmAsync(secret, default);

        // a los 20 días entra y la ventana se corre otra vez 30 días
        f.Db.ChangeTracker.Clear();
        var row = await f.Db.TrustedBrowsers.SingleAsync();
        row.ExpiresAtUtc = DateTime.UtcNow.AddDays(10);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.True(await TrustedAsync(f, Token1));
        Assert.InRange((f.Db.TrustedBrowsers.AsNoTracking().Single().ExpiresAtUtc!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);

        // 31 días sin usar: vence y vuelve a pedir confirmación
        f.Db.ChangeTracker.Clear();
        row = await f.Db.TrustedBrowsers.SingleAsync();
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        row.EmailSentAtUtc = DateTime.UtcNow.AddHours(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.Equal("browser_confirmation_required", (await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token1), default)).Status);
    }

    [Fact]
    public async Task Mine_lists_only_my_trusted_computers_marks_the_current_one_and_removing_one_asks_again()
    {
        await using var f = await FixtureAsync(trustedBrowsers: true);
        var auth = f.Get<AuthService>();
        var browsers = f.Get<TrustedBrowserService>();
        foreach (var token in new[] { Token1, Token2 })
        {
            f.Db.ChangeTracker.Clear();
            await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, token), default);
            var s = System.Text.RegularExpressions.Regex.Match(f.Get<NoEmailSender>().Sent.Last().Html, "token=([0-9a-f]{64})").Groups[1].Value;
            await browsers.ConfirmAsync(s, default);
        }
        f.Db.ChangeTracker.Clear();
        var list = await browsers.ListMineAsync(Token2, default);
        Assert.Equal(2, list.Count);
        Assert.Single(list, b => b.IsCurrent);

        await browsers.RevokeMineAsync(list.Single(b => b.IsCurrent).PublicId, default);
        f.Db.ChangeTracker.Clear();
        Assert.Single(await browsers.ListMineAsync(Token1, default));
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => browsers.RevokeMineAsync(Guid.NewGuid(), default));
        Assert.Equal("Computadora no encontrada.", nf.Message);
        // la quitada vuelve a pedir confirmación
        f.Db.ChangeTracker.Clear();
        Assert.Equal("browser_confirmation_required", (await auth.LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token2), default)).Status);
    }

    [Fact]
    public async Task Changing_the_password_removes_the_trust_of_every_computer()
    {
        await using var f = await FixtureAsync(trustedBrowsers: true);
        var browsers = f.Get<TrustedBrowserService>();
        await f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null, Token1), default);
        var secret = System.Text.RegularExpressions.Regex.Match(f.Get<NoEmailSender>().Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value;
        await browsers.ConfirmAsync(secret, default);
        f.Db.ChangeTracker.Clear();
        Assert.Single(await browsers.ListMineAsync(Token1, default));

        await browsers.RevokeAllAsync(1, default);
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await browsers.ListMineAsync(Token1, default));
    }
}
