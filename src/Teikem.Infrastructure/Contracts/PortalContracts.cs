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
    IReadOnlyList<string>? StepLabels = null);

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
