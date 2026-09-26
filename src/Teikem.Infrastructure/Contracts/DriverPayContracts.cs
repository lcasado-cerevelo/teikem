using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 4 — Tarifas del chofer (entrega, intento, viaje), política de pago del tenant, vista previa y viajes pagados.
// Records sin lógica; firma posicional fija. Ninguna solicitud de tarifa ni de viaje lleva DriverId ni DriverPublicId:
// el chofer sale de la ruta /drivers/{publicId} (R31).

// ---------------------------------------------------------------- tarifas

public sealed record DriverDeliveryRateDto(
    int Id,
    string ServiceTypeCode,
    string ServiceType,
    string PackageTypeCode,
    string PackageType,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

/// <summary>Una entrada por nivel 1..AttemptLevels; Id/Rate null = 'sin tarifa'.</summary>
public sealed record DriverAttemptRateDto(
    int AttemptNumber,
    int? Id,
    decimal? Rate,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

public sealed record DriverTripRateDto(
    int Id,
    int SpecialServiceTypeId,
    string TripType,
    bool TripTypeActive,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent);

/// <summary>Detalle de tarifas del chofer. ReadOnly = chofer eliminado (solo se consulta).</summary>
public sealed record DriverRatesDto(
    Guid DriverPublicId,
    string DriverCode,
    string DriverName,
    bool ReadOnly,
    DateOnly AsOf,
    int AttemptLevels,
    string PayoutFormulaCode,
    IReadOnlyList<DriverDeliveryRateDto> DeliveryRates,
    IReadOnlyList<DriverAttemptRateDto> AttemptRates,
    IReadOnlyList<DriverTripRateDto> TripRates);

public sealed record DriverDeliveryRateCreateRequest(
    string? ServiceType,
    string? PackageType,
    decimal Rate,
    DateOnly? EffectiveFrom = null);

/// <summary>Editar una tarifa = cerrar y abrir. Servicio/paquete/tipo de viaje llegan en Extra y responden 400.</summary>
public sealed record DriverRateUpdateRequest(decimal Rate, DateOnly? EffectiveFrom = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

public sealed record DriverRateCloseRequest(DateOnly? EffectiveTo = null);

public sealed record DriverTripRateCreateRequest(
    int? SpecialServiceTypeId,
    decimal Rate,
    DateOnly? EffectiveFrom = null);

/// <summary>
/// Opción del selector 'tipo de viaje' (GET /driver-trip-types, driverpay.view): solo id y nombre del tipo activo del
/// catálogo de servicios especiales. No trae el conteo de clientes (dato de contratos, protegido con contracts.read).
/// </summary>
public sealed record DriverTripTypeDto(int Id, string Name);

// ---------------------------------------------------------------- política de pago y vista previa

public sealed record PayoutFormulaOptionDto(string Code, string Label);

/// <summary>Política vigente del tenant. IsDefault = no hay fila (rigen 2 niveles y DELIVERY_PLUS_ATTEMPTS).</summary>
public sealed record DriverPayPolicyDto(
    int AttemptLevels,
    string PayoutFormulaCode,
    string PayoutFormula,
    IReadOnlyList<PayoutFormulaOptionDto> Formulas,
    bool IsDefault,
    DateTime? UpdatedAtUtc);

public sealed record DriverPayPolicyPatchRequest(string? PayoutFormula = null);

public sealed record PayoutAttemptInput(int Number, bool Delivered);

public sealed record PayoutAttemptRateInput(int Number, decimal Rate);

/// <summary>Vista previa sin BD: Formula null = la vigente del tenant.</summary>
public sealed record PayoutPreviewRequest(
    IReadOnlyList<PayoutAttemptInput> Attempts,
    decimal? DeliveryRate = null,
    IReadOnlyList<PayoutAttemptRateInput>? AttemptRates = null,
    string? Formula = null);

public sealed record PayoutLineDto(string Kind, int? AttemptNumber, decimal Amount, string? Note);

public sealed record PayoutPreviewDto(string FormulaCode, decimal Total, IReadOnlyList<PayoutLineDto> Lines);

// ---------------------------------------------------------------- viajes pagados al chofer

public sealed record DriverTripListQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string[]? Status = null,
    bool IncludeCancelled = false);

/// <summary>Viaje con monto congelado. Note = 'sin tarifa configurada' cuando RateMissing.</summary>
public sealed record DriverTripDto(
    int Id,
    Guid PublicId,
    Guid DriverPublicId,
    string DriverCode,
    int SpecialServiceTypeId,
    string TripType,
    DateOnly TripDate,
    decimal Amount,
    bool RateMissing,
    string? Note,
    Guid? OrderPublicId,
    string? OrderNumber,
    string StatusCode,
    string Status,
    string? Notes,
    bool IsActive,
    DateTime CreatedAtUtc);

public sealed record DriverTripCreateRequest(
    int? SpecialServiceTypeId,
    DateOnly? TripDate = null,
    string? Notes = null);

public sealed record DriverTripCancelRequest(string? Comment = null);

/// <summary>POST /orders/{publicId}/driver: asignar o reasignar el chofer de una entrega especial.</summary>
public sealed record SpecialDeliveryAssignRequest(
    Guid DriverPublicId,
    bool OverrideCredit = false,
    string? Comment = null,
    string? RowVersion = null);
