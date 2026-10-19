using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 119 (P10) — la persona del portal decide qué avisos quiere recibir. La compañía manda: un aviso que ella no encendió para el cliente no se puede encender aquí (se ve «No disponible»); la persona solo apaga lo que no
/// quiere (correo y bandeja). Se guarda una fila por persona y aviso. Es del cliente de la sesión y de la persona de la sesión: nadie lee ni cambia las preferencias de otro.
/// </summary>
public sealed class PortalNoticePrefService(TeikemDbContext db, ITenantContext tenant, ITransactionalEmailSender? email = null)
{
    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    public const string UnknownKindFormat = "El aviso '{0}' no existe.";

    private async Task<int> PortalUserIdAsync(CancellationToken ct)
    {
        var userId = T.RequireUserId();
        var cid = ClientId;
        return await db.Set<PortalUser>().AsNoTracking().Where(p => p.UserId == userId && p.ClientId == cid && p.IsActive).Select(p => (int?)p.PortalUserId).FirstOrDefaultAsync(ct)
               ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
    }

    public async Task<PortalNoticePrefsDto> GetAsync(CancellationToken ct)
    {
        var cid = ClientId;
        var puid = await PortalUserIdAsync(ct);
        var settings = await db.Set<ClientNotificationSetting>().AsNoTracking().Where(s => s.ClientId == cid).ToListAsync(ct);
        var mine = await db.Set<PortalNoticePref>().AsNoTracking().Where(p => p.PortalUserId == puid).ToListAsync(ct);
        var list = ClientNotificationKinds.All.Select(kind =>
        {
            var s = settings.FirstOrDefault(x => x.Kind == kind);
            var m = mine.FirstOrDefault(x => x.Kind == kind);
            return new PortalNoticePrefDto(kind, s?.EmailEnabled ?? false, s?.AppEnabled ?? false, m?.EmailEnabled ?? true, m?.AppEnabled ?? true);
        }).ToList();
        return new PortalNoticePrefsDto(list, email?.IsConfigured ?? false);
    }

    public async Task<PortalNoticePrefsDto> SaveAsync(PortalNoticePrefsRequest? req, CancellationToken ct)
    {
        if (req?.Prefs is null) throw new ValidationException("prefs", "Indique los avisos que quiere recibir.");
        foreach (var p in req.Prefs)
            if (!ClientNotificationKinds.IsKnown(p.Kind)) throw new ValidationException("kind", string.Format(UnknownKindFormat, p.Kind));
        var puid = await PortalUserIdAsync(ct);
        var tenantId = T.RequireTenantId();
        var rows = await db.Set<PortalNoticePref>().AsTracking().Where(p => p.PortalUserId == puid).ToListAsync(ct);
        foreach (var p in req.Prefs)
        {
            var kind = p.Kind.Trim().ToUpperInvariant();
            var row = rows.FirstOrDefault(r => r.Kind == kind);
            if (row is null)
            {
                if (p.EmailEnabled && p.AppEnabled) continue;   // todo encendido es lo de siempre: no hace falta guardar nada
                row = new PortalNoticePref { TenantId = tenantId, PortalUserId = puid, Kind = kind };
                db.Set<PortalNoticePref>().Add(row);
                rows.Add(row);
            }
            (row.EmailEnabled, row.AppEnabled, row.UpdatedAtUtc) = (p.EmailEnabled, p.AppEnabled, DateTime.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }
}
