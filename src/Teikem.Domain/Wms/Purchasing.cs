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
    /// <summary>Teléfono con SOLO dígitos (sin formato ni prefijo; el formato lo pone la máscara de la compañía). Ver PhoneRules.</summary>
    public string? Phone { get; set; }
    /// <summary>Prefijo internacional de `Phone`, solo dígitos y sin «+»; se llena siempre que haya teléfono (el de la compañía si no se indicó otro).</summary>
    public string? PhoneCountryCallingCode { get; set; }
    public string? Email { get; set; }
    public int? PaymentTermLookupId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }
    /// <summary>Lote 246 (Lote F): cuándo (UTC) quedó el proveedor como ya mandado a QuickBooks (al confirmar o marcar una orden suya, o al importar de QuickBooks con el nombre
    /// exacto). NULL = nuevo para QuickBooks: sale en el bloque !VEND del archivo si se piden los nuevos.</summary>
    public DateTime? QuickBooksSentAt { get; set; }
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
    /// <summary>Lote 195: a quién se le asignó recibirla (NULL = sin asignar; lo puede tomar cualquiera con permiso de recibir).</summary>
    public int? AssignedToUserId { get; set; }

    // --- Lote 246 (Lote F del plan de Solutions): origen, identidad de importación y QuickBooks ---
    /// <summary>De dónde nació la orden (LookupCode PurchaseOrderOrigin: TEIKEM, EXCEL o QUICKBOOKS). NULL se lee como TEIKEM (órdenes de otros caminos o de pruebas).</summary>
    public int? OriginLookupId { get; set; }
    /// <summary>Identidad de la importación («QB:PROVEEDOR|NÚMERO» / «IMP:…», la marca de las notas sin corchetes). Única entre las órdenes ACTIVAS de la compañía
    /// (UX_PurchaseOrder_ImportKey): importar dos veces el mismo documento no la duplica aunque se borre la marca de las notas. NULL = no importada.</summary>
    public string? ImportKey { get; set; }
    /// <summary>La marca «en QuickBooks»: cuándo (UTC) y quién la puso (a mano o al confirmar un archivo, con purchasing.quickbooks). NULL = no está marcada.</summary>
    public DateTime? QuickBooksAt { get; set; }
    public int? QuickBooksBy { get; set; }
    /// <summary>La última vez (UTC) que la orden salió en un archivo IIF y quién lo sacó. No la marca: queda «exportada, por confirmar» hasta «Sí, ya están» o «No pasó».</summary>
    public DateTime? QuickBooksExportedAt { get; set; }
    public int? QuickBooksExportedBy { get; set; }

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
