using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio ContractStatus (Lote 2, P3). Al llegar a ACTIVE aplica ContractRules.EnsureCanActivate:
/// el cliente no puede estar SUSPENDED y solo puede haber un contrato ACTIVE por cliente a la vez (422 si no).
/// La fecha fin NO se evalúa (informativa; decisión de Luis). Al llegar a EXPIRED/CANCELLED no hace nada:
/// las tarifas y servicios especiales quedan como historial. Corre dentro de StatusService.TransitionAsync,
/// antes de que el llamador asigne el StatusCodeId nuevo, por eso consulta la BD (bajo el filtro global de tenant).
/// </summary>
public sealed class ContractStatusEffect(TeikemDbContext db) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.ContractStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.To.InternalCode, ContractStatuses.Active, StringComparison.OrdinalIgnoreCase)) return;

        var info = await db.Contracts.AsNoTracking()
            .Where(c => c.ContractId == context.EntityId)
            .Select(c => new { c.ClientId, ClientStatus = c.Client!.Status!.InternalCode })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Contrato", context.EntityId);

        var hasOtherActive = await db.Contracts.AsNoTracking()
            .AnyAsync(c => c.ClientId == info.ClientId && c.ContractId != context.EntityId && c.IsActive
                           && c.Status!.InternalCode == ContractStatuses.Active, ct);

        ContractRules.EnsureCanActivate(info.ClientStatus, hasOtherActive);
    }
}
