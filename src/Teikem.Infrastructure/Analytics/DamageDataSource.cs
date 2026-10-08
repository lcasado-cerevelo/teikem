using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// 2026-10-08 — fuente de datos DAMAGE_REPORT (daños activos del tenant) para vistas, indicadores y gráficos (daños por causa, origen, producto,
/// en cuarentena…). Actividad de período por ReportedAtUtc. AsNoTracking bajo el filtro de tenant, tope ClientDataSourceHelpers.MaxRows, respeto de
/// q.Ids y del rango (desde inclusivo, hasta exclusivo).
/// </summary>
public sealed class DamageDataSource(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups) : IDataSource
{
    public string Key => EntityTypes.DamageReport;
    public string LabelEs => "Daños";
    public string LabelEn => "Damage";
    public string? EntityTypeCode => null;
    public string? DateField => "ReportedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("Code", "Número de daño", "Damage number", DataFieldType.Text),
        new DataField("Origin", "Origen", "Origin", DataFieldType.Text),
        new DataField("OriginCode", "Código de origen", "Origin code", DataFieldType.Text),
        new DataField("Cause", "Causa", "Cause", DataFieldType.Text),
        new DataField("CauseCode", "Código de causa", "Cause code", DataFieldType.Text),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("Quantity", "Cantidad", "Quantity", DataFieldType.Number),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("ReceiptNumber", "Recibo", "Receipt", DataFieldType.Text),
        new DataField("ReportedAtUtc", "Reportado el", "Reported at", DataFieldType.Date),
        new DataField("ResolvedAtUtc", "Resuelto el", "Resolved at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<DamageReport>().AsNoTracking().Where(d => d.IsActive);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(d => wanted.Contains(d.DamageReportId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(d => d.ReportedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(d => d.ReportedAtUtc < to);
        var damages = await query.OrderByDescending(d => d.DamageReportId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (damages.Count == 0) return new List<DataRow>();

        var productIds = damages.Select(d => d.ProductId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => (p.Sku, p.Name), ct);
        var whIds = damages.Select(d => d.WarehouseId).Distinct().ToList();
        var warehouses = await db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Code, ct);
        var receiptIds = damages.Where(d => d.ReceiptHeaderId != null).Select(d => d.ReceiptHeaderId!.Value).Distinct().ToList();
        var receipts = await db.Set<ReceiptHeader>().AsNoTracking().Where(r => receiptIds.Contains(r.ReceiptHeaderId)).ToDictionaryAsync(r => r.ReceiptHeaderId, r => r.Number, ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.DamageStatus, ct);
        var lang = tenant.Lang;

        var rows = new List<DataRow>(damages.Count);
        foreach (var d in damages)
        {
            var origin = await lookups.GetAsync(d.OriginLookupId, ct);
            var cause = await lookups.GetAsync(d.CauseLookupId, ct);
            var p = products.GetValueOrDefault(d.ProductId);
            rows.Add(new DataRow
            {
                ["Id"] = d.DamageReportId,
                ["Code"] = DamageRules.Code(d.DamageReportId),
                ["Origin"] = origin is null ? null : MultilingualText.Resolve(origin.LabelJson, lang),
                ["OriginCode"] = origin?.InternalCode,
                ["Cause"] = cause is null ? null : MultilingualText.Resolve(cause.LabelJson, lang),
                ["CauseCode"] = cause?.InternalCode,
                ["WarehouseCode"] = warehouses.GetValueOrDefault(d.WarehouseId),
                ["Sku"] = p.Sku,
                ["ProductName"] = p.Name,
                ["Quantity"] = d.Quantity,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, d.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, d.StatusCodeId),
                ["ReceiptNumber"] = d.ReceiptHeaderId is int rid ? receipts.GetValueOrDefault(rid) : null,
                ["ReportedAtUtc"] = d.ReportedAtUtc,
                ["ResolvedAtUtc"] = d.ResolvedAtUtc,
            });
        }
        return rows;
    }
}
