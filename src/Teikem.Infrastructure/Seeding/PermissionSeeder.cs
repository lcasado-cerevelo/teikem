using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Seeding;

/// <summary>Permisos sembrados desde código (PermissionCatalog) + plantillas de rol de sistema. Idempotente; corre en cada db-init.</summary>
public sealed class PermissionSeeder(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ILogger<PermissionSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.BypassTenantFilter();
        db.SuppressAudit = true;

        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        var added = 0;
        foreach (var def in PermissionCatalog.All)
        {
            var catId = await lookups.GetIdAsync(LookupDomains.PermissionCategory, def.Category, ct);
            if (existing.TryGetValue(def.Code, out var p))
            {
                p.CategoryLookupId = catId; p.LabelJson = MultilingualText.Build(def.LabelEs, def.LabelEn); p.IsSystem = true;
            }
            else
            {
                db.Permissions.Add(new Permission { Code = def.Code, CategoryLookupId = catId, LabelJson = MultilingualText.Build(def.LabelEs, def.LabelEn), IsSystem = true });
                added++;
            }
        }
        await db.SaveChangesAsync(ct);

        var perms = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        var templates = await db.AppRoles.Include(r => r.Permissions).Where(r => r.TenantId == null).ToListAsync(ct);
        foreach (var (name, codes) in PermissionCatalog.RoleTemplates)
        {
            var role = templates.FirstOrDefault(r => r.Name == name);
            if (role is null)
            {
                var (es, en) = PermissionCatalog.RoleTemplateLabels[name];
                role = new Role { TenantId = null, Name = name, DescriptionJson = MultilingualText.Build(es, en), IsSystem = true, IsActive = true };
                db.AppRoles.Add(role);
            }
            foreach (var code in codes)
                if (perms.TryGetValue(code, out var p) && !role.Permissions.Any(rp => rp.PermissionId == p.PermissionId))
                    role.Permissions.Add(new RolePermission { PermissionId = p.PermissionId });
        }
        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        lookups.Invalidate();
        logger.LogInformation("Permisos sembrados: {Total} en catálogo ({Added} nuevos); plantillas de rol verificadas.", PermissionCatalog.All.Count, added);
    }
}
