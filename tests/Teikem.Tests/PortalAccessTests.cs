using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
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
/// Lote 101 — acceso del portal por cliente: reglas de rutas, reto anti-robot (Turnstile), login del portal (separado del staff, bloqueo, computadora de confianza siempre, MFA), sesión ligada a un cliente (claim cid) y
/// aislamiento de datos: un usuario de portal solo lee lo de SU cliente; lo de otro cliente (misma compañía) y lo de otra compañía responde 404 como si no existiera.
/// </summary>
public class PortalAccessTests
{
    private const string Password = "Clave-Portal-2026x";
    private const string Browser = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    // ---------------------------------------------------------------- reglas de rutas

    [Fact]
    public void A_portal_session_only_reaches_portal_session_and_legal_routes()
    {
        foreach (var ok in new[] { "/api/v1/portal/orders", "/api/v1/portal/me", "/api/v1/portal/orders/abc", "/api/v1/auth/refresh", "/api/v1/auth/logout", "/api/v1/auth/trusted-browsers", "/api/v1/legal/accept", "/index.html", "/assets/x.js" })
            Assert.True(PortalScopeRules.IsAllowedForPortal(ok), ok);
        foreach (var no in new[] { "/api/v1/orders", "/api/v1/clients", "/api/v1/users", "/api/v1/me", "/api/v1/auth/switch-tenant", "/api/v1/auth/device/login", "/api/v1/auth/onboarding/email/send", "/api/v1/billing/invoices", "/hubs/messages", "/API/V1/Orders/" })
            Assert.False(PortalScopeRules.IsAllowedForPortal(no), no);
    }

    [Fact]
    public void Portal_only_paths_are_recognized_and_the_legal_gate_lets_portal_me_through()
    {
        Assert.True(PortalScopeRules.IsPortalOnlyPath("/api/v1/portal/orders"));
        Assert.True(PortalScopeRules.IsPortalOnlyPath("/API/v1/Portal/me/"));
        Assert.False(PortalScopeRules.IsPortalOnlyPath("/api/v1/portalusers"));
        Assert.False(PortalScopeRules.IsPortalOnlyPath("/api/v1/orders"));
        Assert.True(LegalRules.IsExemptPath("/api/v1/portal/me"));
        Assert.False(LegalRules.IsExemptPath("/api/v1/portal/orders"));
    }

