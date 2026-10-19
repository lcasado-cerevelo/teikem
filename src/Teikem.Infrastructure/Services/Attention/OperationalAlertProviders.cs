using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

// Lote 125 (P12) — alertas operacionales de «Necesita tu atención» que no son de almacén: intentos fallidos, reclamaciones abiertas, documentos de flota vencidos o por vencer y facturas vencidas.
// Todas son filas AGREGADAS (una sola con la cuenta; `Summary`), igual que «órdenes sin chofer», «COD sin cuadrar» y «SLA en riesgo». Cada una se ve con el permiso y el módulo de quien la atiende.

/// <summary>
/// Órdenes activas en «Intento fallido» (la entrega no se pudo). Módulo LTL_GROUND y orders.view. Tono: warn; danger si la que lleva más tiempo así pasa de 2 días. «Revisar» abre Órdenes filtradas por ese estatus.
/// Params (cadenas): count y oldestDays (días desde que la más antigua quedó en Intento fallido).
/// </summary>
public sealed class OrdersFailedAttentionProvider(TeikemDbContext db, ILookupCache lookups) : IAttentionItemProvider
{
    public const string ItemCode = "ORDERS_FAILED";
    public const string Route = "/orders";
    public const int UrgentDays = 2;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Operations;
    public string? TenantModule => ModuleKeys.LtlGround;
    public string RequiredPermission => PermissionCatalog.OrdersView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var failedId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && s.InternalCode == OrderStatuses.Failed).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (failedId is null) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var orders = await db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.StatusCodeId == failedId).Select(o => new { o.TransportOrderId, o.CreatedAtUtc }).ToListAsync(ct);
        if (orders.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        // desde cuándo está en fallido: el último paso a «Intento fallido» de cada una (respaldo: cuándo se creó)
        var ids = orders.Select(o => o.TransportOrderId).ToList();
        var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var since = (await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.ToStatusCodeId == failedId && ids.Contains(h.EntityId)).Select(h => new { h.EntityId, h.ChangedAtUtc }).ToListAsync(ct))
            .GroupBy(h => h.EntityId).ToDictionary(g => g.Key, g => g.Max(h => h.ChangedAtUtc));
        var oldest = orders.Min(o => since.GetValueOrDefault(o.TransportOrderId, o.CreatedAtUtc));
        var days = Math.Max(0, (int)(DateTime.UtcNow - oldest).TotalDays);
        var parameters = new Dictionary<string, string> { ["count"] = orders.Count.ToString(CultureInfo.InvariantCulture), ["oldestDays"] = days.ToString(CultureInfo.InvariantCulture) };
        var query = new Dictionary<string, string> { ["statuses"] = OrderStatuses.Failed };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Operations, days > UrgentDays ? "danger" : "warn", orders.Count, parameters, Route, query, oldest, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Operations, 1, Route, query));
    }
}

/// <summary>
/// Reclamaciones abiertas (sin resolver). Módulo LTL_GROUND y claims.view. Tono: warn; danger si la más antigua lleva más de 7 días. «Revisar» abre Reclamaciones.
/// Params (cadenas): count, oldestDays y amount (suma del valor declarado de las que lo tienen, con punto decimal).
/// </summary>
public sealed class ClaimsOpenAttentionProvider(TeikemDbContext db) : IAttentionItemProvider
{
    public const string ItemCode = "CLAIMS_OPEN";
    public const string Route = "/ops/claims";
    public const int UrgentDays = 7;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Operations;
    public string? TenantModule => ModuleKeys.LtlGround;
    public string RequiredPermission => PermissionCatalog.ClaimsView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var rows = await db.Set<Claim>().AsNoTracking().Where(c => c.IsActive && c.Resolution == null).Select(c => new { c.CreatedAtUtc, c.DeclaredValue }).ToListAsync(ct);
        if (rows.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var oldest = rows.Min(r => r.CreatedAtUtc);
        var days = Math.Max(0, (int)(DateTime.UtcNow - oldest).TotalDays);
        var parameters = new Dictionary<string, string>
        {
            ["count"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["oldestDays"] = days.ToString(CultureInfo.InvariantCulture),
            ["amount"] = rows.Sum(r => r.DeclaredValue ?? 0m).ToString("0.00", CultureInfo.InvariantCulture),
        };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Operations, days > UrgentDays ? "danger" : "warn", rows.Count, parameters, Route, null, oldest, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Operations, 1, Route, null));
    }
}

