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

/// <summary>
/// Qué tan visible es una persona en la mensajería de una compañía (UserTenant.MessagingMode): FULL = ve a todos y todos la ven; OFFICE = solo ve a las personas FULL
/// (el chofer por omisión: habla con la oficina, no con otros choferes); NONE = no aparece en ninguna lista ni puede escribir.
/// </summary>
public static class MessagingModes
{
    public const string Full = "FULL";
    public const string Office = "OFFICE";
    public const string None = "NONE";
    public static readonly string[] All = { Full, Office, None };
    public const string InvalidMessage = "La mensajería debe ser FULL (completa), OFFICE (solo con la oficina) o NONE (sin mensajería).";
    public static string? Normalize(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToUpperInvariant();
        return All.Contains(m) ? m : null;
    }
}

/// <summary>Las entidades a las que un mensaje puede hacer referencia (la tarjeta del hilo). Nuevas se agregan aquí y en MessagingService.ResolveRefsAsync.</summary>
public static class MessageRefs
{
    public const string Order = "ORDER";
    public const string Trip = "TRIP";
    public static readonly string[] All = { Order, Trip };
    public const string InvalidMessage = "La referencia debe ser una ORDER (orden) o una TRIP (ruta).";
    public const string NotFoundMessage = "La orden o ruta a la que se refiere el mensaje no existe en esta compañía.";
    public static string? Normalize(string? entity)
    {
        var e = (entity ?? string.Empty).Trim().ToUpperInvariant();
        return All.Contains(e) ? e : null;
    }
}

/// <summary>
/// Lote 212 — «quién responde por esto»: usuarios responsables de una entidad (hoy una ruta o una zona de despacho; mañana un cliente, una orden…) con el patrón polimórfico de
/// StoredFile (EntityLookupId + EntityId). Varios por entidad. Alimenta las sugerencias de «Hablar sobre esta orden». Quitar a alguien = IsActive 0.
/// </summary>
public class EntityResponsible : ITenantScoped
{
    public int EntityResponsibleId { get; set; }
    public int TenantId { get; set; }
    public int EntityLookupId { get; set; }
    public int EntityId { get; set; }
    public int UserId { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class Message : ITenantScoped
{
    public long MessageId { get; set; }
    public int TenantId { get; set; }
    public long MessageThreadId { get; set; }
    public int SenderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Lote 212: la orden o ruta de la que habla el mensaje (la tarjeta del hilo); NULL = mensaje normal. RefEntity ∈ MessageRefs.All.</summary>
    public string? RefEntity { get; set; }
    public Guid? RefPublicId { get; set; }
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
