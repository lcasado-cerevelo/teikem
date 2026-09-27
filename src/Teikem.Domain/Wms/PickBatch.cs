using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Recolección y empaque ad hoc (D43): COLLECTED → PACKED; CANCELLED (eliminar con reversa, IsActive = 0). Number =
/// EMP-##### del contador PACKBATCH (D10): al empacar es el PackBatchNumber de la orden. Una recolección empacada solo se
/// elimina si su orden sigue en la etapa inicial.
/// </summary>
[AuditEntity(Constants.EntityTypes.PickBatch)]
public class PickBatch : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int PickBatchId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int StatusCodeId { get; set; }
    public int? TransportOrderId { get; set; }
    public string? ClientInvoiceNumber { get; set; }
    public DateTime CollectedAtUtc { get; set; }
    public int? CollectedBy { get; set; }
    public DateTime? PackedAtUtc { get; set; }
    public int? PackedBy { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledBy { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<PickBatchLine> Lines { get; set; } = new List<PickBatchLine>();
}

/// <summary>
/// Línea recolectada: Quantity en magnitud (el ISSUE del ledger la guarda negativa), UnitCost congelado (D35), IssueTxnId y
/// ReversalTxnId (ADJUSTMENT PICK_BATCH_REVERSAL al eliminar, D13). Sin TenantId: por su recolección filtrada.
/// </summary>
[AuditEntity(Constants.EntityTypes.PickBatch)]
public class PickBatchLine
{
    public int PickBatchLineId { get; set; }
    public int PickBatchId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int FromBinId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public long IssueTxnId { get; set; }
    public long? ReversalTxnId { get; set; }
}
