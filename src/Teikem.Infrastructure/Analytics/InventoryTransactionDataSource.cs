using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P3) — fuente de datos INVENTORY_TRANSACTION (actividad por CreatedAtUtc) para las vistas 'Kárdex de movimientos',
/// 'Movimientos por tipo' (agrupada por TxnType con suma de Quantity, L874) y 'Ajustes de inventario', el indicador
/// 'Movimientos registrados' y los gráficos por tipo, usuario y día.
/// - Quantity = la cantidad del ledger CON signo (D3, L331: despacho negativo, recepción positiva; TRANSFER positiva con
///   origen y destino). SignedQuantity = la perspectiva sin filtro de ubicación (TRANSFER = 0): sumarla da el cambio neto de
///   existencia. Date = día UTC del movimiento (para agrupar por día).
/// - AsNoTracking bajo el filtro de tenant (InventoryTransaction es ITenantScoped), rango q.FromUtc (inclusivo) / q.ToUtc
///   (exclusivo) sobre CreatedAtUtc, respeto de q.Ids y tope de ClientDataSourceHelpers.MaxRows (los más recientes).
/// - Producto, ubicaciones, lote, serie, motivo, origen legible y usuario se resuelven por lotes con InventoryReadService.
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class InventoryTransactionDataSource(TeikemDbContext db, InventoryReadService reads) : IDataSource
{
    public string Key => EntityTypes.InventoryTransaction;
    public string LabelEs => "Movimientos de inventario";
    public string LabelEn => "Inventory transactions";
    /// <summary>Los movimientos del ledger no tienen campos personalizados.</summary>
    public string? EntityTypeCode => null;
    /// <summary>Actividad de período por fecha del movimiento.</summary>
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("CreatedAtUtc", "Fecha y hora", "Date and time", DataFieldType.Date),
        new DataField("Date", "Fecha", "Date", DataFieldType.Date),
        new DataField("TxnType", "Tipo", "Type", DataFieldType.Text),
        new DataField("TxnTypeCode", "Código de tipo", "Type code", DataFieldType.Text),
        new DataField("ProductId", "Id de producto", "Product id", DataFieldType.Number),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("Category", "Categoría", "Category", DataFieldType.Text),
        new DataField("Quantity", "Cantidad", "Quantity", DataFieldType.Number),
        new DataField("SignedQuantity", "Cambio neto", "Net change", DataFieldType.Number),
        new DataField("FromWarehouse", "Almacén origen", "From warehouse", DataFieldType.Text),
        new DataField("FromBin", "Posición origen", "From bin", DataFieldType.Text),
        new DataField("ToWarehouse", "Almacén destino", "To warehouse", DataFieldType.Text),
        new DataField("ToBin", "Posición destino", "To bin", DataFieldType.Text),
        new DataField("Position", "Posición", "Position", DataFieldType.Text),
        new DataField("LotNumber", "Lote", "Lot", DataFieldType.Text),
        new DataField("SerialNumber", "Serie", "Serial", DataFieldType.Text),
        new DataField("RefEntity", "Documento", "Document type", DataFieldType.Text),
        new DataField("RefId", "Id del documento", "Document id", DataFieldType.Number),
        new DataField("RefLabel", "Origen", "Source", DataFieldType.Text),
        new DataField("Reason", "Motivo", "Reason", DataFieldType.Text),
        new DataField("ReasonCode", "Código de motivo", "Reason code", DataFieldType.Text),
        new DataField("UserName", "Usuario", "User", DataFieldType.Text),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Product", EntityTypes.Product, "ProductId", "Producto", "Product"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<InventoryTransaction>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var ids = q.Ids.Select(i => (long)i).ToList();
            query = query.Where(t => ids.Contains(t.InventoryTransactionId));
        }
        if (q.FromUtc.HasValue) query = query.Where(t => t.CreatedAtUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(t => t.CreatedAtUtc < q.ToUtc.Value);

        var txns = await query.OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.InventoryTransactionId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (txns.Count == 0) return new List<DataRow>();

        // Filas del Kárdex sin filtro de ubicación (SignedQuantity: TRANSFER = 0) y categorías de sus productos.
        var kardex = await reads.ToKardexRowsAsync(txns, KardexLocationFilter.None, ct);
        var products = await reads.ProductInfoAsync(txns.Select(t => t.ProductId).Distinct().ToList(), ct);
        var categories = await reads.CategoryNamesAsync(products.Values.Where(p => p.CategoryId.HasValue).Select(p => p.CategoryId!.Value), ct);

        var rows = new List<DataRow>(txns.Count);
        for (var i = 0; i < txns.Count; i++)
        {
            var t = txns[i];
            var k = kardex[i];
            var p = products.GetValueOrDefault(t.ProductId);
            rows.Add(new DataRow
            {
                ["Id"] = t.InventoryTransactionId,
                ["CreatedAtUtc"] = t.CreatedAtUtc,
                ["Date"] = t.CreatedAtUtc.Date,
                ["TxnType"] = k.Type,
                ["TxnTypeCode"] = k.TypeCode,
                ["ProductId"] = t.ProductId,
                ["Sku"] = k.Sku,
                ["ProductName"] = k.ProductName,
                ["Category"] = p?.CategoryId is int cid ? categories.GetValueOrDefault(cid) : null,
                ["Quantity"] = t.Quantity,
                ["SignedQuantity"] = k.SignedQuantity,
                ["FromWarehouse"] = k.FromWarehouseCode,
                ["FromBin"] = k.FromBinCode,
                ["ToWarehouse"] = k.ToWarehouseCode,
                ["ToBin"] = k.ToBinCode,
                ["Position"] = k.Position,
                ["LotNumber"] = k.LotNumber,
                ["SerialNumber"] = k.SerialNumber,
                ["RefEntity"] = k.RefEntityCode,
                ["RefId"] = k.RefId,
                ["RefLabel"] = k.RefLabel,
                ["Reason"] = k.Reason,
                ["ReasonCode"] = k.ReasonCode,
                ["UserName"] = k.UserName,
            });
        }
        return rows;
    }
}
