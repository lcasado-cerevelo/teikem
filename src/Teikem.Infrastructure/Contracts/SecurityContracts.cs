namespace Teikem.Infrastructure.Contracts;

public sealed record PermissionDto(int Id, string Code, string Category, string Label);
public sealed record RoleDto(int Id, string Name, string? Description, IDictionary<string, string> Descriptions, bool IsSystem, bool IsTemplate, bool IsActive, IReadOnlyList<string> Permissions, int UserCount);
public sealed record RoleUpsertRequest(string Name, IDictionary<string, string>? Descriptions, IList<string> Permissions);

public sealed record UserSummaryDto(int Id, string? FullName, string? Email, string? UserKind, bool IsActive, string MembershipStatus, bool MfaEnabled, DateTime? LastLoginUtc, IReadOnlyList<string> Roles, IReadOnlyList<string> ExtraPermissions, bool IsPlatformAdmin);
public sealed record UserCreateRequest(string Email, string FullName, string? Password, IList<string>? Roles, string? UserKind);
public sealed record UserUpdateRequest(string? FullName, bool? IsActive);
public sealed record UserRolesRequest(IList<string> Roles);
public sealed record UserExtraPermissionsRequest(IList<string> Permissions);
public sealed record MembershipStatusRequest(string Status);
public sealed record DataScopeDto(string ScopeEntity, int ScopeId);
public sealed record DataScopesRequest(IList<DataScopeDto> Scopes);

public sealed record AuditLogDto(long Id, DateTime CreatedAtUtc, string EntityType, int EntityId, string Action, int? UserId, string? UserName, string? ChangesJson, Guid? CorrelationId, string? IpAddress);
public sealed record SecurityEventDto(long Id, DateTime CreatedAtUtc, string EventType, string Outcome, int? UserId, string? UserName, string? IpAddress, string? UserAgent, string? DetailJson);
public sealed record ActivityRowDto(string Kind, long Id, DateTime CreatedAtUtc, string Type, string? Detail, int? UserId, string? UserName, string? IpAddress, Guid? CorrelationId);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);
public sealed record SessionDto(long Id, string? DeviceInfo, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? Aal2VerifiedAtUtc, bool IsCurrent);
