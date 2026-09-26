using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 4 (P8) — fuentes de datos de identidad de flota (VEHICLE, DRIVER) y de órdenes de trabajo (WORK_ORDER) para vistas,
/// indicadores y gráficos (módulos G/H/I). Siguen el patrón de ClientDataSources/TransportOrderDataSource: lectura
/// AsNoTracking bajo el filtro de tenant, tope de ClientDataSourceHelpers.MaxRows filas, etiquetas resueltas con
/// ILookupCache/MultilingualText y StatusMapAsync, respeto de q.Ids (relaciones muchos-a-uno) y del rango cuando hay DateField.
/// - Los documentos, licencias y zonas no llevan TenantId: se alcanzan SIEMPRE a través de Vehicles/Drivers/DispatchZones
///   (filtro de tenant). Los vencimientos usan FleetQueries.LoadFleetDocumentsAsync, que ya aplica la regla "vigente por
///   tipo" (FleetDocuments.MarkSuperseded): un documento superado no cuenta para el próximo vencimiento.
/// - No hay fuentes de tarifas ni de viajes de chofer (DECISIÓN: Análisis solo exige analytics.view y expondría la compensación).
/// - Nombres de campo estables: los usa SystemAnalyticsSeeder (vista "Vehículos", indicador "Órdenes de trabajo abiertas").
/// </summary>
public sealed class VehicleDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Vehicle;
    public string LabelEs => "Vehículos";
    public string LabelEn => "Vehicles";
    /// <summary>Habilita los campos personalizados del vehículo como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.Vehicle;
    /// <summary>Estado actual: sin rango de fecha.</summary>
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Código", "Code", DataFieldType.Text),
        new DataField("PlateNumber", "Placa", "Plate number", DataFieldType.Text),
        new DataField("VehicleType", "Tipo de vehículo", "Vehicle type", DataFieldType.Text),
        new DataField("VehicleTypeCode", "Código de tipo de vehículo", "Vehicle type code", DataFieldType.Text),
        new DataField("Ownership", "Propiedad", "Ownership", DataFieldType.Text),
        new DataField("OwnershipCode", "Código de propiedad", "Ownership code", DataFieldType.Text),
        new DataField("FuelType", "Combustible", "Fuel type", DataFieldType.Text),
        new DataField("FuelTypeCode", "Código de combustible", "Fuel type code", DataFieldType.Text),
        new DataField("Make", "Marca", "Make", DataFieldType.Text),
        new DataField("Model", "Modelo", "Model", DataFieldType.Text),
        new DataField("ModelYear", "Año", "Model year", DataFieldType.Number),
        new DataField("Vin", "VIN", "VIN", DataFieldType.Text),
        new DataField("CurrentOdometerKm", "Odómetro (km)", "Odometer (km)", DataFieldType.Number),
        new DataField("MaxWeightKg", "Capacidad de peso (kg)", "Max weight (kg)", DataFieldType.Number),
        new DataField("MaxVolumeM3", "Capacidad de volumen (m³)", "Max volume (m³)", DataFieldType.Number),
        new DataField("MaxStops", "Tope de paradas", "Max stops", DataFieldType.Number),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("NextDocumentExpiry", "Próximo vencimiento de documento", "Next document expiry", DataFieldType.Date),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creado", "Created", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Vehicles.AsNoTracking().AsQueryable();
        List<int>? wanted = null;
        if (q.Ids is not null)
        {
            var ids = q.Ids.ToList();
            wanted = ids;
            query = query.Where(v => ids.Contains(v.VehicleId));
        }
        var vehicles = await query.OrderBy(v => v.Code).ThenBy(v => v.VehicleId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (vehicles.Count == 0) return new List<DataRow>();

        var lang = tenant.Lang;
        var today = ClientDataSourceHelpers.Today();
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.VehicleStatus, ct);

        // Próximo vencimiento: documentos activos NO superados (regla "vigente por tipo" del helper compartido). Sin q.Ids
        // se leen todos los del tenant (evita mandar hasta 20 000 ids); los dueños inactivos también cuentan aquí.
        var docs = await db.LoadFleetDocumentsAsync(today,
            new FleetDocumentScope(IncludeVehicles: true, IncludeDrivers: false, OnlyActiveOwners: false, VehicleIds: wanted), ct);
        var nextExpiry = docs
            .Where(d => d.OwnerKind == FleetOwnerKinds.Vehicle && !d.IsSuperseded && d.ExpiryDate.HasValue)
            .GroupBy(d => d.OwnerId)
            .ToDictionary(g => g.Key, g => g.Min(d => d.ExpiryDate!.Value));

        var rows = new List<DataRow>(vehicles.Count);
        foreach (var v in vehicles)
        {
            rows.Add(new DataRow
            {
                ["Id"] = v.VehicleId,
                ["PublicId"] = v.PublicId.ToString(),
                ["Code"] = v.Code,
                ["PlateNumber"] = v.PlateNumber,
                ["VehicleType"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, v.VehicleTypeLookupId, lang, ct),
                ["VehicleTypeCode"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, v.VehicleTypeLookupId, ct),
                ["Ownership"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, v.OwnershipLookupId, lang, ct),
                ["OwnershipCode"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, v.OwnershipLookupId, ct),
                ["FuelType"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, v.FuelTypeLookupId, lang, ct),
                ["FuelTypeCode"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, v.FuelTypeLookupId, ct),
                ["Make"] = v.Make,
                ["Model"] = v.Model,
                ["ModelYear"] = v.ModelYear,
                ["Vin"] = v.Vin,
                ["CurrentOdometerKm"] = v.CurrentOdometerKm,
                ["MaxWeightKg"] = v.MaxWeightKg,
                ["MaxVolumeM3"] = v.MaxVolumeM3,
                ["MaxStops"] = v.MaxStops,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, v.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, v.StatusCodeId),
                ["NextDocumentExpiry"] = nextExpiry.TryGetValue(v.VehicleId, out var exp) ? exp : (DateOnly?)null,
                ["IsActive"] = v.IsActive,
                ["CreatedAtUtc"] = v.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>
/// Fuente: choferes del tenant (estado actual → sin rango de fecha). Zona/Área = zona de despacho primaria
/// (DriverZone.IsPrimary; Área = DispatchZone.Name). Tope efectivo = el propio o el default del tenant (R4).
/// Nunca expone tarifas, viajes, usuario vinculado (solo HasUser) ni tokens de dispositivo.
/// </summary>
public sealed class DriverDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.Driver;
    public string LabelEs => "Choferes";
    public string LabelEn => "Drivers";
    /// <summary>Habilita los campos personalizados del chofer como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.Driver;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Code", "Código", "Code", DataFieldType.Text),
        new DataField("FullName", "Nombre", "Full name", DataFieldType.Text),
        new DataField("ZoneCode", "Zona", "Zone", DataFieldType.Text),
        new DataField("Area", "Área", "Area", DataFieldType.Text),
        new DataField("MaxStopsPerRoute", "Tope de paradas propio", "Own max stops", DataFieldType.Number),
        new DataField("EffectiveMaxStops", "Tope de paradas", "Max stops", DataFieldType.Number),
        new DataField("HireDate", "Fecha de ingreso", "Hire date", DataFieldType.Date),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("LicenseExpiry", "Vencimiento de licencia", "License expiry", DataFieldType.Date),
        new DataField("HasUser", "Tiene usuario", "Has user", DataFieldType.Bool),
        new DataField("IsActive", "Activo", "Active", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creado", "Created", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.Drivers.AsNoTracking().AsQueryable();
        List<int>? wanted = null;
        if (q.Ids is not null)
        {
            var ids = q.Ids.ToList();
            wanted = ids;
            query = query.Where(d => ids.Contains(d.DriverId));
        }
        var drivers = await query.OrderBy(d => d.EmployeeCode).ThenBy(d => d.DriverId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (drivers.Count == 0) return new List<DataRow>();

        var lang = tenant.Lang;
        var today = ClientDataSourceHelpers.Today();
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.DriverStatus, ct);

        // Default del tenant para el tope de paradas (R4); Tenant es la raíz, se lee por el id del principal.
        var tenantId = tenant.TenantId;
        int? tenantDefault = tenantId is null
            ? null
            : await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId.Value)
                .Select(t => (int?)t.MaxStopsPerRouteDefault).FirstOrDefaultAsync(ct);

        // Zona primaria: DriverZone no lleva TenantId → se une con Drivers y DispatchZones (ambos bajo el filtro de tenant).
        var zoneQuery = from dz in db.DriverZones.AsNoTracking()
                        join d in db.Drivers.AsNoTracking() on dz.DriverId equals d.DriverId
                        join z in db.DispatchZones.AsNoTracking() on dz.DispatchZoneId equals z.DispatchZoneId
                        where dz.IsPrimary
                        select new { dz.DriverId, z.DispatchZoneId, z.Code, z.Name };
        if (wanted is { } zoneDriverIds) zoneQuery = zoneQuery.Where(z => zoneDriverIds.Contains(z.DriverId));
        var zones = (await zoneQuery.ToListAsync(ct))
            .GroupBy(z => z.DriverId)
            .ToDictionary(g => g.Key, g => g.OrderBy(z => z.DispatchZoneId).First());

        // Vencimiento de licencia: licencias activas NO superadas (helper compartido, regla "vigente por tipo").
        var docs = await db.LoadFleetDocumentsAsync(today,
            new FleetDocumentScope(IncludeVehicles: false, IncludeDrivers: true, OnlyActiveOwners: false, DriverIds: wanted), ct);
        var licenseExpiry = docs
            .Where(r => r.OwnerKind == FleetOwnerKinds.Driver && r.DocumentKind == FleetDocumentKinds.License
                        && !r.IsSuperseded && r.ExpiryDate.HasValue)
            .GroupBy(r => r.OwnerId)
            .ToDictionary(g => g.Key, g => g.Min(r => r.ExpiryDate!.Value));

        var rows = new List<DataRow>(drivers.Count);
        foreach (var d in drivers)
        {
            var zone = zones.GetValueOrDefault(d.DriverId);
            rows.Add(new DataRow
            {
                ["Id"] = d.DriverId,
                ["PublicId"] = d.PublicId.ToString(),
                ["Code"] = d.EmployeeCode,
                ["FullName"] = d.FullName,
                ["ZoneCode"] = zone?.Code,
                ["Area"] = zone?.Name,
                ["MaxStopsPerRoute"] = d.MaxStopsPerRoute,
                ["EffectiveMaxStops"] = FleetRules.EffectiveMaxStops(d.MaxStopsPerRoute, tenantDefault),
                ["HireDate"] = d.HireDate,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, d.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, d.StatusCodeId),
                ["LicenseExpiry"] = licenseExpiry.TryGetValue(d.DriverId, out var exp) ? exp : (DateOnly?)null,
                ["HasUser"] = d.UserId.HasValue,
                ["IsActive"] = d.IsActive,
                ["CreatedAtUtc"] = d.CreatedAtUtc,
            });
        }
        return rows;
    }
}

