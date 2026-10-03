namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Conteo cíclico en modo informado (D22). Firma posicional FIJA.

public sealed record CycleCountCreateRequest(Guid? WarehousePublicId = null, int[]? ZoneIds = null, int[]? BinIds = null,
    Guid[]? ProductPublicIds = null, int[]? CategoryIds = null);

/// <summary>
/// Encabezado del conteo. VarianceLines y NetVariance llegan null en el conteo a ciegas (Lote 8A: quien consulta no tiene
/// warehouse.count), en la ficha y en la lista: junto con lo contado revelarían lo esperado. En modo informado nunca son null.
/// Lote 14 (al final, con valor por defecto): BinCount = posiciones distintas de sus líneas; BinCode y ZoneCode solo si el
/// conteo es de UNA posición; OriginCode/Origin = MANUAL 'Selección' | CHANGES 'Lo cambiado' (con su ventana
/// ChangesFromUtc/ChangesToUtc); TaskId = tarea COUNT del conteo (la abierta; si no hay, la última) para asignarla con
/// POST /warehouse-tasks/{taskId}/assign; AssignedToUserId/AssignedToName = a quién está asignada.
/// Lote 21: OriginCode puede ser PRODUCT (conteo por producto); CorrectedLines = líneas cuya cantidad corrigió alguien distinto de
/// quien la capturó (o después de terminar el conteo).
/// </summary>
public sealed record CycleCountDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status,
    int LineCount, int CountedLines, int? VarianceLines, decimal? NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive,
    int BinCount = 0, string? BinCode = null, string? ZoneCode = null, string? OriginCode = null, int? TaskId = null, string? AssignedToName = null,
    string? Origin = null, DateTime? ChangesFromUtc = null, DateTime? ChangesToUtc = null, int? AssignedToUserId = null,
    int CorrectedLines = 0);

/// <summary>
/// Línea del conteo. Conteo a ciegas (Lote 8A): cuando quien consulta la ficha NO tiene warehouse.count (solo inventory.view),
/// las cantidades esperadas se omiten: SystemQty, VarianceQty, CurrentQty, ReconciledSystemQty y AdjustedQty llegan null,
/// ExpectedSerials vacío e IsStale/SystemQtyChanged en false. Lo contado (CountedQty, CountedSerials) siempre se devuelve.
/// Con warehouse.count la ficha es la de siempre (modo informado) y SystemQty/CurrentQty nunca son null.
/// Lote 14: Barcode = código de barras del producto (el escáner de la web lleva a la línea por SKU, código de barras, lote o
/// serie); no revela lo esperado, así que también llega a ciegas.
/// Lote 21 (evidencia, también a ciegas: no revela lo esperado): CountedQty es SIEMPRE el valor vigente (el que se reconcilia).
/// CapturedQty/CapturedByName/CapturedByUserId/CapturedAtUtc = lo que contó originalmente quien capturó (null = sin captura);
/// CorrectedByName/CorrectedByUserId/CorrectedAtUtc = quién corrigió y cuándo (null = no corregida); WasCorrected = hay corrección
/// (CountedQty ≠ CapturedQty). BinIsProvisional = la posición se creó desde un conteo y está pendiente de revisión.
/// </summary>
public sealed record CycleCountLineDto(int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName,
    string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal? SystemQty, decimal? CountedQty, decimal? VarianceQty,
    IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal? CurrentQty, decimal? ReconciledSystemQty,
    bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId, string? Barcode = null,
    decimal? CapturedQty = null, string? CapturedByName = null, int? CapturedByUserId = null, DateTime? CapturedAtUtc = null,
    string? CorrectedByName = null, int? CorrectedByUserId = null, DateTime? CorrectedAtUtc = null, bool WasCorrected = false,
    bool BinIsProvisional = false);

/// <summary>Ficha del conteo. IsBlind (Lote 8A) = true cuando las cantidades esperadas de las líneas se omitieron (conteo a ciegas).</summary>
public sealed record CycleCountDetailDto(CycleCountDto Count, IReadOnlyList<CycleCountLineDto> Lines, string RowVersion, bool IsBlind = false);

