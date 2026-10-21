using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 4 — Choferes (ficha maestro-detalle), licencias, certificaciones, dispositivos y zonas de despacho. Records sin
// lógica; firma posicional fija. Ningún DTO de dispositivo expone el PushToken.

public sealed record DriverListQuery(
    string? Search = null,
    string[]? Status = null,
    int? DispatchZoneId = null,
    bool IncludeInactive = false,
    // Lote 130: filtros por campos personalizados con «Mostrar en la búsqueda»
    IReadOnlyList<Teikem.Infrastructure.Services.CustomFieldFilter>? CustomFields = null);

/// <summary>Fila del maestro. Area = nombre de la zona primaria; EffectiveMaxStops = propio o default del tenant (R4).</summary>
public sealed record DriverListItemDto(
    int Id,
    Guid PublicId,
    string Code,
    string FullName,
    int? DispatchZoneId,
    string? ZoneCode,
    string? Area,
    int? MaxStopsPerRoute,
    int? EffectiveMaxStops,
    DateOnly? HireDate,
    string StatusCode,
    string Status,
    string? StatusColor,
    bool IsActive,
    DateOnly? LicenseExpiry,
    bool HasUser);

public sealed record DriverUserDto(int UserId, string Email, string? FullName);

public sealed record DriverLicenseDto(
    int Id,
    string LicenseClassCode,
    string LicenseClass,
    string LicenseNumber,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string ExpiryState,
    int? DaysToExpiry,
    bool IsSuperseded,
    bool IsActive);

public sealed record DriverLicenseRequest(
    string? LicenseClass = null,
    string? LicenseNumber = null,
    DateOnly? IssuedDate = null,
    DateOnly? ExpiryDate = null,
    bool? ClearIssuedDate = null,
    bool? ClearExpiryDate = null);

public sealed record DriverCertificationDto(
    int Id,
    string CertTypeCode,
    string CertType,
    string? CertNumber,
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string ExpiryState,
    int? DaysToExpiry,
    bool IsSuperseded,
    bool IsActive);

public sealed record DriverCertificationRequest(
    string? CertType = null,
    string? CertNumber = null,
    DateOnly? IssuedDate = null,
    DateOnly? ExpiryDate = null,
    bool? ClearIssuedDate = null,
    bool? ClearExpiryDate = null);

/// <summary>Dispositivo de la app del chofer: solo si tiene token (HasPushToken), nunca el token.</summary>
public sealed record DriverDeviceDto(
    int Id,
    string PlatformCode,
    string Platform,
    string? AppVersion,
    bool HasPushToken,
    DateTime? LastSeenUtc,
    bool IsActive);

public sealed record DriverDetailDto(
    int Id,
    Guid PublicId,
    string Code,
    string FullName,
    int? DispatchZoneId,
    string? ZoneCode,
    string? Area,
    int? MaxStopsPerRoute,
    int? EffectiveMaxStops,
    DateOnly? HireDate,
    DriverUserDto? User,
    string StatusCode,
    string Status,
    bool IsTerminal,
    bool IsActive,
    IReadOnlyList<DriverLicenseDto> Licenses,
    IReadOnlyList<DriverCertificationDto> Certifications,
    IReadOnlyList<DriverDeviceDto> Devices,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion);

/// <summary>POST /drivers. Code (EmployeeCode) obligatorio e inmutable; UserId exige además admin.users.</summary>
public sealed record DriverCreateRequest(
    string? Code,
    string? FullName,
    int? DispatchZoneId = null,
    int? MaxStopsPerRoute = null,
    DateOnly? HireDate = null,
    int? UserId = null);

/// <summary>PATCH /drivers/{publicId}: null = sin cambio; Clear* borra. 'code'/'employeeCode' llegan en Extra y responden 400.</summary>
public sealed record DriverPatchRequest(
    string? FullName = null,
    int? DispatchZoneId = null,
    bool? ClearZone = null,
    int? MaxStopsPerRoute = null,
    bool? ClearMaxStopsPerRoute = null,
    DateOnly? HireDate = null,
    int? UserId = null,
    bool? ClearUser = null,
    string? RowVersion = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>DELETE /drivers/{publicId}: baja definitiva (estatus terminal) con comentario opcional.</summary>
public sealed record DriverRetireRequest(string? Comment = null);

/// <summary>Lote 129: ZoneGroup* = el nivel de la zona (Isla/Metro…), o nulos sin nivel.</summary>
public sealed record DispatchZoneDto(int Id, string Code, string? Name, int DriverCount, bool IsActive, int? ZoneGroupId = null, string? ZoneGroupCode = null, string? ZoneGroupName = null);

/// <summary>Lote 129: ZoneGroupId pone el nivel; ClearZoneGroup = true lo quita.</summary>
public sealed record DispatchZoneRequest(string? Code = null, string? Name = null, int? ZoneGroupId = null, bool ClearZoneGroup = false);

/// <summary>Lote 129 — nivel de zona.</summary>
public sealed record ZoneGroupDto(int Id, string Code, string? Name, int SortOrder, Guid? DefaultDriverPublicId, string? DefaultDriverCode, string? DefaultDriverName, int ZoneCount, bool IsActive);

/// <summary>Alta/edición del nivel. Code solo al crear. DefaultDriverPublicId pone el chofer por omisión; ClearDefaultDriver lo quita.</summary>
public sealed record ZoneGroupRequest(string? Code = null, string? Name = null, int? SortOrder = null, Guid? DefaultDriverPublicId = null, bool ClearDefaultDriver = false);

/// <summary>Resultado de asignar zona a las órdenes existentes (Lote 129).</summary>
public sealed record AssignOrderZonesResultDto(int Examined, int Assigned, int Unresolved);

/// <summary>
/// 2026-10-21 — Por qué unos envíos activos siguen sin zona: una fila por (código postal, pueblo) con cuántos envíos son y el motivo (NO_DATA = la parada no trae código postal ni pueblo; NO_MATCH = ninguna zona activa lo cubre;
/// AMBIGUOUS = lo cubren varias zonas del mismo nivel) y, si es ambigua, las zonas que empatan. Las filas van de más envíos a menos.
/// </summary>
public sealed record UnresolvedZoneRowDto(string? PostalCode, string? City, int Orders, string Reason, IReadOnlyList<string> Candidates);

public sealed record UnresolvedZonesDto(int TotalOrders, int Groups, IReadOnlyList<UnresolvedZoneRowDto> Rows);

// ---------------- Lote 5 — miembros de la zona de despacho y resolución 'CP/pueblo → zona' ----------------

/// <summary>Criterio de la zona: MatchTypeCode (POSTAL_CODE, POSTAL_RANGE, MUNICIPALITY) y su valor normalizado.</summary>
public sealed record DispatchZoneMemberDto(int Id, string MatchTypeCode, string MatchType, string MatchValue);

public sealed record DispatchZoneMembersDto(int ZoneId, string Code, string? Name, bool IsActive, IReadOnlyList<DispatchZoneMemberDto> Members);

public sealed record DispatchZoneMemberRequest(string? MatchType, string? MatchValue);

/// <summary>
/// Resolución de una dirección: zona ganadora (id y código) y por qué criterio (MatchedBy); Ambiguous con los códigos
/// empatados en Candidates; sin coincidencia, DispatchZoneId null.
/// </summary>
public sealed record ZoneResolutionDto(
    string? PostalCode,
    string? City,
    int? DispatchZoneId,
    string? ZoneCode,
    string? MatchedBy,
    bool Ambiguous,
    IReadOnlyList<string> Candidates);
