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
/// </summary>
public sealed record CycleCountDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status,
    int LineCount, int CountedLines, int? VarianceLines, decimal? NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive,
    int BinCount = 0, string? BinCode = null, string? ZoneCode = null, string? OriginCode = null, int? TaskId = null, string? AssignedToName = null,
    string? Origin = null, DateTime? ChangesFromUtc = null, DateTime? ChangesToUtc = null, int? AssignedToUserId = null);

/// <summary>
/// Línea del conteo. Conteo a ciegas (Lote 8A): cuando quien consulta la ficha NO tiene warehouse.count (solo inventory.view),
/// las cantidades esperadas se omiten: SystemQty, VarianceQty, CurrentQty, ReconciledSystemQty y AdjustedQty llegan null,
/// ExpectedSerials vacío e IsStale/SystemQtyChanged en false. Lo contado (CountedQty, CountedSerials) siempre se devuelve.
/// Con warehouse.count la ficha es la de siempre (modo informado) y SystemQty/CurrentQty nunca son null.
/// Lote 14: Barcode = código de barras del producto (el escáner de la web lleva a la línea por SKU, código de barras, lote o
/// serie); no revela lo esperado, así que también llega a ciegas.
/// </summary>
public sealed record CycleCountLineDto(int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName,
    string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal? SystemQty, decimal? CountedQty, decimal? VarianceQty,
    IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal? CurrentQty, decimal? ReconciledSystemQty,
    bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId, string? Barcode = null);

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
