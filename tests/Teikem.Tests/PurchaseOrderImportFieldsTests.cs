using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Xunit;
using F = Teikem.Domain.Wms.PurchaseOrderImportFields;

namespace Teikem.Tests;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions, P1) — metadatos de la plantilla de importación de órdenes de compra (Kind PURCHASE_ORDER) en el motor genérico: sus 8 campos,
/// cuáles son obligatorios y cuál admite valor fijo (solo el proveedor), las reglas de grupo con su mensaje exacto y que la plantilla no lleva cliente.
/// </summary>
public sealed class PurchaseOrderImportFieldsTests
{
    private static readonly ImportEntityMeta Meta = ImportEntities.TryGet(ImportKinds.PurchaseOrder)!;

    private static List<ImportColumn> Cols(params string[] fields) => fields.Select((f, i) => new ImportColumn(i + 1, f)).ToList();

    [Fact]
    public void The_kind_is_registered_with_its_eight_fields()
    {
        Assert.Equal("PURCHASE_ORDER", ImportKinds.PurchaseOrder);
        Assert.NotNull(ImportEntities.TryGet("purchase_order"));
        Assert.Contains(ImportKinds.PurchaseOrder, ImportEntities.Kinds);
        Assert.Same(F.Meta, Meta);
        Assert.Equal(new[] { "supplier", "documentNumber", "orderDate", "expectedDate", "sku", "description", "quantity", "unitCost" }, Meta.Fields.Select(f => f.Field));
        Assert.Equal(new[] { "supplier", "documentNumber", "sku", "quantity" }, Meta.Fields.Where(f => f.Required).Select(f => f.Field));
        Assert.Equal(new[] { "supplier" }, Meta.Fields.Where(f => f.DefaultAllowed).Select(f => f.Field));
        Assert.False(Meta.AllowsClient);
        Assert.Equal("Este tipo de plantilla no lleva cliente.", ImportEntityMeta.NoClientMessage);
        // las demás entidades del motor siguen admitiendo cliente
        foreach (var kind in new[] { ImportKinds.Product, ImportKinds.Supplier, ImportKinds.Warehouse, ImportKinds.Bin })
            Assert.True(ImportEntities.TryGet(kind)!.AllowsClient, kind);
    }

    [Fact]
    public void A_complete_template_is_valid_with_the_supplier_in_a_column_or_as_a_fixed_value()
    {
        Assert.Empty(Meta.ValidateColumns(Cols("supplier", "documentNumber", "orderDate", "expectedDate", "sku", "description", "quantity", "unitCost"), null));
        Assert.Empty(Meta.ValidateColumns(Cols("documentNumber", "sku", "quantity"), new Dictionary<string, string> { ["supplier"] = "Acme" }));
        Assert.Empty(Meta.ValidateColumns(Cols("DocumentNumber", "SKU", "Quantity", "Supplier"), null));   // sin distinguir mayúsculas
    }

    [Fact]
    public void Each_group_rule_has_its_exact_message()
    {
        Assert.Equal("La plantilla debe incluir la columna documentNumber.", Meta.ValidateColumns(Cols("supplier", "sku", "quantity"), null)["columns"]);
        Assert.Equal("La plantilla debe incluir la columna sku.", Meta.ValidateColumns(Cols("supplier", "documentNumber", "quantity"), null)["columns"]);
        Assert.Equal("La plantilla debe incluir la columna quantity.", Meta.ValidateColumns(Cols("supplier", "documentNumber", "sku"), null)["columns"]);
        Assert.Equal("La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo.",
            Meta.ValidateColumns(Cols("documentNumber", "sku", "quantity"), null)["columns"]);
        Assert.Equal("Indique al menos una columna.", Meta.ValidateColumns(Cols(), new Dictionary<string, string> { ["supplier"] = "Acme" })["columns"]);
    }

    [Fact]
    public void Only_the_supplier_admits_a_fixed_value()
    {
        var errors = Meta.ValidateColumns(Cols("supplier", "sku", "quantity"), new Dictionary<string, string> { ["documentNumber"] = "PO-1" });
        Assert.Equal("El campo documentNumber no admite valor por defecto.", errors["defaults.documentNumber"]);
        Assert.Equal("La plantilla debe incluir la columna documentNumber.", errors["columns"]);   // el fijo no cuenta como columna
        foreach (var field in new[] { "orderDate", "expectedDate", "sku", "description", "quantity", "unitCost" })
            Assert.Equal($"El campo {field} no admite valor por defecto.",
                Meta.ValidateColumns(Cols("supplier", "documentNumber", "sku", "quantity"), new Dictionary<string, string> { [field] = "x" })[$"defaults.{field}"]);
        Assert.Equal(new Dictionary<string, string> { ["supplier"] = "Acme" },
            Meta.NormalizeDefaults(new Dictionary<string, string> { ["SUPPLIER"] = " Acme ", ["sku"] = "X", ["quantity"] = "" }));
    }

    [Fact]
    public void Unknown_fields_and_repeated_positions_are_rejected()
    {
        var unknown = Meta.ValidateColumns(Cols("supplier", "documentNumber", "sku", "quantity", "warehouse"), null);
        Assert.Equal("Campo desconocido: warehouse.", unknown["columns[4].field"]);
        var repeated = Meta.ValidateColumns(new List<ImportColumn> { new(1, "supplier"), new(2, "documentNumber"), new(2, "sku"), new(3, "quantity") }, null);
        Assert.Equal("La posición 2 está repetida.", repeated["columns[2].position"]);
        var twice = Meta.ValidateColumns(Cols("supplier", "documentNumber", "sku", "quantity", "sku"), null);
        Assert.Equal("El campo sku está repetido.", twice["columns[4].field"]);
        Assert.Equal("Campo desconocido: client.", Meta.ValidateColumns(Cols("supplier", "documentNumber", "sku", "quantity"), new Dictionary<string, string> { ["client"] = "C1" })["defaults.client"]);
    }

    [Fact]
    public void A_row_maps_cells_and_an_empty_supplier_cell_takes_the_fixed_value()
    {
        var cols = Cols("supplier", "documentNumber", "sku", "quantity");
        var defaults = Meta.NormalizeDefaults(new Dictionary<string, string> { ["supplier"] = "Acme" });
        var row = Meta.MapRow(new[] { "", "PO-7", "NECH-1001", "3" }, cols, defaults);
        Assert.Equal("Acme", row[F.Supplier]);
        Assert.Equal("PO-7", row[F.DocumentNumber]);
        Assert.Equal("NECH-1001", row[F.Sku]);
        Assert.Equal("3", row[F.Quantity]);
        Assert.Equal("Beta", Meta.MapRow(new[] { "Beta", "PO-7", "X", "1" }, cols, defaults)[F.Supplier]);
    }
}
