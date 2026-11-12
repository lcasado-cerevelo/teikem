using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 222 (plan §1.7) — fila de "Necesita tu atención" por cada cliente nuevo (PROSPECTO activo) que espera aprobación: la ve quien puede aprobarlo (sales.approve_client), con el
/// módulo de órdenes de venta. Las más antiguas primero. Tono warn; danger si lleva más de 3 días esperando. "Revisar" abre la ficha del cliente.
/// Params: publicId, name, code, seller (quién lo dio de alta), orders (órdenes que esperan su aprobación), days.
/// No hay tabla de avisos: se calcula al leer (el aviso de la campana es el evento SALES_CLIENT_PENDING_APPROVAL).
/// </summary>
public sealed class SalesClientPendingAttentionProvider(TeikemDbContext db) : IAttentionItemProvider
{
    public const string ItemCode = "SALES_CLIENT_PENDING";
    public const string Route = "/clients";
    public const int LongWaitDays = 3;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.SalesOrders;
    public string RequiredPermission => PermissionCatalog.SalesApproveClient;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var prospectId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ClientStatus && s.InternalCode == ClientStatuses.Prospect)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (prospectId is not int pid) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var q = db.Clients.AsNoTracking().Where(c => c.IsActive && c.StatusCodeId == pid);
        var total = await q.CountAsync(ct);
        if (total == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var rows = await q.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.ClientId).Take(Math.Max(0, take))
            .Select(c => new { c.ClientId, c.PublicId, c.Name, c.Code, c.CreatedAtUtc, c.CreatedBy }).ToListAsync(ct);
        var ids = rows.Select(r => r.ClientId).ToList();
        var waiting = await db.Set<SalesOrder>().AsNoTracking().Where(o => ids.Contains(o.ClientId) && o.AwaitingClientApproval)
            .GroupBy(o => o.ClientId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var userIds = rows.Where(r => r.CreatedBy is not null).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var now = DateTime.UtcNow;
        var items = rows.Select(r =>
        {
            var days = Math.Max(0, (int)(now - r.CreatedAtUtc).TotalDays);
            var p = new Dictionary<string, string>
            {
                ["publicId"] = r.PublicId.ToString(), ["name"] = r.Name, ["code"] = r.Code,
                ["seller"] = r.CreatedBy is int u ? users.GetValueOrDefault(u, string.Empty) : string.Empty,
                ["orders"] = waiting.GetValueOrDefault(r.ClientId).ToString(CultureInfo.InvariantCulture),
                ["days"] = days.ToString(CultureInfo.InvariantCulture),
            };
            return new AttentionItemDto(ItemCode, BusinessModules.Accounting, days > LongWaitDays ? "danger" : "warn", 1, p, Route,
                new Dictionary<string, string> { ["client"] = r.PublicId.ToString() }, r.CreatedAtUtc);
        }).ToList();
        return new AttentionProviderResult(total, items, new AttentionGroupDto(ItemCode, BusinessModules.Accounting, total, Route, new Dictionary<string, string> { ["status"] = ClientStatuses.Prospect }));
    }
}

/// <summary>
/// Lote 222 (plan §1.7) — fila de "Necesita tu atención" por cada pedido URGENTE abierto (solicitado o despachado en parte, que no espere la aprobación del cliente): la ve el almacén
/// (warehouse.pick) con el módulo de órdenes de venta. Las más viejas primero; tono danger. "Revisar" abre la orden.
/// Params: publicId, number, client, warehouse, note (la nota de la orden).
/// </summary>
public sealed class SalesUrgentAttentionProvider(TeikemDbContext db) : IAttentionItemProvider
{
    public const string ItemCode = "SALES_ORDER_URGENT";
    public const string Route = "/sales-orders";

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;
    public string RequiredPermission => PermissionCatalog.WarehousePick;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var open = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == SalesCodes.StatusDomain && (s.InternalCode == SalesOrderStatuses.Requested || s.InternalCode == SalesOrderStatuses.Partial))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var q = db.Set<SalesOrder>().AsNoTracking().Where(o => o.IsUrgent && !o.AwaitingClientApproval && open.Contains(o.StatusCodeId));
        var total = await q.CountAsync(ct);
        if (total == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);
        var rows = await (from o in q
                          join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId
                          join w in db.Set<Warehouse>().AsNoTracking() on o.WarehouseId equals w.WarehouseId
                          orderby o.RequestedAtUtc, o.SalesOrderId
                          select new { o.PublicId, o.Number, Client = c.Name, Warehouse = w.Name, o.Notes, o.RequestedAtUtc, o.CreatedAtUtc })
            .Take(Math.Max(0, take)).ToListAsync(ct);
        var items = rows.Select(r => new AttentionItemDto(ItemCode, BusinessModules.Warehouse, "danger", 1,
            new Dictionary<string, string>
            {
                ["publicId"] = r.PublicId.ToString(), ["number"] = r.Number, ["client"] = r.Client, ["warehouse"] = r.Warehouse, ["note"] = r.Notes ?? string.Empty,
            },
            Route, new Dictionary<string, string> { ["order"] = r.PublicId.ToString() }, r.RequestedAtUtc ?? r.CreatedAtUtc)).ToList();
        return new AttentionProviderResult(total, items, new AttentionGroupDto(ItemCode, BusinessModules.Warehouse, total, Route, new Dictionary<string, string> { ["urgent"] = "1" }));
    }
}

