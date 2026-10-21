namespace Teikem.Infrastructure.Contracts;

// Lote 182 (C4) — recepción maestra: varios documentos del mismo dueño y almacén que llegan juntos; cada uno conserva su recibo.

/// <summary>Un documento de la recepción maestra: un aviso de llegada (AsnId) o una orden de compra (PurchaseOrderPublicId), uno solo.</summary>
public sealed record ReceiptGroupDocumentRequest(int? AsnId = null, Guid? PurchaseOrderPublicId = null);

/// <summary>
/// Abre un recibo por cada documento (de 2 a 20, del mismo dueño y almacén) bajo una recepción maestra. DockId, StagingBinId, Carrier, Reference y ReceivingMode se aplican a todos los recibos (el mismo camión);
/// Note es de la maestra (≤ 500).
/// </summary>
public sealed record ReceiptGroupCreateRequest(IReadOnlyList<ReceiptGroupDocumentRequest>? Documents = null, int? DockId = null, int? StagingBinId = null, string? Carrier = null,
    string? Reference = null, string? ReceivingMode = null, string? Note = null);

public sealed record ReceiptGroupConfirmRequest(string? Comment = null);

public sealed record ReceiptGroupQuery(string? Search = null, string? Status = null, int Skip = 0, int Take = 50);

/// <summary>Un recibo de la maestra con su documento de origen y su avance.</summary>
public sealed record ReceiptGroupMemberDto(Guid ReceiptPublicId, string ReceiptNumber, string StatusCode, string Status, string OriginKind, string? OriginRef, int LineCount, decimal ExpectedQty,
    decimal ReceivedQty, bool IsConfirmed);

/// <summary>StatusCode: OPEN (algún recibo sin confirmar), CONFIRMED (todos confirmados) o CANCELLED (eliminada).</summary>
public sealed record ReceiptGroupDto(Guid PublicId, string Number, string StatusCode, Guid WarehousePublicId, string WarehouseCode, Guid? OwnerClientPublicId, string? OwnerName, string? Note,
    DateTime CreatedAtUtc, string? CreatedBy, int MemberCount, int ConfirmedCount, decimal ExpectedQty, decimal ReceivedQty, bool CanConfirm, bool CanDelete, bool IsActive, string RowVersion,
    IReadOnlyList<ReceiptGroupMemberDto> Members);

public sealed record ReceiptGroupPageDto(int Total, int Skip, int Take, IReadOnlyList<ReceiptGroupDto> Items);
