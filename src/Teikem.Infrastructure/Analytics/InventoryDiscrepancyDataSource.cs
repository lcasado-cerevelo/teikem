using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 14 (P1) — fuente de datos INVENTORY_DISCREPANCY (descuadres Kárdex ↔ saldo) para vistas, indicadores y gráficos. El
/// indicador de sistema "Descuadres pendientes" queda para el Lote 15 (Pulso); la fuente ya existe.
/// - Actividad de período por DetectedAtUtc (cuándo se detectó).
/// - AsNoTracking bajo el filtro de tenant (InventoryDiscrepancy es ITenantScoped), tope ClientDataSourceHelpers.MaxRows (los
///   más recientes), respeto de q.Ids y del rango.
/// - Producto, almacén, posición y lote por lotes de consultas con InventoryReadService (sin N+1); Difference = saldo − Kárdex.
/// - Nombres de campo estables (un indicador futuro los usará).
/// </summary>
public sealed class InventoryDiscrepancyDataSource(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, InventoryReadService reads)
    : IDataSource
{
    public string Key => EntityTypes.InventoryDiscrepancy;
    public string LabelEs => "Descuadres de inventario";
    public string LabelEn => "Inventory discrepancies";
    /// <summary>Sin campos personalizados en el motor de análisis.</summary>
    public string? EntityTypeCode => null;
    /// <summary>Actividad de período por fecha de detección.</summary>
    public string? DateField => "DetectedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("ProductId", "Id de producto", "Product id", DataFieldType.Number),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("BinCode", "Posición", "Bin", DataFieldType.Text),
        new DataField("LotNumber", "Lote", "Lot", DataFieldType.Text),
        new DataField("LedgerQty", "Según Kárdex", "Per ledger", DataFieldType.Number),
        new DataField("BalanceQty", "Saldo", "Balance", DataFieldType.Number),
        new DataField("Difference", "Diferencia", "Difference", DataFieldType.Number),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("Kind", "Tipo", "Kind", DataFieldType.Text),
        new DataField("Trigger", "Origen", "Trigger", DataFieldType.Text),
        new DataField("IsOpen", "Pendiente", "Open", DataFieldType.Bool),
        new DataField("DetectedAtUtc", "Detectado el", "Detected at", DataFieldType.Date),
        new DataField("ClosedAtUtc", "Cerrado el", "Closed at", DataFieldType.Date),
        new DataField("ResolvedBy", "Resuelto por", "Resolved by", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Product", EntityTypes.Product, "ProductId", "Producto", "Product"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.InventoryDiscrepancies.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(d => wanted.Contains(d.InventoryDiscrepancyId));
        }
        if (q.FromUtc is DateTime from) query = query.Where(d => d.DetectedAtUtc >= from);
        if (q.ToUtc is DateTime to) query = query.Where(d => d.DetectedAtUtc < to);

        var rows = await query.OrderByDescending(d => d.DetectedAtUtc).ThenByDescending(d => d.InventoryDiscrepancyId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (rows.Count == 0) return new List<DataRow>();

        var products = await reads.ProductInfoAsync(rows.Select(r => r.ProductId).Distinct().ToList(), ct);
        var warehouses = await reads.WarehouseInfoAsync(rows.Where(r => r.WarehouseId.HasValue).Select(r => r.WarehouseId!.Value), ct);
        var bins = await reads.BinInfoAsync(rows.Where(r => r.WarehouseBinId.HasValue).Select(r => r.WarehouseBinId!.Value), ct);
        var lots = await reads.LotInfoAsync(rows.Where(r => r.LotId.HasValue).Select(r => r.LotId!.Value), ct);
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.InventoryDiscrepancyStatus, ct);
        var userIds = rows.Where(r => r.ResolvedBy.HasValue).Select(r => r.ResolvedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? u.UserName ?? "" })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var lang = tenant.Lang;

        var result = new List<DataRow>(rows.Count);
        foreach (var d in rows)
        {
            var p = products.GetValueOrDefault(d.ProductId);
            result.Add(new DataRow
            {
                ["Id"] = d.InventoryDiscrepancyId,
                ["ProductId"] = d.ProductId,
                ["Sku"] = p?.Sku,
                ["ProductName"] = p?.Name,
                ["WarehouseCode"] = d.WarehouseId is int w ? warehouses.GetValueOrDefault(w)?.Code : null,
                ["BinCode"] = d.WarehouseBinId is int b ? bins.GetValueOrDefault(b)?.Code : null,
                ["LotNumber"] = d.LotId is int l ? lots.GetValueOrDefault(l)?.Number : null,
                ["LedgerQty"] = d.LedgerQty,
                ["BalanceQty"] = d.BalanceQty,
                ["Difference"] = ReconciliationRules.Difference(d.LedgerQty, d.BalanceQty),
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, d.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, d.StatusCodeId),
                ["Kind"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, d.KindLookupId, lang, ct),
                ["Trigger"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, d.TriggerLookupId, lang, ct),
                ["IsOpen"] = d.ClosedAtUtc is null,
                ["DetectedAtUtc"] = d.DetectedAtUtc,
                ["ClosedAtUtc"] = d.ClosedAtUtc,
                ["ResolvedBy"] = d.ResolvedBy is int u ? users.GetValueOrDefault(u) : null,
            });
        }
        return result;
    }
}
