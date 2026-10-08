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

/// <summary>
/// Lote 13: ExpectedQty solo en recibos ciegos o de devolución (en uno con documento viene del aviso/OC → 400).
/// Lote 16: posición destino por id (TargetBinId) o por código escaneado (TargetBinCode), no ambos (400); del almacén del
/// recibo (id ajeno → 404; código inexistente → 400), de guardado (STAGING o CROSSDOCK → 400) y activa (422).
/// </summary>
public sealed record ReceiptLineRequest(Guid? ProductPublicId, decimal? ReceivedQty, LotInput? Lot = null, IReadOnlyList<string>? SerialNumbers = null,
    int? StagingBinId = null, decimal? ExpectedQty = null, int? TargetBinId = null, string? TargetBinCode = null,
    decimal? DamagedQty = null, string? DamageCause = null, string? DamageNote = null, int? DamageBinId = null, string? DamageBinCode = null,
    bool? DamageDiscard = null);

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
/// Lote 16: ReceivingMode (PUTAWAY | DIRECT; desconocido → 400) = modo del recibo; sin él, el del almacén. Con Confirm = true,
/// sin modo y sin ninguna posición destino en las líneas, un almacén directo recibe "Con acomodo" (D9: app anterior).
/// </summary>
public sealed record ReceiptCreateRequest(Guid? WarehousePublicId = null, string? Type = null, int? AsnId = null, Guid? PurchaseOrderPublicId = null,
    int? DockId = null, int? StagingBinId = null, IReadOnlyList<ReceiptLineRequest>? Lines = null, bool Confirm = false,
    string? Carrier = null, string? Reference = null, string? ReceivingMode = null);

/// <summary>
/// Lote 13: ProductPublicId cambia el producto (solo en líneas sin línea del aviso; limpia lote y series si no vienen);
/// ExpectedQty / ClearExpected fijan o quitan lo esperado (solo en recibos ciegos o de devolución).
/// Lote 16: TargetBinId fija la posición destino (mismas reglas que al agregar la línea); ClearTargetBin = true la quita.
/// </summary>
public sealed record ReceiptLineUpdateRequest(decimal? ReceivedQty = null, LotInput? Lot = null, bool? ClearLot = null,
    IReadOnlyList<string>? SerialNumbers = null, int? StagingBinId = null, Guid? ProductPublicId = null, decimal? ExpectedQty = null,
    bool? ClearExpected = null, int? TargetBinId = null, bool? ClearTargetBin = null,
    decimal? DamagedQty = null, string? DamageCause = null, string? DamageNote = null, int? DamageBinId = null, bool? DamageDiscard = null,
    bool? ClearDamage = null);

/// <summary>
/// Lote 13 — PATCH del encabezado de un recibo abierto: null = no cambiar. Type (BLIND ↔ RETURN) solo sin aviso ni orden de
/// compra; WarehousePublicId solo sin documento y sin líneas (limpia posición y muelle); DockId/ClearDock y
/// StagingBinId/ClearStagingBin del almacén del recibo; Carrier y Reference ('' = borrar, máximo 80). RowVersion = el de la
/// ficha (409 si cambió). Lote 16: ReceivingMode (PUTAWAY | DIRECT) cambia el modo SOLO de este recibo abierto; pasar a
/// "Con acomodo" exige una posición de recepción (422 si el almacén no tiene zona STAGING y no se indica).
/// </summary>
public sealed record ReceiptHeaderUpdateRequest(string? Type = null, Guid? WarehousePublicId = null, int? DockId = null, bool? ClearDock = null,
    int? StagingBinId = null, bool? ClearStagingBin = null, string? Carrier = null, string? Reference = null, string? RowVersion = null,
    string? ReceivingMode = null);

public sealed record ReceiptConfirmRequest(string? Comment = null, string? RowVersion = null);

/// <summary>
/// Lote 13: Variance (SHORT, OVER, NONE; varias con O) y Phase (OPEN, PENDING_PUTAWAY, DONE). IncludeLines (exportación de
/// la lista con sus líneas): cada recibo de la página trae sus líneas en ReceiptListItemDto.Lines (todas las del recibo,
/// leídas en lote para toda la página, sin N+1); por defecto false (Lines = null).
/// </summary>
public sealed record ReceiptQuery(Guid? WarehousePublicId = null, string[]? Status = null, string[]? Types = null, DateOnly? From = null,
    DateOnly? To = null, Guid[]? ProductPublicIds = null, bool? HasVariance = null, string? Search = null, int Skip = 0, int Take = 100,
    string[]? Variance = null, string? Phase = null, bool IncludeLines = false);

