using Teikem.Domain.Integrations;

namespace Teikem.Infrastructure.Contracts;

// Lote 132 (P11) — integraciones: configuración, corridas, archivos y bandeja. Permisos integrations.* (ver PermissionCatalog).

/// <summary>Un usuario al que se avisa de lo que pasa en la integración.</summary>
public sealed record IntegrationNotifyUserDto(int Id, string Name, string? Email);

public sealed record IntegrationDto(
    int Id,
    Guid PublicId,
    string Name,
    string? Description,
    string Direction,
    Guid? ClientPublicId,
    string? ClientName,
    string ConnectorKind,
    /// <summary>Nombre de la carpeta de entrada (conector CARPETA).</summary>
    string? Folder,
    Guid? TemplatePublicId,
    string? TemplateName,
    string ScheduleKind,
    int? IntervalSeconds,
    DateTime? NextRunUtc,
    IReadOnlyList<string> Scopes,
    /// <summary>Nombre de la cuenta de servicio con la que crea lo que entra.</summary>
    string? ServiceUserName,
    IReadOnlyList<IntegrationNotifyUserDto> NotifyUsers,
    int RetentionDays,
    bool IsActive,
    DateTime? LastStartedUtc,
    DateTime? LastFinishedUtc,
    string LastStatus,
    string? LastMessage,
    int PendingInbox,
    DateTime CreatedAtUtc,
    /// <summary>Configuración del conector de correo (sin la contraseña, que nunca sale del servidor).</summary>
    EmailConfig? Email = null,
    bool HasMailPassword = false,
    /// <summary>Configuración del conector SFTP/FTP (sin la contraseña ni la llave, que nunca salen del servidor).</summary>
    RemoteConfig? Remote = null,
    bool HasRemoteSecret = false,
    /// <summary>Formato del archivo (Excel, JSON o XML con mapeo); null = CSV.</summary>
    FileFormatConfig? Format = null,
    /// <summary>Lote 137b: qué manda una integración de SALIDA (estatus, disparador, formato, columnas y, en HTTP, la API); null en las de entrada.</summary>
    OutputConfig? Output = null,
    bool HasHttpSecret = false,
    /// <summary>2026-10-21: en una salida de un catálogo, la entidad que manda (PRODUCT); null = envíos del cliente.</summary>
    string? EntityKind = null);

/// <summary>
/// Alta. Scopes: códigos de IntegrationScopes (solo los que quien crea puede dar). Folder: carpeta de entrada (conector CARPETA). IncludeMe: agrega a quien crea a los avisos.
/// La cuenta de servicio y su rol se crean con la integración.
/// </summary>
public sealed record IntegrationCreateRequest(
    string Name,
    string? Description,
    Guid? ClientPublicId,
    string ConnectorKind,
    string? Folder,
    Guid? TemplatePublicId,
    string? ScheduleKind,
    int? IntervalSeconds,
    IList<string> Scopes,
    IList<int>? NotifyUserIds = null,
    bool IncludeMe = false,
    int? RetentionDays = null,
    EmailConfig? Email = null,
    /// <summary>Contraseña del buzón (conector EMAIL): se guarda cifrada y no se vuelve a mostrar.</summary>
    string? MailPassword = null,
    RemoteConfig? Remote = null,
    /// <summary>Contraseña o llave privada del servidor (SFTP/FTP): se guarda cifrada y no se vuelve a mostrar.</summary>
    RemoteSecret? Secret = null,
    FileFormatConfig? Format = null,
    /// <summary>INBOUND (por omisión) u OUTBOUND (Lote 137b: la integración MANDA órdenes del cliente).</summary>
    string? Direction = null,
    OutputConfig? Output = null,
    /// <summary>Token, contraseña o valor del encabezado de la API saliente: se guarda cifrado y no se vuelve a mostrar.</summary>
    HttpOutSecret? HttpSecret = null,
    /// <summary>2026-10-21: solo con Direction = OUTBOUND: la entidad de catálogo que manda (PRODUCT) con la plantilla TemplatePublicId; sin ella, la integración manda envíos del cliente.</summary>
    string? EntityKind = null);

