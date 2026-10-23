namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 53 — mensajería interna con hilos. Permiso messages.use (lo traen todos los roles). Cada quien ve solo sus hilos.
// ======================================================================================================================

/// <summary>Una persona de la compañía con su presencia: Online = visto hace menos de 90 s; LastSeenUtc = la última vez (null = nunca).</summary>
public sealed record MessagePersonDto(int Id, string Name, string Email, bool Online, DateTime? LastSeenUtc);

/// <summary>
/// Un hilo en la lista. Kind: DIRECT | GROUP. Name = la otra persona (DIRECT) o el nombre del grupo («Ana, Luis, +2» si no tiene).
/// Other = la otra persona (solo DIRECT) con su presencia. Unread = mensajes de otros que no ha leído; IsMuted = silenciado.
/// </summary>
public sealed record MessageThreadSummaryDto(
    Guid PublicId,
    string Kind,
    string Name,
    int MemberCount,
    MessagePersonDto? Other,
    string? LastMessagePreview,
    string? LastSenderName,
    DateTime LastMessageAtUtc,
    int Unread,
    bool IsMuted);

/// <summary>Un mensaje. Mine = lo escribió quien lo pide (la web lo pinta a la derecha).</summary>
public sealed record MessageDto(long Id, int SenderId, string SenderName, string Body, DateTime CreatedAtUtc, bool Mine, MessageRefDto? Ref = null);

/// <summary>Lote 212 — a qué orden o ruta se refiere un mensaje. Entity ∈ ORDER | TRIP.</summary>
public sealed record MessageRefRequest(string? Entity = null, Guid? PublicId = null);

/// <summary>La tarjeta del hilo: Title (número de orden / código de ruta), Subtitle (cliente / zona), Status. CanOpen = quien lo ve tiene permiso para abrirla; si no, solo se ve Title.</summary>
public sealed record MessageRefDto(string Entity, Guid PublicId, string Title, string? Subtitle, string? Status, bool CanOpen);

/// <summary>Un participante del hilo con su presencia. IsCreator = quien creó el grupo (puede agregar personas).</summary>
public sealed record MessageMemberDto(int UserId, string Name, string Email, bool Online, DateTime? LastSeenUtc, bool IsCreator);

/// <summary>
/// Un hilo con sus mensajes (los más nuevos de la página, de más viejo a más nuevo). HasMore = hay mensajes anteriores (pida con
/// before = el Id del primero). CanAddMembers = es un grupo y quien lo pide lo creó.
/// </summary>
public sealed record MessageThreadDto(
    MessageThreadSummaryDto Thread,
    IReadOnlyList<MessageMemberDto> Members,
    IReadOnlyList<MessageDto> Messages,
    bool HasMore,
    bool CanAddMembers);

/// <summary>
/// Nuevo hilo: UserIds (personas) y/o RoleId (todas las personas activas con ese rol hoy, «en foto»). Una sola persona sin rol = hilo entre
/// dos (se reusa el que ya exista); si no, grupo (Title opcional). Text = el primer mensaje.
/// </summary>
public sealed record MessageThreadRequest(IReadOnlyList<int>? UserIds = null, int? RoleId = null, string? Title = null, string? Text = null, MessageRefRequest? Ref = null);

public sealed record MessageSendRequest(string? Text = null, MessageRefRequest? Ref = null);

public sealed record MessageMembersRequest(IReadOnlyList<int>? UserIds = null);

public sealed record MessageMuteRequest(bool? Muted = null);

/// <summary>Lo no leído: Count = mensajes sin leer en todos los hilos (de otros; los hilos silenciados cuentan pero no avisan), Threads = cuántos hilos y Latest = el último hilo con novedades.</summary>
public sealed record MessageUnreadDto(int Count, int Threads, MessageLatestDto? Latest);

public sealed record MessageLatestDto(Guid ThreadPublicId, string FromName, string Preview, DateTime CreatedAtUtc, bool IsMuted);

public sealed record PresencePingDto(DateTime SeenUtc);

/// <summary>Un rol de la compañía para «escribirle a todo un rol».</summary>
public sealed record MessageRoleDto(int Id, string Name, int MemberCount);

/// <summary>Lo que se empuja por la conexión en vivo a las personas del hilo (el cliente vuelve a pedir lo que cambió).</summary>
public sealed record MessageLiveEvent(int TenantId, Guid ThreadPublicId, long? MessageId, int? SenderId, string? SenderName, string? Preview, bool Muted);

/// <summary>Lote 212 — la mensajería de una persona en esta compañía: Mode ∈ FULL | OFFICE | NONE (ver MessagingModes).</summary>
public sealed record MessagingModeDto(int UserId, string Name, string Email, string Mode);
public sealed record MessagingModeRequest(string? Mode = null);

/// <summary>
/// Lote 212 — un sugerido para «Hablar sobre esta orden»: una persona (UserId) o el grupo de un rol (RoleId). Source: TRIP (responsable de la ruta), ZONE (responsable de la zona),
/// ROLE (rol Despachador) o RECENT (con quien se habló hace poco); SourceLabel es el texto para pintarlo («Responsable de la ruta», «Zona R-01»…).
/// </summary>
public sealed record MessageSuggestionDto(string Source, string SourceLabel, int? UserId, int? RoleId, string Name, bool Online);

/// <summary>Un responsable de una entidad. Entity ∈ TRIP | DISPATCH_ZONE; Key = PublicId de la ruta o id de la zona.</summary>
public sealed record ResponsibleDto(int UserId, string Name, string Email);
public sealed record ResponsiblesRequest(string? Entity = null, string? Key = null, IReadOnlyList<int>? UserIds = null);

/// <summary>Un mensaje (de mis hilos) que habla de una orden o ruta, con el hilo donde está.</summary>
public sealed record MessageAboutDto(Guid ThreadPublicId, long MessageId, string SenderName, string Preview, DateTime CreatedAtUtc);
