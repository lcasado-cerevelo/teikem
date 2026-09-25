using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Contrato del cliente. Modelo de facturación con 5 componentes (BillPerService por defecto encendido, RC4),
/// cargo por despacho (por orden) y cargo por COD (CodFeeType FIXED|PERCENT + CodFeeValue; "ninguno" = BillCodFee apagado).
/// EndDate y AutoRenew son informativos: no hay vencimiento automático (EXPIRED solo por transición manual).
/// CodCommissionPct se conserva para la remesa COD (módulo 11B) y no se expone en este lote.
/// </summary>
[AuditEntity(Constants.EntityTypes.Contract)]
public class Contract : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int ContractId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int StatusCodeId { get; set; }
    public int? CurrencyLookupId { get; set; }
    /// <summary>Disparador de facturación (BillingModel: BY_PICKUP | BY_DELIVERY | MIXED).</summary>
    public int? BillingModelLookupId { get; set; }
    public decimal? CodCommissionPct { get; set; }
    public bool AutoRenew { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    // --- Lote 2: modelo de facturación ---
    public bool BillPerService { get; set; } = true;
    public bool BillExtraPiece { get; set; }
    public bool BillDispatchFee { get; set; }
    public bool BillCodFee { get; set; }
    public bool BillSpecialServices { get; set; }
    /// <summary>Cargo por despacho: monto por orden.</summary>
    public decimal? DispatchFee { get; set; }
    /// <summary>PricingType FIXED (monto por orden) | PERCENT (por ciento del monto COD cobrado).</summary>
    public int? CodFeeTypeLookupId { get; set; }
    public decimal? CodFeeValue { get; set; }

    public Client? Client { get; set; }
    public StatusCode? Status { get; set; }
    public LookupCode? Currency { get; set; }
    public LookupCode? BillingModel { get; set; }
    public LookupCode? CodFeeType { get; set; }
    public ICollection<ContractServiceLevel> ServiceLevels { get; set; } = new List<ContractServiceLevel>();
}

/// <summary>
/// Nivel de servicio (SLA) por tipo de servicio del contrato; un solo nivel activo por tipo (UX_ContractServiceLevel).
/// No lleva TenantId: se alcanza siempre a través del contrato del tenant.
/// </summary>
[AuditEntity(Constants.EntityTypes.Contract)]
public class ContractServiceLevel : ISoftDeletable
{
    public int ServiceLevelId { get; set; }
    public int ContractId { get; set; }
    public int ServiceTypeLookupId { get; set; }
    public int? MaxTransitHours { get; set; }
    public int? PickupWindowMin { get; set; }
    public decimal? OnTimeTargetPct { get; set; }
    public decimal? PenaltyAmount { get; set; }
    public bool IsActive { get; set; } = true;

    public Contract? Contract { get; set; }
    public LookupCode? ServiceType { get; set; }
}
