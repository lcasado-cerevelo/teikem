using System.Globalization;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-21 — Plantillas de importación de PRODUCTOS (primera entidad del motor genérico, dictado de Luis 2026-10-19). Catálogo de campos que una plantilla
/// (dbo.ImportTemplate, Kind = PRODUCT) puede mapear a una posición de columna. Obligatorios: sku y nombre (en columna o como valor por defecto el nombre; el SKU
/// siempre en columna). El resto es opcional y toma lo que toma el alta manual del producto.
/// </summary>
public static class ProductImportFields
{
    public const string Sku = "sku";
    public const string Name = "name";
    public const string OwnerClientCode = "ownerClientCode";
    public const string BaseUom = "baseUom";
    public const string TrackingType = "trackingType";
    public const string WeightKg = "weightKg";
    public const string VolumeM3 = "volumeM3";
    public const string Barcode = "barcode";
    public const string PurchaseCost = "purchaseCost";
    public const string SalePrice = "salePrice";
    public const string MinQty = "minQty";
    public const string MinPickQty = "minPickQty";
    public const string MaxPickQty = "maxPickQty";
    public const string Brand = "brand";
    public const string Model = "model";
    public const string PackUom = "packUom";
    public const string PackQty = "packQty";
    public const string RotationMethod = "rotationMethod";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Sku, Name, OwnerClientCode, BaseUom, TrackingType, WeightKg, VolumeM3, Barcode, PurchaseCost, SalePrice, MinQty, MinPickQty, MaxPickQty, Brand, Model, PackUom, PackQty, RotationMethod,
    };

    /// <summary>Campos que no admiten valor por defecto (cada producto trae el suyo).</summary>
    public static readonly IReadOnlySet<string> NoDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sku, Barcode };

    public static string? Canonical(string? field)
        => string.IsNullOrWhiteSpace(field) ? null : All.FirstOrDefault(f => string.Equals(f, field.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reglas puras de la plantilla de productos y del mapeo de una fila (celda no vacía → valor; vacía → valor por defecto si hay; si no, se omite).</summary>
public static class ProductImportMapping
{
    public const string ColumnsRequiredMessage = "Indique al menos una columna.";
    public const string SkuColumnMessage = "La plantilla debe incluir la columna sku.";
    public const string NameColumnMessage = "La plantilla debe incluir la columna name o un valor por defecto para name.";
    public static string UnknownFieldMessage(string field) => $"Campo desconocido: {field}.";
    public static string DuplicatePositionMessage(int position) => $"La posición {position} está repetida.";
    public static string DuplicateFieldMessage(string field) => $"El campo {field} está repetido.";
    public static string DefaultNotAllowedMessage(string field) => $"El campo {field} no admite valor por defecto.";

    public static IReadOnlyDictionary<string, string> ValidateColumns(IReadOnlyList<ImportColumn>? columns, IReadOnlyDictionary<string, string>? defaults)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (columns is null || columns.Count == 0) errors["columns"] = ColumnsRequiredMessage;
        else if (columns.Count > ImportMapping.MaxColumns) errors["columns"] = $"Máximo {ImportMapping.MaxColumns} columnas.";

        var positions = new HashSet<int>();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; columns is not null && i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.Position < 1) errors[$"columns[{i}].position"] = "La posición debe ser 1 o mayor.";
            else if (c.Position > ImportMapping.MaxPosition) errors[$"columns[{i}].position"] = $"La posición no puede exceder {ImportMapping.MaxPosition}.";
            else if (!positions.Add(c.Position)) errors[$"columns[{i}].position"] = DuplicatePositionMessage(c.Position);

            var canonical = ProductImportFields.Canonical(c.Field);
            if (canonical is null) errors[$"columns[{i}].field"] = UnknownFieldMessage(c.Field?.Trim() ?? string.Empty);
            else if (!fields.Add(canonical)) errors[$"columns[{i}].field"] = DuplicateFieldMessage(canonical);
        }

        var defaultFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in defaults ?? new Dictionary<string, string>())
        {
            var canonical = ProductImportFields.Canonical(key);
            if (canonical is null) errors[$"defaults.{key}"] = UnknownFieldMessage(key);
            else if (ProductImportFields.NoDefault.Contains(canonical)) errors[$"defaults.{key}"] = DefaultNotAllowedMessage(canonical);
            else defaultFields.Add(canonical);
        }

        if (columns is { Count: > 0 } && !errors.ContainsKey("columns"))
        {
            if (!fields.Contains(ProductImportFields.Sku)) errors["columns"] = SkuColumnMessage;
            else if (!fields.Contains(ProductImportFields.Name) && !defaultFields.Contains(ProductImportFields.Name)) errors["columns"] = NameColumnMessage;
        }
        return errors;
    }

    /// <summary>Defaults con el nombre canónico del campo, sin los no admitidos ni los vacíos.</summary>
    public static IReadOnlyDictionary<string, string> NormalizeDefaults(IReadOnlyDictionary<string, string>? defaults)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in defaults ?? new Dictionary<string, string>())
        {
            var canonical = ProductImportFields.Canonical(key);
            if (canonical is null || ProductImportFields.NoDefault.Contains(canonical) || string.IsNullOrWhiteSpace(value)) continue;
            result[canonical] = value.Trim();
        }
        return result;
    }

    /// <summary>Valores por campo de una fila: la celda de la columna, o el valor por defecto de la plantilla si la celda viene vacía.</summary>
    public static Dictionary<string, string> MapRow(IReadOnlyList<string> cells, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in columns)
        {
            var field = ProductImportFields.Canonical(c.Field);
            if (field is null) continue;
            var text = c.Position >= 1 && c.Position <= cells.Count ? cells[c.Position - 1].Trim() : string.Empty;
            if (text.Length > 0) values[field] = text;
        }
        foreach (var (field, value) in defaults)
            if (!values.ContainsKey(field)) values[field] = value;
        return values;
    }

    /// <summary>Número con punto o coma decimal (sin separador de miles); vacío → null; no numérico → (false).</summary>
    public static bool TryDecimal(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim().Replace("$", string.Empty).Replace(" ", string.Empty);
        if (t.Contains(',') && !t.Contains('.')) t = t.Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return false;
        value = d;
        return true;
    }
}
