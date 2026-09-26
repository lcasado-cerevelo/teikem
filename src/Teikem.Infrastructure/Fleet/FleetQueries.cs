using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Fleet;

/// <summary>Alcance de LoadFleetDocumentsAsync. Ids null = todos los del tenant; una lista vacía no devuelve nada.</summary>
public sealed record FleetDocumentScope(
    bool IncludeVehicles = true,
    bool IncludeDrivers = true,
    bool OnlyActiveOwners = true,
    IReadOnlyCollection<int>? VehicleIds = null,
    IReadOnlyCollection<int>? DriverIds = null);

/// <summary>
/// Lote 4 (P0) — consultas compartidas de Flota (costuras implementadas, no esqueleto). Todas parten de conjuntos con
/// filtro global de tenant (Vehicles, Drivers, FuelLogs, MaintenanceWorkOrders); las hijas sin TenantId (documentos,
/// licencias, certificaciones) se alcanzan SIEMPRE a través de su dueño filtrado.
/// </summary>
public static class FleetQueries
{
    public const string VehicleInactiveMessage = "El vehículo está inactivo; reactívelo para registrar órdenes de trabajo o cargas de combustible.";
    public const string DriverRetiredMessage = "El chofer fue eliminado; sus tarifas y viajes solo se consultan.";

    /// <summary>Origen de la última lectura de odómetro (LastRecordedOdometerAsync).</summary>
    public const string OdometerSourceFuel = EntityTypes.FuelLog;
    public const string OdometerSourceWorkOrder = EntityTypes.WorkOrder;

    /// <summary>Vehículo del tenant por PublicId (404 'Vehículo no encontrado.').</summary>
    public static async Task<Vehicle> ResolveVehicleAsync(this TeikemDbContext db, Guid publicId, bool track, CancellationToken ct)
    {
        var q = track ? db.Vehicles.AsQueryable() : db.Vehicles.AsNoTracking();
        return await q.FirstOrDefaultAsync(v => v.PublicId == publicId, ct) ?? throw new NotFoundException("Vehículo");
    }

    /// <summary>Chofer del tenant por PublicId (404 'Chofer no encontrado.').</summary>
    public static async Task<Driver> ResolveDriverAsync(this TeikemDbContext db, Guid publicId, bool track, CancellationToken ct)
    {
        var q = track ? db.Drivers.AsQueryable() : db.Drivers.AsNoTracking();
        return await q.FirstOrDefaultAsync(d => d.PublicId == publicId, ct) ?? throw new NotFoundException("Chofer");
    }

