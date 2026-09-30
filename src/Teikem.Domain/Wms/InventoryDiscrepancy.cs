using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 14 (D5) — descuadre entre el Kárdex (InventoryTransaction) y el saldo (StockBalance) detectado por la conciliación.
/// - Kind BALANCE: una clave (producto, almacén, posición, lote); PRODUCT_TOTAL: el total del producto (sin almacén).
/// - OPEN → RESOLVED (se corrigió el saldo según el Kárdex, con CorrectedFrom/To) | DISMISSED (con nota) | SELF_CORRECTED
///   (volvió a cuadrar). Un solo descuadre abierto por clave (UX_InvDiscrepancy_OpenKey, filtrado por ClosedAtUtc NULL).
/// - LedgerQty y BalanceQty son las cifras de la ÚLTIMA revisión que lo vio descuadrado; la diferencia se calcula en código
///   (saldo − Kárdex). Cada revisión cambia LedgerQty, BalanceQty, LastCheckedAtUtc y CheckCount: van [NotAudited] para no
///   ensuciar la bitácora; se auditan el alta, el estatus y la resolución.
/// </summary>
[AuditEntity(Constants.EntityTypes.InventoryDiscrepancy)]
public class InventoryDiscrepancy : ITenantScoped, IHasStatus
{
    public int InventoryDiscrepancyId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int KindLookupId { get; set; }
    public int TriggerLookupId { get; set; }
    public int ProductId { get; set; }
    public int? WarehouseId { get; set; }
    public int? WarehouseBinId { get; set; }
    public int? LotId { get; set; }
    [NotAudited] public decimal LedgerQty { get; set; }
    [NotAudited] public decimal BalanceQty { get; set; }
    public DateTime DetectedAtUtc { get; set; }
    [NotAudited] public DateTime LastCheckedAtUtc { get; set; }
    [NotAudited] public int CheckCount { get; set; } = 1;
    /// <summary>Movimiento que destapó el descuadre (revisión por evento); NULL en la manual, la migración o el barrido.</summary>
    public long? LastTxnId { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public int? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }
    public decimal? CorrectedFromQty { get; set; }
    public decimal? CorrectedToQty { get; set; }
    /// <summary>Conteo de la posición creado después de corregir (D5); reservado: hoy no lo llena ningún endpoint.</summary>
    public int? CycleCountId { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
}
