using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Componente de tarifa del contrato (ContractId) o genérico del tenant (ContractId NULL; ServiceType/PackageType NULL =
/// comodín). "Por servicio" = BASE_FREIGHT/FIXED/PER_SHIPMENT con FlatAmount; "Pieza extra" = EXTRA_PIECE/TIERED/GRADUATED/
/// PER_PIECE con RateTier. Historial efectivo-fechado por fila; solo una fila abierta por (contrato, tipo, servicio, paquete)
/// (UQ_RateComponent_Open). RateCard/RateZone/RateRule no se mapean en este lote (RateCardId queda NULL).
/// </summary>
[AuditEntity(Constants.EntityTypes.RateComponent)]
public class RateComponent : ITenantScoped, ISoftDeletable, IEffectiveDated
{
    public int RateComponentId { get; set; }
    public int TenantId { get; set; }
    public int? RateCardId { get; set; }
    public int? ContractId { get; set; }
    public int ComponentTypeLookupId { get; set; }
    public int BasisLookupId { get; set; }
    public int? FromZoneId { get; set; }
    public int? ToZoneId { get; set; }
    public int? ServiceTypeLookupId { get; set; }
    public int? PackageTypeLookupId { get; set; }
    public int PricingModeLookupId { get; set; }
    public int? TierModeLookupId { get; set; }
    public decimal? FlatAmount { get; set; }
    public decimal? UnitAmount { get; set; }
    public decimal? MinCharge { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public Contract? Contract { get; set; }
    public ICollection<RateTier> Tiers { get; set; } = new List<RateTier>();
}

/// <summary>
/// Tramo de un componente escalonado (pieza extra): MinValue = desde, MaxValue = hasta (NULL = abierto), UnitAmount = tarifa
/// por pieza. Efectivo-fechado por fila; el no-traslape entre tramos abiertos lo valida el servicio (RateTierRules).
/// No lleva TenantId: se alcanza siempre a través del componente del contrato.
/// </summary>
[AuditEntity(Constants.EntityTypes.RateComponent)]
public class RateTier : IEffectiveDated
{
    public int RateTierId { get; set; }
    public int RateComponentId { get; set; }
    public decimal MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public decimal? UnitAmount { get; set; }
    public decimal? FlatAmount { get; set; }
    public int SortOrder { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public RateComponent? Component { get; set; }
}
