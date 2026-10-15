using Teikem.Domain.Messaging;

namespace Teikem.Infrastructure.Messaging;

/// <summary>
/// Lote 53 — reglas PURAS de la mensajería: mensajes exactos, hilo único por pareja, nombre de un grupo sin título, vista previa y presencia.
/// </summary>
public static class MessagingRules
{
    public const int MaxBodyLength = 2000;
    public const int MaxTitleLength = 100;
    public const int PreviewLength = 200;
    public const int PageSize = 50;
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(90);

    public const string BodyRequiredMessage = "Escriba el mensaje.";
    public const string BodyTooLongMessage = "El mensaje no puede pasar de 2000 caracteres.";
    public const string TitleTooLongMessage = "El nombre del grupo no puede pasar de 100 caracteres.";
    public const string NeedRecipientsMessage = "Escoja al menos una persona o un rol.";
    public const string CannotWriteSelfMessage = "No puede escribirse a sí mismo.";
    public const string UserNotFoundMessage = "Usuario no encontrado en esta compañía.";
    public const string RoleNotFoundMessage = "Rol no encontrado.";
    public const string RoleEmptyMessage = "Ese rol no tiene personas activas a quienes escribir.";
    public const string OnlyCreatorAddsMessage = "Solo quien creó el grupo puede agregar personas.";
    public const string DirectNoAddMessage = "A una conversación entre dos personas no se pueden agregar más; cree un grupo.";
    public const string DirectNoLeaveMessage = "Una conversación entre dos personas no se abandona.";
    public const string NeedMembersMessage = "Indique a quién agregar.";

    /// <summary>Clave del hilo entre dos personas: «menorId-mayorId» (la misma sin importar quién escribe primero).</summary>
    public static string DirectKey(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";

    /// <summary>Texto sin espacios de más; null si queda vacío.</summary>
    public static string? NormalizeBody(string? text)
    {
        var t = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>Una línea corta para la lista: saltos de línea y espacios repetidos se vuelven un espacio; se corta en 200.</summary>
    public static string Preview(string body)
    {
        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= PreviewLength ? flat : flat[..(PreviewLength - 1)] + "…";
    }

    /// <summary>
    /// Nombre de un grupo: el título si lo tiene; si no, los nombres de las OTRAS personas: hasta 2 y «+N» con el resto («Ana, Luis, +2»).
    /// </summary>
    public static string GroupName(string? title, IReadOnlyList<string> otherNames)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        if (otherNames.Count == 0) return "—";
        var shown = otherNames.Take(2).ToList();
        return otherNames.Count > 2 ? $"{string.Join(", ", shown)}, +{otherNames.Count - 2}" : string.Join(", ", shown);
    }

    public static bool IsOnline(DateTime? lastSeenUtc, DateTime nowUtc) => lastSeenUtc is DateTime seen && nowUtc - seen < OnlineWindow;

    /// <summary>Quien escribe un mensaje lo tiene leído; los de otros cuentan como no leídos si son más nuevos que lo último leído y que su entrada.</summary>
    public static bool IsUnread(long messageId, int senderId, int me, long lastRead, long joinedAfter) => senderId != me && messageId > lastRead && messageId > joinedAfter;

    public static string KindOf(int recipientCount, bool usedRole) => recipientCount == 1 && !usedRole ? MessageThreadKinds.Direct : MessageThreadKinds.Group;
}

/// <summary>Nombres de los eventos que se empujan por la conexión en vivo.</summary>
public static class MessageLiveEvents
{
    /// <summary>Llegó un mensaje nuevo a un hilo (a todos menos a quien lo escribió).</summary>
    public const string MessageReceived = "MessageReceived";
    /// <summary>Cambió un hilo (personas que entran o salen, o un mensaje propio desde otra ventana).</summary>
    public const string ThreadChanged = "ThreadChanged";
    /// <summary>La persona leyó un hilo (para sus otras ventanas).</summary>
    public const string ThreadRead = "ThreadRead";
}
