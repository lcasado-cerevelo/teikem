using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Almacenes y ubicaciones. Records con firma posicional FIJA (WmsContractsTests la verifica). Ninguna solicitud
// lleva TenantId: el almacén se identifica por PublicId en la URL; zonas, posiciones y muelles por id int SIEMPRE resuelto a
// través de su almacén filtrado (WmsResolve).

public sealed record WarehouseCreateRequest(string? Code, string? Name, string? Line1 = null, string? City = null, string? State = null,
    string? PostalCode = null, string? Country = null);

/// <summary>PATCH del almacén: el código es inmutable ('code' en Extra → 400).</summary>
public sealed record WarehousePatchRequest(string? Name = null, string? Line1 = null, string? City = null, string? State = null,
    string? PostalCode = null, string? Country = null, string? RowVersion = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record WarehouseDeactivateRequest(string? Comment = null, string? RowVersion = null);

public sealed record WarehouseDto(int Id, Guid PublicId, string Code, string Name, string? Line1, string? City, string? State,
    string? PostalCode, string CountryCode, string StatusCode, string Status, bool IsActive, int ZoneCount, int BinCount, int DockCount,
    decimal QtyOnHand, string RowVersion);

public sealed record WarehouseZoneRequest(string? Code, string? Name, string? ZoneType = null);

public sealed record WarehouseZonePatchRequest(string? Name = null, string? ZoneType = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record WarehouseZoneDto(int Id, string Code, string Name, string? ZoneTypeCode, string? ZoneType, bool IsActive, int BinCount);

public sealed record WarehouseBinRequest(int? ZoneId, string? Code = null, string? Aisle = null, string? Rack = null, string? Level = null,
    string? Position = null, decimal? MaxWeightKg = null);

/// <summary>PATCH de la posición: código y zona inmutables.</summary>
public sealed record WarehouseBinPatchRequest(string? Aisle = null, string? Rack = null, string? Level = null, string? Position = null,
    decimal? MaxWeightKg = null, bool? ClearMaxWeight = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record WarehouseBinQuery(int? ZoneId = null, string? Search = null, bool IncludeInactive = false, bool OnlyWithStock = false);

public sealed record WarehouseBinDto(int Id, int ZoneId, string ZoneCode, string? ZoneTypeCode, string Code, string? Aisle, string? Rack,
    string? Level, string? Position, decimal? MaxWeightKg, bool IsActive, decimal QtyOnHand, int ProductCount);

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
