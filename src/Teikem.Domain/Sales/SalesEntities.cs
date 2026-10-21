using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>Constantes de las órdenes de venta (Lote 57). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class SalesCodes
{
    /// <summary>EntityType de la bitácora y del historial de estatus.</summary>
    public const string EntityType = "SALES_ORDER";
    /// <summary>Dominio de estatus (StatusCode).</summary>
    public const string StatusDomain = "SalesOrderStatus";
    /// <summary>Contador de números (NumberSequence.Kind), por compañía (ClientId NULL).</summary>
    public const string NumberKind = "SALESORDER";
    public const string NumberPattern = "OV-#####";
}

/// <summary>
/// Estatus de una orden de venta. DRAFT = ventas la arma; REQUESTED = solicitada (validada y reservada; el almacén la ve); PARTIAL = se despachó
/// parte; SHIPPED = todo despachado o cerrado; CANCELLED. El backorder NO es un estatus: es la marca de que alguna línea tiene QtyBackordered &gt; 0.
/// </summary>
public static class SalesOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Requested = "REQUESTED";
    public const string Partial = "PARTIAL";
    public const string Shipped = "SHIPPED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Lote 57 — orden de venta (OV-#####): el documento que viaja de Ventas a Almacén. ClientId = el comprador (obligatorio); ConsigneeId solo si habrá
/// entrega. Los totales (Subtotal, TaxAmount) son copia de las líneas al guardar. Nunca DELETE: se cancela.
/// </summary>
[AuditEntity(SalesCodes.EntityType)]
public class SalesOrder : ITenantScoped, IHasStatus, IAuditStamped
{
    public int SalesOrderId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int ClientId { get; set; }
    public int? ConsigneeId { get; set; }
    public int? RequestedBy { get; set; }
    public DateOnly RequestedDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public int? CurrencyLookupId { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? RequestedAtUtc { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    /// <summary>Lote 195: a quién se le asignó despacharla (NULL = sin asignar; lo puede tomar cualquiera con permiso de recolectar).</summary>
    public int? AssignedToUserId { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = new();
}

/// <summary>
/// Línea de una orden de venta. UnitPrice = copia del precio de venta del producto al crear (cambiarlo exige sales.price). El impuesto
/// (TaxRateId, TaxRatePct, TaxAmount, IsTaxExempt) es la copia del cálculo al guardar. QtyReserved + QtyBackordered + lo despachado = lo pedido.
/// </summary>
public class SalesOrderLine : ITenantScoped
{
    public int SalesOrderLineId { get; set; }
    public int TenantId { get; set; }
    public int SalesOrderId { get; set; }
    public int LineNo { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public decimal QtyOrdered { get; set; }
    public decimal QtyReserved { get; set; }
    public decimal QtyBackordered { get; set; }
    public decimal QtyShipped { get; set; }
    public decimal QtyInvoiced { get; set; }
    public decimal QtyReturned { get; set; }
    public decimal QtyCredited { get; set; }
    /// <summary>La línea se dio por terminada con faltante (cerrar con faltante): no espera más inventario.</summary>
    public bool IsClosed { get; set; }
    public decimal UnitPrice { get; set; }
    public int? TaxRateId { get; set; }
    public decimal TaxRatePct { get; set; }
    public decimal TaxAmount { get; set; }
    public bool IsTaxExempt { get; set; }
    /// <summary>Lote 69: si la línea es de un kit, la FOTO de sus componentes al guardar el borrador (cantidad por kit). Vacío = producto normal.</summary>
    public List<SalesOrderLineComponent> Components { get; set; } = new();
}

/// <summary>
/// Lote 69 — componente de un kit en una línea de orden de venta: copia (foto) de lo que llevaba el kit al guardar el borrador, o lo que el vendedor ajustó (si el kit es
/// ajustable al pedir). Un cambio posterior del catálogo no altera una orden ya guardada. Quantity = piezas del componente por cada kit.
/// </summary>
public class SalesOrderLineComponent : ITenantScoped
{
    public int SalesOrderLineComponentId { get; set; }
    public int TenantId { get; set; }
    public int SalesOrderLineId { get; set; }
    public int ComponentProductId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>Qué saldo (almacén, posición, lote) reservó una línea y cuánto: se libera EXACTAMENTE esto al cancelar, modificar o despachar.</summary>
public class SalesOrderReservation : ITenantScoped
{
    public int SalesOrderReservationId { get; set; }
    public int TenantId { get; set; }
    public int SalesOrderLineId { get; set; }
    public int WarehouseId { get; set; }
    public int WarehouseBinId { get; set; }
    public int? LotId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Lote 69: el producto que se reservó. NULL = el de la línea (filas anteriores); en una línea de kit es el componente.</summary>
    public int? ProductId { get; set; }
}
