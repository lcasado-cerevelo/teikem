using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Conteo cíclico CC-##### en modo informado (R16, D22): OPEN → COUNTED → RECONCILED. Un conteo reconciliado queda
/// congelado (ya está en el ledger).
/// </summary>
[AuditEntity(Constants.EntityTypes.CycleCount)]
public class CycleCount : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int CycleCountId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int StatusCodeId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public int? ReconciledBy { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<CycleCountLine> Lines { get; set; } = new List<CycleCountLine>();
}

/// <summary>
/// Línea del conteo. SystemQty = foto al crear; VarianceQty (computada en SQL, contra la foto) NUNCA se usa en lógica.
/// ReconciledSystemQty = saldo en mano bloqueado al reconciliar (base del ajuste); SystemQtyChanged marca que el saldo se
/// movió desde la foto (D22). Sin TenantId: por su conteo filtrado.
/// </summary>
[AuditEntity(Constants.EntityTypes.CycleCount)]
public class CycleCountLine
{
    public int CycleCountLineId { get; set; }
    public int CycleCountId { get; set; }
    public int WarehouseBinId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public decimal SystemQty { get; set; }
    public decimal? CountedQty { get; set; }
    /// <summary>Computada en SQL (ISNULL(CountedQty,0) − SystemQty). No usar en lógica.</summary>
    public decimal VarianceQty { get; private set; }
    public string? CountedSerialsJson { get; set; }
    public decimal? ReconciledSystemQty { get; set; }
    public bool SystemQtyChanged { get; set; }
    public long? AdjustmentTxnId { get; set; }
}
