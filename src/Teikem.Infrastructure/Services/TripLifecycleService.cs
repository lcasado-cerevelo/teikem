using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P4) — ciclo de vida de la ruta despachada: la SALIDA (Trip DISPATCHED → IN_PROGRESS). Es la costura que reutilizará
/// la app del chofer del Lote 7 ('el estado lo mueven eventos: salida, llegada, POD').
/// - StartTrackedAsync: corre dentro de la transacción del llamador, con el Trip ya bloqueado (TripQueries.LockTripAsync).
///   IN_PROGRESS → 422 'La ruta … ya salió.'; cualquier otro estatus distinto de DISPATCHED → 422 'La ruta … no está
///   despachada; despáchela antes de registrar su salida.'. La transición la hace StatusService (si el tenant deshabilitó
///   IN_PROGRESS, responde el 422 del motor); TripStatusEffect fija ActualStartUtc y lleva las órdenes a IN_TRANSIT.
/// - StartAsync: POST /trips/{id}/start (trips.dispatch). Transacción propia, bloqueo, RowVersion (409) y ficha.
/// </summary>
public sealed class TripLifecycleService(TeikemDbContext db, StatusService statuses, TripReadService reader)
{
    public async Task StartTrackedAsync(Trip trip, string? comment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var code = await db.StatusCodes.AsNoTracking()
                       .Where(s => s.StatusCodeId == trip.StatusCodeId)
                       .Select(s => s.InternalCode)
                       .FirstOrDefaultAsync(ct) ?? string.Empty;
        if (string.Equals(code, TripStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(DispatchBatchRules.AlreadyStarted(trip.Code));
        if (!trip.IsActive || !string.Equals(code, TripStatuses.Dispatched, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(DispatchBatchRules.NotDispatchedForStart(trip.Code));

        // Defensa en profundidad: una ruta sin chofer o sin vehículo no sale (el despacho ya los exigió).
        var missing = new List<string>();
        if (trip.DriverId is null) missing.Add(DispatchBatchRules.NoDriverMessage);
        if (trip.VehicleId is null) missing.Add(DispatchBatchRules.NoVehicleMessage);
        if (missing.Count > 0) throw new StatusRuleException(DispatchBatchRules.DispatchBlocked(trip.Code, missing));

        var text = string.IsNullOrWhiteSpace(comment) ? DispatchBatchRules.DefaultStartComment : comment.Trim();
        var to = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId,
            TripStatuses.InProgress, text, ct);
        trip.StatusCodeId = to.StatusCodeId;
    }

    /// <summary>
    /// Cierre de la ruta (IN_PROGRESS → COMPLETED). Solo una ruta en curso, y solo si TODAS sus órdenes vigentes ya están en una etapa
    /// terminal (entregada, fallida, devuelta, cancelada…). Si no, 422 con las que faltan. Orden de bloqueo: la ruta (el llamador ya la
    /// tiene) y luego sus órdenes. La transición y su efecto (ActualEndUtc, TripOrder deja de ser vigente) los hace StatusService.
    /// </summary>
    public async Task CompleteTrackedAsync(Trip trip, string? comment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var code = await db.StatusCodes.AsNoTracking()
                       .Where(s => s.StatusCodeId == trip.StatusCodeId)
                       .Select(s => s.InternalCode)
                       .FirstOrDefaultAsync(ct) ?? string.Empty;
        if (string.Equals(code, TripStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(DispatchBatchRules.AlreadyCompleted(trip.Code));
        if (!trip.IsActive || !string.Equals(code, TripStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            throw new StatusRuleException(DispatchBatchRules.NotInProgressForComplete(trip.Code));

        var orderIds = await db.TripOrders.AsNoTracking()
            .Where(o => o.TripId == trip.TripId && o.IsCurrent)
            .Select(o => o.TransportOrderId)
            .ToListAsync(ct);
        var orders = await db.LockOrdersAsync(orderIds, includeStops: false, ct);
        var statusIds = orders.Select(o => o.StatusCodeId).Distinct().ToList();
        var statusRows = await db.StatusCodes.AsNoTracking()
            .Where(s => statusIds.Contains(s.StatusCodeId))
            .Select(s => new { s.StatusCodeId, s.InternalCode, Stage = s.StageKind != null ? s.StageKind.InternalCode : string.Empty })
            .ToListAsync(ct);
        var finished = statusRows.Where(s => DispatchBatchRules.OrderFinishedForClose(s.Stage, s.InternalCode)).Select(s => s.StatusCodeId).ToHashSet();
        var open = orders.Where(o => !finished.Contains(o.StatusCodeId)).Select(o => o.OrderNumber).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (trip.Kind == TripKinds.Pickup)
        {
            // Ruta de RECOGIDA: un envío está atendido si ya se recogió (salió de «Sometido») o si su parada quedó FALLIDA (el chofer dijo por qué no se pudo); los demás bloquean el cierre.
            var initialIds = (await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId) && s.IsInitial).Select(s => s.StatusCodeId).ToListAsync(ct)).ToHashSet();
            var routeId = await db.Routes.AsNoTracking().Where(r => r.TripId == trip.TripId && r.IsActive).Select(r => (int?)r.RouteId).FirstOrDefaultAsync(ct);
            var failedOrders = routeId is null ? new HashSet<int>() : (await (from rs in db.RouteStops.AsNoTracking()
                                                                              join os in db.OrderStops.AsNoTracking() on rs.OrderStopId equals os.OrderStopId
                                                                              join st in db.StatusCodes.AsNoTracking() on rs.StatusCodeId equals st.StatusCodeId
                                                                              where rs.RouteId == routeId && st.InternalCode == RouteStopStatuses.Failed
                                                                              select os.TransportOrderId).ToListAsync(ct)).ToHashSet();
            open = orders.Where(o => initialIds.Contains(o.StatusCodeId) && !failedOrders.Contains(o.TransportOrderId) && !finished.Contains(o.StatusCodeId)).Select(o => o.OrderNumber).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        if (open.Count > 0) throw new StatusRuleException(DispatchBatchRules.OpenOrdersBlockClose(trip.Code, open));

        var text = string.IsNullOrWhiteSpace(comment) ? DispatchBatchRules.DefaultCompleteComment : comment.Trim();
        var to = await statuses.TransitionAsync(StatusDomains.TripStatus, EntityTypes.Trip, trip.TripId, trip.StatusCodeId,
            TripStatuses.Completed, text, ct);
        trip.StatusCodeId = to.StatusCodeId;
    }

    /// <summary>POST /trips/{id}/complete (trips.close): transacción propia, bloqueo, RowVersion (409) y ficha.</summary>
    public async Task<TripDetailDto> CompleteAsync(Guid publicId, TripCompleteRequest? req, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength }) throw new ValidationException("comment", StatusService.CommentTooLongMessage);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);                 // 404 'Ruta no encontrada.'
            TripDispatchRowVersion.Ensure(db, trip, req?.RowVersion);   // 409 si cambió
            await CompleteTrackedAsync(trip, comment, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(publicId, ct);
    }

    public async Task<TripDetailDto> StartAsync(Guid publicId, TripStartRequest? req, CancellationToken ct)
    {
        var comment = string.IsNullOrWhiteSpace(req?.Comment) ? null : req!.Comment!.Trim();
        if (comment is { Length: > StatusService.CommentMaxLength }) throw new ValidationException("comment", StatusService.CommentTooLongMessage);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var trip = await db.LockTripAsync(publicId, ct2);                 // 404 'Ruta no encontrada.'
            TripDispatchRowVersion.Ensure(db, trip, req?.RowVersion);   // 409 si cambió
            await StartTrackedAsync(trip, comment, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);

        return await reader.GetAsync(publicId, ct);
    }
}

/// <summary>
/// Control optimista con el Trip recién bloqueado: ApplyRowVersion (400 si no es base64; 409 al guardar si cambió) y, como
/// el valor actual se leyó bajo UPDLOCK, un rowVersion viejo es 409 de inmediato (antes de disparar efectos).
/// </summary>
internal static class TripDispatchRowVersion
{
    public static void Ensure(TeikemDbContext db, Trip trip, string? rowVersion)
    {
        db.ApplyRowVersion(trip, rowVersion);
        if (string.IsNullOrWhiteSpace(rowVersion) || trip.RowVersion is not { Length: > 0 } current) return;
        var sent = Convert.FromBase64String(rowVersion.Trim());
        if (sent.Length > 0 && !sent.AsSpan().SequenceEqual(current)) throw new ConflictException(DbExtensions.ConcurrencyMessage);
    }
}
