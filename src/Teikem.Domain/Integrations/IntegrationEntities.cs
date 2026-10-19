using Teikem.Domain.Common;
using Teikem.Domain.Identity;
using Teikem.Domain.Orders;
using Teikem.Domain.Security;

namespace Teikem.Domain.Integrations;

/// <summary>Cómo habla la integración con el otro lado (Lote 132: CARPETA y SUBIDA MANUAL; CORREO y SFTP llegan en los lotes siguientes).</summary>
public static class IntegrationConnectorKinds
{
    public const string Folder = "FOLDER";
    public const string Manual = "MANUAL";
    public const string Email = "EMAIL";
    public const string Sftp = "SFTP";
    public const string Ftp = "FTP";
    /// <summary>Lote 135: la integración ES una API: el cliente llama a Teikem con una llave (ver ApiAccessRules).</summary>
    public const string Api = "API";
    /// <summary>Los que ya corren (el resto se define pero aún no se puede elegir).</summary>
    public static readonly IReadOnlyList<string> Available = [Folder, Manual, Email, Sftp, Ftp, Api];
    public static readonly IReadOnlyList<string> All = [Folder, Manual, Email, Sftp, Ftp, Api];
    public static bool IsRemote(string? kind) => kind is Sftp or Ftp;
}

public static class IntegrationDirections
{
    public const string Inbound = "INBOUND";
    public const string Outbound = "OUTBOUND";
}

/// <summary>Cuándo corre: solo a mano o cada cierto tiempo.</summary>
public static class IntegrationScheduleKinds
{
    public const string Manual = "MANUAL";
    public const string Interval = "INTERVAL";
    public const int MinIntervalSeconds = 60;
    public const int MaxIntervalSeconds = 604800;
}

public static class IntegrationRunStatuses
{
    public const string Running = "RUNNING";
    public const string Ok = "OK";
    /// <summary>Corrió, pero algo fue a la bandeja.</summary>
    public const string Partial = "PARTIAL";
    public const string Failed = "FAILED";
    public const string Never = "NEVER";
}

public static class IntegrationTriggers
{
    public const string Manual = "MANUAL";
    public const string Schedule = "SCHEDULE";
    public const string Upload = "UPLOAD";
    /// <summary>Se reprocesó un ítem de la bandeja.</summary>
    public const string Reprocess = "REPROCESS";
}

public static class IntegrationFileResults
{
    public const string Processed = "PROCESSED";
    /// <summary>Mismo contenido (hash) que uno ya procesado: no se vuelve a procesar.</summary>
    public const string Duplicate = "DUPLICATE";
    public const string Failed = "FAILED";
    /// <summary>Se volvió a pasar una orden de la bandeja (no cuenta para el «ya procesado» del archivo).</summary>
    public const string Reprocessed = "REPROCESSED";
}

public static class InboxStatuses
{
    public const string Pending = "PENDING";
    public const string Reprocessed = "REPROCESSED";
    public const string Discarded = "DISCARDED";
}

/// <summary>
/// Lote 132 (P11) — una integración: de dónde (o a dónde) van los datos, con qué plantilla y cuándo. Tiene su propia CUENTA DE SERVICIO (usuario de clase SERVICE con un rol
/// «Integración: nombre» que solo trae los permisos de los alcances elegidos): lo que crea la integración queda atribuido a ella, nunca al usuario que la configuró.
/// Nunca se borra: se apaga (IsActive = 0).
/// </summary>
[AuditEntity(Constants.EntityTypes.Integration)]
public class Integration : ITenantScoped, IAuditStamped
{
    public int IntegrationId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>IntegrationDirections: ENTRADA a Teikem (SALIDA llega en un lote posterior).</summary>
    public string Direction { get; set; } = IntegrationDirections.Inbound;
    /// <summary>Cliente de las órdenes que entran por esta integración (obligatorio en las de entrada).</summary>
    public int? ClientId { get; set; }
    public string ConnectorKind { get; set; } = IntegrationConnectorKinds.Manual;
    /// <summary>Configuración NO secreta del conector (CARPETA: {"folder":"nombre"}). Los secretos van aparte, cifrados.</summary>
    public string? ConnectorConfigJson { get; set; }
    [SensitiveData] public byte[]? SecretEnc { get; set; }
    /// <summary>Plantilla de importación con la que se leen los archivos.</summary>
    public int? ImportTemplateId { get; set; }
    public string ScheduleKind { get; set; } = IntegrationScheduleKinds.Manual;
    public int? IntervalSeconds { get; set; }
    [NotAudited] public DateTime? NextRunUtc { get; set; }
    /// <summary>Códigos de alcance (IntegrationScopes) que se le dieron a la cuenta de servicio.</summary>
    public string ScopesJson { get; set; } = "[]";
    /// <summary>La cuenta de servicio de la integración y su rol.</summary>
    public int? ServiceUserId { get; set; }
    public int? ServiceRoleId { get; set; }
    /// <summary>Usuarios a quienes se avisa (JSON [ids]); el aviso sale por una regla administrada por la integración (NotifyRuleId).</summary>
    public string NotifyUserIdsJson { get; set; } = "[]";
    public int? NotifyRuleId { get; set; }
    /// <summary>Días que se guardan los datos crudos de la bandeja (luego se purga el detalle; el motivo queda).</summary>
    public int RetentionDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime? LastStartedUtc { get; set; }
    [NotAudited] public DateTime? LastFinishedUtc { get; set; }
    [NotAudited] public string LastStatus { get; set; } = IntegrationRunStatuses.Never;
    [NotAudited] public string? LastMessage { get; set; }
    [NotAudited] public string? LockedBy { get; set; }
    [NotAudited] public DateTime? LockedUntilUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public ImportTemplate? ImportTemplate { get; set; }
    public ApplicationUser? ServiceUser { get; set; }
    public Role? ServiceRole { get; set; }
}