/// <summary>
/// Fuente: órdenes de trabajo de mantenimiento (EntityType WORK_ORDER, ya sembrado). Actividad de período por CreatedAtUtc
/// (desde inclusivo, hasta exclusivo). Relación muchos-a-uno Vehicle → VEHICLE. Base del indicador "Órdenes de trabajo
/// abiertas" (IsActive + StatusCode in [OPEN, IN_PROGRESS]).
/// </summary>
public sealed class WorkOrderDataSource(TeikemDbContext db, ILookupCache lookups, ITenantContext tenant) : IDataSource
{
    public string Key => EntityTypes.WorkOrder;
    public string LabelEs => "Órdenes de trabajo";
    public string LabelEn => "Work orders";
    /// <summary>Habilita los campos personalizados de la orden de trabajo como columnas (módulo F).</summary>
    public string? EntityTypeCode => EntityTypes.WorkOrder;
    public string? DateField => "CreatedAtUtc";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Operations;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("PublicId", "Id público", "Public id", DataFieldType.Text),
        new DataField("Number", "Número", "Number", DataFieldType.Text),
        new DataField("VehicleId", "Id de vehículo", "Vehicle id", DataFieldType.Number),
        new DataField("VehicleCode", "Vehículo", "Vehicle", DataFieldType.Text),
        new DataField("MaintenanceType", "Tipo de mantenimiento", "Maintenance type", DataFieldType.Text),
        new DataField("MaintenanceTypeCode", "Código de tipo de mantenimiento", "Maintenance type code", DataFieldType.Text),
        new DataField("ScheduleName", "Programa", "Schedule", DataFieldType.Text),
        new DataField("Status", "Estatus", "Status", DataFieldType.Text),
        new DataField("StatusCode", "Código de estatus", "Status code", DataFieldType.Text),
        new DataField("ScheduledDate", "Fecha programada", "Scheduled date", DataFieldType.Date),
        new DataField("CompletedDate", "Fecha de cierre", "Completed date", DataFieldType.Date),
        new DataField("OdometerKm", "Odómetro (km)", "Odometer (km)", DataFieldType.Number),
        new DataField("Vendor", "Proveedor", "Vendor", DataFieldType.Text),
        new DataField("LaborCost", "Costo de labor", "Labor cost", DataFieldType.Number, IsMoney: true),
        new DataField("PartsCost", "Costo de partes", "Parts cost", DataFieldType.Number, IsMoney: true),
        new DataField("TotalCost", "Costo total", "Total cost", DataFieldType.Number, IsMoney: true),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        new DataField("CreatedAtUtc", "Creada el", "Created at", DataFieldType.Date),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Vehicle", EntityTypes.Vehicle, "VehicleId", "Vehículo", "Vehicle"),
    };

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        var query = db.MaintenanceWorkOrders.AsNoTracking().AsQueryable();
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(w => wanted.Contains(w.WorkOrderId));
        }
        // Rango sobre CreatedAtUtc: desde inclusivo, hasta exclusivo (el resolutor de rangos entrega "mañana 00:00")
        if (q.FromUtc.HasValue) query = query.Where(w => w.CreatedAtUtc >= q.FromUtc.Value);
        if (q.ToUtc.HasValue) query = query.Where(w => w.CreatedAtUtc < q.ToUtc.Value);

        var orders = await query.OrderByDescending(w => w.CreatedAtUtc).ThenByDescending(w => w.WorkOrderId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (orders.Count == 0) return new List<DataRow>();

        var lang = tenant.Lang;
        var statusMap = await ClientDataSourceHelpers.StatusMapAsync(db, StatusDomains.WorkOrderStatus, ct);

        // Códigos de vehículo y nombres de programa: una consulta cada uno (sin N+1), bajo el filtro de tenant.
        var vehicleIds = orders.Select(w => w.VehicleId).Distinct().ToList();
        var vehicleCodes = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId))
            .Select(v => new { v.VehicleId, v.Code }).ToDictionaryAsync(v => v.VehicleId, v => v.Code, ct);

        var scheduleIds = orders.Where(w => w.MaintenanceScheduleId.HasValue).Select(w => w.MaintenanceScheduleId!.Value).Distinct().ToList();
        var scheduleNames = scheduleIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.MaintenanceSchedules.AsNoTracking().Where(s => scheduleIds.Contains(s.MaintenanceScheduleId))
                .Select(s => new { s.MaintenanceScheduleId, s.Name }).ToDictionaryAsync(s => s.MaintenanceScheduleId, s => s.Name, ct);

        var rows = new List<DataRow>(orders.Count);
        foreach (var w in orders)
        {
            rows.Add(new DataRow
            {
                ["Id"] = w.WorkOrderId,
                ["PublicId"] = w.PublicId.ToString(),
                ["Number"] = w.Number,
                ["VehicleId"] = w.VehicleId,
                ["VehicleCode"] = vehicleCodes.GetValueOrDefault(w.VehicleId),
                ["MaintenanceType"] = await ClientDataSourceHelpers.LookupLabelAsync(lookups, w.MaintenanceTypeLookupId, lang, ct),
                ["MaintenanceTypeCode"] = await ClientDataSourceHelpers.LookupCodeAsync(lookups, w.MaintenanceTypeLookupId, ct),
                ["ScheduleName"] = w.MaintenanceScheduleId is int sid ? scheduleNames.GetValueOrDefault(sid) : null,
                ["Status"] = ClientDataSourceHelpers.StatusLabel(statusMap, w.StatusCodeId, lang),
                ["StatusCode"] = ClientDataSourceHelpers.StatusCodeOf(statusMap, w.StatusCodeId),
                ["ScheduledDate"] = w.ScheduledDate,
                ["CompletedDate"] = w.CompletedDate,
                ["OdometerKm"] = w.OdometerKm,
                ["Vendor"] = w.Vendor,
                ["LaborCost"] = w.LaborCost,
                ["PartsCost"] = w.PartsCost,
                ["TotalCost"] = w.TotalCost,
                ["IsActive"] = w.IsActive,
                ["CreatedAtUtc"] = w.CreatedAtUtc,
            });
        }
        return rows;
    }
}
