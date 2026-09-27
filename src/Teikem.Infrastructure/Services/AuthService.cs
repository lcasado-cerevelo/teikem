using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Verificación contra brechas conocidas (HIBP k-anonimato). Por defecto no-op; se conecta cuando haya salida a Internet.</summary>
public interface IPasswordBreachChecker
{
    Task<bool> IsBreachedAsync(string password, CancellationToken ct);
}

public sealed class NoOpPasswordBreachChecker : IPasswordBreachChecker
{
    public Task<bool> IsBreachedAsync(string password, CancellationToken ct) => Task.FromResult(false);
}

/// <summary>Claims propios de las sesiones de aparato (Lote 8A).</summary>
public static class DeviceClaims
{
    /// <summary>PublicId del aparato (UserDevice) que emitió la sesión; solo en tokens de aparato.</summary>
    public const string DeviceId = "did";

    /// <summary>
    /// Clave de caché (IMemoryCache, 60 s) del estado activo del aparato que valida cada access token con `did` en
    /// OnTokenValidated; DeviceService la borra al desactivar para que el corte sea inmediato en esa instancia.
    /// </summary>
    public static string ActiveCacheKey(Guid devicePublicId) => $"dev:{devicePublicId}";
}

/// <summary>
/// Capa D: identidad y sesiones. Login con Identity (hash, lockout), tenant activo al login → JWT; refresh tokens hasheados
/// con rotación; revocación por dispositivo o global (SecurityStamp invalida access tokens vivos); MFA TOTP + códigos de
/// recuperación; step-up AAL2 (reauth). Todo evento entra a SecurityEvent.
/// Lote 8A (P1): login por aparato de confianza + PIN (sin contraseña ni MFA): la sesión queda ligada al aparato
/// (RefreshToken.UserDeviceId), dura Tenant.DeviceSessionDays días y se renueva en cada refresh; el access token lleva el
/// claim `did` con el PublicId del aparato y los mismos `tid`/permisos del usuario. Si el aparato se desactiva, el refresh
/// se rechaza con 401.
/// </summary>
public sealed class AuthService(
    TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, JwtTokenService jwt, ILookupCache lookups,
    ISecurityEventWriter security, IDataProtectionProvider dataProtection, IPasswordBreachChecker breachChecker, PermissionService permissions,
    DeviceService devices, PinService pins)
{
    private const string InvalidCredentials = "Credenciales inválidas.";
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Teikem.Mfa.Totp");

    // ---------------- Login ----------------

    public async Task<AuthResultDto> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
        if (user is null || !user.IsActive)
        {
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Failure, null, null, new { email = req.Email }, ct);
            throw new UnauthorizedException(InvalidCredentials); // sin enumeración de usuarios
        }
        if (await users.IsLockedOutAsync(user))
        {
            await security.WriteAsync(SecurityEventTypes.Lockout, SecurityOutcomes.Blocked, user.Id, null, null, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }
        if (!await users.CheckPasswordAsync(user, req.Password ?? string.Empty))
        {
            await users.AccessFailedAsync(user);
            var locked = await users.IsLockedOutAsync(user);
            await security.WriteAsync(locked ? SecurityEventTypes.Lockout : SecurityEventTypes.Login, locked ? SecurityOutcomes.Blocked : SecurityOutcomes.Failure, user.Id, null, null, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }
        await users.ResetAccessFailedCountAsync(user);
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode ?? UserKinds.Internal;
        if (kind == UserKinds.Portal) throw new UnauthorizedException("Los usuarios de portal se autentican en el portal de clientes.");

        // Tenant activo: pedido → default → única membresía → selección
        var memberships = await ActiveMembershipsAsync(user, ct);
        int tenantId;
        if (req.TenantId.HasValue)
        {
            if (!memberships.Any(m => m.TenantId == req.TenantId.Value)) throw new ForbiddenException("No pertenece a esa compañía.");
            tenantId = req.TenantId.Value;
        }
        else if (user.DefaultTenantId.HasValue && memberships.Any(m => m.TenantId == user.DefaultTenantId.Value)) tenantId = user.DefaultTenantId.Value;
        else if (memberships.Count == 1) tenantId = memberships[0].TenantId;
        else if (memberships.Count == 0) { await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, null, new { reason = "no_membership" }, ct); throw new ForbiddenException("El usuario no tiene ninguna compañía activa."); }
        else return new AuthResultDto("tenant_selection", null, null, false, memberships);

        var t = await db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstAsync(x => x.TenantId == tenantId, ct);
        if (!t.IsActive) throw new ForbiddenException("La compañía está inactiva.");

        // MFA
        var totp = await ConfirmedTotpAsync(user.Id, ct);
        if (totp is not null || t.MfaRequired)
        {
            var (challenge, _) = jwt.CreateMfaChallengeToken(user, tenantId, req.DeviceInfo);
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "password", mfa = totp is not null ? "required" : "enrollment_required" }, ct);
            return new AuthResultDto("mfa_required", null, challenge, totp is null, null);
        }

        var pair = await IssueAsync(user, tenantId, kind, req.DeviceInfo, aal2At: null, ct);
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { device = req.DeviceInfo }, ct);
        return new AuthResultDto("ok", pair, null, false, null);
    }

    /// <summary>Segundo paso del login: código TOTP o código de recuperación, con el challenge token del paso 1.</summary>
    public async Task<AuthResultDto> VerifyMfaAsync(int userId, int tenantId, string? deviceInfo, MfaVerifyRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        if (!await VerifyCodeAsync(user, req.Code, ct))
        {
            await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Failure, user.Id, tenantId, null, ct);
            await users.AccessFailedAsync(user);
            throw new UnauthorizedException("Código MFA inválido.");
        }
        await users.ResetAccessFailedCountAsync(user);
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var pair = await IssueAsync(user, tenantId, kind, req.DeviceInfo ?? deviceInfo, aal2At: DateTime.UtcNow, ct);
        await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Success, user.Id, tenantId, null, ct);
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "mfa" }, ct);
        return new AuthResultDto("ok", pair, null, false, null);
    }

    private async Task<List<TenantOptionDto>> ActiveMembershipsAsync(ApplicationUser user, CancellationToken ct)
    {
        var active = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", MembershipStatuses.Active);
        if (user.IsPlatformAdmin)
        {
            var all = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
            return all.Select(t => new TenantOptionDto(t.TenantId, t.Name, t.TenantId == user.DefaultTenantId)).ToList();
        }
        return await db.UserTenants.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id && m.StatusCodeId == active && m.Tenant!.IsActive)
            .Select(m => new TenantOptionDto(m.TenantId, m.Tenant!.Name, m.IsDefault)).ToListAsync(ct);
    }

    /// <summary>Aparato al que queda ligada una sesión (Lote 8A): Id para RefreshToken.UserDeviceId, PublicId para `did`.</summary>
    private sealed record DeviceBinding(int UserDeviceId, Guid PublicId, int SessionDays);

    private async Task<TokenPairDto> IssueAsync(ApplicationUser user, int tenantId, string? kind, string? deviceInfo, DateTime? aal2At, CancellationToken ct, DeviceBinding? device = null)
    {
        var t = await db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstAsync(x => x.TenantId == tenantId, ct);
        var raw = JwtTokenService.NewRefreshToken();
        var rt = new RefreshToken
        {
            UserId = user.Id, TenantId = tenantId, TokenHash = JwtTokenService.HashToken(raw), DeviceInfo = deviceInfo is { Length: > 200 } d ? d[..200] : deviceInfo,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(device?.SessionDays ?? t.SessionDays), Aal2VerifiedAtUtc = aal2At,
        };
        db.SuppressAudit = true;
        db.RefreshTokens.Add(rt);
        if (device is not null) SetTokenDevice(rt, device.UserDeviceId);
        user.LastLoginUtc = DateTime.UtcNow;
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        var (access, exp) = CreateAccess(user, tenantId, rt.RefreshTokenId, kind, aal2At, device?.PublicId);
        return new TokenPairDto(access, exp, raw, rt.ExpiresAtUtc, tenantId);
    }

    // ---------------- Login por aparato (Lote 8A) ----------------

    /// <summary>
    /// Aparato registrado + secreto + usuario + PIN → par de tokens de sesión de aparato. El usuario debe ser interno,
    /// activo, con membresía ACTIVE en la compañía del aparato y con inventory.view (los mismos de /auth/device/users).
    /// Aparato inválido → 401 'El aparato no está registrado o fue desactivado.'; PIN incorrecto → 401 'PIN incorrecto.'
    /// (cuenta el intento); bloqueado → 423 'PIN bloqueado por 15 minutos.'. Escribe SecurityEvent LOGIN (stage = device).
    /// </summary>
    public async Task<TokenPairDto> DeviceLoginAsync(DeviceLoginRequest req, CancellationToken ct)
    {
        var device = await devices.AuthenticateAsync(req.DevicePublicId, req.DeviceSecret, "login", ct);
        var tenantId = device.TenantId;
        // Sin el usuario de un bearer ajeno que venga en la petición (flujo anónimo del aparato).
        using var scope = ((TenantContext)tenant).AsAnonymous(tenantId);

        var user = req.UserId > 0 ? await users.FindByIdAsync(req.UserId.ToString()) : null;
        var kind = user?.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode ?? UserKinds.Internal;
        // El admin de plataforma nunca entra por aparato (sin contraseña ni MFA abriría todas las compañías).
        if (user is null || !user.IsActive || user.IsPlatformAdmin || kind == UserKinds.Portal || !(await ActiveMembershipsAsync(user, ct)).Any(m => m.TenantId == tenantId))
        {
            // Sin enumeración de usuarios: el mismo 401 que un PIN incorrecto.
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Failure, user?.Id, tenantId, new { stage = "device", device = device.Code, reason = "user" }, ct);
            throw new UnauthorizedException(PinService.WrongPinMessage);
        }

        await pins.VerifyForLoginAsync(user, tenantId, req.Pin, device.Code, ct);

        if (!user.IsPlatformAdmin && !(await permissions.GetEffectivePermissionsAsync(user.Id, tenantId, ct)).Contains(PermissionCatalog.InventoryView))
        {
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, tenantId, new { stage = "device", device = device.Code, permission = PermissionCatalog.InventoryView }, ct);
            throw new ForbiddenException($"Falta el permiso '{PermissionCatalog.InventoryView}'.");
        }

        // El aparato se toca ANTES de emitir: si el touch fallara no quedaría un refresh token emitido que nadie recibió.
        await devices.TouchAsync(device, user.Id, null, ct);
        var binding = new DeviceBinding(device.UserDeviceId, device.PublicId, await devices.SessionDaysAsync(tenantId, ct));
        var pair = await IssueAsync(user, tenantId, kind, DeviceInfoOf(device), aal2At: null, ct, binding);
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, tenantId, new { stage = "device", device = device.Code }, ct);
        return pair;
    }

    private static string DeviceInfoOf(UserDevice device)
        => string.IsNullOrWhiteSpace(device.Model) ? $"Aparato {device.Code}" : $"Aparato {device.Code} · {device.Model}";

    /// <summary>RefreshToken.UserDeviceId (columna de P0) por nombre: la sesión queda ligada al aparato.</summary>
    private void SetTokenDevice(RefreshToken rt, int? userDeviceId)
        => db.Entry(rt).Property<int?>(DeviceService.RefreshTokenDeviceColumn).CurrentValue = userDeviceId;

    private int? TokenDevice(RefreshToken rt)
        => db.Entry(rt).Property<int?>(DeviceService.RefreshTokenDeviceColumn).CurrentValue;

    /// <summary>Aparato de una sesión: null si no es de aparato; 401 (y revoca la sesión) si el aparato ya no sirve.</summary>
    private async Task<UserDevice?> SessionDeviceAsync(RefreshToken rt, CancellationToken ct)
    {
        if (TokenDevice(rt) is not int deviceId) return null;
        var device = await devices.FindByIdAsync(deviceId, ct);
        if (device is not null && device.IsActive && device.TenantId == rt.TenantId) return device;
        await RevokeAsync(rt, null, ct);
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Blocked, rt.UserId, rt.TenantId, new { reason = "device_inactive", device = device?.Code }, ct);
        throw new UnauthorizedException(DeviceService.InvalidDeviceMessage);
    }

    /// <summary>
    /// Access token del usuario; en sesiones de aparato agrega el claim `did`. Se firma con la misma llave, emisor,
    /// audiencia y vigencia que JwtTokenService (se re-firma su token con el claim extra para no duplicar la lista de claims).
    /// </summary>
    private (string Token, DateTime ExpiresAtUtc) CreateAccess(ApplicationUser user, int tenantId, long sessionId, string? kind, DateTime? aal2At, Guid? devicePublicId)
    {
        var (token, exp) = jwt.CreateAccessToken(user, tenantId, sessionId, kind, aal2At);
        if (devicePublicId is not Guid did) return (token, exp);
        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);
        var registered = new HashSet<string>(StringComparer.Ordinal)
            { JwtRegisteredClaimNames.Iss, JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat };
        var claims = parsed.Claims.Where(c => !registered.Contains(c.Type)).Select(c => new Claim(c.Type, c.Value)).ToList();
        claims.Add(new Claim(DeviceClaims.DeviceId, did.ToString()));
        var signed = new JwtSecurityToken(parsed.Issuer, parsed.Audiences.FirstOrDefault(), claims, parsed.ValidFrom, parsed.ValidTo,
            new SigningCredentials(jwt.SigningKey, SecurityAlgorithms.HmacSha256));
        return (handler.WriteToken(signed), exp);
    }

    // ---------------- Refresh / logout / switch ----------------

    public async Task<TokenPairDto> RefreshAsync(string refreshToken, string? deviceInfo, CancellationToken ct)
    {
        var rt = await FindActiveAsync(refreshToken, ct);
        var user = await users.FindByIdAsync(rt.UserId.ToString());
        if (user is null || !user.IsActive) throw new UnauthorizedException();
        var memberships = await ActiveMembershipsAsync(user, ct);
        if (!memberships.Any(m => m.TenantId == rt.TenantId)) { await RevokeAsync(rt, null, ct); throw new ForbiddenException("La membresía ya no está activa."); }
        // Sesión de aparato (Lote 8A): el aparato debe seguir activo; la vigencia se renueva (DeviceSessionDays desde hoy).
        var device = await SessionDeviceAsync(rt, ct);
        var expires = device is null ? rt.ExpiresAtUtc : DateTime.UtcNow.AddDays(await devices.SessionDaysAsync(rt.TenantId, ct));

        // Rotación: el token usado se revoca y apunta al nuevo
        var raw = JwtTokenService.NewRefreshToken();
        var next = new RefreshToken
        {
            UserId = rt.UserId, TenantId = rt.TenantId, TokenHash = JwtTokenService.HashToken(raw),
            DeviceInfo = device is null ? deviceInfo ?? rt.DeviceInfo : rt.DeviceInfo,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = expires, Aal2VerifiedAtUtc = rt.Aal2VerifiedAtUtc,
        };
        db.SuppressAudit = true;
        db.RefreshTokens.Add(next);
        if (device is not null) SetTokenDevice(next, device.UserDeviceId);
        rt.RevokedAtUtc = DateTime.UtcNow;
        rt.ReplacedByTokenHash = next.TokenHash;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var (access, exp) = CreateAccess(user, rt.TenantId, next.RefreshTokenId, kind, next.Aal2VerifiedAtUtc, device?.PublicId);
        return new TokenPairDto(access, exp, raw, next.ExpiresAtUtc, rt.TenantId);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var rt = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == JwtTokenService.HashToken(refreshToken), ct);
        if (rt is null) return;
        await RevokeAsync(rt, null, ct);
        await security.WriteAsync(SecurityEventTypes.Logout, SecurityOutcomes.Success, rt.UserId, rt.TenantId, null, ct);
    }

    /// <summary>Cierra todas las sesiones del usuario (todos los dispositivos y compañías) e invalida access tokens vivos.</summary>
    public async Task LogoutAllAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var tokens = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null) await users.UpdateSecurityStampAsync(user);
        db.SuppressAudit = true;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, userId, tenant.TenantId, new { scope = "all", count = tokens.Count }, ct);
    }

    public async Task<TokenPairDto> SwitchTenantAsync(SwitchTenantRequest req, CancellationToken ct)
    {
        var rt = await FindActiveAsync(req.RefreshToken, ct);
        if (TokenDevice(rt) is not null) throw new ForbiddenException("La sesión de un aparato no cambia de compañía.");
        var user = await users.FindByIdAsync(rt.UserId.ToString()) ?? throw new UnauthorizedException();
        var memberships = await ActiveMembershipsAsync(user, ct);
        if (!memberships.Any(m => m.TenantId == req.TenantId)) throw new ForbiddenException("No pertenece a esa compañía.");
        await RevokeAsync(rt, null, ct);
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var pair = await IssueAsync(user, req.TenantId, kind, rt.DeviceInfo, rt.Aal2VerifiedAtUtc, ct);
        await security.WriteAsync(SecurityEventTypes.TenantSwitch, SecurityOutcomes.Success, user.Id, req.TenantId, new { from = rt.TenantId }, ct);
        return pair;
    }

    private async Task<RefreshToken> FindActiveAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedException("Refresh token inválido.");
        var hash = JwtTokenService.HashToken(refreshToken);
        var rt = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is null) throw new UnauthorizedException("Refresh token inválido.");
        if (rt.RevokedAtUtc is not null)
        {
            // Reutilización de un token ya rotado = posible robo: se revoca toda la cadena del usuario.
            await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Blocked, rt.UserId, rt.TenantId, new { reason = "refresh_reuse" }, ct);
            var chain = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == rt.UserId && t.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var t in chain) t.RevokedAtUtc = DateTime.UtcNow;
            db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
            throw new UnauthorizedException("Refresh token inválido.");
        }
        if (rt.ExpiresAtUtc <= DateTime.UtcNow) throw new UnauthorizedException("Sesión expirada.");
        return rt;
    }

    private async Task RevokeAsync(RefreshToken rt, string? replacedBy, CancellationToken ct)
    {
        rt.RevokedAtUtc = DateTime.UtcNow;
        rt.ReplacedByTokenHash = replacedBy;
        db.SuppressAudit = true;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
    }

    // ---------------- Sesiones ----------------

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(long currentSessionId, CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var list = await db.RefreshTokens.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.IssuedAtUtc).ToListAsync(ct);
        return list.Select(t => new SessionDto(t.RefreshTokenId, t.DeviceInfo, t.IssuedAtUtc, t.ExpiresAtUtc, t.Aal2VerifiedAtUtc, t.RefreshTokenId == currentSessionId)).ToList();
    }

    public async Task RevokeSessionAsync(long sessionId, long currentSessionId, CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        if (sessionId == currentSessionId) throw new ConflictException("La sesión actual no se revoca desde la lista; use logout.");
        var rt = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.RefreshTokenId == sessionId && t.UserId == userId, ct) ?? throw new NotFoundException("Sesión", sessionId);
        await RevokeAsync(rt, null, ct);
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, userId, tenant.TenantId, new { session = sessionId }, ct);
    }

    /// <summary>Admin: revocar todas las sesiones de otro usuario del tenant (requiere admin.users).</summary>
    public async Task RevokeUserSessionsAsync(int userId, CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.AdminUsers, ct);
        var tokens = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId && t.TenantId == tenant.TenantId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null) await users.UpdateSecurityStampAsync(user);
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Success, userId, tenant.TenantId, new { by = tenant.UserId, count = tokens.Count }, ct);
    }

    // ---------------- Step-up AAL2 ----------------

    public async Task<ReauthResultDto> ReauthAsync(long sessionId, ReauthRequest req, CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        if (!await users.CheckPasswordAsync(user, req.Password ?? ""))
        {
            await security.WriteAsync(SecurityEventTypes.Reauth, SecurityOutcomes.Failure, userId, tenant.TenantId, null, ct);
            throw new UnauthorizedException("Contraseña incorrecta.");
        }
        if (await ConfirmedTotpAsync(userId, ct) is not null && !await VerifyCodeAsync(user, req.MfaCode, ct))
        {
            await security.WriteAsync(SecurityEventTypes.Reauth, SecurityOutcomes.Failure, userId, tenant.TenantId, new { reason = "mfa" }, ct);
            throw new UnauthorizedException("Código MFA inválido.");
        }
        var rt = await db.RefreshTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.RefreshTokenId == sessionId && t.UserId == userId && t.RevokedAtUtc == null, ct)
                 ?? throw new UnauthorizedException("Sesión inválida.");
        var device = await SessionDeviceAsync(rt, ct);
        rt.Aal2VerifiedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var (access, exp) = CreateAccess(user, rt.TenantId, rt.RefreshTokenId, kind, rt.Aal2VerifiedAtUtc, device?.PublicId);
        await security.WriteAsync(SecurityEventTypes.Reauth, SecurityOutcomes.Success, userId, tenant.TenantId, null, ct);
        return new ReauthResultDto(access, exp, rt.Aal2VerifiedAtUtc.Value);
    }

    // ---------------- Contraseña ----------------

    public async Task ChangePasswordAsync(ChangePasswordRequest req, long currentSessionId, CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        if (await breachChecker.IsBreachedAsync(req.NewPassword ?? "", ct)) throw new ValidationException("newPassword", "Esta contraseña aparece en brechas conocidas; elija otra.");
        var result = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
        if (!result.Succeeded)
        {
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, userId, tenant.TenantId, new { errors = result.Errors.Select(e => e.Code) }, ct);
            throw new ValidationException("newPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        // Cambio de contraseña: se cierran las demás sesiones (Identity ya rotó el SecurityStamp).
        var others = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.RefreshTokenId != currentSessionId).ToListAsync(ct);
        foreach (var t in others) t.RevokedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, userId, tenant.TenantId, null, ct);
    }

    // ---------------- MFA (TOTP + recuperación) ----------------

    public async Task<MfaEnrollResultDto> EnrollTotpAsync(int userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var typeId = await lookups.GetIdAsync(LookupDomains.MfaFactorType, MfaFactorTypes.Totp, ct);
        var secret = TotpService.GenerateSecret();
        var factor = await db.UserMfaFactors.FirstOrDefaultAsync(f => f.UserId == userId && f.FactorTypeLookupId == typeId, ct);
        if (factor is null) { factor = new UserMfaFactor { UserId = userId, FactorTypeLookupId = typeId }; db.UserMfaFactors.Add(factor); }
        else if (factor.IsConfirmed && factor.IsActive) throw new ConflictException("TOTP ya está enrolado y confirmado; desactívelo primero (requiere AAL2).");
        factor.SecretEnc = _protector.Protect(secret);
        factor.IsConfirmed = false; factor.ConfirmedAtUtc = null; factor.IsActive = true;
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Success, userId, tenant.TenantId, new { action = "enroll_started" }, ct);
        return new MfaEnrollResultDto(TotpService.Base32Encode(secret), TotpService.BuildOtpAuthUri("Teikem", user.Email ?? user.UserName ?? userId.ToString(), secret));
    }

    public async Task<MfaConfirmResultDto> ConfirmTotpAsync(int userId, MfaConfirmRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var typeId = await lookups.GetIdAsync(LookupDomains.MfaFactorType, MfaFactorTypes.Totp, ct);
        var factor = await db.UserMfaFactors.FirstOrDefaultAsync(f => f.UserId == userId && f.FactorTypeLookupId == typeId && f.IsActive, ct)
                     ?? throw new ConflictException("Primero inicie el enrolamiento TOTP.");
        var secret = _protector.Unprotect(factor.SecretEnc!);
        if (!TotpService.Verify(secret, req.Code))
        {
            await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Failure, userId, tenant.TenantId, new { action = "confirm" }, ct);
            throw new ValidationException("code", "Código inválido.");
        }
        factor.IsConfirmed = true; factor.ConfirmedAtUtc = DateTime.UtcNow;
        user.TwoFactorEnabled = true;
        await users.UpdateAsync(user);
        var codes = TotpService.GenerateRecoveryCodes();
        db.MfaRecoveryCodes.RemoveRange(db.MfaRecoveryCodes.Where(c => c.UserId == userId));
        foreach (var c in codes) db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = userId, CodeHash = TotpService.Hash(c) });
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Success, userId, tenant.TenantId, new { action = "enrolled" }, ct);
        return new MfaConfirmResultDto(codes);
    }

    public async Task DisableTotpAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var factors = await db.UserMfaFactors.Where(f => f.UserId == userId).ToListAsync(ct);
        foreach (var f in factors) { f.IsActive = false; f.IsConfirmed = false; }
        db.MfaRecoveryCodes.RemoveRange(db.MfaRecoveryCodes.Where(c => c.UserId == userId));
        user.TwoFactorEnabled = false;
        await users.UpdateAsync(user);
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Success, userId, tenant.TenantId, new { action = "disabled" }, ct);
    }

    private async Task<UserMfaFactor?> ConfirmedTotpAsync(int userId, CancellationToken ct)
    {
        var typeId = await lookups.GetIdAsync(LookupDomains.MfaFactorType, MfaFactorTypes.Totp, ct);
        return await db.UserMfaFactors.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.FactorTypeLookupId == typeId && f.IsActive && f.IsConfirmed, ct);
    }

    private async Task<bool> VerifyCodeAsync(ApplicationUser user, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var factor = await ConfirmedTotpAsync(user.Id, ct);
        if (factor?.SecretEnc is not null && TotpService.Verify(_protector.Unprotect(factor.SecretEnc), code)) return true;
        // Código de recuperación (un solo uso)
        var hash = TotpService.Hash(code.Trim().ToLowerInvariant());
        var rc = await db.MfaRecoveryCodes.FirstOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hash && c.UsedAtUtc == null, ct);
        if (rc is null) return false;
        rc.UsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.Mfa, SecurityOutcomes.Success, user.Id, tenant.TenantId, new { action = "recovery_code_used" }, ct);
        return true;
    }
}
