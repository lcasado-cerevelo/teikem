namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 51 — avisos (reglas, registro y bandeja). Reglas y registro: permiso notifications.manage. Bandeja: cada usuario la suya.
// ======================================================================================================================

/// <summary>
/// Una variable que un evento ofrece a las plantillas ({nombre}); Sample = valor de ejemplo. IsBlock (Lote 243) = variable bloque
/// (p. ej. {orderDetail}): solo puede ir en el texto, no en el asunto.
/// </summary>
public sealed record NotificationVariableDto(string Name, string Label, string LabelEn, string Sample, bool IsBlock = false);

/// <summary>
/// Lote 243 — un destinatario que sale del propio aviso (Kind CONTEXT, Value = Key), p. ej. SELLER «Vendedor de la orden».
/// IsDefault = viene puesto en la regla nueva. Audience = STAFF | CUSTOMER (la versión para el cliente no lleva enlaces ni marcas internas).
/// </summary>
public sealed record NotificationContextRecipientDto(string Key, string Label, string LabelEn, bool IsDefault, string Audience);

/// <summary>Lote 243 — plantilla por omisión de un canal (asunto y texto en español e inglés) para la regla nueva.</summary>
public sealed record NotificationChannelDefaultDto(string Channel, string Subject, string SubjectEn, string Body, string BodyEn);

/// <summary>
/// Un evento del catálogo (definido en código). IsOptional = el usuario lo puede silenciar en su bandeja. Los textos por omisión
/// rellenan la regla nueva. Lote 243: ContextRecipients = destinatarios «del evento» (Kind CONTEXT; vacío si no tiene);
/// ChannelDefaults = plantilla por omisión por canal (los canales que no aparecen usan DefaultSubject/DefaultBody).
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
    string DefaultBodyEn,
    string Category = "OTHER",
    IReadOnlyList<NotificationContextRecipientDto>? ContextRecipients = null,
    IReadOnlyList<NotificationChannelDefaultDto>? ChannelDefaults = null);

/// <summary>
/// Los canales y si están configurados en Teikem (los no configurados no se pueden encender en una regla). IsLocal (Lote 243) =
/// el canal entrega a un buzón local de desarrollo (Mailpit): el correo nunca sale a la calle.
/// </summary>
public sealed record NotificationChannelStatusDto(string Channel, bool IsConfigured, bool IsLocal = false);

/// <summary>Un canal de una regla: interruptor y plantilla (asunto y texto) en español e inglés, con {variables}.</summary>
public sealed record NotificationRuleChannelDto(
    string Channel,
    bool IsEnabled,
    string? SubjectEs,
    string? SubjectEn,
    string? BodyEs,
    string? BodyEn,
    bool IsConfigured);

/// <summary>
/// Un destinatario: Kind ROLE (RoleId) | USER (UserId) | EMAIL | PHONE | CONTEXT (Value; CONTEXT = clave del evento, Lote 243).
/// Label = nombre del rol, del usuario, la etiqueta del destinatario del evento o el valor.
/// </summary>
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

/// <summary>
/// «Enviar prueba»: manda el texto de ese canal de la regla (con valores de ejemplo) a quien la pide. Reference (Lote 243, opcional)
/// = número o PublicId de un documento real (p. ej. OV-00012) para armar el aviso con sus datos en vez del ejemplo.
/// </summary>
public sealed record NotificationTestRequest(string? Channel = null, string? Reference = null);

/// <summary>
/// Lote 243 — vista previa de un aviso, sin enviarlo ni escribir en el registro. Subject y Body nulos = la plantilla de la regla
/// activa del evento con ese canal encendido o, si no hay, la de omisión. Lang = es | en (nulo = el de la compañía).
/// Reference = número o PublicId de un documento real (exige además el permiso y el módulo que declara el evento); nulo = ejemplo.
/// Audience = STAFF | CUSTOMER (nulo = STAFF).
/// </summary>
public sealed record NotificationPreviewRequest(
    string? EventCode = null,
    string? Channel = null,
    string? Subject = null,
    string? Body = null,
    string? Lang = null,
    string? Reference = null,
    string? Audience = null);

/// <summary>Lote 243 — un adjunto de la vista previa (sin el contenido).</summary>
public sealed record NotificationPreviewAttachmentDto(string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Lote 243 — el aviso como llegaría: asunto, texto plano y (en EMAIL) el HTML, con sus adjuntos. UsedSample = se armó con el
/// ejemplo del evento (sin documento real). TemplateSource = REQUEST (el texto mandado) | RULE (la regla activa) | DEFAULT (la de omisión).
/// </summary>
public sealed record NotificationPreviewDto(
    string Channel,
    string? Subject,
    string BodyText,
    string? BodyHtml,
    IReadOnlyList<NotificationPreviewAttachmentDto> Attachments,
    bool UsedSample,
    string TemplateSource);

/// <summary>Resultado de la prueba: SENT | SKIPPED | FAILED y, si no salió, por qué.</summary>
public sealed record NotificationTestResultDto(string Channel, string Status, string? Message);

/// <summary>Dispara el evento de prueba (TEST) por la cola real: lo reciben las reglas activas de ese evento.</summary>
public sealed record NotificationTestEventRequest(string? Message = null);

public sealed record NotificationQueuedDto(bool Queued);

/// <summary>
/// Una línea del registro de avisos: Status PENDING | SENT | FAILED | SKIPPED; Error = por qué falló o se omitió. Lote 243: un
/// destinatario del evento que no resolvió a nadie sale como Recipient «(Vendedor de la orden)» SKIPPED con el motivo en Error.
/// </summary>
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
public sealed record UserNotificationDto(long Id, string EventCode, string Title, string? Body, string? LinkUrl, bool IsRead, DateTime CreatedAtUtc, string? FromName = null);

public sealed record UnreadCountDto(int Count);

public sealed record MarkedReadDto(int Marked);

/// <summary>Un evento opcional que el usuario puede silenciar y si lo tiene silenciado.</summary>
public sealed record NotificationPrefDto(string EventCode, string Name, string NameEn, bool IsMuted);

public sealed record NotificationPrefRequest(bool? Muted = null);
