using Teikem.Domain.Common;

namespace Teikem.Domain.Notifications;

/// <summary>Constantes de los avisos (Lote 51). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class NotificationCodes
{
    /// <summary>EntityType de la bitácora para las reglas de aviso.</summary>
    public const string RuleEntityType = "NOTIFICATION_RULE";
    public const string TestEvent = "TEST";
}

public static class NotificationChannels
{
    public const string Email = "EMAIL";
    public const string Sms = "SMS";
    public const string App = "APP";
    public const string Push = "PUSH";
    public static readonly string[] All = { Email, Sms, App, Push };
}

public static class NotificationRecipientKinds
{
    public const string Role = "ROLE";
    public const string User = "USER";
    public const string Email = "EMAIL";
    public const string Phone = "PHONE";
    public static readonly string[] All = { Role, User, Email, Phone };
}

public static class NotificationOutboxStatuses
{
    public const string Pending = "PENDING";
    public const string Done = "DONE";
    public const string Failed = "FAILED";
}

public static class NotificationLogStatuses
{
    public const string Pending = "PENDING";
    public const string Sent = "SENT";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";
}

/// <summary>
/// Lote 51 — regla de aviso de una compañía: «cuando pase el evento X, avisar a estos destinatarios por estos canales».
/// El evento vive en código (catálogo de eventos); aquí solo se guarda su código. Una regla activa necesita al menos un
/// destinatario y un canal encendido. Nunca DELETE: se apaga (IsActive = 0).
/// </summary>
[AuditEntity(NotificationCodes.RuleEntityType)]
public class NotificationRule : ITenantScoped, IAuditStamped
{
    public int NotificationRuleId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<NotificationRuleChannel> Channels { get; set; } = new();
    public List<NotificationRuleRecipient> Recipients { get; set; } = new();
}

/// <summary>Un canal de una regla con su plantilla: asunto y texto en JSON multilingüe {"es":..,"en":..}; llevan {variables} del evento.</summary>
public class NotificationRuleChannel : ITenantScoped
{
    public int NotificationRuleChannelId { get; set; }
    public int TenantId { get; set; }
    public int NotificationRuleId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? SubjectJson { get; set; }
    public string? BodyJson { get; set; }
}

/// <summary>Destinatario de una regla: un rol, un usuario, un correo suelto o un teléfono suelto (guardado, sin uso hasta que haya SMS).</summary>
public class NotificationRuleRecipient : ITenantScoped
{
    public int NotificationRuleRecipientId { get; set; }
    public int TenantId { get; set; }
    public int NotificationRuleId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int? RoleId { get; set; }
    public int? UserId { get; set; }
    public string? Value { get; set; }
}

/// <summary>
/// Cola de avisos (outbox): se escribe en la MISMA transacción que la operación de negocio; un aviso que falla nunca deshace la
/// operación. El procesador la toma con un UPDATE atómico (LockedBy/LockedUntilUtc) y reintenta a 1, 5 y 30 minutos (3 intentos).
/// </summary>
public class NotificationOutbox : ITenantScoped
{
    public long NotificationOutboxId { get; set; }
    public int TenantId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    /// <summary>Variables del evento en JSON {"nombre":"valor"}.</summary>
    public string? PayloadJson { get; set; }
    /// <summary>Clave para no avisar dos veces lo mismo (única por compañía y evento cuando viene).</summary>
    public string? DedupKey { get; set; }
    public string Status { get; set; } = NotificationOutboxStatuses.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public string? LockedBy { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? ProcessedUtc { get; set; }
}

/// <summary>Registro de cada envío (evento × regla × canal × destinatario). Idempotente: índice único (cola, regla, canal, destinatario).</summary>
public class NotificationLog : ITenantScoped
{
    public long NotificationLogId { get; set; }
    public int TenantId { get; set; }
    public long NotificationOutboxId { get; set; }
    public int NotificationRuleId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = NotificationLogStatuses.Pending;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}

/// <summary>Bandeja de avisos de un usuario (canal APP): lo que ve en la campanita.</summary>
public class UserNotification : ITenantScoped
{
    public long UserNotificationId { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public long? NotificationOutboxId { get; set; }
    public int? NotificationRuleId { get; set; }
    /// <summary>Lote 54: si es un aviso manual enviado por una persona, cuál (de ahí sale «De Ana»).</summary>
    public long? AnnouncementId { get; set; }
}

/// <summary>Preferencia de un usuario: silenciar un evento opcional en su bandeja.</summary>
public class UserNotificationPref : ITenantScoped
{
    public int UserNotificationPrefId { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
}
