using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Productos, categorías, lotes y series. Firma posicional FIJA. El dueño del producto (cliente 3PL) viaja por
// PublicId; NULL = propio del tenant (nunca se confunde con el TenantId, que sale del principal).

/// <summary>Lote capturado (número y fechas). Un lote existente con otras fechas → 409 (D34).</summary>
public sealed record LotInput(string? Number, DateOnly? ManufactureDate = null, DateOnly? ExpiryDate = null);

public sealed record ProductCreateRequest(string? Sku, string? Name, Guid? OwnerClientPublicId = null, int? CategoryId = null,
    string? BaseUom = null, string? TrackingType = null, decimal? WeightKg = null, decimal? VolumeM3 = null, string? Barcode = null,
    decimal? PurchaseCost = null, decimal? SalePrice = null, Guid? PreferredWarehousePublicId = null, int? PreferredBinId = null,
    decimal? MinQty = null, decimal? MinPickQty = null, decimal? MaxPickQty = null, string? Brand = null, string? Model = null);

/// <summary>
/// PATCH del producto: el SKU es inmutable ('sku' en Extra → 400). Lote 12: Brand/Model null = sin cambio; "" (o solo
/// espacios) = quitarlos.
/// </summary>
public sealed record ProductPatchRequest(string? Name = null, Guid? OwnerClientPublicId = null, bool? ClearOwner = null, int? CategoryId = null,
    bool? ClearCategory = null, string? BaseUom = null, string? TrackingType = null, decimal? WeightKg = null, decimal? VolumeM3 = null,
    string? Barcode = null, bool? ClearBarcode = null, decimal? PurchaseCost = null, decimal? SalePrice = null,
    Guid? PreferredWarehousePublicId = null, int? PreferredBinId = null, bool? ClearPreferred = null, decimal? MinQty = null,
    decimal? MinPickQty = null, decimal? MaxPickQty = null, string? RowVersion = null, string? Brand = null, string? Model = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Filtros de la lista de productos. SelectorOrder = orden de los selectores de producto (maestro L1207): clientes 3PL primero
/// (el de más inventario en mano antes, cada cliente en bloque) y propios al final; sin él, por SKU (pantalla Productos e inventario).
/// BelowMin (Lote 7A) = solo productos bajo mínimo (activos, con mínimo y disponible &lt; mínimo, el mismo cálculo de IsBelowMin;
/// con WarehousePublicId, el disponible de ese almacén): el panel Almacén del Pulso lo cuenta con take=1 y total.
/// Lote 12: WarehousePublicIds (varios; se combinan con WarehousePublicId) acotan los totales, el disponible, el bajo mínimo
/// y el conteo de series a esos almacenes (como el singular: no quitan productos de la lista); ProductPublicIds = selección
/// de SKU; Name = el nombre contiene el texto (sin distinguir mayúsculas); Brands = marca igual a alguna (sin distinguir
/// mayúsculas); SerialOnly = rastreo SERIAL o con series registradas; SerialMissing = activos SERIAL con existencia en mano
/// mayor que sus series en stock (AVAILABLE/RESERVED): el KPI 'series por capturar' lo cuenta con take=1 y total.
/// OnlyOnHand (ajuste del 2026-09-30, KPI 'Unidades totales') = solo productos con existencia en mano &gt; 0 (Σ QtyOnHand de
/// todas sus posiciones, incluidas cuarentena y cross-dock; en los almacenes indicados si los hay). Como OnlyAvailable, sí
/// quita productos de la lista.
/// </summary>
public sealed record ProductListQuery(string? Search = null, int[]? CategoryIds = null, Guid? OwnerClientPublicId = null, bool? OwnOnly = null,
    bool ActiveOnly = false, Guid? WarehousePublicId = null, bool OnlyAvailable = false, int Skip = 0, int Take = 100, bool SelectorOrder = false,
    bool BelowMin = false, Guid[]? WarehousePublicIds = null, Guid[]? ProductPublicIds = null, string? Name = null, string[]? Brands = null,
    bool SerialOnly = false, bool SerialMissing = false, bool OnlyOnHand = false);

public sealed record ProductListItemDto(int Id, Guid PublicId, string Sku, string Name, int? CategoryId, string? CategoryName,
    Guid? OwnerClientPublicId, string? OwnerName, bool IsOwn, string BaseUomCode, string TrackingTypeCode, string? Barcode,
    decimal? PurchaseCost, decimal? SalePrice, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? MinQty,
    bool IsBelowMin, bool IsActive, string? Brand, string? Model);

public sealed record ProductPageDto(int Total, int Skip, int Take, IReadOnlyList<ProductListItemDto> Items);

public sealed record ProductDetailDto(ProductListItemDto Product, decimal? WeightKg, decimal? VolumeM3, Guid? PreferredWarehousePublicId,
    string? PreferredWarehouseCode, int? PreferredBinId, string? PreferredBinCode, decimal? MinPickQty, decimal? MaxPickQty,
    bool HasMovements, string RowVersion);

public sealed record ProductCategoryRequest(string? Name, int? ParentId = null);

public sealed record ProductCategoryPatchRequest(string? Name = null, int? ParentId = null, bool? ClearParent = null);

public sealed record ProductCategoryDto(int Id, string Name, int? ParentId, string Path, bool IsActive, int ProductCount);

public sealed record LotDto(int Id, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, int? DaysToExpiry, decimal QtyOnHand,
    bool IsActive);

public sealed record SerialDto(int Id, string SerialNumber, int? LotId, string? LotNumber, string? StatusCode, string? Status,
    Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode);