/// <summary>Edición parcial: null = sin cambio. NotifyUserIds reemplaza la lista; Scopes reemplaza los alcances (se revalidan contra quien edita).</summary>
public sealed record IntegrationPatchRequest(
    string? Name = null,
    string? Description = null,
    Guid? TemplatePublicId = null,
    string? Folder = null,
    string? ScheduleKind = null,
    int? IntervalSeconds = null,
    IList<string>? Scopes = null,
    IList<int>? NotifyUserIds = null,
    bool? IncludeMe = null,
    int? RetentionDays = null,
    EmailConfig? Email = null,
    string? MailPassword = null,
    RemoteConfig? Remote = null,
    RemoteSecret? Secret = null,
    FileFormatConfig? Format = null,
    OutputConfig? Output = null,
    HttpOutSecret? HttpSecret = null);

/// <summary>Un alcance que se puede ofrecer en el alta: de qué familia es, si quien mira lo puede dar y, si no, por qué.</summary>
public sealed record IntegrationScopeOptionDto(string Code, string Family, string Label, string? ModuleKey, bool Available, string? UnavailableReason, bool IsApi = false, bool NeedsClient = false);

/// <summary>Opciones del formulario de alta: alcances, conectores disponibles y usuarios a quienes se puede avisar.</summary>
public sealed record IntegrationOptionsDto(
    IReadOnlyList<IntegrationScopeOptionDto> Scopes,
    IReadOnlyList<string> Connectors,
    IReadOnlyList<IntegrationNotifyUserDto> Users,
    /// <summary>Lote 137b: los campos de orden que se pueden poner en una salida, los estatus que la pueden disparar y los conectores de salida.</summary>
    IReadOnlyList<OutputFieldDto>? OutputFields = null,
    IReadOnlyList<string>? OrderStatuses = null,
    IReadOnlyList<string>? OutboundConnectors = null);

public sealed record IntegrationFileDto(long Id, string FileName, string Result, long SizeBytes, int OrdersCreated, int Rejected, string? Message, DateTime ProcessedUtc);

public sealed record IntegrationRunDto(
    long Id,
    Guid IntegrationPublicId,
    string IntegrationName,
    DateTime StartedUtc,
    DateTime? FinishedUtc,
    string Status,
    string TriggeredBy,
    string? TriggeredByName,
    int FilesRead,
    int OrdersCreated,
    int Rejected,
    string? Message,
    IReadOnlyList<IntegrationFileDto> Files);

/// <summary>Una fila cruda del archivo (número de fila del archivo y sus celdas tal como llegaron).</summary>
public sealed record InboxRowDto(int Row, IReadOnlyList<string> Cells);

public sealed record InboxItemDto(
    long Id,
    Guid IntegrationPublicId,
    string IntegrationName,
    long RunId,
    string? FileName,
    int? SourceRow,
    string? ReferenceKey,
    string Reason,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc,
    string? ResolvedByName,
    string? Note,
    Guid? OrderPublicId,
    /// <summary>¿Todavía se guardan los datos crudos? (se purgan pasada la retención).</summary>
    bool HasData,
    IReadOnlyList<string>? Header,
    IReadOnlyList<InboxRowDto>? Rows);

public sealed record InboxPageDto(IReadOnlyList<InboxItemDto> Items, int Total, int Pending);

/// <summary>Corrige las celdas de las filas de un ítem de la bandeja (las mismas filas del archivo; el número de fila no cambia).</summary>
public sealed record InboxFixRequest(IList<InboxRowDto> Rows);

public sealed record InboxDiscardRequest(string? Note);

/// <summary>Resultado de reprocesar: lo que se creó, lo que sigue en la bandeja y su motivo.</summary>
public sealed record InboxReprocessResultDto(int Reprocessed, int StillPending, IReadOnlyList<InboxItemDto> Items);

/// <summary>Resultado de probar la conexión con el buzón (sin datos sensibles).</summary>
public sealed record EmailTestResultDto(bool Ok, string Message, int Unseen);

/// <summary>Resultado de probar la conexión de un conector (correo, SFTP o FTP). Fingerprint = la huella SHA-256 que presenta un servidor SFTP (para fijarla).</summary>
public sealed record ConnectionTestDto(bool Ok, string Message, int Count, string? Fingerprint);

// ---------------------------------------------------------------- Lote 135: llaves de API

public sealed record ApiKeyDto(int Id, string KeyId, string Label, string SecretLast4, int RateLimitPerMinute, bool IsActive, DateTime? ExpiresAtUtc, DateTime? LastUsedUtc, DateTime CreatedAtUtc, DateTime? RevokedAtUtc);

