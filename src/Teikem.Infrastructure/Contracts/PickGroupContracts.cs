namespace Teikem.Infrastructure.Contracts;

/// <summary>Lote 176 — una línea a recolectar de un documento de la maestra: la línea del documento, la cantidad y, si se toma de otra posición, la posición y el lote (igual que el despacho de un documento).</summary>
public sealed record PickGroupLineRequest(int? LineId = null, decimal? Quantity = null, int? BinId = null, int? LotId = null);

/// <summary>Un documento de la maestra (tipo SALES_ORDER o EXIT_NOTICE + su id). Sin Lines se recolecta TODO lo reservado del documento.</summary>
public sealed record PickGroupDocumentRequest(string? Kind = null, Guid? PublicId = null, IReadOnlyList<PickGroupLineRequest>? Lines = null);

/// <summary>POST /pick-batches/groups: de 2 a 20 documentos del mismo cliente y almacén, recolectados juntos.</summary>
public sealed record PickGroupCreateRequest(IReadOnlyList<PickGroupDocumentRequest>? Documents = null, string? Note = null);

public sealed record PickGroupDocumentDto(string Kind, Guid PublicId, string Number, Guid BatchPublicId, string BatchNumber, decimal Quantity);

/// <summary>Un movimiento de la maestra: de qué posición salió qué y para cuál documento.</summary>
public sealed record PickGroupMovementDto(string DocumentKind, string DocumentNumber, string BatchNumber, Guid ProductPublicId, string Sku, string ProductName,
    decimal Quantity, int BinId, string BinCode, int? LotId, string? LotNumber);

/// <summary>
/// La recolección maestra: documentos, movimientos (solo en la ficha), el envío único al empacar y lo que se puede hacer. StatusCode = COLLECTED, PACKED o CANCELLED (dominio PickBatchStatus).
/// </summary>
public sealed record PickGroupDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, Guid ClientPublicId, string ClientName,
    string StatusCode, DateTime CollectedAtUtc, string? CollectedBy, DateTime? PackedAtUtc, Guid? OrderPublicId, string? OrderNumber, string? ClientInvoiceNumber,
    string? Note, decimal TotalQty, bool CanPack, bool CanDelete, IReadOnlyList<PickGroupDocumentDto> Documents, IReadOnlyList<PickGroupMovementDto> Movements,
    bool IsActive, string RowVersion);

public sealed record PickGroupPageDto(int Total, int Skip, int Take, IReadOnlyList<PickGroupDto> Items);

/// <summary>Filtros de GET /pick-batches/groups: Search (número, cliente o número de un documento), Status (COLLECTED, PACKED, CANCELLED).</summary>
public sealed record PickGroupQuery(string? Search = null, string? Status = null, int Skip = 0, int Take = 50);

public sealed record PickGroupPackResultDto(PickGroupDto Group, OrderDetailDto Order);
