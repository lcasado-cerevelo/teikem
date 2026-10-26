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
    /// <summary>Módulos con los que nace una compañía (2026-10-26): solo los base. Transporte, almacén, facturación y los demás los enciende después el administrador de la plataforma, en Ajustes → Módulos.</summary>
    public static readonly string[] BaseModules = { ModuleKeys.System, ModuleKeys.Catalog, ModuleKeys.Analytics, ModuleKeys.CustomFields };

    public async Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.BypassTenantFilter();
        var list = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return list.Select(t => new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc, t.CustomDomain, t.Subdomain)).ToList();
    }

    /// <summary>
    /// 2026-10-22: fija (o quita, con vacío) el dominio propio y el subdominio de la compañía. 400 si no son válidos y 409 si otra compañía ya usa la dirección
    /// (mensajes de TenantDomainRules). Solo el administrador de la plataforma: la dirección decide el logo que ve cualquiera antes de entrar.
    /// </summary>
    public async Task<TenantSummaryDto> SetDomainsAsync(Guid publicId, TenantDomainsRequest req, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.BypassTenantFilter();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Compañía", publicId.ToString());
        var (domain, sub) = await ResolveDomainsAsync(req.CustomDomain, req.Subdomain, t.TenantId, ct);
        t.CustomDomain = domain;
        t.Subdomain = sub;
        await db.SaveChangesAsync(ct);
        return new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc, t.CustomDomain, t.Subdomain);
    }

    /// <summary>Dominio y subdominio normalizados (null = sin) y validados: 400 si no son válidos, 409 si otra compañía ya usa la dirección.</summary>
    private async Task<(string? Domain, string? Subdomain)> ResolveDomainsAsync(string? customDomain, string? subdomain, int? exceptTenantId, CancellationToken ct)
    {
        var domain = TenantDomainRules.NormalizeDomain(customDomain) ?? throw new ValidationException("customDomain", TenantDomainRules.DomainInvalidMessage);
        var sub = TenantDomainRules.NormalizeSubdomain(subdomain) ?? throw new ValidationException("subdomain", TenantDomainRules.SubdomainInvalidMessage);
        if (domain.Length > 0 && await db.Tenants.AnyAsync(x => x.CustomDomain == domain && x.TenantId != exceptTenantId, ct)) throw new ConflictException(TenantDomainRules.DomainTakenMessage);
        if (sub.Length > 0 && await db.Tenants.AnyAsync(x => x.Subdomain == sub && x.TenantId != exceptTenantId, ct)) throw new ConflictException(TenantDomainRules.SubdomainTakenMessage);
        return (domain.Length == 0 ? null : domain, sub.Length == 0 ? null : sub);
    }

    /// <summary>
    /// 2026-10-26: activa o desactiva una compañía (solo el administrador de la plataforma). Desactivar: nadie entra (login, cambio de compañía y la API responden 403 «La compañía está inactiva.»)
    /// y se cierran las sesiones de todas las demás personas; las de quien lo hace se respetan para que pueda volver a activarla. Los datos no se tocan: reactivar lo deja todo como estaba.
    /// </summary>
    public async Task<TenantSummaryDto> SetActiveAsync(Guid publicId, bool isActive, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.BypassTenantFilter();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Compañía", publicId.ToString());
        if (t.IsActive != isActive)
        {
            t.IsActive = isActive;
            if (!isActive)
            {
                var me = tenant.UserId;
                var open = await db.RefreshTokens.IgnoreQueryFilters().Where(r => r.TenantId == t.TenantId && r.RevokedAtUtc == null && r.UserId != me).ToListAsync(ct);
                foreach (var r in open) r.RevokedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            TenantStatusGuard.Invalidate(t.TenantId);
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, t.TenantId, new { action = isActive ? "tenant_activated" : "tenant_deactivated" }, ct);
        }
        return new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc, t.CustomDomain, t.Subdomain);
    }

    /// <summary>
    /// Alta de una compañía. <paramref name="fromScreen"/> = true (la pantalla de la plataforma y el API): teléfono y domicilio obligatorios y solo los módulos base. false = las importaciones
    /// y la compañía de demostración, que no traen teléfono ni domicilio y arrancan con transporte encendido como siempre.
    /// </summary>
    public async Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest req, CancellationToken ct, bool fromScreen = true)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        // 2026-10-26: datos base de una compañía nueva: teléfono (para cartas y documentos) y domicilio obligatorios; dominio, subdominio y dirección postal opcionales; el administrador inicial ya no hace falta
        // (la plataforma opera cualquier compañía y los usuarios se agregan después).
        var (phone, phoneError) = TenantContactRules.NormalizePhone(req.PhoneNumber, required: fromScreen);
        if (phoneError is not null) throw new ValidationException("phoneNumber", phoneError);
        if (fromScreen && req.Address is null) throw new ValidationException("address.line1", TenantContactRules.Line1RequiredMessage);
        if (req.Address is not null && TenantContactRules.ValidateAddress(req.Address.Line1, req.Address.Line2, req.Address.City, req.Address.State, req.Address.PostalCode) is { } badAddress) throw new ValidationException($"address.{badAddress.Field}", badAddress.Message);
        if (req.PostalAddress is not null && TenantContactRules.ValidateAddress(req.PostalAddress.Line1, req.PostalAddress.Line2, req.PostalAddress.City, req.PostalAddress.State, req.PostalAddress.PostalCode) is { } badPostal) throw new ValidationException($"postalAddress.{badPostal.Field}", badPostal.Message);
        var hasAdmin = !string.IsNullOrWhiteSpace(req.AdminEmail);
        if (hasAdmin && string.IsNullOrWhiteSpace(req.AdminFullName)) throw new ValidationException("adminFullName", "El nombre del administrador es obligatorio.");
        // Región y formatos (2026-10): se valida antes de tocar la base; la compañía nace con los valores de su región (PR por defecto).
        var t = new Tenant { Name = req.Name.Trim(), LegalName = req.LegalName, TaxId = req.TaxId, DefaultLangCode = (req.DefaultLangCode ?? "es").ToLowerInvariant() };
        TenantService.ApplyFormat(t, TenantFormatRules.Defaults(TenantFormatRules.DefaultRegion), FormatChanges(req));
        t.PhoneNumber = phone;
        var tc = (TenantContext)tenant;
        using var bypass = tc.BypassTenantFilter();
        (t.CustomDomain, t.Subdomain) = await ResolveDomainsAsync(req.CustomDomain, req.Subdomain, null, ct);

        if (await db.Tenants.AnyAsync(x => x.Name == req.Name.Trim(), ct)) throw new ConflictException($"Ya existe la compañía '{req.Name}'.");
        if (req.MfaRequired.HasValue) t.MfaRequired = req.MfaRequired.Value;
        db.Tenants.Add(t);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Tenant {Name} creado con id {Id}", t.Name, t.TenantId);

        using var asTenant = tc.As(t.TenantId);

        // Módulos: núcleo + default + pedidos, respetando dependencias (en orden topológico simple)
        var defs = await db.ModuleDefinitions.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
        var wanted = new HashSet<string>(BaseModules.Concat(fromScreen ? Array.Empty<string>() : new[] { ModuleKeys.LtlGround }).Concat(req.Modules ?? new List<string>()), StringComparer.OrdinalIgnoreCase);
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

        // Domicilio y dirección postal (filas de Location de la compañía nueva)
        await TenantAddresses.ApplyAsync(db, lookups, t, req.Address, req.PostalAddress, postalSame: null, ct);
        await db.SaveChangesAsync(ct);

        // Admin del tenant (opcional)
        int? adminId = null; string? temp = null;
        if (hasAdmin)
        {
            var (id, tp) = await EnsureAdminUserAsync(t, req.AdminEmail!.Trim(), req.AdminFullName!, req.AdminPassword, ct);
            adminId = id; temp = tp;
        }

        // Contenido por default (vistas/indicadores/gráficos de sistema)
        await analyticsSeeder.SeedForTenantAsync(t.TenantId, ct);

        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, t.TenantId, new { action = "tenant_provisioned", admin = adminId }, ct);
        return new TenantProvisionResult(new TenantSummaryDto(t.TenantId, t.PublicId, t.Name, t.LegalName, t.IsActive, t.CreatedAtUtc, t.CustomDomain, t.Subdomain), adminId, temp);
    }

    private static TenantFormatChanges FormatChanges(TenantProvisionRequest r) => new(
        r.RegionCode, r.TimeZoneId, r.CurrencyCode, r.CurrencySymbol, r.CurrencySymbolPosition, r.CurrencyDecimals, r.DateOrder, r.DateSeparator,
        r.TimeFormat, r.WeekStartDay, r.ThousandsSeparator, r.DecimalSeparator, r.PhoneCountryCode, r.PhoneMask);

    public async Task<(int UserId, string? TemporaryPassword)> EnsureAdminUserAsync(Tenant t, string email, string fullName, string? password, CancellationToken ct)
    {
        var internalKind = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Internal, ct);
        var activeStatus = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Estatus {StatusDomains.MembershipStatus}", MembershipStatuses.Active);
        var user = await users.FindByEmailAsync(email);
        string? temp = null;
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, UserKindLookupId = internalKind, DefaultTenantId = t.TenantId, IsActive = true, EmailConfirmed = false };
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
