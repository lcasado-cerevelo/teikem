namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 54 — avisos manuales. Permiso notifications.send. Un aviso no se responde: llega a la campanita de cada persona («De Ana»).
// ======================================================================================================================

/// <summary>
/// Un aviso a enviar. A quién: UserIds (personas), RoleIds (todas las personas activas con cada rol hoy) o All = true (todas las personas
/// activas de la compañía; entonces UserIds y RoleIds se ignoran). Quien envía nunca se avisa a sí mismo. Text es opcional.
/// </summary>
public sealed record AnnouncementRequest(IReadOnlyList<int>? UserIds = null, IReadOnlyList<int>? RoleIds = null, bool? All = null, string? Title = null, string? Text = null);

/// <summary>Un aviso enviado (historial). AudienceText = a quién se envió, en una línea; ReadCount = cuántas personas ya lo leyeron.</summary>
public sealed record AnnouncementDto(
    Guid PublicId,
    string Title,
    string? Body,
    string SentByName,
    DateTime SentAtUtc,
    bool ToAll,
    string AudienceText,
    int RecipientCount,
    int ReadCount);

/// <summary>Una persona a la que se le puede enviar un aviso.</summary>
public sealed record AnnouncementPersonDto(int Id, string Name, string Email);
