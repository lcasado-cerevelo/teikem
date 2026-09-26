using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio DriverStatus (Lote 4, P6). Al entrar a una etapa TERMINAL ('Eliminar chofer', baja definitiva)
/// cierra hoy todas las tarifas del chofer que siguen vivas (entrega, intento y viaje), sin borrar historial (R17):
/// - una fila abierta queda con EffectiveTo = max(hoy, EffectiveFrom) (EffectiveDated.CloseNotBefore: una fila que
///   nacía en el futuro queda de longitud cero, nunca con EffectiveTo &lt; EffectiveFrom);
/// - una fila ya cerrada con fecha futura se recorta a la misma fecha, para que no quede ninguna tarifa vigente.
/// Sus viajes (DriverTrip) se conservan para pagarle lo pendiente. Trabaja sobre entidades tracked y no guarda: el
/// servicio que transiciona persiste todo junto con el nuevo StatusCodeId, dentro de su transacción.
/// </summary>
public sealed class DriverRatesRetirementEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.DriverStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.To.StageKind?.InternalCode, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase)) return;

        var driverId = context.EntityId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Bajo el filtro global de tenant: solo filas de la compañía activa.
        var delivery = await db.DriverDeliveryRates
            .Where(r => r.DriverId == driverId && r.IsActive && (r.EffectiveTo == null || r.EffectiveTo > today)).ToListAsync(ct);
        foreach (var r in delivery) CloseToday(r, today);

        var attempts = await db.DriverAttemptRates
            .Where(r => r.DriverId == driverId && r.IsActive && (r.EffectiveTo == null || r.EffectiveTo > today)).ToListAsync(ct);
        foreach (var r in attempts) CloseToday(r, today);

        var trips = await db.DriverTripRates
            .Where(r => r.DriverId == driverId && r.IsActive && (r.EffectiveTo == null || r.EffectiveTo > today)).ToListAsync(ct);
        foreach (var r in trips) CloseToday(r, today);
    }

    // Abierta o cerrada a futuro: queda cerrada hoy (sin bajar de su inicio). Regla pura en EffectiveDated.CutOffAt.
    private static void CloseToday(IEffectiveDated row, DateOnly today) => EffectiveDated.CutOffAt(row, today);
}