/// <summary>
/// Filtros de la lista de conteos. From/To = fecha de alta en días LOCALES de la compañía (Lote 14: hora de Puerto Rico), 'hasta'
/// inclusive. Lote 14: ZoneIds (alguna línea en una posición de esas zonas), Origins (MANUAL | CHANGES; MANUAL incluye los
/// conteos sin origen) y la página Skip/Take de GET /cycle-counts/page (Take 1..200, por defecto 50).
/// </summary>
public sealed record CycleCountQuery(Guid[]? WarehousePublicIds = null, string[]? Status = null, DateOnly? From = null, DateOnly? To = null,
    int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, string? Search = null,
    int[]? ZoneIds = null, string[]? Origins = null, int Skip = 0, int Take = 50);

/// <summary>Lote 14 (hallazgo 14): página de conteos con el total (el Pulso cuenta "Conteos abiertos" con Total).</summary>
public sealed record CycleCountPageDto(int Total, int Skip, int Take, IReadOnlyList<CycleCountDto> Items);

/// <summary>
/// Lote 14 (D2, D3, D4) — "Conteo de lo cambiado": almacén (o el único activo), ventana [FromUtc, ToUtc) (null = la por
/// defecto: desde la última generación del almacén o, la primera vez, desde las 00:00 locales de hoy; 'hasta' = ahora),
/// zonas opcionales e IncludeEmpty (las posiciones o claves que quedaron en 0 entran con SystemQty 0).
/// </summary>
public sealed record CycleCountFromChangesRequest(Guid? WarehousePublicId = null, DateTime? FromUtc = null, DateTime? ToUtc = null,
    int[]? ZoneIds = null, bool IncludeEmpty = true);

/// <summary>
/// Vista previa de "lo cambiado" (lo mismo que haría el alta, sin escribir): ventana efectiva, movimientos considerados (sin los
/// de un conteo), Positions = conteos que se crearían, PositionsWithOpenCount = posiciones saltadas por tener un conteo
/// Pendiente o Contado, PositionsInactive = posiciones inactivas saltadas, PositionsEmpty = posiciones sin nada que contar
/// (vacías con IncludeEmpty = false), Lines = líneas en total, MaxPositions = tope por generación. LastChangesToUtc = 'hasta'
/// de la generación anterior del almacén (null la primera vez). Problem = el mensaje exacto con que el alta respondería 400
/// (sin movimientos, todas con conteo pendiente, más de 200 posiciones o nada que contar); null si se puede crear.
/// </summary>
public sealed record CycleCountChangesPreviewDto(DateTime FromUtc, DateTime ToUtc, int Movements, int Positions, int PositionsWithOpenCount,
    int PositionsInactive, int Lines, int MaxPositions, Guid WarehousePublicId = default, string WarehouseCode = "", int PositionsEmpty = 0,
    DateTime? LastChangesToUtc = null, string? Problem = null);

/// <summary>Resultado del alta de "lo cambiado": la ventana usada (como la vista previa) y los conteos creados (uno por posición).</summary>
public sealed record CycleCountBatchResultDto(CycleCountChangesPreviewDto Window, IReadOnlyList<CycleCountDto> Counts);

public sealed record CycleCountLinesQuery(int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, bool? OnlyVariance = null,
    bool? OnlyPending = null, string? Search = null);

public sealed record CountCaptureItem(int LineId, decimal? CountedQty = null, IReadOnlyList<string>? SerialNumbers = null);

public sealed record CountCaptureRequest(IReadOnlyList<CountCaptureItem>? Lines, string? RowVersion = null);

/// <summary>
/// Lote 8A — captura en lote (PUT /cycle-counts/{id}/lines/batch). Cada renglón identifica su línea por LineId o, si no lo
/// trae, por posición + producto (+ lote por id o por número): si esa línea ya está en el conteo se captura; si no, se agrega
/// (lo encontrado) con su captura. Todo en una transacción: un error en cualquier renglón no guarda nada.
/// </summary>
public sealed record CountBatchItem(int? LineId = null, int? BinId = null, Guid? ProductPublicId = null, int? LotId = null, LotInput? Lot = null,
    decimal? CountedQty = null, IReadOnlyList<string>? SerialNumbers = null);

