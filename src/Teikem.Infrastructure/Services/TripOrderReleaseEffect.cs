using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 5 (P4) — efecto del dominio OrderStatus sobre las rutas. Actúa SOLO sobre TRANSPORT_ORDER cuando la orden pasa a
/// CANCELLED o a un lateral (ON_HOLD/PARTIAL/FAILED) y tiene un TripOrder vigente:
/// - ruta abierta (DRAFT/PLANNED): la bloquea (TripQueries.LockTripAsync) y libera la orden (RouteWriter.ReleaseOrderAsync:
///   DELETE del TripOrder y de su RouteStop de la versión vigente, resecuencia y recálculo). El estatus de la orden no se toca;
/// - ruta despachada o en curso (DISPATCHED/IN_PROGRESS) y destino CANCELLED: 422 'La orden va en la ruta … ya despachada; no
///   se puede cancelar mientras la ruta esté en curso.';
/// - lateral en una ruta despachada: sin cambios (lo decide el Lote 7).
/// No guarda: todo queda en la unidad de trabajo del llamador. RouteWriter se resuelve de forma perezosa (evita el ciclo
/// StatusService → efectos → este efecto → RouteWriter → StatusService).
/// </summary>
public sealed class TripOrderReleaseEffect(TeikemDbContext db, IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        var toCancelled = string.Equals(context.To.InternalCode, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);
        var toLateral = string.Equals(context.To.StageKind?.InternalCode, StageKinds.Lateral, StringComparison.OrdinalIgnoreCase);
        if (!toCancelled && !toLateral) return;

        var orderId = context.EntityId;
        var link = await db.TripOrders.AsNoTracking()
            .Where(x => x.TransportOrderId == orderId && x.IsCurrent)
            .Select(x => new { x.TripId })
            .FirstOrDefaultAsync(ct);
        if (link is null) return;

        var head = await db.Trips.AsNoTracking()
            .Where(t => t.TripId == link.TripId)
            .Select(t => new { t.Code, t.StatusCodeId })
            .FirstOrDefaultAsync(ct);
        if (head is null) return;
        var statusCode = await StatusCodeOfAsync(head.StatusCodeId, ct);

        if (TripRules.IsEditable(statusCode))
        {
            var trip = await db.LockTripAsync(link.TripId, ct);
            // Re-verificación bajo bloqueo: la ruta pudo despacharse entre la lectura y el bloqueo.
            var lockedCode = await StatusCodeOfAsync(trip.StatusCodeId, ct);
            if (TripRules.IsEditable(lockedCode))
            {
                var writer = services.GetRequiredService<RouteWriter>();
                await writer.ReleaseOrderAsync(trip, orderId, ct);
                return;
            }
            if (toCancelled && IsDispatched(lockedCode))
                throw new StatusRuleException(DispatchBatchRules.CannotCancelOrderInDispatchedTrip(trip.Code));
            return;
        }

        if (toCancelled && IsDispatched(statusCode))
            throw new StatusRuleException(DispatchBatchRules.CannotCancelOrderInDispatchedTrip(head.Code));
        // Lateral en una ruta despachada: sin cambios.
    }

    private async Task<string> StatusCodeOfAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId)
               .Select(s => s.InternalCode).FirstOrDefaultAsync(ct) ?? string.Empty;

    private static bool IsDispatched(string? code)
        => string.Equals(code, TripStatuses.Dispatched, StringComparison.OrdinalIgnoreCase)
           || string.Equals(code, TripStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
}
