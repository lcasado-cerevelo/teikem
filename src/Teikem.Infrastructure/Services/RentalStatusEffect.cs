using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 27 (Rentas, plan 4.5) — efecto del dominio RentalStatus: al pasar a ON_RENT sella DispatchedAtUtc; a RETURNED o CANCELLED
/// sella ClosedAtUtc; y NUNCA deja cancelar una renta con equipos ya despachados (422 'Solo se cancela una renta en Borrador o
/// Programada; para terminarla registre la devolución.'), aunque una compañía cambie las entradas laterales de CANCELLED.
/// Corre dentro de StatusService.TransitionAsync, en la transacción del llamador (que ya bloqueó la renta): toma la renta del
/// ChangeTracker (o la carga tracked) y no guarda. No usa el ledger (sin dependencias de StatusService: no forma ciclo).
/// </summary>
public sealed class RentalStatusEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.RentalStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.Rental, StringComparison.OrdinalIgnoreCase)) return;
        var to = context.To.InternalCode;
        if (to is not (RentalStatuses.OnRent or RentalStatuses.Returned or RentalStatuses.Cancelled)) return;

        var rentalId = context.EntityId;
        var rental = db.Rentals.Local.FirstOrDefault(r => r.RentalId == rentalId)
                     ?? await db.Rentals.FirstOrDefaultAsync(r => r.RentalId == rentalId, ct);
        if (rental is null) return;
        var now = DateTime.UtcNow;
        switch (to)
        {
            case RentalStatuses.OnRent:
                rental.DispatchedAtUtc ??= now;
                break;
            case RentalStatuses.Cancelled:
                var dispatched = db.RentalLines.Local.Any(l => l.RentalId == rentalId && l.DispatchedAtUtc != null)
                                 || await db.RentalLines.AsNoTracking().AnyAsync(l => l.RentalId == rentalId && l.DispatchedAtUtc != null, ct);
                if (dispatched) throw new StatusRuleException(RentalRules.CancelNotAllowed);
                rental.ClosedAtUtc = now;
                break;
            default:
                rental.ClosedAtUtc = now;
                break;
        }
    }
}
