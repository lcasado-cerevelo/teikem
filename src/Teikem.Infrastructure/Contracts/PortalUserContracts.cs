namespace Teikem.Infrastructure.Contracts;

// Lote 2 — Usuarios de portal administrados desde el expediente del cliente (módulo CLIENT_PORTAL).
// Lote 3 (ajuste B) — una misma cuenta de portal puede pertenecer a varios clientes del tenant (una fila por cliente).

/// <summary>
/// Usuario de portal de un cliente (una fila por cliente). HasUser = ya tiene cuenta (ApplicationUser) asociada;
/// HasPassword = la cuenta ya fijó contraseña (aceptó alguna invitación); ClientsCount = clientes del tenant a los que
/// pertenece la misma cuenta contando esta fila (filas no dadas de baja: INVITED, ACTIVE o SUSPENDED).
/// </summary>
public sealed record PortalUserDto(int Id, Guid ClientPublicId, string Email, string? FullName, string Role, string RoleLabel,
    string Status, string StatusLabel, DateTime? LastLoginUtc, bool IsActive, bool HasUser, bool HasPassword, int ClientsCount);

/// <summary>Invitación: correo, nombre opcional y rol de portal (código de LookupDomains.PortalRole).</summary>
public sealed record PortalInviteRequest(string Email, string? FullName, string Role);

/// <summary>
/// Resultado de invitar/reenviar. InviteToken solo viaja con Portal:ReturnInviteTokenInResponse=true (Development/smoke).
/// Cuando la cuenta ya tenía contraseña y solo se le agregó otro cliente (Lote 3, ajuste B) no se emite enlace:
/// InviteToken y ExpiresAtUtc vienen null y User.Status ya es ACTIVE.
/// </summary>
public sealed record PortalInviteResultDto(PortalUserDto User, string? InviteToken, DateTime? ExpiresAtUtc);

/// <summary>Aceptación anónima de la invitación: el invitado fija su propia contraseña (R41).</summary>
public sealed record PortalAcceptInviteRequest(string Email, string Token, string Password);

/// <summary>
/// Lote 121 — una persona de portal en la lista de TODA la compañía (Sistema → Roles y usuarios → Clientes y Consignatarios). <c>Kind</c> = CLIENT (persona de un cliente) o CONSIGNEE (acceso propio de un consignatario, con
/// su <c>ConsigneeName</c>). Las acciones (suspender, reactivar, dar de baja, reenviar la invitación) usan los mismos endpoints por cliente que el expediente: <c>ClientPublicId</c> + <c>Id</c>.
/// </summary>
public sealed record PortalUserAdminDto(int Id, Guid ClientPublicId, string ClientName, string ClientCode, string Kind, string? ConsigneeName, string Email, string? FullName, string Role, string RoleLabel,
    string Status, string StatusLabel, DateTime? LastLoginUtc, bool IsActive);
