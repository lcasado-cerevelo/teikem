using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso del aviso de salida (Lote 143). Hasta 60 caracteres (columna EventCode).</summary>
public static class ExitNoticeEventCodes
{
    public const string Requested = "EXIT_NOTICE_REQUESTED";
    public const string BackorderReserved = "EXIT_NOTICE_BACKORDER_RESERVED";
    public const string Shipped = "EXIT_NOTICE_SHIPPED";
    public const string Cancelled = "EXIT_NOTICE_CANCELLED";

    public static readonly string[] All = { Requested, BackorderReserved, Shipped, Cancelled };
}

/// <summary>
/// Lote 143 — los eventos de aviso del aviso de salida. Se publican desde ExitNoticeService (en la misma transacción de la operación) y desde el trabajo
/// EXIT_NOTICE_BACKORDER_SCAN (pendiente que ya se reservó). Todos son opcionales (el usuario puede silenciarlos). Las reglas (a quién y por qué canal) las arma el
/// administrador en Sistema → Reglas de aviso: no se siembra ninguna.
/// </summary>
public sealed class ExitNoticeEventProvider : INotificationEventProvider
{
    private static NotificationVariable[] Base(params NotificationVariable[] extra) => new[]
    {
        new NotificationVariable("number", "Número del aviso", "Notice number", "SAL-00012"),
        new NotificationVariable("client", "Cliente dueño", "Owner customer", "ASICS Care"),
        new NotificationVariable("warehouse", "Almacén", "Warehouse", "Almacén principal"),
    }.Concat(extra).ToArray();

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            ExitNoticeEventCodes.Requested, ModuleKeys.WmsLotSerial, "Aviso de salida solicitado", "Exit notice requested",
            Base(new NotificationVariable("lineCount", "Líneas", "Lines", "3"), new NotificationVariable("detail", "Detalle", "Detail", "1 línea con pendiente por falta de existencia.")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=exit-notices",
            "Aviso de salida {number} solicitado", "Exit notice {number} requested",
            "{client} pidió el aviso de salida {number} para {warehouse} ({lineCount} líneas). {detail}",
            "{client} requested exit notice {number} for {warehouse} ({lineCount} lines). {detail}"),
        new NotificationEventDef(
            ExitNoticeEventCodes.BackorderReserved, ModuleKeys.WmsLotSerial, "Pendiente de un aviso de salida ya reservado", "Exit notice backorder reserved",
            Base(new NotificationVariable("lines", "Productos reservados", "Products reserved", "MED × 3, VIT × 2"), new NotificationVariable("units", "Unidades", "Units", "5")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=exit-notices",
            "Ya hay inventario para el aviso de salida {number}", "Stock is now reserved for exit notice {number}",
            "Entró inventario y se reservó el pendiente del aviso de salida {number} de {client} en {warehouse}: {lines}.",
            "Stock arrived and the pending part of exit notice {number} for {client} was reserved in {warehouse}: {lines}."),
        new NotificationEventDef(
            ExitNoticeEventCodes.Shipped, ModuleKeys.WmsLotSerial, "Aviso de salida despachado", "Exit notice shipped",
            Base(new NotificationVariable("shipped", "Unidades despachadas", "Units shipped", "24")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=exit-notices",
            "Aviso de salida {number} despachado", "Exit notice {number} shipped",
            "El aviso de salida {number} de {client} quedó despachado por completo ({shipped} unidades) desde {warehouse}.",
            "Exit notice {number} for {client} was fully shipped ({shipped} units) from {warehouse}."),
        new NotificationEventDef(
            ExitNoticeEventCodes.Cancelled, ModuleKeys.WmsLotSerial, "Aviso de salida cancelado", "Exit notice cancelled",
            Base(new NotificationVariable("reason", "Motivo", "Reason", "El cliente lo anuló.")),
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=exit-notices",
            "Aviso de salida {number} cancelado", "Exit notice {number} cancelled",
            "Se canceló el aviso de salida {number} de {client}. {reason}",
            "Exit notice {number} for {client} was cancelled. {reason}"),
    };
}
