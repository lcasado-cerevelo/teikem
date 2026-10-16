using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso de las órdenes de venta (Lote 66, P4 S3). Hasta 60 caracteres (columna EventCode).</summary>
public static class SalesEventCodes
{
    public const string OrderRequested = "SALES_ORDER_REQUESTED";
    public const string OrderShipped = "SALES_ORDER_SHIPPED";
    public const string OrderCancelled = "SALES_ORDER_CANCELLED";
    public const string CreditOverrideNeeded = "SALES_CREDIT_OVERRIDE_NEEDED";
    public const string BackorderAvailable = "SALES_BACKORDER_AVAILABLE";

    public static readonly string[] All = { OrderRequested, OrderShipped, OrderCancelled, CreditOverrideNeeded, BackorderAvailable };
}

/// <summary>
/// Lote 66 (P4 S3) — los eventos de aviso del módulo de órdenes de venta. Se publican desde SalesOrderService (en la misma transacción de la operación) y desde
/// el trabajo SALES_BACKORDER_SCAN (backorder que ya se puede surtir). Todos son opcionales (el usuario puede silenciarlos en su bandeja) salvo el de crédito, que es
/// una petición de autorización. Las reglas (a quién y por qué canal) las arma el administrador en Sistema → Reglas de aviso: no se siembra ninguna.
/// </summary>
public sealed class SalesEventProvider : INotificationEventProvider
{
    private static NotificationVariable[] Base(params NotificationVariable[] extra) => new[]
    {
        new NotificationVariable("orderNumber", "Número de la orden", "Order number", "OV-00012"),
        new NotificationVariable("client", "Cliente", "Customer", "Farmacia Central"),
        new NotificationVariable("warehouse", "Almacén", "Warehouse", "Almacén principal"),
    }.Concat(extra).ToArray();

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            SalesEventCodes.OrderRequested, ModuleKeys.SalesOrders, "Orden de venta solicitada", "Sales order requested",
            Base(new NotificationVariable("lineCount", "Líneas", "Lines", "3"), new NotificationVariable("detail", "Detalle", "Detail", "1 línea con faltante en backorder.")),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Orden {orderNumber} solicitada", "Order {orderNumber} requested",
            "Se solicitó la orden {orderNumber} de {client} para {warehouse} ({lineCount} líneas). {detail}",
            "Order {orderNumber} for {client} was requested for {warehouse} ({lineCount} lines). {detail}"),
        new NotificationEventDef(
            SalesEventCodes.OrderShipped, ModuleKeys.SalesOrders, "Orden de venta despachada", "Sales order shipped",
            Base(new NotificationVariable("shipped", "Unidades despachadas", "Units shipped", "24")),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Orden {orderNumber} despachada", "Order {orderNumber} shipped",
            "La orden {orderNumber} de {client} quedó despachada por completo ({shipped} unidades) desde {warehouse}.",
            "Order {orderNumber} for {client} was fully shipped ({shipped} units) from {warehouse}."),
        new NotificationEventDef(
            SalesEventCodes.OrderCancelled, ModuleKeys.SalesOrders, "Orden de venta cancelada", "Sales order cancelled",
            Base(new NotificationVariable("reason", "Motivo", "Reason", "El cliente la anuló.")),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Orden {orderNumber} cancelada", "Order {orderNumber} cancelled",
            "Se canceló la orden {orderNumber} de {client}. {reason}",
            "Order {orderNumber} for {client} was cancelled. {reason}"),
        new NotificationEventDef(
            SalesEventCodes.CreditOverrideNeeded, ModuleKeys.SalesOrders, "Orden de venta necesita autorizar crédito", "Sales order needs a credit override",
            Base(
                new NotificationVariable("user", "Quién la intentó solicitar", "Who tried to request it", "Ana Pérez"),
                new NotificationVariable("limit", "Límite de crédito", "Credit limit", "$5,000.00"),
                new NotificationVariable("exposure", "En curso", "Outstanding", "$4,200.00"),
                new NotificationVariable("amount", "Esta orden", "This order", "$1,300.00")),
            IsOptional: false, LinkTemplate: "/sales-orders",
            "Orden {orderNumber} excede el crédito de {client}", "Order {orderNumber} exceeds {client}'s credit",
            "{user} intentó solicitar la orden {orderNumber} de {client} y excede el crédito (límite {limit}, en curso {exposure}, esta orden {amount}). Quien tenga el permiso puede autorizarla al solicitarla.",
            "{user} tried to request order {orderNumber} for {client} and it exceeds the credit (limit {limit}, outstanding {exposure}, this order {amount}). Whoever has the permission can authorize it when requesting."),
        new NotificationEventDef(
            SalesEventCodes.BackorderAvailable, ModuleKeys.SalesOrders, "Backorder que ya se puede surtir", "Backorder ready to fulfill",
            Base(new NotificationVariable("lines", "Productos que ya hay", "Products now available", "MED × 3, VIT × 2"), new NotificationVariable("lineCount", "Líneas", "Lines", "2")),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Ya hay inventario para la orden {orderNumber}", "Stock is now available for order {orderNumber}",
            "La orden {orderNumber} de {client} tiene backorder que ya se puede surtir en {warehouse}: {lines}.",
            "Order {orderNumber} for {client} has backorder that can now be fulfilled in {warehouse}: {lines}."),
    };
}
