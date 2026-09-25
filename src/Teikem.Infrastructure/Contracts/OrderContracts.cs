using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 3 — Órdenes de transporte. Records sin lógica: es el contrato entre piezas (P2–P5, P9) y con el front.
// StatusChangeRequest se reutiliza de ClientContracts.cs; QuoteLineDto de RateContracts.cs.

// ---------------------------------------------------------------- captura

/// <summary>Línea de paquete de la captura. PackageType null = tipo por defecto del tenant; PackageNumber en blanco = PQT-##### del patrón del cliente.</summary>
public sealed record OrderPackageLineRequest(
    string? PackageType,
    string? Description,
    int Pieces = 1,
    decimal? WeightKg = null,
    decimal? VolumeM3 = null,
    string? PackageNumber = null);

/// <summary>
/// Consignatario escrito libre (L244): se resuelve contra el directorio del cliente por nombre + línea 1 normalizados y, si no
/// coincide, se crea una Location DELIVERY del cliente al guardar. Country = código ISO ('PR' por defecto).
/// </summary>
public sealed record OrderConsigneeCreateRequest(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Code,
    bool AllowDupInvoice = false,
    string? DeliveryNotes = null,
    int DefaultServiceMinutes = 0);

public sealed record OrderReferenceRequest(string RefType, string Value, string? Source);

/// <summary>
/// POST /orders (entrada rápida, detallada o especial). Exactamente uno de ConsigneeLocationPublicId / NewConsignee.
/// OrderNumber/ClientInvoiceNumber solo si el cliente los asigna (400 si no). ConfirmDuplicateInvoice = 'Crear de todos modos'
/// (R36). ConfirmNow = crear y confirmar en una sola transacción (DECISIÓN 26). Extra recoge llaves no declaradas para
/// rechazar codType/packBatchNumber con 400 (DECISIÓN 4/15).
/// </summary>
public sealed record OrderCreateRequest(
    Guid? ClientPublicId,
    Guid? ConsigneeLocationPublicId,
    OrderConsigneeCreateRequest? NewConsignee,
    Guid? PickupLocationPublicId,
    string? ServiceType,
    string? Priority,
    IList<OrderPackageLineRequest>? Packages,
    decimal? CodAmount,
    string? OrderNumber,
    string? ClientInvoiceNumber,
    DateTime? RequestedDate,
    DateTime? PromisedDate,
    string? Notes,
    IList<OrderReferenceRequest>? References,
    bool IsSpecialDelivery = false,
    int? SpecialServiceId = null,
    bool ConfirmDuplicateInvoice = false,
    bool ConfirmNow = false)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// PATCH /orders/{id}: null = sin cambio. Packages/References reemplazan la lista completa; ClearCod quita el COD.
