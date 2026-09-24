using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Audit;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Contacts;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Persistence;

/// <summary>
/// DbContext único de la plataforma. Identity (int) + todas las tablas transversales.
/// Aplica el filtro global de tenant a toda entidad ITenantScoped / IOptionallyTenantScoped:
/// olvidarse de filtrar deja de ser posible.
/// </summary>
public class TeikemDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
{
    private readonly ITenantContext _tenant;

    public TeikemDbContext(DbContextOptions<TeikemDbContext> options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    // Usados por los filtros globales (EF los parametriza por consulta al referenciar miembros del contexto).
    public int CurrentTenantId => _tenant.TenantId ?? -1;
    public bool TenantFilterDisabled => _tenant.TenantFilterDisabled;

    /// <summary>Bandera interna del interceptor de auditoría para no auditar la escritura del propio AuditLog.</summary>
    internal bool SuppressAudit { get; set; }
    internal List<Interceptors.PendingAudit> PendingAudits { get; } = new();

    // Tenancy / módulos
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantHoliday> TenantHolidays => Set<TenantHoliday>();
    public DbSet<ModuleDefinition> ModuleDefinitions => Set<ModuleDefinition>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    // Catálogos / estatus
    public DbSet<CatalogDomain> CatalogDomains => Set<CatalogDomain>();
    public DbSet<LookupCode> LookupCodes => Set<LookupCode>();
    public DbSet<LookupCodeOverride> LookupCodeOverrides => Set<LookupCodeOverride>();
    public DbSet<StatusCode> StatusCodes => Set<StatusCode>();
    public DbSet<StatusCodeOverride> StatusCodeOverrides => Set<StatusCodeOverride>();
    public DbSet<StatusLateralEntry> StatusLateralEntries => Set<StatusLateralEntry>();
    public DbSet<StatusCapability> StatusCapabilities => Set<StatusCapability>();
    public DbSet<EntityStatusHistory> EntityStatusHistories => Set<EntityStatusHistory>();
    // Seguridad
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> AppRoles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> AppUserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();
    public DbSet<UserMfaFactor> UserMfaFactors => Set<UserMfaFactor>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    // Auditoría / contactos / campos / análisis
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContactPoint> ContactPoints => Set<ContactPoint>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldOption> CustomFieldOptions => Set<CustomFieldOption>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<ReportDefinition> ReportDefinitions => Set<ReportDefinition>();
    public DbSet<ReportShare> ReportShares => Set<ReportShare>();
    public DbSet<IndicatorDefinition> IndicatorDefinitions => Set<IndicatorDefinition>();
    public DbSet<IndicatorShare> IndicatorShares => Set<IndicatorShare>();
    public DbSet<ChartDefinition> ChartDefinitions => Set<ChartDefinition>();
    public DbSet<ChartShare> ChartShares => Set<ChartShare>();
    public DbSet<UserAnalyticsPreference> UserAnalyticsPreferences => Set<UserAnalyticsPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(TeikemDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            if (clr.Namespace is null || !clr.Namespace.StartsWith("Teikem.Domain")) continue;

            // Sin cascadas EF: el esquema SQL no las declara; la baja es soft-delete o cancelación con bitácora.
            foreach (var fk in entityType.GetForeignKeys())
                fk.DeleteBehavior = DeleteBehavior.Restrict;

            // Filtro global de tenant
            if (typeof(ITenantScoped).IsAssignableFrom(clr))
                entityType.SetQueryFilter(BuildTenantFilter(clr, optional: false));
            else if (typeof(IOptionallyTenantScoped).IsAssignableFrom(clr))
                entityType.SetQueryFilter(BuildTenantFilter(clr, optional: true));
        }
    }

    /// <summary>e => TenantFilterDisabled || e.TenantId == CurrentTenantId  (|| e.TenantId == null si es opcional).</summary>
    private LambdaExpression BuildTenantFilter(Type clr, bool optional)
    {
        var e = Expression.Parameter(clr, "e");
        var ctx = Expression.Constant(this);
        var disabled = Expression.Property(ctx, nameof(TenantFilterDisabled));
        var current = Expression.Property(ctx, nameof(CurrentTenantId));
        var tenantProp = Expression.Property(e, "TenantId");
        Expression body;
        if (!optional)
        {
            body = Expression.OrElse(disabled, Expression.Equal(tenantProp, current));
        }
        else
        {
            var isNull = Expression.Equal(tenantProp, Expression.Constant(null, typeof(int?)));
            var eq = Expression.Equal(tenantProp, Expression.Convert(current, typeof(int?)));
            body = Expression.OrElse(disabled, Expression.OrElse(isNull, eq));
        }
        return Expression.Lambda(body, e);
    }
}
