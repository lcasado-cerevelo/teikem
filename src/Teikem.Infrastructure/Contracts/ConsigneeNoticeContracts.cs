namespace Teikem.Infrastructure.Contracts;

// Lote 116 — avisos al consignatario (el cliente los prende y marca desde el portal).

/// <summary>Cómo está el aviso al consignatario del cliente de la sesión: prendido o no, el idioma de los avisos y si el servidor puede mandar correos. CanEdit = su rol de portal puede cambiarlo (administrador u operador).</summary>
public sealed record ConsigneeNoticeSettingsDto(bool Enabled, string Lang, bool EmailConfigured, bool CanEdit);

/// <summary>Enabled: prender o apagar; Lang: es o en (el idioma de los avisos). Lo que venga nulo no cambia.</summary>
public sealed record ConsigneeNoticeSettingsRequest(bool? Enabled, string? Lang);

/// <summary>Un consignatario del cliente con sus medios de contacto y lo que aceptó.</summary>
public sealed record ConsigneeNoticeConsigneeDto(Guid PublicId, string Name, string? City, string? Email, string? Phone, bool AcceptsEmail, bool AcceptsSms);

/// <summary>Marcar (o desmarcar) que un consignatario acepta avisos por un medio. Para marcar hay que aceptar la advertencia (WarningAccepted).</summary>
public sealed record ConsigneeNoticeMarkRequest(string? Channel, bool? Accepted, bool? WarningAccepted);

// Lote 213 — marcar varios consignatarios de una vez (una sola aceptación de la advertencia para todo el grupo).

/// <summary>Marcar (o desmarcar) varios consignatarios por el mismo medio. Hasta 200 por vez; los repetidos cuentan una vez. Para marcar hay que aceptar la advertencia (WarningAccepted) una vez para todos.</summary>
public sealed record ConsigneeNoticeBulkMarkRequest(IList<Guid>? PublicIds, string? Channel, bool? Accepted, bool? WarningAccepted);

/// <summary>Cómo le fue a un consignatario del grupo: Ok con su estado nuevo (Consignee) o el mensaje de por qué no se pudo (sin el medio, dado de baja, no encontrado).</summary>
public sealed record ConsigneeNoticeBulkItemDto(Guid PublicId, string Name, bool Ok, string? Message, ConsigneeNoticeConsigneeDto? Consignee);

/// <summary>Resultado del marcado de varios: cuántos quedaron y cuántos no, y el detalle en el orden recibido.</summary>
public sealed record ConsigneeNoticeBulkResultDto(int Done, int Failed, IReadOnlyList<ConsigneeNoticeBulkItemDto> Items);
