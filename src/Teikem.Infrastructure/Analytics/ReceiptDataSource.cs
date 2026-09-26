using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P4) — fuente de datos RECEIPT (recibos activos del tenant) para vistas, indicadores y gráficos (vista
/// 'Recepciones con diferencia'). Actividad de período por ReceivedAtUtc (fecha de confirmación; los recibos abiertos no
/// tienen fecha y quedan fuera de un rango). Sigue el patrón de las fuentes del Lote 5: AsNoTracking bajo el filtro de
/// tenant, tope ClientDataSourceHelpers.MaxRows, respeto de q.Ids y del rango (desde inclusivo, hasta exclusivo).
/// - Las líneas (sin TenantId) se alcanzan SOLO por los ids de recibos ya filtrados; ASN, PO, proveedor y cliente por los
///   ids de esos recibos. Todo en consultas por lote (sin N+1).
/// - Origin: PO (aviso nacido de una orden de compra), ASN (aviso de cliente), BLIND o RETURN (ReceiptRules.OriginOf).
/// - ExpectedQty/VarianceQty/HasVariance con ReceiptRules (un ciego espera lo recibido; una línea extra de ASN espera 0).
/// - ReceivedCost = Σ recibido × UnitCost de la línea de PO (costo congelado, D35), Round4; NULL si el recibo no es de PO.
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class ReceiptDataSource(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups) : IDataSource
{
    public string Key => EntityTypes.Receipt;
    public string LabelEs => "Recepciones";
    public string LabelEn => "Receipts";
    /// <summary>Habilita los campos personalizados del recibo como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.Receipt;
    public string? DateField => "ReceivedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número de recibo", "Receipt number", DataFieldType.Text),
        new DataField("Type", "Tipo", "Type", DataFieldType.Text),
        new DataField("TypeCode", "Código de tipo", "Type code", DataFieldType.Text),
        new DataField("Origin", "Origen", "Origin", DataFieldType.Text),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("SupplierName", "Proveedor", "Supplier", DataFieldType.Text),
        new DataField("ClientName", "Cliente", "Client", DataFieldType.Text),
        new DataField("PurchaseOrderNumber", "Orden de compra", "Purchase order", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("LineCount", "Líneas", "Lines", DataFieldType.Number),
        new DataField("ExpectedQty", "Cantidad esperada", "Expected quantity", DataFieldType.Number),
        new DataField("ReceivedQty", "Cantidad recibida", "Received quantity", DataFieldType.Number),
        new DataField("VarianceQty", "Diferencia", "Variance", DataFieldType.Number),
        new DataField("HasVariance", "Con diferencia", "Has variance", DataFieldType.Bool),
        new DataField("ReceivedCost", "Costo recibido", "Received cost", DataFieldType.Number, IsMoney: true),
        new DataField("CreatedAtUtc", "Creado el", "Created at", DataFieldType.Date),
        new DataField("ReceivedAtUtc", "Confirmado el", "Received at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<ReceiptHeader>().AsNoTracking().Where(r => r.IsActive);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(r => wanted.Contains(r.ReceiptHeaderId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(r => r.ReceivedAtUtc != null && r.ReceivedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(r => r.ReceivedAtUtc != null && r.ReceivedAtUtc < to);

        var receipts = await query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.ReceiptHeaderId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (receipts.Count == 0) return new List<DataRow>();

        var ids = receipts.Select(r => r.ReceiptHeaderId).ToList();
        var whIds = receipts.Select(r => r.WarehouseId).Distinct().ToList();
        var asnIds = receipts.Where(r => r.AsnId != null).Select(r => r.AsnId!.Value).Distinct().ToList();

        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId))
            .ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var asns = await (from a in db.Set<Asn>().AsNoTracking()
                          where asnIds.Contains(a.AsnId)
                          join c in db.Set<Client>().AsNoTracking() on a.ClientId equals c.ClientId into cj
                          from c in cj.DefaultIfEmpty()
                          join p in db.Set<PurchaseOrder>().AsNoTracking() on a.PurchaseOrderId equals p.PurchaseOrderId into pj
                          from p in pj.DefaultIfEmpty()
                          join s in db.Set<Supplier>().AsNoTracking() on p.SupplierId equals s.SupplierId into sj
                          from s in sj.DefaultIfEmpty()
                          select new
                          {
                              a.AsnId, a.PurchaseOrderId,
                              ClientName = c == null ? null : c.Name,
                              PoNumber = p == null ? null : p.Number,
                              SupplierName = s == null ? null : s.Name,
                          }).ToDictionaryAsync(x => x.AsnId, ct);
        // Líneas por los recibos ya filtrados; el costo de la línea de PO por el enlace AsnLine → PurchaseOrderLine.
        var lines = await (from l in db.Set<ReceiptLine>().AsNoTracking()
                           where ids.Contains(l.ReceiptHeaderId)
                           join al in db.Set<AsnLine>().AsNoTracking() on l.AsnLineId equals al.AsnLineId into alj
                           from al in alj.DefaultIfEmpty()
                           join pl in db.Set<PurchaseOrderLine>().AsNoTracking() on al.PurchaseOrderLineId equals pl.PurchaseOrderLineId into plj
                           from pl in plj.DefaultIfEmpty()
                           select new
                           {
                               l.ReceiptHeaderId, l.ExpectedQty, l.ReceivedQty,
                               UnitCost = pl == null ? (decimal?)null : pl.UnitCost,
                           }).ToListAsync(ct);
        var linesByReceipt = lines.GroupBy(l => l.ReceiptHeaderId).ToDictionary(g => g.Key, g => g.ToList());
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.ReceiptStatus, ct);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(receipts.Count);
        foreach (var r in receipts)
        {
            var type = await lookups.GetAsync(r.ReceiptTypeLookupId, ct);
            var typeCode = type?.InternalCode ?? ReceiptTypes.Blind;
            var expects = ReceiptRules.ExpectsQuantities(typeCode);
            var a = r.AsnId is int aid ? asns.GetValueOrDefault(aid) : null;
            var fromPo = a?.PurchaseOrderId is not null;
            var own = linesByReceipt.GetValueOrDefault(r.ReceiptHeaderId) ?? new();
            var expected = own.Sum(l => ReceiptRules.ExpectedFor(expects, l.ExpectedQty, l.ReceivedQty));
            var received = own.Sum(l => l.ReceivedQty);
            decimal? cost = fromPo
                ? decimal.Round(own.Where(l => l.UnitCost != null).Sum(l => l.ReceivedQty * l.UnitCost!.Value), 4, MidpointRounding.AwayFromZero)
                : null;
            rows.Add(new DataRow
            {
                ["Id"] = r.ReceiptHeaderId,
                ["PublicId"] = r.PublicId.ToString(),
                ["Number"] = r.Number,
                ["Type"] = type is null ? typeCode : MultilingualText.Resolve(type.LabelJson, lang),
                ["TypeCode"] = typeCode,
                ["Origin"] = ReceiptRules.OriginOf(typeCode, a is not null, fromPo),
                ["WarehouseCode"] = warehouses.GetValueOrDefault(r.WarehouseId),
                ["SupplierName"] = a?.SupplierName,
                ["ClientName"] = a?.ClientName,
                ["PurchaseOrderNumber"] = a?.PoNumber,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, r.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, r.StatusCodeId),
                ["LineCount"] = own.Count,
                ["ExpectedQty"] = expected,
                ["ReceivedQty"] = received,
                ["VarianceQty"] = received - expected,
                ["HasVariance"] = own.Any(l => ReceiptRules.HasVariance(expects, l.ExpectedQty, l.ReceivedQty)),
                ["ReceivedCost"] = cost,
                ["CreatedAtUtc"] = r.CreatedAtUtc,
                ["ReceivedAtUtc"] = r.ReceivedAtUtc,
            });
        }
        return rows;
    }
}
