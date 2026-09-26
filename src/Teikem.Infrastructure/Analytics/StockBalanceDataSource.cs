using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P3) — fuente de datos STOCK_BALANCE (estado actual, sin rango de fecha) para la vista 'Inventario', 'Próximos a
/// vencer', los indicadores 'Inventario disponible' y 'Valor de inventario a costo/venta' y los gráficos por categoría.
/// Sigue el patrón de las fuentes existentes: AsNoTracking bajo el filtro de tenant (StockBalance es ITenantScoped), tope de
/// ClientDataSourceHelpers.MaxRows filas y respeto de q.Ids (relaciones). Sin q.Ids se omiten los saldos en cero (en mano y
/// reservado en 0): no aportan a ninguna vista ni indicador.
/// - Catálogos (producto, almacén, posición, zona, lote, categoría, dueño) por lotes con InventoryReadService (sin N+1); las
///   hijas sin TenantId se alcanzan por su padre filtrado.
/// - QtyAvailable = en mano − reservado calculado en código (la columna computada no se lee).
/// - CostValue/SaleValue = Round4(QtyOnHand × PurchaseCost/SalePrice) (D31).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class StockBalanceDataSource(TeikemDbContext db, InventoryReadService reads) : IDataSource
{
    public string Key => EntityTypes.StockBalance;
    public string LabelEs => "Inventario (saldos)";
    public string LabelEn => "Inventory (balances)";
    /// <summary>Los saldos no tienen campos personalizados.</summary>
    public string? EntityTypeCode => null;
    /// <summary>Estado actual: sin rango de fecha.</summary>
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("WarehouseId", "Id de almacén", "Warehouse id", DataFieldType.Number),
        new DataField("WarehouseCode", "Almacén", "Warehouse", DataFieldType.Text),
        new DataField("ZoneCode", "Zona", "Zone", DataFieldType.Text),
        new DataField("ZoneType", "Tipo de zona", "Zone type", DataFieldType.Text),
        new DataField("BinCode", "Posición", "Bin", DataFieldType.Text),
        new DataField("ProductId", "Id de producto", "Product id", DataFieldType.Number),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("ProductName", "Producto", "Product", DataFieldType.Text),
        new DataField("Category", "Categoría", "Category", DataFieldType.Text),
        new DataField("OwnerName", "Dueño", "Owner", DataFieldType.Text),
        new DataField("IsOwn", "Propio", "Own", DataFieldType.Bool),
        new DataField("LotNumber", "Lote", "Lot", DataFieldType.Text),
        new DataField("ExpiryDate", "Vencimiento", "Expiry date", DataFieldType.Date),
        new DataField("DaysToExpiry", "Días al vencimiento", "Days to expiry", DataFieldType.Number),
        new DataField("QtyOnHand", "En mano", "On hand", DataFieldType.Number),
        new DataField("QtyReserved", "Reservado", "Reserved", DataFieldType.Number),
        new DataField("QtyAvailable", "Disponible", "Available", DataFieldType.Number),
        new DataField("CostValue", "Valor a costo", "Cost value", DataFieldType.Number, IsMoney: true),
        new DataField("SaleValue", "Valor a venta", "Sale value", DataFieldType.Number, IsMoney: true),
        new DataField("UpdatedAtUtc", "Actualizado", "Updated", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Product", EntityTypes.Product, "ProductId", "Producto", "Product"),
        new DataRelation("Warehouse", EntityTypes.Warehouse, "WarehouseId", "Almacén", "Warehouse"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<StockBalance>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var ids = q.Ids.ToList();
            query = query.Where(b => ids.Contains(b.StockBalanceId));
        }
        else
        {
            query = query.Where(b => b.QtyOnHand != 0 || b.QtyReserved != 0);
        }

        var balances = await query.OrderBy(b => b.WarehouseId).ThenBy(b => b.ProductId).ThenBy(b => b.StockBalanceId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (balances.Count == 0) return new List<DataRow>();

        var refs = await reads.LoadBalanceRefsAsync(balances, ct);
        var today = ClientDataSourceHelpers.Today();
        var rows = new List<DataRow>(balances.Count);
        foreach (var b in balances)
        {
            if (!refs.Products.TryGetValue(b.ProductId, out var p)) continue;
            var bin = b.WarehouseBinId is int bid ? refs.Bins.GetValueOrDefault(bid) : null;
            var zone = bin is not null ? refs.Zones.GetValueOrDefault(bin.ZoneId) : null;
            var lot = b.LotId is int lid ? refs.Lots.GetValueOrDefault(lid) : null;
            rows.Add(new DataRow
            {
                ["Id"] = b.StockBalanceId,
                ["WarehouseId"] = b.WarehouseId,
                ["WarehouseCode"] = refs.Warehouses.GetValueOrDefault(b.WarehouseId)?.Code,
                ["ZoneCode"] = zone?.Code,
                ["ZoneType"] = zone?.TypeCode,
                ["BinCode"] = bin?.Code,
                ["ProductId"] = b.ProductId,
                ["Sku"] = p.Sku,
                ["ProductName"] = p.Name,
                ["Category"] = p.CategoryId is int cid ? refs.Categories.GetValueOrDefault(cid) : null,
                ["OwnerName"] = KardexRules.OwnerLabel(p.ClientId is int oid ? refs.Owners.GetValueOrDefault(oid) : null),
                ["IsOwn"] = p.ClientId is null,
                ["LotNumber"] = lot?.Number,
                ["ExpiryDate"] = lot?.Expiry,
                ["DaysToExpiry"] = KardexRules.DaysToExpiry(lot?.Expiry, today),
                ["QtyOnHand"] = b.QtyOnHand,
                ["QtyReserved"] = b.QtyReserved,
                ["QtyAvailable"] = InventoryRules.Available(b.QtyOnHand, b.QtyReserved),
                ["CostValue"] = InventoryReadService.Value(b.QtyOnHand, p.PurchaseCost),
                ["SaleValue"] = InventoryReadService.Value(b.QtyOnHand, p.SalePrice),
                ["UpdatedAtUtc"] = b.UpdatedAtUtc,
            });
        }
        return rows;
    }
}