/// <summary>
/// Lote 222 (plan §1.7) — panel Ventas del Pulso del día (pulse.sales; datos: sales.view; módulo SALES_ORDERS). Cifras de hoy (fecha de la compañía) y de lo pendiente; QuickBooks y
/// consignación quedan en null hasta que sus partes las sumen.
/// </summary>
public sealed class SalesPulseService(TeikemDbContext db, ITenantClock clock, PermissionService permissions, QuickBooksService? quickBooks = null,
    ConsignmentService? consignment = null, ModuleService? modules = null, SalesAnalysisService? analysis = null)
{
    public async Task<PulseSalesPanelDto> GetAsync(CancellationToken ct)
    {
        await permissions.EnsureAsync(PermissionCatalog.SalesView, ct);
        var today = clock.Today;
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesCodes.StatusDomain).ToDictionaryAsync(s => s.InternalCode, s => s.StatusCodeId, ct);
        var cancelled = statuses.GetValueOrDefault(SalesOrderStatuses.Cancelled, -1);
        var open = new[] { SalesOrderStatuses.Requested, SalesOrderStatuses.Partial }.Where(statuses.ContainsKey).Select(c => statuses[c]).ToList();
        var todays = db.Set<SalesOrder>().AsNoTracking().Where(o => o.RequestedDate == today && o.StatusCodeId != cancelled);
        var ordersToday = await todays.CountAsync(ct);
        var amountToday = await todays.SumAsync(o => (decimal?)(o.Subtotal + o.TaxAmount + o.ShippingCharge + o.ShippingTaxAmount), ct) ?? 0m;
        var urgent = await db.Set<SalesOrder>().AsNoTracking().CountAsync(o => o.IsUrgent && open.Contains(o.StatusCodeId), ct);
        var awaiting = await db.Set<SalesOrder>().AsNoTracking().CountAsync(o => o.AwaitingClientApproval && open.Contains(o.StatusCodeId), ct);
        var prospectId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.ClientStatus && s.InternalCode == ClientStatuses.Prospect)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var toApprove = prospectId is int pid ? await db.Clients.AsNoTracking().CountAsync(c => c.IsActive && c.StatusCodeId == pid, ct) : 0;
        var routes = new Dictionary<string, string>
        {
            ["ordersToday"] = "/sales-orders", ["urgentOpen"] = "/sales-orders", ["clientsToApprove"] = "/clients", ["awaitingApproval"] = "/sales-orders",
        };
        // Lote 225: facturas por pasar a QuickBooks, solo para quien las puede ver (sales.quickbooks.view); si no, null
        int? toQuickBooks = null;
        if (quickBooks is not null && await permissions.HasPermissionAsync(PermissionCatalog.SalesQuickBooksView, ct))
        {
            toQuickBooks = (await quickBooks.CountPendingAsync(ct)).Count;
            routes["invoicesToQuickBooks"] = "/sales-orders?tab=quickbooks";
        }
        // Lote 224: facturas con saldo en consignación, para quien ve Facturación (billing.view o billing.consignment.pay) con el módulo encendido; si no, null
        int? consignmentUnpaid = null;
        if (consignment is not null && (modules is null || await modules.IsEnabledAsync(ModuleKeys.Billing, ct))
            && (await permissions.HasPermissionAsync(PermissionCatalog.BillingView, ct) || await permissions.HasPermissionAsync(PermissionCatalog.BillingConsignmentPay, ct)))
        {
            consignmentUnpaid = await consignment.CountUnpaidAsync(ct);
            routes["consignmentUnpaid"] = "/money/billing?tab=consignment";
        }
        // Lote 238: venta del mes vs. cuota, para quien ve el análisis de ventas (sales.analysis.view o manage); si no, null
        decimal? monthSales = null, monthQuota = null;
        if (analysis is not null && (await permissions.HasPermissionAsync(PermissionCatalog.SalesAnalysisView, ct)
                                     || await permissions.HasPermissionAsync(PermissionCatalog.SalesAnalysisManage, ct)))
        {
            (monthSales, monthQuota) = await analysis.MonthSalesVsQuotaAsync(ct);
            routes["monthSales"] = "/analytics/sales";
        }
        return new PulseSalesPanelDto(ordersToday, amountToday, urgent, toApprove, awaiting, toQuickBooks, consignmentUnpaid, routes, monthSales, monthQuota);
    }
}
