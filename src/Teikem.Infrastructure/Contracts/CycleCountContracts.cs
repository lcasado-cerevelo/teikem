namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Conteo cíclico en modo informado (D22). Firma posicional FIJA.

public sealed record CycleCountCreateRequest(Guid? WarehousePublicId = null, int[]? ZoneIds = null, int[]? BinIds = null,
    Guid[]? ProductPublicIds = null, int[]? CategoryIds = null);

/// <summary>
/// Encabezado del conteo. VarianceLines y NetVariance llegan null en el conteo a ciegas (Lote 8A: quien consulta no tiene
/// warehouse.count), en la ficha y en la lista: junto con lo contado revelarían lo esperado. En modo informado nunca son null.
/// </summary>
public sealed record CycleCountDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status,
    int LineCount, int CountedLines, int? VarianceLines, decimal? NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive);

/// <summary>
/// Línea del conteo. Conteo a ciegas (Lote 8A): cuando quien consulta la ficha NO tiene warehouse.count (solo inventory.view),
/// las cantidades esperadas se omiten: SystemQty, VarianceQty, CurrentQty, ReconciledSystemQty y AdjustedQty llegan null,
/// ExpectedSerials vacío e IsStale/SystemQtyChanged en false. Lo contado (CountedQty, CountedSerials) siempre se devuelve.
/// Con warehouse.count la ficha es la de siempre (modo informado) y SystemQty/CurrentQty nunca son null.
/// </summary>
public sealed record CycleCountLineDto(int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName,
    string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal? SystemQty, decimal? CountedQty, decimal? VarianceQty,
    IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal? CurrentQty, decimal? ReconciledSystemQty,
    bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId);

/// <summary>Ficha del conteo. IsBlind (Lote 8A) = true cuando las cantidades esperadas de las líneas se omitieron (conteo a ciegas).</summary>
public sealed record CycleCountDetailDto(CycleCountDto Count, IReadOnlyList<CycleCountLineDto> Lines, string RowVersion, bool IsBlind = false);

public sealed record CycleCountQuery(Guid[]? WarehousePublicIds = null, string[]? Status = null, DateOnly? From = null, DateOnly? To = null,
    int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, string? Search = null);

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
