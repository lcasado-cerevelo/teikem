using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio DriverTripStatus (Lote 4, P7): al entrar a CANCELLED el viaje queda IsActive = 0 en la MISMA entidad
/// tracked del llamador (o se carga tracked bajo el filtro de tenant). Así el índice filtrado UX_DriverTrip_Order
/// (TransportOrderId WHERE IsActive = 1) garantiza en BD un solo viaje vigente por orden, y el listado por defecto (solo
/// vigentes) deja fuera los cancelados. No transiciona nada; corre antes de que el llamador guarde.
/// </summary>
public sealed class DriverTripStatusEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.DriverTripStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.DriverTrip, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, DriverTripStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)) return;

        var trip = db.DriverTrips.Local.FirstOrDefault(t => t.DriverTripId == context.EntityId)
                   ?? await db.DriverTrips.FirstOrDefaultAsync(t => t.DriverTripId == context.EntityId, ct);
        if (trip is null) return; // fuera del tenant: el llamador ya habría respondido 404
        trip.IsActive = false;
    }
}
