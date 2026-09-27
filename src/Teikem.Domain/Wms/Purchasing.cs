using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Wms;

/// <summary>Lote 6 — Proveedor (compras de inventario propio). Nombre único entre activos (UX_Supplier_Name).</summary>
[AuditEntity(Constants.EntityTypes.Supplier)]
public class Supplier : ITenantScoped, ISoftDeletable
{
    public int SupplierId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int? PaymentTermLookupId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Orden de compra PO-##### (D9): DRAFT → SENT → PARTIAL → RECEIVED; CANCELLED desde DRAFT/SENT/PARTIAL (D47). La edición se
/// controla con la capacidad EDIT_PURCHASE_ORDER (D46). Solo productos propios, sin repetir.
/// </summary>
[AuditEntity(Constants.EntityTypes.PurchaseOrder)]
public class PurchaseOrder : ITenantScoped, ISoftDeletable, IHasStatus
{
    public int PurchaseOrderId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public int StatusCodeId { get; set; }
    public int? CurrencyLookupId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public StatusCode? Status { get; set; }
    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
}

/// <summary>
/// Línea de la PO. UnitCost congelado (D35). LineTotal es columna computada de SQL: se mapea pero NUNCA se usa en lógica
/// (InMemory no la calcula). Sin TenantId: se alcanza por su PO filtrada.
/// </summary>
[AuditEntity(Constants.EntityTypes.PurchaseOrder)]
public class PurchaseOrderLine
{
    public int PurchaseOrderLineId { get; set; }
    public int PurchaseOrderId { get; set; }
    public int ProductId { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal QtyReceived { get; set; }
    public decimal UnitCost { get; set; }
    /// <summary>Computada en SQL (QtyOrdered × UnitCost). No usar en lógica.</summary>
    public decimal LineTotal { get; private set; }
}

/// <summary>
/// Resolución de un faltante de línea de PO (D8): CLOSE, REORDER (con la PO nueva) o MANUAL_ADJUSTMENT (con su movimiento
/// del ledger). Pantalla 'Ajustes de inventario' (bitácora L762).
/// </summary>
[AuditEntity(Constants.EntityTypes.PurchaseOrder)]
public class PurchaseOrderShortageResolution : ITenantScoped
{
    public int PurchaseOrderShortageResolutionId { get; set; }
    public int TenantId { get; set; }
    public int PurchaseOrderId { get; set; }
    public int PurchaseOrderLineId { get; set; }
    public int ActionLookupId { get; set; }
    public decimal Quantity { get; set; }
    public int? ReasonLookupId { get; set; }
    public string? Notes { get; set; }
    public int? ReorderPurchaseOrderId { get; set; }
    public long? InventoryTransactionId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
}
