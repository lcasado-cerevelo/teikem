using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Fleet;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P1) — cabecera de la ruta (Trip): alta con número automático, edición, 'Eliminar ruta' y reasignación en bloque
/// del chofer por zona. El CONTENIDO (órdenes, paradas y secuencia) nunca se toca desde aquí: es de RouteWriter.
/// - Número 'AAAA-####' (NumberSequence TRIP por tenant, ClientId NULL): PrepareNumberingAsync (EnsureAsync) en autocommit
///   ANTES de la transacción y NextAsync dentro (bloqueo de fila hasta el commit: altas simultáneas consecutivas, sin huecos).
/// - Chofer por defecto: con zona y sin chofer, el único chofer activo con esa zona como primaria, si está disponible en la
///   fecha y CATALOG está encendido (si está apagado se omite sin error).
/// - Hora de salida por defecto: 12:00 UTC (08:00 AST) de la fecha del plan, para que siempre haya ETA.
/// - Disponibilidad del Lote 4 (IFleetAvailabilityService) al asignar chofer o vehículo: 409 con los motivos bloqueantes.
/// - Toda escritura con el Trip bloqueado (TripQueries.LockTripAsync, UPDLOCK con TenantId) dentro de RunInTransactionAsync.
/// - Cabecera de una ruta despachada: solo si el tenant habilita la capacidad EDIT_TRIP en su estatus; aun así la fecha y la
///   zona no cambian.
/// - Cambios de estatus solo vía StatusService (el efecto de TripStatus libera y archiva al eliminar).
/// </summary>
public sealed class TripService(
    TeikemDbContext db,
    ITenantContext tenant,
    StatusService statuses,
    INumberSequenceService numbers,
    IFleetAvailabilityService availability,
    ModuleService modules,
    RouteWriter writer,
    TripReadService reader)
{
    private const string DriverDoubleBookedCode = "DRIVER_DOUBLE_BOOKED";

    // ================================================================ numeración (antes de la transacción)

    /// <summary>Asegura la fila del contador TRIP en autocommit. Se llama SIEMPRE antes de abrir la transacción.</summary>
    public Task PrepareNumberingAsync(CancellationToken ct) => numbers.EnsureAsync(NumberKinds.Trip, null, ct);

    // ================================================================ alta dentro de la transacción del llamador

    /// <summary>
    /// Crea la ruta dentro de la transacción del llamador (la usan POST /trips y 'Planificar el día'). Valida zona (404/400),
    /// chofer y vehículo explícitos (CATALOG 403, 404, disponibilidad 409), aplica el chofer por defecto si se pide, toma el
    /// siguiente número y registra el historial de nacimiento. Devuelve el Trip tracked y guardado.
    /// </summary>
    public async Task<Trip> CreateTrackedAsync(TripCreateSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("CreateTrackedAsync requiere una transacción abierta (RunInTransactionAsync).");

        if (spec.DispatchZoneId is int zoneId) await EnsureZoneUsableAsync(zoneId, ct);

        var plannedStart = spec.PlannedStartUtc is DateTime s ? TripPlanningRules.AsUtc(s) : TripPlanningRules.DefaultPlannedStart(spec.PlanDate);
        if (TripPlanningRules.ValidatePlannedStart(plannedStart, spec.PlanDate) is string startError)
            throw new ValidationException("plannedStartUtc", startError);

        var driverId = spec.DriverId;
        if (driverId is int explicitDriver)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);           // 403 module_disabled
            await EnsureDriverAvailableAsync(explicitDriver, spec.PlanDate, ct); // 404 / 409
        }
        else if (spec.UseDefaultDriver && spec.DispatchZoneId is int defaultZone && await modules.IsEnabledAsync(ModuleKeys.Catalog, ct))
        {
            driverId = await DefaultDriverAsync(defaultZone, spec.PlanDate, ct);
        }

        if (spec.VehicleId is int vehicleId)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
            await EnsureVehicleAvailableAsync(vehicleId, spec.PlanDate, ct);
        }

        var initial = await statuses.GetInitialAsync(StatusDomains.TripStatus, ct);
        var seq = await numbers.NextAsync(NumberKinds.Trip, null, ct);
        var trip = new Trip
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Code = TripPlanningRules.FormatCode(spec.PlanDate, seq),
            PlanDate = spec.PlanDate,
            DispatchZoneId = spec.DispatchZoneId,
            DriverId = driverId,
            VehicleId = spec.VehicleId,
            StatusCodeId = initial.StatusCodeId,
            PlannedStartUtc = plannedStart,
            IsActive = true,
        };
        db.Trips.Add(trip);
        await db.SaveGuardedAsync(TripPlanningRules.DuplicateCodeMessage, ct); // UQ_Trip_Code es la segunda barrera

        // Historial de nacimiento (null → etapa inicial del tenant; TripStatusEffect lo ignora).
        var born = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, null, initial.InternalCode, null, ct);
        trip.StatusCodeId = born.StatusCodeId;
        await db.SaveGuardedAsync(TripPlanningRules.DuplicateCodeMessage, ct);
        return trip;
    }

    // ================================================================ POST /trips

    public async Task<TripDetailDto> CreateAsync(TripCreateRequest req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        if (req is null) throw new ValidationException("planDate", TripPlanningRules.PlanDateRequiredMessage);

        var errors = new Dictionary<string, string[]>();
        if (TripPlanningRules.ValidatePlanDate(req.PlanDate, Today()) is string dateError) errors["planDate"] = new[] { dateError };
        else if (req.PlannedStartUtc is DateTime start && TripPlanningRules.ValidatePlannedStart(start, req.PlanDate!.Value) is string startError)
            errors["plannedStartUtc"] = new[] { startError };
        ThrowIfAny(errors);
        var planDate = req.PlanDate!.Value;

        // Zona antes que chofer y vehículo (mismo orden que la validación dentro de la transacción).
        if (req.DispatchZoneId is int zoneId) await EnsureZoneUsableAsync(zoneId, ct);

        int? driverId = null, vehicleId = null;
        if (req.DriverPublicId is Guid driverPublicId)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
            driverId = (await db.ResolveDriverAsync(driverPublicId, false, ct)).DriverId;     // 404 'Chofer no encontrado.'
        }
        if (req.VehiclePublicId is Guid vehiclePublicId)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
            vehicleId = (await db.ResolveVehicleAsync(vehiclePublicId, false, ct)).VehicleId; // 404 'Vehículo no encontrado.'
        }

        await PrepareNumberingAsync(ct);
        var spec = new TripCreateSpec(planDate, req.DispatchZoneId, driverId, vehicleId, req.PlannedStartUtc, UseDefaultDriver: true);
        var publicId = await db.RunInTransactionAsync(async ct2 => (await CreateTrackedAsync(spec, ct2)).PublicId, ct);
        return await reader.GetAsync(publicId, ct);
    }

    // ================================================================ PATCH /trips/{id}

    /// <summary>
    /// Edición de la cabecera (null = sin cambio; Clear* quita el dato). Orden: llaves prohibidas (400), flags en conflicto
    /// (400), y dentro de la transacción con el Trip bloqueado: RowVersion (409), cerrada (422), sin EDIT_TRIP (422),
    /// despachada con fecha/zona (422), disponibilidad del chofer/vehículo (409) y recálculo de ETAs si cambia la salida.
    /// </summary>
    public async Task<TripDetailDto> UpdateAsync(Guid publicId, TripPatchRequest req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        req ??= new TripPatchRequest();

        if (TripPlanningRules.CheckForbiddenKeys(req.Extra?.Keys) is { } forbidden)
            throw new ValidationException(forbidden.Field, forbidden.Message);

        var flags = new TripPatchFlags(
            HasDriver: req.DriverPublicId.HasValue, ClearDriver: req.ClearDriver == true,
            HasVehicle: req.VehiclePublicId.HasValue, ClearVehicle: req.ClearVehicle == true,
            HasZone: req.DispatchZoneId.HasValue, ClearZone: req.ClearZone == true,
            HasPlannedStart: req.PlannedStartUtc.HasValue, ClearPlannedStart: req.ClearPlannedStart == true);
        var flagErrors = TripPlanningRules.ValidatePatchFlags(flags);
        ThrowIfAny(flagErrors.ToDictionary(kv => kv.Key, kv => new[] { kv.Value }));

        // Referencias del request resueltas antes de bloquear (404/403 sin retener bloqueos).
        if (req.DispatchZoneId is int zoneId) await EnsureZoneUsableAsync(zoneId, ct);
        int? newDriverId = null, newVehicleId = null;
        if (req.DriverPublicId is Guid driverPublicId)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
            newDriverId = (await db.ResolveDriverAsync(driverPublicId, false, ct)).DriverId;
        }
        if (req.VehiclePublicId is Guid vehiclePublicId)
        {
            await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
            newVehicleId = (await db.ResolveVehicleAsync(vehiclePublicId, false, ct)).VehicleId;
        }

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);                     // 404 'Ruta no encontrada.'
            EnsureRowVersion(trip, req.RowVersion);

            var status = await StatusOfAsync(trip.StatusCodeId, ct2);
            if (!trip.IsActive || status.IsTerminal)
                throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, status.IsTerminal ? status.Code : TripStatuses.Cancelled));
            if (!await statuses.IsAllowedAsync(EntityTypes.Trip, trip.StatusCodeId, Capabilities.EditTrip, ct2))
                throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, status.Code));

            // Valores finales
            var planDate = req.PlanDate ?? trip.PlanDate;
            int? zone = req.ClearZone == true ? null : req.DispatchZoneId ?? trip.DispatchZoneId;
            int? driverId = req.ClearDriver == true ? null : newDriverId ?? trip.DriverId;
            int? vehicleId = req.ClearVehicle == true ? null : newVehicleId ?? trip.VehicleId;
            DateTime? plannedStart = req.ClearPlannedStart == true
                ? null
                : req.PlannedStartUtc is DateTime ps
                    ? TripPlanningRules.AsUtc(ps)
                    : planDate != trip.PlanDate ? TripPlanningRules.ShiftPlannedStart(trip.PlannedStartUtc, trip.PlanDate, planDate) : trip.PlannedStartUtc;

            var change = new TripHeaderChange(
                PlanDate: planDate != trip.PlanDate,
                Zone: zone != trip.DispatchZoneId,
                Driver: driverId != trip.DriverId,
                Vehicle: vehicleId != trip.VehicleId,
                PlannedStart: plannedStart != trip.PlannedStartUtc);

            // Ruta despachada (el tenant habilitó EDIT_TRIP): solo chofer, vehículo y hora de salida.
            if (TripPlanningRules.IsDispatchedStatus(status.Code) && TripPlanningRules.ValidateDispatchedPatch(change) is string dispatchedError)
                throw new StatusRuleException(dispatchedError);
            // ... y el chofer, el vehículo y la hora de salida se cambian, no se quitan (el despacho los exige).
            if (TripPlanningRules.IsDispatchedStatus(status.Code)
                && TripPlanningRules.ValidateDispatchedValues(driverId, vehicleId, plannedStart, trip.PlannedStartUtc) is string requiredError)
                throw new StatusRuleException(requiredError);

            // La fecha nueva (solo si cambia) debe caer entre ayer y +60 días.
            if (change.PlanDate && TripPlanningRules.ValidatePlanDate(planDate, Today()) is string dateError)
                throw new ValidationException("planDate", dateError);

            if (plannedStart is DateTime finalStart && (change.PlannedStart || change.PlanDate)
                && TripPlanningRules.ValidatePlannedStart(finalStart, planDate) is string startError)
                throw new ValidationException("plannedStartUtc", startError);

            // Disponibilidad en la fecha final: del chofer/vehículo nuevo, o del actual si cambia la fecha.
            if (driverId is int d && (change.Driver || change.PlanDate)) await EnsureDriverAvailableAsync(d, planDate, ct2);
            if (vehicleId is int v && (change.Vehicle || change.PlanDate)) await EnsureVehicleAvailableAsync(v, planDate, ct2);

            trip.PlanDate = planDate;
            trip.DispatchZoneId = zone;
            trip.DriverId = driverId;
            trip.VehicleId = vehicleId;
            trip.PlannedStartUtc = plannedStart;

            // La hora de salida mueve las ETAs de la versión vigente (sin tocar membresía ni secuencia).
            if (change.PlannedStart) await writer.RecomputeAsync(trip, ct2);

            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(publicId, ct);
    }

    // ================================================================ DELETE /trips/{id} ('Eliminar ruta')

    /// <summary>
    /// 'Eliminar ruta': transición a CANCELLED (comentario opcional, 'Ruta eliminada' por defecto). TripStatusEffect libera
    /// las órdenes, archiva la versión vigente y deja la ruta con IsActive = 0. Solo desde DRAFT/PLANNED: una ruta
    /// despachada o cerrada responde 422 con el mensaje de TripRules (antes de consultar al motor de estatus).
    /// </summary>
    public async Task CancelAsync(Guid publicId, TripCancelRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength }) throw new ValidationException("comment", StatusService.CommentTooLongMessage);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);                     // 404 'Ruta no encontrada.'
            EnsureRowVersion(trip, req?.RowVersion);

            var status = await StatusOfAsync(trip.StatusCodeId, ct2);
            if (!trip.IsActive || !TripRules.IsEditable(status.Code))
                throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, trip.IsActive ? status.Code : TripStatuses.Cancelled));

            var to = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId,
                TripStatuses.Cancelled, comment ?? TripPlanningRules.DefaultDeleteComment, ct2);
            trip.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
    }

    // ================================================================ POST /trips/reassign-zone

    /// <summary>
    /// Reasignación en bloque: el chofer indicado pasa a ser el de TODAS las rutas abiertas (activas, DRAFT/PLANNED) de esas
    /// zonas en esa fecha. No toca DriverZone ni rutas despachadas; omite las rutas cuyo estatus niega EDIT_TRIP. Sin rutas: 200 con TripsUpdated 0. Devuelve los ítems
    /// actualizados y los avisos DRIVER_DOUBLE_BOOKED (el chofer queda en más de una ruta ese día; no bloquea).
    /// </summary>
    public async Task<ZoneReassignResultDto> ReassignZoneAsync(ZoneReassignRequest req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        await modules.EnsureEnabledAsync(ModuleKeys.Catalog, ct);
        if (req is null) throw new ValidationException("planDate", TripPlanningRules.PlanDateRequiredMessage);

        var errors = new Dictionary<string, string[]>();
        if (req.PlanDate is null) errors["planDate"] = new[] { TripPlanningRules.PlanDateRequiredMessage };
        var zoneIds = (req.DispatchZoneIds ?? Array.Empty<int>()).Distinct().ToList();
        if (zoneIds.Count == 0) errors["dispatchZoneIds"] = new[] { TripPlanningRules.ZonesRequiredMessage };
        else if (zoneIds.Count > TripPlanningRules.MaxReassignZones) errors["dispatchZoneIds"] = new[] { TripPlanningRules.MaxReassignZonesMessage };
        if (req.DriverPublicId is null) errors["driverPublicId"] = new[] { TripPlanningRules.DriverRequiredMessage };
        ThrowIfAny(errors);
        var planDate = req.PlanDate!.Value;

        var found = await db.DispatchZones.AsNoTracking().CountAsync(z => zoneIds.Contains(z.DispatchZoneId), ct);
        if (found != zoneIds.Count) throw new NotFoundException("Zona de despacho", null, true);

        var driver = await db.ResolveDriverAsync(req.DriverPublicId!.Value, false, ct);   // 404 'Chofer no encontrado.'
        await EnsureDriverAvailableAsync(driver.DriverId, planDate, ct);                     // 409

        // Solo los estatus abiertos cuya capacidad EDIT_TRIP permite cambiar la cabecera (la misma guarda del PATCH): las rutas
        // cuyo estatus la niega se omiten, igual que las despachadas.
        var openIds = new List<int>();
        foreach (var id in await OpenStatusIdsAsync(ct))
            if (await statuses.IsAllowedAsync(EntityTypes.Trip, id, Capabilities.EditTrip, ct)) openIds.Add(id);
        var updated = await db.RunInTransactionAsync(async ct2 =>
        {
            var candidates = await db.Trips.AsNoTracking()
                .Where(t => t.IsActive && t.PlanDate == planDate && t.DispatchZoneId != null && zoneIds.Contains(t.DispatchZoneId.Value)
                            && openIds.Contains(t.StatusCodeId))
                .Select(t => t.TripId).ToListAsync(ct2);
            if (candidates.Count == 0) return new List<int>();

            // Orden de bloqueo del lote: Trips por TripId ascendente; se re-verifica bajo bloqueo.
            var locked = await db.LockTripsAsync(candidates.OrderBy(id => id), ct2);
            var ids = new List<int>();
            foreach (var trip in locked)
            {
                if (trip is null || !trip.IsActive || trip.PlanDate != planDate || !openIds.Contains(trip.StatusCodeId)
                    || trip.DispatchZoneId is not int z || !zoneIds.Contains(z)) continue;
                trip.DriverId = driver.DriverId;
                ids.Add(trip.TripId);
            }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return ids;
        }, ct);

        var items = await reader.ItemsAsync(updated, ct);
        IReadOnlyList<TripIssueDto> issues = Array.Empty<TripIssueDto>();
        if (updated.Count > 0)
        {
            var byTrip = await reader.IssuesAsync(updated, false, ct);
            issues = byTrip.Values.SelectMany(l => l)
                .Where(i => string.Equals(i.Code, DriverDoubleBookedCode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => (i.Code, i.Message)).Select(g => g.First())
                .ToList();
        }
        return new ZoneReassignResultDto(driver.PublicId, driver.EmployeeCode, driver.FullName, updated.Count, items, issues);
    }

    // ================================================================ helpers

    /// <summary>
    /// Control optimista con el Trip recién bloqueado: ApplyRowVersion (400 si no es base64; 409 al guardar si cambió) y,
    /// como el valor actual se leyó bajo UPDLOCK, un rowVersion viejo es 409 de inmediato aunque el PATCH no cambie nada.
    /// </summary>
    private void EnsureRowVersion(Trip trip, string? rowVersion)
    {
        db.ApplyRowVersion(trip, rowVersion);
        if (string.IsNullOrWhiteSpace(rowVersion) || trip.RowVersion is not { Length: > 0 } current) return;
        var sent = Convert.FromBase64String(rowVersion.Trim());
        if (sent.Length > 0 && !sent.AsSpan().SequenceEqual(current)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    /// <summary>Un solo error: 400 con ese mensaje como título; varios: 400 'Datos inválidos.' con el detalle por campo.</summary>
    private static void ThrowIfAny(IDictionary<string, string[]> errors)
    {
        if (errors.Count == 0) return;
        if (errors.Count == 1)
        {
            var only = errors.First();
            throw new ValidationException(only.Key, only.Value[0]);
        }
        throw new ValidationException(errors);
    }

    private sealed record TripStatusInfo(string Code, bool IsTerminal);

    private async Task<TripStatusInfo> StatusOfAsync(int statusCodeId, CancellationToken ct)
    {
        var s = await db.StatusCodes.AsNoTracking().Where(x => x.StatusCodeId == statusCodeId)
            .Select(x => new { x.InternalCode, Terminal = x.StageKind != null && x.StageKind.InternalCode == StageKinds.Terminal })
            .FirstOrDefaultAsync(ct);
        return new TripStatusInfo(s?.InternalCode ?? string.Empty, s?.Terminal ?? false);
    }

    /// <summary>Ids de los estatus 'abiertos' de TripStatus (DRAFT y PLANNED).</summary>
    private async Task<List<int>> OpenStatusIdsAsync(CancellationToken ct)
    {
        var codes = new[] { TripStatuses.Draft, TripStatuses.Planned };
        return await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.TripStatus && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
    }

    /// <summary>Zona del tenant (404 'Zona de despacho no encontrada.') y activa (400 'La zona de despacho está inactiva.').</summary>
    private async Task EnsureZoneUsableAsync(int zoneId, CancellationToken ct)
    {
        var zone = await db.DispatchZones.AsNoTracking().Where(z => z.DispatchZoneId == zoneId)
                       .Select(z => new { z.IsActive }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Zona de despacho", null, true);
        if (!zone.IsActive) throw new ValidationException("dispatchZoneId", DriverRules.ZoneInactiveMessage);
    }

    /// <summary>Disponibilidad del chofer en la fecha (404 si no es del tenant; 409 con los motivos bloqueantes).</summary>
    private async Task EnsureDriverAvailableAsync(int driverId, DateOnly date, CancellationToken ct)
    {
        var check = await availability.CheckDriverAsync(driverId, date, ct);
        if (!check.Available) throw new ConflictException(TripPlanningRules.DriverNotAvailableMessage(BlockingMessages(check)));
    }

    /// <summary>Disponibilidad del vehículo en la fecha (404 si no es del tenant; 409 con los motivos bloqueantes).</summary>
    private async Task EnsureVehicleAvailableAsync(int vehicleId, DateOnly date, CancellationToken ct)
    {
        var check = await availability.CheckVehicleAsync(vehicleId, date, ct);
        if (!check.Available) throw new ConflictException(TripPlanningRules.VehicleNotAvailableMessage(BlockingMessages(check)));
    }

    private static IReadOnlyList<string> BlockingMessages(AvailabilityResult check)
    {
        var blocking = check.Issues.Where(i => i.Blocking).Select(i => i.Message).ToList();
        return blocking.Count > 0 ? blocking : check.Issues.Select(i => i.Message).ToList();
    }

    /// <summary>
    /// Chofer por defecto de la zona: choferes activos con la zona como primaria (DriverZone.IsPrimary, chofer bajo el filtro
    /// de tenant), su disponibilidad en la fecha y TripPlanningRules.PickDefaultDriver (solo si es único y disponible).
    /// </summary>
    private async Task<int?> DefaultDriverAsync(int zoneId, DateOnly date, CancellationToken ct)
    {
        var ids = await (from dz in db.DriverZones.AsNoTracking()
                         join d in db.Drivers.AsNoTracking() on dz.DriverId equals d.DriverId
                         where dz.DispatchZoneId == zoneId && dz.IsPrimary && d.IsActive
                         select d.DriverId).Distinct().ToListAsync(ct);
        if (ids.Count != 1) return null; // ninguno o varios: sin chofer por defecto (no hace falta evaluar disponibilidad)
        var check = await availability.CheckDriverAsync(ids[0], date, ct);
        return TripPlanningRules.PickDefaultDriver(new[] { new DefaultDriverCandidate(ids[0], check.Available) });
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
