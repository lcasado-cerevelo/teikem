using Teikem.Domain.Common;
using Teikem.Domain.Identity;

namespace Teikem.Domain.Security;

/// <summary>Usuario↔tenant (N:M) con estatus de membresía separado del estado global del usuario.</summary>
[AuditEntity(Constants.EntityTypes.User)]
public class UserTenant : ITenantScoped, IHasStatus
{
    public int UserTenantId { get; set; }
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public bool IsDefault { get; set; }
    /// <summary>Entity='MembershipStatus'.</summary>
    public int StatusCodeId { get; set; }
    public int? InvitedBy { get; set; }
    public DateTime? JoinedAtUtc { get; set; }
    public ApplicationUser? User { get; set; }
    public Tenancy.Tenant? Tenant { get; set; }
    public Catalogs.StatusCode? Status { get; set; }
}

/// <summary>Permiso granular (recurso.acción). Sembrado desde código; el tenant no lo edita.</summary>
public class Permission
{
    public int PermissionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int CategoryLookupId { get; set; }
    public string LabelJson { get; set; } = "{}";
    public bool IsSystem { get; set; } = true;
    public Catalogs.LookupCode? Category { get; set; }
}

/// <summary>Rol componible por tenant. TenantId NULL = plantilla de sistema clonable al aprovisionar.</summary>
[AuditEntity(Constants.EntityTypes.Role)]
public class Role : IOptionallyTenantScoped, ISoftDeletable
{
    public int RoleId { get; set; }
    public int? TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DescriptionJson { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}

[AuditEntity(Constants.EntityTypes.Role)]
public class RolePermission
{
    public int RolePermissionId { get; set; }
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

[AuditEntity(Constants.EntityTypes.User)]
public class UserRole : ITenantScoped
{
    public int UserRoleId { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public int TenantId { get; set; }
    public int? GrantedBy { get; set; }
    [NotAudited] public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public ApplicationUser? User { get; set; }
    public Role? Role { get; set; }
}

/// <summary>Lote 1: permiso extra concedido directo a una persona (excepción puntual, sin tocar un rol).</summary>
[AuditEntity(Constants.EntityTypes.User)]
public class UserPermission : ITenantScoped
{
    public int UserPermissionId { get; set; }
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public int PermissionId { get; set; }
    public int? GrantedBy { get; set; }
    [NotAudited] public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
    public Permission? Permission { get; set; }
}

/// <summary>Alcance de datos opcional: filtro adicional sobre el filtro de tenant (almacén, cliente...).</summary>
[AuditEntity(Constants.EntityTypes.User)]
public class UserDataScope : ITenantScoped
{
    public int UserDataScopeId { get; set; }
    public int UserId { get; set; }
    public int TenantId { get; set; }
    public int ScopeEntityLookupId { get; set; }
    public int ScopeId { get; set; }
    public Catalogs.LookupCode? ScopeEntity { get; set; }
}

public class UserMfaFactor : ISoftDeletable
{
    public int UserMfaFactorId { get; set; }
    public int UserId { get; set; }
    public int FactorTypeLookupId { get; set; }
    [SensitiveData] public byte[]? SecretEnc { get; set; }
    public string? PhoneE164 { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public Catalogs.LookupCode? FactorType { get; set; }
}

public class MfaRecoveryCode
{
    public int MfaRecoveryCodeId { get; set; }
    public int UserId { get; set; }
    [SensitiveData] public string CodeHash { get; set; } = string.Empty;
    public DateTime? UsedAtUtc { get; set; }
}

/// <summary>Sesión revocable: refresh token hasheado con rotación en cada uso.</summary>
public class RefreshToken : ITenantScoped
{
    public long RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public int TenantId { get; set; }
    [SensitiveData] public string TokenHash { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public DateTime? Aal2VerifiedAtUtc { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [SensitiveData] public string? ReplacedByTokenHash { get; set; }
    public bool IsActive => RevokedAtUtc == null && ExpiresAtUtc > DateTime.UtcNow;
}

/// <summary>Plano 3 de auditoría: quién intentó qué.</summary>
public class SecurityEvent : IOptionallyTenantScoped
{
    public long SecurityEventId { get; set; }
    public int? TenantId { get; set; }
    public int? UserId { get; set; }
    public int EventTypeLookupId { get; set; }
    public int OutcomeLookupId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DetailJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Catalogs.LookupCode? EventType { get; set; }
    public Catalogs.LookupCode? Outcome { get; set; }
}
