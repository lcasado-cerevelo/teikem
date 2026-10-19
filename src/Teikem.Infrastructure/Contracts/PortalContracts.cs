namespace Teikem.Infrastructure.Contracts;

// Lote 101 — contratos del portal del cliente. Son DTOs propios y mínimos: nunca se reutilizan los de la operación interna (llevan precios, notas internas, rutas y choferes).

public sealed record PortalClientDto(Guid ClientPublicId, string Name, string TenantName, bool IsCurrent);

/// <summary>La cuenta que entró al portal: quién es, con qué cliente está trabajando y a qué otros clientes tiene acceso.</summary>
public sealed record PortalMeDto(string? FullName, string? Email, bool MfaEnabled, PortalClientDto CurrentClient, IReadOnlyList<PortalClientDto> Clients);

public sealed record PortalOrderListItemDto(
    Guid PublicId,
    string WaybillNumber,
    string OrderNumber,
    string ClientInvoiceNumber,
    string ConsigneeName,
    string ConsigneeCity,
    string? ConsigneeState,
    string ServiceTypeLabel,
    int TotalPieces,
    decimal? CodAmount,
    string Status,
    string StatusLabel,
    DateTime CreatedAtUtc,
    PortalProgressDto? Progress = null);

/// <summary>
/// El avance de la orden para la barra de progreso: etapa actual (Step) de Total (las etapas son los estatus del camino normal de la compañía), estado (IN_PROGRESS, COMPLETED, ALERT = detenida, CANCELLED) y el estatus actual
/// con su texto. En la ficha, StepLabels trae el texto de cada etapa.
/// </summary>
public sealed record PortalProgressDto(int Step, int Total, string State, string StatusCode, string StatusLabel);

public sealed record PortalOrderStopDto(string? Name, string Line1, string? Line2, string City, string? State, string? PostalCode);

public sealed record PortalOrderPackageDto(string Description, int Pieces, decimal? WeightKg);

/// <summary>Un cambio de estatus de la orden (la línea de tiempo del cliente). Sin comentarios ni quién lo hizo: eso es interno.</summary>
public sealed record PortalOrderEventDto(string Status, string StatusLabel, DateTime AtUtc);

public sealed record PortalOrderDto(
    Guid PublicId,
    string WaybillNumber,
    string OrderNumber,
    string ClientInvoiceNumber,
    string ServiceTypeLabel,
    string Status,
    string StatusLabel,
    int TotalPieces,
    decimal? TotalWeightKg,
    decimal? CodAmount,
    string? CodStatusLabel,
    DateTime? RequestedDate,
    DateTime? PromisedDate,
    DateTime CreatedAtUtc,
    PortalOrderStopDto? Pickup,
    PortalOrderStopDto Delivery,
    IReadOnlyList<PortalOrderPackageDto> Packages,
    IReadOnlyList<PortalOrderEventDto> Timeline,
    PortalProgressDto? Progress = null,
    IReadOnlyList<string>? StepLabels = null,
    PortalDeliveryProofDto? DeliveryProof = null);

/// <summary>
/// Lote 119 — el comprobante de entrega que ve el cliente: quién recibió y cuándo, si lo registró el personal de la compañía (sin nombre del usuario) y si el consignatario firmó, y la ubicación si se encontró. Sin notas internas.
/// </summary>
public sealed record PortalDeliveryProofDto(DateTime DeliveredAtUtc, string RecipientName, bool RegisteredByStaff, bool SignedByRecipient, double? Latitude, double? Longitude);

/// <summary>Cuántas órdenes del cliente hay en cada estatus (para el inicio del portal).</summary>
public sealed record PortalStatusCountDto(string Status, string StatusLabel, int Count);

/// <summary>Inicio del portal: total de órdenes, cuántas hay por estatus y las más recientes.</summary>
public sealed record PortalSummaryDto(int TotalOrders, IReadOnlyList<PortalStatusCountDto> ByStatus, IReadOnlyList<PortalOrderListItemDto> RecentOrders);

/// <summary>Un aviso de la bandeja del portal: el resumen que la compañía configuró mandar a este cliente (canal «bandeja del portal»).</summary>
public sealed record PortalNotificationDto(Guid PublicId, string Kind, string Subject, string BodyText, DateTime CreatedAtUtc);