public sealed record CountBatchRequest(IReadOnlyList<CountBatchItem>? Lines, string? RowVersion = null);

public sealed record CountAddLineRequest(int? BinId, Guid? ProductPublicId, int? LotId = null, LotInput? Lot = null, decimal? CountedQty = null,
    IReadOnlyList<string>? SerialNumbers = null);

public sealed record CountReconcileRequest(string? Comment = null, string? RowVersion = null);

// ======================================================================== Lote 21 — conteo por producto

/// <summary>
/// Serie de una línea en la vista previa: lo que se asentaría. Bajas = esperadas hoy en la posición y no contadas; Altas =
/// contadas que el sistema no tiene en inventario; Traslados = contadas aquí y en inventario en otra posición.
/// </summary>
public sealed record ReconcilePreviewSerialsDto(IReadOnlyList<string> Removals, IReadOnlyList<string> Additions, IReadOnlyList<string> Transfers);

/// <summary>
/// Una línea en la vista previa de la reconciliación (se calcula con la MISMA regla que la reconciliación real; no escribe).
/// SystemQty = foto al crear; CurrentQty/ReservedQty = existencia HOY (la base del ajuste, D22); CountedQty = valor vigente
/// (null con IsPending = true: sin contar, no es error); CapturedQty/CorrectedBy* = evidencia. AdjustmentQty = movimiento neto
/// con signo que se asentaría en la posición (0 = la línea concuerda), ResultingQty = CurrentQty + AdjustmentQty, Movements =
/// asientos del ledger que generaría (en serie, uno por serie). Error = el mensaje exacto con que la reconciliación respondería
/// 409 por esta línea (contado menor que lo reservado); null si no hay. SystemQtyChanged = la existencia se movió desde la foto.
/// </summary>
public sealed record ReconcilePreviewLineDto(int LineId, int BinId, string BinCode, string ZoneCode, bool BinIsProvisional,
    Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode, int? LotId, string? LotNumber,
    decimal SystemQty, decimal CurrentQty, decimal ReservedQty, decimal? CountedQty, bool IsPending,
    decimal? CapturedQty, string? CapturedByName, DateTime? CapturedAtUtc, string? CorrectedByName, DateTime? CorrectedAtUtc, bool WasCorrected,
    decimal AdjustmentQty, decimal ResultingQty, int Movements, bool SystemQtyChanged, string? Error,
    ReconcilePreviewSerialsDto? Serials);

/// <summary>
/// Totales de la vista previa. Lines = líneas del conteo; PendingLines = sin contar (dato, no error); LinesWithDifference = líneas
/// que asentarían algo; Movements = asientos del ledger que se generarían; ErrorLines = líneas con error; Matches = el conteo
/// CUADRA: tiene líneas, ninguna pendiente, ninguna con error ni con diferencia contra la existencia actual (se puede cerrar en
/// bloque y termina en Concordancia); ResultStatusCode = estatus en que terminaría (RECONCILED | RECONCILED_VARIANCE; null si no se
/// puede reconciliar todavía por pendientes, errores o línea de serie repetida).
/// </summary>
public sealed record ReconcilePreviewTotalsDto(int Lines, int PendingLines, int LinesWithDifference, int Movements, int ErrorLines,
    bool Matches, string? ResultStatusCode);

/// <summary>
/// Vista previa de reconciliar el conteo (GET /cycle-counts/{id}/reconcile-preview, warehouse.count). BlockingError = error del
/// conteo entero (una serie capturada en dos líneas: 400 al reconciliar), null si no hay. RowVersion = el de la ficha.
/// </summary>
public sealed record ReconcilePreviewDto(CycleCountDto Count, IReadOnlyList<ReconcilePreviewLineDto> Lines, ReconcilePreviewTotalsDto Totals,
    string? BlockingError, string RowVersion);

