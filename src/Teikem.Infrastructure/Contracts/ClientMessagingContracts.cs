namespace Teikem.Infrastructure.Contracts;

// Lote 104 — mensajes del portal con el equipo de servicio al cliente.

/// <summary>Un mensaje. SenderKind: CLIENT o STAFF; SenderName: quien lo escribió; Mine: es mío (el de quien mira).</summary>
public sealed record ClientMessageDto(long Id, string SenderKind, string SenderName, string Body, DateTime CreatedAtUtc, bool Mine);

public sealed record ClientMessageSendRequest(string? Body);

/// <summary>Mi conversación con el equipo (la del portal): ConversationPublicId es null hasta el primer mensaje; Messages en orden cronológico; HasMore = hay mensajes más viejos; Unread = del equipo sin leer.</summary>
public sealed record PortalMessagesDto(Guid? ConversationPublicId, IReadOnlyList<ClientMessageDto> Messages, bool HasMore, int Unread);

public sealed record PortalUnreadDto(int Unread);

// ---------------------------------------------------------------- lado del equipo (web de la compañía)

public sealed record ClientConversationSummaryDto(Guid PublicId, Guid ClientPublicId, string ClientName, int PersonId, string PersonName, string? PersonEmail, string? LastMessagePreview,
    DateTime LastMessageAtUtc, string? LastSenderKind, int Unread);

public sealed record ClientConversationDto(Guid PublicId, Guid ClientPublicId, string ClientName, int PersonId, string PersonName, string? PersonEmail, IReadOnlyList<ClientMessageDto> Messages, bool HasMore);

/// <summary>Abrir una conversación escribiéndole primero a una persona del cliente (PersonId = el usuario de portal).</summary>
public sealed record ClientConversationStartRequest(Guid ClientPublicId, int PersonId, string? Body);

/// <summary>Conversaciones con mensajes del cliente sin leer, entre las que veo.</summary>
public sealed record StaffUnreadDto(int Conversations);

/// <summary>Una persona del cliente (usuario de portal activo) a quien se le puede escribir.</summary>
public sealed record ClientPersonDto(int PersonId, string FullName, string? Email);

public sealed record ClientServiceTeamMemberDto(int UserId, string FullName, string? Email);

/// <summary>El equipo de un cliente. UsesFallback = no hay equipo designado y lo atiende quien tiene el permiso de atender mensajes de clientes (FallbackCount personas).</summary>
public sealed record ClientServiceTeamDto(IReadOnlyList<ClientServiceTeamMemberDto> Members, bool UsesFallback, int FallbackCount);

public sealed record ClientServiceTeamRequest(IReadOnlyList<int>? UserIds);

/// <summary>Una persona de la compañía que se puede designar al equipo (usuario interno activo).</summary>
public sealed record TeamCandidateDto(int UserId, string FullName, string? Email);