/// <summary>
/// Lote 13: Carrier, Reference, ExpectedDate (la del aviso; si no, la de la orden de compra), posición de recepción por
/// defecto, DockId, IsOpen (EXPECTED, RECEIVING o DISCREPANCY) y PendingPutawayCount (tareas de acomodo abiertas).
/// Lines: solo en la lista con includeLines = true (exportación con líneas); null en los demás casos (en la ficha las
/// líneas van en ReceiptDetailDto.Lines).
/// Lote 16: ReceivingModeCode (PUTAWAY | DIRECT) y su etiqueta = el modo con que se abrió el recibo.
/// </summary>
public sealed record ReceiptListItemDto(int Id, Guid PublicId, string Number, string TypeCode, string Type, string Origin, string? OriginRef,
    string? SenderName, Guid WarehousePublicId, string WarehouseCode, string? DockCode, string StatusCode, string Status, int LineCount,
    decimal ExpectedQty, decimal ReceivedQty, decimal VarianceQty, bool HasVariance, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc,
    string? Carrier, string? Reference, DateOnly? ExpectedDate, int? DefaultStagingBinId, string? DefaultStagingBinCode, int? DockId,
    bool IsOpen, int PendingPutawayCount, IReadOnlyList<ReceiptLineDto>? Lines = null, string ReceivingModeCode = "PUTAWAY",
    string? ReceivingMode = null);

/// <summary>
/// Lote 16: posición destino de la línea (id, código y tipo de zona). TargetFreeQty = espacio libre del destino en unidades
/// (cupo − existencia − lo recibido en otras líneas de este recibo hacia la misma posición; nunca negativo): solo con el
/// recibo abierto y un destino con cupo; null en los demás casos. Si ReceivedQty &gt; TargetFreeQty, excede el cupo (solo aviso).
/// </summary>
public sealed record ReceiptLineDto(int Id, int? AsnLineId, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode,
    decimal? ExpectedQty, decimal ReceivedQty, decimal VarianceQty, int? LotId, string? LotNumber, DateOnly? ExpiryDate,
    IReadOnlyList<string> SerialNumbers, int? StagingBinId, string? StagingBinCode, long? AdjustmentTxnId, decimal? UnitCost,
    decimal AllocatedToCrossDock, int? TargetBinId = null, string? TargetBinCode = null, string? TargetZoneTypeCode = null,
    decimal? TargetFreeQty = null, decimal DamagedQty = 0m, string? DamageCauseCode = null, string? DamageNote = null, int? DamageBinId = null,
    string? DamageBinCode = null, bool DamageDiscard = false, int? DamageReportId = null, string? DamageReportCode = null);

/// <summary>
/// Lote 16 — posición destino sugerida para una línea (GET /receipts/{id}/lines/{lineId}/target-suggestions): razón (la del
/// acomodo dirigido; en devoluciones la cuarentena primero), cupo, existencia actual de la posición, lo ya destinado a ella
/// por otras líneas del recibo, espacio libre (null sin cupo) y Fits (la cantidad de la línea cabe). Las que caben van primero.
/// </summary>
public sealed record ReceiptTargetSuggestionDto(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason,
    int? MaxCapacityQty, decimal QtyOnHand, decimal ClaimedQty, decimal? FreeQty, bool Fits);

/// <summary>Lote 16 — "Usar posiciones sugeridas" (POST /receipts/{id}/targets/suggest): RowVersion = el de la ficha (409 si cambió).</summary>
public sealed record ReceiptApplySuggestionsRequest(string? RowVersion = null);

/// <summary>
/// Lote 16 — resultado de "Usar posiciones sugeridas": el recibo actualizado, cuántas líneas recibieron posición y cuántas
/// quedaron sin sugerencia (ninguna posición de guardado con espacio). Nada se llena solo (D3): solo al pedirlo.
/// </summary>
public sealed record ReceiptApplySuggestionsResultDto(ReceiptDetailDto Receipt, int Assigned, int WithoutSuggestion);

/// <summary>Lote 13: CanDelete = abierto y sin asignaciones de cruce de muelle.</summary>
public sealed record ReceiptDetailDto(ReceiptListItemDto Header, IReadOnlyList<ReceiptLineDto> Lines, IReadOnlyList<WarehouseTaskDto> PutawayTasks,
    string RowVersion, bool CanDelete);

public sealed record ReceiptPageDto(int Total, int Skip, int Take, IReadOnlyList<ReceiptListItemDto> Items);
