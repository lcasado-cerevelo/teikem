using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso de flota (Lote 168, 2026-10-21). Hasta 60 caracteres (columna EventCode).</summary>
public static class FleetEventCodes
{
    public const string DocExpiring = "FLEET_DOC_EXPIRING";
    public const string DocExpired = "FLEET_DOC_EXPIRED";
    public const string MaintenanceDueSoon = "FLEET_MAINTENANCE_DUE_SOON";
    public const string MaintenanceOverdue = "FLEET_MAINTENANCE_OVERDUE";

    public static readonly string[] All = { DocExpiring, DocExpired, MaintenanceDueSoon, MaintenanceOverdue };
}

/// <summary>
/// Lote 168 — los eventos de aviso de la flota. Los publica el trabajo diario FLEET_ALERT_SCAN (<see cref="Services.FleetAlertService"/>) con el mismo mecanismo de los avisos
/// de inventario: UN aviso por evento con el conjunto completo, que no se repite mientras el conjunto sea el mismo en la misma semana. Todos son opcionales (el usuario puede
/// silenciarlos en su bandeja). Las reglas (a quién y por qué canal) las arma el administrador en Sistema → Reglas de aviso: no se siembra ninguna. La flota vive en el módulo de catálogo.
/// </summary>
public sealed class FleetEventProvider : INotificationEventProvider
{
    private static NotificationVariable Count(string sample) => new("count", "Cantidad", "Count", sample);
    private static NotificationVariable Items(string sample) => new("items", "Detalle (uno por renglón)", "Detail (one per line)", sample);

    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            FleetEventCodes.DocExpiring, ModuleKeys.Catalog, "Documentos de la flota por vencer", "Fleet documents about to expire",
            new[] { Count("3"), new NotificationVariable("days", "Días de anticipación", "Days ahead", "30"), Items("Vehículo V-01 · Seguro · 2026-11-02 · 12 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet?tab=expiring",
            "{count} documento(s) de la flota por vencer en {days} días", "{count} fleet document(s) expiring within {days} days",
            "Hay {count} documento(s) de vehículos o choferes que vencen en los próximos {days} días (a quién · documento · vencimiento · días):\n{items}",
            "{count} vehicle or driver document(s) expire within the next {days} days (whose · document · expiry · days):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.DocExpired, ModuleKeys.Catalog, "Documentos de la flota vencidos", "Expired fleet documents",
            new[] { Count("2"), Items("Chofer C-02 · Licencia · 2026-09-30 · vencido hace 21 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet?tab=expiring",
            "{count} documento(s) de la flota vencido(s)", "{count} fleet document(s) expired",
            "Hay {count} documento(s) de vehículos o choferes vencido(s) (a quién · documento · vencimiento):\n{items}",
            "{count} vehicle or driver document(s) have expired (whose · document · expiry):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.MaintenanceDueSoon, ModuleKeys.Catalog, "Mantenimiento próximo", "Maintenance coming up",
            new[] { Count("2"), Items("V-01 · Cambio de aceite · faltan 450 km · faltan 12 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet?tab=maintenance",
            "{count} mantenimiento(s) próximo(s)", "{count} maintenance item(s) coming up",
            "Hay {count} mantenimiento(s) preventivo(s) próximo(s) (vehículo · programa · lo que falta):\n{items}",
            "{count} preventive maintenance item(s) are coming up (vehicle · schedule · what is left):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.MaintenanceOverdue, ModuleKeys.Catalog, "Mantenimiento vencido", "Overdue maintenance",
            new[] { Count("1"), Items("V-02 · Revisión de frenos · pasado por 300 km") },
            IsOptional: true, LinkTemplate: "/catalog/fleet?tab=maintenance",
            "{count} mantenimiento(s) vencido(s)", "{count} maintenance item(s) overdue",
            "Hay {count} mantenimiento(s) preventivo(s) vencido(s) (vehículo · programa · lo que se pasó):\n{items}",
            "{count} preventive maintenance item(s) are overdue (vehicle · schedule · how far past):\n{items}"),
    };
}
