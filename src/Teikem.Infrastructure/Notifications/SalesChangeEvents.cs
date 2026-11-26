using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Lote 249 (Lote E del plan) — códigos de los eventos de aviso de las modificaciones de órdenes de venta. Con el prefijo SALES_ caen en el grupo «Ventas» de Reglas
/// de aviso (<see cref="NotificationEventCategories.Of"/>). Hasta 60 caracteres (columna EventCode).
/// </summary>
public static class SalesChangeEventCodes
{
    /// <summary>Se aplicó una modificación a una orden de venta (al registrarla o al aprobarla).</summary>
    public const string OrderChanged = "SALES_ORDER_CHANGED";
    /// <summary>Se creó la ADICIÓN (orden complementaria) de una orden que ya salió.</summary>
    public const string AdditionCreated = "SALES_ORDER_ADDITION_CREATED";
    /// <summary>Se reabrió un empaque (PACKED → COLLECTED) y se dio de baja su envío en la etapa inicial.</summary>
    public const string PackReopened = "SALES_PACK_REOPENED";
    /// <summary>Una modificación quedó pendiente de la segunda aprobación (la atiende quien tiene sales.modify.approve).</summary>
    public const string ApprovalPending = "SALES_CHANGE_APPROVAL_PENDING";

    public static readonly string[] All = { OrderChanged, AdditionCreated, PackReopened, ApprovalPending };
}

/// <summary>Lote 249 — claves de los destinatarios del evento (Kind CONTEXT) de los avisos de las modificaciones. Nunca van al cliente.</summary>
public static class SalesChangeContextRecipients
{
    /// <summary>«Vendedor de la orden»: el vendedor CONGELADO de la orden al solicitarla (Lote 244, SalesOrderSeller).</summary>
    public const string Seller = "SELLER";
    /// <summary>«Quien registró la modificación» (SalesOrderChange.RegisteredBy). En «Orden de venta modificada» solo cuando se aplicó al APROBARLA.</summary>
    public const string Registrant = "REGISTRANT";
    /// <summary>«Encargado del almacén de la orden» (SalesOrder.AssignedToUserId).</summary>
    public const string Assignee = "ASSIGNEE";
}

/// <summary>
/// Lote 249 — los eventos de las modificaciones de órdenes de venta. Se publican desde SalesOrderChangeService y PickBatchService (en la misma transacción de la
/// operación); el contenido y los destinatarios del evento los arma SalesChangeNoticeContent al enviar. «Modificación pendiente de aprobar» es una petición (no se
/// puede silenciar) y la reciben quienes el administrador ponga en sus reglas (por permiso sales.modify.approve); los demás son opcionales. Las reglas las arma el
/// administrador en Sistema → Reglas de aviso: no se siembra ninguna. El enlace abre la orden en Órdenes de venta.
/// </summary>
public sealed class SalesChangeEventProvider : INotificationEventProvider
{
    public const string LinkTemplate = "/sales-orders?order={orderPublicId}";

    private static NotificationVariable[] Base(params NotificationVariable[] extra) => new[]
    {
        new NotificationVariable("orderNumber", "Número de la orden", "Order number", "OV-00040"),
        new NotificationVariable("client", "Cliente", "Customer", "Farmacia Central"),
        new NotificationVariable("warehouse", "Almacén", "Warehouse", "Almacén principal"),
        new NotificationVariable("orderPublicId", "Identificador de la orden", "Order identifier", "0f8fad5bd9cb469fa16570867728950e"),
    }.Concat(extra).ToArray();