    // ---------------------------------------------------------------- reto anti-robot

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            return reply(request);
        }
    }

    private static CloudflareTurnstileVerifier Verifier(StubHandler h, bool enabled = true, string? secret = "s3cret")
        => new(new HttpClient(h), Options.Create(new TurnstileOptions { Enabled = enabled, SiteKey = "site", SecretKey = secret, VerifyUrl = "https://turnstile.test/verify" }));

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Turnstile_off_or_without_keys_never_blocks_and_never_calls_cloudflare()
    {
        var h = new StubHandler(_ => Json("{\"success\":false}"));
        Assert.False(Verifier(h, enabled: false).Enabled);
        Assert.True(await Verifier(h, enabled: false).VerifyAsync(null, null, default));
        Assert.False(Verifier(h, secret: null).Enabled);
        Assert.Empty(h.Bodies);
        Assert.False(new TurnstileOptions().Enabled);
    }

    [Fact]
    public async Task Turnstile_accepts_only_a_successful_siteverify_and_sends_the_secret_token_and_ip()
    {
        var ok = new StubHandler(_ => Json("{\"success\":true}"));
        Assert.True(await Verifier(ok).VerifyAsync("tok123", "10.0.0.9", default));
        Assert.Contains("secret=s3cret", ok.Bodies.Single());
        Assert.Contains("response=tok123", ok.Bodies.Single());
        Assert.Contains("remoteip=10.0.0.9", ok.Bodies.Single());

        Assert.False(await Verifier(new StubHandler(_ => Json("{\"success\":false}"))).VerifyAsync("tok", null, default));
        Assert.False(await Verifier(new StubHandler(_ => Json("{}", HttpStatusCode.InternalServerError))).VerifyAsync("tok", null, default));
        Assert.False(await Verifier(new StubHandler(_ => throw new HttpRequestException("sin red"))).VerifyAsync("tok", null, default));   // si cae el servicio el reto no se salta
        Assert.False(await Verifier(ok).VerifyAsync("", null, default));
        Assert.False(await Verifier(ok).VerifyAsync(new string('x', 3000), null, default));
    }

    // ---------------------------------------------------------------- claims

    [Fact]
    public void The_portal_access_token_carries_the_client_and_the_staff_one_does_not()
    {
        var jwt = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) }));
        var user = new ApplicationUser { Id = 5, Email = "p@c.co", FullName = "Portal", SecurityStamp = "s" };
        var portal = new JwtSecurityTokenHandler().ReadJwtToken(jwt.CreateAccessToken(user, 1, 10, UserKinds.Portal, null, portalClientId: 77).Token);
        Assert.Equal("77", portal.Claims.Single(c => c.Type == TeikemClaims.ClientId).Value);
        Assert.Equal("PORTAL", portal.Claims.Single(c => c.Type == TeikemClaims.UserKind).Value);
        var staff = new JwtSecurityTokenHandler().ReadJwtToken(jwt.CreateAccessToken(user, 1, 11, UserKinds.Internal, null).Token);
        Assert.DoesNotContain(staff.Claims, c => c.Type == TeikemClaims.ClientId);
    }

    // ---------------------------------------------------------------- login del portal

    private sealed class FakeTurnstile : ITurnstileVerifier
    {
        public bool Enabled { get; set; } = true;
        public int RequireAfterFailures { get; set; } = 3;
        public string? SiteKey => "site-key";
        public bool Result { get; set; }
        public int Calls { get; set; }
        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct) { Calls++; return Task.FromResult(Result); }
    }

    private sealed record World(WmsFixture F, AuthService Auth, TrustedBrowserService Browsers, NoEmailSender Mail, FakeTurnstile Captcha, Client A, Client B) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static async Task<World> WorldAsync(bool captcha = false)
    {
        var fake = new FakeTurnstile { Enabled = captcha };
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
            s.AddSingleton<IOptions<PasswordResetOptions>>(Options.Create(new PasswordResetOptions { WebBaseUrl = "https://portal.test" }));
            // el staff NO usa computadoras de confianza (Enabled = false): el portal las usa siempre
            s.AddSingleton<IOptions<TrustedBrowserOptions>>(Options.Create(new TrustedBrowserOptions { Enabled = false }));
            s.AddSingleton<NoEmailSender>();
            s.AddSingleton<ITransactionalEmailSender>(sp => sp.GetRequiredService<NoEmailSender>());
            s.AddSingleton<TrustedBrowserService>();
            s.AddSingleton<ITurnstileVerifier>(fake);
            s.AddSingleton<AuthService>();
        });
        var db = f.Db;
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 901, Entity = StatusDomains.PortalUserStatus, InternalCode = PortalUserStatuses.Active, LabelJson = "{\"es\":\"Activo\"}", IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 902, Entity = StatusDomains.PortalUserStatus, InternalCode = PortalUserStatuses.Suspended, LabelJson = "{\"es\":\"Suspendido\"}", IsActive = true });
        db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        db.LookupCodes.Add(new LookupCode { LookupCodeId = 951, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Portal, LabelJson = "{\"es\":\"Portal\"}", IsActive = true });
        db.LookupCodes.Add(new LookupCode { LookupCodeId = 952, Entity = LookupDomains.UserKind, InternalCode = UserKinds.Internal, LabelJson = "{\"es\":\"Interno\"}", IsActive = true });
        var a = new Client { ClientId = 501, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "A", Name = "Cliente A", IsActive = true, StatusCodeId = 900 };
        var b = new Client { ClientId = 502, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "B", Name = "Cliente B", IsActive = true, StatusCodeId = 900 };
        db.Clients.AddRange(a, b);
        ApplicationUser U(int id, string mail, int kind) => new()
        {
            Id = id, UserName = mail, NormalizedUserName = mail.ToUpperInvariant(), Email = mail, NormalizedEmail = mail.ToUpperInvariant(), FullName = mail, SecurityStamp = "s" + id, IsActive = true,
            LockoutEnabled = true, OnboardingRequired = false, MustChangePassword = false, EmailVerifiedUtc = DateTime.UtcNow, UserKindLookupId = kind,
        };
        var portalUser = U(21, "ana@cliente.co", 951);   // portal, con acceso al cliente A
        var staffUser = U(22, "staff@t.local", 952);
        var noAccess = U(23, "sin@cliente.co", 951);     // portal, pero suspendida en el cliente A
        foreach (var u in new[] { portalUser, staffUser, noAccess }) u.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(u, Password);
        db.Users.AddRange(portalUser, staffUser, noAccess);
        db.PortalUsers.AddRange(
            new PortalUser { PortalUserId = 1, TenantId = WmsFixture.TenantId, ClientId = a.ClientId, UserId = 21, Email = "ana@cliente.co", RoleLookupId = 950, StatusCodeId = 901, IsActive = true },
            new PortalUser { PortalUserId = 2, TenantId = WmsFixture.TenantId, ClientId = a.ClientId, UserId = 23, Email = "sin@cliente.co", RoleLookupId = 950, StatusCodeId = 902, IsActive = true });
        foreach (var tn in await db.Tenants.IgnoreQueryFilters().ToListAsync()) tn.MfaRequired = false;
        await db.SaveChangesAsync();
        f.Lookups.Load(db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        db.ChangeTracker.Clear();
        return new World(f, f.Get<AuthService>(), f.Get<TrustedBrowserService>(), f.Get<NoEmailSender>(), fake, a, b);
    }

    private static PortalLoginRequest Login(string mail, string password, string? browser = Browser, Guid? client = null, string? captcha = null, bool doNotTrust = false)
        => new(mail, password, client, null, browser, captcha, doNotTrust);

    private static async Task<UnauthorizedException> Unauthorized(Task t) => await Assert.ThrowsAsync<UnauthorizedException>(() => t);

    [Fact]
    public async Task Staff_unknown_and_wrong_password_all_answer_the_same_401()
    {
        await using var w = await WorldAsync();
        var staff = await Unauthorized(w.Auth.PortalLoginAsync(Login("staff@t.local", Password), default));   // un usuario del staff no entra por el portal
        var none = await Unauthorized(w.Auth.PortalLoginAsync(Login("nadie@x.co", Password), default));
        var wrong = await Unauthorized(w.Auth.PortalLoginAsync(Login("ana@cliente.co", "equivocada"), default));
        Assert.Equal("Credenciales inválidas.", staff.Message);
        Assert.Equal(staff.Message, none.Message);
        Assert.Equal(staff.Message, wrong.Message);
        Assert.Equal(1, w.F.Db.Users.AsNoTracking().Single(u => u.Id == 21).AccessFailedCount);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_portal_account_and_the_right_one_no_longer_enters()
    {
        await using var w = await WorldAsync();
        for (var i = 0; i < 5; i++) await Unauthorized(w.Auth.PortalLoginAsync(Login("ana@cliente.co", "equivocada"), default));
        var u = w.F.Db.Users.AsNoTracking().Single(x => x.Id == 21);
        Assert.Equal(1, u.LockoutLevel);
        Assert.InRange((u.LockoutEnd!.Value - DateTimeOffset.UtcNow).TotalSeconds, 30, 65);
        var locked = await Unauthorized(w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default));
        Assert.Equal("Credenciales inválidas.", locked.Message);
    }

    [Fact]
    public async Task No_active_access_or_a_client_that_is_not_theirs_is_403()
    {
        await using var w = await WorldAsync();
        var none = await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.PortalLoginAsync(Login("sin@cliente.co", Password), default));
        Assert.Equal(AuthService.PortalNoAccessMessage, none.Message);
        var other = await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password, client: w.B.PublicId), default));
        Assert.Equal(AuthService.PortalClientMessage, other.Message);
    }

    [Fact]
    public async Task A_new_computer_asks_for_email_confirmation_even_with_the_staff_feature_off_and_then_the_mfa_step_comes()
    {
        await using var w = await WorldAsync();
        w.F.Tenant.UserAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/120.0 Safari/537.36";
        var first = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        Assert.Equal("browser_confirmation_required", first.Status);
        Assert.Null(first.Tokens);
        Assert.Contains("https://portal.test/confirm-browser?token=", Assert.Single(w.Mail.Sent).Html);

        // sin marca de computadora tampoco entra
        w.F.Db.ChangeTracker.Clear();
        var noMark = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password, browser: null), default);
        Assert.Equal("browser_confirmation_required", noMark.Status);
        Assert.False(noMark.Browser!.Sent);

        // confirmada la computadora, sigue el segundo paso: sin MFA inscrito se le exige inscribirlo
        var secret = System.Text.RegularExpressions.Regex.Match(w.Mail.Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value;
        await w.Browsers.ConfirmAsync(secret, default);
        w.F.Db.ChangeTracker.Clear();
        var next = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        Assert.Equal("mfa_required", next.Status);
        Assert.True(next.MfaEnrollmentRequired);
        Assert.NotNull(next.MfaChallengeToken);
        Assert.Null(next.Tokens);
    }

    [Fact]
    public async Task With_the_mfa_enrolled_the_code_is_asked_until_it_was_written_on_this_computer_and_then_remembered_30_days()
    {
        await using var w = await WorldAsync();
        await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        await w.Browsers.ConfirmAsync(System.Text.RegularExpressions.Regex.Match(w.Mail.Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value, default);
        w.F.Db.UserMfaFactors.Add(new UserMfaFactor { UserId = 21, FactorTypeLookupId = 950, IsConfirmed = true, IsActive = true });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        // computadora de confianza pero sin código escrito desde ella: lo pide
        var user = await w.F.Db.Users.AsNoTracking().SingleAsync(u => u.Id == 21);
        var pending = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        Assert.Equal("mfa_required", pending.Status);
        Assert.False(pending.MfaEnrollmentRequired);
        Assert.False((await w.Browsers.CheckAsync(user, Browser, null, default)).MfaRecent);

        // escrito el código, se recuerda 30 días; a los 31 vuelve a pedirse
        await w.Browsers.MarkMfaVerifiedAsync(21, Browser, default);
        Assert.True((await w.Browsers.CheckAsync(user, Browser, null, default)).MfaRecent);
        var row = await w.F.Db.TrustedBrowsers.SingleAsync();
        row.MfaVerifiedAtUtc = DateTime.UtcNow.AddDays(-31);
        await w.F.Db.SaveChangesAsync();
        Assert.False((await w.Browsers.CheckAsync(user, Browser, null, default)).MfaRecent);
    }

    [Fact]
    public async Task Do_not_trust_this_computer_lets_one_login_through_asks_the_code_always_and_then_forgets_the_computer()
    {
        await using var w = await WorldAsync();
        // «No confiar»: nace de un solo uso
        await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password, doNotTrust: true), default);
        Assert.True(w.F.Db.TrustedBrowsers.AsNoTracking().Single().SingleUse);
        await w.Browsers.ConfirmAsync(System.Text.RegularExpressions.Regex.Match(w.Mail.Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value, default);
        w.F.Db.ChangeTracker.Clear();
        var user = await w.F.Db.Users.AsNoTracking().SingleAsync(u => u.Id == 21);

        // aunque el código se hubiera escrito hace poco, en una computadora «no confiar» se pide siempre
        await w.Browsers.MarkMfaVerifiedAsync(21, Browser, default);
        var check = await w.Browsers.CheckAsync(user, Browser, null, default);
        Assert.True(check.Trusted);
        Assert.False(check.MfaRecent);

        // al terminar el ingreso se olvida la computadora: la próxima vez vuelve a pedir el correo (reutiliza su fila: el índice es único por usuario y marca)
        await w.Browsers.ConsumeIfSingleUseAsync(21, Browser, default);
        w.F.Db.ChangeTracker.Clear();
        var again = await w.Browsers.CheckAsync(user, Browser, null, default, doNotTrust: true);
        Assert.False(again.Trusted);
        Assert.Single(w.F.Db.TrustedBrowsers.AsNoTracking());
        Assert.Null(w.F.Db.TrustedBrowsers.AsNoTracking().Single().RevokedAtUtc);
        Assert.Null(w.F.Db.TrustedBrowsers.AsNoTracking().Single().ConfirmedAtUtc);
    }

    [Fact]
    public async Task Marking_do_not_trust_on_an_already_trusted_computer_makes_it_single_use_from_then_on()
    {
        await using var w = await WorldAsync();
        await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        await w.Browsers.ConfirmAsync(System.Text.RegularExpressions.Regex.Match(w.Mail.Sent.Single().Html, "token=([0-9a-f]{64})").Groups[1].Value, default);
        w.F.Db.ChangeTracker.Clear();
        var user = await w.F.Db.Users.AsNoTracking().SingleAsync(u => u.Id == 21);
        Assert.False(w.F.Db.TrustedBrowsers.AsNoTracking().Single().SingleUse);
        Assert.True((await w.Browsers.CheckAsync(user, Browser, null, default, doNotTrust: true)).Trusted);
        Assert.True(w.F.Db.TrustedBrowsers.AsNoTracking().Single().SingleUse);
    }

    [Fact]
    public async Task The_captcha_is_asked_only_after_three_failures_and_cannot_be_skipped()
    {
        await using var w = await WorldAsync(captcha: true);
        w.Captcha.Result = false;
        for (var i = 0; i < 3; i++) await Unauthorized(w.Auth.PortalLoginAsync(Login("ana@cliente.co", "equivocada"), default));
        Assert.Equal(0, w.Captcha.Calls);   // los primeros fallos no piden reto

        // desde el cuarto intento: sin reto (o con uno que no vale) ni siquiera se mira la contraseña
        var without = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password), default);
        Assert.Equal("captcha_required", without.Status);
        var bad = await w.Auth.PortalLoginAsync(Login("ana@cliente.co", Password, captcha: "token-malo"), default);
        Assert.Equal("captcha_required", bad.Status);
        Assert.Equal(1, w.Captcha.Calls);
        Assert.Equal(3, w.F.Db.Users.AsNoTracking().Single(u => u.Id == 21).AccessFailedCount);

        // con un reto válido se sigue con la contraseña (equivocada: cuenta otro fallo)
        w.Captcha.Result = true;
        await Unauthorized(w.Auth.PortalLoginAsync(Login("ana@cliente.co", "equivocada", captcha: "token-bueno"), default));
        Assert.Equal(4, w.F.Db.Users.AsNoTracking().Single(u => u.Id == 21).AccessFailedCount);
        Assert.True(w.Auth.PortalAuthConfig().CaptchaEnabled);
        Assert.Equal("site-key", w.Auth.PortalAuthConfig().CaptchaSiteKey);
    }

    [Fact]
    public async Task The_portal_refresh_keeps_the_client_and_dies_when_the_access_is_lost()
    {
        await using var w = await WorldAsync();
        var raw = JwtTokenService.NewRefreshToken();
        w.F.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = 21, TenantId = WmsFixture.TenantId, TokenHash = JwtTokenService.HashToken(raw), IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1), PortalClientId = w.A.ClientId,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var pair = await w.Auth.RefreshAsync(raw, null, default);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken).Claims.ToList();
        Assert.Equal(w.A.ClientId.ToString(), claims.Single(c => c.Type == TeikemClaims.ClientId).Value);
        Assert.Equal("PORTAL", claims.Single(c => c.Type == TeikemClaims.UserKind).Value);
        Assert.Equal(w.A.ClientId, w.F.Db.RefreshTokens.AsNoTracking().Single(t => t.TokenHash == JwtTokenService.HashToken(pair.RefreshToken)).PortalClientId);

        // se le quita el acceso al cliente: la renovación falla y la sesión se revoca
        w.F.Db.ChangeTracker.Clear();
        var pu = await w.F.Db.PortalUsers.IgnoreQueryFilters().SingleAsync(p => p.UserId == 21);
        pu.StatusCodeId = 902;
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<UnauthorizedException>(() => w.Auth.RefreshAsync(pair.RefreshToken, null, default));
        Assert.All(w.F.Db.RefreshTokens.AsNoTracking().Where(t => t.UserId == 21), t => Assert.NotNull(t.RevokedAtUtc));
    }

    [Fact]
    public async Task Switching_to_a_client_that_is_not_yours_or_with_a_staff_session_is_403()
    {
        await using var w = await WorldAsync();
        string Session(int userId, int? clientId)
        {
            var raw = JwtTokenService.NewRefreshToken();
            w.F.Db.RefreshTokens.Add(new RefreshToken { UserId = userId, TenantId = WmsFixture.TenantId, TokenHash = JwtTokenService.HashToken(raw), IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1), PortalClientId = clientId });
            w.F.Db.SaveChanges();
            return raw;
        }
        var portal = Session(21, w.A.ClientId);
        var other = await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.PortalSwitchClientAsync(new PortalSwitchClientRequest(portal, w.B.PublicId), default));
        Assert.Equal(AuthService.PortalClientMessage, other.Message);
        var staff = Session(22, null);
        await Assert.ThrowsAsync<ForbiddenException>(() => w.Auth.PortalSwitchClientAsync(new PortalSwitchClientRequest(staff, w.A.PublicId), default));
    }

    // ---------------------------------------------------------------- aislamiento de datos

    private static PortalService PortalOf(OrderReadFixture f, int clientId)
    {
        f.Tenant.IsPortal = true;
        f.Tenant.PortalClientId = clientId;
        f.Tenant.ScopedClientIds = new[] { clientId };   // lo que hace TenantContextMiddleware con el claim cid
        return new PortalService(f.Db, f.Tenant, null!, null!, f.Reader, f.Lookups);
    }

    [Fact]
    public async Task A_portal_user_lists_only_the_orders_of_the_client_in_the_session()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var portal = PortalOf(f, f.C1.ClientId);
        var page = await portal.ListOrdersAsync(null, null, null, null, 0, 100, default);
        Assert.Equal(new[] { f.O1.PublicId, f.O2.PublicId }.OrderBy(x => x), page.Items.Select(i => i.PublicId).OrderBy(x => x));
        Assert.Equal(2, page.Total);
        // el otro cliente de la misma compañía ve lo suyo y nada del primero
        var other = PortalOf(f, f.C2.ClientId);
        var otherPage = await other.ListOrdersAsync(null, null, null, null, 0, 100, default);
        Assert.DoesNotContain(otherPage.Items, i => i.PublicId == f.O1.PublicId || i.PublicId == f.O2.PublicId);
    }

    [Fact]
    public async Task An_order_of_another_client_or_another_company_answers_404_like_one_that_does_not_exist()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var portal = PortalOf(f, f.C1.ClientId);
        var mine = await portal.GetOrderAsync(f.O1.PublicId, default);
        Assert.Equal(f.O1.OrderNumber, mine.OrderNumber);
        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => portal.GetOrderAsync(f.O3.PublicId, default));      // de otro cliente de la compañía
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => portal.GetOrderAsync(Guid.NewGuid(), default));
        Assert.Equal(missing.Message.Replace(missing.Message, "x"), foreign.Message.Replace(foreign.Message, "x"));
        Assert.Equal(404, foreign.StatusCode);
        var otherTenantOrder = f.Db.TransportOrders.IgnoreQueryFilters().Single(o => o.TenantId == 2);
        await Assert.ThrowsAsync<NotFoundException>(() => portal.GetOrderAsync(otherTenantOrder.PublicId, default));
    }

    [Fact]
    public async Task The_client_scope_is_a_data_filter_too_not_only_a_check_in_the_service()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        PortalOf(f, f.C1.ClientId);
        // aunque otra consulta olvidara filtrar por cliente, el filtro global del contexto solo deja ver las órdenes del cliente de la sesión
        var ids = f.Db.TransportOrders.AsNoTracking().Select(o => o.ClientId).Distinct().ToList();
        Assert.Equal(new[] { f.C1.ClientId }, ids);
    }

    [Fact]
    public async Task Portal_dtos_do_not_carry_prices_internal_notes_routes_or_drivers()
    {
        var forbidden = new[] { "QuotedAmount", "Notes", "AssignedDriverName", "AssignedDriverCode", "AssignedTripCode", "AssignedTripPublicId", "ContractPublicId", "Capabilities", "RowVersion", "SourceEntityType", "SpecialServiceName", "ClientName" };
        foreach (var type in new[] { typeof(PortalOrderDto), typeof(PortalOrderListItemDto), typeof(PortalOrderEventDto), typeof(PortalOrderStopDto), typeof(PortalOrderPackageDto) })
            foreach (var name in forbidden) Assert.DoesNotContain(type.GetProperties(), p => p.Name == name);
        await using var f = await OrderReadFixture.CreateAsync();
        var dto = await PortalOf(f, f.C1.ClientId).GetOrderAsync(f.O1.PublicId, default);
        Assert.Equal(f.O1.WaybillNumber, dto.WaybillNumber);
        Assert.NotNull(dto.Delivery);
    }

    [Fact]
    public async Task The_summary_counts_only_the_orders_of_the_client_in_the_session()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var mine = await PortalOf(f, f.C1.ClientId).GetSummaryAsync(default);
        Assert.Equal(2, mine.TotalOrders);
        Assert.Equal(2, mine.ByStatus.Sum(s => s.Count));
        Assert.Equal(2, mine.RecentOrders.Count);
        Assert.DoesNotContain(mine.RecentOrders, o => o.PublicId == f.O3.PublicId || o.PublicId == f.O4.PublicId);
        var other = await PortalOf(f, f.C2.ClientId).GetSummaryAsync(default);
        Assert.DoesNotContain(other.RecentOrders, o => o.PublicId == f.O1.PublicId || o.PublicId == f.O2.PublicId);
    }

    [Fact]
    public async Task The_notice_inbox_shows_only_the_portal_notices_of_this_client_newest_first()
    {
        await using var f = await OrderReadFixture.CreateAsync();
        var set = f.Db.Set<Teikem.Domain.Clients.ClientNotification>();
        Teikem.Domain.Clients.ClientNotification N(int client, string subject, bool inPortal, int minutesAgo, int tenant = OrderReadFixture.TenantId) => new()
        {
            PublicId = Guid.NewGuid(), TenantId = tenant, ClientId = client, Kind = "DELIVERED", Subject = subject, BodyText = "texto", InPortal = inPortal, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
        };
        set.AddRange(N(f.C1.ClientId, "viejo", true, 60), N(f.C1.ClientId, "nuevo", true, 5), N(f.C1.ClientId, "solo correo", false, 1), N(f.C2.ClientId, "de otro cliente", true, 2));
        await f.Db.SaveChangesAsync();
        var page = await PortalOf(f, f.C1.ClientId).ListNotificationsAsync(0, 25, default);
        Assert.Equal(new[] { "nuevo", "viejo" }, page.Items.Select(n => n.Subject));
        Assert.Equal(2, page.Total);
        var paged = await PortalOf(f, f.C1.ClientId).ListNotificationsAsync(1, 1, default);
        Assert.Equal(new[] { "viejo" }, paged.Items.Select(n => n.Subject));
    }
}
