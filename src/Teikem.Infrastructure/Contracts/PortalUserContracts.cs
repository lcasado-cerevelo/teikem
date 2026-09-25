namespace Teikem.Infrastructure.Contracts;

// Lote 2 — Usuarios de portal administrados desde el expediente del cliente (módulo CLIENT_PORTAL).

/// <summary>Usuario de portal de un cliente. HasUser = ya tiene cuenta (ApplicationUser) asociada.</summary>
public sealed record PortalUserDto(int Id, Guid ClientPublicId, string Email, string? FullName, string Role, string RoleLabel,
    string Status, string StatusLabel, DateTime? LastLoginUtc, bool IsActive, bool HasUser);

/// <summary>Invitación: correo, nombre opcional y rol de portal (código de LookupDomains.PortalRole).</summary>
public sealed record PortalInviteRequest(string Email, string? FullName, string Role);

/// <summary>Resultado de invitar/reenviar. InviteToken solo viaja con Portal:ReturnInviteTokenInResponse=true (Development/smoke).</summary>
public sealed record PortalInviteResultDto(PortalUserDto User, string? InviteToken, DateTime ExpiresAtUtc);

/// <summary>Aceptación anónima de la invitación: el invitado fija su propia contraseña (R41).</summary>
public sealed record PortalAcceptInviteRequest(string Email, string Token, string Password);
