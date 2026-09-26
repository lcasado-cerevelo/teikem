using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P4) — efectos del dominio TripStatus, vengan de donde venga la transición (despacho, salida, 'Eliminar ruta' o
/// el cierre del Lote 7). Ignora el nacimiento (From null) y las entidades distintas de TRIP. No guarda nada: todo queda en
/// la unidad de trabajo (y la transacción) del llamador, que ya tiene el Trip bloqueado (TripQueries.LockTripAsync).
///
/// → DISPATCHED (solo desde DRAFT/PLANNED):
///   - invariantes de última línea: chofer, vehículo y ruta vigente con al menos una parada (422 'La ruta … no se puede
///     despachar: …');
///   - la versión vigente avanza etapa por etapa hasta ACTIVE (congelada). Comentario 'Despachada', o 'Secuencia manual
///     confirmada al despachar' si nunca se optimizó (DRAFT → OPTIMIZED → ACTIVE). Sin ACTIVE habilitada → 422;
///   - las órdenes vigentes se bloquean (paso 3 del orden de bloqueo: después del Trip), se re-verifica su elegibilidad
///     (422 'Orden {n}: {motivo}') y avanzan etapa por etapa hasta PLANNED (o la última etapa habilitada anterior), una
///     transición e historial por paso con 'Despachada en la ruta {código}'. Las paradas siguen en PENDING.
/// → IN_PROGRESS: ActualStartUtc ??= ahora; las órdenes vigentes que sigan en el pipeline avanzan hasta IN_TRANSIT con
///   'Salió en la ruta {código}'. Las que estén en un lateral se omiten (lo decide el Lote 7).
/// → COMPLETED: ActualEndUtc ??= ahora; los TripOrder dejan de ser vigentes (IsCurrent = 0).
/// → CANCELLED (solo desde DRAFT/PLANNED; si no, 422 'La ruta … ya fue despachada; no se puede eliminar.'): libera todas
///   las órdenes (RouteWriter.ReleaseAllAsync), archiva la versión vigente (IsActive = 0) y deja la ruta con IsActive = 0.
///
/// StatusService y RouteWriter se resuelven de forma perezosa: inyectarlos formaría un ciclo en DI
/// (StatusService → efectos → este efecto → StatusService).
/// Sin dinero: despachar NO crea DriverTrip.
/// </summary>
public sealed class TripStatusEffect(TeikemDbContext db, IServiceProvider services, ILookupCache lookups) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.TripStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.Trip, StringComparison.OrdinalIgnoreCase)) return;
        if (context.From is null) return; // nacimiento: sin efectos

        var to = context.To.InternalCode;
        if (Same(to, TripStatuses.Dispatched)) await OnDispatchedAsync(context, ct);
        else if (Same(to, TripStatuses.InProgress)) await OnStartedAsync(context, ct);
        else if (Same(to, TripStatuses.Completed)) await OnCompletedAsync(context, ct);
        else if (Same(to, TripStatuses.Cancelled)) await OnCancelledAsync(context, ct);
    }

    // ================================================================ → DISPATCHED

    private async Task OnDispatchedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        var trip = await TrackedTripAsync(context.EntityId, ct);
        var from = context.From!.InternalCode;
        if (!Same(from, TripStatuses.Draft) && !Same(from, TripStatuses.Planned))
            throw new StatusRuleException(TripRules.NotEditableMessage(trip.Code, from));

        // Invariantes de última línea (el servicio ya los reportó como bloqueantes antes de transicionar).
        var route = await ActiveRouteAsync(trip.TripId, ct);
        var stopCount = route is null ? 0 : await db.RouteStops.CountAsync(s => s.RouteId == route.RouteId, ct);
        var missing = new List<string>();
        if (trip.DriverId is null) missing.Add(DispatchBatchRules.NoDriverMessage);
        if (trip.VehicleId is null) missing.Add(DispatchBatchRules.NoVehicleMessage);
        if (route is null || stopCount == 0) missing.Add(DispatchBatchRules.NoStopsMessage);
        if (missing.Count > 0) throw new StatusRuleException(DispatchBatchRules.DispatchBlocked(trip.Code, missing));

        var statuses = services.GetRequiredService<StatusService>();

        // 1. La versión vigente se congela: etapa por etapa hasta ACTIVE.
        var routePipeline = await statuses.GetPipelineAsync(StatusDomains.RouteStatus, includeDisabled: true, ct);
        var active = routePipeline.FirstOrDefault(s => Same(s.Code, RouteStatuses.Active));
        if (active is null || !active.IsEnabled) throw new StatusRuleException(DispatchBatchRules.PipelineMissing(RouteStatuses.Active));
        var routeCurrent = routePipeline.FirstOrDefault(s => s.Id == route!.StatusCodeId)
                           ?? throw new InvalidOperationException($"La versión vigente de la ruta {trip.Code} tiene un estatus desconocido.");
        if (!Same(routeCurrent.Code, RouteStatuses.Active))
        {
            var routeComment = Same(routeCurrent.Code, RouteStatuses.Draft)
                ? DispatchBatchRules.ManualSequenceComment
                : DispatchBatchRules.RouteDispatchedComment;
            var steps = SafeSteps(routePipeline, routeCurrent.Code, RouteStatuses.Active);
            if (steps.Count == 0 || !Same(steps[^1], RouteStatuses.Active))
                throw new StatusRuleException(DispatchBatchRules.PipelineMissing(RouteStatuses.Active));
            foreach (var code in steps)
            {
                var next = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, route!.RouteId,
                    route.StatusCodeId, code, routeComment, ct);
                route.StatusCodeId = next.StatusCodeId;
            }
        }

        // 2. Órdenes vigentes: bloqueo (después del Trip), re-verificación y avance hasta PLANNED.
        var orders = await LockCurrentOrdersAsync(trip.TripId, ct);
        if (orders.Count == 0) return;

        var orderPipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var inTransit = orderPipeline.FirstOrDefault(s => Same(s.Code, OrderStatuses.InTransit))
                        ?? throw new InvalidOperationException("El pipeline de órdenes no tiene la etapa IN_TRANSIT.");
        var pending = await PendingDeliveryOrderIdsAsync(orders.Select(o => o.TransportOrderId).ToList(), ct);
        var allowedByStatus = new Dictionary<int, bool>();
        var errors = new Dictionary<string, string[]>();
        string? firstError = null;
        foreach (var order in orders)
        {
            var status = orderPipeline.FirstOrDefault(s => s.Id == order.StatusCodeId);
            if (!allowedByStatus.TryGetValue(order.StatusCodeId, out var allowed))
            {
                allowed = await statuses.IsAllowedAsync(EntityTypes.TransportOrder, order.StatusCodeId, Capabilities.AssignTrip, ct);
                allowedByStatus[order.StatusCodeId] = allowed;
            }
            var reason = TripRules.CheckEligibility(new OrderEligibilityInput(
                order.OrderNumber, order.IsActive, order.IsSpecialDelivery,
                status?.Code ?? string.Empty, status?.Label ?? string.Empty, status?.StageKind ?? string.Empty,
                status?.IsInitial ?? false, status?.SortOrder ?? int.MaxValue, inTransit.SortOrder,
                allowed, pending.Contains(order.TransportOrderId)));
            if (reason is null) continue;
            var message = DispatchBatchRules.OrderNotEligible(order.OrderNumber, reason);
            firstError ??= message;
            errors[order.OrderNumber] = new[] { reason };
        }
        if (firstError is not null) throw new StatusRuleException(firstError) { Errors = errors };

        var stages = Stages(orderPipeline);
        var comment = DispatchBatchRules.OrderDispatchedComment(trip.Code);
        foreach (var order in orders)
        {
            var current = orderPipeline.First(s => s.Id == order.StatusCodeId);
            foreach (var code in PipelinePath.StepsTo(stages, current.Code, OrderStatuses.Planned))
            {
                var next = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId,
                    order.StatusCodeId, code, comment, ct);
                order.StatusCodeId = next.StatusCodeId;
            }
        }
    }

    // ================================================================ → IN_PROGRESS (salida)

    private async Task OnStartedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        var trip = await TrackedTripAsync(context.EntityId, ct);
        trip.ActualStartUtc ??= DateTime.UtcNow;

        var orders = await LockCurrentOrdersAsync(trip.TripId, ct);
        if (orders.Count == 0) return;

        var statuses = services.GetRequiredService<StatusService>();
        var orderPipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var stages = Stages(orderPipeline);
        var comment = DispatchBatchRules.OrderStartedComment(trip.Code);
        foreach (var order in orders)
        {
            var current = orderPipeline.FirstOrDefault(s => s.Id == order.StatusCodeId);
            // Un lateral o un terminal en una ruta despachada no se toca: lo decide el Lote 7.
            if (current is null || !Same(current.StageKind, StageKinds.Pipeline)) continue;
            foreach (var code in PipelinePath.StepsTo(stages, current.Code, OrderStatuses.InTransit))
            {
                var next = await statuses.TransitionAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId,
                    order.StatusCodeId, code, comment, ct);
                order.StatusCodeId = next.StatusCodeId;
            }
        }
    }

    // ================================================================ → COMPLETED (cierre, Lote 7)

    private async Task OnCompletedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        var trip = await TrackedTripAsync(context.EntityId, ct);
        trip.ActualEndUtc ??= DateTime.UtcNow;
        if (trip.ActualStartUtc is DateTime start && trip.ActualEndUtc < start) trip.ActualEndUtc = start; // CK_Trip_Numbers

        var tripId = trip.TripId;
        var current = await db.TripOrders.Where(x => x.TripId == tripId && x.IsCurrent).ToListAsync(ct);
        foreach (var link in current) link.IsCurrent = false;
    }

    // ================================================================ → CANCELLED ('Eliminar ruta')

    private async Task OnCancelledAsync(StatusTransitionContext context, CancellationToken ct)
    {
        var trip = await TrackedTripAsync(context.EntityId, ct);
        var from = context.From!.InternalCode;
        if (!Same(from, TripStatuses.Draft) && !Same(from, TripStatuses.Planned))
            throw new StatusRuleException(DispatchBatchRules.CannotDeleteDispatched(trip.Code));

        // Libera todas las órdenes vigentes (DELETE de TripOrder y de sus RouteStop de la versión vigente).
        var writer = services.GetRequiredService<RouteWriter>();
        await writer.ReleaseAllAsync(trip, ct);

        // Archiva la versión vigente.
        var route = await ActiveRouteAsync(trip.TripId, ct);
        if (route is not null)
        {
            var statuses = services.GetRequiredService<StatusService>();
            var archivedId = await ArchivedStatusIdAsync(ct);
            if (route.StatusCodeId != archivedId)
            {
                var next = await statuses.TransitionAsync(StatusDomains.RouteStatus, EntityTypes.Route, route.RouteId,
                    route.StatusCodeId, RouteStatuses.Archived, DispatchBatchRules.RouteArchivedOnDeleteComment, ct);
                route.StatusCodeId = next.StatusCodeId;
            }
            route.IsActive = false;
        }
        trip.IsActive = false;
    }

    // ================================================================ helpers

    /// <summary>El Trip tracked del llamador (bloqueado con LockTripAsync); si no está en el tracker se carga tracked.</summary>
    private async Task<Domain.Trips.Trip> TrackedTripAsync(int tripId, CancellationToken ct)
        => db.Trips.Local.FirstOrDefault(t => t.TripId == tripId)
           ?? await db.Trips.FirstOrDefaultAsync(t => t.TripId == tripId, ct)
           ?? throw new NotFoundException(TripQueries.TripLabel, null, true);

    /// <summary>Versión vigente (UX_Route_Trip_Active), tracked. Prefiere la instancia que ya esté en el tracker.</summary>
    private async Task<Domain.Trips.Route?> ActiveRouteAsync(int tripId, CancellationToken ct)
    {
        var local = db.ChangeTracker.Entries<Domain.Trips.Route>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.TripId == tripId && e.Entity.IsActive)
            .Select(e => e.Entity)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();
        if (local is not null) return local;
        return await db.Routes.Where(r => r.TripId == tripId && r.IsActive)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Órdenes vigentes de la ruta bloqueadas con UPDLOCK en una sola sentencia (TripQueries.LockOrdersAsync, por id
    /// ascendente) y devueltas tracked en ese orden. El Trip ya está bloqueado por el llamador: se respeta el orden global.
    /// </summary>
    private async Task<List<Domain.Orders.TransportOrder>> LockCurrentOrdersAsync(int tripId, CancellationToken ct)
    {
        var ids = await db.TripOrders.AsNoTracking()
            .Where(x => x.TripId == tripId && x.IsCurrent)
            .Select(x => x.TransportOrderId)
            .ToListAsync(ct);
        if (ids.Count == 0) return new List<Domain.Orders.TransportOrder>();
        ids = ids.Distinct().OrderBy(id => id).ToList();

        await db.LockOrdersAsync(ids, false, ct);
        var orders = await db.TransportOrders.Where(o => ids.Contains(o.TransportOrderId)).ToListAsync(ct);
        return orders.OrderBy(o => o.TransportOrderId).ToList();
    }

    /// <summary>Órdenes (de las indicadas) con una parada DELIVERY no terminal.</summary>
    private async Task<HashSet<int>> PendingDeliveryOrderIdsAsync(List<int> orderIds, CancellationToken ct)
    {
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
        var terminalStopIds = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == StatusDomains.StopStatus && s.StageKind!.InternalCode == StageKinds.Terminal)
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var ids = await db.OrderStops.AsNoTracking()
            .Where(s => orderIds.Contains(s.TransportOrderId) && s.StopTypeLookupId == deliveryTypeId && !terminalStopIds.Contains(s.StatusCodeId))
            .Select(s => s.TransportOrderId)
            .Distinct()
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task<int> ArchivedStatusIdAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking()
               .Where(s => s.Entity == StatusDomains.RouteStatus && s.InternalCode == RouteStatuses.Archived)
               .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct)
           ?? throw new InvalidOperationException("El catálogo RouteStatus no tiene la etapa ARCHIVED.");

    private static List<PipelineStage> Stages(IEnumerable<StatusDto> pipeline)
        => pipeline.Select(s => new PipelineStage(s.Code, s.SortOrder, s.StageKind, s.IsInitial, s.IsEnabled)).ToList();

    /// <summary>PipelinePath sin excepciones: un origen lateral/terminal o inexistente no tiene camino.</summary>
    private static IReadOnlyList<string> SafeSteps(IEnumerable<StatusDto> pipeline, string fromCode, string targetCode)
    {
        try { return PipelinePath.StepsTo(Stages(pipeline), fromCode, targetCode); }
        catch (InvalidOperationException) { return Array.Empty<string>(); }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
