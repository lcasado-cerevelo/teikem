using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Lote 227 (P7 del pedido de vendedores) — códigos de los avisos del puente entre compañías. Empiezan por INTEGRATION_: la pantalla de reglas los agrupa en Integraciones.</summary>
public static class TenantLinkEventCodes
{
    /// <summary>A la otra compañía: propusieron, aceptaron, rechazaron o terminaron el enlace.</summary>
    public const string LinkChanged = "INTEGRATION_LINK_CHANGED";
    /// <summary>A la compañía cliente: la orden de la compañía enlazada cambió de estatus (entregada, en ruta, fallida…).</summary>
    public const string DeliveryStatus = "INTEGRATION_LINK_DELIVERY_STATUS";
    /// <summary>A la compañía cliente: la compañía enlazada no aceptó la orden (hay que corregir y reintentar).</summary>
    public const string DeliveryFailed = "INTEGRATION_LINK_DELIVERY_FAILED";
    /// <summary>A la compañía que da el transporte: entró una orden urgente de la compañía enlazada.</summary>
    public const string UrgentOrder = "INTEGRATION_LINK_URGENT_ORDER";

    public static readonly string[] All = { LinkChanged, DeliveryStatus, DeliveryFailed, UrgentOrder };
}

/// <summary>
/// Lote 227 — los eventos de aviso del puente (módulo INTERCOMPANY). Los publica TenantLinkService (cambio del enlace, en la otra compañía) y TenantLinkBridge (entregas: en la
/// compañía cliente; urgente: en la que da el transporte). Las reglas (a quién y por qué canal) las arma el administrador en Reglas de aviso: no se siembra ninguna.
/// </summary>
public sealed class TenantLinkEventProvider : INotificationEventProvider
{
    public static string ActionText(string action) => action switch
    {
        "PROPOSED" => "propuso",
        "ACCEPTED" => "aceptó",
        "REJECTED" => "rechazó",
        "ENDED" => "terminó",
        _ => action,
    };

    private static readonly NotificationVariable[] Delivery =
    {
        new("salesOrder", "Orden de venta", "Sales order", "OV-00012"),
        new("dispatch", "Despacho", "Dispatch", "EMP-00031"),
        new("company", "Compañía que entrega", "Delivering company", "Advance Logistics"),
        new("orderNumber", "Orden en la compañía enlazada", "Order at the linked company", "PR0123-000045"),
    };

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            TenantLinkEventCodes.LinkChanged, ModuleKeys.Intercompany, "Compañía enlazada: cambio en el enlace", "Linked company: link changed",
            new[]
            {
                new NotificationVariable("company", "Compañía que hizo el cambio", "Company that made the change", "Advance Solutions"),
                new NotificationVariable("action", "Qué hizo", "What it did", "propuso"),
                new NotificationVariable("client", "Compañía cliente", "Client company", "Advance Solutions"),
                new NotificationVariable("provider", "Compañía que da el transporte", "Transport company", "Advance Logistics"),
                new NotificationVariable("note", "Nota o motivo", "Note or reason", "Para las entregas a farmacias."),
            },
            IsOptional: false, LinkTemplate: "/system/integrations?tab=links",
            "{company} {action} el enlace con su compañía", "{company} changed the link with your company ({action})",
            "{company} {action} el enlace «{client} es cliente de {provider}». {note}",
            "{company} changed the link “{client} is a client of {provider}” ({action}). {note}"),
        new NotificationEventDef(
            TenantLinkEventCodes.DeliveryStatus, ModuleKeys.Intercompany, "Entrega por compañía enlazada: cambio de estatus", "Delivery by linked company: status change",
            Delivery.Concat(new[]
            {
                new NotificationVariable("status", "Estatus", "Status", "Entregada"),
                new NotificationVariable("deliveredTo", "Recibió", "Received by", "María Rivera"),
            }).ToArray(),
            IsOptional: true, LinkTemplate: "/sales-orders",
            "Orden {salesOrder}: {status} ({company})", "Order {salesOrder}: {status} ({company})",
            "La entrega del despacho {dispatch} de la orden {salesOrder} ({company}, orden {orderNumber}) está {status}. {deliveredTo}",
            "The delivery of dispatch {dispatch} of order {salesOrder} ({company}, order {orderNumber}) is {status}. {deliveredTo}"),
        new NotificationEventDef(
            TenantLinkEventCodes.DeliveryFailed, ModuleKeys.Intercompany, "Entrega por compañía enlazada: no se pudo crear", "Delivery by linked company: could not be created",
            Delivery.Concat(new[] { new NotificationVariable("error", "Motivo", "Reason", "El consignatario es obligatorio.") }).ToArray(),
            IsOptional: false, LinkTemplate: "/sales-orders",
            "Orden {salesOrder}: {company} no aceptó la entrega", "Order {salesOrder}: {company} did not accept the delivery",
            "La entrega del despacho {dispatch} de la orden {salesOrder} no se pudo crear en {company}: {error} Corríjalo y reintente desde Integraciones → Compañías enlazadas.",
            "The delivery of dispatch {dispatch} of order {salesOrder} could not be created at {company}: {error} Fix it and retry from Integrations → Linked companies."),
        new NotificationEventDef(
            TenantLinkEventCodes.UrgentOrder, ModuleKeys.Intercompany, "Orden urgente de una compañía enlazada", "Urgent order from a linked company",
            new[]
            {
                new NotificationVariable("orderNumber", "Orden", "Order", "PR0123-000045"),
                new NotificationVariable("company", "Compañía cliente", "Client company", "Advance Solutions"),
                new NotificationVariable("salesOrder", "Su orden de venta", "Their sales order", "OV-00012"),
                new NotificationVariable("consignee", "Consignatario", "Consignee", "Farmacia Central"),
            },
            IsOptional: false, LinkTemplate: "/orders",
            "Orden urgente {orderNumber} de {company}", "Urgent order {orderNumber} from {company}",
            "{company} mandó la orden urgente {orderNumber} (su orden de venta {salesOrder}) para {consignee}.",
            "{company} sent the urgent order {orderNumber} (their sales order {salesOrder}) for {consignee}."),
    };
}
