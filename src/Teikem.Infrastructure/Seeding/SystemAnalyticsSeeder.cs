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
/// transversales (AUDIT_LOG, SECURITY_EVENT, USER); el Lote 2 agrega Clientes y contratos (CLIENT, CONTRACT); el Lote 3
/// agrega Órdenes (TRANSPORT_ORDER); cada lote de negocio agrega los suyos (Inventario, ...).
/// Idempotente por nombre.
/// </summary>
public sealed class SystemAnalyticsSeeder(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    /// <summary>Filtro vigente de 'COD por cobrar': COD PENDING de órdenes activas que no estén canceladas.</summary>
    public const string CodPendingFilter =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"CodStatusCode\",\"op\":\"eq\",\"value\":\"PENDING\"},{\"field\":\"StatusCode\",\"op\":\"neq\",\"value\":\"CANCELLED\"}]}";

    /// <summary>Filtro sembrado por la primera versión del Lote 3 (sumaba el COD de órdenes canceladas); se corrige al resembrar.</summary>
    public const string CodPendingFilterV1 =
        "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"CodStatusCode\",\"op\":\"eq\",\"value\":\"PENDING\"}]}";

    public async Task SeedForTenantAsync(int tenantId, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        using var _ = tc.As(tenantId);
        db.SuppressAudit = true;

        var visTenant = await lookups.GetIdAsync(LookupDomains.ReportVisibility, ReportVisibilities.Tenant, ct);
        var count = await lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Count, ct);
        var sum = await lookups.GetIdAsync(LookupDomains.AggregateFn, AggregateFns.Sum, ct);
        var ops = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Operations, ct);
        var acct = await lookups.GetIdAsync(LookupDomains.BusinessModule, BusinessModules.Accounting, ct);
        var last7 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last7, ct);
        var last30 = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.Last30, ct);
        var all = await lookups.GetIdAsync(LookupDomains.DateRangeMode, DateRangeModes.All, ct);
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
        // Lote 2 — Clientes y contratos
        await Report(EntityTypes.Client, "Clientes", "Directorio de clientes con estatus y modelo de facturación vigente", "Client directory with status and current billing model",
            new[] { "Code", "Name", "Status", "BillingSummary", "CreditLimit", "IsActive" }, null, null, "[{\"field\":\"Name\",\"dir\":\"asc\"}]");
        // Lote 3 — Órdenes de transporte (los nombres de campo son los de TransportOrderDataSource; no cambiarlos sin cambiar ambos)
        await Report(EntityTypes.TransportOrder, "Órdenes", "Órdenes de transporte con empaque, consignatario, estatus y COD", "Transport orders with pack batch, consignee, status and COD",
            new[] { "PackBatchNumber", "OrderNumber", "ClientInvoiceNumber", "ClientName", "ConsigneeName", "Status", "TotalPieces", "CodAmount", "CreatedAtUtc" }, null, null, "[{\"field\":\"CreatedAtUtc\",\"dir\":\"desc\"}]");

        // ---- Indicadores ----
        var existingInd = await db.IndicatorDefinitions.Where(i => i.TenantId == tenantId).Select(i => i.Name).ToListAsync(ct);
        void Indicator(string name, string es, string en, string source, string? field, int fn, string? filter, int? range, bool pulse, int sort, bool isMoney = false, int? module = null)
        {
            if (existingInd.Contains(name)) return;
            db.IndicatorDefinitions.Add(new IndicatorDefinition
            {
                TenantId = tenantId, Name = name, DescriptionJson = MultilingualText.Build(es, en), DataSourceKey = source, FieldKey = field, AggregateFnLookupId = fn,
                FilterJson = filter, BusinessModuleLookupId = module ?? ops, IsMoney = isMoney, IsSystem = true, VisibilityLookupId = visTenant, DateRangeModeLookupId = range, ShowInPulse = pulse, SortOrder = sort,
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
        // Lote 2 — Clientes y contratos (estado actual: sin rango de fecha)
        Indicator("Clientes activos", "Clientes activos con estatus ACTIVE", "Active clients with ACTIVE status", EntityTypes.Client, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"eq\",\"value\":\"ACTIVE\"}]}", null, true, 60);
        // Lote 3 — Órdenes de transporte (estado actual: rango ALL, porque la fuente TRANSPORT_ORDER tiene DateField y con
        // rango null el motor aplicaría LAST7 por defecto). 'COD por cobrar' es de Contabilidad (L155/L159: el ciclo COD vive ahí).
        Indicator("Órdenes en curso", "Órdenes activas confirmadas y aún no entregadas", "Active orders confirmed and not yet delivered", EntityTypes.TransportOrder, null, count,
            "{\"and\":[{\"field\":\"IsActive\",\"op\":\"isTrue\"},{\"field\":\"StatusCode\",\"op\":\"in\",\"value\":[\"CONFIRMED\",\"PICKUP\",\"INBOUND\",\"PLANNED\",\"IN_TRANSIT\",\"ARRIVED\"]}]}", all, true, 70);
        Indicator("COD por cobrar", "Suma del COD pendiente de cobro de las órdenes activas no canceladas", "Sum of pending COD of active, non-cancelled orders", EntityTypes.TransportOrder, "CodAmount", sum,
            CodPendingFilter, all, true, 71, isMoney: true, module: acct);

        // Corrección idempotente de tenants ya sembrados con la versión anterior (rango null / COD en Operación).
        var orderIndicators = await db.IndicatorDefinitions
            .Where(i => i.TenantId == tenantId && i.IsSystem && (i.Name == "Órdenes en curso" || i.Name == "COD por cobrar"))
            .ToListAsync(ct);
        foreach (var ind in orderIndicators)
        {
            ind.DateRangeModeLookupId ??= all;
            if (ind.Name == "COD por cobrar" && ind.BusinessModuleLookupId == ops) ind.BusinessModuleLookupId = acct;
            // Una orden cancelada nunca se entrega: su COD no está por cobrar. Solo se reemplaza el filtro original exacto
            // (no se pisa un filtro que el tenant haya personalizado).
            if (ind.Name == "COD por cobrar" && ind.FilterJson == CodPendingFilterV1) ind.FilterJson = CodPendingFilter;
        }

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
        // Lote 2 — Clientes y contratos
        Chart("Contratos por estatus", "Distribución de contratos por estatus", "Contracts by status", EntityTypes.Contract, "Status", donut, null, all, false, 60);
        // Lote 3 — Órdenes de transporte
        Chart("Órdenes por estatus", "Distribución de las órdenes de los últimos 30 días por estatus", "Orders of the last 30 days by status", EntityTypes.TransportOrder, "Status", donut, null, last30, true, 70);

        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
    }
}
