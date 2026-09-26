using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 6 (P2) — fuente de datos PRODUCT (estado actual, sin rango de fecha) para vistas, indicadores y gráficos: 'Inventario
/// bajo mínimo', 'Productos por cliente dueño', 'Productos activos', 'Valor de inventario a costo/venta', 'Productos por
/// categoría'. Sigue el patrón de las fuentes de Flota: AsNoTracking bajo el filtro de tenant, tope de
/// ClientDataSourceHelpers.MaxRows, respeto de q.Ids, totales de saldo agrupados en UNA consulta (sin N+1).
/// - Dueño = cliente 3PL (OwnerName) o 'Propio' (IsOwn), nunca el TenantId (R31).
/// - QtyAvailable = en mano − reservado calculado en código (la columna computada no se lee).
/// - CostValue = Round4(QtyOnHand × PurchaseCost); SaleValue = Round4(QtyOnHand × SalePrice) (D31).
/// - IsBelowMin = MinQty != null y disponible &lt; MinQty y producto activo (R32).
/// - BaseUom y TrackingType se exponen con su código de catálogo (UN, LOT, SERIAL...).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (AnalyticsSeedFieldsTests lo verifica).
/// </summary>
public sealed class ProductDataSource(TeikemDbContext db, ILookupCache lookups) : IDataSource
{
    public string Key => EntityTypes.Product;
    public string LabelEs => "Productos";
    public string LabelEn => "Products";
    /// <summary>Habilita los campos personalizados del producto como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.Product;
    /// <summary>Estado actual: sin rango de fecha.</summary>
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Sku", "SKU", "SKU", DataFieldType.Text),
        new DataField("Name", "Producto", "Product", DataFieldType.Text),
        new DataField("CategoryId", "Id de categoría", "Category id", DataFieldType.Number),
        new DataField("Category", "Categoría", "Category", DataFieldType.Text),
        new DataField("OwnerClientId", "Id del cliente dueño", "Owner client id", DataFieldType.Number),
        new DataField("OwnerName", "Dueño", "Owner", DataFieldType.Text),
        new DataField("IsOwn", "Propio", "Own", DataFieldType.Bool),
        new DataField("BaseUom", "Unidad base", "Base UoM", DataFieldType.Text),
        new DataField("TrackingType", "Seguimiento", "Tracking", DataFieldType.Text),
        new DataField("Barcode", "Código de barras", "Barcode", DataFieldType.Text),
        new DataField("PurchaseCost", "Costo de compra", "Purchase cost", DataFieldType.Number, IsMoney: true),
        new DataField("SalePrice", "Precio de venta", "Sale price", DataFieldType.Number, IsMoney: true),
        new DataField("QtyOnHand", "En mano", "On hand", DataFieldType.Number),
        new DataField("QtyReserved", "Reservado", "Reserved", DataFieldType.Number),
        new DataField("QtyAvailable", "Disponible", "Available", DataFieldType.Number),
        new DataField("CostValue", "Valor a costo", "Cost value", DataFieldType.Number, IsMoney: true),
        new DataField("SaleValue", "Valor a venta", "Sale value", DataFieldType.Number, IsMoney: true),
        new DataField("MinQty", "Mínimo", "Minimum", DataFieldType.Number),
        new DataField("IsBelowMin", "Bajo mínimo", "Below minimum", DataFieldType.Bool),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Owner", EntityTypes.Client, "OwnerClientId", "Cliente dueño", "Owner client"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Set<Product>().AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var ids = q.Ids.ToList();
            query = query.Where(p => ids.Contains(p.ProductId));
        }
        var products = await query.OrderBy(p => p.Sku).ThenBy(p => p.ProductId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (products.Count == 0) return new List<DataRow>();

        // Totales de saldo por producto: una consulta agrupada. Sin q.Ids se agrupa todo el tenant (evita mandar hasta
        // 20 000 ids como parámetros).
        var balances = db.Set<StockBalance>().AsNoTracking();
        if (q.Ids is not null)
        {
            var ids = products.Select(p => p.ProductId).ToList();
            balances = balances.Where(b => ids.Contains(b.ProductId));
        }
        var totals = (await balances.GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, OnHand = g.Sum(b => b.QtyOnHand), Reserved = g.Sum(b => b.QtyReserved) })
                .ToListAsync(ct))
            .ToDictionary(x => x.ProductId, x => (x.OnHand, x.Reserved));

        var ownerIds = products.Where(p => p.ClientId.HasValue).Select(p => p.ClientId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Set<Client>().AsNoTracking().Where(c => ownerIds.Contains(c.ClientId))
                .ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);

        var categories = await db.Set<ProductCategory>().AsNoTracking()
            .ToDictionaryAsync(c => c.ProductCategoryId, c => c.Name, ct);

        var rows = new List<DataRow>(products.Count);
        foreach (var p in products)
        {
            var (onHand, reserved) = totals.TryGetValue(p.ProductId, out var t) ? t : (0m, 0m);
            var available = ProductRules.Available(onHand, reserved);
            var ownerName = p.ClientId is int oid ? owners.GetValueOrDefault(oid) : null;
            rows.Add(new DataRow
            {
                ["Id"] = p.ProductId,
                ["PublicId"] = p.PublicId.ToString(),
                ["Sku"] = p.Sku,
                ["Name"] = p.Name,
                ["CategoryId"] = p.ProductCategoryId,
                ["Category"] = p.ProductCategoryId is int cid ? categories.GetValueOrDefault(cid) : null,
                ["OwnerClientId"] = p.ClientId,
                ["OwnerName"] = ProductRules.OwnerLabel(ownerName),
                ["IsOwn"] = p.ClientId is null,
                ["BaseUom"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, p.BaseUomLookupId, ct),
                ["TrackingType"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, p.TrackingTypeLookupId, ct),
                ["Barcode"] = p.Barcode,
                ["PurchaseCost"] = p.PurchaseCost,
                ["SalePrice"] = p.SalePrice,
                ["QtyOnHand"] = onHand,
                ["QtyReserved"] = reserved,
                ["QtyAvailable"] = available,
                ["CostValue"] = ProductRules.Value(onHand, p.PurchaseCost),
                ["SaleValue"] = ProductRules.Value(onHand, p.SalePrice),
                ["MinQty"] = p.MinQty,
                ["IsBelowMin"] = ProductRules.IsBelowMin(p.MinQty, available, p.IsActive),
                ["IsActive"] = p.IsActive,
            });
        }
        return rows;
    }
}
