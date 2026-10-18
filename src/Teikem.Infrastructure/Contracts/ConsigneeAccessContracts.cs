namespace Teikem.Infrastructure.Contracts;

// Lote 117 — acceso del consignatario (docs/p10-portal-diseno.md). El cliente invita al consignatario por correo; el consignatario entra a su propio espacio del portal.

/// <summary>Una persona con acceso a un consignatario (lo ve el cliente en la ficha del consignatario).</summary>
public sealed record ConsigneeAccessDto(int Id, string Email, string? FullName, string Status, string StatusLabel, DateTime? LastLoginUtc);

/// <summary>Invitar a un consignatario: el correo (si no se escribe, el principal que ya tiene registrado) y el nombre opcional.</summary>
public sealed record ConsigneeAccessInviteRequest(string? Email, string? FullName);

/// <summary>Resultado de invitar o reenviar. InviteToken solo viaja con Portal:ReturnInviteTokenInResponse=true (desarrollo).</summary>
public sealed record ConsigneeAccessInviteResultDto(ConsigneeAccessDto Access, string? InviteToken, DateTime? ExpiresAtUtc);

/// <summary>
/// Un medio de aviso del consignatario. Enabled = hoy recibe por ahí; CanTurnOn = puede volver a prenderlo él (solo el que apagó él mismo y que no se dio de baja por el enlace);
/// Unsubscribed = se dio de baja definitiva por el enlace de un aviso.
/// </summary>
public sealed record ConsigneeChannelDto(string? Contact, bool Enabled, bool CanTurnOn, bool Unsubscribed);

/// <summary>Quién es el consignatario con acceso, de quién es cliente y cómo recibe los avisos.</summary>
public sealed record ConsigneeMeDto(string? FullName, string? Email, string ConsigneeName, string? City, string ClientName, bool NoticesEnabled, ConsigneeChannelDto EmailChannel, ConsigneeChannelDto SmsChannel);

/// <summary>Prender o apagar un medio de aviso desde el perfil.</summary>
public sealed record ConsigneeChannelRequest(bool? Enabled);

/// <summary>Un aviso que se le envió al consignatario (la bandeja). Title = «Su paquete va en camino»; TrackToken lleva a la página de seguimiento de la orden.</summary>
public sealed record ConsigneeInboxItemDto(int Id, string Kind, string Channel, string Title, string? Waybill, string? TrackToken, string State, DateTime CreatedAtUtc, DateTime? SentAtUtc);

// ---------------------------------------------------------------- los envíos del consignatario (Lote 117d): solo ve; NO crea órdenes (Luis, 2026-10-18)

/// <summary>Un envío dirigido al consignatario. Sin precios, factura ni número de orden del cliente. TrackToken lleva a la página de seguimiento si la orden ya tiene enlace.</summary>
public sealed record ConsigneeOrderListItemDto(Guid PublicId, string WaybillNumber, string ServiceTypeLabel, int TotalPieces, string Status, string StatusLabel, DateTime CreatedAtUtc,
    DateTime? PromisedDate, PortalProgressDto? Progress, string? TrackToken);

/// <summary>La ficha de un envío para el consignatario: su dirección de entrega, los paquetes (sin pesos), la línea de tiempo y el avance.</summary>
public sealed record ConsigneeOrderDto(Guid PublicId, string WaybillNumber, string ServiceTypeLabel, string Status, string StatusLabel, int TotalPieces, DateTime? PromisedDate, DateTime CreatedAtUtc,
    PortalOrderStopDto? Delivery, IReadOnlyList<PortalOrderPackageDto> Packages, IReadOnlyList<PortalOrderEventDto> Timeline, PortalProgressDto? Progress, IReadOnlyList<string>? StepLabels);

// ---------------------------------------------------------------- mensajes del consignatario con su cliente (Lote 117b)
// El consignatario usa PortalMessagesDto / PortalUnreadDto / ClientMessageDto / ClientMessageSendRequest del Lote 104 (SenderKind: CONSIGNEE o CLIENT).

/// <summary>Una conversación de un consignatario con el cliente, para la bandeja del cliente. Unread = mensajes del consignatario que ningún administrador u operador ha leído.</summary>
public sealed record ConsigneeConversationSummaryDto(Guid PublicId, Guid LocationPublicId, string ConsigneeName, string PersonName, string? PersonEmail, string? LastMessagePreview,
    DateTime LastMessageAtUtc, string? LastSenderKind, int Unread);

/// <summary>La conversación abierta (lado del cliente): Messages en orden cronológico; HasMore = hay mensajes más viejos.</summary>
public sealed record ConsigneeConversationDto(Guid PublicId, Guid LocationPublicId, string ConsigneeName, string PersonName, string? PersonEmail, IReadOnlyList<ClientMessageDto> Messages, bool HasMore);
