namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 57 — órdenes de venta (módulo SALES_ORDERS). Permisos sales.view / sales.create / sales.cancel / sales.price.
// ======================================================================================================================

/// <summary>Una línea a vender. Con UnitPrice vacío se usa el precio de venta del producto; escribir otro exige sales.price.</summary>
/// <summary>Lote 69: Components solo para un kit «ajustable al pedir»: las piezas de ESTA línea (si no vienen, las del catálogo).</summary>
/// <remarks>
/// Lote 230: también en un kit fijo, para SUSTITUIR piezas: se mandan todas las piezas del kit y la que cambia lleva substitutesProductPublicId = la pieza original
/// (misma categoría exacta, mismo precio de venta, misma cantidad; la pieza no debe estar marcada «no sustituible»). El precio de la línea no cambia.
/// </remarks>
public sealed record SalesOrderLineRequest(Guid? ProductPublicId = null, decimal? Quantity = null, decimal? UnitPrice = null, int? LotId = null,
    IReadOnlyList<KitComponentRequest>? Components = null);

/// <summary>
/// Orden de venta a crear o a reemplazar (PATCH, solo en borrador). WarehousePublicId vacío = el único almacén activo. ClearConsignee quita el consignatario.
/// </summary>
/// <remarks>
/// Lote 222 (null = sin cambio en el PATCH): IsUrgent; DeliveryMode DELIVERY | PICKUP ("" = se deduce del consignatario); PaymentMethod (COD_CHECK, COD_CASH, COD_ACH, CREDIT,
/// CONSIGNMENT; "" = quitarla) y PaymentTerm (término del crédito, solo con CREDIT; "" = quitarlo): sin forma de pago al crear se prellena con la del cliente y, si es otra,
/// la orden queda marcada «cambió»; IsConsignment solo con CONSIGNMENT; AuthorizedByName = nombre de quien autoriza ("" = quitarlo).
/// Lote 223 (pedido guiado): Step = el paso que se guarda (CLIENT, PRODUCTS, DELIVERY, AUTHORIZATION; vacío = guardado normal). Con CLIENT el borrador puede no llevar
/// productos (Lines vacío o ausente); desde PRODUCTS la orden necesita al menos uno; en DELIVERY y AUTHORIZATION un pedido marcado DELIVERY necesita consignatario.
/// </remarks>
public sealed record SalesOrderRequest(Guid? WarehousePublicId = null, Guid? ClientPublicId = null, Guid? ConsigneePublicId = null, bool? ClearConsignee = null,
    DateOnly? RequiredDate = null, bool? ClearRequiredDate = null, string? Notes = null, IReadOnlyList<SalesOrderLineRequest>? Lines = null,
    bool? IsUrgent = null, string? DeliveryMode = null, string? PaymentMethod = null, string? PaymentTerm = null, bool? IsConsignment = null, string? AuthorizedByName = null,
    string? Step = null,
    // Lote 244: el vendedor escogido en el BORRADOR (el preferido del cliente o una persona vigente de su ruta; 400 si no). ClearSeller = volver a la propuesta.
    int? SellerUserId = null, bool? ClearSeller = null);

/// <remarks>
/// Lote 247: el ORIGEN guardado del precio de la línea (DECISIÓN 12): PriceSource (CLIENT_LIST, CHAIN_LIST, OFFER, GENERAL_LIST, PRODUCT, NONE; null = línea de antes
/// del lote), la lista, la oferta y la cadena (nombres leídos al mostrar), ResolvedUnitPrice = el precio que daba la resolución e IsManualPrice = UnitPrice distinto
/// de ese precio (precio escrito con sales.price).
/// </remarks>
public sealed record SalesOrderLineDto(int Id, int LineNo, Guid ProductPublicId, string Sku, string ProductName, int? LotId, decimal QtyOrdered, decimal QtyReserved,
    decimal QtyBackordered, decimal QtyShipped, bool IsClosed, decimal UnitPrice, decimal LineSubtotal, decimal TaxRatePct, decimal TaxAmount, bool IsTaxExempt,
    bool IsKit = false, IReadOnlyList<SalesOrderLineComponentDto>? Components = null,
    string? PriceSource = null, Guid? PriceListPublicId = null, string? PriceListName = null, Guid? OfferPublicId = null, string? OfferName = null,
    Guid? PriceChainPublicId = null, string? PriceChainName = null, decimal? ResolvedUnitPrice = null, bool IsManualPrice = false,
    // Lote 249: línea QUITADA por una modificación (pedido 0 y cerrada; nunca se borra) y el IVU: TaxReason (la razón del impuesto; MANUAL_OVERRIDE = IVU manual),
    // el motivo del IVU manual y lo que daría el cálculo de hoy (TaxComputedRatePct / TaxComputedAmount; solo con IVU manual)
    bool IsRemoved = false, string? TaxReason = null, string? TaxOverrideNote = null, decimal? TaxComputedRatePct = null, decimal? TaxComputedAmount = null);