    /// <summary>¿El estatus es una etapa TERMINAL (baja definitiva, OT cerrada/cancelada, viaje liquidado/cancelado)?</summary>
    public static Task<bool> IsTerminalAsync(this TeikemDbContext db, int statusCodeId, CancellationToken ct)
        => db.StatusCodes.AsNoTracking()
            .Where(s => s.StatusCodeId == statusCodeId)
            .Select(s => s.StageKind != null && s.StageKind.InternalCode == StageKinds.Terminal)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Carga el vehículo del tenant con bloqueo de fila (UPDLOCK, ROWLOCK) hasta el fin de la transacción, tracked. Es la
    /// ÚNICA forma de cargar el vehículo en escrituras que tocan el odómetro: serializa cargas de combustible, cierres de OT y
    /// correcciones manuales concurrentes. Exige una transacción abierta (RunInTransactionAsync); sin ella el bloqueo se
    /// soltaría al terminar la consulta, así que lanza InvalidOperationException. null si no existe en el tenant.
    /// </summary>
    public static async Task<Vehicle?> LockVehicleAsync(this TeikemDbContext db, int vehicleId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("LockVehicleAsync requiere una transacción abierta (RunInTransactionAsync).");
        var tenantId = db.CurrentTenantId;
        return await db.Vehicles
            .FromSqlInterpolated($"SELECT * FROM dbo.Vehicle WITH (UPDLOCK, ROWLOCK) WHERE VehicleId = {vehicleId} AND TenantId = {tenantId}")
            .AsTracking()
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Mayor lectura de odómetro registrada del vehículo entre las cargas de combustible activas y las OT CLOSED activas
    /// (con su fecha y origen FUEL_LOG / WORK_ORDER). Es el piso de la corrección manual. null si no hay lecturas.
    /// </summary>
    public static async Task<(decimal Km, DateOnly Date, string Source)?> LastRecordedOdometerAsync(this TeikemDbContext db, int vehicleId, CancellationToken ct)
    {
        var fuel = await db.FuelLogs.AsNoTracking()
            .Where(f => f.VehicleId == vehicleId && f.IsActive && f.OdometerKm != null)
            .OrderByDescending(f => f.OdometerKm).ThenByDescending(f => f.FillDateUtc)
            .Select(f => new { Km = f.OdometerKm!.Value, f.FillDateUtc })
            .FirstOrDefaultAsync(ct);

        var closedId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WorkOrderStatus && s.InternalCode == WorkOrderStatuses.Closed)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        var wo = closedId is null
            ? null
            : await db.MaintenanceWorkOrders.AsNoTracking()
                .Where(w => w.VehicleId == vehicleId && w.IsActive && w.StatusCodeId == closedId.Value && w.OdometerKm != null)
                .OrderByDescending(w => w.OdometerKm).ThenByDescending(w => w.CompletedDate)
                .Select(w => new { Km = w.OdometerKm!.Value, w.CompletedDate, w.UpdatedAtUtc, w.CreatedAtUtc })
                .FirstOrDefaultAsync(ct);

        (decimal Km, DateOnly Date, string Source)? best = null;
        if (fuel is not null) best = (fuel.Km, DateOnly.FromDateTime(fuel.FillDateUtc), OdometerSourceFuel);
        if (wo is not null && (best is null || wo.Km > best.Value.Km))
            best = (wo.Km, wo.CompletedDate ?? DateOnly.FromDateTime(wo.UpdatedAtUtc ?? wo.CreatedAtUtc), OdometerSourceWorkOrder);
        return best;
    }

