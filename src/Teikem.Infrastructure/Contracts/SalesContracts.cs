namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 57 — órdenes de venta (módulo SALES_ORDERS). Permisos sales.view / sales.create / sales.cancel / sales.price.
// ======================================================================================================================

/// <summary>Una línea a vender. Con UnitPrice vacío se usa el precio de venta del producto; escribir otro exige sales.price.</summary>
/// <summary>Lote 69: Components solo para un kit «ajustable al pedir»: las piezas de ESTA línea (si no vienen, las del catálogo).</summary>
public sealed record SalesOrderLineRequest(Guid? ProductPublicId = null, decimal? Quantity = null, decimal? UnitPrice = null, int? LotId = null,
    IReadOnlyList<KitComponentRequest>? Components = null);

/// <summary>
/// Orden de venta a crear o a reemplazar (PATCH, solo en borrador). WarehousePublicId vacío = el único almacén activo. ClearConsignee quita el consignatario.
/// </summary>
/// <remarks>
/// Lote 222 (null = sin cambio en el PATCH): IsUrgent; DeliveryMode DELIVERY | PICKUP ("" = se deduce del consignatario); PaymentMethod (COD_CHECK, COD_CASH, COD_ACH, CREDIT,
/// CONSIGNMENT; "" = quitarla) y PaymentTerm (término del crédito, solo con CREDIT; "" = quitarlo): sin forma de pago al crear se prellena con la del cliente y, si es otra,
/// la orden queda marcada «cambió»; IsConsignment solo con CONSIGNMENT; AuthorizedByName = nombre de quien autoriza ("" = quitarlo).
/// </remarks>
public sealed record SalesOrderRequest(Guid? WarehousePublicId = null, Guid? ClientPublicId = null, Guid? ConsigneePublicId = null, bool? ClearConsignee = null,
    DateOnly? RequiredDate = null, bool? ClearRequiredDate = null, string? Notes = null, IReadOnlyList<SalesOrderLineRequest>? Lines = null,
    bool? IsUrgent = null, string? DeliveryMode = null, string? PaymentMethod = null, string? PaymentTerm = null, bool? IsConsignment = null, string? AuthorizedByName = null);

public sealed record SalesOrderLineDto(int Id, int LineNo, Guid ProductPublicId, string Sku, string ProductName, int? LotId, decimal QtyOrdered, decimal QtyReserved,
    decimal QtyBackordered, decimal QtyShipped, bool IsClosed, decimal UnitPrice, decimal LineSubtotal, decimal TaxRatePct, decimal TaxAmount, bool IsTaxExempt,
    bool IsKit = false, IReadOnlyList<SalesOrderLineComponentDto>? Components = null);

/// <summary>Lote 69: un componente de la línea de un kit. Las cantidades son de piezas del componente (cantidad por kit × kits): pedido, reservado, en backorder y despachado.</summary>
public sealed record SalesOrderLineComponentDto(Guid ProductPublicId, string Sku, string Name, decimal QtyPerKit, decimal Needed, decimal Reserved, decimal Backordered, decimal Shipped);

public sealed record SalesOrderListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, decimal Subtotal, decimal TaxAmount, decimal Total,
    int LineCount, bool HasBackorder, string? RequestedByName, DateTime CreatedAtUtc, int? AssignedToUserId = null, string? AssignedToName = null,
    // Lote 222: distintivos de la lista (urgente, cliente nuevo por aprobar, consignación, forma de pago cambiada), entrega efectiva y cargo de envío
    bool IsUrgent = false, bool AwaitingClientApproval = false, bool IsConsignment = false, bool PaymentChanged = false, string? DeliveryMode = null, decimal ShippingCharge = 0m);

public sealed record SalesOrderDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid? ConsigneePublicId, string? ConsigneeName, Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, string? Notes,
    decimal Subtotal, decimal TaxAmount, decimal Total, bool HasBackorder, string? RequestedByName, DateTime? RequestedAtUtc, DateTime CreatedAtUtc,
    IReadOnlyList<SalesOrderLineDto> Lines, IReadOnlyList<SalesDispatchInfoDto>? Dispatches = null, int? AssignedToUserId = null, string? AssignedToName = null,
    SalesOrderTermsDto? Terms = null);

/// <summary>
/// Lote 222 — los datos nuevos de la orden (pedido de vendedores). DeliveryMode = la entrega efectiva (DELIVERY | PICKUP) y DeliveryModeSet = si la orden la dice (o se dedujo
/// del consignatario). Forma de pago con su etiqueta y la marca «cambió respecto al cliente». Envío: cargo, su IVU y el porqué (ShippingReason null = sin cargo configurado).
/// ClientIsProspect = el comprador sigue como prospecto (distintivo «Cliente nuevo»); AwaitingClientApproval = no se puede despachar hasta que lo aprueben.
/// La firma de quien autoriza se ve por GET /files/{SignatureFilePublicId}.
/// </summary>
public sealed record SalesOrderTermsDto(bool IsUrgent, string DeliveryMode, bool DeliveryModeSet, string? PaymentMethod, string? PaymentMethodLabel, string? PaymentTerm,
    string? PaymentTermLabel, bool PaymentChanged, decimal ShippingCharge, decimal ShippingTaxAmount, string? ShippingReason, string? ShippingReasonLabel, bool IsConsignment,
    string? AuthorizedByName, Guid? SignatureFilePublicId, string? SubmittedWithAuth, bool AwaitingClientApproval, bool ClientIsProspect, bool ClientRequiresSignature);

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

/// <summary>
/// Lote 64: con Delivery (los datos de la entrega, como el empaque) el despacho y la entrega ocurren en UNA sola transacción: es lo que manda el aparato, que despacha sin
/// señal y no conoce el número de la recolección hasta que sincroniza.
/// </summary>
public sealed record SalesDispatchRequest(IReadOnlyList<SalesDispatchLineRequest>? Lines = null, string? Note = null, OrderCreateRequest? Delivery = null);

/// <summary>La recolección creada (su número EMP) y la orden como quedó.</summary>
public sealed record SalesDispatchResultDto(Guid PickBatchPublicId, string PickBatchNumber, SalesOrderDto Order);

// ---------------------------------------------------------------- Lote 62 — entrega y despachos de la orden

/// <summary>
/// Crea la entrega de un despacho de la orden de venta: la orden de transporte (como el empaque). El cliente y el consignatario se toman de la orden de venta si no se
/// mandan. Order lleva el servicio, los paquetes, el COD, etc.
/// </summary>
public sealed record SalesDeliveryRequest(Guid? PickBatchPublicId = null, OrderCreateRequest? Order = null);

/// <summary>Un despacho de la orden (su recolección): Status = COLLECTED (despachado, sin entrega), PACKED (con entrega) o CANCELLED (deshecho).</summary>
public sealed record SalesDispatchInfoDto(Guid PickBatchPublicId, string Number, string Status, DateTime CollectedAtUtc, decimal Quantity, Guid? TransportOrderPublicId, string? TransportOrderNumber);
