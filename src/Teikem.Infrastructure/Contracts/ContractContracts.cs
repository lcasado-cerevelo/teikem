namespace Teikem.Infrastructure.Contracts;

// Lote 2 (P3): contratos. StatusChangeRequest (ToCode, Comment) vive en ClientContracts.cs y se comparte con clientes.

/// <summary>Fila de la lista de contratos (pantalla de Catálogo o pestaña Contratos del cliente).</summary>
public sealed record ContractSummaryDto(
    int Id, Guid PublicId, Guid ClientPublicId, string ClientName,
    string ContractNumber, string Title, DateOnly StartDate, DateOnly? EndDate,
    string Status, string StatusLabel, bool AutoRenew, string BillingSummary, bool IsActive);

/// <summary>Los 5 checkboxes del modelo de facturación y su resumen legible ("Por servicio, COD").</summary>
public sealed record ContractBillingModelDto(
    bool BillPerService, bool BillExtraPiece, bool BillDispatchFee, bool BillCodFee, bool BillSpecialServices, string Summary);

/// <summary>Cargo por COD configurado: FIXED = monto fijo por orden; PERCENT = por ciento del monto COD cobrado.</summary>
public sealed record CodFeeDto(string Type, decimal Value);

public sealed record ServiceLevelDto(
    int Id, string ServiceType, string ServiceTypeLabel,
    int? MaxTransitHours, int? PickupWindowMin, decimal? OnTimeTargetPct, decimal? PenaltyAmount);

/// <summary>
/// Ficha del contrato. CodFee es null si nunca se configuró tipo/valor (aunque el checkbox esté apagado se conserva
/// lo configurado, R7b). CanEdit = la capacidad EDIT_CONTRACT está permitida en el estatus actual.
/// </summary>
public sealed record ContractDetailDto(
    int Id, Guid PublicId, Guid ClientPublicId, string ClientName,
    string ContractNumber, string Title, DateOnly StartDate, DateOnly? EndDate, bool AutoRenew,
    string Status, string StatusLabel, string? Currency, string? BillingTrigger, string? Notes,
    ContractBillingModelDto BillingModel, decimal? DispatchFee, CodFeeDto? CodFee,
    IReadOnlyList<ServiceLevelDto> ServiceLevels, bool CanEdit, bool IsActive, string RowVersion);

/// <summary>
/// Alta de contrato adicional (el inicial se crea con el cliente). ContractNumber vacío = '{Code}-C{n}' automático.
/// BillPerService queda encendido por defecto (RC4); los otros 4 componentes apagados. BillingTrigger = BillingModel
/// (BY_PICKUP | BY_DELIVERY | MIXED).
/// </summary>
public sealed record ContractCreateRequest(
    Guid ClientPublicId,
    string? ContractNumber,
    string Title,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    bool AutoRenew = false,
    string? Currency = null,
    string? BillingTrigger = null,
    string? Notes = null,
    IList<ServiceLevelUpsert>? ServiceLevels = null);

/// <summary>
/// Edición parcial: null = sin cambio. StartDate = 'cliente desde' (documento: editable inline); decide el contrato
/// vigente (StartDate &lt;= fecha consultada) y debe seguir siendo &lt;= EndDate. ClearEndDate=true borra la fecha fin;
/// Notes con cadena vacía borra las notas. EndDate y AutoRenew son informativos: no hay vencimiento ni renovación automáticos.
/// RowVersion (base64 de la ficha) activa el control de concurrencia (409 si cambió).
/// </summary>
public sealed record ContractPatchRequest(
    string? Title = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool? ClearEndDate = null,
    bool? AutoRenew = null,
    string? Currency = null,
    string? BillingTrigger = null,
    string? Notes = null,
    string? RowVersion = null);

/// <summary>Solo cambia banderas; desmarcar NO borra DispatchFee/CodFee/tarifas/servicios especiales (R7b).</summary>
public sealed record BillingModelRequest(
    bool? BillPerService = null, bool? BillExtraPiece = null, bool? BillDispatchFee = null,
    bool? BillCodFee = null, bool? BillSpecialServices = null);

/// <summary>Cargo por despacho: monto por orden (>= 0). No toca el checkbox BillDispatchFee.</summary>
public sealed record DispatchFeeRequest(decimal Amount);

/// <summary>Cargo por COD: Type FIXED (monto por orden, >= 0) o PERCENT (0..100 sobre el monto COD). No toca el checkbox BillCodFee.</summary>
public sealed record CodFeeRequest(string Type, decimal Value);

/// <summary>Nivel de servicio (SLA) por tipo de servicio; la lista se reemplaza completa con PUT.</summary>
public sealed record ServiceLevelUpsert(
    string ServiceType, int? MaxTransitHours = null, int? PickupWindowMin = null,
    decimal? OnTimeTargetPct = null, decimal? PenaltyAmount = null);
