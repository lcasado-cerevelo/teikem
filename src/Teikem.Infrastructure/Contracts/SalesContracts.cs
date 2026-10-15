namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 57 — órdenes de venta (módulo SALES_ORDERS). Permisos sales.view / sales.create / sales.cancel / sales.price.
// ======================================================================================================================

/// <summary>Una línea a vender. Con UnitPrice vacío se usa el precio de venta del producto; escribir otro exige sales.price.</summary>
public sealed record SalesOrderLineRequest(Guid? ProductPublicId = null, decimal? Quantity = null, decimal? UnitPrice = null, int? LotId = null);

/// <summary>
/// Orden de venta a crear o a reemplazar (PATCH, solo en borrador). WarehousePublicId vacío = el único almacén activo. ClearConsignee quita el consignatario.
/// </summary>
public sealed record SalesOrderRequest(Guid? WarehousePublicId = null, Guid? ClientPublicId = null, Guid? ConsigneePublicId = null, bool? ClearConsignee = null,
    DateOnly? RequiredDate = null, bool? ClearRequiredDate = null, string? Notes = null, IReadOnlyList<SalesOrderLineRequest>? Lines = null);

public sealed record SalesOrderLineDto(int Id, int LineNo, Guid ProductPublicId, string Sku, string ProductName, int? LotId, decimal QtyOrdered, decimal QtyReserved,
    decimal QtyBackordered, decimal QtyShipped, bool IsClosed, decimal UnitPrice, decimal LineSubtotal, decimal TaxRatePct, decimal TaxAmount, bool IsTaxExempt);

public sealed record SalesOrderListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, decimal Subtotal, decimal TaxAmount, decimal Total,
    int LineCount, bool HasBackorder, string? RequestedByName, DateTime CreatedAtUtc);

public sealed record SalesOrderDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid? ConsigneePublicId, string? ConsigneeName, Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, string? Notes,
    decimal Subtotal, decimal TaxAmount, decimal Total, bool HasBackorder, string? RequestedByName, DateTime? RequestedAtUtc, DateTime CreatedAtUtc,
    IReadOnlyList<SalesOrderLineDto> Lines);

// ---------------------------------------------------------------- Lote 58 — solicitar, disponibilidad, modificar línea

/// <summary>
/// Solicitar la orden (DRAFT → REQUESTED): valida y RESERVA en la misma transacción. AllowBackorder = reservar lo disponible y dejar el resto pendiente
/// (backorder); sin él, un faltante responde 409 y no se reserva nada. OverrideCredit = autorizar un crédito excedido (exige orders.credit_override).
/// </summary>
public sealed record SalesOrderRequestRequest(bool? AllowBackorder = null, bool? OverrideCredit = null, string? Comment = null);

/// <summary>Una línea de la comprobación: pedido, disponible para vender y faltante.</summary>
public sealed record SalesAvailabilityLineDto(int LineNo, Guid ProductPublicId, string Sku, string ProductName, decimal Ordered, decimal Available, decimal Shortage);

/// <summary>Estado del crédito del cliente frente a esta orden (Limit vacío = sin límite).</summary>
public sealed record SalesCreditDto(decimal? Limit, decimal Exposure, decimal NewAmount, decimal? Available, bool Exceeded, string? Message);

/// <summary>Lo que pasaría si se solicitara hoy: por línea pedido / disponible / faltante, y el crédito. No guarda nada.</summary>
public sealed record SalesAvailabilityDto(IReadOnlyList<SalesAvailabilityLineDto> Lines, bool HasShortage, SalesCreditDto Credit);

/// <summary>Modificar una línea de una orden solicitada: cantidad y/o precio (lo que no viene no cambia). AllowBackorder para subir más de lo disponible.</summary>
public sealed record SalesOrderLineModifyRequest(decimal? Quantity = null, decimal? UnitPrice = null, bool? AllowBackorder = null);

// ---------------------------------------------------------------- Lote 59 — backorders: ver, surtir y cerrar con faltante

/// <summary>
/// Una línea con backorder (lo pendiente de una orden solicitada). Pending = lo que falta; Available = lo vendible hoy de ese producto; Fulfillable =
/// lo que se podría reservar ahora para esta línea (el menor de los dos). DaysWaiting = días desde que se solicitó la orden.
/// </summary>
public sealed record SalesBackorderLineDto(Guid OrderPublicId, string OrderNumber, string OrderStatus, string ClientName, string WarehouseName, int LineId, int LineNo,
    Guid ProductPublicId, string Sku, string ProductName, decimal Ordered, decimal Reserved, decimal Pending, decimal Available, decimal Fulfillable, DateTime RequestedAtUtc,
    int DaysWaiting);

/// <summary>Resultado de surtir: por línea, cuánto se reservó ahora y cuánto sigue pendiente.</summary>
public sealed record SalesFulfillLineDto(Guid OrderPublicId, string OrderNumber, int LineId, string Sku, decimal Reserved, decimal StillPending);

public sealed record SalesFulfillResultDto(IReadOnlyList<SalesFulfillLineDto> Lines, decimal TotalReserved, int LinesCompleted);

// ---------------------------------------------------------------- Lote 61 — despachar contra la orden de venta

/// <summary>
/// Una línea a despachar: cuánto (hasta lo reservado). Sin BinId sale de lo que reservó el sistema; con BinId (y LotId si la posición tiene lotes) el operario tomó de otra
/// posición: la reserva se mueve a ella si hay saldo.
/// </summary>
public sealed record SalesDispatchLineRequest(int? LineId = null, decimal? Quantity = null, int? BinId = null, int? LotId = null);

public sealed record SalesDispatchRequest(IReadOnlyList<SalesDispatchLineRequest>? Lines = null, string? Note = null);

/// <summary>La recolección creada (su número EMP) y la orden como quedó.</summary>
public sealed record SalesDispatchResultDto(Guid PickBatchPublicId, string PickBatchNumber, SalesOrderDto Order);
