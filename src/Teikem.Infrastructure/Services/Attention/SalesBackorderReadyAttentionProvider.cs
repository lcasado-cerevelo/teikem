using System.Globalization;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 67 (P4 S4) — fila de "Necesita tu atención" por cada orden de venta abierta con backorder que YA se puede surtir (hay disponible para vender). Módulo Órdenes de
/// venta y sales.backorder (quien puede surtirlo). Una fila por orden, las más esperadas primero. "Revisar" abre la orden en Operación → Órdenes de venta; "Ver todos"
/// abre la lista (pestaña de backorders). Usa la misma cuenta que la vista de backorders (SalesOrderService.BackordersAsync), así que lo que avisa es lo que Surtir va
/// a reservar. Tono: warn (una orden esperando inventario que ya llegó), danger si lleva más de 7 días esperando.
/// Params (cadenas): publicId, number, client, warehouse, lines (líneas surtibles), units (unidades surtibles) y days (días de la línea que más espera).
/// </summary>
public sealed class SalesBackorderReadyAttentionProvider(SalesOrderService sales) : IAttentionItemProvider
{
    public const string ItemCode = "SALES_BACKORDER_READY";
    public const string Route = "/sales-orders";
    public const int LongWaitDays = 7;

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;
    public string RequiredPermission => PermissionCatalog.SalesBackorder;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var lines = await sales.BackordersAsync(null, ct);
        var orders = lines.Where(l => l.Fulfillable > 0m).GroupBy(l => l.OrderPublicId)
            .Select(g => new { First = g.OrderBy(l => l.RequestedAtUtc).First(), Lines = g.ToList() })
            .OrderBy(x => x.First.RequestedAtUtc).ThenBy(x => x.First.OrderNumber, StringComparer.Ordinal)
            .ToList();
        if (orders.Count == 0) return new AttentionProviderResult(0, Array.Empty<AttentionItemDto>(), null);

        var items = new List<AttentionItemDto>();
        foreach (var o in orders.Take(Math.Max(0, take)))
        {
            var days = o.Lines.Max(l => l.DaysWaiting);
            var publicId = o.First.OrderPublicId.ToString();
            var parameters = new Dictionary<string, string>
            {
                ["publicId"] = publicId,
                ["number"] = o.First.OrderNumber,
                ["client"] = o.First.ClientName,
                ["warehouse"] = o.First.WarehouseName,
                ["lines"] = o.Lines.Count.ToString(CultureInfo.InvariantCulture),
                ["units"] = o.Lines.Sum(l => l.Fulfillable).ToString("0.###", CultureInfo.InvariantCulture),
                ["days"] = days.ToString(CultureInfo.InvariantCulture),
            };
            items.Add(new AttentionItemDto(ItemCode, BusinessModules.Warehouse, days > LongWaitDays ? "danger" : "warn", 1, parameters, Route,
                new Dictionary<string, string> { ["order"] = publicId }, o.First.RequestedAtUtc));
        }
        var group = new AttentionGroupDto(ItemCode, BusinessModules.Warehouse, orders.Count, Route, null);
        return new AttentionProviderResult(orders.Count, items, group);
    }
}
