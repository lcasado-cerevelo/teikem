using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P9) — 'Planificar el día' (POST /trips/plan-day, trips.plan). Cubre L262: agrupar las órdenes confirmadas sin ruta
/// de la fecha por zona de despacho, en la ruta abierta de la zona o en una nueva con el chofer estándar.
/// Flujo:
/// 1. Validación (DayPlanningRules.ValidateRequest): fecha entre ayer y +60 días; como máximo 50 zonas. Zonas del tenant
///    (404 'Zona de despacho no encontrada.') y activas (400 'La zona de despacho está inactiva.'). Sin lista: todas las
///    zonas activas del tenant.
/// 2. TripService.PrepareNumberingAsync en autocommit, ANTES de la transacción.
/// 3. Una transacción con el ORDEN DE BLOQUEO ÚNICO del lote:
///    a. la fila Tenant (TripQueries.LockTenantPlanningAsync): dos planificaciones simultáneas se serializan y la segunda
///       ve lo que hizo la primera (una sola ruta por zona);
///    b. las rutas abiertas (activas, DRAFT/PLANNED) de la fecha y de esas zonas, por TripId ascendente, re-verificadas
///       bajo bloqueo; por zona gana la de menor id (misma regla que TripRules.PickOpenTrip);
///    c. el pool (TripOrderService.PoolAsync, RequestedDate ≤ fecha o nula) y el reparto puro (DayPlanningRules.Allocate);
///    d. las órdenes a asignar, por id ascendente en una sola sentencia (TripQueries.LockOrdersAsync); las que otra operación
///       asignó entre la lectura y el bloqueo salen del reparto (DayPlanningRules.RemoveTaken);
///    e. se escribe: rutas nuevas (TripService.CreateTrackedAsync: chofer por defecto de la zona, sin vehículo, salida
///       12:00 UTC) y RouteWriter.AddOrdersAsync por zona (re-verifica elegibilidad y ruta vigente bajo bloqueo).
/// 4. Respuesta por zona: la ruta (creada o reutilizada), asignadas, omitidas y avisos informativos (TripIssueBuilder:
///    NO_VEHICLE, OVER_STOP_LIMIT, …; más un aviso 'Orden {n}: {motivo}' por orden omitida).
/// Reglas: idempotente (repetirla no crea rutas si ya hay una abierta y solo agrega las órdenes que siguen sin ruta); no
/// optimiza, no despacha, no asigna vehículo, no cambia OrderStatus y no reasigna órdenes que ya tienen ruta.
/// </summary>
public sealed class TripDayPlanningService(
    TeikemDbContext db,
    ITenantContext tenant,
    TripService trips,
    TripOrderService tripOrders,
    RouteWriter writer,
    TripIssueBuilder issueBuilder)
{
    public const string ZoneLabel = "Zona de despacho";

    /// <summary>Resultado interno de una zona dentro de la transacción (se completa con los avisos después del commit).</summary>
    private sealed record ZoneOutcome(
        int ZoneId, int? TripId, Guid? TripPublicId, string? TripCode, bool TripCreated,
        int OrdersAssigned, IReadOnlyList<DayPlanningSkip> Skipped);

    private sealed record PlanOutcome(IReadOnlyList<ZoneOutcome> Zones, int OrdersWithoutZone);

    public async Task<PlanDayResultDto> PlanDayAsync(PlanDayRequest? req, CancellationToken ct)
    {
        ((TenantContext)tenant).RequireTenantId();

        // 1. Validación de la solicitud.
        var check = DayPlanningRules.ValidateRequest(req?.PlanDate, req?.DispatchZoneIds, Today());
        if (!check.IsValid) throw new ValidationException(check.Field!, check.Error!);
        var planDate = check.PlanDate!.Value;
        var createEmptyTrips = req!.CreateEmptyTrips;

        // Zonas destino (bajo el filtro de tenant): id → código.
        Dictionary<int, string> zoneCodes;
        if (check.ZoneIds is { Count: > 0 } requestedIds)
        {
            var requested = requestedIds.ToList();
            var zones = await db.DispatchZones.AsNoTracking()
                .Where(z => requested.Contains(z.DispatchZoneId))
                .Select(z => new { z.DispatchZoneId, z.Code, z.IsActive })
                .ToListAsync(ct);
            if (zones.Count != requested.Count) throw new NotFoundException(ZoneLabel, null, true);   // ajena o inexistente, sin oráculo
            if (zones.Any(z => !z.IsActive)) throw new ValidationException("dispatchZoneIds", DriverRules.ZoneInactiveMessage);
            zoneCodes = zones.ToDictionary(z => z.DispatchZoneId, z => z.Code);
        }
        else
        {
            zoneCodes = await db.DispatchZones.AsNoTracking()
                .Where(z => z.IsActive)
                .ToDictionaryAsync(z => z.DispatchZoneId, z => z.Code, ct);
        }
        var targetZones = zoneCodes.Keys.OrderBy(id => id).ToList();
        var openStatusIds = await OpenStatusIdsAsync(ct);

        // 2. Contador TRIP en autocommit, antes de la transacción.
        await trips.PrepareNumberingAsync(ct);

        // 3. Transacción con el orden de bloqueo del lote: Tenant → Trips (id asc) → órdenes (id asc) → escritura.
        var outcome = await db.RunInTransactionAsync(async ct2 =>
        {
            // a. Serializa 'Planificar el día' por tenant.
            await db.LockTenantPlanningAsync(ct2);

            // b. Rutas abiertas de la fecha y de esas zonas, bloqueadas por TripId ascendente y re-verificadas bajo bloqueo.
            var openByZone = new Dictionary<int, Trip>();
            if (targetZones.Count > 0)
            {
                var candidateTripIds = await db.Trips.AsNoTracking()
                    .Where(t => t.IsActive && t.PlanDate == planDate && t.DispatchZoneId != null
                                && targetZones.Contains(t.DispatchZoneId.Value) && openStatusIds.Contains(t.StatusCodeId))
                    .Select(t => t.TripId)
                    .ToListAsync(ct2);
                if (candidateTripIds.Count > 0)
                {
                    foreach (var trip in await db.LockTripsAsync(candidateTripIds.OrderBy(id => id), ct2))
                    {
                        if (trip is null || !trip.IsActive || trip.PlanDate != planDate || !openStatusIds.Contains(trip.StatusCodeId)
                            || trip.DispatchZoneId is not int zoneId || !zoneCodes.ContainsKey(zoneId)) continue;
                        if (!openByZone.TryGetValue(zoneId, out var current) || trip.TripId < current.TripId)
                            openByZone[zoneId] = trip;   // PickOpenTrip: la de menor id
                    }
                }
            }

            // Paradas vigentes de cada ruta abierta (una orden = una parada DELIVERY), para el tope técnico de 300.
            var openTripIds = openByZone.Values.Select(t => t.TripId).ToList();
            var stopsByTrip = openTripIds.Count == 0
                ? new Dictionary<int, int>()
                : await db.TripOrders.AsNoTracking()
                    .Where(o => o.IsCurrent && openTripIds.Contains(o.TripId))
                    .GroupBy(o => o.TripId)
                    .Select(g => new { TripId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.TripId, x => x.Count, ct2);
            var openStops = openByZone.ToDictionary(kv => kv.Key, kv => stopsByTrip.GetValueOrDefault(kv.Value.TripId));

            // c. Pool de órdenes sin ruta vigente (zonas destino + sin zona/ambiguas para el conteo) y reparto puro.
            var pool = await tripOrders.PoolAsync(new UnassignedPoolFilter(planDate, targetZones, IncludeNoZone: true), ct2);
            var candidates = pool
                .Select(c => new DayPlanningCandidate(c.TransportOrderId, c.OrderNumber, c.ZoneAmbiguous ? null : c.ZoneId,
                    c.ZoneAmbiguous, c.RequestedDate, c.IneligibleReason))
                .ToList();
            var allocation = DayPlanningRules.Allocate(candidates, openStops, targetZones, createEmptyTrips);

            // d. Órdenes a asignar bloqueadas en una sola sentencia (id ascendente); las que otra operación ya asignó salen.
            var toAssign = allocation.Zones.SelectMany(z => z.Assigned).Distinct().OrderBy(id => id).ToList();
            if (toAssign.Count > 0)
            {
                await db.LockOrdersAsync(toAssign, false, ct2);
                var taken = await db.TripOrders.AsNoTracking()
                    .Where(o => o.IsCurrent && toAssign.Contains(o.TransportOrderId))
                    .Select(o => o.TransportOrderId)
                    .ToListAsync(ct2);
                allocation = DayPlanningRules.RemoveTaken(allocation, taken, createEmptyTrips);
            }

            // e. Escritura por zona: ruta nueva si hace falta y órdenes al final de la ruta.
            var results = new List<ZoneOutcome>(allocation.Zones.Count);
            foreach (var zone in allocation.Zones)
            {
                openByZone.TryGetValue(zone.ZoneId, out var trip);
                var created = false;
                if (trip is null && zone.CreateTrip)
                {
                    // Chofer por defecto de la zona (si es único, disponible y CATALOG está encendido), sin vehículo y
                    // salida por defecto 12:00 UTC de la fecha.
                    trip = await trips.CreateTrackedAsync(
                        new TripCreateSpec(planDate, zone.ZoneId, DriverId: null, VehicleId: null, PlannedStartUtc: null, UseDefaultDriver: true), ct2);
                    created = true;
                }

                if (trip is not null && zone.Assigned.Count > 0)
                    await writer.AddOrdersAsync(trip, zone.Assigned, ct2);   // re-verifica bajo bloqueo; atómico

                results.Add(new ZoneOutcome(zone.ZoneId, trip?.TripId, trip?.PublicId, trip?.Code, created,
                    trip is null ? 0 : zone.Assigned.Count, zone.Skipped));
            }

            await db.SaveGuardedAsync(TripRules.OrderTakenMessage, ct2);   // carrera en UX_TripOrder_Current → 409
            return new PlanOutcome(results, allocation.OrdersWithoutZone);
        }, ct);

        // 4. Avisos informativos por ruta (después del commit; no bloquean nada aquí).
        var tripIds = outcome.Zones.Where(z => z.TripId.HasValue).Select(z => z.TripId!.Value).Distinct().ToList();
        var issuesByTrip = tripIds.Count == 0
            ? new Dictionary<int, IReadOnlyList<TripIssueDto>>()
            : (await issueBuilder.BuildAsync(tripIds, false, ct)).ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<TripIssueDto>)kv.Value.Select(i => new TripIssueDto(i.Code, i.Message, i.Blocking)).ToList());

        var zoneResults = outcome.Zones.Select(z =>
        {
            var issues = new List<TripIssueDto>();
            if (z.TripId is int tripId && issuesByTrip.TryGetValue(tripId, out var tripIssues)) issues.AddRange(tripIssues);
            issues.AddRange(z.Skipped.Select(s =>
                new TripIssueDto(s.ReasonCode, DayPlanningRules.SkippedIssueMessage(s.OrderNumber, s.Reason), false)));
            return new PlanDayZoneResultDto(z.ZoneId, zoneCodes.GetValueOrDefault(z.ZoneId) ?? string.Empty,
                z.TripPublicId, z.TripCode, z.TripCreated, z.OrdersAssigned, z.Skipped.Count, issues);
        }).ToList();

        return new PlanDayResultDto(
            planDate,
            TripsCreated: outcome.Zones.Count(z => z.TripCreated),
            OrdersAssigned: outcome.Zones.Sum(z => z.OrdersAssigned),
            OrdersSkipped: outcome.Zones.Sum(z => z.Skipped.Count),
            OrdersWithoutZone: outcome.OrdersWithoutZone,
            Zones: zoneResults);
    }

    /// <summary>Ids de los estatus 'abiertos' de TripStatus (DRAFT y PLANNED).</summary>
    private async Task<List<int>> OpenStatusIdsAsync(CancellationToken ct)
    {
        var codes = new[] { TripStatuses.Draft, TripStatuses.Planned };
        return await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.TripStatus && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId)
            .ToListAsync(ct);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
