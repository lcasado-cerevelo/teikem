namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Conteo cíclico en modo informado (D22). Firma posicional FIJA.

public sealed record CycleCountCreateRequest(Guid? WarehousePublicId = null, int[]? ZoneIds = null, int[]? BinIds = null,
    Guid[]? ProductPublicIds = null, int[]? CategoryIds = null);

public sealed record CycleCountDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status,
    int LineCount, int CountedLines, int VarianceLines, decimal NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive);

public sealed record CycleCountLineDto(int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName,
    string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal SystemQty, decimal? CountedQty, decimal? VarianceQty,
    IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal CurrentQty, decimal? ReconciledSystemQty,
    bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId);

public sealed record CycleCountDetailDto(CycleCountDto Count, IReadOnlyList<CycleCountLineDto> Lines, string RowVersion);

public sealed record CycleCountQuery(Guid[]? WarehousePublicIds = null, string[]? Status = null, DateOnly? From = null, DateOnly? To = null,
    int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, string? Search = null);

public sealed record CycleCountLinesQuery(int[]? BinIds = null, Guid[]? ProductPublicIds = null, int[]? CategoryIds = null, bool? OnlyVariance = null,
    bool? OnlyPending = null, string? Search = null);

public sealed record CountCaptureItem(int LineId, decimal? CountedQty = null, IReadOnlyList<string>? SerialNumbers = null);

public sealed record CountCaptureRequest(IReadOnlyList<CountCaptureItem>? Lines, string? RowVersion = null);

public sealed record CountAddLineRequest(int? BinId, Guid? ProductPublicId, int? LotId = null, LotInput? Lot = null, decimal? CountedQty = null,
    IReadOnlyList<string>? SerialNumbers = null);

public sealed record CountReconcileRequest(string? Comment = null, string? RowVersion = null);
