using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio OrderStatus (Lote 4, P7): cuando una orden pasa a CANCELLED, su DriverTrip OPEN vigente (entrega
/// especial con chofer) pasa a CANCELLED con el comentario 'Orden {número} cancelada'; DriverTripStatusEffect lo deja
/// IsActive = 0. Un viaje ya liquidado (SETTLED) no se toca. No guarda: todo queda en la unidad de trabajo del llamador.
/// StatusService se resuelve de forma perezosa (como OrderStatusEffect): inyectarlo formaría un ciclo en DI
/// (StatusService → efectos → este efecto → StatusService).
/// </summary>
public sealed class DriverTripOrderEffect(TeikemDbContext db, IServiceProvider services) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, OrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)) return;

        var orderId = context.EntityId;
        var openId = await db.StatusIdAsync(StatusDomains.DriverTripStatus, DriverTripStatuses.Open, ct);
        var trip = db.DriverTrips.Local.FirstOrDefault(t => t.TransportOrderId == orderId && t.IsActive && t.StatusCodeId == openId)
                   ?? await db.DriverTrips.FirstOrDefaultAsync(t => t.TransportOrderId == orderId && t.IsActive && t.StatusCodeId == openId, ct);
        if (trip is null) return;

        var orderNumber = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == orderId)?.OrderNumber
                          ?? await db.TransportOrders.AsNoTracking().Where(o => o.TransportOrderId == orderId)
                              .Select(o => o.OrderNumber).FirstOrDefaultAsync(ct);

        var statuses = services.GetRequiredService<StatusService>();
        var to = await statuses.TransitionAsync(StatusDomains.DriverTripStatus, EntityTypes.DriverTrip, trip.DriverTripId,
            trip.StatusCodeId, DriverTripStatuses.Cancelled, $"Orden {orderNumber} cancelada", ct);
        trip.StatusCodeId = to.StatusCodeId;
    }
}