/// <summary>Lote 69: un componente de la línea de un kit. Las cantidades son de piezas del componente (cantidad por kit × kits): pedido, reservado, en backorder y despachado.</summary>
/// <remarks>Lote 230: el producto es la pieza REAL; si sustituye a una pieza del kit, SubstitutesProductPublicId / SubstitutesSku / SubstitutesName dicen cuál (para mostrar «en lugar de»).</remarks>
public sealed record SalesOrderLineComponentDto(Guid ProductPublicId, string Sku, string Name, decimal QtyPerKit, decimal Needed, decimal Reserved, decimal Backordered, decimal Shipped,
    Guid? SubstitutesProductPublicId = null, string? SubstitutesSku = null, string? SubstitutesName = null);

public sealed record SalesOrderListItemDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, decimal Subtotal, decimal TaxAmount, decimal Total,
    int LineCount, bool HasBackorder, string? RequestedByName, DateTime CreatedAtUtc, int? AssignedToUserId = null, string? AssignedToName = null,
    // Lote 222: distintivos de la lista (urgente, cliente nuevo por aprobar, consignación, forma de pago cambiada), entrega efectiva y cargo de envío
    bool IsUrgent = false, bool AwaitingClientApproval = false, bool IsConsignment = false, bool PaymentChanged = false, string? DeliveryMode = null, decimal ShippingCharge = 0m,
    // Lote 244: ruta de venta y vendedor guardados en la orden (la copia congelada al solicitar; en borrador, la elección explícita y sin ruta)
    string? SalesRouteName = null, string? SellerName = null,
    // Lote 245: cadena ACTUAL del cliente (en vivo, sin copia en la orden; null = «Sin cadena»)
    string? ChainName = null, Guid? ChainPublicId = null,
    // Lote 249: la orden es una ADICIÓN (orden interna que complementa otra) y tiene una modificación esperando la segunda aprobación. LineCount no cuenta las quitadas.
    bool IsAddition = false, bool HasPendingChange = false);

public sealed record SalesOrderDto(Guid PublicId, string Number, string Status, string StatusLabel, string StatusColor, Guid ClientPublicId, string ClientName,
    Guid? ConsigneePublicId, string? ConsigneeName, Guid WarehousePublicId, string WarehouseName, DateOnly RequestedDate, DateOnly? RequiredDate, string? Notes,
    decimal Subtotal, decimal TaxAmount, decimal Total, bool HasBackorder, string? RequestedByName, DateTime? RequestedAtUtc, DateTime CreatedAtUtc,
    IReadOnlyList<SalesOrderLineDto> Lines, IReadOnlyList<SalesDispatchInfoDto>? Dispatches = null, int? AssignedToUserId = null, string? AssignedToName = null,
    SalesOrderTermsDto? Terms = null, IReadOnlyList<SalesLinePriceDto>? LinePrices = null,
    // Lote 244: ruta de venta y vendedor. Solicitada: la copia congelada (SalesCopyAtUtc puesto; NULL = «Sin vendedor» / «Sin ruta»). Borrador: la ruta efectiva
    // del cliente hoy y la elección del vendedor o, sin elección válida, el vendedor PROPUESTO (SellerProposed = true). Órdenes anteriores al lote: todo NULL.
    int? SalesRouteId = null, string? SalesRouteName = null, int? SellerUserId = null, string? SellerName = null, bool SellerProposed = false, DateTime? SalesCopyAtUtc = null,
    // Lote 249: la orden de la que esta es la adición (null = orden normal), sus adiciones, la modificación pendiente de aprobar (si hay), cuántas modificaciones
    // tiene y el RowVersion (base64) que manda la pantalla al modificar (409 si la orden cambió mientras tanto).
    Guid? ParentPublicId = null, string? ParentNumber = null, IReadOnlyList<SalesOrderAdditionRefDto>? Additions = null, Guid? PendingChangePublicId = null,
    int? PendingChangeNo = null, int ChangeCount = 0, string? RowVersion = null);

