namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 51 — avisos (reglas, registro y bandeja). Reglas y registro: permiso notifications.manage. Bandeja: cada usuario la suya.
// ======================================================================================================================

/// <summary>Una variable que un evento ofrece a las plantillas ({nombre}); Sample = valor de ejemplo.</summary>
public sealed record NotificationVariableDto(string Name, string Label, string LabelEn, string Sample);

/// <summary>
/// Un evento del catálogo (definido en código). IsOptional = el usuario lo puede silenciar en su bandeja. Los textos por omisión
/// rellenan la regla nueva.
/// </summary>
public sealed record NotificationEventDto(
    string Code,
    string? ModuleKey,
    string Name,
    string NameEn,
    bool IsOptional,
    IReadOnlyList<NotificationVariableDto> Variables,
    string DefaultSubject,
    string DefaultSubjectEn,
    string DefaultBody,
    string DefaultBodyEn);

/// <summary>Los canales y si están configurados en Teikem (los no configurados no se pueden encender en una regla).</summary>
public sealed record NotificationChannelStatusDto(string Channel, bool IsConfigured);

/// <summary>Un canal de una regla: interruptor y plantilla (asunto y texto) en español e inglés, con {variables}.</summary>
public sealed record NotificationRuleChannelDto(
    string Channel,
    bool IsEnabled,
    string? SubjectEs,
    string? SubjectEn,
    string? BodyEs,
    string? BodyEn,
    bool IsConfigured);

/// <summary>Un destinatario: Kind ROLE (RoleId) | USER (UserId) | EMAIL | PHONE (Value). Label = nombre del rol, del usuario o el valor.</summary>
public sealed record NotificationRecipientDto(string Kind, int? RoleId, int? UserId, string? Value, string Label);

public sealed record NotificationRuleDto(
    Guid PublicId,
    string EventCode,
    string Name,
    bool IsActive,
    IReadOnlyList<NotificationRuleChannelDto> Channels,
    IReadOnlyList<NotificationRecipientDto> Recipients,
    string RowVersion);

public sealed record NotificationChannelRequest(
    string? Channel = null,
    bool? IsEnabled = null,
    string? SubjectEs = null,
    string? SubjectEn = null,
    string? BodyEs = null,
    string? BodyEn = null);

public sealed record NotificationRecipientRequest(string? Kind = null, int? RoleId = null, int? UserId = null, string? Value = null);

/// <summary>
/// Alta (POST) o edición (PATCH) de una regla. Alta: EventCode y Name obligatorios; los canales que no se manden nacen apagados
/// con los textos por omisión del evento. Edición: null = sin cambio; Channels cambia solo los canales que vengan; Recipients
/// REEMPLAZA la lista completa. RowVersion opcional (409 si la regla cambió).
/// </summary>
public sealed record NotificationRuleRequest(
    string? EventCode = null,
    string? Name = null,
    bool? IsActive = null,
    IReadOnlyList<NotificationChannelRequest>? Channels = null,
    IReadOnlyList<NotificationRecipientRequest>? Recipients = null,
    string? RowVersion = null);

/// <summary>«Enviar prueba»: manda el texto de ese canal de la regla (con valores de ejemplo) a quien la pide.</summary>
public sealed record NotificationTestRequest(string? Channel = null);

/// <summary>Resultado de la prueba: SENT | SKIPPED | FAILED y, si no salió, por qué.</summary>
public sealed record NotificationTestResultDto(string Channel, string Status, string? Message);

/// <summary>Dispara el evento de prueba (TEST) por la cola real: lo reciben las reglas activas de ese evento.</summary>
public sealed record NotificationTestEventRequest(string? Message = null);

public sealed record NotificationQueuedDto(bool Queued);

/// <summary>Una línea del registro de avisos: Status PENDING | SENT | FAILED | SKIPPED; Error = por qué falló o se omitió.</summary>
public sealed record NotificationLogDto(
    long Id,
    long OutboxId,
    Guid? RulePublicId,
    string RuleName,
    string EventCode,
    string Channel,
    string Recipient,
    string Status,
    string? Error,
    int Attempts,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc);

/// <summary>Un aviso de la bandeja del usuario (la campanita).</summary>
public sealed record UserNotificationDto(long Id, string EventCode, string Title, string? Body, string? LinkUrl, bool IsRead, DateTime CreatedAtUtc);

public sealed record UnreadCountDto(int Count);

public sealed record MarkedReadDto(int Marked);

/// <summary>Un evento opcional que el usuario puede silenciar y si lo tiene silenciado.</summary>
public sealed record NotificationPrefDto(string EventCode, string Name, string NameEn, bool IsMuted);

public sealed record NotificationPrefRequest(bool? Muted = null);
