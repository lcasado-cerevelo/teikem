using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
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

/// <summary>
/// Capa D: identidad y sesiones. Login con Identity (hash, lockout), tenant activo al login → JWT; refresh tokens hasheados
/// con rotación; revocación por dispositivo o global (SecurityStamp invalida access tokens vivos); MFA TOTP + códigos de
/// recuperación; step-up AAL2 (reauth). Todo evento entra a SecurityEvent.
/// </summary>
public sealed class AuthService(
    TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, JwtTokenService jwt, ILookupCache lookups,
    ISecurityEventWriter security, IDataProtectionProvider dataProtection, IPasswordBreachChecker breachChecker, PermissionService permissions)
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

    private async Task<TokenPairDto> IssueAsync(ApplicationUser user, int tenantId, string? kind, string? deviceInfo, DateTime? aal2At, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().IgnoreQueryFilters().FirstAsync(x => x.TenantId == tenantId, ct);
        var raw = JwtTokenService.NewRefreshToken();
        var rt = new RefreshToken
        {
            UserId = user.Id, TenantId = tenantId, TokenHash = JwtTokenService.HashToken(raw), DeviceInfo = deviceInfo is { Length: > 200 } d ? d[..200] : deviceInfo,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(t.SessionDays), Aal2VerifiedAtUtc = aal2At,
        };
        db.SuppressAudit = true;
        db.RefreshTokens.Add(rt);
        user.LastLoginUtc = DateTime.UtcNow;
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        var (access, exp) = jwt.CreateAccessToken(user, tenantId, rt.RefreshTokenId, kind, aal2At);
        return new TokenPairDto(access, exp, raw, rt.ExpiresAtUtc, tenantId);
    }

    // ---------------- Refresh / logout / switch ----------------

    public async Task<TokenPairDto> RefreshAsync(string refreshToken, string? deviceInfo, CancellationToken ct)
    {
        var rt = await FindActiveAsync(refreshToken, ct);
        var user = await users.FindByIdAsync(rt.UserId.ToString());
        if (user is null || !user.IsActive) throw new UnauthorizedException();
        var memberships = await ActiveMembershipsAsync(user, ct);
        if (!memberships.Any(m => m.TenantId == rt.TenantId)) { await RevokeAsync(rt, null, ct); throw new ForbiddenException("La membresía ya no está activa."); }

        // Rotación: el token usado se revoca y apunta al nuevo
        var raw = JwtTokenService.NewRefreshToken();
        var next = new RefreshToken
        {
            UserId = rt.UserId, TenantId = rt.TenantId, TokenHash = JwtTokenService.HashToken(raw), DeviceInfo = deviceInfo ?? rt.DeviceInfo,
            IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = rt.ExpiresAtUtc, Aal2VerifiedAtUtc = rt.Aal2VerifiedAtUtc,
        };
        db.SuppressAudit = true;
        db.RefreshTokens.Add(next);
        rt.RevokedAtUtc = DateTime.UtcNow;
        rt.ReplacedByTokenHash = next.TokenHash;
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var (access, exp) = jwt.CreateAccessToken(user, rt.TenantId, next.RefreshTokenId, kind, next.Aal2VerifiedAtUtc);
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
        rt.Aal2VerifiedAtUtc = DateTime.UtcNow;
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        var kind = user.UserKindLookupId is null ? UserKinds.Internal : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        var (access, exp) = jwt.CreateAccessToken(user, rt.TenantId, rt.RefreshTokenId, kind, rt.Aal2VerifiedAtUtc);
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
