using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// App del chofer (2026-10-23): acceso. El chofer «solo app» no entra a la web; la app solo deja entrar a quien es chofer activo en alguna compañía; el challenge del MFA de la app lleva app=driver; una sesión
/// da acceso a todas las compañías donde es chofer y cada compañía que exige MFA lo exige. (Emitir la sesión usa ExecuteUpdate, que el proveedor InMemory no traduce: ese tramo se prueba aparte y contra la base real.)
/// </summary>
public partial class AuthMfaTests
{
    private static async Task MakeDriverAsync(WmsFixture f, int tenantId, bool driver = true, bool member = true)
    {
        var activeStatusId = await f.Db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => s.StatusCodeId).SingleAsync();
        if (member && !await f.Db.UserTenants.IgnoreQueryFilters().AnyAsync(m => m.UserId == 1 && m.TenantId == tenantId))
            f.Db.UserTenants.Add(new UserTenant { UserId = 1, TenantId = tenantId, StatusCodeId = activeStatusId });
        if (driver) f.Db.Drivers.Add(new Driver { DriverId = 70 + tenantId, TenantId = tenantId, EmployeeCode = "D" + tenantId, FullName = "Chofer", UserId = 1, StatusCodeId = 1, IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    private static async Task SetKindAsync(WmsFixture f, string kind)
    {
        var id = 960;
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = id, Entity = LookupDomains.UserKind, InternalCode = kind, LabelJson = "{\"es\":\"x\"}", IsActive = true });
        await f.Db.SaveChangesAsync();
        (await f.Db.Users.SingleAsync(u => u.Id == 1)).UserKindLookupId = id;
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task A_driver_only_user_is_rejected_by_the_web_login()
    {
        await using var f = await FixtureAsync();
        await SetKindAsync(f, UserKinds.Driver);
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default));
        Assert.Equal(DriverAuthMessages.UseTheAppMessage, ex.Message);
    }

    [Fact]
    public async Task The_driver_app_rejects_someone_who_is_not_an_active_driver_anywhere()
    {
        await using var f = await FixtureAsync();
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default, driverApp: true));
        Assert.Equal(DriverAppRules.NotADriverMessage, ex.Message);
    }

    [Fact]
    public async Task The_mfa_challenge_of_the_driver_app_carries_app_driver_and_the_web_one_does_not()
    {
        await using var f = await FixtureAsync();
        await MakeDriverAsync(f, WmsFixture.TenantId);   // la compañía exige MFA por omisión: el login para en el paso del código
        var app = await f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default, driverApp: true);
        Assert.Equal("mfa_required", app.Status);
        Assert.Equal(TeikemClaims.AppDriver, new JwtSecurityTokenHandler().ReadJwtToken(app.MfaChallengeToken).Claims.First(c => c.Type == TeikemClaims.App).Value);

        var web = await f.Get<AuthService>().LoginAsync(new LoginRequest("yo@t.local", Password, null, null), default);
        Assert.Equal("mfa_required", web.Status);
        Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(web.MfaChallengeToken).Claims, c => c.Type == TeikemClaims.App);
    }

    private static async Task<string> DriverSessionAsync(WmsFixture f, DateTime? aal2)
    {
        var raw = JwtTokenService.NewRefreshToken();
        f.Db.RefreshTokens.Add(new RefreshToken { UserId = 1, TenantId = WmsFixture.TenantId, TokenHash = JwtTokenService.HashToken(raw), IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30), Aal2VerifiedAtUtc = aal2, IsDriverApp = true });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return raw;
    }

    [Fact]
    public async Task One_driver_session_gives_a_token_per_company_and_each_company_that_requires_mfa_needs_the_session_to_have_passed_it()
    {
        await using var f = await FixtureAsync();
        await MakeDriverAsync(f, WmsFixture.TenantId);
        await MakeDriverAsync(f, WmsFixture.OtherTenantId);
        var other = await f.Db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.TenantId == WmsFixture.OtherTenantId);
        other.MfaRequired = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var withoutMfa = (await f.Get<AuthService>().DriverCompanyTokensAsync(await DriverSessionAsync(f, null), default)).Companies;
        Assert.Equal(2, withoutMfa.Count);
        Assert.Equal(DriverAuthMessages.MfaRequiredForCompany, withoutMfa.Single(t => t.TenantId == WmsFixture.TenantId).Blocked);
        Assert.Null(withoutMfa.Single(t => t.TenantId == WmsFixture.TenantId).AccessToken);
        Assert.NotNull(withoutMfa.Single(t => t.TenantId == WmsFixture.OtherTenantId).AccessToken);

        var withMfa = (await f.Get<AuthService>().DriverCompanyTokensAsync(await DriverSessionAsync(f, DateTime.UtcNow), default)).Companies;
        Assert.All(withMfa, t => Assert.NotNull(t.AccessToken));
    }

    [Fact]
    public async Task A_session_that_is_not_from_the_driver_app_cannot_ask_for_company_tokens_and_companies_where_he_is_not_a_driver_do_not_appear()
    {
        await using var f = await FixtureAsync();
        await MakeDriverAsync(f, WmsFixture.TenantId);
        await MakeDriverAsync(f, WmsFixture.OtherTenantId, driver: false);   // miembro de la otra compañía, pero no chofer allí
        var raw = JwtTokenService.NewRefreshToken();
        f.Db.RefreshTokens.Add(new RefreshToken { UserId = 1, TenantId = WmsFixture.TenantId, TokenHash = JwtTokenService.HashToken(raw), IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1), IsDriverApp = false });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Get<AuthService>().DriverCompanyTokensAsync(raw, default));

        var list = (await f.Get<AuthService>().DriverCompanyTokensAsync(await DriverSessionAsync(f, DateTime.UtcNow), default)).Companies;
        Assert.Equal(new[] { WmsFixture.TenantId }, list.Select(t => t.TenantId));
    }

    [Fact]
    public async Task Renewing_the_driver_session_rotates_the_refresh_token_keeps_the_absolute_expiry_and_reusing_the_old_one_closes_the_session()
    {
        await using var f = await FixtureAsync();
        await MakeDriverAsync(f, WmsFixture.TenantId);
        var old = await DriverSessionAsync(f, DateTime.UtcNow);
        var expiresBefore = (await f.Db.RefreshTokens.AsNoTracking().SingleAsync()).ExpiresAtUtc;

        var renewed = await f.Get<AuthService>().DriverCompanyTokensAsync(old, default);

        Assert.NotEqual(old, renewed.RefreshToken);
        Assert.Equal(expiresBefore, renewed.RefreshExpiresAtUtc);
        var rows = await f.Db.RefreshTokens.AsNoTracking().OrderBy(r => r.RefreshTokenId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RevokedAtUtc);
        Assert.True(rows[1].IsDriverApp);
        // el token viejo ya no sirve y, como se rotó, reusarlo es señal de robo: se cierra TODA la cadena
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Get<AuthService>().DriverCompanyTokensAsync(old, default));
        Assert.All(await f.Db.RefreshTokens.AsNoTracking().ToListAsync(), r => Assert.NotNull(r.RevokedAtUtc));
    }
}
