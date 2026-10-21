using Teikem.Domain.Orders;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-21 — Plantillas de importación de PRODUCTOS (primera entidad del motor genérico, dictado de Luis 2026-10-19). Campos que una plantilla (dbo.ImportTemplate, Kind = PRODUCT)
/// puede mapear. Obligatorios: sku (siempre en columna) y nombre (en columna o como valor por defecto). El resto es opcional y toma lo que toma el alta manual del producto.
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

    public static readonly ImportEntityMeta Meta = new(ImportKinds.Product,
        All.Select(f => new ImportFieldSpec(f, Required: f == Sku, DefaultAllowed: !NoDefault.Contains(f))).ToList(),
        new[]
        {
            new ImportRequirement(new[] { Sku }, AllowDefault: false, ProductImportMapping.SkuColumnMessage),
            new ImportRequirement(new[] { Name }, AllowDefault: true, ProductImportMapping.NameColumnMessage),
        });

    public static string? Canonical(string? field) => Meta.Canonical(field);
}

/// <summary>Mensajes y atajos de la plantilla de productos (la lógica vive en <see cref="ImportEntityMeta"/>).</summary>
public static class ProductImportMapping
{
    public const string ColumnsRequiredMessage = ImportEntityMeta.ColumnsRequiredMessage;
    public const string SkuColumnMessage = "La plantilla debe incluir la columna sku.";
    public const string NameColumnMessage = "La plantilla debe incluir la columna name o un valor por defecto para name.";
    public static string UnknownFieldMessage(string field) => ImportEntityMeta.UnknownFieldMessage(field);
    public static string DuplicatePositionMessage(int position) => ImportEntityMeta.DuplicatePositionMessage(position);
    public static string DuplicateFieldMessage(string field) => ImportEntityMeta.DuplicateFieldMessage(field);
    public static string DefaultNotAllowedMessage(string field) => ImportEntityMeta.DefaultNotAllowedMessage(field);

    public static IReadOnlyDictionary<string, string> ValidateColumns(IReadOnlyList<ImportColumn>? columns, IReadOnlyDictionary<string, string>? defaults) => ProductImportFields.Meta.ValidateColumns(columns, defaults);
    public static IReadOnlyDictionary<string, string> NormalizeDefaults(IReadOnlyDictionary<string, string>? defaults) => ProductImportFields.Meta.NormalizeDefaults(defaults);
    public static Dictionary<string, string> MapRow(IReadOnlyList<string> cells, IReadOnlyList<ImportColumn> columns, IReadOnlyDictionary<string, string> defaults) => ProductImportFields.Meta.MapRow(cells, columns, defaults);
    public static bool TryDecimal(string? text, out decimal? value) => ImportEntityMeta.TryDecimal(text, out value);
}
