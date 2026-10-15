using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 53 — mensajería interna con hilos (permiso messages.use, de todos los roles). Cada persona ve y toca SOLO sus hilos de la compañía
/// activa: si no es parte (o ya salió) el hilo «no existe» (404, no se revela que existe).
/// - Una sola persona sin rol → hilo entre dos (DIRECT, único por pareja: escribirle otra vez vuelve al mismo hilo). Varias personas o un
///   rol → grupo (GROUP); un rol entra «en foto»: las personas activas que lo tienen hoy.
/// - Los mensajes nunca se editan ni se borran. No leídos: los de otros más nuevos que lo último leído (y que la entrada al grupo).
/// - Quien entra a un grupo después ve el historial solo desde que entra. Solo quien creó el grupo agrega personas; cualquiera se sale
///   de un grupo (de uno entre dos no).
/// - Cada mensaje se empuja en vivo a los demás (IMessageLiveNotifier) DESPUÉS de guardarse.
/// </summary>
public sealed class MessagingService(TeikemDbContext db, ITenantContext tenant, PresenceService presence, IMessageLiveNotifier live)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- directorio

    public async Task<IReadOnlyList<MessagePersonDto>> PeopleAsync(string? q, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 50);
        var me = T.RequireUserId();
        var ids = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        var users = db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && u.Id != me);
        var term = (q ?? string.Empty).Trim();
        if (term.Length > 0) users = users.Where(u => (u.FullName != null && u.FullName.Contains(term)) || (u.Email != null && u.Email.Contains(term)));
        var rows = await users.OrderBy(u => u.FullName ?? u.Email).Take(take).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        var seen = await presence.LoadAsync(rows.Select(r => r.Id).ToList(), ct);
        var now = DateTime.UtcNow;
        return rows.Select(r => Person(r.Id, r.FullName, r.Email, seen, now)).ToList();
    }

    public async Task<IReadOnlyList<MessageRoleDto>> RolesAsync(CancellationToken ct)
    {
        var tenantId = T.RequireTenantId();
        var memberIds = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        var roles = await db.AppRoles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive).OrderBy(r => r.Name).Select(r => new { r.RoleId, r.Name }).ToListAsync(ct);
        var counts = await (from ur in db.AppUserRoles.AsNoTracking()
                            join u in db.Users.AsNoTracking() on ur.UserId equals u.Id
                            where u.IsActive && memberIds.Contains(u.Id)
                            group ur by ur.RoleId into g
                            select new { RoleId = g.Key, N = g.Select(x => x.UserId).Distinct().Count() }).ToListAsync(ct);
        return roles.Select(r => new MessageRoleDto(r.RoleId, r.Name, counts.FirstOrDefault(c => c.RoleId == r.RoleId)?.N ?? 0)).ToList();
    }

    // ---------------------------------------------------------------- lista y lectura

    public async Task<IReadOnlyList<MessageThreadSummaryDto>> ListThreadsAsync(CancellationToken ct)
    {
        var me = T.RequireUserId();
        var mine = await (from m in db.MessageThreadMembers.AsNoTracking()
                          where m.UserId == me && m.LeftAtUtc == null
                          join t in db.MessageThreads.AsNoTracking() on m.MessageThreadId equals t.MessageThreadId
                          select new { Thread = t, Member = m }).ToListAsync(ct);
        if (mine.Count == 0) return Array.Empty<MessageThreadSummaryDto>();
        var threadIds = mine.Select(x => x.Thread.MessageThreadId).ToList();
        var unread = await UnreadByThreadAsync(me, ct);
        var members = await db.MessageThreadMembers.AsNoTracking().Where(m => threadIds.Contains(m.MessageThreadId) && m.LeftAtUtc == null).ToListAsync(ct);
        var names = await NamesAsync(members.Select(m => m.UserId).Concat(mine.Where(x => x.Thread.LastSenderId != null).Select(x => x.Thread.LastSenderId!.Value)).Distinct().ToList(), ct);
        var seen = await presence.LoadAsync(members.Select(m => m.UserId).Distinct().ToList(), ct);
        var now = DateTime.UtcNow;
        return mine.OrderByDescending(x => x.Thread.LastMessageAtUtc).ThenByDescending(x => x.Thread.MessageThreadId)
            .Select(x => Summary(x.Thread, x.Member, members.Where(m => m.MessageThreadId == x.Thread.MessageThreadId).ToList(), names, seen, now, me, unread.GetValueOrDefault(x.Thread.MessageThreadId)))
            .ToList();
    }

    public async Task<MessageThreadDto> GetThreadAsync(Guid publicId, long? before, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? MessagingRules.PageSize : take, 1, 100);
        var me = T.RequireUserId();
        var (thread, member) = await LoadMineAsync(publicId, ct);
        var q = db.Messages.AsNoTracking().Where(m => m.MessageThreadId == thread.MessageThreadId && m.MessageId > member.JoinedAfterMessageId);
        if (before is long b) q = q.Where(m => m.MessageId < b);
        var page = await q.OrderByDescending(m => m.MessageId).Take(take + 1).ToListAsync(ct);
        var hasMore = page.Count > take;
        page = page.Take(take).OrderBy(m => m.MessageId).ToList();
        var members = await db.MessageThreadMembers.AsNoTracking().Where(m => m.MessageThreadId == thread.MessageThreadId && m.LeftAtUtc == null).ToListAsync(ct);
        var ids = members.Select(m => m.UserId).Concat(page.Select(m => m.SenderId)).Distinct().ToList();
        var names = await NamesAsync(ids, ct);
        var seen = await presence.LoadAsync(ids, ct);
        var now = DateTime.UtcNow;
        var unread = (await UnreadByThreadAsync(me, ct)).GetValueOrDefault(thread.MessageThreadId);
        return new MessageThreadDto(
            Summary(thread, member, members, names, seen, now, me, unread),
            members.OrderBy(m => m.UserId).Select(m => new MessageMemberDto(m.UserId, NameOf(names, m.UserId), EmailOf(names, m.UserId), MessagingRules.IsOnline(seen.GetValueOrDefault(m.UserId), now) && seen.ContainsKey(m.UserId),
                seen.TryGetValue(m.UserId, out var s) ? s : null, m.UserId == thread.CreatedBy)).ToList(),
            page.Select(m => new MessageDto(m.MessageId, m.SenderId, NameOf(names, m.SenderId), m.Body, m.CreatedAtUtc, m.SenderId == me)).ToList(),
            hasMore,
            thread.Kind == MessageThreadKinds.Group && thread.CreatedBy == me);
    }

    public async Task<MessageUnreadDto> UnreadAsync(CancellationToken ct)
    {
        var me = T.RequireUserId();
        var byThread = await UnreadByThreadAsync(me, ct);
        if (byThread.Count == 0) return new MessageUnreadDto(0, 0, null);
        var ids = byThread.Keys.ToList();
        var latest = await (from t in db.MessageThreads.AsNoTracking()
                            where ids.Contains(t.MessageThreadId)
                            join m in db.MessageThreadMembers.AsNoTracking() on t.MessageThreadId equals m.MessageThreadId
                            where m.UserId == me
                            orderby t.LastMessageAtUtc descending, t.MessageThreadId descending
                            select new { t.PublicId, t.LastSenderId, t.LastMessagePreview, t.LastMessageAtUtc, m.IsMuted }).FirstAsync(ct);
        var names = await NamesAsync(latest.LastSenderId is int s ? new List<int> { s } : new List<int>(), ct);
        return new MessageUnreadDto((int)byThread.Values.Sum(), byThread.Count,
            new MessageLatestDto(latest.PublicId, latest.LastSenderId is int sid ? NameOf(names, sid) : string.Empty, latest.LastMessagePreview ?? string.Empty, latest.LastMessageAtUtc, latest.IsMuted));
    }

    // ---------------------------------------------------------------- escribir

    public async Task<MessageThreadDto> CreateThreadAsync(MessageThreadRequest req, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        var body = MessagingRules.NormalizeBody(req.Text) ?? throw new ValidationException("text", MessagingRules.BodyRequiredMessage);
        if (body.Length > MessagingRules.MaxBodyLength) throw new ValidationException("text", MessagingRules.BodyTooLongMessage);
        var title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        if (title is { Length: > MessagingRules.MaxTitleLength }) throw new ValidationException("title", MessagingRules.TitleTooLongMessage);

        var asked = (req.UserIds ?? Array.Empty<int>()).Distinct().ToList();
        var recipients = new HashSet<int>(asked.Where(id => id != me));
        var onlySelf = asked.Count > 0 && recipients.Count == 0 && req.RoleId is null;
        if (recipients.Count > 0)
        {
            var valid = await ActivePeopleAsync(recipients.ToList(), ct);
            if (valid.Count != recipients.Count) throw new ValidationException("userIds", MessagingRules.UserNotFoundMessage);
        }
        string? roleName = null;
        if (req.RoleId is int roleId)
        {
            var role = await db.AppRoles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.TenantId == tenantId && r.IsActive, ct)
                ?? throw new ValidationException("roleId", MessagingRules.RoleNotFoundMessage);
            roleName = role.Name;
            var holders = await db.AppUserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
            foreach (var id in await ActivePeopleAsync(holders.Where(id => id != me).ToList(), ct)) recipients.Add(id);
            if (recipients.Count == 0) throw new ValidationException("roleId", MessagingRules.RoleEmptyMessage);
        }
        if (recipients.Count == 0) throw new ValidationException("userIds", onlySelf ? MessagingRules.CannotWriteSelfMessage : MessagingRules.NeedRecipientsMessage);

        var kind = MessagingRules.KindOf(recipients.Count, req.RoleId is not null);
        var (thread, events) = await db.RunInTransactionAsync(async c =>
        {
            MessageThread t;
            if (kind == MessageThreadKinds.Direct)
            {
                var other = recipients.Single();
                var key = MessagingRules.DirectKey(me, other);
                var existing = await db.MessageThreads.FirstOrDefaultAsync(x => x.DirectKey == key, c);
                if (existing is not null) t = existing;
                else
                {
                    try { t = await CreateThreadRowAsync(MessageThreadKinds.Direct, null, key, new[] { me, other }, me, tenantId, c); }
                    catch (DbUpdateException)
                    {
                        // los dos se escribieron a la vez: el otro ganó la carrera (índice único por pareja); se usa su hilo
                        db.ChangeTracker.Clear();
                        t = await db.MessageThreads.FirstOrDefaultAsync(x => x.DirectKey == key, c) ?? throw new InvalidOperationException("No se pudo crear el hilo.");
                    }
                }
            }
            else
            {
                t = await CreateThreadRowAsync(MessageThreadKinds.Group, title ?? roleName, null, recipients.Prepend(me).ToList(), me, tenantId, c);
            }
            var pushed = await AppendAsync(t, me, body, c);
            return (t, pushed);
        }, ct);
        await PushAsync(tenantId, events, ct);
        return await GetThreadAsync(thread.PublicId, null, MessagingRules.PageSize, ct);
    }

    public async Task<MessageDto> SendAsync(Guid publicId, MessageSendRequest req, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        var body = MessagingRules.NormalizeBody(req.Text) ?? throw new ValidationException("text", MessagingRules.BodyRequiredMessage);
        if (body.Length > MessagingRules.MaxBodyLength) throw new ValidationException("text", MessagingRules.BodyTooLongMessage);
        var (message, events) = await db.RunInTransactionAsync(async c =>
        {
            // todo se carga DENTRO de la transacción (al empezar se limpia el ChangeTracker); si no es parte, 404
            var (thread, _) = await LoadMineAsync(publicId, c, track: true);
            var pushed = await AppendAsync(thread, me, body, c);
            return (pushed.Message, pushed);
        }, ct);
        await PushAsync(tenantId, events, ct);
        var name = (await NamesAsync(new List<int> { me }, ct)).GetValueOrDefault(me).Name;
        return new MessageDto(message.MessageId, me, name ?? string.Empty, message.Body, message.CreatedAtUtc, true);
    }

    public async Task MarkReadAsync(Guid publicId, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        var (thread, member) = await LoadMineAsync(publicId, ct, track: true);
        if (member.LastReadMessageId >= thread.LastMessageId) return;
        member.LastReadMessageId = thread.LastMessageId;
        await db.SaveChangesAsync(ct);
        await live.PushAsync(tenantId, new[] { me }, MessageLiveEvents.ThreadRead, new MessageLiveEvent(tenantId, thread.PublicId, null, null, null, null, false), ct);
    }

    public async Task<MessageThreadDto> AddMembersAsync(Guid publicId, MessageMembersRequest req, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        var (thread, _) = await LoadMineAsync(publicId, ct, track: true);
        if (thread.Kind == MessageThreadKinds.Direct) throw new ConflictException(MessagingRules.DirectNoAddMessage);
        if (thread.CreatedBy != me) throw new ForbiddenException(MessagingRules.OnlyCreatorAddsMessage);
        var ids = (req.UserIds ?? Array.Empty<int>()).Where(id => id != me).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("userIds", MessagingRules.NeedMembersMessage);
        if ((await ActivePeopleAsync(ids, ct)).Count != ids.Count) throw new ValidationException("userIds", MessagingRules.UserNotFoundMessage);
        var existing = await db.MessageThreadMembers.Where(m => m.MessageThreadId == thread.MessageThreadId && ids.Contains(m.UserId)).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var id in ids)
        {
            var row = existing.FirstOrDefault(m => m.UserId == id);
            if (row is null)
                db.MessageThreadMembers.Add(new MessageThreadMember { TenantId = tenantId, MessageThreadId = thread.MessageThreadId, UserId = id, JoinedAtUtc = now, JoinedAfterMessageId = thread.LastMessageId, LastReadMessageId = thread.LastMessageId });
            else if (row.LeftAtUtc is not null)
            {
                row.LeftAtUtc = null;
                row.JoinedAtUtc = now;
                row.JoinedAfterMessageId = thread.LastMessageId;
                row.LastReadMessageId = thread.LastMessageId;
            }
        }
        await db.SaveChangesAsync(ct);
        await PushChangedAsync(thread, tenantId, ct);
        return await GetThreadAsync(publicId, null, MessagingRules.PageSize, ct);
    }

    public async Task LeaveAsync(Guid publicId, CancellationToken ct)
    {
        var tenantId = T.RequireTenantId();
        var (thread, member) = await LoadMineAsync(publicId, ct, track: true);
        if (thread.Kind == MessageThreadKinds.Direct) throw new ConflictException(MessagingRules.DirectNoLeaveMessage);
        member.LeftAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await PushChangedAsync(thread, tenantId, ct);
        await live.PushAsync(tenantId, new[] { member.UserId }, MessageLiveEvents.ThreadChanged, new MessageLiveEvent(tenantId, thread.PublicId, null, null, null, null, false), ct);
    }

    public async Task<MessageThreadSummaryDto> MuteAsync(Guid publicId, MessageMuteRequest req, CancellationToken ct)
    {
        var (_, member) = await LoadMineAsync(publicId, ct, track: true);
        member.IsMuted = req.Muted ?? true;
        await db.SaveChangesAsync(ct);
        return (await GetThreadAsync(publicId, null, 1, ct)).Thread;
    }

    // ---------------------------------------------------------------- internos

    private async Task<MessageThread> CreateThreadRowAsync(string kind, string? title, string? directKey, IReadOnlyCollection<int> userIds, int creator, int tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var thread = new MessageThread { PublicId = Guid.NewGuid(), TenantId = tenantId, Kind = kind, Title = title, DirectKey = directKey, CreatedBy = creator, CreatedAtUtc = now, LastMessageAtUtc = now };
        db.MessageThreads.Add(thread);
        await db.SaveChangesAsync(ct);
        foreach (var id in userIds)
            db.MessageThreadMembers.Add(new MessageThreadMember { TenantId = tenantId, MessageThreadId = thread.MessageThreadId, UserId = id, JoinedAtUtc = now });
        await db.SaveChangesAsync(ct);
        return thread;
    }

    private sealed record Pushed(Message Message, MessageThread Thread, string SenderName, IReadOnlyList<(int UserId, bool Muted)> Others);

    /// <summary>Guarda el mensaje, actualiza el resumen del hilo y deja leído lo propio. Devuelve a quién avisar (después de confirmar).</summary>
    private async Task<Pushed> AppendAsync(MessageThread thread, int sender, string body, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var message = new Message { TenantId = thread.TenantId, MessageThreadId = thread.MessageThreadId, SenderId = sender, Body = body, CreatedAtUtc = now };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        thread.LastMessageId = Math.Max(thread.LastMessageId, message.MessageId);
        thread.LastMessageAtUtc = now;
        thread.LastMessagePreview = MessagingRules.Preview(body);
        thread.LastSenderId = sender;
        var members = await db.MessageThreadMembers.Where(m => m.MessageThreadId == thread.MessageThreadId && m.LeftAtUtc == null).ToListAsync(ct);
        var mine = members.FirstOrDefault(m => m.UserId == sender);
        if (mine is not null) mine.LastReadMessageId = Math.Max(mine.LastReadMessageId, message.MessageId);
        await db.SaveChangesAsync(ct);
        var name = (await NamesAsync(new List<int> { sender }, ct)).GetValueOrDefault(sender).Name ?? string.Empty;
        return new Pushed(message, thread, name, members.Where(m => m.UserId != sender).Select(m => (m.UserId, m.IsMuted)).ToList());
    }

    private async Task PushAsync(int tenantId, Pushed p, CancellationToken ct)
    {
        var preview = p.Thread.LastMessagePreview;
        foreach (var muted in new[] { false, true })
        {
            var ids = p.Others.Where(o => o.Muted == muted).Select(o => o.UserId).ToList();
            if (ids.Count > 0)
                await live.PushAsync(tenantId, ids, MessageLiveEvents.MessageReceived,
                    new MessageLiveEvent(tenantId, p.Thread.PublicId, p.Message.MessageId, p.Message.SenderId, p.SenderName, preview, muted), ct);
        }
        // las otras ventanas de quien escribió se enteran de que el hilo cambió
        await live.PushAsync(tenantId, new[] { p.Message.SenderId }, MessageLiveEvents.ThreadChanged, new MessageLiveEvent(tenantId, p.Thread.PublicId, p.Message.MessageId, p.Message.SenderId, p.SenderName, preview, false), ct);
    }

    private async Task PushChangedAsync(MessageThread thread, int tenantId, CancellationToken ct)
    {
        var ids = await db.MessageThreadMembers.AsNoTracking().Where(m => m.MessageThreadId == thread.MessageThreadId && m.LeftAtUtc == null).Select(m => m.UserId).ToListAsync(ct);
        await live.PushAsync(tenantId, ids, MessageLiveEvents.ThreadChanged, new MessageLiveEvent(tenantId, thread.PublicId, null, null, null, null, false), ct);
    }

    private async Task<(MessageThread Thread, MessageThreadMember Member)> LoadMineAsync(Guid publicId, CancellationToken ct, bool track = false)
    {
        var me = T.RequireUserId();
        var threads = track ? db.MessageThreads : db.MessageThreads.AsNoTracking();
        var thread = await threads.FirstOrDefaultAsync(t => t.PublicId == publicId, ct) ?? throw new NotFoundException("Conversación", null, feminine: true);
        var members = track ? db.MessageThreadMembers : db.MessageThreadMembers.AsNoTracking();
        var member = await members.FirstOrDefaultAsync(m => m.MessageThreadId == thread.MessageThreadId && m.UserId == me && m.LeftAtUtc == null, ct)
            ?? throw new NotFoundException("Conversación", null, feminine: true);   // no es parte: no se revela que existe
        return (thread, member);
    }

    /// <summary>De esos usuarios, los que son de esta compañía y están activos.</summary>
    private async Task<List<int>> ActivePeopleAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new List<int>();
        var inTenant = db.UserTenants.AsNoTracking().Where(m => ids.Contains(m.UserId)).Select(m => m.UserId);
        return await db.Users.AsNoTracking().Where(u => u.IsActive && inTenant.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
    }

    private async Task<Dictionary<long, long>> UnreadByThreadAsync(int me, CancellationToken ct)
    {
        var rows = await (from msg in db.Messages.AsNoTracking()
                          join m in db.MessageThreadMembers.AsNoTracking() on msg.MessageThreadId equals m.MessageThreadId
                          where m.UserId == me && m.LeftAtUtc == null && msg.SenderId != me && msg.MessageId > m.LastReadMessageId && msg.MessageId > m.JoinedAfterMessageId
                          group msg by msg.MessageThreadId into g
                          select new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (long)r.N);
    }

    private async Task<Dictionary<int, (string? Name, string? Email)>> NamesAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
        => ids.Count == 0
            ? new Dictionary<int, (string?, string?)>()
            : (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
                .ToDictionary(u => u.Id, u => ((string?)(string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName), u.Email));

    private static string NameOf(Dictionary<int, (string? Name, string? Email)> names, int id) => names.TryGetValue(id, out var n) ? n.Name ?? string.Empty : string.Empty;
    private static string EmailOf(Dictionary<int, (string? Name, string? Email)> names, int id) => names.TryGetValue(id, out var n) ? n.Email ?? string.Empty : string.Empty;

    private static MessagePersonDto Person(int id, string? fullName, string? email, Dictionary<int, DateTime> seen, DateTime now)
        => new(id, string.IsNullOrWhiteSpace(fullName) ? email ?? $"#{id}" : fullName, email ?? string.Empty, seen.TryGetValue(id, out var s) && MessagingRules.IsOnline(s, now), seen.TryGetValue(id, out var ls) ? ls : null);

    private MessageThreadSummaryDto Summary(MessageThread t, MessageThreadMember mine, IReadOnlyList<MessageThreadMember> members, Dictionary<int, (string? Name, string? Email)> names,
        Dictionary<int, DateTime> seen, DateTime now, int me, long unread)
    {
        var others = members.Where(m => m.UserId != me).OrderBy(m => NameOf(names, m.UserId), StringComparer.CurrentCultureIgnoreCase).ToList();
        MessagePersonDto? other = null;
        string name;
        if (t.Kind == MessageThreadKinds.Direct)
        {
            var o = others.FirstOrDefault();
            other = o is null ? null : Person(o.UserId, NameOf(names, o.UserId), EmailOf(names, o.UserId), seen, now);
            name = other?.Name ?? "—";
        }
        else name = MessagingRules.GroupName(t.Title, others.Select(m => NameOf(names, m.UserId)).ToList());
        return new MessageThreadSummaryDto(t.PublicId, t.Kind, name, members.Count, other, t.LastMessagePreview,
            t.LastSenderId is int s ? NameOf(names, s) : null, t.LastMessageAtUtc, (int)unread, mine.IsMuted);
    }
}
