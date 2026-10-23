using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
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
public sealed class MessagingService(TeikemDbContext db, ITenantContext tenant, PresenceService presence, IMessageLiveNotifier live, PermissionService? permissions = null, IFileStore? files = null, DriverPushService? driverPush = null)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- visibilidad (Lote 212)

    /// <summary>Cómo participa quien pregunta en la mensajería de esta compañía (FULL si no hay fila de membresía, p. ej. en pruebas).</summary>
    private async Task<string> MyModeAsync(CancellationToken ct)
    {
        var me = T.RequireUserId();
        return await db.UserTenants.AsNoTracking().Where(m => m.UserId == me).Select(m => m.MessagingMode).FirstOrDefaultAsync(ct) ?? MessagingModes.Full;
    }

    private async Task EnsureCanWriteAsync(CancellationToken ct)
    {
        if (await MyModeAsync(ct) == MessagingModes.None) throw new ForbiddenException(MessagingRules.NoMessagingMessage);
    }

    /// <summary>Los usuarios de la compañía que quien pregunta puede ver, según su modo y el de cada quien.</summary>
    private IQueryable<int> VisibleUserIds(string myMode)
    {
        var members = db.UserTenants.AsNoTracking();
        if (myMode == MessagingModes.None) return members.Where(_ => false).Select(m => m.UserId);
        return (myMode == MessagingModes.Full ? members.Where(m => m.MessagingMode != MessagingModes.None) : members.Where(m => m.MessagingMode == MessagingModes.Full)).Select(m => m.UserId);
    }

    /// <summary>La mensajería de cada persona de la compañía (para el administrador de usuarios).</summary>
    public async Task<IReadOnlyList<MessagingModeDto>> ListModesAsync(CancellationToken ct)
    {
        var rows = await (from m in db.UserTenants.AsNoTracking()
                          join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                          where u.IsActive
                          orderby u.FullName ?? u.Email
                          select new { u.Id, u.FullName, u.Email, m.MessagingMode }).ToListAsync(ct);
        return rows.Select(r => new MessagingModeDto(r.Id, string.IsNullOrWhiteSpace(r.FullName) ? r.Email ?? $"#{r.Id}" : r.FullName, r.Email ?? string.Empty, r.MessagingMode)).ToList();
    }

    public async Task<MessagingModeDto> SetModeAsync(int userId, MessagingModeRequest req, CancellationToken ct)
    {
        var mode = MessagingModes.Normalize(req.Mode) ?? throw new ValidationException("mode", MessagingModes.InvalidMessage);
        var row = await db.UserTenants.FirstOrDefaultAsync(m => m.UserId == userId, ct) ?? throw new NotFoundException("Usuario", null);
        row.MessagingMode = mode;
        await db.SaveChangesAsync(ct);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FullName, u.Email }).FirstAsync(ct);
        return new MessagingModeDto(userId, string.IsNullOrWhiteSpace(user.FullName) ? user.Email ?? $"#{userId}" : user.FullName, user.Email ?? string.Empty, mode);
    }

    // ---------------------------------------------------------------- directorio

    public async Task<IReadOnlyList<MessagePersonDto>> PeopleAsync(string? q, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 20 : take, 1, 50);
        var me = T.RequireUserId();
        var ids = VisibleUserIds(await MyModeAsync(ct));
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
        if (thread.FollowsRoleId is not null)
        {
            await db.RunInTransactionAsync(async c => { var tracked = await db.MessageThreads.FirstAsync(t => t.MessageThreadId == thread.MessageThreadId, c); await SyncFollowedRoleAsync(tracked, c); }, ct);
            (thread, member) = await LoadMineAsync(publicId, ct);   // por si esta persona ya no tiene el rol: el grupo «no existe» para ella
        }
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
        var cards = await ResolveRefsAsync(page, ct);
        var photos = await AttachmentsOfAsync(page.Select(m => m.MessageId).ToList(), ct);
        return new MessageThreadDto(
            Summary(thread, member, members, names, seen, now, me, unread),
            members.OrderBy(m => m.UserId).Select(m => new MessageMemberDto(m.UserId, NameOf(names, m.UserId), EmailOf(names, m.UserId), MessagingRules.IsOnline(seen.GetValueOrDefault(m.UserId), now) && seen.ContainsKey(m.UserId),
                seen.TryGetValue(m.UserId, out var s) ? s : null, m.UserId == thread.CreatedBy)).ToList(),
            page.Select(m => new MessageDto(m.MessageId, m.SenderId, NameOf(names, m.SenderId), m.Body, m.CreatedAtUtc, m.SenderId == me, cards.GetValueOrDefault(m.MessageId), photos.GetValueOrDefault(m.MessageId))).ToList(),
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
        await EnsureCanWriteAsync(ct);
        var photos = await PrepareAttachmentsAsync(req.Attachments, tenantId, ct);
        var body = MessagingRules.NormalizeBody(req.Text) ?? (photos.Count > 0 ? string.Empty : throw new ValidationException("text", MessagingRules.BodyRequiredMessage));
        if (body.Length > MessagingRules.MaxBodyLength) throw new ValidationException("text", MessagingRules.BodyTooLongMessage);
        var mref = await ValidateRefAsync(req.Ref, ct);
        if (req.FollowRole == true && req.RoleId is null) throw new ValidationException("roleId", MessagingRules.FollowRoleNeedsRoleMessage);
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
        var roleHolders = new HashSet<int>();
        if (req.RoleId is int roleId)
        {
            var role = await db.AppRoles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == roleId && r.TenantId == tenantId && r.IsActive, ct)
                ?? throw new ValidationException("roleId", MessagingRules.RoleNotFoundMessage);
            roleName = role.Name;
            var holders = await db.AppUserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId).Distinct().ToListAsync(ct);
            foreach (var id in await ActivePeopleAsync(holders.Where(id => id != me).ToList(), ct)) { recipients.Add(id); roleHolders.Add(id); }
            if (recipients.Count == 0) throw new ValidationException("roleId", MessagingRules.RoleEmptyMessage);
        }
        if (recipients.Count == 0) throw new ValidationException("userIds", onlySelf ? MessagingRules.CannotWriteSelfMessage : MessagingRules.NeedRecipientsMessage);

        var kind = MessagingRules.KindOf(recipients.Count, req.RoleId is not null);
        var followRole = req.FollowRole == true && req.RoleId is int;
        var (thread, events) = await db.RunInTransactionAsync(async c =>
        {
            MessageThread t;
            if (followRole)
            {
                // el grupo «vivo» de un rol es uno solo por compañía: se reusa si ya existe
                var roleId = req.RoleId!.Value;
                var existing = await db.MessageThreads.FirstOrDefaultAsync(x => x.FollowsRoleId == roleId, c);
                if (existing is not null) t = existing;
                else
                {
                    try { t = await CreateThreadRowAsync(MessageThreadKinds.Group, title ?? roleName, null, recipients.Prepend(me).ToList(), me, tenantId, c, followsRoleId: roleId, viaRole: roleHolders); }
                    catch (DbUpdateException)
                    {
                        db.ChangeTracker.Clear();
                        t = await db.MessageThreads.FirstOrDefaultAsync(x => x.FollowsRoleId == roleId, c) ?? throw new InvalidOperationException("No se pudo crear el grupo.");
                    }
                }
                if (!await db.MessageThreadMembers.AnyAsync(m => m.MessageThreadId == t.MessageThreadId && m.UserId == me && m.LeftAtUtc == null, c))
                    await JoinAsync(t, me, tenantId, c);
                await SyncFollowedRoleAsync(t, c);
            }
            else if (kind == MessageThreadKinds.Direct)
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
            var pushed = await AppendAsync(t, me, body, mref, photos, c);
            return (t, pushed);
        }, ct);
        await PushAsync(tenantId, events, ct);
        return await GetThreadAsync(thread.PublicId, null, MessagingRules.PageSize, ct);
    }

    public async Task<MessageDto> SendAsync(Guid publicId, MessageSendRequest req, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var tenantId = T.RequireTenantId();
        await EnsureCanWriteAsync(ct);
        var photos = await PrepareAttachmentsAsync(req.Attachments, tenantId, ct);
        var body = MessagingRules.NormalizeBody(req.Text) ?? (photos.Count > 0 ? string.Empty : throw new ValidationException("text", MessagingRules.BodyRequiredMessage));
        if (body.Length > MessagingRules.MaxBodyLength) throw new ValidationException("text", MessagingRules.BodyTooLongMessage);
        var mref = await ValidateRefAsync(req.Ref, ct);
        var (message, events) = await db.RunInTransactionAsync(async c =>
        {
            // todo se carga DENTRO de la transacción (al empezar se limpia el ChangeTracker); si no es parte, 404
            var (thread, _) = await LoadMineAsync(publicId, c, track: true);
            await SyncFollowedRoleAsync(thread, c);
            var pushed = await AppendAsync(thread, me, body, mref, photos, c);
            return (pushed.Message, pushed);
        }, ct);
        await PushAsync(tenantId, events, ct);
        var name = (await NamesAsync(new List<int> { me }, ct)).GetValueOrDefault(me).Name;
        var cards = await ResolveRefsAsync(new[] { message }, ct);
        var atts = await AttachmentsOfAsync(new[] { message.MessageId }, ct);
        return new MessageDto(message.MessageId, me, name ?? string.Empty, message.Body, message.CreatedAtUtc, true, cards.GetValueOrDefault(message.MessageId), atts.GetValueOrDefault(message.MessageId));
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
        await EnsureCanWriteAsync(ct);
        var (thread, _) = await LoadMineAsync(publicId, ct, track: true);
        if (thread.Kind == MessageThreadKinds.Direct) throw new ConflictException(MessagingRules.DirectNoAddMessage);
        if (thread.FollowsRoleId is not null) throw new ConflictException(MessagingRules.RoleGroupNoAddMessage);
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

    // ---------------------------------------------------------------- fotos del chat y grupos de rol (Lote 212, B)

    private sealed record PreparedPhoto(string ContentType, long SizeBytes, string Sha256, string StorageKey, string? ThumbStorageKey);

    /// <summary>Valida y guarda en el almacén las fotos de un mensaje (hasta 4; JPG/PNG/WEBP; 3 MB cada una) con su miniatura. Se hace ANTES de la transacción del mensaje.</summary>
    private async Task<IReadOnlyList<PreparedPhoto>> PrepareAttachmentsAsync(IReadOnlyList<MessageAttachmentUpload>? uploads, int tenantId, CancellationToken ct)
    {
        if (uploads is null || uploads.Count == 0) return Array.Empty<PreparedPhoto>();
        if (uploads.Count > MessagingRules.MaxAttachmentsPerMessage) throw new ValidationException("attachments", MessagingRules.TooManyAttachmentsMessage);
        if (files is null || !files.IsConfigured) throw new ValidationException("attachments", Teikem.Domain.Files.StoredFileRules.NotConfigured);
        var result = new List<PreparedPhoto>();
        foreach (var up in uploads)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String((up.DataBase64 ?? string.Empty).Trim()); }
            catch (FormatException) { throw new ValidationException("attachments", MessagingRules.AttachmentInvalidMessage); }
            if (bytes.Length == 0) throw new ValidationException("attachments", MessagingRules.AttachmentInvalidMessage);
            if (bytes.Length > MessagingRules.MaxAttachmentMegabytes * 1024 * 1024) throw new ValidationException("attachments", MessagingRules.AttachmentTooLargeMessage);
            var type = Teikem.Domain.Files.StoredFileRules.SniffContentType(bytes);
            if (type is not ("image/jpeg" or "image/png" or "image/webp")) throw new ValidationException("attachments", MessagingRules.AttachmentTypeMessage);
            var key = await files.SaveAsync(tenantId, Guid.NewGuid(), bytes, ct);
            string? thumbKey = null;
            var thumb = ImageThumbnail.TryCreate(bytes, MessagingRules.ThumbSide);
            if (thumb is not null) thumbKey = await files.SaveAsync(tenantId, Guid.NewGuid(), thumb, ct);
            result.Add(new PreparedPhoto(type, bytes.Length, Teikem.Domain.Files.StoredFileRules.Sha256Hex(bytes), key, thumbKey));
        }
        return result;
    }

    private async Task<Dictionary<long, IReadOnlyList<MessageAttachmentDto>>> AttachmentsOfAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct)
    {
        if (messageIds.Count == 0) return new Dictionary<long, IReadOnlyList<MessageAttachmentDto>>();
        var rows = await db.MessageAttachments.AsNoTracking().Where(a => messageIds.Contains(a.MessageId)).OrderBy(a => a.MessageAttachmentId).ToListAsync(ct);
        return rows.GroupBy(a => a.MessageId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<MessageAttachmentDto>)g.Select(a => new MessageAttachmentDto(a.PublicId, a.ContentType, a.SizeBytes, a.State, a.ThumbStorageKey != null)).ToList());
    }

    /// <summary>La foto (o su miniatura) de un mensaje de MIS hilos. 404 si no soy parte; 409 si ya se borró o archivó.</summary>
    public async Task<(byte[] Content, string ContentType)> GetAttachmentAsync(Guid publicId, bool thumb, CancellationToken ct)
    {
        var me = T.RequireUserId();
        var att = await db.MessageAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.PublicId == publicId, ct) ?? throw new NotFoundException("Foto", null, feminine: true);
        var msg = await db.Messages.AsNoTracking().FirstAsync(m => m.MessageId == att.MessageId, ct);
        var member = await db.MessageThreadMembers.AsNoTracking().FirstOrDefaultAsync(m => m.MessageThreadId == msg.MessageThreadId && m.UserId == me && m.LeftAtUtc == null, ct);
        if (member is null || msg.MessageId <= member.JoinedAfterMessageId) throw new NotFoundException("Foto", null, feminine: true);   // no es parte: no se revela que existe
        if (att.State != MessageAttachmentStates.Active || files is null) throw new ConflictException(MessagingRules.AttachmentGoneMessage(att.State, att.RemovedAtUtc));
        var key = thumb && att.ThumbStorageKey is not null ? att.ThumbStorageKey : att.StorageKey ?? throw new ConflictException(MessagingRules.AttachmentGoneMessage(MessageAttachmentStates.Deleted, att.RemovedAtUtc));
        var bytes = await files.ReadAsync(T.RequireTenantId(), key, ct);
        return (bytes, thumb && att.ThumbStorageKey is not null ? "image/jpeg" : att.ContentType);
    }

    private async Task JoinAsync(MessageThread thread, int userId, int tenantId, CancellationToken ct)
    {
        var row = await db.MessageThreadMembers.FirstOrDefaultAsync(m => m.MessageThreadId == thread.MessageThreadId && m.UserId == userId, ct);
        var now = DateTime.UtcNow;
        if (row is null)
            db.MessageThreadMembers.Add(new MessageThreadMember { TenantId = tenantId, MessageThreadId = thread.MessageThreadId, UserId = userId, JoinedAtUtc = now, JoinedAfterMessageId = thread.LastMessageId, LastReadMessageId = thread.LastMessageId });
        else if (row.LeftAtUtc is not null)
        {
            row.LeftAtUtc = null; row.JoinedAtUtc = now; row.JoinedAfterMessageId = thread.LastMessageId; row.LastReadMessageId = thread.LastMessageId;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Grupo «vivo» de un rol: entran quienes tienen hoy el rol (personas activas con mensajería) y salen quienes entraron por el rol y lo perdieron; quien creó el grupo, o entró al escribirle (un chofer), se queda.
    /// Quien entra ve el historial solo desde que entra. Si no es un grupo de rol, no hace nada.
    /// </summary>
    private async Task SyncFollowedRoleAsync(MessageThread thread, CancellationToken ct)
    {
        if (thread.FollowsRoleId is not int roleId) return;
        var tenantId = thread.TenantId;
        var holders = await (from ur in db.AppUserRoles.AsNoTracking()
                             where ur.RoleId == roleId
                             join u in db.Users.AsNoTracking() on ur.UserId equals u.Id
                             where u.IsActive
                             join m in db.UserTenants.AsNoTracking() on u.Id equals m.UserId
                             where m.MessagingMode != MessagingModes.None
                             select u.Id).Distinct().ToListAsync(ct);
        var members = await db.MessageThreadMembers.Where(m => m.MessageThreadId == thread.MessageThreadId).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var id in holders)
        {
            var row = members.FirstOrDefault(m => m.UserId == id);
            if (row is null) { db.MessageThreadMembers.Add(new MessageThreadMember { TenantId = tenantId, MessageThreadId = thread.MessageThreadId, UserId = id, JoinedAtUtc = now, JoinedAfterMessageId = thread.LastMessageId, LastReadMessageId = thread.LastMessageId, ViaRole = id != thread.CreatedBy }); changed = true; }
            else if (row.LeftAtUtc is not null) { row.LeftAtUtc = null; row.JoinedAtUtc = now; row.JoinedAfterMessageId = thread.LastMessageId; row.LastReadMessageId = thread.LastMessageId; row.ViaRole = id != thread.CreatedBy; changed = true; }
        }
        // salen quienes entraron POR el rol y ya no lo tienen; quien entró al escribirle al grupo (o lo creó) se queda
        foreach (var row in members.Where(m => m.LeftAtUtc is null && m.ViaRole && !holders.Contains(m.UserId))) { row.LeftAtUtc = now; changed = true; }
        if (changed) await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- referencias a orden o ruta (Lote 212)

    private sealed record MessageRefResolved(string Entity, Guid PublicId, string Title);

    /// <summary>La referencia pedida debe ser una orden o ruta de ESTA compañía (el filtro de tenant lo garantiza); null si no hay referencia.</summary>
    private async Task<MessageRefResolved?> ValidateRefAsync(MessageRefRequest? r, CancellationToken ct)
    {
        if (r is null || (r.Entity is null && r.PublicId is null)) return null;
        var entity = MessageRefs.Normalize(r.Entity) ?? throw new ValidationException("ref", MessageRefs.InvalidMessage);
        var id = r.PublicId ?? throw new ValidationException("ref", MessageRefs.InvalidMessage);
        var title = entity == MessageRefs.Order
            ? await db.TransportOrders.AsNoTracking().Where(o => o.PublicId == id).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct)
            : await db.Trips.AsNoTracking().Where(t => t.PublicId == id).Select(t => t.Code).FirstOrDefaultAsync(ct);
        return title is null ? throw new ValidationException("ref", MessageRefs.NotFoundMessage) : new MessageRefResolved(entity, id, title);
    }

    /// <summary>La tarjeta de cada mensaje con referencia. El detalle (cliente/zona, estatus) solo se da a quien tiene permiso para ver la orden o la ruta.</summary>
    private async Task<Dictionary<long, MessageRefDto>> ResolveRefsAsync(IEnumerable<Message> messages, CancellationToken ct)
    {
        var withRef = messages.Where(m => m.RefEntity != null && m.RefPublicId != null).ToList();
        var result = new Dictionary<long, MessageRefDto>();
        if (withRef.Count == 0) return result;
        var lang = T.Lang;

        var orderIds = withRef.Where(m => m.RefEntity == MessageRefs.Order).Select(m => m.RefPublicId!.Value).Distinct().ToList();
        if (orderIds.Count > 0)
        {
            var canOpen = permissions is not null && await permissions.HasPermissionAsync(Teikem.Domain.Constants.PermissionCatalog.OrdersView, ct);
            var rows = await (from o in db.TransportOrders.AsNoTracking()
                              where orderIds.Contains(o.PublicId)
                              join c in db.Clients.AsNoTracking() on o.ClientId equals c.ClientId
                              join s in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals s.StatusCodeId
                              select new { o.PublicId, o.OrderNumber, ClientName = c.Name, s.LabelJson }).ToListAsync(ct);
            foreach (var m in withRef.Where(m => m.RefEntity == MessageRefs.Order))
            {
                var row = rows.FirstOrDefault(x => x.PublicId == m.RefPublicId);
                result[m.MessageId] = row is null
                    ? new MessageRefDto(MessageRefs.Order, m.RefPublicId!.Value, "—", null, null, false)
                    : new MessageRefDto(MessageRefs.Order, row.PublicId, row.OrderNumber, canOpen ? row.ClientName : null, canOpen ? Teikem.Domain.Common.MultilingualText.Resolve(row.LabelJson, lang) : null, canOpen);
            }
        }
        var tripIds = withRef.Where(m => m.RefEntity == MessageRefs.Trip).Select(m => m.RefPublicId!.Value).Distinct().ToList();
        if (tripIds.Count > 0)
        {
            var canOpen = permissions is not null && await permissions.HasPermissionAsync(Teikem.Domain.Constants.PermissionCatalog.TripsView, ct);
            var rows = await (from t in db.Trips.AsNoTracking()
                              where tripIds.Contains(t.PublicId)
                              join s in db.StatusCodes.AsNoTracking() on t.StatusCodeId equals s.StatusCodeId
                              join z in db.DispatchZones.AsNoTracking() on t.DispatchZoneId equals (int?)z.DispatchZoneId into zs
                              from z in zs.DefaultIfEmpty()
                              select new { t.PublicId, t.Code, Zone = z != null ? z.Code : null, s.LabelJson }).ToListAsync(ct);
            foreach (var m in withRef.Where(m => m.RefEntity == MessageRefs.Trip))
            {
                var row = rows.FirstOrDefault(x => x.PublicId == m.RefPublicId);
                result[m.MessageId] = row is null
                    ? new MessageRefDto(MessageRefs.Trip, m.RefPublicId!.Value, "—", null, null, false)
                    : new MessageRefDto(MessageRefs.Trip, row.PublicId, row.Code, canOpen ? row.Zone : null, canOpen ? Teikem.Domain.Common.MultilingualText.Resolve(row.LabelJson, lang) : null, canOpen);
            }
        }
        return result;
    }

    /// <summary>Los mensajes de MIS hilos que hablan de esa orden o ruta (los más nuevos primero): «los mensajes de esta orden».</summary>
    public async Task<IReadOnlyList<MessageAboutDto>> AboutAsync(string? entity, Guid publicId, CancellationToken ct)
    {
        var e = MessageRefs.Normalize(entity) ?? throw new ValidationException("entity", MessageRefs.InvalidMessage);
        var me = T.RequireUserId();
        var rows = await (from msg in db.Messages.AsNoTracking()
                          where msg.RefEntity == e && msg.RefPublicId == publicId
                          join m in db.MessageThreadMembers.AsNoTracking() on msg.MessageThreadId equals m.MessageThreadId
                          where m.UserId == me && m.LeftAtUtc == null && msg.MessageId > m.JoinedAfterMessageId
                          join t in db.MessageThreads.AsNoTracking() on msg.MessageThreadId equals t.MessageThreadId
                          orderby msg.MessageId descending
                          select new { t.PublicId, msg.MessageId, msg.SenderId, msg.Body, msg.CreatedAtUtc }).Take(50).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.SenderId).Distinct().ToList(), ct);
        return rows.Select(r => new MessageAboutDto(r.PublicId, r.MessageId, NameOf(names, r.SenderId), MessagingRules.Preview(r.Body), r.CreatedAtUtc)).ToList();
    }

    // ---------------------------------------------------------------- internos

    private async Task<MessageThread> CreateThreadRowAsync(string kind, string? title, string? directKey, IReadOnlyCollection<int> userIds, int creator, int tenantId, CancellationToken ct, int? followsRoleId = null, IReadOnlySet<int>? viaRole = null)
    {
        var now = DateTime.UtcNow;
        var thread = new MessageThread { PublicId = Guid.NewGuid(), TenantId = tenantId, Kind = kind, Title = title, DirectKey = directKey, CreatedBy = creator, CreatedAtUtc = now, LastMessageAtUtc = now, FollowsRoleId = followsRoleId };
        db.MessageThreads.Add(thread);
        await db.SaveChangesAsync(ct);
        foreach (var id in userIds)
            db.MessageThreadMembers.Add(new MessageThreadMember { TenantId = tenantId, MessageThreadId = thread.MessageThreadId, UserId = id, JoinedAtUtc = now, ViaRole = viaRole?.Contains(id) == true && id != creator });
        await db.SaveChangesAsync(ct);
        return thread;
    }

    private sealed record Pushed(Message Message, MessageThread Thread, string SenderName, IReadOnlyList<(int UserId, bool Muted)> Others);

    /// <summary>Guarda el mensaje, actualiza el resumen del hilo y deja leído lo propio. Devuelve a quién avisar (después de confirmar).</summary>
    private async Task<Pushed> AppendAsync(MessageThread thread, int sender, string body, MessageRefResolved? mref, IReadOnlyList<PreparedPhoto> photos, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var message = new Message { TenantId = thread.TenantId, MessageThreadId = thread.MessageThreadId, SenderId = sender, Body = body, CreatedAtUtc = now, RefEntity = mref?.Entity, RefPublicId = mref?.PublicId };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);
        foreach (var ph in photos)
            db.MessageAttachments.Add(new MessageAttachment { TenantId = thread.TenantId, MessageId = message.MessageId, ContentType = ph.ContentType, SizeBytes = ph.SizeBytes, Sha256 = ph.Sha256, StorageKey = ph.StorageKey, ThumbStorageKey = ph.ThumbStorageKey, CreatedAtUtc = now });
        thread.LastMessageId = Math.Max(thread.LastMessageId, message.MessageId);
        thread.LastMessageAtUtc = now;
        thread.LastMessagePreview = MessagingRules.PreviewWithRef(mref?.Title, body.Length == 0 ? MessagingRules.AttachmentPreview(photos.Count) : body);
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
        // aviso emergente al teléfono de los choferes (los que no silenciaron el hilo); nunca hace fallar el mensaje, que ya quedó guardado
        if (driverPush is not null)
        {
            var ids = p.Others.Where(o => !o.Muted).Select(o => o.UserId).ToList();
            if (ids.Count > 0)
            {
                try
                {
                    var group = p.Thread.Kind == MessageThreadKinds.Group;
                    var title = group && !string.IsNullOrWhiteSpace(p.Thread.Title) ? p.Thread.Title! : p.SenderName;
                    var bodyText = group && string.IsNullOrWhiteSpace(p.Thread.Title) ? preview : group ? $"{p.SenderName}: {preview}" : preview;
                    await driverPush.SendToUsersAsync(ids, title, bodyText, new Dictionary<string, string> { ["kind"] = "MESSAGE", ["thread"] = p.Thread.PublicId.ToString(), ["tenant"] = tenantId.ToString() }, ct);
                }
                catch (Exception)
                {
                    // el aviso emergente es un extra: si falla, el mensaje igual llegó y el chofer lo ve al abrir la app
                }
            }
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
        var visible = VisibleUserIds(await MyModeAsync(ct));
        return await db.Users.AsNoTracking().Where(u => u.IsActive && ids.Contains(u.Id) && visible.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
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