// ---------------------------------------------------------------- Lote 105: entrada de órdenes del cliente

/// <summary>Un lugar del cliente para escoger en el formulario (consignatario o punto de recogida): sin códigos internos ni zonas.</summary>
public sealed record PortalPlaceDto(Guid PublicId, string Name, string Line1, string? Line2, string City, string? State, string? PostalCode);

/// <summary>Lo que el formulario de «Nueva orden» necesita: si esta persona puede crear, y los consignatarios y puntos de recogida del cliente.</summary>
public sealed record PortalOrderFormDto(bool CanCreate, IReadOnlyList<PortalPlaceDto> Consignees, IReadOnlyList<PortalPlaceDto> Pickups, int MaxPackages, bool ClientTypesInvoiceNumber = false);

public sealed record PortalNewConsigneeRequest(string? Name, string? Line1, string? Line2, string? City, string? State, string? PostalCode);

public sealed record PortalPackageRequest(string? Description, int Pieces = 1, decimal? WeightKg = null);

/// <summary>Nueva orden desde el portal. Exactamente uno de ConsigneePublicId / NewConsignee. Solo lo básico: sin precios, prioridad, tipo de servicio ni entrega especial.</summary>
public sealed record PortalOrderCreateRequest(
    Guid? ConsigneePublicId,
    PortalNewConsigneeRequest? NewConsignee,
    Guid? PickupPublicId,
    IList<PortalPackageRequest>? Packages,
    decimal? CodAmount,
    string? ClientInvoiceNumber,
    DateTime? RequestedDate,
    string? Notes);

public sealed record PortalOrderCreatedDto(Guid PublicId, string WaybillNumber, string OrderNumber, string Status, string StatusLabel);

// Lote 119 (P10) — facturas del cliente en su portal (solo lectura).

/// <summary>Una factura en la lista del portal: número, estatus, fechas, moneda y lo que va pagado y lo que se debe. Sin borradores.</summary>
public sealed record PortalInvoiceListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, DateOnly? IssueDate, DateOnly? DueDate, string Currency, decimal Total, decimal Paid, decimal Balance);

public sealed record PortalInvoiceLineDto(string Description, decimal Quantity, decimal UnitAmount, decimal LineSubtotal, decimal TaxAmount, string? OrderNumber);

public sealed record PortalInvoicePaymentDto(DateOnly Date, decimal Amount, string MethodLabel, string? Reference);

/// <summary>La factura con sus líneas y los pagos recibidos (sin los anulados). Nunca trae notas internas ni el detalle de un ajuste de la oficina.</summary>
public sealed record PortalInvoiceDto(Guid PublicId, string Number, string Status, string StatusLabel, DateOnly? IssueDate, DateOnly? DueDate, string Currency, decimal Subtotal, decimal TaxAmount, decimal Total,
    decimal Paid, decimal Credited, decimal Balance, IReadOnlyList<PortalInvoiceLineDto> Lines, IReadOnlyList<PortalInvoicePaymentDto> Payments);

// Lote 119 (P10) — preferencias de aviso de la persona del portal.

/// <summary>
/// Un aviso con lo que la compañía tiene encendido para este cliente (<c>EmailAvailable</c>, <c>AppAvailable</c>: el techo, no se cambia aquí) y lo que la persona quiere recibir (<c>EmailEnabled</c>, <c>AppEnabled</c>; por omisión sí).
/// </summary>
public sealed record PortalNoticePrefDto(string Kind, bool EmailAvailable, bool AppAvailable, bool EmailEnabled, bool AppEnabled);

/// <summary>Los cinco avisos con la configuración de la persona. <c>EmailConfigured</c>: el servidor puede mandar correo.</summary>
public sealed record PortalNoticePrefsDto(IReadOnlyList<PortalNoticePrefDto> Prefs, bool EmailConfigured);

/// <summary>Lo que la persona quiere recibir de cada aviso; solo cuentan <c>Kind</c>, <c>EmailEnabled</c> y <c>AppEnabled</c>. Un aviso que no venga no cambia.</summary>
public sealed record PortalNoticePrefsRequest(IList<PortalNoticePrefDto>? Prefs);
