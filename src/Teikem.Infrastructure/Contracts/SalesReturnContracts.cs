namespace Teikem.Infrastructure.Contracts;

// Lote 81 (P6) — devoluciones de venta.

/// <summary>Una línea de la orden de venta que se devuelve y cuánto (hasta lo despachado menos lo ya devuelto o pedido).</summary>
public sealed record SalesReturnLineRequest(int? SalesOrderLineId = null, decimal? Quantity = null);

/// <summary>
/// POST /sales-returns: la devolución solicitada. Reason: DAMAGED, WRONG_ITEM, NOT_NEEDED, EXPIRED u OTHER. Fuera de la ventana de devolución (SalesReturnWindowDays) solo la pide quien también puede
/// autorizar, y debe confirmarlo con ConfirmOutsideWindow.
/// </summary>
public sealed record SalesReturnRequest(Guid? SalesOrderPublicId = null, string? Reason = null, string? Note = null, IReadOnlyList<SalesReturnLineRequest>? Lines = null, bool? ConfirmOutsideWindow = null);

public sealed record SalesReturnAuthorizeRequest(string? Note = null);

public sealed record SalesReturnRejectRequest(string? Reason = null);

/// <summary>Lo que llegó de una línea: GoodQty (bueno, vuelve al inventario) y DamagedQty (dañado: reporte de daño, cuarentena o desecho con DamageDiscard). Lot si el producto lleva lote; TargetBinId = posición destino de lo bueno.</summary>
public sealed record SalesReturnReceiveLineRequest(int? LineId = null, decimal? GoodQty = null, decimal? DamagedQty = null, string? DamageNote = null, bool? DamageDiscard = null, int? TargetBinId = null, LotInput? Lot = null);

/// <summary>POST /sales-returns/{id}/receive: lo que llegó (puede ser en partes). CloseRest = lo que no llegó se da por no devuelto (deja de contar como pendiente).</summary>
public sealed record SalesReturnReceiveRequest(IReadOnlyList<SalesReturnReceiveLineRequest>? Lines = null, bool? CloseRest = null);

public sealed record SalesReturnSettingsDto(int WindowDays);

public sealed record SalesReturnSettingsRequest(int? WindowDays = null);

public sealed record SalesReturnLineDto(int Id, int SalesOrderLineId, int LineNo, Guid ProductPublicId, string Sku, string ProductName, decimal QtyRequested, decimal QtyReceived, decimal QtyDamaged,
    decimal QtyCredited, decimal Pending);

public sealed record SalesReturnReceiptDto(Guid PublicId, string Number);

public sealed record SalesReturnCreditDto(Guid PublicId, string Number, string Status, decimal Total);

public sealed record SalesReturnDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid OrderPublicId, string OrderNumber, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseName, string Reason, string ReasonLabel, string? Note, bool OutsideWindow, DateTime RequestedAtUtc, DateTime? AuthorizedAtUtc, string? AuthorizationNote,
    DateTime? ReceivedAtUtc, IReadOnlyList<SalesReturnLineDto> Lines, IReadOnlyList<SalesReturnReceiptDto> Receipts, IReadOnlyList<SalesReturnCreditDto> CreditNotes);

public sealed record SalesReturnListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid OrderPublicId, string OrderNumber, Guid ClientPublicId,
    string ClientName, string Reason, string ReasonLabel, decimal QtyRequested, decimal QtyReceived, bool OutsideWindow, DateTime RequestedAtUtc);

/// <summary>Una línea despachada de una orden y cuánto se puede devolver todavía (IsKit y IsSerial = no se devuelve en esta versión).</summary>
public sealed record ReturnableLineDto(int LineId, int LineNo, Guid ProductPublicId, string Sku, string ProductName, decimal Shipped, decimal Returned, decimal Pending, decimal Returnable, bool IsKit, bool IsSerial);

public sealed record ReturnableDto(Guid OrderPublicId, string OrderNumber, DateOnly? LastDispatchDate, int WindowDays, bool OutsideWindow, IReadOnlyList<ReturnableLineDto> Lines);
