using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio DriverStatus (Lote 4, P2). Corre dentro de StatusService.TransitionAsync, en la transacción del
/// servicio que transiciona. Al entrar a una etapa TERMINAL (INACTIVE = 'Eliminar chofer', baja definitiva):
/// - el chofer queda IsActive = 0 y sin usuario vinculado (UserId = NULL: la cuenta queda libre para otro chofer);
/// - sus dispositivos quedan IsActive = 0 (dejan de recibir notificaciones de la app);
/// - se borran sus filas DriverZone (asociación de estado actual; el cambio queda en AuditLog bajo DRIVER).
/// El cierre de sus tarifas abiertas lo hace DriverRatesRetirementEffect; sus viajes se conservan.
/// Trabaja sobre la entidad tracked (el servicio ya la cargó con tracking: EF devuelve la misma instancia) y no guarda:
/// el servicio persiste todo junto con el nuevo StatusCodeId.
/// </summary>
public sealed class DriverStatusEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.DriverStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.To.StageKind?.InternalCode, StageKinds.Terminal, StringComparison.OrdinalIgnoreCase)) return;

        // Bajo el filtro global de tenant: el chofer tiene que ser de la compañía activa.
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.DriverId == context.EntityId, ct)
                     ?? throw new NotFoundException("Chofer");
        driver.IsActive = false;
        driver.UserId = null;

        var devices = await db.DriverDevices.Where(d => d.DriverId == driver.DriverId && d.IsActive).ToListAsync(ct);
        foreach (var device in devices) device.IsActive = false;

        var zones = await db.DriverZones.Where(z => z.DriverId == driver.DriverId).ToListAsync(ct);
        if (zones.Count > 0) db.DriverZones.RemoveRange(zones);
    }
}
