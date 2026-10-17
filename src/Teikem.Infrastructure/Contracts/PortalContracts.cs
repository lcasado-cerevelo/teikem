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
    DateTime CreatedAtUtc);

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
    IReadOnlyList<PortalOrderEventDto> Timeline);
