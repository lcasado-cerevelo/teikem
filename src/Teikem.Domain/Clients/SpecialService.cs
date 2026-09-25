using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Tipo de servicio especial del tenant (compartido por todos sus clientes; UNIQUE TenantId+Name). Tabla propia y no
/// LookupCode porque UQ_LookupCode(Entity, InternalCode) es global y dos tenants con el mismo nombre chocarían.
/// </summary>
[AuditEntity(Constants.EntityTypes.SpecialService)]
public class SpecialServiceType : ITenantScoped, ISoftDeletable
{
    public int SpecialServiceTypeId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tarifa de un servicio especial para un cliente (componente 5 del modelo de facturación). Efectivo-fechada por fila:
/// editar = cerrar y abrir; quitar = cerrar. Una sola fila abierta por cliente y tipo (UQ_SpecialService_Open).
/// </summary>
[AuditEntity(Constants.EntityTypes.SpecialService)]
public class SpecialService : ITenantScoped, ISoftDeletable, IEffectiveDated
{
    public int SpecialServiceId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int SpecialServiceTypeId { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Client? Client { get; set; }
    public SpecialServiceType? Type { get; set; }
}
