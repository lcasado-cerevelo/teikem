using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Recolección y empaque ad hoc (D43): COLLECTED → PACKED; CANCELLED (eliminar con reversa, IsActive = 0). Number =
/// EMP-##### del contador PACKBATCH (D10): al empacar es el PackBatchNumber de la orden. Una recolección empacada solo se
/// elimina si su orden sigue en la etapa inicial.
/// 2026-10-11 — Despacho manual: la MISMA salida (ISSUE, FEFO, series, reversa) sin entrega. ManualIssueReasonId no nulo = es un
/// despacho manual: número DMA-##### de su propio contador (MANUALISSUE, no consume EMP), motivo obligatorio (LookupCode
/// 'ManualIssueReason') y nota libre opcional (≤ 500). Nunca se empaca: vive COLLECTED → CANCELLED (eliminar con reversa).
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
    /// <summary>2026-10-11: motivo del despacho manual (LookupCode 'ManualIssueReason'); NULL = recolección normal (EMP).</summary>
    public int? ManualIssueReasonId { get; set; }
    /// <summary>2026-10-11: nota libre del despacho manual (≤ 500 caracteres).</summary>
    public string? Note { get; set; }
    /// <summary>Lote 61: la orden de venta que atiende esta recolección (despacho contra orden de venta); NULL = recolección normal.</summary>
    public int? SalesOrderId { get; set; }
    /// <summary>Lote 143: el aviso de salida que atiende esta recolección (despacho contra aviso de salida); NULL = recolección normal.</summary>
    public int? ExitNoticeId { get; set; }
    /// <summary>Lote 176: la recolección maestra (varios documentos del mismo cliente recolectados juntos) a la que pertenece; NULL = recolección suelta.</summary>
    public int? PickGroupId { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<PickBatchLine> Lines { get; set; } = new List<PickBatchLine>();
}

/// <summary>
/// Lote 176 — recolección maestra: varios documentos (órdenes de venta y/o avisos de salida) DEL MISMO CLIENTE y almacén que se recolectan juntos y salen en UN solo envío.
/// Cada documento conserva su recolección (PickBatch con PickGroupId) con sus movimientos; el grupo es el documento maestro y le da su número (EMP-#####, el del envío).
/// El estatus se deriva de sus recolecciones: recolectada → empacada (TransportOrderId = el envío) → eliminada (IsActive = 0).
/// </summary>
public class PickGroup : ITenantScoped, ISoftDeletable
{
    public int PickGroupId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int ClientId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CollectedAtUtc { get; set; }
    public int? CollectedBy { get; set; }
    public int? TransportOrderId { get; set; }
    public DateTime? PackedAtUtc { get; set; }
    public int? PackedBy { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
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
    /// <summary>Lote 61: la línea de la orden de venta que atendió; NULL = recolección normal.</summary>
    public int? SalesOrderLineId { get; set; }
    /// <summary>Lote 143: la línea del aviso de salida que atendió; NULL = recolección normal.</summary>
    public int? ExitNoticeLineId { get; set; }
    /// <summary>Lote 69: si esta línea sale de un kit, el kit (la línea es de un componente); NULL = producto normal.</summary>
    public int? KitProductId { get; set; }
    /// <summary>Lote 69: cuántos kits representa este despacho; solo en UNA de las líneas de componentes de cada kit (el resto NULL), para deshacer en kits.</summary>
    public decimal? KitQuantity { get; set; }
}