    /// <summary>
    /// Documentos de flota del tenant: documentos de vehículo, licencias y certificaciones ACTIVOS, en tres consultas que
    /// parten SIEMPRE de db.Vehicles / db.Drivers (filtro de tenant). Marca si el dueño está activo y si está en etapa
    /// terminal, y aplica la regla 'vigente por tipo' (FleetDocuments.MarkSuperseded). La reutilizan el panel 'Documentos
    /// por vencer', la disponibilidad para despacho y la fuente FLEET_DOCUMENT: la regla no se triplica.
    /// Con OnlyActiveOwners solo trae dueños activos y no terminales. <paramref name="today"/> es la fecha de referencia
    /// del llamador (la regla de 'superado' no depende de ella).
    /// </summary>
    public static async Task<IReadOnlyList<FleetDocumentRow>> LoadFleetDocumentsAsync(this TeikemDbContext db, DateOnly today,
        FleetDocumentScope scope, CancellationToken ct)
    {
        _ = today;
        scope ??= new FleetDocumentScope();
        var terminalIds = await db.StatusCodes.AsNoTracking()
            .Where(s => (s.Entity == StatusDomains.VehicleStatus || s.Entity == StatusDomains.DriverStatus)
                        && s.StageKind != null && s.StageKind.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);

        var rows = new List<FleetDocumentRow>();

        if (scope.IncludeVehicles)
        {
            var vehicles = db.Vehicles.AsNoTracking();
            if (scope.OnlyActiveOwners) vehicles = vehicles.Where(v => v.IsActive && !terminalIds.Contains(v.StatusCodeId));
            if (scope.VehicleIds is not null)
            {
                var ids = scope.VehicleIds.Distinct().ToList();
                vehicles = vehicles.Where(v => ids.Contains(v.VehicleId));
            }

            var docs = await (from v in vehicles
                              join d in db.VehicleDocuments.AsNoTracking() on v.VehicleId equals d.VehicleId
                              join t in db.LookupCodes.AsNoTracking() on d.DocTypeLookupId equals t.LookupCodeId
                              where d.IsActive
                              select new
                              {
                                  d.VehicleDocumentId, v.VehicleId, v.PublicId, v.Code, v.PlateNumber, v.Make, v.Model, v.IsActive, v.StatusCodeId,
                                  d.DocTypeLookupId, TypeCode = t.InternalCode, d.DocNumber, d.IssuedDate, d.ExpiryDate,
                              }).ToListAsync(ct);
            rows.AddRange(docs.Select(d => new FleetDocumentRow(
                "VD-" + d.VehicleDocumentId, FleetOwnerKinds.Vehicle, d.VehicleId, d.PublicId, d.Code, VehicleName(d.Code, d.Make, d.Model, d.PlateNumber),
                d.IsActive, terminalIds.Contains(d.StatusCodeId), d.TypeCode, d.DocTypeLookupId, d.TypeCode, d.DocNumber, d.IssuedDate, d.ExpiryDate)));
        }

        if (scope.IncludeDrivers)
        {
            var drivers = db.Drivers.AsNoTracking();
            if (scope.OnlyActiveOwners) drivers = drivers.Where(d => d.IsActive && !terminalIds.Contains(d.StatusCodeId));
            if (scope.DriverIds is not null)
            {
                var ids = scope.DriverIds.Distinct().ToList();
                drivers = drivers.Where(d => ids.Contains(d.DriverId));
            }

            var licenses = await (from d in drivers
                                  join l in db.DriverLicenses.AsNoTracking() on d.DriverId equals l.DriverId
                                  join t in db.LookupCodes.AsNoTracking() on l.LicenseClassLookupId equals t.LookupCodeId
                                  where l.IsActive
                                  select new
                                  {
                                      l.DriverLicenseId, d.DriverId, d.PublicId, d.EmployeeCode, d.FullName, d.IsActive, d.StatusCodeId,
                                      l.LicenseClassLookupId, TypeCode = t.InternalCode, l.LicenseNumber, l.IssuedDate, l.ExpiryDate,
                                  }).ToListAsync(ct);
            rows.AddRange(licenses.Select(l => new FleetDocumentRow(
                "DL-" + l.DriverLicenseId, FleetOwnerKinds.Driver, l.DriverId, l.PublicId, l.EmployeeCode, l.FullName,
                l.IsActive, terminalIds.Contains(l.StatusCodeId), FleetDocumentKinds.License, l.LicenseClassLookupId, l.TypeCode,
                l.LicenseNumber, l.IssuedDate, l.ExpiryDate)));

            var certifications = await (from d in drivers
                                        join c in db.DriverCertifications.AsNoTracking() on d.DriverId equals c.DriverId
                                        join t in db.LookupCodes.AsNoTracking() on c.CertTypeLookupId equals t.LookupCodeId
                                        where c.IsActive
                                        select new
                                        {
                                            c.DriverCertificationId, d.DriverId, d.PublicId, d.EmployeeCode, d.FullName, d.IsActive, d.StatusCodeId,
                                            c.CertTypeLookupId, TypeCode = t.InternalCode, c.CertNumber, c.IssuedDate, c.ExpiryDate,
                                        }).ToListAsync(ct);
            rows.AddRange(certifications.Select(c => new FleetDocumentRow(
                "DC-" + c.DriverCertificationId, FleetOwnerKinds.Driver, c.DriverId, c.PublicId, c.EmployeeCode, c.FullName,
                c.IsActive, terminalIds.Contains(c.StatusCodeId), FleetDocumentKinds.Certification, c.CertTypeLookupId, c.TypeCode,
                c.CertNumber, c.IssuedDate, c.ExpiryDate)));
        }

        return FleetDocuments.MarkSuperseded(rows);
    }

    /// <summary>Nombre visible del vehículo: marca, modelo y placa si los tiene; si no, su código.</summary>
    private static string VehicleName(string code, string? make, string? model, string? plate)
    {
        var name = string.Join(" ", new[] { make, model }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        if (!string.IsNullOrWhiteSpace(plate)) name = string.IsNullOrEmpty(name) ? plate.Trim() : $"{name} · {plate.Trim()}";
        return string.IsNullOrEmpty(name) ? code : name;
    }
}
