using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Cambio 1 de las decisiones del dueño (2026-10-03): revocar una sesión cierra SOLO esa sesión. Un refresh token revocado a
/// propósito (administrador, la persona desde Mi cuenta, "cerrar las demás", logout) falla con 401 sin cascada; la revocación en
/// cascada de toda la cadena del usuario queda solo para el reuso de un token ya ROTADO (ReplacedByTokenHash lleno).
/// </summary>
public class RefreshRevocationTests
{
    private sealed record Seed(WmsFixture F, string RawMine, long MineId, string RawOld, long OldId, long OtherDevice, long OtherTenantMine, long AnaId);

    private static async Task<Seed> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.AddSingleton<CapturingWriter>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<CapturingWriter>());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<ITransactionalEmailSender, NoEmailSender>();
            s.AddSingleton<AuthService>();
            s.AddSingleton<CompanySessionService>();
        });
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "yo@t.local", Email = "yo@t.local", SecurityStamp = "s1", IsActive = true });
        f.Db.Users.Add(new ApplicationUser { Id = 2, UserName = "ana@t.local", Email = "ana@t.local", SecurityStamp = "s2", IsActive = true });
        var now = DateTime.UtcNow;
        string raw1 = "raw-mine", raw2 = "raw-old";
        RefreshToken Tok(int user, int tenant, string? raw = null, DateTime? revoked = null, string? replaced = null) => new()
        {
            UserId = user, TenantId = tenant, TokenHash = JwtTokenService.HashToken(raw ?? Guid.NewGuid().ToString("N")), IssuedAtUtc = now,
            ExpiresAtUtc = now.AddDays(30), RevokedAtUtc = revoked, ReplacedByTokenHash = replaced,
        };
        var mine = Tok(1, WmsFixture.TenantId, raw1);
        var old = Tok(1, WmsFixture.TenantId, raw2);               // el aparato que el administrador revocará
        var other = Tok(1, WmsFixture.TenantId);
        var ana = Tok(2, WmsFixture.TenantId);
        f.Db.RefreshTokens.AddRange(mine, old, other, ana);
        await f.Db.SaveChangesAsync();
        var otherTenantMine = Tok(1, WmsFixture.OtherTenantId);
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            f.Db.RefreshTokens.Add(otherTenantMine);
            await f.Db.SaveChangesAsync();
        }
        f.Db.ChangeTracker.Clear();
        return new Seed(f, raw1, mine.RefreshTokenId, raw2, old.RefreshTokenId, other.RefreshTokenId, otherTenantMine.RefreshTokenId, ana.RefreshTokenId);
    }

    private sealed class CapturingWriter : ISecurityEventWriter
    {
        public List<string> Reasons { get; } = new();
        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Reasons.Add(detail is null ? "" : System.Text.Json.JsonSerializer.Serialize(detail));
            return Task.CompletedTask;
        }
    }

    private static bool Alive(WmsFixture f, long id)
    {
        f.Db.ChangeTracker.Clear();
        return f.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Single(t => t.RefreshTokenId == id).RevokedAtUtc == null;
    }

    private static async Task AssertPlain401AndNoCascade(Seed s, string raw, params long[] stillAlive)
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => s.F.Get<AuthService>().RefreshAsync(raw, null, default));
        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("Refresh token inválido.", ex.Message);
        foreach (var id in stillAlive) Assert.True(Alive(s.F, id), $"la sesión {id} debía seguir viva");
        Assert.DoesNotContain(s.F.Get<CapturingWriter>().Reasons, r => r.Contains("refresh_reuse"));
    }

    [Fact]
    public async Task Reuse_of_a_rotated_token_still_revokes_the_whole_chain_of_the_user()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        // Simula la rotación: el token viejo quedó revocado y apuntando al nuevo.
        var old = s.F.Db.RefreshTokens.IgnoreQueryFilters().Single(t => t.RefreshTokenId == s.OldId);
        old.RevokedAtUtc = DateTime.UtcNow; old.ReplacedByTokenHash = "hash-del-nuevo";
        await s.F.Db.SaveChangesAsync();
        s.F.Db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<UnauthorizedException>(() => s.F.Get<AuthService>().RefreshAsync(s.RawOld, null, default));

        Assert.False(Alive(s.F, s.MineId));
        Assert.False(Alive(s.F, s.OtherDevice));
        Assert.False(Alive(s.F, s.OtherTenantMine));     // todas sus compañías
        Assert.True(Alive(s.F, s.AnaId));                // otra persona no se toca
        Assert.Contains(s.F.Get<CapturingWriter>().Reasons, r => r.Contains("refresh_reuse"));
    }

    [Fact]
    public async Task Admin_revoking_one_session_makes_that_refresh_fail_without_cascade()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        // El administrador (usuario 1, sesión MineId) revoca la sesión OldId de su compañía.
        await s.F.Get<CompanySessionService>().RevokeAsync(s.OldId, s.MineId, default);
        Assert.False(Alive(s.F, s.OldId));

        await AssertPlain401AndNoCascade(s, s.RawOld, s.MineId, s.OtherDevice, s.OtherTenantMine, s.AnaId);
    }

    [Fact]
    public async Task User_revoking_one_of_their_sessions_from_my_account_does_not_cascade()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await s.F.Get<AuthService>().RevokeSessionAsync(s.OldId, s.MineId, default);

        await AssertPlain401AndNoCascade(s, s.RawOld, s.MineId, s.OtherDevice, s.OtherTenantMine, s.AnaId);
    }

    [Fact]
    public async Task Close_the_others_makes_each_old_refresh_fail_without_cascade_and_keeps_the_current_alive()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await s.F.Get<CompanySessionService>().RevokeOthersAsync(s.MineId, default);
        Assert.False(Alive(s.F, s.OldId));
        Assert.False(Alive(s.F, s.AnaId));

        // La sesión propia sigue viva y la de otra compañía (no es de esta compañía) no se tocó.
        await AssertPlain401AndNoCascade(s, s.RawOld, s.MineId, s.OtherTenantMine);
    }

    [Fact]
    public async Task Logout_then_refresh_with_that_token_is_a_plain_401()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await s.F.Get<AuthService>().LogoutAsync(s.RawOld, default);
        Assert.False(Alive(s.F, s.OldId));

        await AssertPlain401AndNoCascade(s, s.RawOld, s.MineId, s.OtherDevice, s.OtherTenantMine, s.AnaId);
    }

    [Fact]
    public async Task Revoked_session_of_one_company_does_not_touch_the_same_person_in_another_company()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await s.F.Get<CompanySessionService>().RevokeAsync(s.OldId, s.MineId, default);
        await AssertPlain401AndNoCascade(s, s.RawOld, s.OtherTenantMine);
    }
}
