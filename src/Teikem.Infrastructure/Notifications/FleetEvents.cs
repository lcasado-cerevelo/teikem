using Teikem.Domain.Constants;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Códigos de los eventos de aviso de flota (Lote 168, 2026-10-21). Hasta 60 caracteres (columna EventCode).</summary>
public static class FleetEventCodes
{
    public const string DocExpiring = "FLEET_DOC_EXPIRING";
    public const string DocExpired = "FLEET_DOC_EXPIRED";
    public const string MaintenanceDueSoon = "FLEET_MAINTENANCE_DUE_SOON";
    public const string MaintenanceOverdue = "FLEET_MAINTENANCE_OVERDUE";

    // Lote 179 (G2)
    public const string WorkOrderStale = "FLEET_WORKORDER_STALE";
    public const string FuelOutOfRange = "FLEET_FUEL_OUT_OF_RANGE";
    public const string VehicleIdle = "FLEET_VEHICLE_IDLE";
    public const string DriverIdle = "FLEET_DRIVER_IDLE";

    public static readonly string[] All = { DocExpiring, DocExpired, MaintenanceDueSoon, MaintenanceOverdue, WorkOrderStale, FuelOutOfRange, VehicleIdle, DriverIdle };
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
        // Lote 179 (G2, 2026-10-21)
        new NotificationEventDef(
            FleetEventCodes.WorkOrderStale, ModuleKeys.Catalog, "Órdenes de trabajo abiertas demasiado tiempo", "Work orders open for too long",
            new[] { Count("2"), Items("OT-00012 · V-01 · abierta hace 20 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet?tab=maintenance",
            "{count} orden(es) de trabajo abierta(s) hace demasiado", "{count} work order(s) open for too long",
            "Hay {count} orden(es) de trabajo de mantenimiento abiertas por más de 14 días (orden · vehículo · tiempo):\n{items}",
            "{count} maintenance work order(s) have been open for more than 14 days (order · vehicle · time):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.FuelOutOfRange, ModuleKeys.Catalog, "Combustible fuera de rango", "Fuel out of range",
            new[] { Count("1"), Items("V-01 · 4.2 km/L · promedio 7.1 · -41 %") },
            IsOptional: true, LinkTemplate: "/catalog/fleet",
            "{count} vehículo(s) con rendimiento de combustible fuera de rango", "{count} vehicle(s) with fuel economy out of range",
            "El rendimiento de la última carga se aparta más de 30 % del promedio de las anteriores (vehículo · km/L · promedio · diferencia):\n{items}",
            "The latest fill's economy is more than 30 % away from the average of the previous ones (vehicle · km/L · average · difference):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.VehicleIdle, ModuleKeys.Catalog, "Vehículos sin viaje", "Vehicles without trips",
            new[] { Count("2"), Items("V-03 · sin viaje desde 2026-09-30 · 21 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet",
            "{count} vehículo(s) sin viaje en más de 14 días", "{count} vehicle(s) without a trip for over 14 days",
            "Hay {count} vehículo(s) activo(s) sin ningún viaje en más de 14 días (vehículo · último viaje · días):\n{items}",
            "{count} active vehicle(s) have had no trip for more than 14 days (vehicle · last trip · days):\n{items}"),
        new NotificationEventDef(
            FleetEventCodes.DriverIdle, ModuleKeys.Catalog, "Choferes sin viaje", "Drivers without trips",
            new[] { Count("1"), Items("C-02 Ana Ruiz · sin viaje desde 2026-09-28 · 23 días") },
            IsOptional: true, LinkTemplate: "/catalog/fleet",
            "{count} chofer(es) sin viaje en más de 14 días", "{count} driver(s) without a trip for over 14 days",
            "Hay {count} chofer(es) activo(s) sin ningún viaje en más de 14 días (chofer · último viaje · días):\n{items}",
            "{count} active driver(s) have had no trip for more than 14 days (driver · last trip · days):\n{items}"),
    };
}
