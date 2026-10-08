namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Cola de tareas de almacén (D41). Firma posicional FIJA. CompletableFromQueue = existe un handler para el tipo y
// su NotFromQueueMessage es null.

public sealed record WarehouseTaskDto(int Id, string TypeCode, string Type, string StatusCode, string Status, int Priority, Guid WarehousePublicId,
    string WarehouseCode, Guid? ProductPublicId, string? Sku, string? ProductName, int? LotId, string? LotNumber, string? SerialNumber,
    decimal? Quantity, int? FromBinId, string? FromBinCode, int? ToBinId, string? ToBinCode, string? RefEntityCode, int? RefId,
    string? RefLabel, int? AssignedToUserId, string? AssignedToName, bool CompletableFromQueue, DateTime CreatedAtUtc, DateTime? CompletedAtUtc,
    IReadOnlyList<WarehouseTaskMoveDto>? Moves = null);

/// <summary>
/// Un movimiento del ledger hecho por la tarea (2026-10-07): un acomodo repartido en varias posiciones deja una línea por posición (la tarea guarda una sola
/// posición destino; el detalle real vive aquí). Vacío mientras la tarea no mueva nada.
/// </summary>
public sealed record WarehouseTaskMoveDto(long TransactionId, string? FromBinCode, string? ToBinCode, decimal Quantity, string? LotNumber, DateTime AtUtc);

public sealed record WarehouseTaskQuery(Guid? WarehousePublicId = null, string[]? Types = null, string[]? Status = null, bool AssignedToMe = false,
    int? AssignedUserId = null, bool IncludeClosed = false, int Skip = 0, int Take = 100);

public sealed record WarehouseTaskPageDto(int Total, int Skip, int Take, IReadOnlyList<WarehouseTaskDto> Items);

public sealed record TaskAssignRequest(int? UserId);

public sealed record TaskCompleteRequest(int? ToBinId = null, decimal? Quantity = null, IReadOnlyList<string>? SerialNumbers = null, string? Comment = null);

/// <summary>Reparto de una tarea de acomodo: la misma cantidad en cada posición, en el orden dado (ids de posición del almacén de la tarea).</summary>
public sealed record TaskDistributeRequest(decimal QuantityPerBin, IReadOnlyList<int>? ToBinIds = null, string? Comment = null);

public sealed record TaskCancelRequest(string? Comment = null);

public sealed record ReplenishmentRunRequest(Guid? WarehousePublicId = null);

public sealed record ReplenishmentResultDto(int ProductsEvaluated, int TasksCreated, int SkippedWithOpenTask, int SkippedNoReserve,
    IReadOnlyList<WarehouseTaskDto> Tasks);

/// <summary>
/// Posición sugerida para guardar. Lote 16: el acomodo dirigido respeta el cupo en unidades (salta las posiciones donde
/// no cabe); MaxCapacityQty = cupo de la posición y FreeQty = espacio libre (cupo − existencia; null sin cupo).
/// </summary>
public sealed record PutawaySuggestionDto(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason,
    string RotationClass, int? MaxCapacityQty = null, decimal? FreeQty = null);