    private static NotificationVariable[] Change(params NotificationVariable[] extra) => Base(new[]
    {
        new NotificationVariable("changeNo", "Número de la modificación", "Change number", "2"),
        new NotificationVariable("registeredBy", "Quién la registró", "Registered by", "Ana Pérez"),
        new NotificationVariable("authorizedBy", "Quién autorizó del cliente", "Authorized by (customer)", "María López (compras)"),
        new NotificationVariable("channel", "Cómo autorizó", "How it was authorized", "Por teléfono"),
        new NotificationVariable("reason", "Motivo", "Reason", "El cliente pidió 5 cajas más."),
        new NotificationVariable("summary", "Resumen del cambio", "Change summary", "+1 línea; MED-10: 10 → 15"),
        new NotificationVariable("totalBefore", "Total antes", "Total before", "$1,200.00"),
        new NotificationVariable("totalAfter", "Total después", "Total after", "$1,450.00"),
        new NotificationVariable("totalDelta", "Diferencia", "Difference", "+$250.00"),
    }.Concat(extra).ToArray());

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            SalesChangeEventCodes.OrderChanged, ModuleKeys.SalesOrders, "Orden de venta modificada", "Sales order changed",
            Change(new NotificationVariable("approvedBy", "Quién la aprobó (si pidió aprobación)", "Approved by (if it needed approval)", "Luis Rivera")),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Orden {orderNumber} modificada (n.º {changeNo})", "Order {orderNumber} changed (no. {changeNo})",
            "{registeredBy} modificó la orden {orderNumber} de {client}: {summary}. Total {totalBefore} → {totalAfter} ({totalDelta}). Autorizó {authorizedBy} ({channel}). Motivo: {reason}",
            "{registeredBy} changed order {orderNumber} for {client}: {summary}. Total {totalBefore} → {totalAfter} ({totalDelta}). Authorized by {authorizedBy} ({channel}). Reason: {reason}",
            ContextRecipients: new[]
            {
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Seller, "Vendedor de la orden", "Order's sales rep", IsDefault: true),
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Assignee, "Encargado del almacén de la orden", "Order's warehouse assignee", IsDefault: true),
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Registrant, "Quien registró la modificación (cuando se aplicó al aprobarla)",
                    "Who registered the change (when it was applied on approval)", IsDefault: true),
            }),
        new NotificationEventDef(
            SalesChangeEventCodes.AdditionCreated, ModuleKeys.SalesOrders, "Adición a una orden de venta", "Addition to a sales order",
            Change(
                new NotificationVariable("additionNumber", "Número de la adición", "Addition number", "OV-00045"),
                new NotificationVariable("additionPublicId", "Identificador de la adición", "Addition identifier", "7c9e6679742540de944be07fc1f90ae7")),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Adición {additionNumber} a la orden {orderNumber}", "Addition {additionNumber} to order {orderNumber}",
            "{registeredBy} creó la adición {additionNumber} a la orden {orderNumber} de {client} para {warehouse} ({totalDelta}). Autorizó {authorizedBy} ({channel}). Motivo: {reason}",
            "{registeredBy} created addition {additionNumber} to order {orderNumber} for {client} for {warehouse} ({totalDelta}). Authorized by {authorizedBy} ({channel}). Reason: {reason}",
            ContextRecipients: new[]
            {
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Seller, "Vendedor de la orden", "Order's sales rep", IsDefault: true),
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Assignee, "Encargado del almacén de la orden", "Order's warehouse assignee", IsDefault: true),
            }),
        new NotificationEventDef(
            SalesChangeEventCodes.PackReopened, ModuleKeys.SalesOrders, "Empaque reabierto", "Pack reopened",
            Base(
                new NotificationVariable("batchNumber", "Recolección o grupo", "Pick batch or group", "PK-00031"),
                new NotificationVariable("shipmentNumber", "Envío anulado", "Voided shipment", "ORD-01234"),
                new NotificationVariable("reopenedBy", "Quién lo reabrió", "Reopened by", "Ana Pérez"),
                new NotificationVariable("reason", "Motivo", "Reason", "Modificación n.º 2 de la orden")),
            IsOptional: true, LinkTemplate: LinkTemplate,
            "Empaque reabierto: {batchNumber} (orden {orderNumber})", "Pack reopened: {batchNumber} (order {orderNumber})",
            "{reopenedBy} reabrió el empaque {batchNumber} de la orden {orderNumber} de {client} en {warehouse}; se anuló el envío {shipmentNumber}. Motivo: {reason}. Vuelva a empacarlo cuando esté listo.",
            "{reopenedBy} reopened pack {batchNumber} of order {orderNumber} for {client} in {warehouse}; shipment {shipmentNumber} was voided. Reason: {reason}. Pack it again when it is ready.",
            ContextRecipients: new[]
            {
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Assignee, "Encargado del almacén de la orden", "Order's warehouse assignee", IsDefault: true),
                new NotificationContextRecipientDef(SalesChangeContextRecipients.Seller, "Vendedor de la orden", "Order's sales rep", IsDefault: false),
            }),
        new NotificationEventDef(
            SalesChangeEventCodes.ApprovalPending, ModuleKeys.SalesOrders, "Modificación de orden de venta por aprobar", "Sales order change pending approval",
            Change(new NotificationVariable("approvalRule", "Por qué pide aprobación", "Why it needs approval", "El cambio pasa de $500.00")),
            IsOptional: false, LinkTemplate: LinkTemplate,
            "Modificación por aprobar: orden {orderNumber} (n.º {changeNo})", "Change pending approval: order {orderNumber} (no. {changeNo})",
            "{registeredBy} registró la modificación n.º {changeNo} de la orden {orderNumber} de {client} y necesita la segunda aprobación ({approvalRule}): {summary}. Total {totalBefore} → {totalAfter} ({totalDelta}). Autorizó {authorizedBy} ({channel}). Motivo: {reason}",
            "{registeredBy} registered change no. {changeNo} of order {orderNumber} for {client} and it needs the second approval ({approvalRule}): {summary}. Total {totalBefore} → {totalAfter} ({totalDelta}). Authorized by {authorizedBy} ({channel}). Reason: {reason}"),
    };
}
