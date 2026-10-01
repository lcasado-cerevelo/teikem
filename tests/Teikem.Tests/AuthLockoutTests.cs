using System.Text.Json;
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
/// Lote 8A (decisión 27) — POST /auth/reauth y PUT /auth/password comparten el bloqueo por cuenta de Identity (el mismo del
/// login): una contraseña equivocada cuenta, un acierto reinicia el contador, 5 fallos bloquean 15 minutos y, con la cuenta
/// bloqueada, la contraseña correcta responde como incorrecta (reauth 401 'Contraseña incorrecta.' sin AAL2; cambio de
/// contraseña 400 PasswordMismatch sin cambiar el hash) con SecurityEvent LOCKOUT stage = reauth / password_change.
/// Una sesión de aparato (abierta solo con PIN) llega a estas rutas: sin este bloqueo podría adivinar la contraseña sin límite.
/// </summary>
public class AuthLockoutTests
{
    private const string Password = "Clave-Segura1";

    private sealed class DetailSecurityEventWriter : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome, string Detail)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, detail is null ? "" : JsonSerializer.Serialize(detail)));
            return Task.CompletedTask;
        }
    }

    private static async Task<(WmsFixture F, long SessionId)> FixtureAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.Configure<IdentityOptions>(o => { o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); });
            s.AddSingleton<DetailSecurityEventWriter>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<DetailSecurityEventWriter>());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            // Primer ingreso (2026-09-30) apagado aquí: estas pruebas son del login de siempre (OnboardingTests lo prueba encendido).
            s.AddSingleton<Microsoft.Extensions.Options.IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<Teikem.Infrastructure.Abstractions.ITransactionalEmailSender, NoEmailSender>();
            s.AddSingleton<AuthService>();
        });
        var me = new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", FullName = "Yo Mismo", SecurityStamp = "s1", IsActive = true, LockoutEnabled = true };
        me.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(me, Password);
        f.Db.Users.Add(me);
        var session = new RefreshToken
        {
            UserId = 1, TenantId = WmsFixture.TenantId, TokenHash = Guid.NewGuid().ToString("N"), IssuedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        };
        f.Db.RefreshTokens.Add(session);
        // Catálogo que ReauthAsync consulta (¿tiene TOTP confirmado?) y que WmsFixture no siembra.
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = 950, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return (f, session.RefreshTokenId);
    }

    private static ApplicationUser User(WmsFixture f)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.Users.AsNoTracking().Single(u => u.Id == 1);
    }

    private static RefreshToken Session(WmsFixture f, long id)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Single(t => t.RefreshTokenId == id);
    }

    [Fact]
    public async Task Reauth_wrong_password_counts_toward_the_account_lockout_and_locked_account_rejects_the_right_one()
    {
        var (f, sessionId) = await FixtureAsync();
        await using var _ = f;
        var auth = f.Get<AuthService>();
        var writer = f.Get<DetailSecurityEventWriter>();

        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => auth.ReauthAsync(sessionId, new ReauthRequest("equivocada", null), default));
        Assert.Equal("Contraseña incorrecta.", wrong.Message);
        Assert.Equal(1, User(f).AccessFailedCount);

        // Un acierto antes del bloqueo reinicia el contador y fija AAL2.
        var ok = await auth.ReauthAsync(sessionId, new ReauthRequest(Password, null), default);
        Assert.NotNull(ok.AccessToken);
        Assert.Equal(0, User(f).AccessFailedCount);
        Assert.NotNull(Session(f, sessionId).Aal2VerifiedAtUtc);

        // Se limpia AAL2 para comprobar que el intento bloqueado no la vuelve a fijar.
        var rt = f.Db.RefreshTokens.IgnoreQueryFilters().Single(t => t.RefreshTokenId == sessionId);
        rt.Aal2VerifiedAtUtc = null;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() => auth.ReauthAsync(sessionId, new ReauthRequest("equivocada", null), default));
        Assert.True(User(f).LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));

        writer.Events.Clear();
        var locked = await Assert.ThrowsAsync<UnauthorizedException>(() => auth.ReauthAsync(sessionId, new ReauthRequest(Password, null), default));
        Assert.Equal("Contraseña incorrecta.", locked.Message);
        Assert.Equal(401, locked.StatusCode);
        Assert.Null(Session(f, sessionId).Aal2VerifiedAtUtc);
        Assert.Contains(writer.Events, e => e.EventType == SecurityEventTypes.Lockout && e.Outcome == SecurityOutcomes.Blocked && e.Detail.Contains("\"stage\":\"reauth\""));
    }

    [Fact]
    public async Task Change_password_wrong_current_counts_toward_the_account_lockout_and_locked_account_keeps_the_hash()
    {
        var (f, sessionId) = await FixtureAsync();
        await using var _ = f;
        var auth = f.Get<AuthService>();
        var writer = f.Get<DetailSecurityEventWriter>();
        var mismatch = f.Get<UserManager<ApplicationUser>>().ErrorDescriber.PasswordMismatch().Description;

        var wrong = await Assert.ThrowsAsync<ValidationException>(() => auth.ChangePasswordAsync(new ChangePasswordRequest("equivocada", "Otra-Clave22"), sessionId, default));
        Assert.Equal(400, wrong.StatusCode);
        Assert.Equal(new[] { mismatch }, wrong.Errors!["newPassword"]);
        Assert.Equal(1, User(f).AccessFailedCount);

        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<ValidationException>(() => auth.ChangePasswordAsync(new ChangePasswordRequest("equivocada", "Otra-Clave22"), sessionId, default));
        var lockedUser = User(f);
        Assert.True(lockedUser.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
        Assert.Contains(writer.Events, e => e.EventType == SecurityEventTypes.Lockout && e.Detail.Contains("\"stage\":\"password_change\""));
        var hashBefore = lockedUser.PasswordHash;

        writer.Events.Clear();
        var locked = await Assert.ThrowsAsync<ValidationException>(() => auth.ChangePasswordAsync(new ChangePasswordRequest(Password, "Otra-Clave22"), sessionId, default));
        Assert.Equal(new[] { mismatch }, locked.Errors!["newPassword"]);
        var after = User(f);
        Assert.Equal(hashBefore, after.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Failed, new PasswordHasher<ApplicationUser>().VerifyHashedPassword(after, after.PasswordHash!, "Otra-Clave22"));
        Assert.Contains(writer.Events, e => e.EventType == SecurityEventTypes.Lockout && e.Outcome == SecurityOutcomes.Blocked && e.Detail.Contains("\"stage\":\"password_change\""));
    }

    [Fact]
    public async Task Change_password_right_current_resets_the_counter()
    {
        var (f, sessionId) = await FixtureAsync();
        await using var _ = f;
        var auth = f.Get<AuthService>();

        await Assert.ThrowsAsync<ValidationException>(() => auth.ChangePasswordAsync(new ChangePasswordRequest("equivocada", "Otra-Clave22"), sessionId, default));
        await Assert.ThrowsAsync<ValidationException>(() => auth.ChangePasswordAsync(new ChangePasswordRequest("equivocada", "Otra-Clave22"), sessionId, default));
        Assert.Equal(2, User(f).AccessFailedCount);

        await auth.ChangePasswordAsync(new ChangePasswordRequest(Password, "Otra-Clave22"), sessionId, default);
        var user = User(f);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, user.PasswordHash!, "Otra-Clave22"));
    }
}
