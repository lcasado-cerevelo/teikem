using Teikem.Domain.Common;

namespace Teikem.Domain.Notifications;

public static class AnnouncementCodes
{
    /// <summary>EventCode de los avisos manuales en la bandeja (UserNotification.EventCode). No es un evento del catálogo: no se silencia.</summary>
    public const string EventCode = "ANNOUNCEMENT";
}

/// <summary>
/// Lote 54 — un aviso MANUAL que alguien con el permiso notifications.send envía a personas, a roles o a todos. No se responde. Cada
/// persona lo recibe como un aviso de su campanita (UserNotification con AnnouncementId); esta fila es el historial de lo enviado:
/// AudienceText = a quién (resumen legible), RecipientCount = cuántas personas lo recibieron. Nunca se edita ni se borra.
/// </summary>
public class Announcement : ITenantScoped
{
    public long AnnouncementId { get; set; }
    public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public int SentBy { get; set; }
    public DateTime SentAtUtc { get; set; }
    public bool ToAll { get; set; }
    public string AudienceText { get; set; } = string.Empty;
    public int RecipientCount { get; set; }
}
