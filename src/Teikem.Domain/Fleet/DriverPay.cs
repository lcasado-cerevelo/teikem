using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Orders;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Política de pago a choferes del tenant (una fila por compañía, PK TenantId, creada con el primer cambio): niveles de
/// intento contiguos 1..AttemptLevels (tope 20) y fórmula de pago vigente (catálogo DriverPayoutFormula). Sin fila rigen
/// los defaults (2 niveles, DELIVERY_PLUS_ATTEMPTS).
/// </summary>
[AuditEntity(Constants.EntityTypes.DriverRate)]
public class DriverPayPolicy : ITenantScoped
{
    public int TenantId { get; set; }
    public int AttemptLevels { get; set; } = 2;
    public int PayoutFormulaLookupId { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }

    public LookupCode? PayoutFormula { get; set; }
}

/// <summary>
/// Tarifa del chofer por entrega (servicio + tipo de paquete exactos). Efectivo-fechada: editar = cerrar y abrir; una sola
/// fila abierta por (chofer, servicio, paquete) (UQ_DriverDeliveryRate_Open).
/// </summary>
[AuditEntity(Constants.EntityTypes.DriverRate)]
public class DriverDeliveryRate : ITenantScoped, ISoftDeletable, IEffectiveDated
{
    public int DriverDeliveryRateId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int ServiceTypeLookupId { get; set; }
    public int PackageTypeLookupId { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }

    public Driver? Driver { get; set; }
    public LookupCode? ServiceType { get; set; }
    public LookupCode? PackageType { get; set; }
}

/// <summary>Tarifa del chofer por intento (nivel 1..N de la política). Una sola fila abierta por (chofer, nivel).</summary>
[AuditEntity(Constants.EntityTypes.DriverRate)]
public class DriverAttemptRate : ITenantScoped, ISoftDeletable, IEffectiveDated
{
    public int DriverAttemptRateId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int AttemptNumber { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }

    public Driver? Driver { get; set; }
}

/// <summary>
/// Tarifa del chofer por viaje: el tipo de viaje es el catálogo de servicios especiales del tenant (R21). Una sola fila
/// abierta por (chofer, tipo) (UQ_DriverTripRate_Open).
/// </summary>
[AuditEntity(Constants.EntityTypes.DriverRate)]
public class DriverTripRate : ITenantScoped, ISoftDeletable, IEffectiveDated
{
    public int DriverTripRateId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int SpecialServiceTypeId { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }

    public Driver? Driver { get; set; }
    public SpecialServiceType? TripType { get; set; }
}

/// <summary>
/// Viaje pagado al chofer (insumo de la liquidación del Lote 9): monto congelado al crear con la tarifa por viaje vigente
/// en TripDate; sin tarifa, Amount 0 y RateMissing (CK_DriverTrip_Rate). TransportOrderId = entrega especial de origen
/// (NULL = alta manual); un solo viaje vigente por orden (UX_DriverTrip_Order: CANCELLED implica IsActive 0).
/// Estatus DriverTripStatus: OPEN → SETTLED | CANCELLED.
/// </summary>
[AuditEntity(Constants.EntityTypes.DriverTrip)]
public class DriverTrip : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int DriverTripId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int DriverId { get; set; }
    public int SpecialServiceTypeId { get; set; }
    public int? DriverTripRateId { get; set; }
    public int? TransportOrderId { get; set; }
    public DateOnly TripDate { get; set; }
    public decimal Amount { get; set; }
    public bool RateMissing { get; set; }
    public int StatusCodeId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Driver? Driver { get; set; }
    public SpecialServiceType? TripType { get; set; }
    public DriverTripRate? Rate { get; set; }
    public TransportOrder? Order { get; set; }
    public StatusCode? Status { get; set; }
}