/// <summary>
/// Documentos de vehículos y choferes vencidos o que vencen en 30 días (los mismos del panel «Documentos por vencer»). Módulo Catálogos y fleet.view. Tono: warn; danger si hay alguno ya vencido.
/// «Revisar» abre Flota. Params (cadenas): expired, expiring y minDays (días que le quedan al que primero vence; negativo = lleva ese tiempo vencido).
/// </summary>
public sealed class FleetDocumentsDueAttentionProvider(TeikemDbContext db, ITenantClock clock) : IAttentionItemProvider
{
    public const string ItemCode = "FLEET_DOCS_DUE";
    public const string Route = "/catalog/fleet";

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Operations;
    public string? TenantModule => ModuleKeys.Catalog;
    public string RequiredPermission => PermissionCatalog.FleetView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var today = clock.Today;
        var docs = await db.LoadFleetDocumentsAsync(today, new FleetDocumentScope(IncludeVehicles: true, IncludeDrivers: true, OnlyActiveOwners: true), ct);
        var due = docs.Where(r => r.OwnerIsActive && !r.OwnerIsTerminal && !r.IsSuperseded && r.ExpiryDate is not null)
            .Select(r => new { State = FleetRules.ExpiryState(r.ExpiryDate, today), Days = FleetRules.DaysTo(r.ExpiryDate!.Value, today) })
            .Where(r => r.State is "EXPIRED" or "EXPIRING").ToList();
        if (due.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var expired = due.Count(r => r.State == "EXPIRED");
        var parameters = new Dictionary<string, string>
        {
            ["expired"] = expired.ToString(CultureInfo.InvariantCulture),
            ["expiring"] = (due.Count - expired).ToString(CultureInfo.InvariantCulture),
            ["minDays"] = due.Min(r => r.Days).ToString(CultureInfo.InvariantCulture),
        };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Operations, expired > 0 ? "danger" : "warn", due.Count, parameters, Route, null, null, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Operations, 1, Route, null));
    }
}

/// <summary>
/// Facturas vencidas (pasó su plazo y siguen con saldo). Módulo Facturación y billing.view. Tono: danger (es dinero que ya debía haber entrado). «Revisar» abre Facturación.
/// Params (cadenas): count y oldestDays (días de vencida de la más antigua).
/// </summary>
public sealed class InvoicesOverdueAttentionProvider(TeikemDbContext db, ITenantClock clock) : IAttentionItemProvider
{
    public const string ItemCode = "INVOICES_OVERDUE";
    public const string Route = "/money/billing";

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.Billing;
    public string RequiredPermission => PermissionCatalog.BillingView;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        if (take <= 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var overdueId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && s.InternalCode == InvoiceStatuses.Overdue).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (overdueId is null) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var due = await db.Set<Invoice>().AsNoTracking().Where(i => i.IsActive && i.StatusCodeId == overdueId).Select(i => i.DueDate).ToListAsync(ct);
        if (due.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var today = clock.Today;
        var days = due.Where(d => d != null).Select(d => today.DayNumber - d!.Value.DayNumber).DefaultIfEmpty(0).Max();
        var parameters = new Dictionary<string, string> { ["count"] = due.Count.ToString(CultureInfo.InvariantCulture), ["oldestDays"] = Math.Max(0, days).ToString(CultureInfo.InvariantCulture) };
        var item = new AttentionItemDto(ItemCode, BusinessModules.Accounting, "danger", due.Count, parameters, Route, null, null, Summary: true);
        return new AttentionProviderResult(1, new[] { item }, new AttentionGroupDto(ItemCode, BusinessModules.Accounting, 1, Route, null));
    }
}
