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

public sealed record AsnQuery(Guid? WarehousePublicId = null, string[]? Status = null, Guid? ClientPublicId = null, string? Search = null);

public sealed record ReceiptLineRequest(Guid? ProductPublicId, decimal? ReceivedQty, LotInput? Lot = null, IReadOnlyList<string>? SerialNumbers = null,
    int? StagingBinId = null);

/// <summary>
/// Alta del recibo. Confirm (Lote 8A, cola del aparato) = crear, capturar las líneas de la solicitud y confirmar en UNA sola
/// transacción: devuelve el recibo ya confirmado (RECEIVED o PUTAWAY) o nada si algo falla (mismas validaciones y mensajes
/// que el flujo por pasos). Sin Confirm (o false) el recibo nace OPEN como siempre.
/// Lines: en un recibo ciego o de devolución son las líneas del recibo. Contra aviso (asnId) u orden de compra se aplican
/// sobre las líneas del documento por producto (y lote): lo escaneado manda, lo no mencionado queda en 0 y un producto que
/// no está en el documento entra como línea extra. Sin Lines, contra aviso u orden de compra se recibe lo esperado.
/// </summary>
public sealed record ReceiptCreateRequest(Guid? WarehousePublicId = null, string? Type = null, int? AsnId = null, Guid? PurchaseOrderPublicId = null,
    int? DockId = null, int? StagingBinId = null, IReadOnlyList<ReceiptLineRequest>? Lines = null, bool Confirm = false);

public sealed record ReceiptLineUpdateRequest(decimal? ReceivedQty = null, LotInput? Lot = null, bool? ClearLot = null,
    IReadOnlyList<string>? SerialNumbers = null, int? StagingBinId = null);

public sealed record ReceiptConfirmRequest(string? Comment = null, string? RowVersion = null);

public sealed record ReceiptQuery(Guid? WarehousePublicId = null, string[]? Status = null, string[]? Types = null, DateOnly? From = null,
    DateOnly? To = null, Guid[]? ProductPublicIds = null, bool? HasVariance = null, string? Search = null, int Skip = 0, int Take = 100);

public sealed record ReceiptListItemDto(int Id, Guid PublicId, string Number, string TypeCode, string Type, string Origin, string? OriginRef,
    string? SenderName, Guid WarehousePublicId, string WarehouseCode, string? DockCode, string StatusCode, string Status, int LineCount,
    decimal ExpectedQty, decimal ReceivedQty, decimal VarianceQty, bool HasVariance, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc);

public sealed record ReceiptLineDto(int Id, int? AsnLineId, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode,
    decimal? ExpectedQty, decimal ReceivedQty, decimal VarianceQty, int? LotId, string? LotNumber, DateOnly? ExpiryDate,
    IReadOnlyList<string> SerialNumbers, int? StagingBinId, string? StagingBinCode, long? AdjustmentTxnId, decimal? UnitCost,
    decimal AllocatedToCrossDock);

public sealed record ReceiptDetailDto(ReceiptListItemDto Header, IReadOnlyList<ReceiptLineDto> Lines, IReadOnlyList<WarehouseTaskDto> PutawayTasks,
    string RowVersion);

public sealed record ReceiptPageDto(int Total, int Skip, int Take, IReadOnlyList<ReceiptListItemDto> Items);
