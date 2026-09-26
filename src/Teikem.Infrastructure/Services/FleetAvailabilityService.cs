using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 4 (P3) — disponibilidad para despacho (R7). Arma los snapshots por lote (sin N+1) y los evalúa con las reglas
/// puras FleetAvailabilityRules. La usan el panel (GET /fleet/availability), la entrega especial con chofer (P7) y la
/// usará el planificador de Despacho.
/// - Documentos: EXCLUSIVAMENTE FleetQueries.LoadFleetDocumentsAsync (regla "vigente por tipo" ya aplicada).
/// - OT en proceso: MaintenanceWorkOrder activas en IN_PROGRESS (filtro de tenant), una consulta por lote.
/// - Estatus: IsInitial/terminal del StatusCode; la etiqueta respeta la personalización del tenant.
/// - Zona: la primaria (DriverZone.IsPrimary) unida a DispatchZone (filtro de tenant).
/// </summary>
public sealed class FleetAvailabilityService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups) : IFleetAvailabilityService
{
    private sealed record StatusInfo(string Code, string Label, bool IsInitial, bool IsTerminal);
    private sealed record DriverHead(int DriverId, Guid PublicId, string Code, string FullName, bool IsActive, int StatusCodeId);
    private sealed record VehicleHead(int VehicleId, Guid PublicId, string Code, string? PlateNumber, int? VehicleTypeLookupId, bool IsActive, int StatusCodeId);

    /// <summary>
    /// Disponibilidad de todos los choferes y vehículos del tenant que no están dados de baja definitiva (los inactivos
    /// aparecen como no disponibles). onlyAvailable deja solo los disponibles.
    /// </summary>
    public async Task<FleetAvailabilityDto> GetAsync(DateOnly date, bool onlyAvailable, CancellationToken ct)
    {
        var driverStatus = await StatusMapAsync(StatusDomains.DriverStatus, ct);
        var vehicleStatus = await StatusMapAsync(StatusDomains.VehicleStatus, ct);

        var drivers = (await DriverHeadsAsync(null, ct)).Where(d => !IsTerminal(driverStatus, d.StatusCodeId)).ToList();
        var vehicles = (await VehicleHeadsAsync(null, ct)).Where(v => !IsTerminal(vehicleStatus, v.StatusCodeId)).ToList();

        // Lote completo del tenant: una sola lectura de documentos (sin lista de ids; los dados de baja se descartan al indexar).
        var docs = await db.LoadFleetDocumentsAsync(date, new FleetDocumentScope(IncludeVehicles: true, IncludeDrivers: true, OnlyActiveOwners: false), ct);
        var labels = new Dictionary<int, string>();
        var docsByOwner = await IndexDocumentsAsync(docs, labels, ct);
        var inProgress = await InProgressWorkOrdersAsync(null, ct);
        var zones = await PrimaryZoneCodesAsync(drivers.Select(d => d.DriverId).ToList(), ct);

        var driverItems = new List<DriverAvailabilityDto>(drivers.Count);
        foreach (var d in drivers.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase))
        {
            var result = FleetAvailabilityRules.EvaluateDriver(DriverSnapshot(d, driverStatus, docsByOwner), date);
            if (onlyAvailable && !result.Available) continue;
            driverItems.Add(new DriverAvailabilityDto(d.DriverId, d.PublicId, d.Code, d.FullName, zones.GetValueOrDefault(d.DriverId),
                result.Available, ToDtos(result)));
        }

