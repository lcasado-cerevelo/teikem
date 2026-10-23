namespace Teikem.Infrastructure.Contracts;

public sealed record ModuleDto(string Key, string Name, string? Description, string Category, string? DependsOn, int SortOrder, bool IsCore, bool IsEnabled, DateTime? EnabledAtUtc);
public sealed record TenantModuleRequest(bool IsEnabled);

/// <summary>
/// Configuración de la compañía. DeviceSessionDays (Lote 8A): vida en días de la sesión de los aparatos de almacén (1 a 365).
/// Región y formatos (2026-10): RegionCode ('PR' | 'US') y los valores con que se muestran fechas, horas, dinero, números y
/// teléfonos (TenantFormatRules); TimeZoneId es la zona de "hoy". IsRegionCustomized = algún valor difiere del juego de su región
/// (la pantalla la muestra como "Personalizada").
/// DefaultManualIssueReason (2026-10-11 b): código del motivo por default del despacho manual (catálogo ManualIssueReason) que la
/// app y la web preseleccionan; null = sin default, o el guardado ya no está activo y habilitado para la compañía (se ignora).
/// </summary>
public sealed record TenantSettingsDto(
    int Id, Guid PublicId, string Name, string? LegalName, string? TaxId, string DefaultLangCode, byte WorkDaysMask, int MaxStopsPerRouteDefault,
    string? DefaultServiceType, string? DefaultPackageType, bool MfaRequired, int Aal2WindowMinutes, int SessionDays, int DeviceSessionDays, string? BrandingJson, bool IsActive,
    string RegionCode, string TimeZoneId, string CurrencyCode, string CurrencySymbol, string CurrencySymbolPosition, byte CurrencyDecimals,
    string DateOrder, string DateSeparator, byte TimeFormat, byte WeekStartDay, string ThousandsSeparator, string DecimalSeparator,
    string PhoneCountryCode, string PhoneMask, bool IsRegionCustomized,
    string CountExpectedReveal = "MARKED", decimal CountRecountTolerancePct = 0m, bool CountRevealShowsNumber = true,
    bool CountAutoCloseMatching = false, string? DefaultManualIssueReason = null, int? DefaultProductCategoryId = null, string RotationMethod = "FEFO",
    int SessionMinutes = 43200, int DeviceSessionMinutes = 43200, string RotationEnforcement = "SUGGEST",
    int InventoryExpiryWarnDays = 30, int InventoryNoMovementDays = 90, string RotationMethodNoLot = "FIFO",
    string DispatchRequirement = "FREE", string ValuationMethod = "AVERAGE", string ReceiptRequirement = "FREE",
    int FleetDocWarnDays = 30, int? DriverSessionMinutes = null);

/// <summary>
/// Cambio parcial de los ajustes: null = sin cambio. Región y formatos: si RegionCode es distinto del actual se cargan los valores
/// por defecto de esa región y los campos de formato que vengan mandan sobre ellos (sin campos = la región completa).
/// Conteo informado al capturar (tarea 25): CountExpectedReveal ('NONE' | 'MARKED' | 'ALL'), CountRecountTolerancePct (0 a 100) y CountRevealShowsNumber.
/// CountAutoCloseMatching (2026-10-10): terminar un conteo que cuadra lo confirma solo.
/// DefaultManualIssueReason (2026-10-11 b): código del catálogo ManualIssueReason activo y habilitado para la compañía; "" = quitar
/// el default; null = sin cambio.
/// DefaultProductCategoryId (2026-10-11 c): categoría de producto que se preselecciona en un producto nuevo; null = sin cambio; 0 = quitar;
/// un id = debe existir y estar activa en la compañía.
/// RotationMethod (2026-10-11, Lote 34): método de rotación del inventario de la compañía, 'FEFO' (por omisión) o 'FIFO'; null = sin cambio.
/// RotationMethodNoLot (2026-10-20): método de rotación de los productos sin lote ni series, 'FIFO' (por omisión) o 'POSITION' (por posición); RotationMethod queda para los productos por lote (FEFO | FIFO); null = sin cambio.
/// InventoryExpiryWarnDays / InventoryNoMovementDays (2026-10-20, Lote 141): días de anticipación del aviso de lotes por vencer (1 a 365) y días sin movimiento del aviso de productos sin movimiento (1 a 3650); null = sin cambio.
/// RotationEnforcement (2026-10-20): cómo se aplica en las salidas, 'SUGGEST' (por omisión: se propone y se puede cambiar con advertencia) o 'ENFORCE' (el sistema escoge solo); null = sin cambio.
/// </summary>
public sealed record TenantSettingsUpdateRequest(
    string? Name, string? LegalName, string? TaxId, string? DefaultLangCode, byte? WorkDaysMask, int? MaxStopsPerRouteDefault,
    string? DefaultServiceType, string? DefaultPackageType, bool? MfaRequired, int? Aal2WindowMinutes, int? SessionDays, int? DeviceSessionDays, string? BrandingJson,
    string? RegionCode = null, string? TimeZoneId = null, string? CurrencyCode = null, string? CurrencySymbol = null, string? CurrencySymbolPosition = null,
    byte? CurrencyDecimals = null, string? DateOrder = null, string? DateSeparator = null, byte? TimeFormat = null, byte? WeekStartDay = null,
    string? ThousandsSeparator = null, string? DecimalSeparator = null, string? PhoneCountryCode = null, string? PhoneMask = null,
    string? CountExpectedReveal = null, decimal? CountRecountTolerancePct = null, bool? CountRevealShowsNumber = null,
    bool? CountAutoCloseMatching = null, string? DefaultManualIssueReason = null, int? DefaultProductCategoryId = null, string? RotationMethod = null,
    int? SessionMinutes = null, int? DeviceSessionMinutes = null, string? RotationEnforcement = null,
    int? InventoryExpiryWarnDays = null, int? InventoryNoMovementDays = null, string? RotationMethodNoLot = null,
    string? DispatchRequirement = null, string? ValuationMethod = null, string? ReceiptRequirement = null,
    int? FleetDocWarnDays = null, int? DriverSessionMinutes = null);

