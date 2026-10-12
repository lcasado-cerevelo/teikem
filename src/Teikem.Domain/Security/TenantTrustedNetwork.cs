using Teikem.Domain.Common;
using Teikem.Domain.Tenancy;

namespace Teikem.Domain.Security;

/// <summary>
/// IP preferida / red de confianza de una compañía (2026-10-12; docs/ip-preferida-mfa-diseno.md): una IP o un rango (p. ej. la IP pública del
/// firewall de una localidad). Quien inicia sesión desde una IP que cae en una entrada ACTIVA y vigente no recibe el pedido del código MFA al
/// entrar (la contraseña sí se pide; las acciones sensibles siguen pidiendo MFA). Las lee <c>TrustedNetworkService</c>; las reglas de IP y rangos
/// están en <see cref="TrustedNetworkRules"/>. Apagada por defecto a nivel del servidor (<c>Auth:TrustedNetworks:Enabled</c>).
/// </summary>
[AuditEntity(Constants.EntityTypes.Tenant)]
public class TenantTrustedNetwork : ITenantScoped
{
    public int TenantTrustedNetworkId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    /// <summary>Nombre de la localidad o red («Almacén Bayamón»).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>IP o rango normalizado (<see cref="TrustedCidr.Text"/>): «203.0.113.10/32», «192.168.10.0/24».</summary>
    public string Cidr { get; set; } = string.Empty;
    /// <summary>Activar o desactivar sin borrar: es lo que manda cuando no hay fecha de vencimiento.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Opcional; vacío = no se toma en cuenta. Con fecha, la entrada deja de valer sola al llegar a ella.</summary>
    public DateTime? ExpiresAtUtc { get; set; }
    public string? Note { get; set; }
    /// <summary>Quién y cuándo confirmó «esta IP es solo de la red interna» (obligatorio al crear o al cambiar la IP/rango).</summary>
    public int? InternalOnlyConfirmedByUserId { get; set; }
    public DateTime? InternalOnlyConfirmedAtUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public int? CreatedBy { get; set; }
    public Tenant? Tenant { get; set; }
}
