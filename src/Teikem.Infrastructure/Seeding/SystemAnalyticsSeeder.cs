using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Seeding;

/// <summary>
/// Vistas, indicadores y gráficos por default (IsSystem=1) que un tenant trae de fábrica. En el Lote 1 usan las fuentes
/// transversales (AUDIT_LOG, SECURITY_EVENT, USER); cada lote de negocio agrega los suyos (Órdenes, Inventario, ...).
/// Idempotente por nombre.
/// </summary>
public sealed class SystemAnalyticsSeeder(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public async Task SeedForTenantAsync(int tenantId, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.As(tenantId);
        db.SuppressAudit = true;

        var visTenant = await lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Tenant, ct);
        var count = await lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Count, ct);
        var ops = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Operations, ct);
        var last7 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last7, ct);
        var last30 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last30, ct);
        var bar = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Bar, ct);
        var donut = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Donut, ct);
        var line = await lookups.GetIdAsync(LookupDomains.ReportChartType, ChartTypes.Line, ct);

        // ---- Vistas ----
        var existingReports = await db.ReportDefinitions.Where(r => r.TenantId == tenantId).Select(r => r.Name).ToListAsync(ct);
        async Task Report(string entityType, string name, string es, string en, string[] columns, string? filter, string? group, string? sort)
        {
            if (existingReports.Contains(name)) return;
            db.ReportDefinitions.Add(new ReportDefinition
            {
                TenantId = tenantId, BaseEntityTypeLookupId = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct), Name = name,
                DescriptionJson = MultilingualText.Build(es, en), VisibilityLookupId = visTenant, IsSystem = true, OwnerUserId = null,
                ColumnsJson = JsonSerializer.Serialize(columns), FilterJson = filter, GroupJson = group, SortJson = sort,
            });
        }
        await Report(EntityTypes.AuditLog, "Actividad reciente", "Últimos cambios registrados en la compañía", "Latest recorded changes",
            new[] { "CreatedAtUtc", "Action", "EntityType", "EntityId", "UserName", "CorrelationId" }, null, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.SecurityEvent, "Accesos fallidos", "Intentos de acceso fallidos o bloqueados", "Failed or blocked access attempts",
            new[] { "CreatedAtUtc", "EventType", "Outcome", "UserName", "IpAddress" }, "{\"and\":[{\"field\":\"OutcomeCode\",\"op\":\"in\",\"value\":[\"FAILURE\",\"BLOCKED\"]}]}", null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");
        await Report(EntityTypes.User, "Usuarios de la compañía", "Directorio de usuarios con roles y membresía", "User directory with roles and membership",
            new[] { "FullName", "Email", "Roles", "MembershipStatus", "IsActive", "MfaEnabled", "LastLoginUtc" }, null, null, "[{\"field\":\"FullName\",\"dir\":\"asc\"}]");
        await Report(EntityTypes.AuditLog, "Cambios por usuario y acción", "Conteo de cambios agrupado por usuario y acción", "Change count grouped by user and action",
            Array.Empty<string>(), null, "{\"by\":[\"UserName\",\"Action\"],\"aggregates\":[{\"fn\":\"COUNT\"}],\"totals\":true}", null);

        // ---- Indicadores ----
        var existingInd = await db.IndicatorDefinitions.Where(i => i.TenantId == tenantId).Select(i => i.Name).ToListAsync(ct);
        void Indicator(string name, string es, string en, string source, string? field, int fn, string? filter, int? range, bool pulse, int sort)
        {
            if (existingInd.Contains(name)) return;
            db.IndicatorDefinitions.Add(new IndicatorDefinition
            {
                TenantId = tenantId, Name = name, DescriptionJson = MultilingualText.Build(es, en), DataSourceKey = source, FieldKey = field, AggregateFnLookupId = fn,
                FilterJson = filter, BusinessModuleLookupId = ops, IsMoney = false, IsSystem = true, VisibilityLookupId = visTenant, DateRangeModeLookupId = range, ShowInPulse = pulse, SortOrder = sort,
            });
        }
        Indicator("Cambios registrados", "Cambios auditados en el período", "Audited changes in the period", EntityTypes.AuditLog, null, count, null, last7, true, 10);
        Indicator("Accesos fallidos", "Intentos de acceso fallidos o bloqueados en el período", "Failed/blocked access attempts in the period", EntityTypes.SecurityEvent, null, count,
            "{\"and\":[{\"field\":\"OutcomeCode\",\"op\":\"in\",\"value\":[\"FAILURE\",\"BLOCKED\"]}]}", last7, true, 20);
        Indicator("Permisos denegados", "Acciones bloqueadas por RBAC en el período", "Actions blocked by RBAC in the period", EntityTypes.SecurityEvent, null, count,
            "{\"and\":[{\"field\":\"EventTypeCode\",\"op\":\"eq\",\"value\":\"PERMISSION_DENIED\"}]}", last30, false, 30);
        Indicator("Usuarios activos", "Usuarios activos con membresía activa", "Active users with active membership", EntityTypes.User, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"MembershipStatus\",\"op\":\"eq\",\"value\":\"ACTIVE\"}]}", null, true, 40);
        Indicator("Usuarios sin MFA", "Usuarios activos sin segundo factor", "Active users without MFA", EntityTypes.User, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"MfaEnabled\",\"op\":\"isFalse\"}]}", null, false, 50);

        // ---- Gráficos ----
        var existingCharts = await db.ChartDefinitions.Where(c => c.TenantId == tenantId).Select(c => c.Name).ToListAsync(ct);
        void Chart(string name, string es, string en, string source, string groupBy, int type, string? filter, int? range, bool pulse, int sort)
        {
            if (existingCharts.Contains(name)) return;
            db.ChartDefinitions.Add(new ChartDefinition
            {
                TenantId = tenantId, Name = name, DescriptionJson = MultilingualText.Build(es, en), DataSourceKey = source, GroupByField = groupBy, FieldKey = null,
                AggregateFnLookupId = count, ChartTypeLookupId = type, FilterJson = filter, BusinessModuleLookupId = ops, IsSystem = true, VisibilityLookupId = visTenant,
                DateRangeModeLookupId = range, ShowInPulse = pulse, SortOrder = sort,
            });
        }
        Chart("Cambios por acción", "Distribución de cambios por tipo de acción", "Changes by action type", EntityTypes.AuditLog, "Action", donut, null, last7, true, 10);
        Chart("Cambios por usuario", "Quién registró más cambios", "Who recorded the most changes", EntityTypes.AuditLog, "UserName", bar, null, last30, false, 20);
        Chart("Eventos de seguridad por día", "Tendencia diaria de eventos de seguridad", "Daily trend of security events", EntityTypes.SecurityEvent, "CreatedAtUtc", line, null, last30, true, 30);
        Chart("Usuarios por rol", "Distribución de usuarios por rol", "Users by role", EntityTypes.User, "Roles", donut, null, null, false, 40);

        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
    }
}
