using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P3) — optimización de la ruta, reordenamiento manual con ETAs y pin manual por parada.
///
/// OPTIMIZACIÓN EN TRES FASES (el motor nunca corre con el Trip bloqueado):
/// - FASE 1 (transacción #1): bloquea el Trip (UPDLOCK con TenantId), valida RowVersion (409) y que la ruta sea editable
///   (422), arma la solicitud del motor con las paradas de la versión vigente y crea la corrida PENDING con su historial de
///   nacimiento. No modifica el Trip: su RowVersion sigue siendo el de la instantánea. Las validaciones no crean corrida.
/// - FASE 2 (sin transacción ni bloqueos): IRouteOptimizer con un token combinado con RouteEditRules.EngineTimeout y
///   validación del resultado (RouteOptimizationRules.ValidateResult). Excepción, timeout o resultado inválido → FASE E.
/// - FASE 3 (transacción #2): vuelve a bloquear el Trip; si la versión vigente, el RowVersion o la editabilidad cambiaron,
///   la corrida pasa a ERROR y se responde 409 (RouteEditRules.StaleMessage). Si no, libera lo que no cupo, crea la versión
///   n+1 (OPTIMIZED, archiva la anterior), pasa el Trip de DRAFT a PLANNED y cierra la corrida en OK.
/// - FASE E (transacción NUEVA, con CancellationToken.None): la corrida pasa PENDING → ERROR con el motivo recortado a 4000
///   y se responde 409 (RouteEditRules.EngineErrorMessage). Una corrida nunca queda PENDING por un error de este servicio.
///
/// Orden de bloqueo del lote: Trip → órdenes (las bloquea RouteWriter). Toda escritura de TripOrder / Route / RouteStop pasa
/// por RouteWriter; las coordenadas (GEOGRAPHY) solo por TripQueries (SQL crudo con 'TenantId =').
/// </summary>
public sealed class RouteOptimizationService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    IRouteOptimizer optimizer,
    RouteWriter writer,
    TripReadService reader,
    ILogger<RouteOptimizationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ================================================================ POST /trips/{publicId}/optimize

    public async Task<OptimizationResultDto> OptimizeAsync(Guid publicId, OptimizeRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();

        // ---------- FASE 1: instantánea + corrida PENDING (transacción #1) ----------
        var prepared = await db.RunInTransactionAsync(ct2 => PrepareRunAsync(publicId, req?.RowVersion, ct2), ct);
        var runId = prepared.RunId;

        // ---------- FASE 2: el motor, fuera de la transacción y sin bloqueos ----------
        RouteOptimizationResult result;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(RouteEditRules.EngineTimeout);
            // WaitAsync: un motor que ignore el token tampoco retiene la solicitud más allá del timeout.
            result = await optimizer.OptimizeAsync(prepared.Request, linked.Token).WaitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkRunErrorAsync(runId, RouteEditRules.CancelledRunErrorMessage, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Optimización {RunId}: el motor {Engine} excedió {Timeout}.", runId, optimizer.EngineCode, RouteEditRules.EngineTimeout);
            await MarkRunErrorAsync(runId, RouteEditRules.EngineTimeoutRunErrorMessage, CancellationToken.None);
            throw new ConflictException(RouteEditRules.EngineErrorMessage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Optimización {RunId}: el motor {Engine} falló.", runId, optimizer.EngineCode);
            await MarkRunErrorAsync(runId, $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);
            throw new ConflictException(RouteEditRules.EngineErrorMessage);
        }

        var invalid = result is null ? "el motor no devolvió resultado." : RouteOptimizationRules.ValidateResult(prepared.Request, result);
        if (invalid is not null)
        {
            logger.LogWarning("Optimización {RunId}: resultado inválido del motor {Engine}: {Error}", runId, optimizer.EngineCode, invalid);
            await MarkRunErrorAsync(runId, RouteEditRules.InvalidResultRunErrorPrefix + invalid, CancellationToken.None);
            throw new ConflictException(RouteEditRules.EngineErrorMessage);
        }

        // ---------- FASE 3: aplicar solo si la ruta no cambió (transacción #2) ----------
        ApplyOutcome outcome;
        try
        {
            outcome = await db.RunInTransactionAsync(ct2 => ApplyResultAsync(prepared, result!, ct2), ct);
        }
        catch (Exception ex)
        {
            // La transacción #2 se revirtió: la corrida sigue PENDING. Se cierra en ERROR en una transacción aparte.
            if (ex is not TeikemException) logger.LogError(ex, "Optimización {RunId}: falló al aplicar el resultado.", runId);
            var reason = ex switch
            {
                OperationCanceledException when ct.IsCancellationRequested => RouteEditRules.CancelledRunErrorMessage,
                TeikemException te => te.Message,
                _ => $"{ex.GetType().Name}: {ex.Message}",
            };
            await MarkRunErrorAsync(runId, reason, CancellationToken.None);
            throw;
        }

        // La corrida descartada ya quedó en ERROR dentro de la transacción #2; el 409 se lanza FUERA para no revertirla.
        if (outcome.Stale) throw new ConflictException(RouteEditRules.StaleMessage);

        var trip = await reader.GetByIdAsync(prepared.Snapshot.TripId, ct);
        return new OptimizationResultDto(
            RunId: runId,
            EngineCode: optimizer.EngineCode,
            StatusCode: OptimizationRunStatuses.Ok,
            RouteVersion: outcome.RouteVersion,
            AssignedCount: result!.OrderedStopIds.Count,
            UnassignedCount: outcome.Unassigned.Count,
            Unassigned: outcome.Unassigned,
            Polyline: result.Polyline,
            Trip: trip);
    }

    /// <summary>FASE 1. Corre dentro de la transacción #1; no modifica el Trip.</summary>
    private async Task<PreparedRun> PrepareRunAsync(Guid publicId, string? rowVersion, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var trip = await db.LockTripAsync(publicId, ct);                 // 404 'Ruta no encontrada.'
        EnsureRowVersion(trip, rowVersion);                              // 409 (explícito: esta fase no guarda el Trip)
        await writer.EnsureEditableAsync(trip, ct);                      // 422 'La ruta … ya fue despachada …'

        var route = await db.ActiveRouteAsync(trip.TripId, false, ct);
        var facts = route is null ? new List<StopFact>() : await StopFactsAsync(route.RouteId, ct);
        if (facts.Count == 0) throw new StatusRuleException(RouteEditRules.NoStopsMessage);

        var request = await BuildRequestAsync(trip.VehicleId, trip.PlannedStartUtc, facts, ct);

        var pending = await statuses.GetByCodeAsync(StatusDomains.OptimizationRunStatus, OptimizationRunStatuses.Pending, ct);
        var run = new OptimizationRun
        {
            TenantId = tenantId,
            TripId = trip.TripId,
            RouteId = route!.RouteId,                                    // versión previa; al cerrar en OK apunta a la nueva
            EngineLookupId = await lookups.GetIdAsync(LookupDomains.OptimizerEngine, optimizer.EngineCode, ct),
            StatusCodeId = pending.StatusCodeId,
            RequestJson = JsonSerializer.Serialize(request, JsonOptions), // sin nombres de personas (PlanStopInput no los lleva)
            StartedAtUtc = DateTime.UtcNow,
            CreatedBy = tenant.UserId,
        };
        db.OptimizationRuns.Add(run);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        // Historial de nacimiento bajo OPTIMIZATION_RUN (id int).
        var born = await statuses.TransitionAsync(StatusDomains.OptimizationRunStatus, EntityTypes.OptimizationRun,
            run.OptimizationRunId, null, OptimizationRunStatuses.Pending, null, ct);
        run.StatusCodeId = born.StatusCodeId;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        var snapshot = new OptimizationSnapshot(trip.TripId, route.RouteId, trip.RowVersion ?? Array.Empty<byte>());
        return new PreparedRun(run.OptimizationRunId, snapshot, request, facts.ToDictionary(f => f.OrderStopId));
    }

    /// <summary>FASE 3. Corre dentro de la transacción #2 con el Trip bloqueado de nuevo.</summary>
    private async Task<ApplyOutcome> ApplyResultAsync(PreparedRun prepared, RouteOptimizationResult result, CancellationToken ct)
    {
        var trip = await db.LockTripAsync(prepared.Snapshot.TripId, ct);
        var tripStatusCode = await StatusCodeOfAsync(trip.StatusCodeId, ct);
        var current = await db.ActiveRouteAsync(trip.TripId, false, ct);
        var isEditable = trip.IsActive && TripRules.IsEditable(tripStatusCode);

        var run = await db.OptimizationRuns.FirstAsync(r => r.OptimizationRunId == prepared.RunId, ct);

        if (RouteEditRules.IsStale(prepared.Snapshot, current?.RouteId, trip.RowVersion, isEditable))
        {
            await CloseRunAsync(run, OptimizationRunStatuses.Error, ct);
            run.ErrorMessage = RouteEditRules.StaleRunErrorMessage;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
            return new ApplyOutcome(true, 0, Array.Empty<UnassignedStopDto>());
        }

        // 1) Versión n+1 OPTIMIZED con TODAS las paradas (las asignadas en el orden del motor y, al final, las que no cupieron):
        //    así la versión n se archiva INTACTA (paradas, secuencias y ETAs del plan anterior, L264). Se guarda antes de liberar.
        var fullOrder = result.OrderedStopIds.Concat(result.Unassigned.Select(u => u.OrderStopId)).ToList();
        await writer.ReplaceActiveRouteAsync(trip, fullOrder, RouteStatuses.Optimized,
            RouteEditRules.OptimizationComment(prepared.RunId), ct);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        // 2) Lo que no cupo vuelve a 'sin asignar': DELETE de su TripOrder y de su parada en la versión n+1 (ya vigente). Como
        //    quedaron al final, la resecuencia conserva 1..k el orden del motor. Luego se guarda y se recalcula.
        var unassigned = new List<UnassignedStopDto>(result.Unassigned.Count);
        var planUnassigned = new List<RoutePlanUnassigned>(result.Unassigned.Count);
        foreach (var u in result.Unassigned)
        {
            var fact = prepared.Stops[u.OrderStopId];
            await writer.ReleaseOrderAsync(trip, fact.TransportOrderId, ct);
            unassigned.Add(new UnassignedStopDto(fact.OrderPublicId, fact.OrderNumber, u.ReasonCode, UnassignedReasons.Message(u.ReasonCode)));
            planUnassigned.Add(new RoutePlanUnassigned(fact.OrderPublicId, fact.OrderNumber, u.ReasonCode));
        }
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        await writer.RecomputeAsync(trip, ct);

        var newRoute = await db.ActiveRouteAsync(trip.TripId, false, ct)
                       ?? throw new InvalidOperationException("La optimización no dejó una versión vigente de la ruta.");

        // 3) DRAFT → PLANNED (si el tenant tiene PLANNED habilitado; si no, la ruta sigue en DRAFT y se despacha desde ahí).
        if (tripStatusCode == TripStatuses.Draft && await IsEnabledAsync(StatusDomains.TripStatus, TripStatuses.Planned, ct))
        {
            var to = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId,
                TripStatuses.Planned, RouteEditRules.TripOptimizedComment(newRoute.Version), ct);
            trip.StatusCodeId = to.StatusCodeId;
        }

        // 4) La corrida cierra en OK con la versión creada, el plan (tolerante al leerse) y los totales recalculados.
        await CloseRunAsync(run, OptimizationRunStatuses.Ok, ct);
        run.RouteId = newRoute.RouteId;
        run.ResponseJson = RoutePlanJson.Serialize(new RoutePlanResponse(result.OrderedStopIds, planUnassigned, result.Polyline));
        run.TotalDistanceKm = trip.TotalDistanceKm;
        run.TotalDurationMin = trip.TotalDurationMin;
        run.UnassignedCount = unassigned.Count;

        // 5) Carrera en la BD (UX_Route_Trip_Active / UQ_Route_Version / RowVersion) → 409.
        await db.SaveGuardedAsync(RouteEditRules.StaleMessage, ct);
        return new ApplyOutcome(false, newRoute.Version, unassigned);
    }

    /// <summary>FASE E: la corrida PENDING pasa a ERROR en una transacción NUEVA (con CancellationToken.None desde el llamador).</summary>
    private async Task MarkRunErrorAsync(int runId, string message, CancellationToken ct)
    {
        try
        {
            await db.RunInTransactionAsync(async ct2 =>
            {
                var run = await db.OptimizationRuns.FirstOrDefaultAsync(r => r.OptimizationRunId == runId, ct2);
                if (run is null) return;
                var status = await StatusCodeOfAsync(run.StatusCodeId, ct2);
                if (status != OptimizationRunStatuses.Pending) return;   // ya cerrada (p. ej. descartada en la FASE 3)
                await CloseRunAsync(run, OptimizationRunStatuses.Error, ct2);
                run.ErrorMessage = RouteEditRules.TrimRunError(message);
                await db.SaveChangesAsync(ct2);
            }, ct);
        }
        catch (Exception ex)
        {
            // No se oculta el error original del llamador por no poder registrar la corrida.
            logger.LogError(ex, "No se pudo registrar el ERROR de la corrida de optimización {RunId}.", runId);
        }
    }

    /// <summary>PENDING → OK | ERROR por StatusService (historial OPTIMIZATION_RUN) y CompletedAtUtc.</summary>
    private async Task CloseRunAsync(OptimizationRun run, string toCode, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.OptimizationRunStatus, EntityTypes.OptimizationRun,
            run.OptimizationRunId, run.StatusCodeId, toCode, null, ct);
        run.StatusCodeId = to.StatusCodeId;
        run.CompletedAtUtc = DateTime.UtcNow;
    }

    // ================================================================ PUT /trips/{publicId}/route/sequence

    /// <summary>
    /// Reordena a mano la versión vigente (misma versión, sin corrida) y recalcula las ETAs. La secuencia debe ser una
    /// permutación exacta de las paradas de la versión vigente: ids de otra ruta u otro tenant → 400 SequenceMessage.
    /// </summary>
    public async Task<TripDetailDto> ReorderAsync(Guid publicId, RouteSequenceRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        if (req?.RouteStopIds is null || req.RouteStopIds.Count == 0)
            throw new ValidationException("routeStopIds", TripRules.SequenceMessage);
        var requested = req.RouteStopIds.ToList();

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);             // 404 'Ruta no encontrada.'
            EnsureRowVersion(trip, req.RowVersion);                       // 409
            await writer.EnsureEditableAsync(trip, ct2);                  // 422 en rutas despachadas o cerradas
            await writer.ApplySequenceAsync(trip, requested, ct2);        // 400 SequenceMessage
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            await writer.RecomputeAsync(trip, ct2);                       // ETAs con la nueva secuencia
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(publicId, ct);
    }

    // ================================================================ PUT /trips/{publicId}/stops/{routeStopId}/location

    /// <summary>
    /// Pin manual de una parada: fija la coordenada de su OrderStop (GEOGRAPHY por TripQueries), marca la precisión MANUAL
    /// (por EF: el AuditLog de TRANSPORT_ORDER registra el cambio) y recalcula las ETAs. Solo en rutas DRAFT/PLANNED. La
    /// parada debe ser de la versión VIGENTE de ESA ruta (BOLA por id hijo) → 404 'Parada no encontrada en esta ruta.'
    /// </summary>
    public async Task<TripDetailDto> SetStopLocationAsync(Guid tripPublicId, int routeStopId, StopLocationRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        if (RouteEditRules.ValidateLocation(req?.Lat, req?.Lng) is { } error)
            throw new ValidationException(error.Field, error.Message);
        var lat = req!.Lat!.Value;
        var lng = req.Lng!.Value;

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(tripPublicId, ct2);         // 404 'Ruta no encontrada.'
            EnsureRowVersion(trip, req.RowVersion);                       // 409
            await writer.EnsureEditableAsync(trip, ct2);                  // 422 si ya fue despachada

            var route = await db.ActiveRouteAsync(trip.TripId, false, ct2);
            var orderStopId = route is null
                ? (int?)null
                : await db.RouteStops.AsNoTracking()
                    .Where(s => s.RouteStopId == routeStopId && s.RouteId == route.RouteId)
                    .Select(s => (int?)s.OrderStopId)
                    .FirstOrDefaultAsync(ct2);
            if (orderStopId is null) throw new StopNotInTripException();

            // Segunda barrera: la parada de la orden se alcanza por su TransportOrder (filtro de tenant).
            var orderStop = await (from os in db.OrderStops
                                   join o in db.TransportOrders on os.TransportOrderId equals o.TransportOrderId
                                   where os.OrderStopId == orderStopId.Value
                                   select os).FirstOrDefaultAsync(ct2)
                            ?? throw new StopNotInTripException();

            await db.SetStopPointAsync(orderStopId.Value, lat, lng, ct2);  // SQL crudo con 'TenantId =' (en esta transacción)
            orderStop.GeocodeAccuracyLookupId = await lookups.GetIdAsync(LookupDomains.GeocodeAccuracy, GeocodeAccuracies.Manual, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);

            await writer.RecomputeAsync(trip, ct2);                       // ETAs con la coordenada nueva
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(tripPublicId, ct);
    }

    // ================================================================ GET /trips/{publicId}/optimization-runs

    /// <summary>Corridas de la ruta, de la más reciente a la más antigua, con motor, estatus, versión resultante y quién la lanzó.</summary>
    public async Task<IReadOnlyList<OptimizationRunDto>> ListRunsAsync(Guid publicId, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();
        var tripId = await db.Trips.AsNoTracking().Where(t => t.PublicId == publicId).Select(t => (int?)t.TripId).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException(TripQueries.TripLabel, null, true);

        var runs = await db.OptimizationRuns.AsNoTracking()
            .Where(r => r.TripId == tripId)
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.OptimizationRunId)
            .ToListAsync(ct);
        if (runs.Count == 0) return Array.Empty<OptimizationRunDto>();

        var routeIds = runs.Where(r => r.RouteId.HasValue).Select(r => r.RouteId!.Value).Distinct().ToList();
        var versions = await db.Routes.AsNoTracking()
            .Where(r => r.TripId == tripId && routeIds.Contains(r.RouteId))
            .ToDictionaryAsync(r => r.RouteId, r => r.Version, ct);

        var statusById = (await statuses.GetPipelineAsync(StatusDomains.OptimizationRunStatus, true, ct)).ToDictionary(s => s.Id);

        var userIds = runs.Where(r => r.CreatedBy.HasValue).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, string?>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);

        var engines = new Dictionary<int, (string Code, string Label)>();
        foreach (var engineId in runs.Select(r => r.EngineLookupId).Distinct())
        {
            var engine = await lookups.GetAsync(engineId, ct);
            engines[engineId] = engine is null
                ? (string.Empty, string.Empty)
                : (engine.InternalCode, MultilingualText.Resolve(engine.LabelJson, tenant.Lang));
        }

        return runs.Select(r =>
        {
            var status = statusById.GetValueOrDefault(r.StatusCodeId);
            var statusCode = status?.Code ?? string.Empty;
            var engine = engines[r.EngineLookupId];
            // Versión RESULTANTE: solo las corridas OK crearon una versión; las demás apuntan a la versión que intentaron reemplazar.
            int? version = statusCode == OptimizationRunStatuses.Ok && r.RouteId is int rid && versions.TryGetValue(rid, out var v) ? v : null;
            return new OptimizationRunDto(
                Id: r.OptimizationRunId,
                EngineCode: engine.Code,
                Engine: engine.Label,
                StatusCode: statusCode,
                Status: status?.Label ?? string.Empty,
                RouteId: r.RouteId,
                RouteVersion: version,
                UnassignedCount: r.UnassignedCount,
                TotalDistanceKm: r.TotalDistanceKm,
                TotalDurationMin: r.TotalDurationMin,
                ErrorMessage: r.ErrorMessage,
                StartedAtUtc: r.StartedAtUtc,
                CompletedAtUtc: r.CompletedAtUtc,
                StartedBy: r.CreatedBy is int uid ? users.GetValueOrDefault(uid) : null);
        }).ToList();
    }

    // ================================================================ helpers

    /// <summary>Datos de una parada de la versión vigente para el motor y para liberar lo que no cupo.</summary>
    private sealed record StopFact(
        int OrderStopId, int TransportOrderId, Guid OrderPublicId, string OrderNumber,
        string? PostalCode, string City, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes,
        decimal? WeightKg, decimal? VolumeM3);

    private sealed record PreparedRun(int RunId, OptimizationSnapshot Snapshot, RouteOptimizationRequest Request, IReadOnlyDictionary<int, StopFact> Stops);

    private sealed record ApplyOutcome(bool Stale, int RouteVersion, IReadOnlyList<UnassignedStopDto> Unassigned);

    /// <summary>404 con el mensaje exacto del manual (BOLA por id hijo en el pin manual).</summary>
    private sealed class StopNotInTripException() : TeikemException(TripRules.StopNotInTripMessage, 404, "not_found");

    /// <summary>
    /// Paradas de una versión de ruta en su secuencia. El JOIN a db.TransportOrders (filtro de tenant) es la segunda
    /// barrera: una parada cuya orden no fuera del tenant no entra al motor.
    /// </summary>
    private async Task<List<StopFact>> StopFactsAsync(int routeId, CancellationToken ct)
        => await (from rs in db.RouteStops.AsNoTracking()
                  join os in db.OrderStops.AsNoTracking() on rs.OrderStopId equals os.OrderStopId
                  join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                  where rs.RouteId == routeId
                  orderby rs.Sequence, rs.RouteStopId
                  select new StopFact(
                      rs.OrderStopId, o.TransportOrderId, o.PublicId, o.OrderNumber,
                      os.SnapPostalCode, os.SnapCity, os.WindowStartUtc, os.WindowEndUtc, os.ServiceMinutes,
                      o.TotalWeightKg, o.TotalVolumeM3))
            .ToListAsync(ct);

    /// <summary>
    /// Solicitud del motor: por parada ventana, servicio, CP, pueblo, zona (DispatchZoneMatcher sobre las zonas activas;
    /// ambigua = sin zona), peso y volumen de la orden y coordenadas (TripQueries); capacidad del vehículo (sin límites si la
    /// ruta no tiene vehículo). StartUtc = hora de salida planificada.
    /// </summary>
    private async Task<RouteOptimizationRequest> BuildRequestAsync(int? vehicleId, DateTime? startUtc, List<StopFact> facts, CancellationToken ct)
    {
        var capacity = new VehicleCapacity(MaxStops: null, MaxWeightKg: null, MaxVolumeM3: null);
        if (vehicleId is int vid)
        {
            var v = await db.Vehicles.AsNoTracking().Where(x => x.VehicleId == vid)
                .Select(x => new { x.MaxStops, x.MaxWeightKg, x.MaxVolumeM3 }).FirstOrDefaultAsync(ct);
            if (v is not null) capacity = new VehicleCapacity(MaxStops: v.MaxStops, MaxWeightKg: v.MaxWeightKg, MaxVolumeM3: v.MaxVolumeM3);
        }

        var members = await db.ActiveZoneMembersAsync(ct);
        var points = await db.StopPointsAsync(facts.Select(f => f.OrderStopId).Distinct().ToList(), ct);

        var stops = facts.Select(f =>
        {
            var zone = DispatchZoneMatcher.Resolve(members, f.PostalCode, f.City);
            var hasPoint = points.TryGetValue(f.OrderStopId, out var p);
            return new PlanStopInput(
                OrderStopId: f.OrderStopId,
                OrderNumber: f.OrderNumber,
                ZoneCode: zone.Ambiguous ? null : zone.ZoneCode,
                PostalCode: f.PostalCode,
                City: f.City,
                WindowStartUtc: f.WindowStartUtc,
                WindowEndUtc: f.WindowEndUtc,
                ServiceMinutes: f.ServiceMinutes,
                WeightKg: f.WeightKg,
                VolumeM3: f.VolumeM3,
                Lat: hasPoint ? p!.Lat : null,
                Lng: hasPoint ? p!.Lng : null);
        }).ToList();

        return new RouteOptimizationRequest(StartUtc: startUtc, Capacity: capacity, Stops: stops);
    }

    /// <summary>
    /// 409 si el cliente leyó otra versión del Trip. Se valida explícitamente (no solo al guardar) porque la FASE 1 de la
    /// optimización no modifica el Trip; con el Trip ya bloqueado (UPDLOCK) nadie más puede cambiarlo en esta transacción.
    /// </summary>
    private void EnsureRowVersion(Trip trip, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) return;
        db.ApplyRowVersion(trip, rowVersion);                             // 400 si no es base64 válido
        var prop = db.Entry(trip).Property("RowVersion");
        var expected = prop.OriginalValue as byte[] ?? Array.Empty<byte>();
        var actual = prop.CurrentValue as byte[] ?? Array.Empty<byte>();
        if (expected.Length == 0) return;
        if (!expected.AsSpan().SequenceEqual(actual)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct)
           ?? string.Empty;

    private async Task<bool> IsEnabledAsync(string statusDomain, string code, CancellationToken ct)
        => (await statuses.GetPipelineAsync(statusDomain, false, ct)).Any(s => s.Code == code);
}
