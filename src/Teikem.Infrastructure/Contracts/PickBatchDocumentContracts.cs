namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Lote 173 (C1) — «Recolección y empaque» agrupada por DOCUMENTO: Kind = EXIT_NOTICE (aviso de salida), SALES_ORDER (orden de venta) u ORDER (envío ya empacado). Un despacho manual no
/// tiene documento y no sale aquí (se ve en la lista de movimientos). Ordered/Reserved son null en un envío (no tiene «pedido»: solo lo recolectado).
/// </summary>
public sealed record PickDocumentQuery(string? Kind = null, string[]? Status = null, string? Search = null, bool PendingOnly = false, int Skip = 0, int Take = 50);

/// <summary>Un movimiento (una recolección) del documento. Se abre con GET /pick-batches/{publicId}.</summary>
public sealed record PickBatchSummaryDto(Guid PublicId, string Number, string StatusCode, string Status, DateTime CollectedAtUtc, string? CollectedBy, decimal TotalQty);

/// <summary>
/// Un documento con su avance: Ordered = pedido, Reserved = reservado todavía por sacar, Shipped = sacado; Pending = lo que falta por reservar o sacar (Ordered − Shipped − Reserved, solo si hay pedido).
/// BatchCount y LastCollectedAtUtc cuentan las recolecciones activas (no eliminadas); Batches las trae (la más reciente primero).
/// </summary>
public sealed record PickDocumentDto(string Kind, Guid PublicId, string Number, string? ClientName, string StatusCode, string Status, decimal? Ordered, decimal? Reserved, decimal Shipped,
    int BatchCount, DateTime? LastCollectedAtUtc, IReadOnlyList<PickBatchSummaryDto> Batches);

public sealed record PickDocumentPageDto(int Total, int Skip, int Take, IReadOnlyList<PickDocumentDto> Items);
