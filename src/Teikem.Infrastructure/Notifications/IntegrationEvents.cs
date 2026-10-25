using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso de las integraciones (Lote 132, P11). Hasta 60 caracteres (columna EventCode).</summary>
public static class IntegrationEventCodes
{
    /// <summary>Una corrida terminó con algo que reportar: órdenes creadas, filas a la bandeja o una falla.</summary>
    public const string RunFinished = "INTEGRATION_RUN";
    /// <summary>Lote 136: un webhook se suspendió solo tras varias salidas seguidas sin entregarse.</summary>
    public const string WebhookSuspended = "INTEGRATION_WEBHOOK";
    /// <summary>2026-10-24: una corrida creó avisos de llegada (UN aviso por corrida, por mucho que sean). Va a quienes tienen el permiso de crearlos.</summary>
    public const string ArrivalNotices = "INTEGRATION_ARRIVALS";
    /// <summary>2026-10-24: una corrida creó avisos de salida (UN aviso por corrida). Va a quienes tienen el permiso de crearlos.</summary>
    public const string ExitNotices = "INTEGRATION_EXITS";
}

/// <summary>
/// Lote 132 (P11) — el acuse de una integración es un aviso del sistema: a quienes se definieron en la propia integración (con «incluirme»), no a quien la creó por defecto.
/// Lo dispara el ejecutor de integraciones; la regla que decide a quién la administra la integración (NotificationRule.ScopeKey = «integration:{id}»).
/// </summary>
public sealed class IntegrationEventProvider : INotificationEventProvider
{
    private static readonly NotificationVariable[] NoticeVariables =
    {
        new NotificationVariable("integration", "Integración", "Integration", "Axiscare avisos por correo"),
        new NotificationVariable("count", "Avisos creados", "Notices created", "10"),
        new NotificationVariable("documents", "Documentos", "Documents", "Despacho de Mercancia ..pdf, Despacho de Mercancia (2).pdf"),
        new NotificationVariable("integrationId", "Número de la integración", "Integration id", "7"),
    };

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            IntegrationEventCodes.RunFinished, ModuleKeys.Integrations, "Corrida de integración", "Integration run",
            new[]
            {
                new NotificationVariable("integration", "Integración", "Integration", "Envíos de Truss"),
                new NotificationVariable("status", "Resultado", "Result", "Con pendientes"),
                new NotificationVariable("files", "Archivos leídos", "Files read", "2"),
                new NotificationVariable("created", "Envíos creados", "Shipments created", "14"),
                new NotificationVariable("rejected", "Enviadas a la bandeja", "Sent to the inbox", "3"),
                new NotificationVariable("message", "Detalle", "Detail", "Archivo ordenes.csv: 14 creadas, 3 a la bandeja."),
                new NotificationVariable("integrationId", "Número de la integración", "Integration id", "7"),
            },
            IsOptional: false, LinkTemplate: "/integrations?integration={integrationId}",
            "Integración «{integration}»: {status}", "Integration “{integration}”: {status}",
            "{message} Creadas: {created}. A la bandeja: {rejected}.", "{message} Created: {created}. To the inbox: {rejected}."),
        new NotificationEventDef(
            IntegrationEventCodes.ArrivalNotices, ModuleKeys.Integrations, "Avisos de llegada creados por una integración", "Arrival notices created by an integration",
            NoticeVariables,
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=asns",
            "{count} aviso(s) de llegada creado(s) por «{integration}»", "{count} arrival notice(s) created by “{integration}”",
            "La integración «{integration}» creó {count} aviso(s) de llegada a partir de: {documents}. Revíselos en Almacén → Recibo.",
            "The integration “{integration}” created {count} arrival notice(s) from: {documents}. Review them in Warehouse → Receiving."),
        new NotificationEventDef(
            IntegrationEventCodes.ExitNotices, ModuleKeys.Integrations, "Avisos de salida creados por una integración", "Exit notices created by an integration",
            NoticeVariables,
            IsOptional: true, LinkTemplate: "/warehouse/receipts?tab=exit-notices",
            "{count} aviso(s) de salida creado(s) por «{integration}»", "{count} exit notice(s) created by “{integration}”",
            "La integración «{integration}» creó {count} aviso(s) de salida a partir de: {documents}. Revíselos en Almacén → Recibo.",
            "The integration “{integration}” created {count} exit notice(s) from: {documents}. Review them in Warehouse → Receiving."),
        new NotificationEventDef(
            IntegrationEventCodes.WebhookSuspended, ModuleKeys.Integrations, "Webhook suspendido", "Webhook suspended",
            new[]
            {
                new NotificationVariable("integration", "Integración", "Integration", "API de Truss"),
                new NotificationVariable("url", "URL", "URL", "https://cliente.example.com/hooks"),
                new NotificationVariable("failures", "Salidas seguidas sin entregar", "Consecutive undelivered", "5"),
                new NotificationVariable("message", "Último motivo", "Last reason", "El servidor respondió 500."),
                new NotificationVariable("integrationId", "Número de la integración", "Integration id", "7"),
            },
            IsOptional: false, LinkTemplate: "/integrations?tab=outbox&integration={integrationId}",
            "Webhook suspendido: «{integration}»", "Webhook suspended: “{integration}”",
            "Se suspendió el webhook {url} tras {failures} salidas seguidas sin entregarse. Último motivo: {message} Revise la bandeja de salidas y reactívelo.",
            "The webhook {url} was suspended after {failures} consecutive undelivered notices. Last reason: {message} Check the outbox and resume it."),
    };
}
