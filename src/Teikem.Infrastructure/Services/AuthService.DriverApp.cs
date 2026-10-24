using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>Ajustes de la app del chofer (sección <c>DriverApp</c> de appsettings; valen para todas las compañías).</summary>
public sealed class DriverAppOptions
{
    /// <summary>Tope de la sesión del chofer que cada compañía puede fijar (minutos). 30 días por omisión.</summary>
    public int MaxSessionMinutes { get; set; } = DriverSessionRules.DefaultMaxMinutes;
}

public sealed partial class AuthService
{
    private int DriverMaxSessionMinutes => driverAppOptions?.Value.MaxSessionMinutes is int m && m > 0 ? m : DriverSessionRules.DefaultMaxMinutes;

    /// <summary>Las compañías (de sus membresías activas) donde la persona es chofer activo.</summary>
    private async Task<List<int>> DriverTenantIdsAsync(int userId, IEnumerable<TenantOptionDto> memberships, CancellationToken ct)
    {
        var member = memberships.Select(m => m.TenantId).ToList();
        var driverIn = await db.Drivers.AsNoTracking().IgnoreQueryFilters().Where(d => d.UserId == userId && d.IsActive && member.Contains(d.TenantId)).Select(d => d.TenantId).ToListAsync(ct);
        return driverIn.Distinct().ToList();
    }

    /// <summary>
    /// Lote 213 — las compañías donde la persona puede usar la app móvil: donde tiene el permiso mobile.use o es chofer activo (así un chofer entra aunque su rol todavía no traiga el permiso nuevo).
    /// Devuelve, por compañía, sus permisos efectivos y si es chofer, para que la app decida qué mostrar sin otra llamada.
    /// </summary>
    private async Task<List<(int TenantId, IReadOnlyList<string> Permissions, bool IsDriver)>> MobileTenantsAsync(ApplicationUser user, IEnumerable<TenantOptionDto> memberships, CancellationToken ct)
    {
        var list = memberships.Select(m => m.TenantId).Distinct().ToList();
        var driverIn = (await DriverTenantIdsAsync(user.Id, list.Select(id => new TenantOptionDto(id, string.Empty, false)), ct)).ToHashSet();
        var result = new List<(int, IReadOnlyList<string>, bool)>();
        foreach (var tenantId in list)
        {
            var perms = (await permissions.GetEffectivePermissionsAsync(user.Id, tenantId, ct)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var isDriver = driverIn.Contains(tenantId);
            if (perms.Contains(PermissionCatalog.MobileUse) || isDriver) result.Add((tenantId, perms, isDriver));
        }
        return result;
    }

    /// <summary>Minutos de la sesión del chofer: la más permisiva entre las compañías donde es chofer (cada una con su valor propio o, sin él, su sesión general), con el tope de la aplicación.</summary>
    private async Task<int> DriverSessionMinutesAsync(int userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        var memberships = user is null ? new List<TenantOptionDto>() : await ActiveMembershipsAsync(user, ct);
        var tenantIds = user is null ? new List<int>() : (await MobileTenantsAsync(user, memberships, ct)).Select(x => x.TenantId).ToList();
        var companies = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => tenantIds.Contains(t.TenantId) && t.IsActive)
            .Select(t => new { t.DriverSessionMinutes, t.SessionMinutes, t.SessionDays }).ToListAsync(ct);
        return DriverSessionRules.Minutes(companies.Select(c => (c.DriverSessionMinutes, c.SessionMinutes, c.SessionDays)), DriverMaxSessionMinutes);
    }

    /// <summary>
    /// App del chofer: un token de acceso por cada compañía donde la persona es chofer, a partir de UNA sola sesión (no se cierra al pasar de una compañía a otra, como pasa con el cambio de compañía de la web).
    /// 403 si la sesión no es de la app del chofer. Una compañía que exige MFA (a toda la compañía o a esta persona) solo recibe token si esta sesión pasó el segundo factor: si no, `Blocked = mfa_required`.
    /// La duración de la sesión es la de <see cref="DriverSessionRules"/> y NO se alarga al renovar; aquí se emiten los tokens de acceso (de minutos) y se ROTA el refresh token.
    /// </summary>
    public async Task<DriverSessionDto> DriverCompanyTokensAsync(string refreshToken, CancellationToken ct)
    {
        var rt = await FindActiveAsync(refreshToken, ct);
        if (!rt.IsDriverApp) throw new ForbiddenException(DriverAuthMessages.NotADriverSessionMessage);
        var user = await users.FindByIdAsync(rt.UserId.ToString());
        if (user is null || !user.IsActive) throw new UnauthorizedException();
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var memberships = await ActiveMembershipsAsync(user, ct);
        var mobile = await MobileTenantsAsync(user, memberships, ct);
        var mobileIds = mobile.Select(x => x.TenantId).ToList();
        var tenants = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => mobileIds.Contains(t.TenantId) && t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        var memberMfa = (await db.UserTenants.AsNoTracking().IgnoreQueryFilters().Where(m => m.UserId == user.Id && m.MfaRequired).Select(m => m.TenantId).ToListAsync(ct)).ToHashSet();
        // Rotación (igual que /auth/refresh): el token usado se revoca y apunta al nuevo, que conserva el vencimiento ABSOLUTO de la sesión.
        var raw = JwtTokenService.NewRefreshToken();
        var next = new Teikem.Domain.Security.RefreshToken
        {
            UserId = rt.UserId, TenantId = rt.TenantId, TokenHash = JwtTokenService.HashToken(raw), DeviceInfo = rt.DeviceInfo, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = rt.ExpiresAtUtc,
            Aal2VerifiedAtUtc = rt.Aal2VerifiedAtUtc, IpAddress = RequestIp() ?? rt.IpAddress, IsDriverApp = true,
        };
        db.SuppressAudit = true;
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);
        rt.RevokedAtUtc = DateTime.UtcNow;
        rt.ReplacedByTokenHash = next.TokenHash;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        var result = new List<DriverCompanyTokenDto>();
        foreach (var t in tenants)
        {
            var mfaNeeded = t.MfaRequired || (!user.IsPlatformAdmin && memberMfa.Contains(t.TenantId));
            var info = mobile.First(x => x.TenantId == t.TenantId);
            if (mfaNeeded && rt.Aal2VerifiedAtUtc is null)
            {
                result.Add(new DriverCompanyTokenDto(t.TenantId, t.Name, null, null, DriverAuthMessages.MfaRequiredForCompany, info.Permissions, info.IsDriver));
                continue;
            }
            var (access, exp) = CreateAccess(user, t.TenantId, next.RefreshTokenId, kind, next.Aal2VerifiedAtUtc, null);
            result.Add(new DriverCompanyTokenDto(t.TenantId, t.Name, access, exp, null, info.Permissions, info.IsDriver));
        }
        return new DriverSessionDto(raw, next.ExpiresAtUtc, result, kind ?? UserKinds.Internal, kind == UserKinds.Driver);
    }
}
