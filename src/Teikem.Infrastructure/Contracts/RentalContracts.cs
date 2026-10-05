using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 27 (Rentas R1) — contratos de la renta (firma posicional FIJA, WmsContractsTests). La renta se identifica por PublicId; sus
// equipos (líneas) por id int, SIEMPRE bajo su renta filtrada. Cliente, localidad, almacén y producto por PublicId; el contacto
// del cliente por id (ClientContact no tiene PublicId) dentro del cliente de la renta. Sin TenantId en ninguna solicitud.

/// <summary>Tarifa de un equipo (D3: solo se guarda): frecuencia DAILY | WEEKLY | MONTHLY | ONE_TIME ("Fija"), monto ≥ 0 y moneda (USD por defecto).</summary>
public sealed record RentalLineRateInput(string? Frequency, decimal? Amount, string? Currency = null);

/// <summary>Equipos a agregar: un producto propio con serie y sus números de serie (una línea por serie), con tarifa opcional.</summary>
public sealed record RentalLinesAddRequest(Guid? ProductPublicId, IReadOnlyList<string>? SerialNumbers, RentalLineRateInput? Rate = null);

/// <summary>Alta en Borrador (REN-#####). Almacén de origen: el indicado o el único activo. Equipos opcionales en el alta.</summary>
public sealed record RentalCreateRequest(Guid? ClientPublicId, Guid? LocationPublicId, DateOnly? StartDate, DateOnly? PickupDate,
    Guid? WarehousePublicId = null, int? ClientContactId = null, string? ContractNumber = null, DateOnly? ContractSignedOn = null,
    decimal? EstimatedDeliveryCost = null, string? TransportCurrency = null, string? Notes = null, IReadOnlyList<RentalLinesAddRequest>? Lines = null);

/// <summary>
/// PATCH de la renta (solo en Borrador o Programada). null = sin cambio; "" en textos = quitar; Clear* = quitar el valor. El
/// cliente y el número no cambian (400); las llaves desconocidas llegan a Extra.
/// </summary>
public sealed record RentalPatchRequest(Guid? LocationPublicId = null, int? ClientContactId = null, bool? ClearClientContact = null,
    Guid? WarehousePublicId = null, DateOnly? StartDate = null, DateOnly? PickupDate = null, string? ContractNumber = null,
    DateOnly? ContractSignedOn = null, bool? ClearContractSignedOn = null, decimal? EstimatedDeliveryCost = null, bool? ClearEstimatedDeliveryCost = null,
    string? TransportCurrency = null, string? Notes = null, string? RowVersion = null)
{
    [JsonExtensionData] public IDictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>PUT de la tarifa vigente de un equipo (solo antes del despacho).</summary>
public sealed record RentalLineRateRequest(string? Frequency, decimal? Amount, string? Currency = null, string? RowVersion = null);

/// <summary>Programar, despachar o cancelar: comentario opcional para el historial de estatus y rowVersion opcional.</summary>
public sealed record RentalStatusRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Tarifa nueva de un equipo en una extensión (solo si cambia).</summary>
public sealed record RentalExtensionRateInput(int? LineId, string? Frequency, decimal? Amount, string? Currency = null);

/// <summary>Extensión (D4): nueva fecha de recogido (posterior a la vigente), motivo obligatorio y tarifas nuevas opcionales.</summary>
public sealed record RentalExtendRequest(DateOnly? NewPickupDate, string? Reason, IReadOnlyList<RentalExtensionRateInput>? Rates = null,
    string? RowVersion = null);

/// <summary>
/// Lista de rentas: status (DRAFT, SCHEDULED, ON_RENT, RETURNED, CANCELLED), cliente, dueWithinDays (abiertas que vencen de hoy a
/// hoy + N días), overdue (abiertas con el recogido antes de hoy) y search (número, contrato, cliente, localidad o serie).
/// </summary>
public sealed record RentalQuery(string[]? Status = null, Guid? ClientPublicId = null, int? DueWithinDays = null, bool Overdue = false,
    string? Search = null, int Skip = 0, int Take = 100);

public sealed record RentalLineRateDto(int Id, string FrequencyCode, string Frequency, decimal Amount, string CurrencyCode, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, int? ExtensionId);

public sealed record RentalLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, int SerialId, string SerialNumber, string? LotNumber,
    int FromBinId, string FromBinCode, bool IsActive, long? DispatchTxnId, DateTime? DispatchedAtUtc, DateTime? ReturnedAtUtc,
    RentalLineRateDto? Rate, IReadOnlyList<RentalLineRateDto> RateHistory);

/// <summary>Fila de la lista: DaysToPickup e IsOverdue son datos calculados con el "hoy" de la compañía ("vencida" no es estatus).</summary>
public sealed record RentalListItemDto(int Id, Guid PublicId, string Number, Guid ClientPublicId, string ClientName, Guid LocationPublicId,
    string LocationName, string? LocationCity, Guid WarehousePublicId, string WarehouseCode, DateOnly StartDate, DateOnly PickupDate,
    DateOnly OriginalPickupDate, int DaysToPickup, bool IsOverdue, string? ContractNumber, string StatusCode, string Status, string? StatusColor,
    int Units, int ExtensionCount, DateTime? DispatchedAtUtc, DateTime? ClosedAtUtc, DateTime CreatedAtUtc);

/// <summary>
/// Ficha: la fila, los datos de contrato y transporte, los enlaces nulos a Envíos y Facturación, qué se puede hacer y los
/// equipos (los activos; en una renta cancelada, los que tenía al cancelarse) con su tarifa vigente e historial.
/// </summary>
public sealed record RentalDto(RentalListItemDto Rental, int? ClientContactId, string? ClientContactName, DateOnly? ContractSignedOn,
    decimal? EstimatedDeliveryCost, string? TransportCurrencyCode, int? DeliveryShipmentId, int? InvoiceId, string? Notes,
    bool CanEdit, bool CanSchedule, bool CanDispatch, bool CanExtend, bool CanCancel, IReadOnlyList<RentalLineDto> Lines, string RowVersion);

public sealed record RentalPageDto(int Total, int Skip, int Take, IReadOnlyList<RentalListItemDto> Items);

public sealed record RentalExtensionRateDto(int LineId, string SerialNumber, RentalLineRateDto Rate);

/// <summary>Una extensión de la bitácora: fechas anterior y nueva, días agregados, motivo, quién y cuándo, y las tarifas que abrió.</summary>
public sealed record RentalExtensionDto(int Id, DateOnly PreviousPickupDate, DateOnly NewPickupDate, int DaysAdded, string Reason,
    DateTime CreatedAtUtc, int? CreatedBy, string? CreatedByName, IReadOnlyList<RentalExtensionRateDto> Rates);
