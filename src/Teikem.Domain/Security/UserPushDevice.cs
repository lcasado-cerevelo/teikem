using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Security;

/// <summary>
/// Lote 213 — el teléfono de una persona del personal para los avisos emergentes de la app móvil (FCM), por compañía. Los choferes siguen en DriverDevice (ligado al chofer);
/// esto cubre a quien no es chofer. PushToken es un secreto ([SensitiveData]). Un token activo pertenece a un solo registro por compañía.
/// </summary>
[AuditEntity(Constants.EntityTypes.User)]
public class UserPushDevice : ITenantScoped, ISoftDeletable
{
    public int UserPushDeviceId { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public int PlatformLookupId { get; set; }
    [SensitiveData] public string? PushToken { get; set; }
    public string? AppVersion { get; set; }
    [NotAudited] public DateTime? LastSeenUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public LookupCode? Platform { get; set; }
}
