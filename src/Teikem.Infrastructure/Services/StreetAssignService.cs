using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
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
/// Lote 215 (2026-10-24, Luis) — asignar en la calle, con el permiso propio trips.streetassign (trips.dispatch es del almacén). Mandar una orden a una ruta que ya salió (o está por salir), pasarla de una ruta a otra,
/// sacarla de su ruta, o armar una ruta rápida para un chofer. Todo con las reglas de siempre (RouteWriter: elegibilidad, tope de paradas, una sola ruta vigente por orden); lo único nuevo es que la ruta puede estar
/// DESPACHADA o EN CURSO. Reglas:
/// - la ruta debe ser del mismo tipo que la orden (una orden en su etapa inicial se recoge; las demás se entregan);
/// - en una ruta DESPACHADA la orden queda «Asignada»; en una EN CURSO queda «En tránsito» (el chofer ya salió); en una de recogida la orden no cambia de estatus;
/// - al sacarla de una ruta de entrega vuelve a «Sin asignar» (con su historial); solo se mueven órdenes en una etapa de pipeline (no entregadas, fallidas ni canceladas);
/// - al chofer de la ruta (la nueva y la anterior) se le deja un aviso en su bandeja (llega al teléfono como cualquier aviso).
/// Cada operación es una transacción: o queda todo o no queda nada.
/// </summary>
public sealed class StreetAssignService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, RouteWriter writer, TripService trips, TripOrderService tripOrders, TripDispatchService dispatch,
    ILookupCache lookups, ITenantClock? clock = null)
{
    private readonly ITenantClock _clock = clock ?? TenantClock.Default;

    private sealed record OrderInfo(int Id, Guid PublicId, string Number, int StatusCodeId, string StatusCode, string StatusLabel, string StageKind, bool IsInitial, string? Consignee);

    private async Task<OrderInfo> OrderAsync(Guid publicId, CancellationToken ct)
    {
        var o = await (from x in db.TransportOrders.AsNoTracking()
                       join st in db.StatusCodes.AsNoTracking().Include(s => s.StageKind) on x.StatusCodeId equals st.StatusCodeId
                       where x.PublicId == publicId && x.IsActive
                       select new { x.TransportOrderId, x.PublicId, x.OrderNumber, x.StatusCodeId, st.InternalCode, st.LabelJson, Stage = st.StageKind!.InternalCode, st.IsInitial }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Orden", null, true);
        var consignee = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == o.TransportOrderId).OrderByDescending(s => s.Sequence).Select(s => s.SnapName).FirstOrDefaultAsync(ct);
        return new OrderInfo(o.TransportOrderId, o.PublicId, o.OrderNumber, o.StatusCodeId, o.InternalCode, MultilingualText.Resolve(o.LabelJson, tenant.Lang), o.Stage, o.IsInitial, consignee);
    }

    private static string KindOf(OrderInfo o) => o.IsInitial ? TripKinds.Pickup : TripKinds.Delivery;

    /// <summary>Se puede mover solo mientras la orden sigue en una etapa de pipeline (ni entregada, ni fallida, ni cancelada…).</summary>
    private static string? ImmovableReason(OrderInfo o)
        => string.Equals(o.StageKind, StageKinds.Pipeline, StringComparison.OrdinalIgnoreCase) ? null : StreetAssignRules.NotMovableMessage(o.Number, o.StatusLabel);

    private async Task<(int TripId, Guid PublicId, string Code, string Kind, string StatusCode, int? DriverId)?> CurrentTripAsync(int orderId, CancellationToken ct)
    {
        var t = await (from l in db.TripOrders.AsNoTracking() join tr in db.Trips.AsNoTracking() on l.TripId equals tr.TripId
                       join st in db.StatusCodes.AsNoTracking() on tr.StatusCodeId equals st.StatusCodeId
                       where l.TransportOrderId == orderId && l.IsCurrent && tr.IsActive
                       select new { tr.TripId, tr.PublicId, tr.Code, tr.Kind, Status = st.InternalCode, tr.DriverId }).FirstOrDefaultAsync(ct);
        return t is null ? null : (t.TripId, t.PublicId, t.Code, t.Kind, t.Status, t.DriverId);
    }

    // ================================================================ GET targets

    public async Task<StreetTargetsDto> TargetsAsync(Guid orderPublicId, CancellationToken ct)
    {
        var order = await OrderAsync(orderPublicId, ct);
        var kind = KindOf(order);
        var reason = ImmovableReason(order);
        var current = await CurrentTripAsync(order.Id, ct);

        // Domicilio de la orden (entrega o recogida según el tipo): su punto, si ya tiene coordenadas.
        var stopType = kind == TripKinds.Pickup ? StopTypes.Pickup : StopTypes.Delivery;
        var stopId = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == order.Id && s.StopType!.InternalCode == stopType).OrderByDescending(s => s.Sequence).Select(s => (int?)s.OrderStopId).FirstOrDefaultAsync(ct);
        double? lat = null, lng = null;
        if (stopId is int sid && (await db.StopPointsAsync(new[] { sid }, ct)).TryGetValue(sid, out var p)) (lat, lng) = (p.Lat, p.Lng);

        var today = _clock.Today;
        var open = new[] { TripStatuses.Planned, TripStatuses.Dispatched, TripStatuses.InProgress };
        var rows = await (from t in db.Trips.AsNoTracking()
                          join st in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals st.StatusCodeId
                          join d in db.Drivers.AsNoTracking() on t.DriverId equals d.DriverId into ds from d in ds.DefaultIfEmpty()
                          join v in db.Vehicles.AsNoTracking() on t.VehicleId equals v.VehicleId into vs from v in vs.DefaultIfEmpty()
                          where t.IsActive && t.Kind == kind && open.Contains(st.InternalCode)
                                && (t.PlanDate == today || st.InternalCode == TripStatuses.Dispatched || st.InternalCode == TripStatuses.InProgress)
                          select new { t.TripId, t.PublicId, t.Code, t.Kind, StatusCode = st.InternalCode, st.LabelJson, t.AcceptedAtUtc, t.DriverId, DriverPublic = d != null ? (Guid?)d.PublicId : null, DriverName = d != null ? d.FullName : null,
                                       VehicleCode = v != null ? v.Code : null, t.ActualStartUtc }).ToListAsync(ct);
        var counts = await db.TripOrders.AsNoTracking().Where(l => l.IsCurrent && rows.Select(r => r.TripId).Contains(l.TripId)).GroupBy(l => l.TripId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var pings = await db.LastPingsAsync(rows.Where(r => r.StatusCode == TripStatuses.InProgress).Select(r => new TripPingKey(r.TripId, r.DriverId, r.ActualStartUtc)).ToList(), ct);

        var list = rows.Select(r =>
        {
            pings.TryGetValue(r.TripId, out var ping);
            double? km = ping is not null && lat is double la && lng is double lo ? Math.Round(EtaCalculator.Haversine(ping.Lat, ping.Lng, la, lo), 1) : null;
            return new StreetTripDto(r.PublicId, r.Code, r.Kind, r.StatusCode, MultilingualText.Resolve(r.LabelJson, tenant.Lang), r.AcceptedAtUtc != null, r.DriverPublic, r.DriverName, r.VehicleCode,
                counts.GetValueOrDefault(r.TripId), current?.TripId == r.TripId, ping?.Lat, ping?.Lng, ping?.CapturedAtUtc, km);
        }).OrderBy(x => x.DistanceKm ?? double.MaxValue).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();

        var openDrivers = rows.Where(r => r.DriverId != null).Select(r => r.DriverId!.Value).ToHashSet();
        var drivers = (await db.Drivers.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).Select(d => new { d.DriverId, d.PublicId, d.FullName }).ToListAsync(ct))
            .Select(d => new StreetDriverDto(d.PublicId, d.FullName, openDrivers.Contains(d.DriverId))).ToList();
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.IsActive).OrderBy(v => v.Code).Select(v => new StreetVehicleDto(v.PublicId, v.Code, v.PlateNumber)).ToListAsync(ct);

        return new StreetTargetsDto(order.PublicId, order.Number, kind, order.StatusCode, order.StatusLabel, reason is null, reason, current?.PublicId, current?.Code, lat, lng, list, drivers, vehicles);
    }

    // ================================================================ POST assign (asignar o mover)

    public async Task<StreetResultDto> AssignAsync(StreetAssignRequest? req, CancellationToken ct)
    {
        if (req is null || req.OrderPublicId == Guid.Empty) throw new ValidationException("orderPublicId", StreetAssignRules.OrderRequiredMessage);
        if (req.TripPublicId == Guid.Empty) throw new ValidationException("tripPublicId", StreetAssignRules.TripRequiredMessage);
        var userId = ((TenantContext)tenant).RequireUserId();
        var order = await OrderAsync(req.OrderPublicId, ct);
        if (ImmovableReason(order) is string why) throw new StatusRuleException(why);
        var kind = KindOf(order);

        var action = "ASSIGNED";
        var notified = false;
        string tripCode = string.Empty;
        string finalCode = order.StatusCode, finalLabel = order.StatusLabel;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var target = await db.LockTripAsync(req.TripPublicId, ct2);   // 404 'Ruta no encontrada.'
            tripCode = target.Code;
            if (!string.Equals(target.Kind, kind, StringComparison.OrdinalIgnoreCase)) throw new StatusRuleException(StreetAssignRules.WrongKindMessage(order.Number, kind));
            var targetStatus = await StatusCodeOfAsync(target.StatusCodeId, ct2);
            var targetLive = targetStatus is TripStatuses.Dispatched or TripStatuses.InProgress;
            if (!targetLive && !TripRules.IsEditable(targetStatus)) throw new StatusRuleException(TripRules.NotEditableMessage(target.Code, target.IsActive ? targetStatus : TripStatuses.Cancelled));

            // ¿Ya está en otra ruta? Se saca primero (y se avisa a su chofer). Ya en ESTA ruta: 409 como en la web.
            var cur = await CurrentTripAsync(order.Id, ct2);
            if (cur is not null && cur.Value.TripId == target.TripId) throw new ConflictException(TripRules.AlreadyInTripMessage);
            var lockedOrder = (await db.LockOrdersAsync(new List<int> { order.Id }, includeStops: false, ct2)).First();
            if (cur is not null)
            {
                action = "MOVED";
                var from = await db.LockTripAsync(cur.Value.TripId, ct2);
                if (TripRules.IsEditable(cur.Value.StatusCode)) await writer.ReleaseOrderAsync(from, order.Id, ct2);
                else await writer.ReleaseOrderLiveAsync(from, order.Id, ct2);
                if (kind == TripKinds.Delivery) await BackToUnassignedAsync(lockedOrder, StreetAssignRules.MovedComment(from.Code, target.Code), ct2);
                AddNotice(from, StreetAssignRules.StopRemovedTitle(from.Code), StreetAssignRules.StopRemovedBody(order.Number), DriverNoticeCodes.StopRemoved);
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            }

            if (targetLive) await writer.AddOrdersLiveAsync(target, new List<int> { order.Id }, ct2);
            else await writer.AddOrdersAsync(target, new List<int> { order.Id }, ct2);

            if (targetLive && kind == TripKinds.Delivery)
            {
                var goal = targetStatus == TripStatuses.InProgress ? OrderStatuses.InTransit : OrderStatuses.Assigned;
                await AdvanceOrderAsync(lockedOrder, goal, StreetAssignRules.AssignedComment(target.Code), ct2);
            }
            if (targetLive) notified = AddNotice(target, DriverNoticeRules.StopAddedTitle(target.Code), DriverNoticeRules.StopAddedBody(order.Number, order.Consignee), DriverNoticeCodes.StopAdded);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            var now = await StatusAsync(lockedOrder.StatusCodeId, ct2);
            finalCode = now.Code; finalLabel = now.Label;
        }, ct);
        _ = userId;
        return new StreetResultDto(action, order.PublicId, order.Number, finalCode, finalLabel, req.TripPublicId, tripCode, notified);
    }

    // ================================================================ POST unassign

    public async Task<StreetResultDto> UnassignAsync(StreetUnassignRequest? req, CancellationToken ct)
    {
        if (req is null || req.OrderPublicId == Guid.Empty) throw new ValidationException("orderPublicId", StreetAssignRules.OrderRequiredMessage);
        var order = await OrderAsync(req.OrderPublicId, ct);
        if (ImmovableReason(order) is string why) throw new StatusRuleException(why);
        var kind = KindOf(order);
        var notified = false;
        string finalCode = order.StatusCode, finalLabel = order.StatusLabel;
        string fromCode = string.Empty;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var cur = await CurrentTripAsync(order.Id, ct2) ?? throw new StatusRuleException(StreetAssignRules.NotInAnyTripMessage(order.Number));
            var from = await db.LockTripAsync(cur.TripId, ct2);
            fromCode = from.Code;
            var lockedOrder = (await db.LockOrdersAsync(new List<int> { order.Id }, includeStops: false, ct2)).First();
            if (TripRules.IsEditable(cur.StatusCode)) await writer.ReleaseOrderAsync(from, order.Id, ct2);
            else await writer.ReleaseOrderLiveAsync(from, order.Id, ct2);
            if (kind == TripKinds.Delivery) await BackToUnassignedAsync(lockedOrder, StreetAssignRules.UnassignedComment(from.Code), ct2);
            if (!TripRules.IsEditable(cur.StatusCode)) notified = AddNotice(from, DriverNoticeRules.StopRemovedTitle(from.Code), DriverNoticeRules.StopRemovedBody(order.Number), DriverNoticeCodes.StopRemoved);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            var now = await StatusAsync(lockedOrder.StatusCodeId, ct2);
            finalCode = now.Code; finalLabel = now.Label;
        }, ct);
        return new StreetResultDto("UNASSIGNED", order.PublicId, order.Number, finalCode, finalLabel, null, fromCode, notified);
    }

    // ================================================================ POST quick-route

    /// <summary>
    /// Ruta rápida: crea una ruta de hoy para el chofer y el vehículo elegidos con las órdenes dadas (todas del mismo tipo y sin ruta) y la despacha: el chofer recibe el aviso «Nuevo manifiesto» y la acepta en su app.
    /// Si algo falla después de crearla (p. ej. el chofer ya tiene otra ruta, o el despacho se rechaza), la ruta se cancela y las órdenes quedan como estaban.
    /// </summary>
    public async Task<StreetResultDto> QuickRouteAsync(StreetQuickRouteRequest? req, CancellationToken ct)
    {
        var ids = (req?.OrderPublicIds ?? Array.Empty<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("orderPublicIds", StreetAssignRules.OrderRequiredMessage);
        if (req!.DriverPublicId == Guid.Empty) throw new ValidationException("driverPublicId", StreetAssignRules.DriverRequiredMessage);
        if (req.VehiclePublicId == Guid.Empty) throw new ValidationException("vehiclePublicId", StreetAssignRules.VehicleRequiredMessage);

        var orders = new List<OrderInfo>();
        foreach (var id in ids) orders.Add(await OrderAsync(id, ct));
        foreach (var o in orders) if (ImmovableReason(o) is string why) throw new StatusRuleException(why);
        var kinds = orders.Select(KindOf).Distinct().ToList();
        if (kinds.Count > 1) throw new StatusRuleException(StreetAssignRules.MixedKindsMessage);

        var created = await trips.CreateAsync(new TripCreateRequest(_clock.Today, null, req.DriverPublicId, req.VehiclePublicId, null, kinds[0]), ct);
        try
        {
            await tripOrders.AddOrdersAsync(created.PublicId, new TripOrdersAddRequest(ids), ct);
            await dispatch.DispatchAsync(created.PublicId, new TripDispatchRequest(StreetAssignRules.QuickRouteComment), ct);
        }
        catch
        {
            try { await trips.CancelAsync(created.PublicId, null, ct); } catch { /* la ruta queda en borrador: la oficina la ve en la web */ }
            throw;
        }
        return new StreetResultDto("QUICK_ROUTE", orders.Count == 1 ? orders[0].PublicId : null, orders.Count == 1 ? orders[0].Number : null, null, null, created.PublicId, created.Code, true);
    }

    // ================================================================ helpers

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<(string Code, string Label)> StatusAsync(int statusCodeId, CancellationToken ct)
    {
        var s = await db.StatusCodes.AsNoTracking().Where(x => x.StatusCodeId == statusCodeId).Select(x => new { x.InternalCode, x.LabelJson }).FirstAsync(ct);
        return (s.InternalCode, MultilingualText.Resolve(s.LabelJson, tenant.Lang));
    }

    /// <summary>La orden de una ruta de entrega que sale de ella vuelve a «Sin asignar» (con su historial); si ya estaba antes de esa etapa no se toca.</summary>
    private async Task BackToUnassignedAsync(Teikem.Domain.Orders.TransportOrder order, string comment, CancellationToken ct)
        => await AdvanceOrderAsync(order, OrderStatuses.Unassigned, comment, ct, allowBackward: true);

    private async Task AdvanceOrderAsync(Teikem.Domain.Orders.TransportOrder order, string goal, string comment, CancellationToken ct, bool allowBackward = false)
    {
        var pipeline = await statuses.GetPipelineAsync(StatusDomains.OrderStatus, includeDisabled: true, ct);
        var current = pipeline.First(x => x.Id == order.StatusCodeId);
        var target = pipeline.FirstOrDefault(x => string.Equals(x.Code, goal, StringComparison.OrdinalIgnoreCase));
        if (target is null || !target.IsEnabled) throw new StatusRuleException(StreetAssignRules.PipelineMissingMessage(goal));
        if (string.Equals(current.Code, goal, StringComparison.OrdinalIgnoreCase)) return;
        // Hacia adelante solo si la meta está más adelante; hacia atrás solo al sacar la orden de una ruta.
        if (!allowBackward && target.SortOrder < current.SortOrder) return;
        if (allowBackward && target.SortOrder >= current.SortOrder) return;
        var steps = allowBackward ? new List<string> { goal } : PipelinePath.StepsTo(pipeline.Select(x => new PipelineStage(x.Code, x.SortOrder, x.StageKind, x.IsInitial, x.IsEnabled)).ToList(), current.Code, goal).ToList();
        if (steps.Count == 0 || !string.Equals(steps[^1], goal, StringComparison.OrdinalIgnoreCase)) throw new StatusRuleException(StreetAssignRules.PipelineMissingMessage(goal));
        var to = await statuses.AdvanceSkippingAsync(StatusDomains.OrderStatus, EntityTypes.TransportOrder, order.TransportOrderId, order.StatusCodeId, steps, comment, ct);
        order.StatusCodeId = to.StatusCodeId;
    }

    /// <summary>Deja el aviso en la bandeja del chofer de la ruta (el trabajo de avisos lo manda al teléfono). Sin chofer, no hay a quién. Devuelve si lo dejó.</summary>
    private bool AddNotice(Trip trip, string title, string body, string kind)
    {
        if (trip.DriverId is not int driverId) return false;
        db.DriverNotices.Add(new DriverNotice { TenantId = trip.TenantId, DriverId = driverId, Kind = kind, TripId = trip.TripId, Title = title, Body = body, CreatedBy = tenant.UserId });
        return true;
    }
}
