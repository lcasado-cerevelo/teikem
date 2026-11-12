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
    decimal? MinQty = null, decimal? MinPickQty = null, decimal? MaxPickQty = null, string? Brand = null, string? Model = null,
    string? PackUom = null, decimal? PackQty = null, string? RotationMethod = null,
    decimal? Length = null, decimal? Width = null, decimal? Height = null, string? DimensionUnit = null, decimal? Weight = null, string? WeightUnit = null,
    bool? IsAssembledKit = null, IReadOnlyList<AssembledKitPieceInput>? KitPieces = null, decimal? AssembleQuantity = null, Guid? AssembleWarehousePublicId = null,
    int? AssembleToBinId = null);
// Lote 232 (al final del alta): IsAssembledKit = «¿Es un kit?» (kit ARMADO, con existencia propia); KitPieces = su composición (piezas y cantidad por kit); AssembleQuantity
// (opcional) = cuántos kits armar al crearlo, en AssembleWarehousePublicId (o el único almacén activo) y la posición AssembleToBinId (armar pide además inventory.adjust).

/// <summary>
/// PATCH del producto: el SKU es inmutable ('sku' en Extra → 400). Lote 230: Length / Width / Height (null = sin cambio, 0 = quitar), DimensionUnit (IN, CM, FT, M),
/// Weight en WeightUnit (LB, KG; manda sobre WeightKg); con las tres medidas el volumen se calcula y un volumeM3 mandado se ignora. Lote 12: Brand/Model null = sin cambio; "" (o solo
/// espacios) = quitarlos. Lote 34: RotationMethod = excepción de rotación del producto ('FEFO' | 'FIFO'); null = sin cambio; "" = quitarla
/// (vuelve al método de la compañía).
/// </summary>
public sealed record ProductPatchRequest(string? Name = null, Guid? OwnerClientPublicId = null, bool? ClearOwner = null, int? CategoryId = null,
    bool? ClearCategory = null, string? BaseUom = null, string? TrackingType = null, decimal? WeightKg = null, decimal? VolumeM3 = null,
    string? Barcode = null, bool? ClearBarcode = null, decimal? PurchaseCost = null, decimal? SalePrice = null,
    Guid? PreferredWarehousePublicId = null, int? PreferredBinId = null, bool? ClearPreferred = null, decimal? MinQty = null,
    decimal? MinPickQty = null, decimal? MaxPickQty = null, string? RowVersion = null, string? Brand = null, string? Model = null,
    string? PackUom = null, decimal? PackQty = null, bool? ClearPack = null, string? RotationMethod = null,
    decimal? Length = null, decimal? Width = null, decimal? Height = null, string? DimensionUnit = null, decimal? Weight = null, string? WeightUnit = null)
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
/// Unavailable (2026-10-05, tableta 'No disponibles') = productos ACTIVOS con disponible = 0 (en mano − reservado de todas sus posiciones,
/// o de los almacenes indicados; sin saldo = 0): sin existencia o con todo reservado. Misma suma que BelowMin.
/// HasAvailable (2026-10-09, switch «Con disponible» de Productos e inventario) = productos con disponible &gt; 0 con la MISMA suma de la columna
/// «Disponible» de la tabla (en mano − reservado de todas las posiciones, o de los almacenes indicados); es el complemento exacto de Unavailable.
/// OwnerClientPublicIds + IncludeOwn (2026-10-11, filtro «Dueño» de Productos e inventario, varios a la vez) = productos de esos clientes dueños
/// y, con IncludeOwn, también los propios (sin cliente); sin ninguno de los dos no se filtra por dueño (todos). Se combinan con OwnOnly y
/// OwnerClientPublicId (que siguen para los selectores) como una condición más.
/// IncludeOfferKits (Lote 231): los kits de oferta NO salen en la lista (Productos e inventario, filtros, exportaciones, indicadores); con true también salen (p. ej. el
/// selector de producto de una orden de venta). La ficha por id (GET /products/{id}) los sigue devolviendo.
/// </summary>
public sealed record ProductListQuery(string? Search = null, int[]? CategoryIds = null, Guid? OwnerClientPublicId = null, bool? OwnOnly = null,
    bool ActiveOnly = false, Guid? WarehousePublicId = null, bool OnlyAvailable = false, int Skip = 0, int Take = 100, bool SelectorOrder = false,
    bool BelowMin = false, Guid[]? WarehousePublicIds = null, Guid[]? ProductPublicIds = null, string? Name = null, string[]? Brands = null,
    bool SerialOnly = false, bool SerialMissing = false, bool OnlyOnHand = false, bool Unavailable = false,
    bool HasAvailable = false, string? BinSearch = null, Guid[]? OwnerClientPublicIds = null, bool IncludeOwn = false,
    int[]? BinIds = null, bool IncludeOfferKits = false);

