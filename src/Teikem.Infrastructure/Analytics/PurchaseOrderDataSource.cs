using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — fuente PURCHASE_ORDER, «Órdenes de compra»: una fila por orden ACTIVA de la compañía con su proveedor, almacén, estatus, origen
/// (TEIKEM / EXCEL / QUICKBOOKS; NULL = TEIKEM) y su estado frente a QuickBooks calculado con la MISMA función que el DTO (PurchaseOrderQuickBooksRules.State). Actividad de
/// período por OrderDate (filtro por día, como InvoiceDataSource). Abierta = SENT o PARTIAL; atrasada = abierta con la fecha esperada ya vencida (día de la compañía).
/// El total es Σ cantidad pedida × costo (nunca la columna computada LineTotal). Las líneas (sin TenantId) se alcanzan SOLO por los ids ya filtrados y se agregan en SQL.
/// AsNoTracking bajo el filtro global de tenant, tope ClientDataSourceHelpers.MaxRows y respeto de q.Ids. Lectura: purchasing.view (OwnerReadPermission) y el módulo
/// Compras encendido. Sin indicadores ni avisos sembrados.
/// </summary>
public sealed class PurchaseOrderDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => EntityTypes.PurchaseOrder;
    public string LabelEs => "Órdenes de compra";
    public string LabelEn => "Purchase orders";
    public string? EntityTypeCode => null;
    public string? DateField => "OrderDate";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.Purchasing;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de orden", "Order number", DataFieldType.Text),
        new DataField("SupplierId", "Id de proveedor", "Supplier id", DataFieldType.Number),
        new DataField("SupplierName", "Proveedor", "Supplier", DataFieldType.Text),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("IsOpen", "Abierta", "Open", DataFieldType.Bool),
        new DataField("IsLate", "Atrasada", "Late", DataFieldType.Bool),
        new DataField("OriginCode", "Código de origen", "Origin code", DataFieldType.Text),
        new DataField("Origin", "Origen", "Origin", DataFieldType.Text),
        new DataField("QuickBooksState", "Estado en QuickBooks", "QuickBooks state", DataFieldType.Text),
        new DataField("IsInQuickBooks", "En QuickBooks", "In QuickBooks", DataFieldType.Bool),
        new DataField("OrderDate", "Fecha de la orden", "Order date", DataFieldType.Date),
        new DataField("ExpectedDate", "Fecha esperada", "Expected date", DataFieldType.Date),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("UnitsOrdered", "Unidades pedidas", "Units ordered", DataFieldType.Number),
        new DataField("UnitsReceived", "Unidades recibidas", "Units received", DataFieldType.Number),
        new DataField("Total", "Total", "Total", DataFieldType.Number, IsMoney: true),
        new DataField("QuickBooksExportedAtUtc", "Exportada a QuickBooks el", "Exported to QuickBooks at", DataFieldType.Date),
        new DataField("QuickBooksAtUtc", "Marcada en QuickBooks el", "Marked in QuickBooks at", DataFieldType.Date),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.PurchaseOrders.AsNoTracking().Where(p => p.IsActive);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(p => wanted.Contains(p.PurchaseOrderId));
        }
        if ((q.FromDay ?? (q.FromUtc.HasValue ? DateOnly.FromDateTime(q.FromUtc.Value) : (DateOnly?)null)) is DateOnly from)
            query = query.Where(p => p.OrderDate >= from);
        DateOnly? toDay = q.ToDayExclusive;
        if (toDay is null && q.ToUtc.HasValue)
        {
            toDay = DateOnly.FromDateTime(q.ToUtc.Value);
            if (q.ToUtc.Value.TimeOfDay != TimeSpan.Zero) toDay = toDay.Value.AddDays(1);
        }
        if (toDay is DateOnly to) query = query.Where(p => p.OrderDate < to);

        var orders = await query.OrderByDescending(p => p.PurchaseOrderId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (orders.Count == 0) return new List<DataRow>();

        var ids = orders.Select(p => p.PurchaseOrderId).ToList();
        var lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId))
            .GroupBy(l => l.PurchaseOrderId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Ordered = g.Sum(l => l.QtyOrdered), Received = g.Sum(l => l.QtyReceived), Total = g.Sum(l => l.QtyOrdered * l.UnitCost) })
            .ToDictionaryAsync(x => x.Id, ct);
        var supplierIds = orders.Select(p => p.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.SupplierId)).ToDictionaryAsync(s => s.SupplierId, s => s.Name, ct);
        var warehouseIds = orders.Select(p => p.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var origins = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == PurchaseOrderOrigins.Domain).ToListAsync(ct);
        var originById = origins.ToDictionary(l => l.LookupCodeId);
        var teikemOrigin = origins.FirstOrDefault(l => l.InternalCode == PurchaseOrderOrigins.Teikem);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.PurchaseOrderStatus, ct);
        var today = ClientDataSourceHelpers.Today(clock);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(orders.Count);
        foreach (var p in orders)
        {
            var code = ClientDataSourceHelpers.StatusCodeOf(statusMap, p.StatusCodeId);
            var open = code is PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial;
            LookupCode? origin = p.OriginLookupId is int oid && originById.TryGetValue(oid, out var o) ? o : null;
            var originCode = PurchaseOrderOrigins.OrDefault(origin?.InternalCode);
            origin ??= originCode == PurchaseOrderOrigins.Teikem ? teikemOrigin : null;
            var agg = lines.GetValueOrDefault(p.PurchaseOrderId);
            rows.Add(new DataRow
            {
                ["Id"] = p.PurchaseOrderId,
                ["PublicId"] = p.PublicId.ToString(),
                ["Number"] = p.Number,
                ["SupplierId"] = p.SupplierId,
                ["SupplierName"] = suppliers.GetValueOrDefault(p.SupplierId),
                ["WarehouseId"] = p.WarehouseId,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(p.WarehouseId),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, p.StatusCodeId, lang),
                ["StatusCode"] = code,
                ["IsOpen"] = open,
                ["IsLate"] = open && p.ExpectedDate is DateOnly expected && expected < today,
                ["OriginCode"] = originCode,
                ["Origin"] = origin is null ? originCode : MultilingualText.Resolve(origin.LabelJson, lang),
                ["QuickBooksState"] = PurchaseOrderQuickBooksRules.State(originCode, code, p.QuickBooksExportedAt, p.QuickBooksAt),
                ["IsInQuickBooks"] = p.QuickBooksAt is not null,
                ["OrderDate"] = p.OrderDate,
                ["ExpectedDate"] = p.ExpectedDate,
                ["LineCount"] = agg?.Count ?? 0,
                ["UnitsOrdered"] = agg?.Ordered ?? 0m,
                ["UnitsReceived"] = agg?.Received ?? 0m,
                ["Total"] = agg?.Total ?? 0m,
                ["QuickBooksExportedAtUtc"] = p.QuickBooksExportedAt,
                ["QuickBooksAtUtc"] = p.QuickBooksAt,
                ["CreatedAtUtc"] = p.CreatedAtUtc,
            });
        }
        return rows;
    }
}
