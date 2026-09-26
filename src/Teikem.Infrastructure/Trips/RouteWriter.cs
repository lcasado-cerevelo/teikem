using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Trips;

/// <summary>404 con un mensaje exacto del manual (p. ej. 'La orden no está en esta ruta.'): BOLA por id hijo.</summary>
public sealed class TripMemberNotFoundException(string message) : TeikemException(message, 404, "not_found");

/// <summary>
/// Lote 5 (P0) — ÚNICA vía de escritura de TripOrder, Route y RouteStop (costura implementada).
///
/// Contrato con el llamador:
/// - recibe el Trip TRACKED obtenido con TripQueries.LockTripAsync (UPDLOCK con TenantId) y, con proveedor relacional, una
///   transacción abierta (RunInTransactionAsync); si no, InvalidOperationException;
/// - ORDEN DE BLOQUEO ÚNICO del lote: (1) fila Tenant solo en 'Planificar el día'; (2) los Trips por TripId ascendente;
///   (3) las órdenes por TransportOrderId ascendente en UNA sentencia (TripQueries.LockOrdersAsync, lo hace AddOrdersAsync);
///   (4) después se escribe. Los efectos de OrderStatus respetan el orden porque la orden solo se escribe al guardar;
/// - toda mutación toca el Trip (UpdatedAtUtc): su RowVersion cambia y SaveGuardedAsync traduce los choques a 409.
///
/// Operaciones de CONTENIDO (agregar, liberar, secuencia, nueva versión): exigen ruta editable (DRAFT/PLANNED, activa) →
/// 422 'La ruta … ya fue despachada; no se puede editar ni eliminar.'. RecomputeAsync NO lo exige: solo reescribe tiempos y
/// distancias planificados (hora de salida de una cabecera despachada con EDIT_TRIP, pin manual), nunca membresía ni
/// secuencia; en una ruta terminal lanza InvalidOperationException.
/// Liberar una orden es un DELETE físico de su TripOrder y de su RouteStop de la versión VIGENTE (L272); las versiones
/// archivadas nunca se tocan. Liberar NUNCA cambia el OrderStatus. Las RouteStop nacen PENDING sin fila de historial.
/// </summary>
public sealed class RouteWriter(TeikemDbContext db, StatusService statuses, ILookupCache lookups, ITenantContext tenant)
{
    // ================================================================ guardas

