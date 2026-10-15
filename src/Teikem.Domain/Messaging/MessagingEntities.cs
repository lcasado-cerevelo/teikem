using Teikem.Domain.Common;

namespace Teikem.Domain.Messaging;

public static class MessageThreadKinds
{
    /// <summary>Hilo entre dos personas (único por pareja).</summary>
    public const string Direct = "DIRECT";
    /// <summary>Hilo de grupo (varias personas o un rol): todos pueden contestar.</summary>
    public const string Group = "GROUP";
}

/// <summary>
/// Lote 53 — un hilo de mensajes de una compañía. Entre dos personas (DIRECT, único por pareja: DirectKey = «menorId-mayorId») o de grupo
/// (GROUP: varias personas o un rol «en foto»: entran quienes tienen el rol al crearlo). Los mensajes nunca se editan ni se borran.
/// LastMessage* son un resumen para ordenar la lista sin leer los mensajes.
/// </summary>
public class MessageThread : ITenantScoped
{
    public long MessageThreadId { get; set; }
    public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Kind { get; set; } = MessageThreadKinds.Direct;
    /// <summary>Nombre del grupo (null = «Ana, Luis, +2»; en DIRECT siempre null).</summary>
    public string? Title { get; set; }
    public string? DirectKey { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public long LastMessageId { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public string? LastMessagePreview { get; set; }
    public int? LastSenderId { get; set; }
}

/// <summary>
/// Participante de un hilo. LastReadMessageId = hasta qué mensaje leyó (de ahí salen los no leídos); JoinedAfterMessageId = quien entra
/// a un grupo después ve el historial solo desde que entra. LeftAtUtc != null = ya no es parte (el hilo deja de existir para esa persona).
/// </summary>
public class MessageThreadMember : ITenantScoped
{
    public long MessageThreadMemberId { get; set; }
    public int TenantId { get; set; }
    public long MessageThreadId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public long JoinedAfterMessageId { get; set; }
    public long LastReadMessageId { get; set; }
    public bool IsMuted { get; set; }
    public DateTime? LeftAtUtc { get; set; }
}

public class Message : ITenantScoped
{
    public long MessageId { get; set; }
    public int TenantId { get; set; }
    public long MessageThreadId { get; set; }
    public int SenderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// «Visto por última vez» de una persona (de la web abierta): la web avisa cada ~30 s por la conexión en vivo o por POST /presence/ping.
/// En línea = visto hace menos de 90 s. Es de la PERSONA (no de una compañía): quien la lee solo la ve de gente de su propia compañía.
/// </summary>
public class UserPresence
{
    public int UserId { get; set; }
    public DateTime LastSeenUtc { get; set; }
}
