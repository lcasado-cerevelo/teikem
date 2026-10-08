using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// 2026-10-08 — Reporte de daño DAN-#####: algo que llegó dañado en un recibo (Origin = RECEIPT: esas unidades no entran como buenas) o se
/// dañó en el almacén (Origin = WAREHOUSE: ya estaba en inventario). Al reportarlo se manda a una posición de cuarentena (REPORTED →
/// QUARANTINED) o se desecha de una vez (REPORTED → DISCARDED); lo que está en cuarentena se desecha después o se recupera a una posición de
/// guardado (RECOVERED). Los movimientos de inventario son del ledger (ajuste con motivo DAMAGE o transferencia), nunca de esta tabla. La causa
/// (vino así / accidente en el camino / accidente en el almacén / otro) es informativa: no hay reclamo a nadie.
/// </summary>
[AuditEntity(Constants.EntityTypes.DamageReport)]
public class DamageReport : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int DamageReportId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    /// <summary>Dónde estaba lo dañado (origen WAREHOUSE); null en un recibo.</summary>
    public int? FromBinId { get; set; }
    /// <summary>Posición de cuarentena donde quedó (null si se desechó de una vez).</summary>
    public int? QuarantineBinId { get; set; }
    public decimal Quantity { get; set; }
    public int OriginLookupId { get; set; }
    public int CauseLookupId { get; set; }
    /// <summary>Recibo del que llegó dañado (origen RECEIPT).</summary>
    public int? ReceiptHeaderId { get; set; }
    public string? Notes { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ReportedBy { get; set; }
    /// <summary>Cuándo se desechó o se recuperó (null mientras está reportado o en cuarentena).</summary>
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
}