/// <summary>Juego de valores de región y formatos (los de una región en <see cref="TenantFormatOptionsDto"/>).</summary>
public sealed record TenantFormatDto(
    string RegionCode, string TimeZoneId, string CurrencyCode, string CurrencySymbol, string CurrencySymbolPosition, byte CurrencyDecimals,
    string DateOrder, string DateSeparator, byte TimeFormat, byte WeekStartDay, string ThousandsSeparator, string DecimalSeparator,
    string PhoneCountryCode, string PhoneMask);

/// <summary>
/// Región y formatos (2026-10): las regiones con sus valores por defecto (para "Restaurar valores de la región" y para marcar
/// "Personalizada") y los valores permitidos de cada campo (los mismos CHECK de dbo.Tenant). La zona horaria es libre: cualquier
/// nombre IANA que conozca la plataforma.
/// </summary>
public sealed record TenantFormatOptionsDto(
    IReadOnlyList<TenantFormatDto> Regions, IReadOnlyList<string> CurrencySymbolPositions, IReadOnlyList<int> CurrencyDecimals,
    IReadOnlyList<string> DateOrders, IReadOnlyList<string> DateSeparators, IReadOnlyList<int> TimeFormats, IReadOnlyList<int> WeekStartDays,
    IReadOnlyList<string> ThousandsSeparators, IReadOnlyList<string> DecimalSeparators);

public sealed record TenantHolidayDto(int Id, DateOnly Date, string Name, bool IsRecurring);
public sealed record TenantHolidayRequest(DateOnly Date, string Name, bool IsRecurring);

public sealed record TenantSummaryDto(int Id, Guid PublicId, string Name, string? LegalName, bool IsActive, DateTime CreatedAtUtc, string? CustomDomain = null, string? Subdomain = null);
/// <summary>2026-10-22: dominio propio y subdominio de la compañía (vacío o null = quitarlo). Solo el administrador de la plataforma.</summary>
public sealed record TenantDomainsRequest(string? CustomDomain, string? Subdomain);
/// <summary>2026-10-22: lo único que se ve de una compañía sin sesión (pantalla de entrada): su nombre y qué logos tiene. `Source`: tenant (la última compañía usada), domain, subdomain o none.</summary>
public sealed record PublicBrandDto(string Source, Guid? TenantPublicId, string? Name, IReadOnlyList<PublicBrandLogoDto> Logos);
public sealed record PublicBrandLogoDto(string Slot, DateTime StampUtc);
/// <summary>
/// `MfaRequired`: sin valor, la compañía nueva queda con la política por default (Lote F8a: exige MFA a todos).
/// Región y formatos (2026-10): `RegionCode` sin valor = 'PR'; la compañía nace con los valores por defecto de su región y los
/// campos de formato que vengan mandan sobre ellos (mismas validaciones que PUT /tenant/settings).
/// </summary>
public sealed record TenantProvisionRequest(string Name, string? LegalName, string? TaxId, string? DefaultLangCode, IList<string>? Modules, string AdminEmail, string AdminFullName, string? AdminPassword, bool? MfaRequired = null,
    string? RegionCode = null, string? TimeZoneId = null, string? CurrencyCode = null, string? CurrencySymbol = null, string? CurrencySymbolPosition = null,
    byte? CurrencyDecimals = null, string? DateOrder = null, string? DateSeparator = null, byte? TimeFormat = null, byte? WeekStartDay = null,
    string? ThousandsSeparator = null, string? DecimalSeparator = null, string? PhoneCountryCode = null, string? PhoneMask = null);
public sealed record TenantProvisionResult(TenantSummaryDto Tenant, int AdminUserId, string? TemporaryPassword);

public sealed record MeDto(int UserId, string? FullName, string? Email, bool IsPlatformAdmin, int? TenantId, string? TenantName, string Lang,
    IReadOnlyList<MembershipDto> Memberships, IReadOnlyList<string> Permissions, IReadOnlyList<string> EnabledModules, IReadOnlyList<DataScopeDto> DataScopes, bool MfaEnabled, DateTime? Aal2VerifiedAtUtc, Guid? TenantPublicId = null);
public sealed record MembershipDto(int TenantId, string TenantName, string Status, bool IsDefault);

/// <summary>
/// Logo de la marca de la compañía (una ranura): tipo real, tamaño, ETag (SHA-256 del archivo, el mismo del endpoint de lectura) y
/// cuándo se subió por última vez.
/// </summary>
public sealed record BrandLogoDto(string Slot, string ContentType, int SizeBytes, string ETag, DateTime UpdatedAtUtc);
