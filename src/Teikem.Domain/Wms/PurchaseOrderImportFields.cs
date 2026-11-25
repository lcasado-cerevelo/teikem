using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — plantilla de importación de ÓRDENES DE COMPRA definida por el usuario (motor genérico, Kind PURCHASE_ORDER). Las filas se agrupan
/// por número de documento: documentNumber, sku y quantity son columnas OBLIGATORIAS; el proveedor va en su columna o como valor fijo de la plantilla (el único campo que admite
/// valor por omisión). El almacén se elige al importar, para todo el archivo; la plantilla no lleva cliente.
/// </summary>
public static class PurchaseOrderImportFields
{
    public const string Supplier = "supplier";
    public const string DocumentNumber = "documentNumber";
    public const string OrderDate = "orderDate";
    public const string ExpectedDate = "expectedDate";
    public const string Sku = "sku";
    public const string Description = "description";
    public const string Quantity = "quantity";
    public const string UnitCost = "unitCost";

    public static readonly IReadOnlyList<string> All = new[] { Supplier, DocumentNumber, OrderDate, ExpectedDate, Sku, Description, Quantity, UnitCost };
    public static readonly IReadOnlySet<string> Required = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Supplier, DocumentNumber, Sku, Quantity };
    /// <summary>Solo el proveedor admite valor por omisión (proveedor fijo de la plantilla).</summary>
    public static readonly IReadOnlySet<string> DefaultAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Supplier };

    public const string DocumentNumberColumnMessage = "La plantilla debe incluir la columna documentNumber.";
    public const string SkuColumnMessage = "La plantilla debe incluir la columna sku.";
    public const string QuantityColumnMessage = "La plantilla debe incluir la columna quantity.";
    public const string SupplierMessage = "La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo.";

    public static readonly ImportEntityMeta Meta = new(ImportKinds.PurchaseOrder,
        All.Select(f => new ImportFieldSpec(f, Required: Required.Contains(f), DefaultAllowed: DefaultAllowed.Contains(f))).ToList(),
        new[]
        {
            new ImportRequirement(new[] { DocumentNumber }, AllowDefault: false, DocumentNumberColumnMessage),
            new ImportRequirement(new[] { Sku }, AllowDefault: false, SkuColumnMessage),
            new ImportRequirement(new[] { Quantity }, AllowDefault: false, QuantityColumnMessage),
            new ImportRequirement(new[] { Supplier }, AllowDefault: true, SupplierMessage),
        },
        allowsClient: false);
}
