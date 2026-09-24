namespace Teikem.Infrastructure.Contracts;

public sealed record ModuleDto(string Key, string Name, string? Description, string Category, string? DependsOn, int SortOrder, bool IsCore, bool IsEnabled, DateTime? EnabledAtUtc);
public sealed record TenantModuleRequest(bool IsEnabled);

public sealed record TenantSettingsDto(
    int Id, Guid PublicId, string Name, string? LegalName, string? TaxId, string DefaultLangCode, byte WorkDaysMask, int MaxStopsPerRouteDefault,
    string? DefaultServiceType, string? DefaultPackageType, bool MfaRequired, int Aal2WindowMinutes, int SessionDays, string? BrandingJson, bool IsActive);
public sealed record TenantSettingsUpdateRequest(
    string? Name, string? LegalName, string? TaxId, string? DefaultLangCode, byte? WorkDaysMask, int? MaxStopsPerRouteDefault,
    string? DefaultServiceType, string? DefaultPackageType, bool? MfaRequired, int? Aal2WindowMinutes, int? SessionDays, string? BrandingJson);

public sealed record TenantHolidayDto(int Id, DateOnly Date, string Name, bool IsRecurring);
public sealed record TenantHolidayRequest(DateOnly Date, string Name, bool IsRecurring);

public sealed record TenantSummaryDto(int Id, Guid PublicId, string Name, string? LegalName, bool IsActive, DateTime CreatedAtUtc);
public sealed record TenantProvisionRequest(string Name, string? LegalName, string? TaxId, string? DefaultLangCode, IList<string>? Modules, string AdminEmail, string AdminFullName, string? AdminPassword);
public sealed record TenantProvisionResult(TenantSummaryDto Tenant, int AdminUserId, string? TemporaryPassword);

public sealed record MeDto(int UserId, string? FullName, string? Email, bool IsPlatformAdmin, int? TenantId, string? TenantName, string Lang,
    IReadOnlyList<MembershipDto> Memberships, IReadOnlyList<string> Permissions, IReadOnlyList<string> EnabledModules, IReadOnlyList<DataScopeDto> DataScopes, bool MfaEnabled, DateTime? Aal2VerifiedAtUtc);
public sealed record MembershipDto(int TenantId, string TenantName, string Status, bool IsDefault);
