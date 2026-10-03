using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Almacenes y ubicaciones. Records con firma posicional FIJA (WmsContractsTests la verifica). Ninguna solicitud
// lleva TenantId: el almacén se identifica por PublicId en la URL; zonas, posiciones y muelles por id int SIEMPRE resuelto a
// través de su almacén filtrado (WmsResolve).

/// <summary>Lote 16: ReceivingMode = modo de recepción (PUTAWAY | DIRECT; sin él, PUTAWAY; desconocido → 400).</summary>
public sealed record WarehouseCreateRequest(string? Code, string? Name, string? Line1 = null, string? City = null, string? State = null,
    string? PostalCode = null, string? Country = null, string? ReceivingMode = null);

/// <summary>
/// PATCH del almacén: el código es inmutable ('code' en Extra → 400). Lote 16: ReceivingMode (null = sin cambio; PUTAWAY |
/// DIRECT; desconocido → 400; no toca los recibos abiertos, D2) y posición de recepción por defecto (D12):
/// DefaultReceivingBinId (del almacén → si no 404; zona STAGING o CROSSDOCK → si no 400; activa → si no 422) o
/// ClearDefaultReceivingBin = true para quitarla.
/// </summary>
public sealed record WarehousePatchRequest(string? Name = null, string? Line1 = null, string? City = null, string? State = null,
    string? PostalCode = null, string? Country = null, string? RowVersion = null, string? ReceivingMode = null, int? DefaultReceivingBinId = null,
    bool? ClearDefaultReceivingBin = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record WarehouseDeactivateRequest(string? Comment = null, string? RowVersion = null);

/// <summary>
/// Almacén. ZoneTypeCodes (Lote 1 de cambios de Almacén) = códigos de tipo de zona DISTINTOS de sus zonas activas, ordenados, para
/// el filtro "Tipo" de la lista (zonas sin tipo no aportan código).
/// Lote 16: ReceivingModeCode (PUTAWAY | DIRECT) con su etiqueta, y la posición de recepción por defecto (D12; null = la
/// primera STAGING del almacén).
/// </summary>
public sealed record WarehouseDto(int Id, Guid PublicId, string Code, string Name, string? Line1, string? City, string? State,
    string? PostalCode, string CountryCode, string StatusCode, string Status, bool IsActive, int ZoneCount, int BinCount, int DockCount,
    decimal QtyOnHand, string RowVersion, IReadOnlyList<string> ZoneTypeCodes, string ReceivingModeCode = "PUTAWAY", string? ReceivingMode = null,
    int? DefaultReceivingBinId = null, string? DefaultReceivingBinCode = null);

public sealed record WarehouseZoneRequest(string? Code, string? Name, string? ZoneType = null);

/// <summary>PATCH de la zona: código (Lote 1 de cambios: editable, único por almacén → 409), nombre y tipo ("" quita el tipo).</summary>
public sealed record WarehouseZonePatchRequest(string? Name = null, string? ZoneType = null, string? Code = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Zona con su ocupación (Lote 1 de cambios de Almacén; la capacidad de zona NO se guarda, se suma de sus posiciones):
/// BinCount = posiciones activas; OccupiedBinCount = activas con existencia; CapacityQty = Σ cupo de las activas con cupo;
/// QtyOnHandInCapacityBins = existencia en esas mismas posiciones (numerador del % de ocupación); QtyOnHand = existencia total
/// de la zona; BinsWithoutCapacity = activas sin cupo configurado (quedan fuera del %).
/// </summary>
public sealed record WarehouseZoneDto(int Id, string Code, string Name, string? ZoneTypeCode, string? ZoneType, bool IsActive, int BinCount,
    int OccupiedBinCount, long CapacityQty, decimal QtyOnHandInCapacityBins, decimal QtyOnHand, int BinsWithoutCapacity);

/// <summary>Alta de posición. MaxCapacityQty = cupo máximo en unidades de producto (null = sin configurar; si llega, &gt; 0).</summary>
public sealed record WarehouseBinRequest(int? ZoneId, string? Code = null, string? Aisle = null, string? Rack = null, string? Level = null,
    string? Position = null, decimal? MaxWeightKg = null, int? MaxCapacityQty = null);

/// <summary>PATCH de la posición: código y zona inmutables. ClearMaxCapacity = quitar el cupo (queda sin configurar).</summary>
public sealed record WarehouseBinPatchRequest(string? Aisle = null, string? Rack = null, string? Level = null, string? Position = null,
    decimal? MaxWeightKg = null, bool? ClearMaxWeight = null, int? MaxCapacityQty = null, bool? ClearMaxCapacity = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Filtros del listado paginado de posiciones (filtrado, orden por código y paginación en SQL). Search = contiene en código,
/// código de zona, pasillo, rack, nivel o posición. ZoneId (una, 404 si no es del almacén) y ZoneIds (varias); Aisle/Rack/Level/
/// Position = contiene; Lote 21: IsProvisional = true solo las pendientes de revisión, false solo las confirmadas; ProductPublicIds = posiciones con existencia de alguno de esos productos; Occupancy = uno o varios de
/// EMPTY, PARTIAL, FULL, NO_CAPACITY (BinOccupancies); BinIds = solo esas posiciones (resolver ids ya elegidos).
/// </summary>
public sealed record WarehouseBinQuery(int? ZoneId = null, string? Search = null, bool IncludeInactive = false, bool OnlyWithStock = false,
    int[]? ZoneIds = null, string? Aisle = null, string? Rack = null, string? Level = null, string? Position = null,
    Guid[]? ProductPublicIds = null, string[]? Occupancy = null, int[]? BinIds = null, int Skip = 0, int Take = 100,
    bool? IsProvisional = null);

/// <summary>
/// Posición. QtyOnHand y ProductCount = existencia en mano y productos distintos con existencia; Occupancy = EMPTY, PARTIAL,
/// FULL o NO_CAPACITY (WarehouseRules.Occupancy); cuando ProductCount es exactamente 1, SingleProduct* identifica ese producto.
/// Lote 21: IsProvisional = creada desde un conteo y pendiente de revisión (confirmar con POST .../bins/{binId}/confirm-provisional);
/// ProvisionalCycleCountId y ProvisionalCreatedAtUtc = de qué conteo y cuándo (se conservan tras confirmar).
/// </summary>
public sealed record WarehouseBinDto(int Id, int ZoneId, string ZoneCode, string? ZoneTypeCode, string Code, string? Aisle, string? Rack,
    string? Level, string? Position, decimal? MaxWeightKg, bool IsActive, decimal QtyOnHand, int ProductCount, int? MaxCapacityQty,
    string Occupancy, Guid? SingleProductPublicId, string? SingleProductSku, string? SingleProductName,
    bool IsProvisional = false, int? ProvisionalCycleCountId = null, DateTime? ProvisionalCreatedAtUtc = null);

/// <summary>Página del listado de posiciones (mismo sobre que productos, recibos y tareas).</summary>
public sealed record WarehouseBinPageDto(int Total, int Skip, int Take, IReadOnlyList<WarehouseBinDto> Items);

/// <summary>
/// Lote 14 — posición encontrada por la búsqueda entre almacenes (GET /warehouses/bins/search), para los filtros Posición del
/// Kárdex y del Conteo: "Código · Zona · Almacén".
/// </summary>
public sealed record BinSearchItemDto(int Id, string Code, string? ZoneCode, Guid WarehousePublicId, string WarehouseCode, bool IsActive = true,
    bool IsProvisional = false);

/// <summary>
/// Lote 11 — asignación del cupo máximo en bloque (POST /warehouses/{publicId}/bins/capacity). Filtros = los mismos del listado
/// de posiciones con el mismo significado (ZoneIds, Aisle/Rack/Level/Position y Search "contiene", BinIds, IncludeInactive);
/// OnlyWithoutCapacity = solo las posiciones que hoy no tienen cupo (no pisa ninguno ya capturado). Sin ningún filtro de
/// posiciones se exige AllBins = true (todo el almacén). Exactamente uno de MaxCapacityQty (&gt; 0) o Clear = true (quitar el cupo).
/// </summary>
public sealed record WarehouseBinCapacityRequest(int[]? ZoneIds = null, string? Aisle = null, string? Rack = null, string? Level = null,
    string? Position = null, string? Search = null, int[]? BinIds = null, bool IncludeInactive = false, bool OnlyWithoutCapacity = false,
    bool AllBins = false, int? MaxCapacityQty = null, bool Clear = false);

/// <summary>Resultado de la asignación en bloque: posiciones que cumplen los filtros y cuántas cambiaron de cupo.</summary>
public sealed record WarehouseBinCapacityResultDto(int Matched, int Changed);

public sealed record WarehouseDockRequest(string? Code, string? DockType);

public sealed record WarehouseDockPatchRequest(string? DockType = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record WarehouseDockStatusRequest(string? Status, string? Comment = null);

public sealed record WarehouseDockDto(int Id, string Code, string DockTypeCode, string DockType, string StatusCode, string Status,
    string? StatusColor, bool IsActive);

public sealed record WarehouseDetailDto(WarehouseDto Warehouse, IReadOnlyList<WarehouseZoneDto> Zones, IReadOnlyList<WarehouseDockDto> Docks);
