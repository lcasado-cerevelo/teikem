namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Recolección y empaque ad hoc. Firma posicional FIJA. La recolección se identifica por PublicId.

public sealed record PickBatchLineRequest(Guid? ProductPublicId, decimal? Quantity, int? BinId = null, int? LotId = null,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>
/// Recolección. Pack (Lote 8A, cola del aparato) = recolectar y empacar en UNA sola transacción; solo se acepta en
/// POST /api/v1/pick-batches/collect-and-pack (respuesta PickBatchPackResultDto: recolección PACKED + orden creada); en
/// POST /api/v1/pick-batches → 400. Su RowVersion se ignora (la recolección nace en la misma llamada). Mismas validaciones
/// y permisos que el flujo por pasos (empacar exige orders.create).
/// </summary>
public sealed record PickBatchCreateRequest(Guid? WarehousePublicId = null, IReadOnlyList<PickBatchLineRequest>? Lines = null,
    PickBatchPackRequest? Pack = null);

/// <summary>2026-10-11 (al final, con valor por defecto): Kind = MANUAL | PACK | ALL (null = ALL: lo de siempre más los despachos manuales).</summary>
public sealed record PickBatchQuery(DateOnly? From = null, DateOnly? To = null, Guid[]? ProductPublicIds = null, string[]? Status = null,
    string? OrderNumber = null, string? InvoiceNumber = null, string? Search = null, bool IncludeDeleted = false, int Skip = 0, int Take = 100,
    string? Kind = null);

public sealed record PickBatchLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, int BinId, string BinCode,
    int? LotId, string? LotNumber, string? SerialNumber, decimal? UnitCost, long IssueTxnId, long? ReversalTxnId);

/// <summary>
/// Recolección o despacho manual. 2026-10-11 (al final, con valor por defecto): IsManual (despacho manual DMA-#####: nunca se
/// empaca, CanPack = false), ReasonCode/ReasonLabel (motivo del catálogo ManualIssueReason), Note (nota libre) y OwnerClientName
/// (cliente dueño del inventario; null = propio).
/// </summary>
public sealed record PickBatchDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode,
    string Status, DateTime CollectedAtUtc, string? CollectedBy, DateTime? PackedAtUtc, Guid? OrderPublicId, string? OrderNumber,
    string? PackBatchNumber, string? ClientInvoiceNumber, string? OrderStatusCode, string? OrderStatus, string? ClientName,
    string? DisplayNumbers, bool CanPack, bool CanDelete, decimal TotalQty, decimal? TotalCost, IReadOnlyList<PickBatchLineDto> Lines,
    bool IsActive, string RowVersion, bool IsManual = false, string? ReasonCode = null, string? ReasonLabel = null, string? Note = null,
    string? OwnerClientName = null);

public sealed record PickBatchPageDto(int Total, int Skip, int Take, IReadOnlyList<PickBatchDto> Items);

public sealed record PickBatchPackRequest(OrderCreateRequest? Order, string? RowVersion = null);

public sealed record PickBatchPackResultDto(PickBatchDto Batch, OrderDetailDto Order);

public sealed record PickBatchDeleteRequest(string? Comment = null, string? RowVersion = null);

/// <summary>
/// 2026-10-11 — Despacho manual (POST /api/v1/manual-issues): salida de inventario SIN entrega, con el mismo formato de líneas
/// que la recolección (FEFO, posición/lote explícitos, series escaneadas). ReasonCode obligatorio (código del catálogo
/// ManualIssueReason: SAMPLE, INTERNAL_USE, CUSTOMER_PICKUP, SALE, OTHER u otro que agregue la compañía); Note opcional (≤ 500).
/// Sin cliente ni consignatario: el dueño es el de los productos (uno solo por documento).
/// </summary>
public sealed record ManualIssueCreateRequest(Guid? WarehousePublicId = null, IReadOnlyList<PickBatchLineRequest>? Lines = null,
    string? ReasonCode = null, string? Note = null);

/// <summary>
/// 2026-10-11 (b) — Un motivo del despacho manual en GET /api/v1/manual-issues/reasons: los mismos campos que LookupValueDto (la
/// respuesta sigue siendo un arreglo, compatible con la app y la web) más IsDefault = es el «Motivo por default del despacho
/// manual» de la compañía (Ajustes de la compañía). A lo sumo uno sale en true; ninguno si no hay default o si el guardado ya no
/// está activo y habilitado. Solo sirve para preseleccionar: POST /manual-issues sigue exigiendo reasonCode.
/// </summary>
public sealed record ManualIssueReasonDto(
    int Id, string Entity, string Code, string Label, IDictionary<string, string> Labels, string? Description,
    string? ExtraJson, int SortOrder, bool IsSystem, bool IsEnabled, bool IsOverridden, int? TenantId, bool IsActive, bool IsDefault)
{
    public static ManualIssueReasonDto From(LookupValueDto v, bool isDefault) => new(
        v.Id, v.Entity, v.Code, v.Label, v.Labels, v.Description, v.ExtraJson, v.SortOrder, v.IsSystem, v.IsEnabled, v.IsOverridden,
        v.TenantId, v.IsActive, isDefault);
}