/// <summary>
/// Una fila de la lista "Por revisar" (GET /cycle-counts/review, warehouse.count): el encabezado del conteo más lo calculado en
/// lotes. CountedByUserId/CountedByName = quien capturó más líneas (CountedByCount = cuántas personas capturaron); FirstProduct* =
/// el primer producto por SKU y OtherProducts = cuántos productos distintos más tiene; Positions = posiciones distintas; Lines;
/// DifferingLines = líneas que asentarían algo contra la existencia ACTUAL; ErrorLines; Movements = asientos que generaría;
/// CorrectedLines; Matches = igual que en la vista previa (cuadra y se puede cerrar en bloque).
/// </summary>
public sealed record CycleCountReviewItemDto(CycleCountDto Count, int? CountedByUserId, string? CountedByName, int CountedByCount,
    Guid? FirstProductPublicId, string? FirstProductSku, string? FirstProductName, int OtherProducts,
    int Positions, int Lines, int PendingLines, int DifferingLines, int ErrorLines, int Movements, int CorrectedLines, bool Matches);

/// <summary>Página de "Por revisar": total con los filtros, y las filas de la página (más recientes primero).</summary>
public sealed record CycleCountReviewPageDto(int Total, int Skip, int Take, IReadOnlyList<CycleCountReviewItemDto> Items);

/// <summary>
/// Filtros de "Por revisar": almacén, quien contó (usuario que capturó alguna línea), búsqueda (número, SKU o nombre de producto),
/// IncludeOpen (también los conteos Pendientes con TODAS sus líneas capturadas; por defecto solo Contado) y la página.
/// </summary>
public sealed record CycleCountReviewQuery(Guid? WarehousePublicId = null, int? CountedByUserId = null, string? Search = null,
    bool IncludeOpen = false, int Skip = 0, int Take = 50);

/// <summary>
/// Cierre en bloque (POST /cycle-counts/reconcile-matching): WarehousePublicId y/o Ids acotan qué conteos se miran (sin ninguno,
/// todos los del tenant, hasta 200); Comment se guarda en el historial de cada conteo cerrado; IncludeOpen = también los
/// Pendientes con todas sus líneas capturadas (por defecto solo Contado).
/// </summary>
public sealed record CountReconcileMatchingRequest(Guid? WarehousePublicId = null, int[]? Ids = null, string? Comment = null, bool IncludeOpen = false);

/// <summary>Conteo cerrado por el cierre en bloque: terminó en Concordancia (RECONCILED).</summary>
public sealed record CycleCountClosedItemDto(int Id, string Number, string StatusCode, int Lines);

/// <summary>
/// Conteo que el cierre en bloque NO cerró. ReasonCode: WouldPost (asentaría Count movimientos), Errors (Count líneas con error de
/// reconciliación), Pending (Count líneas sin contar), Stale (cuadraba al mirarlo pero la existencia cambió antes de cerrarlo),
/// NotCounted (Pendiente y no se pidió includeOpen), AlreadyReconciled, NotFound (no existe o no es de la compañía) o Failed
/// (otro error; Reason trae el mensaje). Reason es el texto en español para mostrar.
/// </summary>
public sealed record CycleCountSkippedItemDto(int Id, string? Number, string ReasonCode, string Reason, int? Count = null);

/// <summary>Resultado del cierre en bloque. Examined = conteos mirados; Truncated = había más de 200 y quedaron fuera.</summary>
public sealed record CycleCountReconcileMatchingResultDto(int Examined, IReadOnlyList<CycleCountClosedItemDto> Closed,
    IReadOnlyList<CycleCountSkippedItemDto> Skipped, bool Truncated);

/// <summary>
/// Posición provisional desde un conteo (POST /cycle-counts/{id}/bins, warehouse.count.capture): zona del almacén del conteo y
/// código (o pasillo, rack, nivel y posición para componerlo). Mismas validaciones que el alta de posición.
/// </summary>
public sealed record CountProvisionalBinRequest(int? ZoneId, string? Code = null, string? Aisle = null, string? Rack = null,
    string? Level = null, string? Position = null);
