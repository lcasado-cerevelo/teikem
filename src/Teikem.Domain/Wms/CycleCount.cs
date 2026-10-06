using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 6 — Conteo cíclico CC-##### en modo informado (R16, D22): OPEN → COUNTED → RECONCILED. Un conteo reconciliado queda
/// congelado (ya está en el ledger).
/// Lote 14 (D7): el final es RECONCILED 'Concordancia' o RECONCILED_VARIANCE 'Diferencia'. OriginLookupId = CycleCountOrigin
/// (MANUAL | CHANGES); ChangesFromUtc/ChangesToUtc = ventana de movimientos de "lo cambiado" [desde, hasta) (solo CHANGES).
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
    /// <summary>Lote 14: origen del conteo (LookupCode CycleCountOrigin). NULL solo en filas anteriores al seed del lote.</summary>
    public int? OriginLookupId { get; set; }
    /// <summary>Lote 14: inicio (inclusivo) de la ventana de movimientos de "lo cambiado".</summary>
    public DateTime? ChangesFromUtc { get; set; }
    /// <summary>Lote 14: fin (exclusivo) de la ventana; la siguiente generación del almacén arranca aquí por defecto.</summary>
    public DateTime? ChangesToUtc { get; set; }

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

    // Conteo por producto (Lote 21) — evidencia de la captura. CountedQty es SIEMPRE el valor vigente (el que se reconcilia);
    // CapturedQty es lo que contó originalmente quien capturó (CapturedBy, CapturedAtUtc) y no se pierde si alguien lo corrige.
    // Corrected* se llena cuando OTRO usuario, o cualquiera una vez terminado el conteo (Contado), cambia el valor. Una
    // corrección NO mueve inventario: solo cambia la cantidad que se reconcilia.
    /// <summary>Lote 21: cantidad capturada originalmente (null = sin captura).</summary>
    public decimal? CapturedQty { get; set; }
    /// <summary>Lote 21: series capturadas originalmente (JSON), solo en productos con serie.</summary>
    public string? CapturedSerialsJson { get; set; }
    public int? CapturedBy { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    /// <summary>Lote 21: quién corrigió la cantidad (null = no corregida).</summary>
    public int? CorrectedBy { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }

    // Tarea 25 — conteo informado al capturar (CountRevealRules): verificación de la línea contra lo esperado.
    /// <summary>MATCH | RECOUNT | FINAL (CountCheckStates); null = no verificada.</summary>
    public string? CheckState { get; set; }
    /// <summary>Primera cifra verificada (la que vio el contador antes de saber si coincidía).</summary>
    public decimal? FirstCheckQty { get; set; }
    /// <summary>Última cifra verificada: la única que el contador puede guardar en la línea.</summary>
    public decimal? LastCheckQty { get; set; }
}
