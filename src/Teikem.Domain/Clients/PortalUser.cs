using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Identity;

namespace Teikem.Domain.Clients;

/// <summary>
/// Usuario del portal de un cliente (módulo CLIENT_PORTAL), administrado desde el expediente del cliente.
/// Su cuenta es un ApplicationUser de tipo PORTAL sin UserTenant: el alcance del portal es ClientId.
/// Estatus INVITED (inicial) → ACTIVE; SUSPENDED lateral reversible; DISABLED terminal (baja definitiva, nunca DELETE).
/// </summary>
[AuditEntity(Constants.EntityTypes.PortalUser)]
public class PortalUser : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int PortalUserId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public int RoleLookupId { get; set; }
    public int StatusCodeId { get; set; }
    [NotAudited] public DateTime? LastLoginUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public Client? Client { get; set; }
    public LookupCode? Role { get; set; }
    public StatusCode? Status { get; set; }
    public ApplicationUser? User { get; set; }
}
