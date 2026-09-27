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
    decimal? MinQty = null, decimal? MinPickQty = null, decimal? MaxPickQty = null);

/// <summary>PATCH del producto: el SKU es inmutable ('sku' en Extra → 400).</summary>
public sealed record ProductPatchRequest(string? Name = null, Guid? OwnerClientPublicId = null, bool? ClearOwner = null, int? CategoryId = null,
    bool? ClearCategory = null, string? BaseUom = null, string? TrackingType = null, decimal? WeightKg = null, decimal? VolumeM3 = null,
    string? Barcode = null, bool? ClearBarcode = null, decimal? PurchaseCost = null, decimal? SalePrice = null,
    Guid? PreferredWarehousePublicId = null, int? PreferredBinId = null, bool? ClearPreferred = null, decimal? MinQty = null,
    decimal? MinPickQty = null, decimal? MaxPickQty = null, string? RowVersion = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Filtros de la lista de productos. SelectorOrder = orden de los selectores de producto (maestro L1207): clientes 3PL primero
/// (el de más inventario en mano antes, cada cliente en bloque) y propios al final; sin él, por SKU (pantalla Productos e inventario).
/// </summary>
public sealed record ProductListQuery(string? Search = null, int[]? CategoryIds = null, Guid? OwnerClientPublicId = null, bool? OwnOnly = null,
    bool ActiveOnly = false, Guid? WarehousePublicId = null, bool OnlyAvailable = false, int Skip = 0, int Take = 100, bool SelectorOrder = false);

public sealed record ProductListItemDto(int Id, Guid PublicId, string Sku, string Name, int? CategoryId, string? CategoryName,
    Guid? OwnerClientPublicId, string? OwnerName, bool IsOwn, string BaseUomCode, string TrackingTypeCode, string? Barcode,
    decimal? PurchaseCost, decimal? SalePrice, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? MinQty,
    bool IsBelowMin, bool IsActive);

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
