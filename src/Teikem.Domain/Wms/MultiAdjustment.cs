using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>
/// Ajuste múltiple AJM-###### (2026-10-12; docs/ajuste-multiple-diseno.md): UNA pasada para poner al día el inventario de un almacén por producto y posición.
/// Cada línea con diferencia genera un movimiento de tipo Ajuste como el ajuste normal (motivo «Inventario físico», nota obligatoria) con este documento como origen
/// (RefEntityType MULTI_ADJUSTMENT). Se aplica todo o nada. Las líneas guardan lo que se vio y lo que se contó, no solo el resultado.
/// </summary>
[AuditEntity(Constants.EntityTypes.MultiAdjustment)]
public class MultiAdjustment : ITenantScoped
{
    public int MultiAdjustmentId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int WarehouseId { get; set; }
    /// <summary>AJM-###### (contador MULTIADJUST por compañía).</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Nota obligatoria (máx. 300) de toda la pasada.</summary>
    public string Notes { get; set; } = string.Empty;
    /// <summary>Líneas con diferencia (cada una es un movimiento).</summary>
    public int LineCount { get; set; }
    /// <summary>Productos distintos de las líneas.</summary>
    public int ProductCount { get; set; }
    /// <summary>Unidades que suben (suma de las diferencias positivas).</summary>
    public decimal UnitsUp { get; set; }
    /// <summary>Unidades que bajan (suma de las diferencias negativas, en positivo).</summary>
    public decimal UnitsDown { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public ICollection<MultiAdjustmentLine> Lines { get; set; } = new List<MultiAdjustmentLine>();
}

/// <summary>Línea de un ajuste múltiple: producto, posición (y lote), la cantidad del sistema al guardar, la contada («hay ahora»), la diferencia y el movimiento que generó.</summary>
[AuditEntity(Constants.EntityTypes.MultiAdjustment)]
public class MultiAdjustmentLine
{
    public long MultiAdjustmentLineId { get; set; }
    public int MultiAdjustmentId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseBinId { get; set; }
    public int? LotId { get; set; }
    /// <summary>Existencia en mano que tenía el sistema al guardar (con los saldos bloqueados).</summary>
    public decimal SystemQty { get; set; }
    /// <summary>Lo que hay ahora, según quien contó.</summary>
    public decimal CountedQty { get; set; }
    /// <summary>CountedQty − SystemQty (con signo): sube o baja.</summary>
    public decimal Difference { get; set; }
    /// <summary>Movimiento del libro que generó esta línea (InventoryTransaction).</summary>
    public long? InventoryTransactionId { get; set; }
}
