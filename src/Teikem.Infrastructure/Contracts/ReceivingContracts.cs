namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Recepción (ASN, recibos). Firma posicional FIJA. El recibo se identifica por PublicId; el ASN y las líneas por id
// int, SIEMPRE resueltos dentro de su encabezado filtrado.

public sealed record AsnLineRequest(Guid? ProductPublicId, decimal? ExpectedQty, string? LotNumber = null);

public sealed record AsnCreateRequest(Guid? WarehousePublicId, Guid? ClientPublicId, string? Reference = null, DateOnly? ExpectedDate = null,
    IReadOnlyList<AsnLineRequest>? Lines = null);

public sealed record AsnLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal ExpectedQty, string? LotNumber,
    int? PurchaseOrderLineId);

public sealed record AsnDto(int Id, Guid WarehousePublicId, string WarehouseCode, Guid? ClientPublicId, string? ClientName,
    Guid? PurchaseOrderPublicId, string? PurchaseOrderNumber, string? Reference, DateOnly? ExpectedDate, string StatusCode, string Status,
    bool IsActive, IReadOnlyList<AsnLineDto> Lines, Guid? ReceiptPublicId, string? ReceiptNumber);

/// <summary>Lote 13: Reference (contiene) y ExpectedFrom/ExpectedTo sobre la llegada esperada del aviso (inclusive).</summary>
public sealed record AsnQuery(Guid? WarehousePublicId = null, string[]? Status = null, Guid? ClientPublicId = null, string? Search = null,
    string? Reference = null, DateOnly? ExpectedFrom = null, DateOnly? ExpectedTo = null);

/// <summary>Lote 13: ExpectedQty solo en recibos ciegos o de devolución (en uno con documento viene del aviso/OC → 400).</summary>
public sealed record ReceiptLineRequest(Guid? ProductPublicId, decimal? ReceivedQty, LotInput? Lot = null, IReadOnlyList<string>? SerialNumbers = null,
    int? StagingBinId = null, decimal? ExpectedQty = null);

/// <summary>
/// Alta del recibo. Confirm (Lote 8A, cola del aparato) = crear, capturar las líneas de la solicitud y confirmar en UNA sola
/// transacción: devuelve el recibo ya confirmado (RECEIVED o PUTAWAY) o nada si algo falla (mismas validaciones y mensajes
/// que el flujo por pasos). Sin Confirm (o false) el recibo nace abierto (Lote 13): contra aviso u orden de compra en
/// RECEIVING (o DISCREPANCY si lo capturado difiere); ciego o de devolución sin líneas en EXPECTED (solo el encabezado).
/// Lines: en un recibo ciego o de devolución son las líneas del recibo (obligatorias solo con Confirm). Contra aviso (asnId)
/// u orden de compra se aplican sobre las líneas del documento por producto (y lote): lo escaneado manda, lo no mencionado
/// queda en 0 y un producto que no está en el documento entra como línea extra. Sin Lines, contra aviso u orden de compra se
/// recibe lo esperado. StagingBinId queda como posición de recepción por defecto del encabezado. Carrier y Reference: texto
/// libre de hasta 80 (vacío = sin dato).
/// </summary>
public sealed record ReceiptCreateRequest(Guid? WarehousePublicId = null, string? Type = null, int? AsnId = null, Guid? PurchaseOrderPublicId = null,
    int? DockId = null, int? StagingBinId = null, IReadOnlyList<ReceiptLineRequest>? Lines = null, bool Confirm = false,
    string? Carrier = null, string? Reference = null);

/// <summary>
/// Lote 13: ProductPublicId cambia el producto (solo en líneas sin línea del aviso; limpia lote y series si no vienen);
/// ExpectedQty / ClearExpected fijan o quitan lo esperado (solo en recibos ciegos o de devolución).
/// </summary>
public sealed record ReceiptLineUpdateRequest(decimal? ReceivedQty = null, LotInput? Lot = null, bool? ClearLot = null,
    IReadOnlyList<string>? SerialNumbers = null, int? StagingBinId = null, Guid? ProductPublicId = null, decimal? ExpectedQty = null,
    bool? ClearExpected = null);

/// <summary>
/// Lote 13 — PATCH del encabezado de un recibo abierto: null = no cambiar. Type (BLIND ↔ RETURN) solo sin aviso ni orden de
/// compra; WarehousePublicId solo sin documento y sin líneas (limpia posición y muelle); DockId/ClearDock y
/// StagingBinId/ClearStagingBin del almacén del recibo; Carrier y Reference ('' = borrar, máximo 80). RowVersion = el de la
/// ficha (409 si cambió).
/// </summary>
public sealed record ReceiptHeaderUpdateRequest(string? Type = null, Guid? WarehousePublicId = null, int? DockId = null, bool? ClearDock = null,
    int? StagingBinId = null, bool? ClearStagingBin = null, string? Carrier = null, string? Reference = null, string? RowVersion = null);

public sealed record ReceiptConfirmRequest(string? Comment = null, string? RowVersion = null);

/// <summary>Lote 13: Variance (SHORT, OVER, NONE; varias con O) y Phase (OPEN, PENDING_PUTAWAY, DONE).</summary>
public sealed record ReceiptQuery(Guid? WarehousePublicId = null, string[]? Status = null, string[]? Types = null, DateOnly? From = null,
    DateOnly? To = null, Guid[]? ProductPublicIds = null, bool? HasVariance = null, string? Search = null, int Skip = 0, int Take = 100,
    string[]? Variance = null, string? Phase = null);

/// <summary>
/// Lote 13: Carrier, Reference, ExpectedDate (la del aviso; si no, la de la orden de compra), posición de recepción por
/// defecto, DockId, IsOpen (EXPECTED, RECEIVING o DISCREPANCY) y PendingPutawayCount (tareas de acomodo abiertas).
/// </summary>
public sealed record ReceiptListItemDto(int Id, Guid PublicId, string Number, string TypeCode, string Type, string Origin, string? OriginRef,
    string? SenderName, Guid WarehousePublicId, string WarehouseCode, string? DockCode, string StatusCode, string Status, int LineCount,
    decimal ExpectedQty, decimal ReceivedQty, decimal VarianceQty, bool HasVariance, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc,
    string? Carrier, string? Reference, DateOnly? ExpectedDate, int? DefaultStagingBinId, string? DefaultStagingBinCode, int? DockId,
    bool IsOpen, int PendingPutawayCount);

public sealed record ReceiptLineDto(int Id, int? AsnLineId, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode,
    decimal? ExpectedQty, decimal ReceivedQty, decimal VarianceQty, int? LotId, string? LotNumber, DateOnly? ExpiryDate,
    IReadOnlyList<string> SerialNumbers, int? StagingBinId, string? StagingBinCode, long? AdjustmentTxnId, decimal? UnitCost,
    decimal AllocatedToCrossDock);

/// <summary>Lote 13: CanDelete = abierto y sin asignaciones de cruce de muelle.</summary>
public sealed record ReceiptDetailDto(ReceiptListItemDto Header, IReadOnlyList<ReceiptLineDto> Lines, IReadOnlyList<WarehouseTaskDto> PutawayTasks,
    string RowVersion, bool CanDelete);

public sealed record ReceiptPageDto(int Total, int Skip, int Take, IReadOnlyList<ReceiptListItemDto> Items);
