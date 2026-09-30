using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Aviso de llegada (ASN): de un cliente 3PL (ClientId) o nacido de una PO (PurchaseOrderId), nunca ambos
/// (CK_Asn_Origin). Un solo recibo activo por ASN (UX_Receipt_Asn, D6).
/// </summary>
[AuditEntity(Constants.EntityTypes.Asn)]
public class Asn : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int AsnId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int? ClientId { get; set; }
    public int? PurchaseOrderId { get; set; }
    public string? Reference { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public int StatusCodeId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }

    public ICollection<AsnLine> Lines { get; set; } = new List<AsnLine>();
}

/// <summary>Línea del ASN (con la línea de PO de origen si nació de una compra). Sin TenantId: por su ASN filtrado.</summary>
[AuditEntity(Constants.EntityTypes.Asn)]
public class AsnLine
{
    public int AsnLineId { get; set; }
    public int AsnId { get; set; }
    public int ProductId { get; set; }
    public decimal ExpectedQty { get; set; }
    public string? LotNumber { get; set; }
    public int? PurchaseOrderLineId { get; set; }
}

/// <summary>
/// Recibo REC-#####: EXPECTED → RECEIVING ↔ DISCREPANCY → RECEIVED / RECEIVED_VARIANCE (confirmación completa, D5) → PUTAWAY
/// (Lote 13). ReceivedAtUtc = fecha de confirmación (insumo de Contabilización de compras, Lote 10). Un recibo confirmado
/// queda congelado: ya está en el ledger. Lote 13: posición de recepción por defecto (DefaultStagingBinId, del mismo almacén
/// por la FK compuesta), transporte (Carrier) y referencia (Reference) del encabezado.
/// </summary>
[AuditEntity(Constants.EntityTypes.Receipt)]
public class ReceiptHeader : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int ReceiptHeaderId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    public int? AsnId { get; set; }
    public int? DockId { get; set; }
    public int? DefaultStagingBinId { get; set; }
    public string? Carrier { get; set; }
    public string? Reference { get; set; }
    public int ReceiptTypeLookupId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int StatusCodeId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public int? ReceivedBy { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<ReceiptLine> Lines { get; set; } = new List<ReceiptLine>();
}

/// <summary>
/// Línea del recibo. ReceivedQty arranca igual a ExpectedQty (R8). AdjustmentTxnId enlaza el ADJUSTMENT RECEIPT_VARIANCE de
/// la diferencia (D4). Sin TenantId: por su recibo filtrado.
/// </summary>
[AuditEntity(Constants.EntityTypes.Receipt)]
public class ReceiptLine
{
    public int ReceiptLineId { get; set; }
    public int ReceiptHeaderId { get; set; }
    public int? AsnLineId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal? ExpectedQty { get; set; }
    public string? SerialNumbersJson { get; set; }
    public int? StagingBinId { get; set; }
    public long? AdjustmentTxnId { get; set; }
}
