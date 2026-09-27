using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Wms;

// Lote 6 (P0) — tipos de costura internos (no HTTP): asientos del ledger, reservas, tareas, sugerencias de putaway y las
// cuatro interfaces entre piezas (IWarehouseTaskHandler, IPurchaseOrderReceiving, IReceiptConfirmationParticipant,
// IOrderInventoryLines). Viven fuera de Contracts: nunca se deserializan desde un request.

/// <summary>
/// Asiento del ledger. Quantity es la MAGNITUD (&gt; 0): el ledger guarda el signo según la dirección (D3). Ref y motivo se
/// escriben TAL COMO LLEGAN en el INSERT (no hay UPDATE posterior, D48). FromReserved = la salida consume lo reservado
/// (cross-dock: baja reservado y en mano juntos).
/// </summary>
public sealed record InventoryPosting(
    string TxnType,
    int ProductId,
    decimal Quantity,
    int? LotId = null,
    int? SerialId = null,
    string? SerialNumber = null,
    int? FromWarehouseId = null,
    int? FromBinId = null,
    int? ToWarehouseId = null,
    int? ToBinId = null,
    string? RefEntityType = null,
    int? RefId = null,
    string? ReasonCode = null,
    string? Notes = null,
    bool FromReserved = false);

/// <summary>
/// Reserva (o liberación) de saldo en una posición: no escribe movimiento, solo QtyReserved. En productos con serie se
/// nombran las series (tantas como Quantity): reservar las pasa AVAILABLE → RESERVED y liberar RESERVED → AVAILABLE
/// (maestro L328, D16); una salida con FromReserved exige la serie RESERVED.
/// </summary>
public sealed record StockReservation(int ProductId, int WarehouseId, int BinId, int? LotId, decimal Quantity,
    IReadOnlyList<string>? SerialNumbers = null);

/// <summary>Descuadre ledger ↔ saldo (InventoryLedger.ReconcileAsync). Key.WarehouseId = 0 en la fila por producto.</summary>
public sealed record ReconciliationRow(BalanceKey Key, decimal LedgerQty, decimal BalanceQty, bool ProductTotal);

/// <summary>Alta de una tarea de almacén (WarehouseTaskWriter.CreateAsync): nace PENDING con historial null → PENDING.</summary>
public sealed record WarehouseTaskSpec(
    string TaskType,
    int WarehouseId,
    int? ProductId = null,
    decimal? Quantity = null,
    int? LotId = null,
    int? FromBinId = null,
    int? ToBinId = null,
    string? RefEntityType = null,
    int? RefId = null,
    int? SerialId = null,
    int Priority = 100,
    int? AssignedToUserId = null);

/// <summary>Posición sugerida para un putaway (PutawaySuggester), con la rotación con que se calculó (D24).</summary>
public sealed record PutawaySuggestion(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason,
    string RotationClass);

/// <summary>
/// Handler de un tipo de tarea de la cola unificada (D41). Cada tipo declara su permiso y si se completa desde la cola.
/// WarehouseTaskService resuelve el handler por TaskType; un tipo sin handler responde 422.
/// </summary>
public interface IWarehouseTaskHandler
{
    /// <summary>Código WarehouseTaskType (PUTAWAY, REPLENISH, COUNT, CROSSDOCK...).</summary>
    string TaskType { get; }

    /// <summary>Permiso requerido para iniciar y completar la tarea.</summary>
    string RequiredPermission { get; }

    /// <summary>Mensaje del 422 si la tarea NO se completa desde la cola (COUNT); null si sí.</summary>
    string? NotFromQueueMessage { get; }

    /// <summary>
    /// Bloquea los encabezados que la tarea referencia ANTES de bloquear la tarea (orden de bloqueo del lote): PUTAWAY su
    /// recibo; CROSSDOCK su plan y su recibo.
    /// </summary>
    Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct);

    /// <summary>
    /// Hace el movimiento y los efectos de la tarea bloqueada y devuelve la cantidad completada. WarehouseTaskService hace el
    /// split del remanente y el DONE.
    /// </summary>
    Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct);
}

/// <summary>Línea de PO con pendiente de recibir.</summary>
public sealed record PurchaseOrderPendingLine(int PurchaseOrderLineId, int ProductId, decimal QtyPending, decimal UnitCost);

/// <summary>PO bloqueada y validada para recibir contra ella.</summary>
public sealed record PurchaseOrderForReceipt(int PurchaseOrderId, string Number, int WarehouseId, IReadOnlyList<PurchaseOrderPendingLine> Lines);

/// <summary>Cantidad recibida por línea de PO al confirmar un recibo.</summary>
public sealed record PurchaseOrderReceiptQty(int PurchaseOrderLineId, decimal Quantity);

/// <summary>
/// Costura Compras → Recepción (P8 la implementa, P4 la consume). Orden de bloqueo: ReceiptHeader &lt; Asn &lt; PurchaseOrder.
/// </summary>
public interface IPurchaseOrderReceiving
{
    /// <summary>Bloquea la PO, valida IsReceivable (422) y NothingPending (422) y devuelve las líneas con pendiente.</summary>
    Task<PurchaseOrderForReceipt> LockForReceiptAsync(Guid purchaseOrderPublicId, CancellationToken ct);

    /// <summary>Bloquea la PO, suma QtyReceived por línea y transiciona a PARTIAL o RECEIVED.</summary>
    Task ApplyReceiptAsync(int purchaseOrderId, IReadOnlyList<PurchaseOrderReceiptQty> quantities, CancellationToken ct);
}

/// <summary>
/// Participante de la confirmación de un recibo (P9 lo implementa, P4 lo consume). Corre dentro de la transacción de
/// confirmación con el recibo ya bloqueado, después del ledger y antes de las PUTAWAY. Devuelve la cantidad por
/// ReceiptLineId que se va a cruce de muelle (vacío = nada).
/// </summary>
public interface IReceiptConfirmationParticipant
{
    Task<IReadOnlyDictionary<int, decimal>> OnReceiptConfirmedAsync(ReceiptHeader receipt, IReadOnlyList<ReceiptLine> lines, CancellationToken ct);
}

/// <summary>Línea de producto de una orden (desde PickBatchLine; D45): costo congelado y precio de venta vigente.</summary>
public sealed record OrderInventoryLine(int TransportOrderId, int PickBatchId, string PickBatchNumber, int ProductId, Guid ProductPublicId,
    string Sku, string ProductName, int? LotId, string? LotNumber, int? SerialId, string? SerialNumber, decimal Quantity, decimal? UnitCost,
    decimal? SalePrice);

/// <summary>Líneas de producto por orden (P7 lo implementa; insumo de PRODUCT_SALE y Contabilización del Lote 10).</summary>
public interface IOrderInventoryLines
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<OrderInventoryLine>>> GetAsync(IReadOnlyCollection<int> transportOrderIds, CancellationToken ct);
}