/// <summary>Una corrida de una integración (bitácora): cuántos archivos leyó, cuántas órdenes creó y cuántas mandó a la bandeja.</summary>
public class IntegrationRun : ITenantScoped
{
    public long IntegrationRunId { get; set; }
    public int TenantId { get; set; }
    public int IntegrationId { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedUtc { get; set; }
    public string Status { get; set; } = IntegrationRunStatuses.Running;
    public string TriggeredBy { get; set; } = IntegrationTriggers.Manual;
    public int? TriggeredByUserId { get; set; }
    public int FilesRead { get; set; }
    public int OrdersCreated { get; set; }
    public int Rejected { get; set; }
    public string? Message { get; set; }
}

/// <summary>Un archivo procesado: su hash (SHA-256) evita procesar dos veces el mismo contenido.</summary>
public class IntegrationFileLog : ITenantScoped
{
    public long IntegrationFileLogId { get; set; }
    public int TenantId { get; set; }
    public int IntegrationId { get; set; }
    public long IntegrationRunId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Result { get; set; } = IntegrationFileResults.Processed;
    public string? Message { get; set; }
    public int OrdersCreated { get; set; }
    public int Rejected { get; set; }
    public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// La bandeja: lo que entró y no se pudo procesar solo. DataJson guarda los datos tal como llegaron ({header, rows}) para corregirlos y reprocesarlos; pasados los días de
/// retención se purga el detalle (DataJson = NULL) y queda el motivo.
/// </summary>
[AuditEntity(Constants.EntityTypes.IntegrationInbox)]
public class IntegrationInboxItem : ITenantScoped
{
    public long IntegrationInboxItemId { get; set; }
    public int TenantId { get; set; }
    public int IntegrationId { get; set; }
    public long IntegrationRunId { get; set; }
    public long? IntegrationFileLogId { get; set; }
    public int? SourceRow { get; set; }
    /// <summary>Número de la orden (o clave del correo) a la que se refiere.</summary>
    public string? ReferenceKey { get; set; }
    public string Reason { get; set; } = string.Empty;
    [NotAudited] public string? DataJson { get; set; }
    public string Status { get; set; } = InboxStatuses.Pending;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedBy { get; set; }
    public string? Note { get; set; }
    public Guid? OrderPublicId { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Lote 135 (P11) — una llave de API de una integración de tipo API. KeyId es público (tk_…); del secreto solo se guarda su hash SHA-256 y los últimos 4 caracteres (el secreto se muestra una sola vez al crearla).
/// RateLimitPerMinute = solicitudes por minuto. Nunca se borra: se revoca.
/// </summary>
[AuditEntity(Constants.EntityTypes.Integration)]
public class IntegrationApiKey : ITenantScoped
{
    public int IntegrationApiKeyId { get; set; }
    public int TenantId { get; set; }
    public int IntegrationId { get; set; }
    public string KeyId { get; set; } = string.Empty;
    [SensitiveData] public string SecretHash { get; set; } = string.Empty;
    public string SecretLast4 { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int RateLimitPerMinute { get; set; } = ApiKeyRules.DefaultRateLimit;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAtUtc { get; set; }
    [NotAudited] public DateTime? LastUsedUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public int? RevokedBy { get; set; }
}

/// <summary>Bitácora de la API por llave: una fila por solicitud (método, ruta, estatus, milisegundos). Se purga a los 30 días.</summary>
public class IntegrationApiLog : ITenantScoped
{
    public long IntegrationApiLogId { get; set; }
    public int TenantId { get; set; }
    public int IntegrationApiKeyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
}