    /// <summary>422 si la ruta no admite cambios de contenido (no está en DRAFT/PLANNED o fue eliminada).</summary>
    public async Task EnsureEditableAsync(Trip trip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var code = await StatusCodeOfAsync(trip.StatusCodeId, ct);
        if (!trip.IsActive || !TripRules.IsEditable(code))
            throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, trip.IsActive ? code : TripStatuses.Cancelled));
    }

    /// <summary>
    /// Motivo de no elegibilidad por orden (TransportOrderId → motivo exacto de TripRules.CheckEligibility): pipeline del
    /// tenant con etapas deshabilitadas, capacidad ASSIGN_TRIP de su estatus y parada DELIVERY pendiente. Solo lee (no exige
    /// transacción): lo usan AddOrdersAsync bajo bloqueo, el despacho y el selector (TripIssueBuilder).
    /// </summary>
    public async Task<Dictionary<int, string>> CheckEligibilityAsync(IReadOnlyCollection<TransportOrder> orders, CancellationToken ct)
    {
        var result = new Dictionary<int, string>();
        if (orders is null || orders.Count == 0) return result;

        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var inTransitSort = pipeline.FirstOrDefault(s => Same(s.Code, OrderStatuses.InTransit))?.SortOrder ?? int.MaxValue;
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var pending = await db.PendingDeliveryStopsAsync(orders.Select(o => o.TransportOrderId).ToList(), deliveryTypeId, ct);
        var allowedByStatus = new Dictionary<int, bool>();

        foreach (var o in orders)
        {
            var status = pipeline.FirstOrDefault(s => s.Id == o.StatusCodeId);
            if (!allowedByStatus.TryGetValue(o.StatusCodeId, out var allowed))
            {
                allowed = await statuses.IsAllowedAsync(EntityTypes.TransportOrder, o.StatusCodeId, Capabilities.AssignTrip, ct);
                allowedByStatus[o.StatusCodeId] = allowed;
            }
            var reason = TripRules.CheckEligibility(new OrderEligibilityInput(
                o.OrderNumber, o.IsActive, o.IsSpecialDelivery,
                status?.Code ?? string.Empty, status?.Label ?? string.Empty, status?.StageKind ?? string.Empty,
                status?.IsInitial ?? false, status?.SortOrder ?? int.MaxValue, inTransitSort,
                allowed, pending.ContainsKey(o.TransportOrderId)));
            if (reason is not null) result[o.TransportOrderId] = reason;
        }
        return result;
    }

    // ================================================================ versión vigente

    /// <summary>
    /// Versión vigente de la ruta o, si no tiene, una nueva (versión max+1) que nace en la etapa inicial de RouteStatus (DRAFT)
    /// con su historial ROUTE. Guarda para obtener el RouteId (UX_Route_Trip_Active: un alta simultánea es 409).
    /// </summary>
    public async Task<Route> GetOrCreateActiveRouteAsync(Trip trip, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);
        var active = await db.ActiveRouteAsync(trip.TripId, true, ct);
        if (active is not null) return active;

        var initial = await statuses.GetInitialAsync(StatusDomains.RouteStatus, ct);
        var route = new Route
        {
            TripId = trip.TripId,
            Version = await MaxVersionAsync(trip.TripId, ct) + 1,
            IsActive = true,
            StatusCodeId = initial.StatusCodeId,
            StopCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Routes.Add(route);
        Touch(trip);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        var born = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, route.RouteId, null, initial.InternalCode, null, ct);
        route.StatusCodeId = born.StatusCodeId;
        return route;
    }

    // ================================================================ contenido

    /// <summary>
    /// Agrega órdenes al final de la ruta, todo o nada:
    /// 1. bloquea las órdenes (paso 3 del orden de bloqueo) y las RE-LEE bajo bloqueo (404 'Orden no encontrado.' si alguna no
    ///    es del tenant);
    /// 2. elegibilidad atómica → 422 'Hay órdenes que no se pueden asignar a la ruta.' con errors {número: [motivo]};
    /// 3. ya en esta ruta → 409 'La orden ya está en esta ruta.'; en otra ruta vigente → 409 'La orden ya está asignada a la
    ///    ruta {código}.';
    /// 4. tope técnico → 400 'Una ruta admite como máximo 300 paradas.';
    /// 5. TripOrder vigente + RouteStop PENDING al final (sin fila de historial), en el orden pedido;
    /// 6. guarda (una carrera en UX_TripOrder_Current es 409 'La orden ya está asignada a otra ruta.') y recalcula las ETAs.
    /// NO cambia el OrderStatus.
    /// </summary>
    public async Task AddOrdersAsync(Trip trip, IReadOnlyCollection<int> orderIds, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);
        var ids = new List<int>();
        foreach (var id in orderIds ?? Array.Empty<int>())
            if (!ids.Contains(id)) ids.Add(id);
        if (ids.Count == 0) return;

        // 1. Bloqueo y relectura bajo bloqueo.
        var orders = await db.LockOrdersAsync(ids, false, ct);
        if (orders.Count != ids.Count) throw new NotFoundException(OrderQueries.OrderLabel);
        var byId = orders.ToDictionary(o => o.TransportOrderId);

        // 2. Elegibilidad (atómica: una sola orden no elegible rechaza la solicitud completa).
        var ineligible = await CheckEligibilityAsync(orders, ct);
        if (ineligible.Count > 0)
            throw new StatusRuleException(TripRules.EligibilityHeader)
            {
                Errors = ids.Where(ineligible.ContainsKey)
                    .GroupBy(id => byId[id].OrderNumber)
                    .ToDictionary(g => g.Key, g => g.Select(id => ineligible[id]).Distinct().ToArray()),
            };

        // 3. Ruta vigente de cada orden (re-verificada bajo bloqueo).
        var current = await db.CurrentTripsOfOrdersAsync(ids, ct);
        foreach (var id in ids)
        {
            if (!current.TryGetValue(id, out var cur)) continue;
            if (cur.TripId == trip.TripId) throw new ConflictException(TripRules.AlreadyInTripMessage);
            throw new ConflictException(TripRules.OrderInOtherTripMessage(cur.Code));
        }

        // 4. Tope técnico de paradas.
        var existing = await db.ActiveRouteAsync(trip.TripId, true, ct);
        var existingStops = existing is null ? new List<RouteStop>() : await LoadStopsAsync(existing.RouteId, ct);
        if (existingStops.Count + ids.Count > TripRules.MaxStopsHardCap)
            throw new ValidationException("orderPublicIds", TripRules.MaxStopsHardCapMessage);

        // 5. TripOrder + RouteStop al final.
        var route = existing ?? await GetOrCreateActiveRouteAsync(trip, ct);
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var pendingStops = await db.PendingDeliveryStopsAsync(ids, deliveryTypeId, ct);
        var pendingStatus = await statuses.GetByCodeAsync(StatusDomains.RouteStopStatus, RouteStopStatuses.Pending, ct);
        var seq = existingStops.Count == 0 ? 0 : existingStops.Max(s => s.Sequence);
        var now = DateTime.UtcNow;
        foreach (var id in ids)
        {
            if (!pendingStops.TryGetValue(id, out var stop))
                throw new StatusRuleException(TripRules.EligibilityHeader)
                {
                    Errors = new Dictionary<string, string[]> { [byId[id].OrderNumber] = new[] { TripRules.NoPendingDeliveryMessage } },
                };
            seq++;
            db.TripOrders.Add(new TripOrder
            {
                TenantId = trip.TenantId,
                TripId = trip.TripId,
                TransportOrderId = id,
                SortHint = seq,
                IsCurrent = true,
                AssignedAtUtc = now,
                AssignedBy = tenant.UserId,
            });
            db.RouteStops.Add(new RouteStop
            {
                RouteId = route.RouteId,
                OrderStopId = stop.OrderStopId,
                Sequence = seq,
                StatusCodeId = pendingStatus.StatusCodeId,
            });
        }
        Touch(trip);

        // 6. Guardar (UX_TripOrder_Current es la última línea) y recalcular.
        await db.SaveGuardedAsync(TripRules.OrderTakenMessage, ct);
        await RecomputeAsync(trip, ct);
    }

    /// <summary>
    /// Libera una orden de ESTA ruta: DELETE de su TripOrder vigente y de su RouteStop de la versión VIGENTE, resecuencia
    /// 1..N y recálculo. Una orden que no está en esta ruta es 404 'La orden no está en esta ruta.'. No guarda.
    /// </summary>
    public async Task ReleaseOrderAsync(Trip trip, int transportOrderId, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);

        var links = await CurrentLinksAsync(trip.TripId, ct);
        var link = links.FirstOrDefault(x => x.TransportOrderId == transportOrderId)
                   ?? throw new TripMemberNotFoundException(TripRules.NotInTripMessage);

        var orderStopIds = await (from os in db.OrderStops.AsNoTracking()
                                  join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                                  where os.TransportOrderId == transportOrderId
                                  select os.OrderStopId).ToListAsync(ct);
        var route = await db.ActiveRouteAsync(trip.TripId, true, ct);
        if (route is not null)
        {
            var stops = await LoadStopsAsync(route.RouteId, ct);
            foreach (var s in stops.Where(s => orderStopIds.Contains(s.OrderStopId))) db.RouteStops.Remove(s);
            Resequence(route.RouteId);
        }
        db.TripOrders.Remove(link);
        Touch(trip);
        await RecomputeAsync(trip, ct);
    }

    /// <summary>Libera TODAS las órdenes vigentes de la ruta (y sus paradas de la versión vigente). No guarda.</summary>
    public async Task ReleaseAllAsync(Trip trip, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);

        foreach (var link in await CurrentLinksAsync(trip.TripId, ct)) db.TripOrders.Remove(link);
        var route = await db.ActiveRouteAsync(trip.TripId, true, ct);
        if (route is not null)
            foreach (var s in await LoadStopsAsync(route.RouteId, ct)) db.RouteStops.Remove(s);
        Touch(trip);
        await RecomputeAsync(trip, ct);
    }

    /// <summary>
    /// Reordena la versión vigente (misma versión, sin corrida). La secuencia debe ser una permutación exacta de sus paradas:
    /// ids de otra ruta u otro tenant, repetidos o faltantes → 400 'La secuencia debe incluir exactamente las paradas de la
    /// ruta vigente, sin repetir.'. No guarda ni recalcula (el llamador llama RecomputeAsync).
    /// </summary>
    public async Task ApplySequenceAsync(Trip trip, IReadOnlyList<int> routeStopIds, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);
        var route = await db.ActiveRouteAsync(trip.TripId, true, ct);
        var stops = route is null ? new List<RouteStop>() : await LoadStopsAsync(route.RouteId, ct);
        if (route is null || TripRules.ValidateSequence(stops.Select(s => s.RouteStopId).ToList(), routeStopIds) is not null)
            throw new ValidationException("routeStopIds", TripRules.SequenceMessage);

        var byId = stops.ToDictionary(s => s.RouteStopId);
        for (var i = 0; i < routeStopIds.Count; i++) byId[routeStopIds[i]].Sequence = i + 1;
        Touch(trip);
    }

    /// <summary>
    /// Nueva versión del plan con exactamente las paradas vigentes (por OrderStopId) en el orden dado: archiva la vigente
    /// (→ ARCHIVED, IsActive = 0) y GUARDA antes de insertar la nueva (UX_Route_Trip_Active), crea la versión max+1 en la etapa
    /// inicial con su historial y la avanza hasta finalStatusCode (p. ej. OPTIMIZED). Las paradas copian su estatus. La
    /// versión archivada queda intacta. No recalcula (el llamador guarda y llama RecomputeAsync).
    /// </summary>
    public async Task ReplaceActiveRouteAsync(Trip trip, IReadOnlyList<int> orderedOrderStopIds, string finalStatusCode, string? comment, CancellationToken ct)
    {
        EnsureTracked(trip);
        await EnsureEditableAsync(trip, ct);
        var ordered = (orderedOrderStopIds ?? Array.Empty<int>()).ToList();

        var current = await db.ActiveRouteAsync(trip.TripId, true, ct);
        var currentStops = current is null ? new List<RouteStop>() : await LoadStopsAsync(current.RouteId, ct);
        if (ordered.Distinct().Count() != ordered.Count || !new HashSet<int>(ordered).SetEquals(currentStops.Select(s => s.OrderStopId)))
            throw new InvalidOperationException("La nueva versión debe contener exactamente las paradas vigentes de la ruta, sin repetir.");
        var statusByOrderStop = currentStops.GroupBy(s => s.OrderStopId).ToDictionary(g => g.Key, g => g.First().StatusCodeId);

        var newVersion = await MaxVersionAsync(trip.TripId, ct) + 1;
        if (current is not null)
        {
            var archived = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, current.RouteId, current.StatusCodeId,
                RouteStatuses.Archived, $"Reemplazada por la versión {newVersion}", ct);
            current.StatusCodeId = archived.StatusCodeId;
            current.IsActive = false;
            Touch(trip);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        }

        var initial = await statuses.GetInitialAsync(StatusDomains.RouteStatus, ct);
        var route = new Route
        {
            TripId = trip.TripId,
            Version = newVersion,
            IsActive = true,
            StatusCodeId = initial.StatusCodeId,
            StopCount = ordered.Count,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Routes.Add(route);
        Touch(trip);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);

        var born = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, route.RouteId, null, initial.InternalCode, null, ct);
        route.StatusCodeId = born.StatusCodeId;
        if (!Same(initial.InternalCode, finalStatusCode))
        {
            // Etapa por etapa (una transición e historial por paso); si el tenant deshabilitó el destino, se queda en la última
            // etapa habilitada anterior.
            var pipeline = await statuses.GetPipelineAsync(StatusDomains.RouteStatus, includeDisabled: true, ct);
            var stages = pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();
            var currentCode = initial.InternalCode;
            foreach (var step in PipelinePath.StepsTo(stages, currentCode, finalStatusCode))
            {
                var next = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, route.RouteId, route.StatusCodeId, step, comment, ct);
                route.StatusCodeId = next.StatusCodeId;
            }
        }

        for (var i = 0; i < ordered.Count; i++)
            db.RouteStops.Add(new RouteStop
            {
                RouteId = route.RouteId,
                OrderStopId = ordered[i],
                Sequence = i + 1,
                StatusCodeId = statusByOrderStop[ordered[i]],
            });
        Touch(trip);
    }

    // ================================================================ tiempos y distancias

    /// <summary>
    /// Recalcula la versión vigente con EtaCalculator (salida planificada, coordenadas, ventanas y servicio): escribe
    /// RouteStop.Planned* y *FromPrev*, Route.StopCount y sus totales, y Trip.TotalDistanceKm, TotalDurationMin y
    /// PlannedEndUtc. Toca el Trip (su RowVersion cambia). NO exige ruta editable; en una ruta terminal lanza
    /// InvalidOperationException. No guarda.
    /// </summary>
    public async Task RecomputeAsync(Trip trip, CancellationToken ct)
    {
        EnsureTracked(trip);
        if (await IsTerminalAsync(trip.StatusCodeId, ct))
            throw new InvalidOperationException($"La ruta {trip.Code} está cerrada: no se recalculan sus tiempos.");

        Touch(trip);
        var route = await db.ActiveRouteAsync(trip.TripId, true, ct);
        if (route is null)
        {
            trip.TotalDistanceKm = null;
            trip.TotalDurationMin = null;
            trip.PlannedEndUtc = null;
            return;
        }

        var stops = await LoadStopsAsync(route.RouteId, ct);
        var orderStopIds = stops.Select(s => s.OrderStopId).Distinct().ToList();
        var facts = orderStopIds.Count == 0
            ? new Dictionary<int, StopFact>()
            : (await (from os in db.OrderStops.AsNoTracking()
                      join o in db.TransportOrders.AsNoTracking() on os.TransportOrderId equals o.TransportOrderId
                      where orderStopIds.Contains(os.OrderStopId)
                      select new StopFact(os.OrderStopId, os.WindowStartUtc, os.WindowEndUtc, os.ServiceMinutes)).ToListAsync(ct))
                .GroupBy(f => f.OrderStopId).ToDictionary(g => g.Key, g => g.First());
        var points = await db.StopPointsAsync(orderStopIds, ct);

        var inputs = stops.Select(s =>
        {
            facts.TryGetValue(s.OrderStopId, out var f);
            points.TryGetValue(s.OrderStopId, out var p);
            return new EtaStopInput(p?.Lat, p?.Lng, f?.WindowStartUtc, f?.WindowEndUtc, f?.ServiceMinutes ?? 0);
        }).ToList();
        var eta = EtaCalculator.Calculate(trip.PlannedStartUtc, inputs);

        for (var i = 0; i < stops.Count; i++)
        {
            var r = eta.Stops[i];
            stops[i].PlannedArrivalUtc = r.ArrivalUtc;
            stops[i].PlannedDepartureUtc = r.DepartureUtc;
            stops[i].DistanceFromPrevKm = r.DistanceFromPrevKm;
            stops[i].DurationFromPrevMin = r.DurationFromPrevMin;
        }
        route.StopCount = stops.Count;
        route.TotalDistanceKm = eta.TotalDistanceKm;
        route.TotalDurationMin = eta.TotalDurationMin;
        trip.TotalDistanceKm = eta.TotalDistanceKm;
        trip.TotalDurationMin = eta.TotalDurationMin;
        trip.PlannedEndUtc = eta.EndUtc;
    }

    // ================================================================ helpers

    private sealed record StopFact(int OrderStopId, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes);

    /// <summary>El Trip debe venir tracked (LockTripAsync) y, con proveedor relacional, dentro de una transacción.</summary>
    private void EnsureTracked(Trip trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        if (db.Entry(trip).State == EntityState.Detached)
            throw new InvalidOperationException("RouteWriter exige el Trip tracked obtenido con TripQueries.LockTripAsync.");
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("RouteWriter exige una transacción abierta (RunInTransactionAsync).");
    }

    /// <summary>Marca el Trip como modificado: el interceptor estampa UpdatedAtUtc/UpdatedBy y SQL Server cambia su RowVersion.</summary>
    private static void Touch(Trip trip) => trip.UpdatedAtUtc = DateTime.UtcNow;

    /// <summary>Paradas de una versión, tracked, sin las marcadas para borrar e incluidas las agregadas sin guardar, en secuencia.</summary>
    private async Task<List<RouteStop>> LoadStopsAsync(int routeId, CancellationToken ct)
    {
        await db.RouteStops.AsTracking().Where(s => s.RouteId == routeId).LoadAsync(ct);
        return db.RouteStops.Local.Where(s => s.RouteId == routeId)
            .OrderBy(s => s.Sequence).ThenBy(s => s.RouteStopId).ToList();
    }

    /// <summary>TripOrder vigentes de la ruta, tracked, sin los marcados para borrar.</summary>
    private async Task<List<TripOrder>> CurrentLinksAsync(int tripId, CancellationToken ct)
    {
        await db.TripOrders.AsTracking().Where(x => x.TripId == tripId && x.IsCurrent).LoadAsync(ct);
        return db.TripOrders.Local.Where(x => x.TripId == tripId && x.IsCurrent).ToList();
    }

    /// <summary>Resecuencia 1..N las paradas que quedan en la versión (orden actual).</summary>
    private void Resequence(int routeId)
    {
        var remaining = db.RouteStops.Local.Where(s => s.RouteId == routeId)
            .OrderBy(s => s.Sequence).ThenBy(s => s.RouteStopId).ToList();
        for (var i = 0; i < remaining.Count; i++)
            if (remaining[i].Sequence != i + 1) remaining[i].Sequence = i + 1;
    }

    private async Task<int> MaxVersionAsync(int tripId, CancellationToken ct)
    {
        var stored = await db.Routes.AsNoTracking().Where(r => r.TripId == tripId).Select(r => (int?)r.Version).MaxAsync(ct) ?? 0;
        var local = db.ChangeTracker.Entries<Route>().Where(e => e.Entity.TripId == tripId).Select(e => e.Entity.Version).DefaultIfEmpty(0).Max();
        return Math.Max(stored, local);
    }

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId)
               .Select(s => s.InternalCode).FirstOrDefaultAsync(ct) ?? string.Empty;

    private Task<bool> IsTerminalAsync(int statusCodeId, CancellationToken ct)
        => db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId)
            .Select(s => s.StageKind != null && s.StageKind.InternalCode == StageKinds.Terminal)
            .FirstOrDefaultAsync(ct);

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
