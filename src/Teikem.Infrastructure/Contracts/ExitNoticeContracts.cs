namespace Teikem.Infrastructure.Contracts;

// Lote 143 — aviso de salida (SAL-#####). Sin precios: no es una venta. Los ids públicos son Guid; el lote y la posición van por su id interno (como en ventas).

/// <summary>Una línea que se pide: producto (del dueño), cantidad y, si se quiere, un lote concreto.</summary>
public sealed record ExitNoticeLineRequest(Guid? ProductPublicId, decimal? Quantity, int? LotId = null);

/// <summary>
/// Alta del aviso de salida: reserva en el acto lo que haya y deja el resto pendiente (backorder). Almacén opcional si la compañía tiene uno solo activo.
/// Destino = texto libre (retiro, dirección o nota). Hasta 200 líneas.
/// </summary>
public sealed record ExitNoticeRequest(Guid? WarehousePublicId = null, Guid? ClientPublicId = null, string? Reference = null, string? Destination = null,
    DateOnly? RequiredDate = null, string? Notes = null, IReadOnlyList<ExitNoticeLineRequest>? Lines = null);

/// <summary>Lote 158: edita los datos del aviso de salida solicitado (null = sin cambio; texto vacío lo quita).</summary>
public sealed record ExitNoticeHeaderRequest(string? Reference = null, string? Destination = null, DateOnly? RequiredDate = null, string? Notes = null);

/// <summary>Cambia la cantidad pedida de una línea de un aviso solicitado (sube: reserva lo que haya y el resto queda pendiente; baja: libera primero el pendiente).</summary>
public sealed record ExitNoticeLineModifyRequest(decimal? Quantity);

/// <summary>Despachar contra el aviso: por línea, cuánto y, si se quiere, de qué posición (y lote). Sin posición sale de lo que el aviso tiene reservado.</summary>
public sealed record ExitNoticeDispatchLineRequest(int? LineId, decimal? Quantity, int? BinId = null, int? LotId = null);

public sealed record ExitNoticeDispatchRequest(IReadOnlyList<ExitNoticeDispatchLineRequest>? Lines = null, string? Note = null);

public sealed record ExitNoticeCommentRequest(string? Comment = null);

public sealed record ExitNoticeLineDto(int Id, int LineNo, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode, int? LotId, string? LotNumber,
    decimal QtyOrdered, decimal QtyReserved, decimal QtyBackordered, decimal QtyShipped, bool IsClosed);

/// <summary>Un despacho (recolección) hecho contra el aviso; los deshechos salen CANCELLED.</summary>
public sealed record ExitNoticeDispatchInfoDto(Guid PickBatchPublicId, string Number, string StatusCode, DateTime CollectedAtUtc, decimal Quantity);

public sealed record ExitNoticeListItemDto(Guid PublicId, string Number, string StatusCode, string Status, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseCode, string WarehouseName, string? Reference, string? Destination, DateOnly RequestedDate, DateOnly? RequiredDate,
    int LineCount, decimal QtyOrdered, decimal QtyShipped, bool HasBackorder, string? RequestedByName, DateTime CreatedAtUtc,
    int? AssignedToUserId = null, string? AssignedToName = null);

public sealed record ExitNoticeDto(Guid PublicId, string Number, string StatusCode, string Status, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseCode, string WarehouseName, string? Reference, string? Destination, DateOnly RequestedDate, DateOnly? RequiredDate,
    string? Notes, bool HasBackorder, bool CanModify, bool CanCancel, bool CanDispatch, string? RequestedByName, DateTime? RequestedAtUtc, DateTime CreatedAtUtc,
    string RowVersion, IReadOnlyList<ExitNoticeLineDto> Lines, IReadOnlyList<ExitNoticeDispatchInfoDto> Dispatches,
    int? AssignedToUserId = null, string? AssignedToName = null);

public sealed record ExitNoticeDispatchResultDto(Guid PickBatchPublicId, string PickBatchNumber, ExitNoticeDto Notice);

/// <summary>Una línea con pendiente: lo que falta, lo que hay disponible hoy para reservarle y lo que se le podría reservar ya.</summary>
public sealed record ExitNoticeBackorderLineDto(Guid NoticePublicId, string Number, string StatusCode, string ClientName, string WarehouseName, int LineId, int LineNo,
    Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReserved, decimal QtyBackordered, decimal Available, decimal Fulfillable,
    DateTime RequestedAtUtc, int DaysWaiting);

public sealed record ExitNoticeFulfillLineDto(Guid NoticePublicId, string Number, int LineId, string Sku, decimal Reserved, decimal StillPending);

public sealed record ExitNoticeFulfillResultDto(int LinesReserved, decimal UnitsReserved, IReadOnlyList<ExitNoticeFulfillLineDto> Lines);
