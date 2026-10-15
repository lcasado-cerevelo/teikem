using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 54 — avisos manuales (permiso notifications.send): quien tiene el permiso envía un aviso a personas, a roles o a todos; cada persona lo
/// recibe en su campanita («De Ana») y NO se responde. El historial (Announcement) dice a quién se envió y cuántas personas lo leyeron.
/// - Destinatarios: personas activas de la compañía; un rol = quienes lo tienen hoy; «todos» = todas las personas activas. Sin repetir, y quien
///   envía no se avisa a sí mismo.
/// - Se guarda todo en una transacción y DESPUÉS se empuja en vivo (conexión de la mensajería) a quienes tienen Teikem abierto; a los demás les
///   queda pendiente en la campanita.
/// - Los avisos manuales no se silencian ni se editan ni se borran.
/// </summary>
public sealed class AnnouncementService(TeikemDbContext db, ITenantContext tenant, MessagingService messaging, IMessageLiveNotifier live)
{
    public const int MaxTitleLength = 150;
    public const int MaxBodyLength = 1000;
    public const int MaxPeoplePage = 500;
    public const string LiveEvent = "NoticeReceived";

    public const string TitleRequiredMessage = "Escriba el título del aviso.";
    public const string TitleTooLongMessage = "El título del aviso no puede pasar de 150 caracteres.";
    public const string BodyTooLongMessage = "El texto del aviso no puede pasar de 1000 caracteres.";
    public const string NeedAudienceMessage = "Escoja al menos una persona, un rol o a todos.";
    public const string NobodyMessage = "No hay personas a quienes enviar el aviso.";
    public const string UserNotFoundMessage = "Usuario no encontrado en esta compañía.";
    public const string RoleNotFoundMessage = "Rol no encontrado.";

    private TenantContext T => (TenantContext)tenant;

