using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P6) — estación de escaneo Outbound (trips.scan). Responde siempre 200 con un resultado tipado y la palabra de voz
/// (found / dup / notfound), salvo 400 por el código y 401/403 por autenticación, permiso o módulo.
/// 1. OutboundScanRules.NormalizeCode (400 'Escanee o escriba un código.' / 'El código no puede exceder 40 caracteres.').
/// 2. OrderReadService.LookupAsync(code, OrderScope.Any): el MISMO lookup exacto de /orders/lookup (número &gt; empaque &gt;
///    factura, con su ambigüedad). No se reimplementa la búsqueda.
/// 3. Hechos de la única coincidencia: ruta vigente (TripOrder IsCurrent), elegibilidad (TripRules.CheckEligibility con el
///    pipeline del tenant, ASSIGN_TRIP y parada DELIVERY pendiente), zona de la parada pendiente (DispatchZoneMatcher sobre
///    las zonas activas) y rutas abiertas (activas, DRAFT/PLANNED) de esa zona en PlanDate ?? hoy (UTC), por TripId ascendente
///    (misma regla que TripRules.PickOpenTrip: gana la de menor id).
/// 4. OutboundScanRules.Decide con la precedencia fija.
/// 5. FOUND_ASSIGNED: dentro de una transacción se bloquea la ruta (LockTripAsync; orden de bloqueo del lote: Trip y luego
///    órdenes) y se re-verifica que siga abierta, de la zona y la fecha; si no, se prueba la siguiente ruta abierta y, si no
///    queda ninguna, NO_OPEN_ROUTE. RouteWriter.AddOrdersAsync bloquea la orden y RE-VERIFICA bajo bloqueo la elegibilidad y la
///    ruta vigente.
/// 6. Fuera de la transacción: ConflictException (carrera con otro escaneo o asignación) → se relee la ruta vigente y se
///    responde ALREADY_ASSIGNED; StatusRuleException → NOT_ELIGIBLE con su motivo.
/// Límites: el escaneo nunca crea rutas; no cambia chofer, vehículo ni OrderStatus; no usa CROSSDOCK ni DockAppointment.
/// </summary>
public sealed class OutboundScanService(
    TeikemDbContext db,
    ITenantContext tenant,
    ILookupCache lookups,
    StatusService statuses,
    OrderReadService orders,
    RouteWriter writer)
{
    /// <summary>Intentos de asignación ante un 409 sin ruta vigente visible (p. ej. choque de RowVersion en una carrera).</summary>
    private const int MaxAssignAttempts = 2;

    public async Task<OutboundScanResultDto> ScanAsync(OutboundScanRequest? req, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException("No hay tenant activo en la sesión.");

        // 1. Código.
        var (code, error) = OutboundScanRules.NormalizeCode(req?.Code);
        if (error is not null) throw new ValidationException("code", error);
        var planDate = req?.PlanDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // 2. Lookup exacto del Lote 3 (número > empaque > factura), reutilizado tal cual.
        var lookup = await orders.LookupAsync(code!, OrderScope.Any, ct);
        var matches = lookup.Matches;
        if (matches.Count != 1)
        {
            var none = OutboundScanRules.Decide(new ScanFacts(matches.Count, lookup.MatchedBy, null, null, null, null, null, planDate));
            return ToDto(none, lookup.MatchedBy, null, null, null, null, null);
        }

        var match = matches[0];
        var orderId = match.Id;

        // 3. Hechos de la única coincidencia.
        var facts = await LoadFactsAsync(orderId, planDate, ct);
        var decision = OutboundScanRules.Decide(new ScanFacts(
            1, lookup.MatchedBy, match.OrderNumber, facts.CurrentTrip?.Code, facts.IneligibleReason,
            facts.Zone, facts.OpenTrips.Count > 0 ? facts.OpenTrips[0].Code : null, planDate));

        var result = decision.Outcome switch
        {
            ScanOutcomes.AlreadyAssigned => new Result(decision, facts.CurrentTrip),
            ScanOutcomes.FoundAssigned => await AssignAsync(orderId, match.OrderNumber, lookup.MatchedBy, facts, planDate, ct),
            _ => new Result(decision, null),
        };

        return ToDto(result.Decision, lookup.MatchedBy, match.PublicId, match.OrderNumber, match.PackBatchNumber,
            facts.Zone?.Ambiguous == true ? null : facts.Zone?.ZoneCode, result.Trip);
    }

    // ================================================================ asignación bajo bloqueo (paso 5 y 6)

    private sealed record Result(ScanDecision Decision, TripRef? Trip);

    private async Task<Result> AssignAsync(int orderId, string orderNumber, string? matchedBy, ScanFactsRow facts, DateOnly planDate, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var assigned = await db.RunInTransactionAsync(ct2 => TryAssignInTransactionAsync(orderId, facts, planDate, ct2), ct);
                if (assigned is not null)
                    return new Result(Decide(orderNumber, matchedBy, null, null, facts.Zone, assigned.Code, planDate), assigned);

                // Todas las rutas abiertas dejaron de estarlo (despachadas, eliminadas o cambiadas de zona/fecha).
                return new Result(Decide(orderNumber, matchedBy, null, null, facts.Zone, null, planDate), null);
            }
            catch (ConflictException)
            {
                // Carrera: otro escaneo o una asignación manual ganó la orden. Se relee la ruta vigente fuera de la transacción.
                db.ChangeTracker.Clear();
                var current = await CurrentTripAsync(orderId, ct);
                if (current is not null)
                    return new Result(Decide(orderNumber, matchedBy, current.Code, null, facts.Zone, null, planDate), current);
                if (attempt >= MaxAssignAttempts) throw;
            }
            catch (StatusRuleException ex)
            {
                // La re-verificación bajo bloqueo encontró la orden no elegible (cambió de estatus entre la lectura y el bloqueo).
                db.ChangeTracker.Clear();
                var reason = ex.Errors?.Values.SelectMany(v => v).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? ex.Message;
                return new Result(Decide(orderNumber, matchedBy, null, reason, facts.Zone, null, planDate), null);
            }
        }
    }

    /// <summary>
    /// Dentro de la transacción: bloquea las rutas abiertas candidatas en orden ascendente de TripId (orden de bloqueo del lote),
    /// re-verifica que sigan activas, en DRAFT/PLANNED, de la misma zona y fecha y con cupo (tope técnico de paradas), y agrega la
    /// orden a la primera que cumpla. RouteWriter.AddOrdersAsync bloquea la orden y re-verifica elegibilidad y ruta vigente.
    /// </summary>
    private async Task<TripRef?> TryAssignInTransactionAsync(int orderId, ScanFactsRow facts, DateOnly planDate, CancellationToken ct)
    {
        if (facts.ZoneId is not int zoneId) return null;
        var editableStatusIds = await EditableTripStatusIdsAsync(ct);

        foreach (var candidate in facts.OpenTrips.OrderBy(t => t.TripId))
        {
            Trip trip;
            try { trip = await db.LockTripAsync(candidate.TripId, ct); }
            catch (NotFoundException) { continue; }

            if (!trip.IsActive || trip.DispatchZoneId != zoneId || trip.PlanDate != planDate
                || !editableStatusIds.Contains(trip.StatusCodeId))
                continue;

            var stopCount = await ActiveStopCountAsync(trip.TripId, ct);
            if (stopCount >= TripRules.MaxStopsHardCap) continue;

            await writer.AddOrdersAsync(trip, new[] { orderId }, ct);      // 422 no elegible / 409 ya asignada (re-verificado)
            await db.SaveGuardedAsync(TripRules.OrderTakenMessage, ct);    // carrera en UX_TripOrder_Current → 409
            return new TripRef(trip.TripId, trip.PublicId, trip.Code);
        }
        return null;
    }

    private static ScanDecision Decide(string orderNumber, string? matchedBy, string? currentTripCode, string? ineligible,
        ScanZone? zone, string? openTripCode, DateOnly planDate)
        => OutboundScanRules.Decide(new ScanFacts(1, matchedBy, orderNumber, currentTripCode, ineligible, zone, openTripCode, planDate));

    private static OutboundScanResultDto ToDto(ScanDecision d, string? matchedBy, Guid? orderPublicId, string? orderNumber,
        string? packBatchNumber, string? zoneCode, TripRef? trip)
        => new(d.Outcome, d.Voice, d.Message, matchedBy, orderPublicId, orderNumber, packBatchNumber, zoneCode,
            trip?.PublicId, trip?.Code, d.ReasonCode);

    // ================================================================ hechos (paso 3)

    private sealed record TripRef(int TripId, Guid PublicId, string Code);

    private sealed record ScanFactsRow(TripRef? CurrentTrip, string? IneligibleReason, ScanZone? Zone, int? ZoneId, IReadOnlyList<TripRef> OpenTrips);

    private async Task<ScanFactsRow> LoadFactsAsync(int orderId, DateOnly planDate, CancellationToken ct)
    {
        var current = await CurrentTripAsync(orderId, ct);

        var order = await db.TransportOrders.AsNoTracking()
            .Where(o => o.TransportOrderId == orderId)
            .Select(o => new { o.OrderNumber, o.IsActive, o.IsSpecialDelivery, o.StatusCodeId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(OrderQueries.OrderLabel);   // no ocurre: el lookup la acaba de leer

        // Parada DELIVERY no terminal de menor secuencia: la que se rutea y la que define la zona.
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var terminalStopIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.StopStatus && s.StageKind!.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var stop = await db.OrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == orderId && s.StopTypeLookupId == deliveryTypeId && !terminalStopIds.Contains(s.StatusCodeId))
            .OrderBy(s => s.Sequence).ThenBy(s => s.OrderStopId)
            .Select(s => new { s.SnapPostalCode, s.SnapCity })
            .FirstOrDefaultAsync(ct);

        // Elegibilidad: pipeline del tenant (con etapas deshabilitadas), capacidad ASSIGN_TRIP y parada pendiente.
        string? ineligible = null;
        if (current is null)
        {
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
            var inTransit = pipeline.FirstOrDefault(s => string.Equals(s.Code, OrderStatuses.InTransit, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException("El pipeline de órdenes no tiene la etapa IN_TRANSIT.");
            var status = pipeline.FirstOrDefault(s => s.Id == order.StatusCodeId);
            var allowed = await statuses.IsAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.AssignTrip, ct);
            ineligible = TripRules.CheckEligibility(new OrderEligibilityInput(
                order.OrderNumber, order.IsActive, order.IsSpecialDelivery,
                status?.Code ?? string.Empty, status?.Label ?? string.Empty, status?.StageKind ?? string.Empty,
                status?.IsInitial ?? false, status?.SortOrder ?? int.MaxValue, inTransit.SortOrder,
                allowed, stop is not null));
        }

        // Zona de la parada pendiente.
        ScanZone? zone = null;
        int? zoneId = null;
        if (stop is not null)
        {
            (zone, zoneId) = await ResolveZoneAsync(stop.SnapPostalCode, stop.SnapCity, ct);
        }

        // Rutas abiertas de la zona en la fecha (solo si hace falta asignar).
        IReadOnlyList<TripRef> openTrips = Array.Empty<TripRef>();
        if (current is null && ineligible is null && zoneId is int zid)
        {
            var editableStatusIds = await EditableTripStatusIdsAsync(ct);
            openTrips = await db.Trips.AsNoTracking()
                .Where(t => t.IsActive && t.DispatchZoneId == zid && t.PlanDate == planDate && editableStatusIds.Contains(t.StatusCodeId))
                .OrderBy(t => t.TripId)
                .Select(t => new TripRef(t.TripId, t.PublicId, t.Code))
                .ToListAsync(ct);
        }

        return new ScanFactsRow(current, ineligible, zone, zoneId, openTrips);
    }

    /// <summary>Ruta vigente de la orden (TripOrder IsCurrent) bajo el filtro de tenant, o null.</summary>
    private async Task<TripRef?> CurrentTripAsync(int orderId, CancellationToken ct)
        => await (from to in db.TripOrders.AsNoTracking()
                  join t in db.Trips.AsNoTracking() on to.TripId equals t.TripId
                  where to.TransportOrderId == orderId && to.IsCurrent
                  orderby to.TripOrderId descending
                  select new TripRef(t.TripId, t.PublicId, t.Code))
            .FirstOrDefaultAsync(ct);

    /// <summary>Ids de TripStatus DRAFT y PLANNED ('ruta abierta').</summary>
    private async Task<List<int>> EditableTripStatusIdsAsync(CancellationToken ct)
    {
        var codes = new[] { TripStatuses.Draft, TripStatuses.Planned };
        return await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.TripStatus && codes.Contains(s.InternalCode))
            .Select(s => s.StatusCodeId)
            .ToListAsync(ct);
    }

    /// <summary>Paradas de la versión vigente de la ruta (el Trip ya está bloqueado y verificado del tenant).</summary>
    private Task<int> ActiveStopCountAsync(int tripId, CancellationToken ct)
        => db.RouteStops.AsNoTracking()
            .CountAsync(s => db.Routes.Any(r => r.RouteId == s.RouteId && r.TripId == tripId && r.IsActive), ct);

    // ================================================================ adaptador a la costura de P0 (TripQueries / DispatchZoneMatcher)

    /// <summary>
    /// Zona por precedencia CP &gt; rango postal &gt; municipio sobre los miembros de las zonas ACTIVAS del tenant
    /// (TripQueries.ActiveZoneMembersAsync + DispatchZoneMatcher.Resolve). El id sale del código de la zona ganadora
    /// (código único por tenant). Toda dependencia de la forma de ZoneResolution vive aquí.
    /// </summary>
    private async Task<(ScanZone Zone, int? ZoneId)> ResolveZoneAsync(string? postalCode, string? city, CancellationToken ct)
    {
        var members = await db.ActiveZoneMembersAsync(ct);
        var r = DispatchZoneMatcher.Resolve(members, postalCode, city);
        if (r.Ambiguous)
            return (new ScanZone(null, true, (r.Candidates ?? Array.Empty<string>()).ToList()), null);
        if (string.IsNullOrWhiteSpace(r.ZoneCode)) return (ScanZone.None, null);

        var zoneCode = r.ZoneCode;
        var zoneId = await db.DispatchZones.AsNoTracking()
            .Where(z => z.IsActive && z.Code == zoneCode)
            .OrderBy(z => z.DispatchZoneId)
            .Select(z => (int?)z.DispatchZoneId)
            .FirstOrDefaultAsync(ct);
        return zoneId is null ? (ScanZone.None, null) : (new ScanZone(zoneCode, false, Array.Empty<string>()), zoneId);
    }
}
