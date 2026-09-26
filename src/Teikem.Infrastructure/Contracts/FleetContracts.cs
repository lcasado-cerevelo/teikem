using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 4 — Flota (vehículos, documentos por vencer, disponibilidad). Records sin lógica: es el contrato entre piezas
// (P1–P9) y con el front. Firma posicional fija: ninguna pieza la cambia. Los hijos (documentos) se exponen con id entero
// solo bajo la ruta de su padre.

// ---------------------------------------------------------------- vehículos

/// <summary>GET /vehicles: búsqueda libre (qbox) y filtros multi-valor por código de catálogo/estatus.</summary>
public sealed record VehicleListQuery(
    string? Search = null,
    string[]? VehicleType = null,
    string[]? Ownership = null,
    string[]? FuelType = null,
    string[]? Status = null,
    bool IncludeInactive = false);

public sealed record VehicleListItemDto(
    int Id,
    Guid PublicId,
    string Code,
    string? PlateNumber,
    string? VehicleTypeCode,
    string? VehicleType,
    string? OwnershipCode,
    string? Ownership,
    string? FuelTypeCode,
    string? FuelType,
    string? Make,
    string? Model,
    int? ModelYear,
    string? Vin,
    decimal? CurrentOdometerKm,
    string StatusCode,
    string Status,
    string? StatusColor,
    bool IsActive,
    DateOnly? NextDocumentExpiry);

/// <summary>Documento del vehículo (los adjuntos no se exponen). IsSuperseded = renovado por otro del mismo tipo.</summary>
public sealed record VehicleDocumentDto(
    int Id,
    string DocTypeCode,
    string DocType,
    string? DocNumber,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string ExpiryState,
    int? DaysToExpiry,
    bool IsSuperseded,
    bool IsActive);

public sealed record VehicleDetailDto(
    int Id,
    Guid PublicId,
    string Code,
    string? PlateNumber,
    decimal? MaxWeightKg,
    decimal? MaxVolumeM3,
    int? MaxStops,
    string? VehicleTypeCode,
    string? VehicleType,
    string? OwnershipCode,
    string? Ownership,
    string? FuelTypeCode,
    string? FuelType,
    string? Make,
    string? Model,
    int? ModelYear,
    string? Vin,
    decimal? CurrentOdometerKm,
    string StatusCode,
    string Status,
    bool IsTerminal,
    bool IsActive,
    IReadOnlyList<VehicleDocumentDto> Documents,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion);

/// <summary>POST /vehicles. Code obligatorio e inmutable; catálogos por código.</summary>
public sealed record VehicleCreateRequest(
    string? Code,
    string? PlateNumber = null,
    string? VehicleType = null,
    string? Ownership = null,
    string? FuelType = null,
    string? Make = null,
    string? Model = null,
    int? ModelYear = null,
    string? Vin = null,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeM3 = null,
    int? MaxStops = null,
    decimal? CurrentOdometerKm = null);

/// <summary>PATCH /vehicles/{publicId}: null = sin cambio. 'code'/'homeWarehouseId' llegan en Extra y responden 400.</summary>
public sealed record VehiclePatchRequest(
    string? PlateNumber = null,
    string? VehicleType = null,
    string? Ownership = null,
    string? FuelType = null,
    string? Make = null,
    string? Model = null,
    int? ModelYear = null,
    string? Vin = null,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeM3 = null,
    int? MaxStops = null,
    decimal? CurrentOdometerKm = null,
    string? RowVersion = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Alta/edición de un documento de vehículo. ClearIssuedDate/ClearExpiryDate borran la fecha en un PATCH.</summary>
public sealed record VehicleDocumentRequest(
    string? DocType = null,
    string? DocNumber = null,
    DateOnly? IssuedDate = null,
    DateOnly? ExpiryDate = null,
    bool? ClearIssuedDate = null,
    bool? ClearExpiryDate = null);

// ---------------------------------------------------------------- documentos por vencer

/// <summary>GET /fleet/expiring-documents. DocType ∈ FleetDocumentKinds; Entity ∈ {VEHICLE, DRIVER}.</summary>
public sealed record ExpiringDocumentsQuery(
    int WithinDays = 30,
    string[]? DocType = null,
    string[]? Entity = null,
    bool IncludeExpired = true);

public sealed record ExpiringDocumentDto(
    string RowKey,
    string OwnerKind,
    Guid OwnerPublicId,
    string OwnerCode,
    string OwnerName,
    string DocumentKind,
    string DocumentTypeCode,
    string DocumentType,
    string? DocNumber,
    DateOnly? IssuedDate,
    DateOnly ExpiryDate,
    int DaysToExpiry,
    string ExpiryState);

// ---------------------------------------------------------------- disponibilidad para despacho

public sealed record AvailabilityIssueDto(string Code, string Message, bool Blocking);

public sealed record DriverAvailabilityDto(
    int Id,
    Guid PublicId,
    string Code,
    string FullName,
    string? ZoneCode,
    bool Available,
    IReadOnlyList<AvailabilityIssueDto> Issues);

public sealed record VehicleAvailabilityDto(
    int Id,
    Guid PublicId,
    string Code,
    string? PlateNumber,
    string? VehicleTypeCode,
    bool Available,
    IReadOnlyList<AvailabilityIssueDto> Issues);

public sealed record FleetAvailabilityDto(
    DateOnly Date,
    IReadOnlyList<DriverAvailabilityDto> Drivers,
    IReadOnlyList<VehicleAvailabilityDto> Vehicles);
