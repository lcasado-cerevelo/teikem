using Teikem.Domain.Common;

namespace Teikem.Domain.Messaging;

/// <summary>Quién escribió un mensaje entre un consignatario y su cliente.</summary>
public static class ConsigneeMessageSenders
{
    public const string Consignee = "CONSIGNEE";
    public const string Client = "CLIENT";
}

/// <summary>
/// Lote 117b — la conversación de UN consignatario con acceso propio con SU cliente (dictado por Luis, 2026-10-18: «el consignatario le puede enviar mensaje a su cliente»). La abre el consignatario con su primer mensaje; del lado
/// del cliente la ven y la contestan sus administradores y operadores (los de solo consulta la leen). Aparte de los mensajes con el equipo de la compañía (Lote 104): esos son del cliente con la compañía; estos, del consignatario con el
/// cliente. Los mensajes nunca se editan ni se borran.
/// </summary>
public class ConsigneeConversation : ITenantScoped
{
    public long ConsigneeConversationId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    /// <summary>El consignatario (localización de entrega del cliente) con quien se conversa.</summary>
    public int LocationId { get; set; }
    /// <summary>La persona del consignatario (ApplicationUser de portal) dueña de la conversación.</summary>
    public int ConsigneeUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public long LastMessageId { get; set; }
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public string? LastMessagePreview { get; set; }
    public string? LastSenderKind { get; set; }
    /// <summary>Hasta qué mensaje leyó el CLIENTE (cualquier administrador u operador que la abre la marca leída para todos).</summary>
    public long ClientLastReadMessageId { get; set; }
    /// <summary>Hasta qué mensaje leyó el consignatario.</summary>
    public long ConsigneeLastReadMessageId { get; set; }
}

public class ConsigneeMessage : ITenantScoped
{
    public long ConsigneeMessageId { get; set; }
    public int TenantId { get; set; }
    public long ConsigneeConversationId { get; set; }
    public string SenderKind { get; set; } = ConsigneeMessageSenders.Consignee;
    public int SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