    /// <summary>Personas a las que se puede avisar (activas de la compañía, menos quien pregunta); q = parte del nombre o del correo. Hasta 500 por página, con el total.</summary>
    public async Task<PagedResult<AnnouncementPersonDto>> PeopleAsync(string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPeoplePage);
        var me = T.RequireUserId();
        var members = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        var users = db.Users.AsNoTracking().Where(u => members.Contains(u.Id) && u.IsActive && u.Id != me);
        var term = (q ?? string.Empty).Trim();
        if (term.Length > 0) users = users.Where(u => (u.FullName != null && u.FullName.Contains(term)) || (u.Email != null && u.Email.Contains(term)));
        var total = await users.CountAsync(ct);
        var rows = await users.OrderBy(u => u.FullName ?? u.Email).ThenBy(u => u.Id).Skip(skip).Take(take).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        return new PagedResult<AnnouncementPersonDto>(rows.Select(r => new AnnouncementPersonDto(r.Id, string.IsNullOrWhiteSpace(r.FullName) ? r.Email ?? $"#{r.Id}" : r.FullName, r.Email ?? string.Empty)).ToList(), total, skip, take);
    }

    /// <summary>Los roles de la compañía con cuántas personas activas tiene cada uno.</summary>
    public Task<IReadOnlyList<MessageRoleDto>> RolesAsync(CancellationToken ct) => messaging.RolesAsync(ct);

    public async Task<AnnouncementDto> SendAsync(AnnouncementRequest req, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        var title = (req.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw new ValidationException("title", TitleRequiredMessage);
        if (title.Length > MaxTitleLength) throw new ValidationException("title", TitleTooLongMessage);
        var body = string.IsNullOrWhiteSpace(req.Text) ? null : req.Text.Trim();
        if (body is { Length: > MaxBodyLength }) throw new ValidationException("text", BodyTooLongMessage);

        var toAll = req.All == true;
        var userIds = (req.UserIds ?? Array.Empty<int>()).Distinct().ToList();
        var roleIds = (req.RoleIds ?? Array.Empty<int>()).Distinct().ToList();
        if (!toAll && userIds.Count == 0 && roleIds.Count == 0) throw new ValidationException("userIds", NeedAudienceMessage);

        var members = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        var labels = new List<string>();
        HashSet<int> recipients;
        if (toAll)
        {
            recipients = (await db.Users.AsNoTracking().Where(u => members.Contains(u.Id) && u.IsActive && u.Id != me).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
            labels.Add("Todas las personas");
        }
        else
        {
            recipients = new HashSet<int>();
            var roles = await db.AppRoles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId) && r.TenantId == tenantId && r.IsActive).Select(r => new { r.RoleId, r.Name }).ToListAsync(ct);
            if (roles.Count != roleIds.Count) throw new ValidationException("roleIds", RoleNotFoundMessage);
            foreach (var r in roles.OrderBy(r => r.Name)) labels.Add(r.Name);
            if (roleIds.Count > 0)
            {
                var holders = await db.AppUserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
                foreach (var id in await ActiveAsync(holders, members, ct)) recipients.Add(id);
            }
            if (userIds.Count > 0)
            {
                var named = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id) && u.IsActive && members.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
                if (named.Count != userIds.Count) throw new ValidationException("userIds", UserNotFoundMessage);
                foreach (var u in named.Where(u => u.Id != me).OrderBy(u => u.FullName ?? u.Email)) labels.Add(string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? $"#{u.Id}" : u.FullName);
                foreach (var u in named) recipients.Add(u.Id);
            }
            recipients.Remove(me);
        }
        if (recipients.Count == 0) throw new ValidationException("userIds", NobodyMessage);

        var sender = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => new { u.FullName, u.Email }).SingleAsync(ct);
        var senderName = string.IsNullOrWhiteSpace(sender.FullName) ? sender.Email ?? $"#{me}" : sender.FullName;
        var now = DateTime.UtcNow;
        var ann = new Announcement
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Title = title, Body = body, SentBy = me, SentAtUtc = now, ToAll = toAll,
            AudienceText = AudienceText(labels), RecipientCount = recipients.Count,
        };
        await db.RunInTransactionAsync(async c =>
        {
            db.Announcements.Add(ann);
            await db.SaveChangesAsync(c);
            foreach (var chunk in recipients.OrderBy(i => i).Chunk(500))
            {
                foreach (var uid in chunk)
                    db.UserNotifications.Add(new UserNotification { TenantId = tenantId, UserId = uid, EventCode = AnnouncementCodes.EventCode, Title = title, Body = body, CreatedAtUtc = now, AnnouncementId = ann.AnnouncementId });
                await db.SaveChangesAsync(c);
            }
        }, ct);
        await live.PushAsync(tenantId, recipients.ToList(), LiveEvent, new MessageLiveEvent(tenantId, ann.PublicId, null, me, senderName, title, false), ct);
        return new AnnouncementDto(ann.PublicId, ann.Title, ann.Body, senderName, ann.SentAtUtc, ann.ToAll, ann.AudienceText, ann.RecipientCount, 0);
    }

    /// <summary>Los avisos enviados en la compañía, el más reciente primero, con cuántas personas lo leyeron.</summary>
    public async Task<PagedResult<AnnouncementDto>> ListAsync(int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 200);
        var total = await db.Announcements.AsNoTracking().CountAsync(ct);
        var page = await db.Announcements.AsNoTracking().OrderByDescending(a => a.AnnouncementId).Skip(skip).Take(take).ToListAsync(ct);
        var ids = page.Select(a => (long?)a.AnnouncementId).ToList();
        var reads = await db.UserNotifications.AsNoTracking().Where(n => ids.Contains(n.AnnouncementId) && n.IsRead).GroupBy(n => n.AnnouncementId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        var senderIds = page.Select(a => a.SentBy).Distinct().ToList();
        var names = (await db.Users.AsNoTracking().Where(u => senderIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? $"#{u.Id}" : u.FullName);
        return new PagedResult<AnnouncementDto>(page.Select(a => new AnnouncementDto(a.PublicId, a.Title, a.Body, names.GetValueOrDefault(a.SentBy, string.Empty), a.SentAtUtc, a.ToAll, a.AudienceText, a.RecipientCount,
            reads.FirstOrDefault(r => r.Id == a.AnnouncementId)?.N ?? 0)).ToList(), total, skip, take);
    }

    /// <summary>«Gerencia, Ana Rivera, Luis Casado, +4»: hasta 3 nombres y cuántos más; nunca más de 300 caracteres.</summary>
    public static string AudienceText(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0) return "—";
        var text = labels.Count <= 3 ? string.Join(", ", labels) : $"{string.Join(", ", labels.Take(3))}, +{labels.Count - 3}";
        return text.Length <= 300 ? text : text[..299] + "…";
    }

    private async Task<List<int>> ActiveAsync(IReadOnlyCollection<int> ids, IQueryable<int> members, CancellationToken ct)
        => ids.Count == 0 ? new List<int>() : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && members.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
}