public sealed record ProductListItemDto(int Id, Guid PublicId, string Sku, string Name, int? CategoryId, string? CategoryName,
    Guid? OwnerClientPublicId, string? OwnerName, bool IsOwn, string BaseUomCode, string TrackingTypeCode, string? Barcode,
    decimal? PurchaseCost, decimal? SalePrice, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? MinQty,
    bool IsBelowMin, bool IsActive, string? Brand, string? Model, string? PackUomCode = null, string? PackUomName = null, decimal? PackQty = null, bool IsKit = false, bool IsAssembledKit = false,
    Guid? ImagePublicId = null);   // Lote 240: la foto que se muestra en la lista (la propia o, en un kit sin foto, la de sus piezas); null = sin foto o la compañía no maneja fotos

public sealed record ProductPageDto(int Total, int Skip, int Take, IReadOnlyList<ProductListItemDto> Items);

/// <remarks>
/// Lote 230: Measures = largo, ancho y alto con su unidad, el volumen para mostrar (en la unidad cúbica de las medidas; calculado si están las tres) y el peso en la
/// unidad en que se escribió (WeightKg y VolumeM3 siguen siendo la verdad interna en kg y m³). CategoryPath = la ruta de la categoría («Guantes / Nitrilo») y
/// CategoryParentId / CategoryParentName = su categoría padre (null si es de primer nivel o no tiene categoría).
/// Lote 237: TaxCategory = la categoría de impuesto del producto (TAXABLE, EXEMPT o ZERO_RATE; solo lectura aquí: se cambia con PUT /tax/products/{id}/category).
/// </remarks>
public sealed record ProductDetailDto(ProductListItemDto Product, decimal? WeightKg, decimal? VolumeM3, Guid? PreferredWarehousePublicId,
    string? PreferredWarehouseCode, int? PreferredBinId, string? PreferredBinCode, decimal? MinPickQty, decimal? MaxPickQty,
    bool HasMovements, string RowVersion, string? RotationMethod = null,
    ProductMeasuresDto? Measures = null, string? CategoryPath = null, int? CategoryParentId = null, string? CategoryParentName = null, IReadOnlyList<AssembledKitPieceDto>? KitPieces = null,
    string TaxCategory = "TAXABLE");

/// <summary>
/// Lote 230 — las medidas del producto como se escribieron: Length / Width / Height en DimensionUnit (IN, CM, FT, M); Volume en VolumeUnit (IN3, CM3, FT3, M3;
/// M3 si no hay unidad de medidas), VolumeCalculated = salió de las tres medidas (no se edita a mano); Weight en WeightUnit (LB con 2 decimales, KG con 3).
/// </summary>
public sealed record ProductMeasuresDto(decimal? Length, decimal? Width, decimal? Height, string? DimensionUnit, decimal? Volume, string VolumeUnit, bool VolumeCalculated,
    decimal? Weight, string? WeightUnit);

/// <summary>
/// Lote 230 — GET /products/units-suggestion: las unidades que se sugieren por la región de la compañía (PR y US = sistema inglés: IN / FT / LB; el resto métrico:
/// CM / M / KG). DimensionUnit para lo chico, LargeDimensionUnit para lo grande; DimensionUnits y WeightUnits = todas las que se aceptan.
/// </summary>
public sealed record ProductUnitsSuggestionDto(string RegionCode, string System, string DimensionUnit, string LargeDimensionUnit, string WeightUnit,
    IReadOnlyList<string> DimensionUnits, IReadOnlyList<string> WeightUnits);

public sealed record ProductCategoryRequest(string? Name, int? ParentId = null);

public sealed record ProductCategoryPatchRequest(string? Name = null, int? ParentId = null, bool? ClearParent = null);

/// <summary>IsDefault (2026-10-11 c): es la categoría que los ajustes de la compañía preseleccionan en un producto nuevo (la lista y la ficha lo informan).</summary>
public sealed record ProductCategoryDto(int Id, string Name, int? ParentId, string Path, bool IsActive, int ProductCount, bool IsDefault = false);

public sealed record LotDto(int Id, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, int? DaysToExpiry, decimal QtyOnHand,
    bool IsActive);

public sealed record SerialDto(int Id, string SerialNumber, int? LotId, string? LotNumber, string? StatusCode, string? Status,
    Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode);

/// <summary>
/// Lote 26 (Rentas R0) — series capturadas para una posición en la conversión a serie: tantas como unidades en mano tenga el
/// producto en esa posición. La posición se identifica por su id (se resuelve dentro de su almacén filtrado por tenant).
/// </summary>
public sealed record SerialConversionPositionInput(int? BinId, IReadOnlyList<string>? SerialNumbers = null);

/// <summary>
/// Lote 26 (Rentas R0) — POST /products/{publicId}/convert-to-serial: un renglón por posición con existencia (con sus series);
/// notas opcionales para los movimientos (por defecto 'Conversión a serie'); rowVersion opcional del producto (409 si cambió).
/// </summary>
public sealed record ProductSerialConversionRequest(IReadOnlyList<SerialConversionPositionInput>? Positions = null, string? Notes = null,
    string? RowVersion = null);

/// <summary>Resultado de la conversión: el producto ya SERIAL, cuántas series se dieron de alta y los movimientos y saldos tocados.</summary>
public sealed record ProductSerialConversionResultDto(ProductDetailDto Product, int SerialCount, MovementResultDto Movements);
