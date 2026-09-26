using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio VehicleStatus (Lote 4, P1). Al entrar a una etapa TERMINAL (INACTIVE = baja definitiva) pone
/// IsActive = 0 en la entidad tracked del vehículo; no transiciona nada más. Corre dentro de StatusService.TransitionAsync,
/// antes de que el llamador asigne el StatusCodeId nuevo y guarde: el cambio se confirma en la misma unidad de trabajo.
/// Busca primero la instancia ya tracked (la que el servicio va a guardar) y, si no la hay, la carga tracked bajo el
/// filtro global de tenant. Un vehículo dado de baja no se reactiva (VehicleService.SetActiveAsync → 409).
/// </summary>
public sealed class VehicleStatusEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.VehicleStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.To.StageKind?.InternalCode, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase)) return;

        var vehicle = db.Vehicles.Local.FirstOrDefault(v => v.VehicleId == context.EntityId)
                      ?? await db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == context.EntityId, ct);
        if (vehicle is null) return; // fuera del tenant: el llamador ya habría respondido 404
        vehicle.IsActive = false;
    }
}
