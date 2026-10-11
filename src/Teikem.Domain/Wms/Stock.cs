using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Saldo por (producto, almacén, posición, lote): PROYECCIÓN BLOQUEADA del ledger (D2). Solo lo escribe
/// InventoryLedger (WmsWriteConfinementTests). CK_StockBalance_Qty: en mano ≥ 0, reservado ≥ 0, reservado ≤ en mano.
/// QtyAvailable es columna computada de SQL: se mapea pero NUNCA se usa en lógica (InMemory no la calcula); el disponible
/// se calcula con InventoryRules.Available. Sin AuditLog (el rastro es el ledger).
/// </summary>
public class StockBalance : ITenantScoped
{
    public int StockBalanceId { get; set; }
    public int TenantId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int? WarehouseBinId { get; set; }
    public int? LotId { get; set; }
    public decimal QtyOnHand { get; set; }
    public decimal QtyReserved { get; set; }
    /// <summary>Computada en SQL (QtyOnHand − QtyReserved). No usar en lógica: InventoryRules.Available.</summary>
    public decimal QtyAvailable { get; private set; }
    /// <summary>
    /// Lote 34 (FIFO): fecha de PRIMERA ENTRADA de lo que hay en este saldo. La fija el ledger al crearse el saldo o al volver a llenarse desde cero
    /// (en una transferencia hereda la del origen). NULL = no se conoce (va al final del orden FIFO).
    /// </summary>
    [NotAudited] public DateTime? FirstReceivedAtUtc { get; set; }
    [NotAudited] public DateTime UpdatedAtUtc { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Movimiento del ledger de inventario: SOLO INSERCIÓN (una reversa es un movimiento nuevo, R36). Quantity CON SIGNO (D3,
/// maestro L331): + entra a To; − sale de From; TRANSFER + con From y To. CK_InvTxn_Quantity (&lt;&gt; 0) y CK_InvTxn_Direction
/// impiden signos incoherentes. RefEntity/RefId se escriben en el INSERT (no hay UPDATE posterior, D48).
/// </summary>
public class InventoryTransaction : ITenantScoped
{
    public long InventoryTransactionId { get; set; }
    public int TenantId { get; set; }
    public int TxnTypeLookupId { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int? FromWarehouseId { get; set; }
    public int? FromBinId { get; set; }
    public int? ToWarehouseId { get; set; }
    public int? ToBinId { get; set; }
    /// <summary>Cantidad CON SIGNO (D3).</summary>
    public decimal Quantity { get; set; }
    public int? RefEntityLookupId { get; set; }
    public int? RefId { get; set; }
    public int? ReasonLookupId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}
