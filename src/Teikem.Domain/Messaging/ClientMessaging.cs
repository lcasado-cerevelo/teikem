using Teikem.Domain.Common;

namespace Teikem.Domain.Messaging;

/// <summary>Quién escribió un mensaje del portal: una persona del cliente o alguien del equipo de la compañía.</summary>
public static class ClientMessageSenders
{
    public const string Client = "CLIENT";
    public const string Staff = "STAFF";
}

/// <summary>
/// Lote 104 (portal de clientes) — la conversación de UNA persona de un cliente con el equipo de servicio al cliente de la compañía (decidido con Luis, 2026-10-17: una por persona; nadie de su empresa ve lo que otro
/// escribió). El equipo ve todas las de sus clientes y cualquiera del equipo puede contestar; el equipo también puede abrirla escribiéndole primero a la persona. Se crea con el primer mensaje. Los mensajes
/// nunca se editan ni se borran. Aparte de la mensajería interna del staff (Lote 53): esa es entre usuarios de la compañía; esta cruza el portal.
/// </summary>
public class ClientConversation : ITenantScoped
{
    public long ClientConversationId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    /// <summary>La persona del cliente (ApplicationUser de portal) dueña de la conversación.</summary>
    public int PortalUserId { get; set; }
    /// <summary>Quién la abrió: CLIENT (escribió primero) o STAFF (el equipo le escribió primero).</summary>
    public string StartedBy { get; set; } = ClientMessageSenders.Client;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public long LastMessageId { get; set; }
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public string? LastMessagePreview { get; set; }
    public string? LastSenderKind { get; set; }
    /// <summary>Hasta qué mensaje leyó el EQUIPO (cualquiera del equipo que la abre la marca leída para todos).</summary>
    public long TeamLastReadMessageId { get; set; }
    /// <summary>Hasta qué mensaje leyó la persona del cliente.</summary>
    public long ClientLastReadMessageId { get; set; }
}

public class ClientMessage : ITenantScoped
{
    public long ClientMessageId { get; set; }
    public int TenantId { get; set; }
    public long ClientConversationId { get; set; }
    public string SenderKind { get; set; } = ClientMessageSenders.Client;
    public int SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Una persona del equipo de servicio al cliente de un cliente. Sin filas para un cliente = lo atiende quien tenga el permiso de atender mensajes de clientes (el rol «Servicio al cliente»).</summary>
public class ClientServiceTeamMember : ITenantScoped
{
    public int ClientServiceTeamMemberId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int UserId { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public int? AddedBy { get; set; }
}

public static class ClientMessageRules
{
    public const int MaxBodyLength = 2000;
    public const int MaxPerHour = 30;
    public const int PreviewLength = 140;

    public const string EmptyMessage = "Escriba el mensaje.";
    public const string TooLongMessage = "El mensaje no puede pasar de 2000 caracteres.";
    public const string TooManyMessage = "Demasiados mensajes seguidos; espere un rato para volver a escribir.";
    public const string ConversationNotFound = "Conversación";
    public const string PersonNotFound = "La persona no es un usuario activo de portal de ese cliente.";   // 400 al escribirle primero
    public const string TeamUserInvalid = "Solo pueden estar en el equipo usuarios activos de la compañía.";

    /// <summary>Texto del mensaje, recortado; null si queda vacío.</summary>
    public static string? Normalize(string? body)
    {
        var t = body?.Replace("\r\n", "\n").Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>Primeras palabras del mensaje (una línea) para la lista de conversaciones.</summary>
    public static string Preview(string body)
    {
        var one = string.Join(' ', body.Split(new[] { '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= PreviewLength ? one : one[..(PreviewLength - 1)] + "…";
    }
}
