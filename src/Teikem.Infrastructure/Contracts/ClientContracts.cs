namespace Teikem.Infrastructure.Contracts;

// ---------------- Lista y alta ----------------

public sealed record ClientListItemDto(
    int Id, Guid PublicId, string Code, string Name, string Status, string StatusLabel, bool IsActive,
    string BillingSummary, int ContractsCount);

/// <summary>Alta compuesta del modal '+ Nuevo cliente': el cliente y, opcionalmente, su contrato inicial (DRAFT, por servicio).</summary>
public sealed record ClientCreateRequest(
    string? Code, string Name, string? LegalName, string? TaxId, string? PaymentTerm, string? Currency, decimal? CreditLimit,
    ClientInitialContractRequest? Contract);

/// <summary>
/// Contrato inicial del alta: StartDate = 'cliente desde'. Title por defecto 'Contrato marco'. Los niveles de servicio
/// usan la misma forma y las mismas reglas (ContractRules.ValidateServiceLevels) que PUT /contracts/{id}/service-levels.
/// </summary>
public sealed record ClientInitialContractRequest(
    DateOnly StartDate, string? Title, DateOnly? EndDate, bool? AutoRenew, IList<ServiceLevelUpsert>? ServiceLevels);

// ---------------- Ficha ----------------

/// <summary>Dirección del cliente (fila de Location de tipo CORPORATE/BILLING/PICKUP/BOTH).</summary>
public sealed record ClientAddressDto(
    int Id, Guid PublicId, string? Code, string Name, string LocationType, string LocationTypeLabel,
    string Line1, string? Line2, string City, string? State, string? PostalCode, string Country, string CountryLabel, bool IsActive);

/// <summary>Punto de recogido habitual: el almacén elegido o, si no hay, la dirección corporativa (IsDefaultFromCorporate).</summary>
public sealed record ClientPickupAddressDto(
    int Id, Guid PublicId, string? Code, string Name, string LocationType, string LocationTypeLabel,
    string Line1, string? Line2, string City, string? State, string? PostalCode, string Country, string CountryLabel, bool IsActive,
    bool IsDefaultFromCorporate);

public sealed record ClientNumberSettingsDto(
    bool ClientAssignsOrderNumber, bool ClientAssignsInvoiceNumber,
    string? OrderNumberFormat, string? InvoiceNumberFormat, string? PackageNumberFormat,
    string EffectiveOrderNumberFormat, string EffectiveInvoiceNumberFormat, string EffectivePackageNumberFormat,
    string OrderNumberPreview, string InvoiceNumberPreview, string PackageNumberPreview);

public sealed record ClientContractBillingDto(
    bool BillPerService, bool BillExtraPiece, bool BillDispatchFee, bool BillCodFee, bool BillSpecialServices, string Summary);

/// <summary>Resumen de contrato dentro de la ficha del cliente (la ficha completa vive en /contracts).</summary>
public sealed record ClientContractSummaryDto(
    int Id, Guid PublicId, string ContractNumber, string Title, DateOnly StartDate, DateOnly? EndDate,
    string Status, string StatusLabel, bool AutoRenew, ClientContractBillingDto BillingModel, string BillingSummary,
    bool IsActive, bool IsCurrent);

public sealed record ClientContactDto(
    int Id, string FullName, string? Role, bool IsPrimary, bool IsActive, IReadOnlyList<ContactPointDto> ContactPoints);

public sealed record ClientDetailDto(
    int Id, Guid PublicId, string Code, string Name, string? LegalName, string? TaxId, decimal? CreditLimit,
    string? PaymentTerm, string? PaymentTermLabel, string? Currency, string? CurrencyLabel,
    string Status, string StatusLabel, bool IsActive,
    ClientAddressDto? PhysicalAddress, ClientAddressDto? PostalAddress, ClientPickupAddressDto? PickupAddress,
    IReadOnlyList<ClientAddressDto> PickupLocations,
    ClientNumberSettingsDto NumberSettings,
    IReadOnlyList<ClientContactDto> Contacts, IReadOnlyList<ContactPointDto> ContactPoints,
    IReadOnlyList<ClientContractSummaryDto> Contracts, ClientContractSummaryDto? CurrentContract, string BillingSummary,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string? RowVersion);

// ---------------- Edición ----------------

/// <summary>Perfil editable. NO incluye Name (identidad del cliente, R1) ni Code.</summary>
public sealed record ClientProfileUpdateRequest(
    string? LegalName, string? TaxId, decimal? CreditLimit, string? PaymentTerm, string? Currency,
    Guid? DefaultPickupLocationPublicId, bool ClearDefaultPickup, string? RowVersion);

/// <summary>Cadena vacía en un patrón = NULL = patrón por defecto del sistema.</summary>
public sealed record ClientNumberSettingsRequest(
    bool? ClientAssignsOrderNumber, bool? ClientAssignsInvoiceNumber,
    string? OrderNumberFormat, string? InvoiceNumberFormat, string? PackageNumberFormat);

public sealed record NumberPreviewDto(string Pattern, long Seq, string Value);

public sealed record ClientContactCreateRequest(string FullName, string? Role, bool IsPrimary, IList<ContactPointUpsertRequest>? ContactPoints);
public sealed record ClientContactUpdateRequest(string? FullName, string? Role, bool? IsPrimary, bool? IsActive);

/// <summary>Cambio de estatus vía StatusService (cliente y contrato).</summary>
public sealed record StatusChangeRequest(string ToCode, string? Comment);
