namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Recolección y empaque ad hoc. Firma posicional FIJA. La recolección se identifica por PublicId.

public sealed record PickBatchLineRequest(Guid? ProductPublicId, decimal? Quantity, int? BinId = null, int? LotId = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>
/// Recolección. Pack (Lote 8A, cola del aparato) = recolectar y empacar en UNA sola transacción; solo se acepta en
/// POST /api/v1/pick-batches/collect-and-pack (respuesta PickBatchPackResultDto: recolección PACKED + orden creada); en
/// POST /api/v1/pick-batches → 400. Su RowVersion se ignora (la recolección nace en la misma llamada). Mismas validaciones
/// y permisos que el flujo por pasos (empacar exige orders.create).
/// </summary>
public sealed record PickBatchCreateRequest(Guid? WarehousePublicId = null, IReadOnlyList<PickBatchLineRequest>? Lines = null,
    PickBatchPackRequest? Pack = null);

public sealed record PickBatchQuery(DateOnly? From = null, DateOnly? To = null, Guid[]? ProductPublicIds = null, string[]? Status = null,
    string? OrderNumber = null, string? InvoiceNumber = null, string? Search = null, bool IncludeDeleted = false, int Skip = 0, int Take = 100);

public sealed record PickBatchLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, int BinId, string BinCode,
    int? LotId, string? LotNumber, string? SerialNumber, decimal? UnitCost, long IssueTxnId, long? ReversalTxnId);

public sealed record PickBatchDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode,
    string Status, DateTime CollectedAtUtc, string? CollectedBy, DateTime? PackedAtUtc, Guid? OrderPublicId, string? OrderNumber,
    string? PackBatchNumber, string? ClientInvoiceNumber, string? OrderStatusCode, string? OrderStatus, string? ClientName,
    string? DisplayNumbers, bool CanPack, bool CanDelete, decimal TotalQty, decimal? TotalCost, IReadOnlyList<PickBatchLineDto> Lines,
    bool IsActive, string RowVersion);

public sealed record PickBatchPageDto(int Total, int Skip, int Take, IReadOnlyList<PickBatchDto> Items);

public sealed record PickBatchPackRequest(OrderCreateRequest? Order, string? RowVersion = null);

public sealed record PickBatchPackResultDto(PickBatchDto Batch, OrderDetailDto Order);

public sealed record PickBatchDeleteRequest(string? Comment = null, string? RowVersion = null);