/// <summary>Lote 249 — una orden interna de adición de la orden: su número, su estatus (con etiqueta) y su total con el envío.</summary>
public sealed record SalesOrderAdditionRefDto(Guid PublicId, string Number, string Status, string StatusLabel, decimal Total);

/// <summary>
/// Lote 244 — una opción de vendedor para el borrador: el usuario (con nombre, sin correo), de dónde sale (PRIMARY = titular de la ruta, SUPPORT = apoyo,
/// PREFERRED = el preferido del cliente cuando no es persona de la ruta), si es el preferido del cliente y si es el propuesto.
/// </summary>
public sealed record SalesSellerOptionDto(int UserId, string Name, string Kind, bool IsPreferred, bool IsProposed);

/// <summary>
/// Lote 244 — GET /sales-orders/seller-options?client=: la ruta de venta efectiva del cliente (RouteFromRep = heredada de su representante), el vendedor
/// propuesto (ProposedKind = PREFERRED | PRIMARY | SUPPORT; NULL = «Sin vendedor») y las opciones que se pueden escoger (solo personas activas; primero las de la ruta).
/// </summary>
public sealed record SalesSellerOptionsDto(int? RouteId, string? RouteName, bool RouteFromRep, int? ProposedUserId, string? ProposedName, string? ProposedKind,
    IReadOnlyList<SalesSellerOptionDto> Options);

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

/// <summary>
/// Lote 223 — «Enviar pedido» del pedido guiado: reautenticación del vendedor y, en seguida, solicitar (lo mismo que POST /request: valida inventario y crédito y reserva).
/// AuthMethod = PASSWORD (Password del propio usuario y MfaCode si tiene MFA; la web) o BIOMETRIC (la app, después de la huella o el rostro: exige una verificación reciente
/// de la sesión). AllowBackorder / OverrideCredit / Comment, como en solicitar. Queda en SalesOrder.SubmittedWithAuth.
/// </summary>
public sealed record SalesOrderSubmitRequest(string? AuthMethod = null, string? Password = null, string? MfaCode = null, bool? AllowBackorder = null,
    bool? OverrideCredit = null, string? Comment = null);

/// <summary>Una línea de la comprobación: pedido, disponible para vender y faltante.</summary>
public sealed record SalesAvailabilityLineDto(int LineNo, Guid ProductPublicId, string Sku, string ProductName, decimal Ordered, decimal Available, decimal Shortage);

/// <summary>Estado del crédito del cliente frente a esta orden (Limit vacío = sin límite).</summary>
public sealed record SalesCreditDto(decimal? Limit, decimal Exposure, decimal NewAmount, decimal? Available, bool Exceeded, string? Message);

/// <summary>Lo que pasaría si se solicitara hoy: por línea pedido / disponible / faltante, y el crédito. No guarda nada.</summary>
public sealed record SalesAvailabilityDto(IReadOnlyList<SalesAvailabilityLineDto> Lines, bool HasShortage, SalesCreditDto Credit);

/// <summary>Modificar una línea de una orden solicitada: cantidad y/o precio (lo que no viene no cambia). AllowBackorder para subir más de lo disponible.</summary>
/// <remarks>
/// Lote 249: la ruta PATCH lines/{lineId} se volvió un adaptador de la «Modificación de la orden» (exige sales.modify): quién autorizó en el cliente (nombre y cargo),
/// cómo (PHONE | EMAIL | IN_PERSON | MESSAGE), cuándo (vacío = ahora) y el motivo. Sin ellos, 400 con los mensajes de la modificación.
/// </remarks>
public sealed record SalesOrderLineModifyRequest(decimal? Quantity = null, decimal? UnitPrice = null, bool? AllowBackorder = null,
    string? AuthorizedByName = null, string? AuthorizedByTitle = null, string? AuthorizationChannel = null, DateTime? AuthorizedAtUtc = null, string? Reason = null);

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

/// <summary>
/// Un despacho de la orden (su recolección): Status = COLLECTED (despachado, sin entrega), PACKED (con entrega) o CANCELLED (deshecho). Lote 227: LinkedDelivery = la entrega
/// hecha por la compañía enlazada (su orden allá, estatus y entrega); en ese caso no hay orden de transporte aquí (TransportOrder* en null).
/// </summary>
public sealed record SalesDispatchInfoDto(Guid PickBatchPublicId, string Number, string Status, DateTime CollectedAtUtc, decimal Quantity, Guid? TransportOrderPublicId, string? TransportOrderNumber,
    TenantLinkDeliveryDto? LinkedDelivery = null);
