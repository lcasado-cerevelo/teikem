namespace Teikem.Infrastructure.Contracts;

public sealed record CatalogDomainDto(int Id, string DomainKey, byte Scope, string Label, IDictionary<string, string> Labels, string? Description, bool IsSystem, bool IsActive, int? TenantId);

public sealed record LookupValueDto(
    int Id, string Entity, string Code, string Label, IDictionary<string, string> Labels, string? Description,
    string? ExtraJson, int SortOrder, bool IsSystem, bool IsEnabled, bool IsOverridden, int? TenantId, bool IsActive);

public sealed record LookupCodeUpsertRequest(string Code, IDictionary<string, string> Labels, IDictionary<string, string>? Descriptions, string? ExtraJson, int? SortOrder);
public sealed record LookupOverrideRequest(IDictionary<string, string>? Labels, string? ExtraJson, bool? IsEnabled, int? SortOverride);
public sealed record CatalogListCreateRequest(string Name, string? NameEn, string? Description, IList<LookupCodeUpsertRequest>? Values);

public sealed record StatusDto(
    int Id, string Entity, string Code, string Label, IDictionary<string, string> Labels, string? ColorHex, string? Icon,
    int SortOrder, string StageKind, bool IsInitial, bool IsEnabled, bool IsOverridden);

public sealed record StatusOverrideRequest(IDictionary<string, string>? Labels, string? ColorHex, bool? IsEnabled, int? SortOverride);

public sealed record StatusCapabilityDto(string StatusCode, string Capability, bool IsAllowed, bool IsTenantRule);
public sealed record StatusCapabilityUpsert(string StatusCode, string Capability, bool IsAllowed);
public sealed record StatusLateralEntryDto(string LateralStatusCode, string FromStatusCode, bool IsAllowed, bool IsTenantRule);
public sealed record StatusLateralEntryUpsert(string LateralStatusCode, string FromStatusCode, bool IsAllowed);

public sealed record StatusHistoryDto(long Id, string? FromCode, string? FromLabel, string ToCode, string ToLabel, string? Comment, DateTime ChangedAtUtc, int? ChangedBy, string? ChangedByName);

public sealed record PipelineValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Lote 1 (cambios de Almacén) — localidad postal del catálogo global (GET /api/v1/postal-localities): ciudad, código postal,
/// estado y país (código y nombre en el idioma del usuario). City es el nombre postal USPS (mayúsculas); en Puerto Rico,
/// Municipality es el municipio (con acentos). El formulario de almacén llena City (municipio si lo hay), State, PostalCode y Country.
/// </summary>
public sealed record PostalLocalityDto(int Id, string City, string PostalCode, string? State, string CountryCode, string Country, string? Municipality);
