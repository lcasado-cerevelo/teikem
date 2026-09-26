using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P8) — resolvers de pertenencia de flota para las asociaciones polimórficas (ContactPoint, CustomFieldValue) y el
/// historial de estatus: responden si el id existe en el tenant activo. Todas las entidades consultadas llevan TenantId y
/// quedan bajo el filtro global de TeikemDbContext; junto con OwnerReadPermission/OwnerWritePermission (fleet.view /
/// fleet.manage / fleet.maintenance / driverpay.*, PermissionCatalog) cierran contactos, campos personalizados e historial
/// a esos permisos. Un id de otro tenant o inexistente responde false → 404.
/// </summary>
public sealed class VehicleOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Vehicle;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Vehicles.AsNoTracking().AnyAsync(v => v.VehicleId == id, ct);
}

public sealed class DriverOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.Driver;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.Drivers.AsNoTracking().AnyAsync(d => d.DriverId == id, ct);
}

public sealed class DispatchZoneOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.DispatchZone;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.DispatchZones.AsNoTracking().AnyAsync(z => z.DispatchZoneId == id, ct);
}

public sealed class MaintenanceScheduleOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.MaintenanceSchedule;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.MaintenanceSchedules.AsNoTracking().AnyAsync(s => s.MaintenanceScheduleId == id, ct);
}

/// <summary>Orden de trabajo de mantenimiento (EntityType WORK_ORDER, ya sembrado; no existe MAINTENANCE_WORK_ORDER).</summary>
public sealed class WorkOrderOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.WorkOrder;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.MaintenanceWorkOrders.AsNoTracking().AnyAsync(w => w.WorkOrderId == id, ct);
}

public sealed class FuelLogOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.FuelLog;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.FuelLogs.AsNoTracking().AnyAsync(f => f.FuelLogId == id, ct);
}

/// <summary>
/// Viaje pagado al chofer (EntityType DRIVER_TRIP): historial de DriverTripStatus. Sus permisos de dueño son driverpay.*,
/// así que un usuario con solo fleet.* no ve el historial ni la compensación.
/// </summary>
public sealed class DriverTripOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => EntityTypes.DriverTrip;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct)
        => db.DriverTrips.AsNoTracking().AnyAsync(t => t.DriverTripId == id, ct);
}

/// <summary>
/// Resolver cerrado: siempre responde false (404). Se registra (con factory) para EntityTypes que tienen permiso de dueño
/// pero no admiten contactos ni campos personalizados por id suelto: DRIVER_RATE (tres tablas de tarifa, ids no únicos
/// entre ellas; se alcanzan solo bajo /drivers/{publicId}) y FLEET_DOCUMENT (fila virtual que une documentos, licencias
/// y certificaciones). Sin él, CustomFieldService omitiría la verificación de pertenencia y la ruta quedaría abierta.
/// </summary>
public sealed class ClosedOwnedEntityResolver(string entityTypeCode) : IOwnedEntityResolver
{
    public string EntityTypeCode { get; } = entityTypeCode;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => Task.FromResult(false);
}
