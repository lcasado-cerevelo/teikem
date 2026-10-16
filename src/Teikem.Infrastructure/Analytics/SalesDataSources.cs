using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 67 (P4 S4) — fuentes de datos de las órdenes de venta para vistas, indicadores y gráficos: SALES_ORDER (una fila por orden) y SALES_ORDER_LINE (una fila por
/// línea). Siguen el patrón de las fuentes de Rentas: AsNoTracking bajo el filtro global de tenant (ambas son ITenantScoped), tope ClientDataSourceHelpers.MaxRows,
/// respeto de q.Ids y del rango (desde inclusivo, hasta exclusivo) sobre CreatedAtUtc. Lectura: sales.view y el módulo Órdenes de venta encendido (TenantModule). Los
/// borradores no se excluyen (el filtro IsOpen / StatusCode de cada definición los deja fuera). Nombres de campo estables: los usa SystemAnalyticsSeeder.
/// </summary>
internal static class SalesDataSourceHelpers
{
    public static async Task<Dictionary<int, string>> ClientNamesAsync(TeikemDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var list = ids.ToList();
        return await db.Clients.AsNoTracking().Where(c => list.Contains(c.ClientId)).Select(c => new { c.ClientId, c.Name }).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
    }

    public static async Task<Dictionary<int, string>> WarehouseCodesAsync(TeikemDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var list = ids.ToList();
        return await db.Warehouses.AsNoTracking().Where(w => list.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
    }
}

/// <summary>
/// Fuente SALES_ORDER. Actividad de período por CreatedAtUtc. IsOpen = solicitada o despachada parcial; HasBackorder = alguna línea espera inventario; IsLate = abierta con
/// fecha requerida anterior a hoy (día de la compañía). FillRatePct y DaysToShip solo tienen valor en órdenes despachadas por completo (DaysToShip = de solicitarla al
/// último cambio a Despachada en el historial de estatus).
/// </summary>
public sealed class SalesOrderDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => SalesCodes.EntityType;
    public string LabelEs => "Órdenes de venta";
    public string LabelEn => "Sales orders";
    public string? EntityTypeCode => null;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de orden", "Order number", DataFieldType.Text),
        new DataField("ClientId", "Id de cliente", "Client id", DataFieldType.Number),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Abierta", "Open", DataFieldType.Bool),
        new DataField("HasBackorder", "Con backorder", "Has backorder", DataFieldType.Bool),
        new DataField("IsLate", "Atrasada", "Late", DataFieldType.Bool),
        new DataField("RequestedDate", "Fecha del pedido", "Order date", DataFieldType.Date),
        new DataField("RequiredDate", "Fecha requerida", "Required date", DataFieldType.Date),
        new DataField("RequestedAtUtc", "Solicitada el", "Requested at", DataFieldType.Date),
        new DataField("ShippedAtUtc", "Despachada el", "Shipped at", DataFieldType.Date),
        new DataField("DaysToShip", "Días a despachar", "Days to ship", DataFieldType.Number),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("UnitsOrdered", "Unidades pedidas", "Units ordered", DataFieldType.Number),
        new DataField("UnitsReserved", "Unidades reservadas", "Units reserved", DataFieldType.Number),
        new DataField("UnitsShipped", "Unidades despachadas", "Units shipped", DataFieldType.Number),
        new DataField("UnitsBackordered", "Unidades en backorder", "Units backordered", DataFieldType.Number),
        new DataField("FillRatePct", "Cumplimiento (%)", "Fill rate (%)", DataFieldType.Number),
        new DataField("Subtotal", "Subtotal", "Subtotal", DataFieldType.Number, IsMoney: true),
        new DataField("TaxAmount", "Impuesto", "Tax", DataFieldType.Number, IsMoney: true),
        new DataField("Total", "Total", "Total", DataFieldType.Number, IsMoney: true),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Client", EntityTypes.Client, "ClientId", "Cliente", "Client"),
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<SalesOrder>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(o => wanted.Contains(o.SalesOrderId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(o => o.CreatedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(o => o.CreatedAtUtc < to);
        var orders = await query.OrderByDescending(o => o.SalesOrderId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (orders.Count == 0) return new List<DataRow>();

        var ids = orders.Select(o => o.SalesOrderId).ToList();
        var lines = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => ids.Contains(l.SalesOrderId))
            .GroupBy(l => l.SalesOrderId)
            .Select(g => new
            {
                OrderId = g.Key,
                Count = g.Count(),
                Ordered = g.Sum(l => l.QtyOrdered),
                Reserved = g.Sum(l => l.QtyReserved),
                Shipped = g.Sum(l => l.QtyShipped),
                Backordered = g.Where(l => !l.IsClosed).Sum(l => l.QtyBackordered),
            })
            .ToDictionaryAsync(x => x.OrderId, ct);
        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, orders.Select(o => o.ClientId).Distinct().ToList(), ct);
        var warehouses = await SalesDataSourceHelpers.WarehouseCodesAsync(db, orders.Select(o => o.WarehouseId).Distinct().ToList(), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, SalesCodes.StatusDomain, ct);
        var shippedId = statusMap.Values.FirstOrDefault(s => s.InternalCode == SalesOrderStatuses.Shipped)?.StatusCodeId;
        var entityTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, SalesCodes.EntityType, ct);
        var shippedAt = new Dictionary<int, DateTime>();
        if (shippedId is int sid && entityTypeId is int etid)
            shippedAt = await db.Set<EntityStatusHistory>().AsNoTracking().Where(h => h.EntityTypeLookupId == etid && h.ToStatusCodeId == sid && ids.Contains(h.EntityId))
                .GroupBy(h => h.EntityId).Select(g => new { Id = g.Key, At = g.Max(h => h.ChangedAtUtc) }).ToDictionaryAsync(x => x.Id, x => x.At, ct);
        var today = ClientDataSourceHelpers.Today(clock);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(orders.Count);
        foreach (var o in orders)
        {
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, o.StatusCodeId);
            var l = lines.GetValueOrDefault(o.SalesOrderId);
            var done = code == SalesOrderStatuses.Shipped;
            DateTime? shipped = done && shippedAt.TryGetValue(o.SalesOrderId, out var at) ? at : null;
            rows.Add(new DataRow
            {
                ["Id"] = o.SalesOrderId,
                ["PublicId"] = o.PublicId.ToString(),
                ["Number"] = o.Number,
                ["ClientId"] = o.ClientId,
                ["ClientName"] = clients.GetValueOrDefault(o.ClientId),
                ["WarehouseId"] = o.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(o.WarehouseId),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, o.StatusCodeId, lang),
                ["StatusCode"] = code,
                ["IsOpen"] = SalesAnalyticsRules.IsOpen(code),
                ["HasBackorder"] = (l?.Backordered ?? 0m) > 0m,
                ["IsLate"] = SalesAnalyticsRules.IsLate(code, o.RequiredDate, today),
                ["RequestedDate"] = o.RequestedDate,
                ["RequiredDate"] = o.RequiredDate,
                ["RequestedAtUtc"] = o.RequestedAtUtc,
                ["ShippedAtUtc"] = shipped,
                ["DaysToShip"] = done ? SalesAnalyticsRules.DaysToShip(o.RequestedAtUtc, shipped) : null,
                ["LineCount"] = l?.Count ?? 0,
                ["UnitsOrdered"] = l?.Ordered ?? 0m,
                ["UnitsReserved"] = l?.Reserved ?? 0m,
                ["UnitsShipped"] = l?.Shipped ?? 0m,
                ["UnitsBackordered"] = l?.Backordered ?? 0m,
                ["FillRatePct"] = done ? SalesAnalyticsRules.FillRatePct(l?.Ordered ?? 0m, l?.Shipped ?? 0m) : null,
                ["Subtotal"] = o.Subtotal,
                ["TaxAmount"] = o.TaxAmount,
                ["Total"] = o.Subtotal + o.TaxAmount,
                ["CreatedAtUtc"] = o.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>
/// Fuente SALES_ORDER_LINE: una fila por línea con los datos de su orden (número, cliente, almacén, estatus). Actividad de período por la fecha de creación de la orden
/// (CreatedAtUtc). DaysWaiting = días que lleva esperando un backorder pendiente (no cerrado) desde que se solicitó la orden.
/// </summary>
public sealed class SalesOrderLineDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.SalesOrderLine;
    public string LabelEs => "Líneas de órdenes de venta";
    public string LabelEn => "Sales order lines";
    public string? EntityTypeCode => null;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("OrderId", "Id de la orden", "Order id", DataFieldType.Number),
        new DataField("OrderNumber", "Orden", "Order", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("OrderStatus", "Estatus de la orden", "Order status", DataFieldType.Text),
        new DataField("OrderStatusCode", "Código de estatus de la orden", "Order status code", DataFieldType.Text),
        new DataField("IsOpen", "Orden abierta", "Order open", DataFieldType.Bool),
        new DataField("LineNo", "Línea", "Line", DataFieldType.Number),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("QtyOrdered", "Pedido", "Ordered", DataFieldType.Number),
        new DataField("QtyReserved", "Reservado", "Reserved", DataFieldType.Number),
        new DataField("QtyShipped", "Despachado", "Shipped", DataFieldType.Number),
        new DataField("QtyBackordered", "En backorder", "Backordered", DataFieldType.Number),
        new DataField("IsClosed", "Cerrada con faltante", "Closed short", DataFieldType.Bool),
        new DataField("DaysWaiting", "Días esperando", "Days waiting", DataFieldType.Number),
        new DataField("UnitPrice", "Precio", "Price", DataFieldType.Number, IsMoney: true),
        new DataField("LineTotal", "Total de la línea", "Line total", DataFieldType.Number, IsMoney: true),
        new DataField("TaxAmount", "Impuesto", "Tax", DataFieldType.Number, IsMoney: true),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = from l in db.Set<SalesOrderLine>().AsNoTracking()
                    join o in db.Set<SalesOrder>().AsNoTracking() on l.SalesOrderId equals o.SalesOrderId
                    select new { Line = l, Order = o };
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(x => wanted.Contains(x.Line.SalesOrderLineId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(x => x.Order.CreatedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(x => x.Order.CreatedAtUtc < to);
        var items = await query.OrderByDescending(x => x.Line.SalesOrderLineId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (items.Count == 0) return new List<DataRow>();

        var clients = await SalesDataSourceHelpers.ClientNamesAsync(db, items.Select(x => x.Order.ClientId).Distinct().ToList(), ct);
        var warehouses = await SalesDataSourceHelpers.WarehouseCodesAsync(db, items.Select(x => x.Order.WarehouseId).Distinct().ToList(), ct);
        var productIds = items.Select(x => x.Line.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => (p.Sku, p.Name), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, SalesCodes.StatusDomain, ct);
        var lang = tenant.Lang;
        var now = DateTime.UtcNow;

        var rows = new List<DataRow>(items.Count);
        foreach (var x in items)
        {
            var l = x.Line;
            var o = x.Order;
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, o.StatusCodeId);
            var p = products.GetValueOrDefault(l.ProductId);
            rows.Add(new DataRow
            {
                ["Id"] = l.SalesOrderLineId,
                ["OrderId"] = o.SalesOrderId,
                ["OrderNumber"] = o.Number,
                ["ClientName"] = clients.GetValueOrDefault(o.ClientId),
                ["WarehouseCode"] = warehouses.GetValueOrDefault(o.WarehouseId),
                ["OrderStatus"] = ClientDataSourceHelpers.StatusLabel(statusMap, o.StatusCodeId, lang),
                ["OrderStatusCode"] = code,
                ["IsOpen"] = SalesAnalyticsRules.IsOpen(code),
                ["LineNo"] = l.LineNo,
                ["Sku"] = p.Sku,
                ["ProductName"] = p.Name,
                ["QtyOrdered"] = l.QtyOrdered,
                ["QtyReserved"] = l.QtyReserved,
                ["QtyShipped"] = l.QtyShipped,
                ["QtyBackordered"] = l.QtyBackordered,
                ["IsClosed"] = l.IsClosed,
                ["DaysWaiting"] = SalesAnalyticsRules.DaysWaiting(l.QtyBackordered, l.IsClosed, o.RequestedAtUtc, now),
                ["UnitPrice"] = l.UnitPrice,
                ["LineTotal"] = l.QtyOrdered * l.UnitPrice,
                ["TaxAmount"] = l.TaxAmount,
                ["CreatedAtUtc"] = o.CreatedAtUtc,
            });
        }
        return rows;
    }
}
