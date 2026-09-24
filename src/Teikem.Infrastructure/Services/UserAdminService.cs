using System.Security.Cryptography;
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

/// <summary>Pantalla "Roles y usuarios": usuarios del tenant, sus roles, permisos extra, membresía y alcance de datos; y /me.</summary>
public sealed class UserAdminService(TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, ILookupCache lookups, PermissionService permissions, ModuleService modules, ISecurityEventWriter security)
{
    public async Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var memberships = await db.UserTenants.AsNoTracking().Include(m => m.User).ThenInclude(u => u!.UserKind).Include(m => m.Status).Where(m => m.TenantId == tenantId).ToListAsync(ct);
        var ids = memberships.Select(m => m.UserId).ToList();
        var roles = await db.AppUserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId)).Select(r => new { r.UserId, r.Role!.Name }).ToListAsync(ct);
        var extras = await db.UserPermissions.AsNoTracking().Where(p => ids.Contains(p.UserId)).Select(p => new { p.UserId, p.Permission!.Code }).ToListAsync(ct);
        return memberships.OrderBy(m => m.User!.FullName).Select(m => new UserSummaryDto(m.UserId, m.User!.FullName, m.User.Email, m.User.UserKind?.InternalCode, m.User.IsActive,
            m.Status?.InternalCode ?? "", m.User.TwoFactorEnabled, m.User.LastLoginUtc,
            roles.Where(r => r.UserId == m.UserId).Select(r => r.Name).OrderBy(n => n).ToList(),
            extras.Where(e => e.UserId == m.UserId).Select(e => e.Code).OrderBy(c => c).ToList(), m.User.IsPlatformAdmin)).ToList();
    }

    public async Task<UserSummaryDto> GetUserAsync(int userId, CancellationToken ct)
        => (await GetUsersAsync(ct)).FirstOrDefault(u => u.Id == userId) ?? throw new NotFoundException("Usuario", userId);

    /// <summary>Alta por invitación: sin contraseña se genera una temporal (el flujo real manda un enlace de un solo uso).</summary>
    public async Task<(UserSummaryDto User, string? TemporaryPassword)> CreateUserAsync(UserCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.Email)) throw new ValidationException("email", "El correo es obligatorio.");
        var kindCode = (req.UserKind ?? UserKinds.Internal).ToUpperInvariant();
        if (kindCode == UserKinds.Portal) throw new ValidationException("userKind", "Los usuarios de portal se administran desde el expediente del cliente (Lote 2).");
        var kindId = await lookups.GetIdAsync(LookupDomains.UserKind, kindCode, ct);
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", MembershipStatuses.Active);

        var user = await users.FindByEmailAsync(req.Email.Trim());
        string? temp = null;
        if (user is null)
        {
            user = new ApplicationUser { UserName = req.Email.Trim(), Email = req.Email.Trim(), FullName = req.FullName?.Trim(), UserKindLookupId = kindId, DefaultTenantId = tenantId, IsActive = true, EmailConfirmed = true };
            temp = string.IsNullOrEmpty(req.Password) ? GenerateTemporaryPassword() : null;
            var result = await users.CreateAsync(user, req.Password ?? temp!);
            if (!result.Succeeded) throw new ValidationException("password", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        else if (await db.UserTenants.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && m.TenantId == tenantId, ct))
            throw new ConflictException("El usuario ya pertenece a esta compañía.");

        db.UserTenants.Add(new UserTenant { UserId = user.Id, TenantId = tenantId, StatusCodeId = activeId, IsDefault = user.DefaultTenantId == tenantId, InvitedBy = tenant.UserId, JoinedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        if (req.Roles is { Count: > 0 }) await SetRolesAsync(user.Id, new UserRolesRequest(req.Roles), ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { action = "user_created", user = user.Id }, ct);
        return (await GetUserAsync(user.Id, ct), temp);
    }

    public async Task<UserSummaryDto> UpdateUserAsync(int userId, UserUpdateRequest req, CancellationToken ct)
    {
        await EnsureMemberAsync(userId, ct);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Usuario", userId);
        if (req.FullName is not null) user.FullName = req.FullName.Trim();
        if (req.IsActive.HasValue)
        {
            if (userId == tenant.UserId && !req.IsActive.Value) throw new ConflictException("No puede desactivarse a sí mismo.");
            user.IsActive = req.IsActive.Value;
            if (!user.IsActive) await users.UpdateSecurityStampAsync(user);
        }
        await users.UpdateAsync(user);
        return await GetUserAsync(userId, ct);
    }

    public async Task<UserSummaryDto> SetRolesAsync(int userId, UserRolesRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        await EnsureMemberAsync(userId, ct);
        var wanted = new HashSet<string>(req.Roles ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var roles = await db.AppRoles.Where(r => r.TenantId == tenantId && r.IsActive && wanted.Contains(r.Name)).ToListAsync(ct);
        var missing = wanted.Except(roles.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) throw new ValidationException("roles", $"Roles desconocidos: {string.Join(", ", missing)}.");
        var current = await db.AppUserRoles.Where(ur => ur.UserId == userId && ur.TenantId == tenantId).ToListAsync(ct);
        db.AppUserRoles.RemoveRange(current.Where(c => !roles.Any(r => r.RoleId == c.RoleId)));
        foreach (var r in roles.Where(r => !current.Any(c => c.RoleId == r.RoleId)))
            db.AppUserRoles.Add(new UserRole { UserId = userId, RoleId = r.RoleId, TenantId = tenantId, GrantedBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(userId, tenantId);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { user = userId, roles = wanted }, ct);
        return await GetUserAsync(userId, ct);
    }

    public async Task<UserSummaryDto> SetExtraPermissionsAsync(int userId, UserExtraPermissionsRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        await EnsureMemberAsync(userId, ct);
        var wanted = new HashSet<string>(req.Permissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var perms = await db.Permissions.Where(p => wanted.Contains(p.Code)).ToListAsync(ct);
        var missing = wanted.Except(perms.Select(p => p.Code), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) throw new ValidationException("permissions", $"Permisos desconocidos: {string.Join(", ", missing)}.");
        var current = await db.UserPermissions.Where(up => up.UserId == userId && up.TenantId == tenantId).ToListAsync(ct);
        db.UserPermissions.RemoveRange(current.Where(c => !perms.Any(p => p.PermissionId == c.PermissionId)));
        foreach (var p in perms.Where(p => !current.Any(c => c.PermissionId == p.PermissionId)))
            db.UserPermissions.Add(new UserPermission { UserId = userId, TenantId = tenantId, PermissionId = p.PermissionId, GrantedBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(userId, tenantId);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { user = userId, extraPermissions = wanted }, ct);
        return await GetUserAsync(userId, ct);
    }

    /// <summary>Estado de membresía separado del estado global del usuario (activo globalmente, suspendido en un tenant).</summary>
    public async Task<UserSummaryDto> SetMembershipStatusAsync(int userId, MembershipStatusRequest req, CancellationToken ct)
    {
        var m = await db.UserTenants.FirstOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new NotFoundException("Usuario", userId);
        if (userId == tenant.UserId) throw new ConflictException("No puede cambiar su propia membresía.");
        m.StatusCodeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == req.Status).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", req.Status);
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(userId, m.TenantId);
        if (!req.Status.Equals(MembershipStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            var tokens = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId && t.TenantId == m.TenantId && t.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
            db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        }
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, m.TenantId, new { user = userId, membership = req.Status }, ct);
        return await GetUserAsync(userId, ct);
    }

    public async Task<IReadOnlyList<DataScopeDto>> GetDataScopesAsync(int userId, CancellationToken ct)
    {
        var rows = await db.UserDataScopes.AsNoTracking().Include(s => s.ScopeEntity).Where(s => s.UserId == userId).ToListAsync(ct);
        return rows.Select(r => new DataScopeDto(r.ScopeEntity!.InternalCode, r.ScopeId)).ToList();
    }

    public async Task<IReadOnlyList<DataScopeDto>> SetDataScopesAsync(int userId, DataScopesRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        await EnsureMemberAsync(userId, ct);
        var current = await db.UserDataScopes.Where(s => s.UserId == userId).ToListAsync(ct);
        db.UserDataScopes.RemoveRange(current);
        foreach (var s in req.Scopes ?? new List<DataScopeDto>())
            db.UserDataScopes.Add(new UserDataScope { UserId = userId, TenantId = tenantId, ScopeEntityLookupId = await lookups.GetIdAsync(LookupDomains.EntityType, s.ScopeEntity, ct), ScopeId = s.ScopeId });
        await db.SaveChangesAsync(ct);
        return await GetDataScopesAsync(userId, ct);
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new UnauthorizedException();
        var memberships = await db.UserTenants.AsNoTracking().IgnoreQueryFilters().Include(m => m.Tenant).Include(m => m.Status)
            .Where(m => m.UserId == userId).Select(m => new MembershipDto(m.TenantId, m.Tenant!.Name, m.Status!.InternalCode, m.IsDefault)).ToListAsync(ct);
        if (user.IsPlatformAdmin && memberships.Count == 0)
            memberships = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.IsActive).Select(t => new MembershipDto(t.TenantId, t.Name, "PLATFORM", false)).ToListAsync(ct);
        var perms = tenant.TenantId is null ? new List<string>() : (await permissions.GetEffectivePermissionsAsync(userId, tenant.TenantId.Value, ct)).OrderBy(p => p).ToList();
        if (user.IsPlatformAdmin) perms = PermissionCatalog.All.Select(p => p.Code).OrderBy(p => p).ToList();
        var enabled = tenant.TenantId is null ? new List<string>() : (await modules.GetEnabledKeysAsync(tenant.TenantId.Value, ct)).OrderBy(k => k).ToList();
        var tenantName = tenant.TenantId is null ? null : await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenant.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
        return new MeDto(user.Id, user.FullName, user.Email, user.IsPlatformAdmin, tenant.TenantId, tenantName, tenant.Lang, memberships, perms, enabled,
            await GetDataScopesAsync(userId, ct), user.TwoFactorEnabled, tenant.Aal2VerifiedAtUtc);
    }

    private async Task EnsureMemberAsync(int userId, CancellationToken ct)
    {
        if (!await db.UserTenants.AnyAsync(m => m.UserId == userId, ct)) throw new NotFoundException("Usuario", userId);
    }

    public static string GenerateTemporaryPassword()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
