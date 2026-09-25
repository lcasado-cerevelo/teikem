using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Efecto del dominio PortalUserStatus (Lote 2, P6). Corre dentro de StatusService.TransitionAsync:
/// - Al llegar a SUSPENDED o a DISABLED: la cuenta (ApplicationUser) pasa a IsActive=0, se rota el SecurityStamp
///   (invalida los access tokens vivos) y se revocan sus refresh tokens; queda SecurityEvent TOKEN_REVOKED/SUCCESS.
/// - Al llegar a ACTIVE desde SUSPENDED (reactivación): la cuenta vuelve a IsActive=1; la contraseña se conserva.
/// - Al llegar a ACTIVE desde INVITED (aceptación de la invitación): nada; ya lo hizo PortalUserService.
/// El login del portal sigue rechazado por AuthService hasta el módulo 10 (Portal de clientes).
/// Nota: el efecto corre DENTRO de la transacción del servicio (RunInTransactionAsync), que ya tiene bloqueada la fila
/// de AspNetUsers; por eso el SecurityEvent se inserta con este mismo DbContext y no con ISecurityEventWriter (que usa
/// otra conexión y se quedaría esperando la FK SecurityEvent.UserId → AspNetUsers hasta el timeout). Así el evento se
/// confirma o se revierte junto con la transición.
/// </summary>
public sealed class PortalUserStatusEffect(TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, ILookupCache lookups) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.PortalUserStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        var to = context.To.InternalCode;
        if (Same(to, PortalUserStatuses.Suspended)) { await DeactivateAccountAsync(context.EntityId, "portal_user_suspended", ct); return; }
        if (Same(to, PortalUserStatuses.Disabled)) { await DeactivateAccountAsync(context.EntityId, "portal_user_disabled", ct); return; }
        if (Same(to, PortalUserStatuses.Active) && context.From is not null && Same(context.From.InternalCode, PortalUserStatuses.Suspended))
            await ReactivateAccountAsync(context.EntityId, ct);
    }

    private async Task DeactivateAccountAsync(int portalUserId, string reason, CancellationToken ct)
    {
        // Bajo el filtro global de tenant: el PortalUser tiene que ser de la compañía activa.
        var pu = await db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(p => p.PortalUserId == portalUserId, ct)
                 ?? throw new NotFoundException("Usuario de portal", portalUserId);
        if (pu.UserId is null) return;
        var user = await users.FindByIdAsync(pu.UserId.Value.ToString());
        if (user is null) return;

        user.IsActive = false;
        await users.UpdateSecurityStampAsync(user); // persiste IsActive y rota el stamp: los access tokens vivos dejan de valer

        var tokens = await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id && t.RevokedAtUtc == null).ToListAsync(ct);
        if (tokens.Count > 0)
        {
            foreach (var t in tokens) t.RevokedAtUtc = DateTime.UtcNow;
            db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        }

        // SecurityEvent TOKEN_REVOKED en la misma unidad de trabajo (ver nota de la clase).
        db.SecurityEvents.Add(new SecurityEvent
        {
            TenantId = pu.TenantId, UserId = user.Id,
            EventTypeLookupId = await lookups.GetIdAsync(LookupDomains.SecurityEventType, SecurityEventTypes.TokenRevoked, ct),
            OutcomeLookupId = await lookups.GetIdAsync(LookupDomains.SecurityOutcome, SecurityOutcomes.Success, ct),
            IpAddress = tenant.IpAddress, UserAgent = tenant.UserAgent is { Length: > 300 } ua ? ua[..300] : tenant.UserAgent,
            DetailJson = JsonSerializer.Serialize(new { reason, portalUser = pu.PortalUserId, revokedTokens = tokens.Count, by = tenant.UserId, byPlatformAdmin = tenant.IsPlatformAdmin }),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct); // SecurityEvent no lleva [AuditEntity]: no genera AuditLog
    }

    private async Task ReactivateAccountAsync(int portalUserId, CancellationToken ct)
    {
        var pu = await db.PortalUsers.AsNoTracking().FirstOrDefaultAsync(p => p.PortalUserId == portalUserId, ct)
                 ?? throw new NotFoundException("Usuario de portal", portalUserId);
        if (pu.UserId is null) return;
        var user = await users.FindByIdAsync(pu.UserId.Value.ToString());
        if (user is null || user.IsActive) return;
        user.IsActive = true;
        await users.UpdateAsync(user);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
