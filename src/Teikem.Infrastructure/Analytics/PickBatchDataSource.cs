using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P7) — fuente de datos PICK_BATCH (recolecciones del tenant, incluidas las eliminadas: IsActive distingue) para
/// vistas, indicadores y gráficos. Actividad de período por CollectedAtUtc. Mismo patrón que las fuentes del lote:
/// AsNoTracking bajo el filtro de tenant, tope ClientDataSourceHelpers.MaxRows, respeto de q.Ids y del rango (desde
/// inclusivo, hasta exclusivo).
/// - Las líneas (sin TenantId) se alcanzan SOLO por los ids de recolecciones ya filtradas; orden y cliente por los ids de esas
///   recolecciones. Todo en consultas por lote (sin N+1).
/// - TotalCost = Σ cantidad × costo congelado de la línea (D35), Round4 AwayFromZero; NULL si ninguna línea tiene costo.
/// - ClientInvoiceNumber: la factura copiada al lote al empacar (R40) o, en su defecto, la de la orden.
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// - 2026-10-11 — despacho manual (DMA-#####): la misma fuente lo incluye; IsManual lo distingue y ReasonCode/Reason/Note dan su
///   motivo (con el override de etiqueta de la compañía) y su nota. No hay fuente aparte: es la misma tabla y el mismo EntityType.
/// </summary>
public sealed class PickBatchDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.PickBatch;
    public string LabelEs => "Recolecciones";
    public string LabelEn => "Pick batches";
    public string? EntityTypeCode => EntityTypes.PickBatch;
    public string? DateField => "CollectedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de recolección", "Pick batch number", DataFieldType.Text),
        new DataField("CollectedAtUtc", "Recolectada el", "Collected at", DataFieldType.Date),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("TotalQty", "Cantidad total", "Total quantity", DataFieldType.Number),
        new DataField("TotalCost", "Costo total", "Total cost", DataFieldType.Number, IsMoney: true),
        new DataField("PackBatchNumber", "Número de empaque", "Pack batch number", DataFieldType.Text),
        new DataField("OrderNumber", "Número de orden", "Order number", DataFieldType.Text),
        new DataField("ClientInvoiceNumber", "Factura del cliente", "Client invoice", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("PackedAtUtc", "Empacada el", "Packed at", DataFieldType.Date),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        // 2026-10-11 — despacho manual
        new DataField("IsManual", "Despacho manual", "Manual issue", DataFieldType.Bool),
        new DataField("ReasonCode", "Código del motivo", "Reason code", DataFieldType.Text),
        new DataField("Reason", "Motivo del despacho manual", "Manual issue reason", DataFieldType.Text),
        new DataField("Note", "Nota", "Note", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<PickBatch>().AsNoTracking();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(b => wanted.Contains(b.PickBatchId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(b => b.CollectedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(b => b.CollectedAtUtc < to);

        var batches = await query.OrderByDescending(b => b.CollectedAtUtc).ThenByDescending(b => b.PickBatchId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (batches.Count == 0) return new List<DataRow>();

        var ids = batches.Select(b => b.PickBatchId).ToList();
        var whIds = batches.Select(b => b.WarehouseId).Distinct().ToList();
        var orderIds = batches.Where(b => b.TransportOrderId != null).Select(b => b.TransportOrderId!.Value).Distinct().ToList();

        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var lines = await db.Set<PickBatchLine>().AsNoTracking().Where(l => ids.Contains(l.PickBatchId))
            .Select(l => new { l.PickBatchId, l.Quantity, l.UnitCost }).ToListAsync(ct);
        var linesByBatch = lines.GroupBy(l => l.PickBatchId).ToDictionary(g => g.Key, g => g.ToList());
        var orders = await (from o in db.TransportOrders.AsNoTracking()
                            join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId
                            where orderIds.Contains(o.TransportOrderId)
                            select new { o.TransportOrderId, o.OrderNumber, o.PackBatchNumber, o.ClientInvoiceNumber, ClientName = c.Name })
            .ToDictionaryAsync(o => o.TransportOrderId, ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.PickBatchStatus, ct);
        var lang = tenant.Lang;
        var reasonIds = batches.Where(b => b.ManualIssueReasonId != null).Select(b => b.ManualIssueReasonId!.Value).Distinct().ToList();
        var reasons = reasonIds.Count == 0
            ? new Dictionary<int, (string Code, string Label)>()
            : (await (from l in db.LookupCodes.AsNoTracking()
                      join o in db.LookupCodeOverrides.AsNoTracking() on l.LookupCodeId equals o.LookupCodeId into oj
                      from o in oj.DefaultIfEmpty()
                      where reasonIds.Contains(l.LookupCodeId)
                      select new { l.LookupCodeId, l.InternalCode, l.LabelJson, Custom = o == null ? null : o.CustomLabelJson }).ToListAsync(ct))
                .ToDictionary(x => x.LookupCodeId, x => (Code: x.InternalCode, Label: MultilingualText.Resolve(MultilingualText.Merge(x.LabelJson, x.Custom), lang)));

        var rows = new List<DataRow>(batches.Count);
        foreach (var b in batches)
        {
            var own = linesByBatch.GetValueOrDefault(b.PickBatchId) ?? new();
            var order = b.TransportOrderId is int oid ? orders.GetValueOrDefault(oid) : null;
            rows.Add(new DataRow
            {
                ["Id"] = b.PickBatchId,
                ["PublicId"] = b.PublicId.ToString(),
                ["Number"] = b.Number,
                ["CollectedAtUtc"] = b.CollectedAtUtc,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, b.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, b.StatusCodeId),
                ["WarehouseCode"] = warehouses.GetValueOrDefault(b.WarehouseId),
                ["LineCount"] = own.Count,
                ["TotalQty"] = own.Sum(l => l.Quantity),
                ["TotalCost"] = PickBatchRules.TotalCost(own.Select(l => (l.Quantity, l.UnitCost))),
                // el número EMP de la recolección ES el de empaque (D10); un despacho manual (DMA) no tiene número de empaque
                ["PackBatchNumber"] = order?.PackBatchNumber ?? (b.ManualIssueReasonId != null ? null : b.Number),
                ["OrderNumber"] = order?.OrderNumber,
                ["ClientInvoiceNumber"] = b.ClientInvoiceNumber ?? order?.ClientInvoiceNumber,
                ["ClientName"] = order?.ClientName,
                ["PackedAtUtc"] = b.PackedAtUtc,
                ["IsActive"] = b.IsActive,
                ["IsManual"] = b.ManualIssueReasonId != null,
                ["ReasonCode"] = b.ManualIssueReasonId is int rc && reasons.TryGetValue(rc, out var r1) ? r1.Code : null,
                ["Reason"] = b.ManualIssueReasonId is int rl && reasons.TryGetValue(rl, out var r2) ? r2.Label : null,
                ["Note"] = b.Note,
            });
        }
        return rows;
    }
}
