using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 51 — la bandeja de avisos de la persona que inició sesión (la campanita): solo ve y toca los suyos, de la compañía activa.
/// No pide permiso (cada quien su bandeja). Silenciar solo aplica a los eventos opcionales.
/// </summary>
public sealed class NotificationInboxService(TeikemDbContext db, ITenantContext tenant, NotificationEventCatalog catalog)
{
    private int UserId => ((TenantContext)tenant).RequireUserId();

    public async Task<PagedResult<UserNotificationDto>> ListAsync(bool unreadOnly, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 100);
        var uid = UserId;
        var q = db.UserNotifications.AsNoTracking().Where(n => n.UserId == uid);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.UserNotificationId).Skip(skip).Take(take).ToListAsync(ct);
        return new PagedResult<UserNotificationDto>(rows.Select(ToDto).ToList(), total, skip, take);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken ct)
    {
        var uid = UserId;
        return new UnreadCountDto(await db.UserNotifications.AsNoTracking().CountAsync(n => n.UserId == uid && !n.IsRead, ct));
    }

    public async Task<UserNotificationDto> MarkReadAsync(long id, CancellationToken ct)
    {
        var uid = UserId;
        var n = await db.UserNotifications.FirstOrDefaultAsync(x => x.UserNotificationId == id && x.UserId == uid, ct) ?? throw new NotFoundException("Aviso");
        if (!n.IsRead)
        {
            n.IsRead = true;
            n.ReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(n);
    }

    public async Task<MarkedReadDto> MarkAllReadAsync(CancellationToken ct)
    {
        var uid = UserId;
        var now = DateTime.UtcNow;
        var marked = await db.UserNotifications.Where(n => n.UserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAtUtc, now), ct);
        return new MarkedReadDto(marked);
    }

    /// <summary>Los eventos que se pueden silenciar y cuáles tiene silenciados.</summary>
    public async Task<IReadOnlyList<NotificationPrefDto>> PrefsAsync(CancellationToken ct)
    {
        var uid = UserId;
        var muted = (await db.UserNotificationPrefs.AsNoTracking().Where(p => p.UserId == uid && p.IsMuted).Select(p => p.EventCode).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return catalog.All.Where(e => e.IsOptional).OrderBy(e => e.NameEs, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new NotificationPrefDto(e.Code, e.NameEs, e.NameEn, muted.Contains(e.Code))).ToList();
    }

    public async Task<NotificationPrefDto> SetMutedAsync(string eventCode, NotificationPrefRequest req, CancellationToken ct)
    {
        var def = catalog.Find(eventCode) ?? throw new NotFoundException("Evento de aviso");
        if (!def.IsOptional) throw new ConflictException(NotificationRules.NotMutableMessage);
        var uid = UserId;
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var muted = req.Muted ?? true;
        var pref = await db.UserNotificationPrefs.FirstOrDefaultAsync(p => p.UserId == uid && p.EventCode == def.Code, ct);
        if (pref is null) db.UserNotificationPrefs.Add(pref = new UserNotificationPref { TenantId = tenantId, UserId = uid, EventCode = def.Code, IsMuted = muted });
        else pref.IsMuted = muted;
        await db.SaveChangesAsync(ct);
        return new NotificationPrefDto(def.Code, def.NameEs, def.NameEn, muted);
    }

    private static UserNotificationDto ToDto(UserNotification n) => new(n.UserNotificationId, n.EventCode, n.Title, n.Body, n.LinkUrl, n.IsRead, n.CreatedAtUtc);
}
