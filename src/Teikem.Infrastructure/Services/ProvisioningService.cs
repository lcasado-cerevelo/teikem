using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Seeding;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Aprovisionamiento de una compañía (solo admin de plataforma): Tenant + módulos (núcleo + pedidos, con dependencias)
/// + clon de las 6 plantillas de rol + usuario admin con rol TenantAdmin + vistas/indicadores/gráficos por default.
/// </summary>
public sealed class ProvisioningService(TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, ILookupCache lookups,
    PermissionService permissions, ModuleService modules, SystemAnalyticsSeeder analyticsSeeder, ISecurityEventWriter security, ILogger<ProvisioningService> logger)
{
    public static readonly string[] DefaultModules = { ModuleKeys.LtlGround, ModuleKeys.Catalog, ModuleKeys.Analytics, ModuleKeys.System, ModuleKeys.CustomFields };

    public async Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.BypassTenantFilter();
        var list = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return list.Select(t => new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc)).ToList();
    }

    public async Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(req.AdminEmail)) throw new ValidationException("adminEmail", "El correo del administrador es obligatorio.");
        var tc = (TenantContext)tenant;
        using var bypass = tc.BypassTenantFilter();

        if (await db.Tenants.AnyAsync(t => t.Name == req.Name.Trim(), ct)) throw new ConflictException($"Ya existe la compañía '{req.Name}'.");
        var t = new Tenant { Name = req.Name.Trim(), LegalName = req.LegalName, TaxId = req.TaxId, DefaultLangCode = (req.DefaultLangCode ?? "es").ToLowerInvariant() };
        db.Tenants.Add(t);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tenant {Name} creado con id {Id}", t.Name, t.TenantId);

        using var asTenant = tc.As(t.TenantId);

        // Módulos: núcleo + default + pedidos, respetando dependencias (en orden topológico simple)
        var defs = await db.ModuleDefinitions.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
        var wanted = new HashSet<string>(DefaultModules.Concat(req.Modules ?? new List<string>()).Concat(defs.Where(d => d.IsCore).Select(d => d.ModuleKey)), StringComparer.OrdinalIgnoreCase);
        foreach (var key in wanted.ToList())
        {
            var d = defs.FirstOrDefault(x => x.ModuleKey.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("modules", $"Módulo desconocido: {key}.");
            var dep = d.DependsOnModuleKey;
            while (dep is not null) { wanted.Add(dep); dep = defs.First(x => x.ModuleKey == dep).DependsOnModuleKey; }
        }
        foreach (var key in wanted)
            db.TenantModules.Add(new TenantModule { TenantId = t.TenantId, ModuleKey = defs.First(x => x.ModuleKey.Equals(key, StringComparison.OrdinalIgnoreCase)).ModuleKey, IsEnabled = true, EnabledBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        modules.Invalidate(t.TenantId);

        // Roles: clon de plantillas
        await permissions.CloneTemplatesAsync(t.TenantId, ct);

        // Admin del tenant
        var (adminId, temp) = await EnsureAdminUserAsync(t, req.AdminEmail.Trim(), req.AdminFullName, req.AdminPassword, ct);

        // Contenido por default (vistas/indicadores/gráficos de sistema)
        await analyticsSeeder.SeedForTenantAsync(t.TenantId, ct);

        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, t.TenantId, new { action = "tenant_provisioned", admin = adminId }, ct);
        return new TenantProvisionResult(new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc), adminId, temp);
    }

    public async Task<(int UserId, string? TemporaryPassword)> EnsureAdminUserAsync(Tenant t, string email, string fullName, string? password, CancellationToken ct)
    {
        var internalKind = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Internal, ct);
        var activeStatus = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", MembershipStatuses.Active);
        var user = await users.FindByEmailAsync(email);
        string? temp = null;
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, UserKindLookupId = internalKind, DefaultTenantId = t.TenantId, IsActive = true, EmailConfirmed = true };
            temp = string.IsNullOrEmpty(password) ? UserAdminService.GenerateTemporaryPassword() : null;
            var result = await users.CreateAsync(user, password ?? temp!);
            if (!result.Succeeded) throw new ValidationException("adminPassword", string.Join(" ", result.Errors.Select(e => e.Description)));
        }
        if (!await db.UserTenants.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && m.TenantId == t.TenantId, ct))
            db.UserTenants.Add(new UserTenant { UserId = user.Id, TenantId = t.TenantId, StatusCodeId = activeStatus, IsDefault = user.DefaultTenantId == t.TenantId, JoinedAtUtc = DateTime.UtcNow });
        var adminRole = await db.AppRoles.IgnoreQueryFilters().FirstAsync(r => r.TenantId == t.TenantId && r.Name == "TenantAdmin", ct);
        if (!await db.AppUserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == adminRole.RoleId, ct))
            db.AppUserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.RoleId, TenantId = t.TenantId, GrantedBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        permissions.Invalidate(user.Id, t.TenantId);
        return (user.Id, temp);
    }
}