/// Cliente, número de orden, factura y empaque se fijan al crear: si llegan (Extra) responden 400 (DECISIÓN 15).
/// </summary>
public sealed record OrderPatchRequest(
    Guid? ConsigneeLocationPublicId,
    OrderConsigneeCreateRequest? NewConsignee,
    Guid? PickupLocationPublicId,
    string? ServiceType,
    string? Priority,
    IList<OrderPackageLineRequest>? Packages,
    decimal? CodAmount,
    bool? ClearCod,
    DateTime? RequestedDate,
    DateTime? PromisedDate,
    string? Notes,
    IList<OrderReferenceRequest>? References,
    string? RowVersion)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (clientPublicId/orderNumber/clientInvoiceNumber/packBatchNumber/codType → 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

// ---------------------------------------------------------------- tipos internos de servicio (nunca se deserializan desde HTTP)

/// <summary>
/// Alcance de cliente que TODOS los métodos de lectura y escritura reciben (DECISIÓN 19). ClientId null = operador interno
/// (Any); ClientId fijado = portal: el alta fija ese cliente ignorando ClientPublicId, lista/lookup filtran por él y
/// ficha/PATCH/DELETE/confirm/reprice/cancel/status responden 404 para órdenes de otro cliente (sin oráculo).
/// </summary>
public sealed record OrderScope(int? ClientId)
{
    public static readonly OrderScope Any = new((int?)null);
}

/// <summary>
/// Opciones de llamadores internos (Recolección y empaque futuro, importador): número de empaque ya asignado por el lote y
/// origen RefEntity/RefId. No es un DTO HTTP.
/// </summary>
public sealed record OrderCreationOptions(
    string? PackBatchNumberOverride = null,
    string? SourceEntityType = null,
    int? SourceEntityId = null);

// ---------------------------------------------------------------- lectura

public sealed record OrderStopDto(
    int Id,
    string StopType,
    int Sequence,
    Guid? LocationPublicId,
    string? Name,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string? PostalCode,
    string CountryCode,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc,
    int ServiceMinutes,
    string? Notes,
    string Status,
    string StatusLabel);

public sealed record OrderPackageLineDto(
    int Id,
    string? PackageType,
    string? PackageTypeLabel,
    string? PackageNumber,
    string Description,
    int Pieces,
    decimal? WeightKg,
    decimal? VolumeM3);

public sealed record OrderReferenceDto(int Id, string RefType, string RefTypeLabel, string Value, string? Source);

/// <summary>Acciones disponibles según el estatus (StatusCapability, tenant pisa default) y el estado del registro.</summary>
public sealed record OrderCapabilitiesDto(bool CanEditCargo, bool CanCancel, bool CanDelete, bool CanConfirm, bool CanReprice);

public sealed record OrderDetailDto(
    int Id,
    Guid PublicId,
    string OrderNumber,
    string ClientInvoiceNumber,
    string PackBatchNumber,
    Guid ClientPublicId,
    string ClientName,
    Guid? ContractPublicId,
    string ServiceType,
    string ServiceTypeLabel,
    string? Priority,
    string Status,
    string StatusLabel,
    bool IsInitialStatus,
    string? Currency,
    decimal? QuotedAmount,
    DateTime? QuotedAtUtc,
    DateTime? ConfirmedAtUtc,
    decimal? CodAmount,
    string? CodStatus,
    string? CodStatusLabel,
    string? CodType,
    int TotalPieces,
    decimal? TotalWeightKg,
    decimal? TotalVolumeM3,
    string PackagesSummary,
    OrderStopDto? Pickup,
    OrderStopDto Delivery,
    IReadOnlyList<OrderPackageLineDto> Packages,
    IReadOnlyList<OrderReferenceDto> References,
    bool IsSpecialDelivery,
    int? SpecialServiceId,
    string? SpecialServiceName,
    string? SourceEntityType,
    int? SourceEntityId,
    DateTime? RequestedDate,
    DateTime? PromisedDate,
    string? Notes,
    OrderCapabilitiesDto Capabilities,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? RowVersion);

/// <summary>Fila del listado: Empaque es la primera columna de negocio (siempre con valor).</summary>
public sealed record OrderListItemDto(
    int Id,
    Guid PublicId,
    string PackBatchNumber,
    string OrderNumber,
    string ClientInvoiceNumber,
    Guid ClientPublicId,
    string ClientName,
    string ConsigneeName,
    string ConsigneeCity,
    string? ConsigneePostalCode,
    string ServiceType,
    string ServiceTypeLabel,
    string PackagesSummary,
    int TotalPieces,
    decimal? CodAmount,
    decimal? QuotedAmount,
    string Status,
    string StatusLabel,
    bool IsInitialStatus,
    bool IsSpecialDelivery,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderPackageLineDto> Packages);

/// <summary>Filtros del listado (LIKE parcial en orden/factura/empaque/consignatario; FromUtc inclusivo, ToUtc exclusivo sobre CreatedAtUtc; Take 1..500).</summary>
public sealed record OrderListQuery(
    Guid? ClientPublicId,
    string? Status,
    string? OrderNumber,
    string? Invoice,
    string? PackBatch,
    string? Consignee,
    string? Search,
    DateTime? FromUtc,
    DateTime? ToUtc,
    bool IncludeInactive = false,
    int Skip = 0,
    int Take = 100);

public sealed record OrderPageDto(int Total, IReadOnlyList<OrderListItemDto> Items);

/// <summary>Buscar o escanear: coincidencia exacta. MatchedBy = ORDER_NUMBER | PACK_BATCH | INVOICE del primer grupo con resultados, o null.</summary>
public sealed record OrderLookupDto(string Code, string? MatchedBy, IReadOnlyList<OrderListItemDto> Matches);

// ---------------------------------------------------------------- estatus, cotización y crédito

public sealed record OrderConfirmRequest(string? RowVersion);

public sealed record OrderCancelRequest(string? Comment, string? RowVersion);

/// <summary>Chequeo de crédito (R18): límite del cliente (null = sin límite), pendiente en curso, esta orden, disponible y si excede.</summary>
public sealed record CreditCheckDto(decimal? CreditLimit, decimal PendingBalance, decimal OrderTotal, decimal? Available, bool Exceeds);

/// <summary>GET /orders/{id}/quote: vista previa de cotización y crédito sin persistir (DECISIÓN 28).</summary>
public sealed record OrderQuotePreviewDto(
    decimal Total,
    IReadOnlyList<QuoteLineDto> Lines,
    decimal DispatchFee,
    decimal CodFee,
    Guid? ContractPublicId,
    DateOnly AsOf,
    bool IsSpecialDelivery,
    string? SpecialServiceName,
    CreditCheckDto Credit);

/// <summary>Resultado interno de OrderQuoteService.ComputeAsync (sin persistir).</summary>
public sealed record OrderQuoteComputation(
    decimal Total,
    IReadOnlyList<QuoteLineDto> Lines,
    decimal DispatchFee,
    decimal CodFee,
    int? ContractId,
    Guid? ContractPublicId,
    string? SpecialServiceName);