/// <summary>La llave recién creada: el SECRETO viene aquí y en ningún otro lado (no se guarda, solo su hash).</summary>
public sealed record ApiKeyCreatedDto(ApiKeyDto Key, string Secret);

public sealed record ApiKeyCreateRequest(string Label, int? RateLimitPerMinute = null, DateTime? ExpiresAtUtc = null);

public sealed record ApiKeyPatchRequest(string? Label = null, int? RateLimitPerMinute = null);

public sealed record ApiLogDto(DateTime AtUtc, string Method, string Path, int StatusCode, int DurationMs);

/// <summary>Autenticación de la API: la llave (tk_…) y su secreto.</summary>
public sealed record ApiTokenRequest(string ClientId, string ClientSecret);

public sealed record ApiTokenDto(string AccessToken, string TokenType, int ExpiresIn, IReadOnlyList<string> Scopes, string? ClientPublicId);

/// <summary>Lo que una llave puede saber de sí misma.</summary>
public sealed record ApiInfoDto(string Integration, string KeyId, string Label, IReadOnlyList<string> Scopes, Guid? ClientPublicId, string? ClientName, int RateLimitPerMinute, DateTime? ExpiresAtUtc);

// ---------------------------------------------------------------- Lote 136: webhooks de salida

public sealed record WebhookDto(
    Guid PublicId, Guid IntegrationPublicId, string IntegrationName, string Url, IReadOnlyList<string> Events, string SecretLast4, bool IsActive, int ConsecutiveFailures,
    DateTime? SuspendedAtUtc, DateTime? LastDeliveryUtc, string? LastStatus, DateTime CreatedAtUtc);

/// <summary>Un webhook recién creado (o con el secreto rotado): el SECRETO de firma viene aquí y en ninguna otra respuesta.</summary>
public sealed record WebhookCreatedDto(WebhookDto Webhook, string Secret);

public sealed record WebhookCreateRequest(string Url, IReadOnlyList<string> Events);

public sealed record WebhookPatchRequest(string? Url = null, IReadOnlyList<string>? Events = null, bool? IsActive = null);

public sealed record WebhookTestResultDto(bool Ok, int? ResponseCode, string Message);

public sealed record WebhookDeliveryDto(
    long Id, Guid WebhookPublicId, Guid IntegrationPublicId, string IntegrationName, string Url, string EventType, string Status, int AttemptCount, DateTime NextAttemptUtc,
    DateTime? LastAttemptUtc, int? ResponseCode, string? Error, DateTime CreatedAtUtc, DateTime? DeliveredAtUtc, string? PayloadJson);

public sealed record WebhookDeliveryPageDto(IReadOnlyList<WebhookDeliveryDto> Items, int Total, int Undelivered);

// ---------------------------------------------------------------- Lote 137: formato del archivo

/// <summary>Vista previa de un archivo con el formato configurado: las hojas (Excel), el encabezado y las primeras filas tal como las leería la plantilla.</summary>
public sealed record FormatPreviewDto(string Kind, IReadOnlyList<string> Sheets, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows, int TotalRows);

// ---------------------------------------------------------------- Lote 137b: integraciones de salida

public sealed record OutputFieldDto(string Code, string Label, string Kind);

/// <summary>Un envío de una integración de salida: el archivo (o cuerpo) ya armado, su destino y cómo va (intentos, motivo, código HTTP). Preview = los primeros 4.000 caracteres del contenido.</summary>
public sealed record OutputItemDto(
    long Id, Guid IntegrationPublicId, string IntegrationName, string ConnectorKind, string FileName, string Format, int OrderCount, int SizeBytes, string Status, int AttemptCount, DateTime NextAttemptUtc,
    DateTime? LastAttemptUtc, int? ResponseCode, string? Error, DateTime CreatedAtUtc, DateTime? SentAtUtc, string Preview);

public sealed record OutputPageDto(IReadOnlyList<OutputItemDto> Items, int Total, int Undelivered);

/// <summary>Contenido de ejemplo de una salida con órdenes recientes del cliente; Sample = true si eran de los estatus pedidos (si no, las últimas del cliente).</summary>
public sealed record OutputPreviewDto(string FileName, string Format, string Content, int OrderCount, bool Sample);
