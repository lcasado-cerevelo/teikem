namespace Teikem.Infrastructure.Contracts;

public sealed record PermissionDto(int Id, string Code, string Category, string Label);
public sealed record RoleDto(int Id, string Name, string? Description, IDictionary<string, string> Descriptions, bool IsSystem, bool IsTemplate, bool IsActive, IReadOnlyList<string> Permissions, int UserCount);
public sealed record RoleUpsertRequest(string Name, IDictionary<string, string>? Descriptions, IList<string> Permissions);

/// <summary>HasPin (Lote 8A): el usuario tiene PIN para los aparatos de almacén en la compañía activa.</summary>
public sealed record UserSummaryDto(int Id, string? FullName, string? Email, string? UserKind, bool IsActive, string MembershipStatus, bool MfaEnabled, DateTime? LastLoginUtc, IReadOnlyList<string> Roles, IReadOnlyList<string> ExtraPermissions, bool IsPlatformAdmin,
    bool HasPin = false, bool MfaRequired = false);
/// <remarks>2026-10-01: AlsoTenantIds = otras compañías (de GET /users/assignable-companies) a las que se agrega también al usuario,
/// con los mismos roles por nombre en cada una.</remarks>
public sealed record UserCreateRequest(string Email, string FullName, string? Password, IList<string>? Roles, string? UserKind, IList<int>? AlsoTenantIds = null);
/// <summary>Compañía distinta de la activa a la que quien crea el usuario puede agregarlo (administra usuarios allí).</summary>
public sealed record AssignableCompanyDto(int TenantId, string Name);
public sealed record UserUpdateRequest(string? FullName, bool? IsActive);
/// <summary>Respuesta del alta de usuario. `TemporaryPassword` solo viene con valor cuando no se mandó `Password`
/// (se generó una); se muestra una sola vez — el servidor no la vuelve a devolver en ninguna otra respuesta.</summary>
/// <remarks>AlsoAddedTo / AlreadyMemberOf: nombres de las compañías extra a las que se agregó y de las que ya era miembro (no se tocan).</remarks>
public sealed record UserCreateResponseDto(UserSummaryDto User, string? TemporaryPassword, IReadOnlyList<string>? AlsoAddedTo = null, IReadOnlyList<string>? AlreadyMemberOf = null);
public sealed record UserRolesRequest(IList<string> Roles);
public sealed record UserExtraPermissionsRequest(IList<string> Permissions);
public sealed record MembershipStatusRequest(string Status);
/// <summary>Lote F8a: exige (o deja de exigir) MFA a este usuario en la compañía activa, aparte de Tenant.MfaRequired.</summary>
public sealed record MfaRequiredRequest(bool Required);
public sealed record DataScopeDto(string ScopeEntity, int ScopeId);
public sealed record DataScopesRequest(IList<DataScopeDto> Scopes);

/// <remarks>Lote F10: `ActionCode` = InternalCode de la acción (CREATE, UPDATE, DELETE, RESTORE), además de su etiqueta.</remarks>
public sealed record AuditLogDto(long Id, DateTime CreatedAtUtc, string EntityType, int EntityId, string Action, int? UserId, string? UserName, string? ChangesJson, Guid? CorrelationId, string? IpAddress,
    string? ActionCode = null);
/// <remarks>Lote F10: `EventTypeCode` (LOGIN, PERMISSION_DENIED…) y `OutcomeCode` (SUCCESS, FAILURE, BLOCKED), además de sus etiquetas.</remarks>
public sealed record SecurityEventDto(long Id, DateTime CreatedAtUtc, string EventType, string Outcome, int? UserId, string? UserName, string? IpAddress, string? UserAgent, string? DetailJson,
    string? EventTypeCode = null, string? OutcomeCode = null);
/// <remarks>
/// Lote F10: `TypeCode` = código de la acción (cambio) o del tipo de evento (seguridad) y `OutcomeCode` = resultado del evento
/// (null en un cambio): la pantalla decide con ellos la etiqueta Cambio / Evento / Alerta sin leer textos traducidos.
/// </remarks>
public sealed record ActivityRowDto(string Kind, long Id, DateTime CreatedAtUtc, string Type, string? Detail, int? UserId, string? UserName, string? IpAddress, Guid? CorrelationId,
    string? TypeCode = null, string? OutcomeCode = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);
public sealed record SessionDto(long Id, string? DeviceInfo, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? Aal2VerifiedAtUtc, bool IsCurrent);

/// <summary>
/// Lote F10 — sesión activa de CUALQUIER usuario de la compañía activa (Seguridad y auditoría → Sesiones y MFA). Una sesión es
/// la cadena de refresh tokens rotados: se muestra el token vigente, así que `LastActivityUtc` es su emisión (la última
/// renovación, que ocurre cada vez que vence el access token). `DeviceName` = código y nombre del aparato de almacén si la
/// sesión es de un aparato (`IsDevice`). `IsCurrent` = la sesión de quien consulta (no se revoca desde la lista).
/// </summary>
public sealed record CompanySessionDto(long Id, int UserId, string? UserName, string? UserEmail, string? DeviceInfo, string? DeviceName, bool IsDevice,
    string? IpAddress, DateTime LastActivityUtc, DateTime ExpiresAtUtc, bool IsCurrent);

/// <summary>Lote F10 — resultado de "Cerrar las demás sesiones": cuántas sesiones de la compañía se revocaron.</summary>
public sealed record RevokeSessionsResultDto(int Revoked);
