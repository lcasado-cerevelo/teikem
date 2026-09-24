using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Capa D: RBAC. Permisos efectivos = permisos de los roles del usuario en el tenant activo ∪ permisos extra del usuario.
/// Cacheados por usuario+tenant e invalidados al cambiar roles/permisos. El admin de plataforma tiene todos.
/// </summary>
public sealed class PermissionService(TeikemDbContext db, ITenantContext tenant, IMemoryCache cache, ILookupCache lookups, ISecurityEventWriter security)
{
    private static string CacheKey(int userId, int tenantId) => $"perms:{userId}:{tenantId}";

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(int userId, int tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(userId, tenantId), out HashSet<string>? cached) && cached is not null) return cached;
        var fromRoles = await db.AppUserRoles.AsNoTracking().IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId && ur.Role!.IsActive)
            .SelectMany(ur => ur.Role!.Permissions.Select(rp => rp.Permission!.Code)).ToListAsync(ct);
        var extra = await db.UserPermissions.AsNoTracking().IgnoreQueryFilters()
            .Where(up => up.UserId == userId && up.TenantId == tenantId).Select(up => up.Permission!.Code).ToListAsync(ct);
        var set = new HashSet<string>(fromRoles.Concat(extra), StringComparer.OrdinalIgnoreCase);
        cache.Set(CacheKey(userId, tenantId), set, TimeSpan.FromMinutes(5));
        return set;
    }

    public void Invalidate(int userId, int tenantId) => cache.Remove(CacheKey(userId, tenantId));

    public async Task InvalidateRoleAsync(int roleId, int tenantId, CancellationToken ct)
    {
        var users = await db.AppUserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).ToListAsync(ct);
        foreach (var u in users) Invalidate(u, tenantId);
    }

    public async Task<bool> HasPermissionAsync(string code, CancellationToken ct)
    {
        if (tenant.IsPlatformAdmin) return true;
        if (tenant.UserId is null || tenant.TenantId is null) return false;
        var set = await GetEffectivePermissionsAsync(tenant.UserId.Value, tenant.TenantId.Value, ct);
        return set.Contains(code);
    }

    /// <summary>Guard programático (para servicios): registra PERMISSION_DENIED si falla.</summary>
    public async Task EnsureAsync(string code, CancellationToken ct)
    {
        if (await HasPermissionAsync(code, ct)) return;
        await security.WriteAsync(SecurityEventTypes.PermissionDenied, SecurityOutcomes.Blocked, tenant.UserId, tenant.TenantId, new { permission = code }, ct);
        throw new ForbiddenException($"Falta el permiso '{code}'.");
    }

    public async Task<IReadOnlyList<PermissionDto>> GetCatalogAsync(CancellationToken ct)
    {
        var perms = await db.Permissions.AsNoTracking().Include(p => p.Category).OrderBy(p => p.Category!.SortOrder).ThenBy(p => p.Code).ToListAsync(ct);
        return perms.Select(p => new PermissionDto(p.PermissionId, p.Code, p.Category?.InternalCode ?? "", MultilingualText.Resolve(p.LabelJson, tenant.Lang))).ToList();
    }

    // ---------------- Roles del tenant ----------------

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(bool includeTemplates, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var roles = await db.AppRoles.AsNoTracking().Include(r => r.Permissions).ThenInclude(rp => rp.Permission)
            .Where(r => r.IsActive && (r.TenantId == tenantId || (includeTemplates && r.TenantId == null))).ToListAsync(ct);
        var counts = await db.AppUserRoles.AsNoTracking().Where(ur => ur.TenantId == tenantId).GroupBy(ur => ur.RoleId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return roles.OrderBy(r => r.TenantId == null).ThenBy(r => r.Name).Select(r => ToDto(r, counts.GetValueOrDefault(r.RoleId))).ToList();
    }

    public async Task<RoleDto> CreateRoleAsync(RoleUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.AppRoles.AnyAsync(r => r.TenantId == tenantId && r.Name == req.Name.Trim(), ct))
            throw new ConflictException($"Ya existe el rol '{req.Name}'.");
        var role = new Role { TenantId = tenantId, Name = req.Name.Trim(), DescriptionJson = req.Descriptions is { Count: > 0 } ? MultilingualText.Serialize(req.Descriptions) : null };
        db.AppRoles.Add(role);
        await ApplyPermissionsAsync(role, req.Permissions, ct);
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { role = role.Name, action = "create" }, ct);
        return (await GetRolesAsync(false, ct)).First(r => r.Id == role.RoleId);
    }

    public async Task<RoleDto> UpdateRoleAsync(int roleId, RoleUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var role = await db.AppRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == roleId && r.TenantId == tenantId, ct)
                   ?? throw new NotFoundException("Rol", roleId);
        if (!string.IsNullOrWhiteSpace(req.Name)) role.Name = req.Name.Trim();
        if (req.Descriptions is not null) role.DescriptionJson = req.Descriptions.Count == 0 ? null : MultilingualText.Serialize(req.Descriptions);
        await ApplyPermissionsAsync(role, req.Permissions, ct);
        await db.SaveChangesAsync(ct);
        await InvalidateRoleAsync(roleId, tenantId, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { role = role.Name, action = "update", permissions = req.Permissions }, ct);
        return (await GetRolesAsync(false, ct)).First(r => r.Id == role.RoleId);
    }

    /// <summary>Único resguardo: no se elimina un rol que todavía tenga usuarios asignados.</summary>
    public async Task DeleteRoleAsync(int roleId, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var role = await db.AppRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == roleId && r.TenantId == tenantId, ct)
                   ?? throw new NotFoundException("Rol", roleId);
        if (await db.AppUserRoles.AnyAsync(ur => ur.RoleId == roleId, ct))
            throw new ConflictException("El rol tiene usuarios asignados; reasígnelos antes de eliminarlo.");
        role.IsActive = false;
        await db.SaveChangesAsync(ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId, new { role = role.Name, action = "delete" }, ct);
    }

    private async Task ApplyPermissionsAsync(Role role, IList<string>? codes, CancellationToken ct)
    {
        if (codes is null) return;
        var wanted = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
        var perms = await db.Permissions.Where(p => wanted.Contains(p.Code)).ToListAsync(ct);
        var missing = wanted.Except(perms.Select(p => p.Code), StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) throw new ValidationException("permissions", $"Permisos desconocidos: {string.Join(", ", missing)}.");
        foreach (var rp in role.Permissions.Where(rp => !perms.Any(p => p.PermissionId == rp.PermissionId)).ToList())
            db.RolePermissions.Remove(rp);
        foreach (var p in perms.Where(p => !role.Permissions.Any(rp => rp.PermissionId == p.PermissionId)))
            role.Permissions.Add(new RolePermission { PermissionId = p.PermissionId });
    }

    private RoleDto ToDto(Role r, int userCount) => new(r.RoleId, r.Name, MultilingualText.Resolve(r.DescriptionJson, tenant.Lang),
        MultilingualText.Parse(r.DescriptionJson), r.IsSystem, r.TenantId is null, r.IsActive,
        r.Permissions.Select(rp => rp.Permission?.Code ?? "").Where(c => c.Length > 0).OrderBy(c => c).ToList(), userCount);

    /// <summary>Clona las 6 plantillas de sistema (TenantId NULL) a un tenant recién aprovisionado.</summary>
    public async Task CloneTemplatesAsync(int tenantId, CancellationToken ct)
    {
        var templates = await db.AppRoles.IgnoreQueryFilters().Include(r => r.Permissions).Where(r => r.TenantId == null && r.IsActive).ToListAsync(ct);
        var existing = await db.AppRoles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).Select(r => r.Name).ToListAsync(ct);
        foreach (var t in templates.Where(t => !existing.Contains(t.Name)))
        {
            var clone = new Role { TenantId = tenantId, Name = t.Name, DescriptionJson = t.DescriptionJson, IsSystem = false, IsActive = true };
            foreach (var rp in t.Permissions) clone.Permissions.Add(new RolePermission { PermissionId = rp.PermissionId });
            db.AppRoles.Add(clone);
        }
        await db.SaveChangesAsync(ct);
    }
}
