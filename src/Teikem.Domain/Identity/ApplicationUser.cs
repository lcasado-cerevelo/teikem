using Microsoft.AspNetCore.Identity;
using Teikem.Domain.Common;

namespace Teikem.Domain.Identity;

/// <summary>
/// AspNetUsers. ASP.NET Core Identity con llaves int, extendido con las columnas de aplicación del esquema
/// (FullName, DefaultTenantId, UserKindLookupId, IsActive, CreatedAtUtc, LastLoginUtc) + IsPlatformAdmin (Lote 1).
/// </summary>
[AuditEntity(Constants.EntityTypes.User)]
public class ApplicationUser : IdentityUser<int>, ISoftDeletable
{
    public string? FullName { get; set; }
    public int? DefaultTenantId { get; set; }
    public int? UserKindLookupId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public DateTime? LastLoginUtc { get; set; }
    /// <summary>Administrador de Teikem (soporte): opera cualquier tenant; sus acciones quedan atribuidas a él y visibles para el tenant.</summary>
    public bool IsPlatformAdmin { get; set; }

    [SensitiveData] public override string? PasswordHash { get => base.PasswordHash; set => base.PasswordHash = value; }
    [SensitiveData] public override string? SecurityStamp { get => base.SecurityStamp; set => base.SecurityStamp = value; }
    [NotAudited] public override string? ConcurrencyStamp { get => base.ConcurrencyStamp; set => base.ConcurrencyStamp = value; }
    [NotAudited] public override int AccessFailedCount { get => base.AccessFailedCount; set => base.AccessFailedCount = value; }

    public Tenancy.Tenant? DefaultTenant { get; set; }
    public Catalogs.LookupCode? UserKind { get; set; }
    public ICollection<Security.UserTenant> Memberships { get; set; } = new List<Security.UserTenant>();
}

/// <summary>AspNetRoles: solo plumbing de Identity. El RBAC real de la plataforma vive en dbo.Role/RolePermission/UserRole.</summary>
public class ApplicationRole : IdentityRole<int>
{
}
