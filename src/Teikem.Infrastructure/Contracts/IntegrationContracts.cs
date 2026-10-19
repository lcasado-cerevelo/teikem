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
    bool HasRemoteSecret = false);

/// <summary>
/// Alta. Scopes: códigos de IntegrationScopes (solo los que quien crea puede dar). Folder: carpeta de entrada (conector CARPETA). IncludeMe: agrega a quien crea a los avisos.
/// La cuenta de servicio y su rol se crean con la integración.
/// </summary>
public sealed record IntegrationCreateRequest(
    string Name,
    string? Description,
    Guid ClientPublicId,
    string ConnectorKind,
    string? Folder,
    Guid TemplatePublicId,
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
    RemoteSecret? Secret = null);

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
    RemoteSecret? Secret = null);

/// <summary>Un alcance que se puede ofrecer en el alta: de qué familia es, si quien mira lo puede dar y, si no, por qué.</summary>
public sealed record IntegrationScopeOptionDto(string Code, string Family, string Label, string? ModuleKey, bool Available, string? UnavailableReason);

/// <summary>Opciones del formulario de alta: alcances, conectores disponibles y usuarios a quienes se puede avisar.</summary>
public sealed record IntegrationOptionsDto(
    IReadOnlyList<IntegrationScopeOptionDto> Scopes,
    IReadOnlyList<string> Connectors,
    IReadOnlyList<IntegrationNotifyUserDto> Users);

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
