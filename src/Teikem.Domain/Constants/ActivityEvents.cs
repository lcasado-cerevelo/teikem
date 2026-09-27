namespace Teikem.Domain.Constants;

/// <summary>
/// Lote 7A — códigos del catálogo de eventos de "Actividad reciente" (LookupCode.Entity = 'ActivityEventType'). La bandera
/// de obligatorio, el módulo y el encendido por defecto viven en el ExtraJson de cada fila (seed); qué transición o
/// movimiento produce cada evento vive en el proveedor del módulo (IActivityEventProvider). Maestro, módulo 12,
/// 'Catálogo inicial — Almacén'. Operación y Contabilidad se agregan cuando su módulo llegue al frontend.
/// </summary>
public static class ActivityEvents
{
    // Almacén (BusinessModule WAREHOUSE), en el orden del catálogo del maestro
    public const string ReceiptConfirmed = "RECEIPT_CONFIRMED";
    public const string ReceiptVariance = "RECEIPT_VARIANCE";
    public const string ReceiptPutawayDone = "RECEIPT_PUTAWAY_DONE";
    public const string AsnCancelled = "ASN_CANCELLED";
    public const string PutawayDone = "PUTAWAY_DONE";
    public const string ReplenishDone = "REPLENISH_DONE";
    public const string TaskCancelled = "TASK_CANCELLED";
    public const string CountFinished = "COUNT_FINISHED";
    public const string CountReconciled = "COUNT_RECONCILED";
    public const string CountVariance = "COUNT_VARIANCE";
    public const string InventoryAdjusted = "INVENTORY_ADJUSTED";
    public const string InventoryTransferred = "INVENTORY_TRANSFERRED";
    public const string BinMoved = "BIN_MOVED";
    public const string PickCollected = "PICK_COLLECTED";
    public const string PickPacked = "PICK_PACKED";
    public const string PickCancelled = "PICK_CANCELLED";
    public const string PoSent = "PO_SENT";
    public const string PoReceived = "PO_RECEIVED";
    public const string PoCancelled = "PO_CANCELLED";
    public const string PoShortageResolved = "PO_SHORTAGE_RESOLVED";
    public const string CrossDockCompleted = "CROSSDOCK_COMPLETED";
    public const string ProductDeactivated = "PRODUCT_DEACTIVATED";
    public const string WarehouseDeactivated = "WAREHOUSE_DEACTIVATED";

    /// <summary>Los 23 eventos de Almacén sembrados en este lote, en el orden del catálogo (SortOrder 1..23).</summary>
    public static readonly IReadOnlyList<string> Warehouse = new[]
    {
        ReceiptConfirmed, ReceiptVariance, ReceiptPutawayDone, AsnCancelled, PutawayDone, ReplenishDone, TaskCancelled,
        CountFinished, CountReconciled, CountVariance, InventoryAdjusted, InventoryTransferred, BinMoved, PickCollected,
        PickPacked, PickCancelled, PoSent, PoReceived, PoCancelled, PoShortageResolved, CrossDockCompleted, ProductDeactivated,
        WarehouseDeactivated,
    };
}