        var typeCodes = new Dictionary<int, string?>();
        var vehicleItems = new List<VehicleAvailabilityDto>(vehicles.Count);
        foreach (var v in vehicles.OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase))
        {
            var result = FleetAvailabilityRules.EvaluateVehicle(VehicleSnapshot(v, vehicleStatus, docsByOwner, inProgress), date);
            if (onlyAvailable && !result.Available) continue;
            vehicleItems.Add(new VehicleAvailabilityDto(v.VehicleId, v.PublicId, v.Code, v.PlateNumber,
                await TypeCodeAsync(typeCodes, v.VehicleTypeLookupId, ct), result.Available, ToDtos(result)));
        }

        return new FleetAvailabilityDto(date, driverItems, vehicleItems);
    }

    /// <summary>Evalúa un chofer del tenant (404 'Chofer no encontrado.' si no existe en la compañía).</summary>
    public async Task<AvailabilityResult> CheckDriverAsync(int driverId, DateOnly date, CancellationToken ct)
    {
        var d = (await DriverHeadsAsync(new[] { driverId }, ct)).FirstOrDefault() ?? throw new NotFoundException("Chofer");
        var status = await StatusMapAsync(StatusDomains.DriverStatus, ct);
        var docs = await db.LoadFleetDocumentsAsync(date,
            new FleetDocumentScope(IncludeVehicles: false, IncludeDrivers: true, OnlyActiveOwners: false, DriverIds: new[] { driverId }), ct);
        var docsByOwner = await IndexDocumentsAsync(docs, new Dictionary<int, string>(), ct);
        return FleetAvailabilityRules.EvaluateDriver(DriverSnapshot(d, status, docsByOwner), date);
    }

    /// <summary>Evalúa un vehículo del tenant (404 'Vehículo no encontrado.' si no existe en la compañía).</summary>
    public async Task<AvailabilityResult> CheckVehicleAsync(int vehicleId, DateOnly date, CancellationToken ct)
    {
        var v = (await VehicleHeadsAsync(new[] { vehicleId }, ct)).FirstOrDefault() ?? throw new NotFoundException("Vehículo");
        var status = await StatusMapAsync(StatusDomains.VehicleStatus, ct);
        var docs = await db.LoadFleetDocumentsAsync(date,
            new FleetDocumentScope(IncludeVehicles: true, IncludeDrivers: false, OnlyActiveOwners: false, VehicleIds: new[] { vehicleId }), ct);
        var docsByOwner = await IndexDocumentsAsync(docs, new Dictionary<int, string>(), ct);
        var inProgress = await InProgressWorkOrdersAsync(new[] { vehicleId }, ct);
        return FleetAvailabilityRules.EvaluateVehicle(VehicleSnapshot(v, status, docsByOwner, inProgress), date);
    }

    // ---------------------------------------------------------------- snapshots

    private static DriverAvailabilitySnapshot DriverSnapshot(DriverHead d, IReadOnlyDictionary<int, StatusInfo> status,
        IReadOnlyDictionary<(string Kind, int Id), List<(FleetDocumentRow Row, AvailabilityDocSnapshot Snap)>> docsByOwner)
    {
        var s = status.GetValueOrDefault(d.StatusCodeId);
        var own = docsByOwner.GetValueOrDefault((FleetOwnerKinds.Driver, d.DriverId)) ?? new();
        return new DriverAvailabilitySnapshot(d.DriverId, d.IsActive, s?.IsInitial ?? false, s?.IsTerminal ?? false, s?.Label ?? "",
            own.Where(x => x.Row.DocumentKind == FleetDocumentKinds.License).Select(x => x.Snap).ToList(),
            own.Where(x => x.Row.DocumentKind == FleetDocumentKinds.Certification).Select(x => x.Snap).ToList());
    }

    private static VehicleAvailabilitySnapshot VehicleSnapshot(VehicleHead v, IReadOnlyDictionary<int, StatusInfo> status,
        IReadOnlyDictionary<(string Kind, int Id), List<(FleetDocumentRow Row, AvailabilityDocSnapshot Snap)>> docsByOwner,
        IReadOnlyDictionary<int, List<string>> inProgress)
    {
        var s = status.GetValueOrDefault(v.StatusCodeId);
        var own = docsByOwner.GetValueOrDefault((FleetOwnerKinds.Vehicle, v.VehicleId)) ?? new();
        return new VehicleAvailabilitySnapshot(v.VehicleId, v.IsActive, s?.IsInitial ?? false, s?.IsTerminal ?? false, s?.Label ?? "",
            own.Select(x => x.Snap).ToList(), (IReadOnlyList<string>?)inProgress.GetValueOrDefault(v.VehicleId) ?? Array.Empty<string>());
    }

    /// <summary>Documentos del helper agrupados por dueño, con la etiqueta del tipo ya resuelta (ILookupCache).</summary>
    private async Task<Dictionary<(string Kind, int Id), List<(FleetDocumentRow Row, AvailabilityDocSnapshot Snap)>>> IndexDocumentsAsync(
        IReadOnlyList<FleetDocumentRow> docs, Dictionary<int, string> labels, CancellationToken ct)
    {
        var map = new Dictionary<(string Kind, int Id), List<(FleetDocumentRow Row, AvailabilityDocSnapshot Snap)>>();
        foreach (var r in docs)
        {
            var label = await FleetDocumentService.LabelAsync(lookups, labels, r, tenant.Lang, ct);
            var key = (r.OwnerKind, r.OwnerId);
            if (!map.TryGetValue(key, out var list)) map[key] = list = new();
            list.Add((r, new AvailabilityDocSnapshot(r.DocTypeCode, label, r.ExpiryDate, r.IsSuperseded)));
        }
        return map;
    }

    // ---------------------------------------------------------------- lecturas por lote

    private async Task<List<DriverHead>> DriverHeadsAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var q = db.Drivers.AsNoTracking();
        if (ids is not null)
        {
            var wanted = ids.ToList();
            q = q.Where(d => wanted.Contains(d.DriverId));
        }
        return await q.Select(d => new DriverHead(d.DriverId, d.PublicId, d.EmployeeCode, d.FullName, d.IsActive, d.StatusCodeId)).ToListAsync(ct);
    }

    private async Task<List<VehicleHead>> VehicleHeadsAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var q = db.Vehicles.AsNoTracking();
        if (ids is not null)
        {
            var wanted = ids.ToList();
            q = q.Where(v => wanted.Contains(v.VehicleId));
        }
        return await q.Select(v => new VehicleHead(v.VehicleId, v.PublicId, v.Code, v.PlateNumber, v.VehicleTypeLookupId, v.IsActive, v.StatusCodeId))
            .ToListAsync(ct);
    }

    /// <summary>Números de OT activas en IN_PROGRESS por vehículo (una consulta; NULL = todos los vehículos del tenant).</summary>
    private async Task<Dictionary<int, List<string>>> InProgressWorkOrdersAsync(IReadOnlyCollection<int>? vehicleIds, CancellationToken ct)
    {
        var inProgressId = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.WorkOrderStatus && s.InternalCode == WorkOrderStatuses.InProgress)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (inProgressId is null) return new Dictionary<int, List<string>>();

        var q = db.MaintenanceWorkOrders.AsNoTracking().Where(w => w.IsActive && w.StatusCodeId == inProgressId.Value);
        if (vehicleIds is not null)
        {
            var wanted = vehicleIds.ToList();
            q = q.Where(w => wanted.Contains(w.VehicleId));
        }
        var rows = await q.Select(w => new { w.VehicleId, w.Number }).ToListAsync(ct);
        return rows.GroupBy(r => r.VehicleId).ToDictionary(g => g.Key, g => g.Select(r => r.Number).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>Código de la zona primaria por chofer (DispatchZone bajo el filtro de tenant; DriverZone no lleva TenantId).</summary>
    private async Task<Dictionary<int, string>> PrimaryZoneCodesAsync(List<int> driverIds, CancellationToken ct)
    {
        if (driverIds.Count == 0) return new Dictionary<int, string>();
        var rows = await (from dz in db.DriverZones.AsNoTracking()
                          join z in db.DispatchZones.AsNoTracking() on dz.DispatchZoneId equals z.DispatchZoneId
                          where dz.IsPrimary && driverIds.Contains(dz.DriverId)
                          select new { dz.DriverId, z.Code }).ToListAsync(ct);
        return rows.GroupBy(r => r.DriverId).ToDictionary(g => g.Key, g => g.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal).First());
    }

    /// <summary>Estatus del dominio con la etiqueta personalizada del tenant (si existe), marca de inicial y de terminal.</summary>
    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == domain).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        return codes.ToDictionary(c => c.StatusCodeId, c => new StatusInfo(
            c.InternalCode,
            MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, overrides.GetValueOrDefault(c.StatusCodeId)?.CustomLabelJson), tenant.Lang),
            c.IsInitial,
            c.StageKind?.InternalCode == StageKinds.Terminal));
    }

    private static bool IsTerminal(IReadOnlyDictionary<int, StatusInfo> map, int statusCodeId)
        => map.TryGetValue(statusCodeId, out var s) && s.IsTerminal;

    private async Task<string?> TypeCodeAsync(Dictionary<int, string?> cache, int? lookupId, CancellationToken ct)
    {
        if (lookupId is not int id) return null;
        if (cache.TryGetValue(id, out var code)) return code;
        code = (await lookups.GetAsync(id, ct))?.InternalCode;
        cache[id] = code;
        return code;
    }

    private static IReadOnlyList<AvailabilityIssueDto> ToDtos(AvailabilityResult r)
        => r.Issues.Select(i => new AvailabilityIssueDto(i.Code, i.Message, i.Blocking)).ToList();
}
