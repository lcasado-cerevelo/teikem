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
    /// <summary>Lote 222: entró un cliente nuevo (prospecto) por un pedido; lo aprueba o lo rechaza quien tiene sales.approve_client.</summary>
    public const string ClientPendingApproval = "SALES_CLIENT_PENDING_APPROVAL";
    /// <summary>Lote 222: se solicitó un pedido urgente (para el almacén).</summary>
    public const string OrderUrgent = "SALES_ORDER_URGENT";
    /// <summary>Lote 225: el aviso del día «Facturas por pasar a QuickBooks: N» (lo publica el trabajo SALES_QUICKBOOKS_PENDING, uno al día y solo si hay algo).</summary>
    public const string QuickBooksPending = "SALES_QUICKBOOKS_PENDING";

    public static readonly string[] All = { OrderRequested, OrderShipped, OrderCancelled, CreditOverrideNeeded, BackorderAvailable, ClientPendingApproval, OrderUrgent, QuickBooksPending };
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
        // Lote 222 (pedido de vendedores): el cliente nuevo por aprobar y el pedido urgente
        new NotificationEventDef(
            SalesEventCodes.ClientPendingApproval, ModuleKeys.SalesOrders, "Cliente nuevo pendiente de aprobación", "New client pending approval",
            new[]
            {
                new NotificationVariable("client", "Cliente", "Customer", "Farmacia Central"),
                new NotificationVariable("clientCode", "Número del cliente", "Customer number", "FARMACIA-CE"),
                new NotificationVariable("seller", "Vendedor", "Sales rep", "Ana Pérez"),
            },
            IsOptional: false, LinkTemplate: "/clients",
            "Cliente nuevo pendiente de aprobación: {client}", "New client pending approval: {client}",
            "{seller} dio de alta al cliente {client} ({clientCode}) desde un pedido. Verifique sus datos (registro de comerciante, certificado) y apruébelo o recházelo; sus pedidos no se despachan hasta entonces.",
            "{seller} added the client {client} ({clientCode}) from an order. Check its data (merchant registration, certificate) and approve or reject it; its orders are not shipped until then."),
        new NotificationEventDef(
            SalesEventCodes.OrderUrgent, ModuleKeys.SalesOrders, "Pedido urgente", "Urgent order",
            Base(new NotificationVariable("note", "Nota", "Note", "Lo necesitan antes del mediodía."), new NotificationVariable("seller", "Vendedor", "Sales rep", "Ana Pérez")),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Pedido urgente {orderNumber} de {client}", "Urgent order {orderNumber} for {client}",
            "{seller} solicitó el pedido URGENTE {orderNumber} de {client} para {warehouse}. {note}",
            "{seller} requested the URGENT order {orderNumber} for {client} for {warehouse}. {note}"),
        // Lote 225 (pedido de vendedores): el aviso de la mañana de QuickBooks
        new NotificationEventDef(
            SalesEventCodes.QuickBooksPending, ModuleKeys.SalesOrders, "Facturas por pasar a QuickBooks", "Invoices to pass to QuickBooks",
            new[]
            {
                new NotificationVariable("count", "Facturas por pasar", "Invoices to pass", "12"),
                new NotificationVariable("exported", "De ellas, ya salieron en un archivo", "Of them, already in a file", "3"),
                new NotificationVariable("oldest", "Emisión más vieja", "Oldest issue date", "2026-11-02"),
            },
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Facturas por pasar a QuickBooks: {count}", "Invoices to pass to QuickBooks: {count}",
            "Hay {count} factura(s) por pasar a QuickBooks (la más vieja es del {oldest}; {exported} ya salieron en un archivo sin confirmar). Entre a Órdenes de venta → QuickBooks para exportarlas o marcarlas.",
            "There are {count} invoice(s) to pass to QuickBooks (the oldest is from {oldest}; {exported} already went out in a file without confirmation). Go to Sales orders → QuickBooks to export or mark them."),
    };
}
