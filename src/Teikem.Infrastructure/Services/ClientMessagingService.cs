using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>429: la persona mandó demasiados mensajes seguidos.</summary>
public sealed class TooManyMessagesException() : TeikemException(ClientMessageRules.TooManyMessage, 429, "rate_limited");

/// <summary>
/// Lote 104 — mensajes del portal con el equipo de servicio al cliente. <b>Lado del portal</b>: cada persona del cliente tiene SU conversación con el equipo (la crea el primer mensaje); solo ve y toca la suya
/// (la conversación sale del usuario y del cliente de la SESIÓN, nunca del request) y no ve lo de nadie más de su empresa. <b>Lado del equipo</b>: ve las conversaciones de los clientes donde está designado (o de todos los
/// clientes sin equipo designado, si tiene el permiso de atender mensajes; quien tiene el permiso de administrar las ve todas), contesta, y puede abrir una conversación escribiéndole primero a una persona del cliente.
/// Los mensajes nunca se editan ni se borran. Tope de mensajes por hora por persona del cliente.
/// </summary>
public sealed class ClientMessagingService(TeikemDbContext db, ITenantContext tenant, PermissionService permissions)
{
    private TenantContext T => (TenantContext)tenant;
    public const int PageSize = 30;

    // ================================================================ lado del portal

    private int PortalClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    private async Task<ClientConversation?> MineAsync(CancellationToken ct)
    {
        var cid = PortalClientId;
        var me = T.RequireUserId();
        return await db.ClientConversations.FirstOrDefaultAsync(c => c.ClientId == cid && c.PortalUserId == me, ct);
    }

    /// <summary>Mi conversación: los últimos mensajes (o los anteriores a <paramref name="before"/>), en orden cronológico, y cuántos del equipo no he leído.</summary>
    public async Task<PortalMessagesDto> GetMineAsync(long? before, int take, CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        if (conv is null) return new PortalMessagesDto(null, Array.Empty<ClientMessageDto>(), false, 0);
        var (messages, hasMore) = await PageAsync(conv.ClientConversationId, before, take, T.RequireUserId(), ct);
        return new PortalMessagesDto(conv.PublicId, messages, hasMore, await UnreadForClientAsync(conv, ct));
    }

    public async Task<PortalUnreadDto> UnreadMineAsync(CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        return new PortalUnreadDto(conv is null ? 0 : await UnreadForClientAsync(conv, ct));
    }

    private Task<int> UnreadForClientAsync(ClientConversation c, CancellationToken ct)
        => db.ClientMessages.AsNoTracking().CountAsync(m => m.ClientConversationId == c.ClientConversationId && m.SenderKind == ClientMessageSenders.Staff && m.ClientMessageId > c.ClientLastReadMessageId, ct);

    /// <summary>La persona escribe al equipo (400 si está vacío o pasa de 2000 caracteres; 429 si manda demasiados seguidos). Crea la conversación con el primer mensaje.</summary>
    public async Task<ClientMessageDto> SendAsPortalAsync(ClientMessageSendRequest req, CancellationToken ct)
    {
        var cid = PortalClientId;
        var me = T.RequireUserId();
        var tid = T.RequireTenantId();
        var body = ValidBody(req.Body);
        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.ClientMessages.AsNoTracking().CountAsync(m => m.SenderUserId == me && m.SenderKind == ClientMessageSenders.Client && m.CreatedAtUtc > since, ct);
        if (recent >= ClientMessageRules.MaxPerHour) throw new TooManyMessagesException();

        var message = await db.RunInTransactionAsync(async ct2 =>
        {
            var conv = await db.ClientConversations.FirstOrDefaultAsync(c => c.ClientId == cid && c.PortalUserId == me, ct2);
            if (conv is null)
            {
                conv = new ClientConversation { PublicId = Guid.NewGuid(), TenantId = tid, ClientId = cid, PortalUserId = me, StartedBy = ClientMessageSenders.Client };
                db.ClientConversations.Add(conv);
                await db.SaveChangesAsync(ct2);
            }
            var m = await AppendAsync(conv, ClientMessageSenders.Client, me, body, ct2);
            conv.ClientLastReadMessageId = m.ClientMessageId;   // lo que escribo ya lo leí
            await db.SaveChangesAsync(ct2);
            return m;
        }, ct);
        return ToDto(message, await NameOfAsync(me, ct), me);
    }

    /// <summary>Marca como leído lo que el equipo escribió.</summary>
    public async Task MarkReadAsPortalAsync(CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        if (conv is null || conv.ClientLastReadMessageId >= conv.LastMessageId) return;
        conv.ClientLastReadMessageId = conv.LastMessageId;
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ lado del equipo

    private async Task<bool> SeesAllAsync(CancellationToken ct) => T.IsPlatformAdmin || await permissions.HasPermissionAsync(PermissionCatalog.ClientMessagesManage, ct);

    /// <summary>Las conversaciones que veo: de los clientes donde estoy designado, de los que no tienen equipo (atiende quien tiene el permiso) o todas si administro.</summary>
    private async Task<IQueryable<ClientConversation>> VisibleAsync(CancellationToken ct)
    {
        var me = T.RequireUserId();
        if (await SeesAllAsync(ct)) return db.ClientConversations.AsNoTracking();
        var withTeam = db.ClientServiceTeamMembers.Select(m => m.ClientId);
        var mine = db.ClientServiceTeamMembers.Where(m => m.UserId == me).Select(m => m.ClientId);
        return db.ClientConversations.AsNoTracking().Where(c => mine.Contains(c.ClientId) || !withTeam.Contains(c.ClientId));
    }

    public async Task<PagedResult<ClientConversationSummaryDto>> ListAsync(Guid? clientPublicId, bool unreadOnly, string? q, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 100);
        skip = Math.Max(0, skip);
        var visible = await VisibleAsync(ct);
        var query = from c in visible
                    join cl in db.Clients.AsNoTracking() on c.ClientId equals cl.ClientId
                    join u in db.Users.AsNoTracking() on c.PortalUserId equals u.Id
                    select new { c, ClientPublicId = cl.PublicId, ClientName = cl.Name, u.FullName, u.Email };
        if (clientPublicId is Guid cp) query = query.Where(x => x.ClientPublicId == cp);
        if (unreadOnly) query = query.Where(x => x.c.LastSenderKind == ClientMessageSenders.Client && x.c.LastMessageId > x.c.TeamLastReadMessageId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.ClientName.Contains(term) || (x.FullName != null && x.FullName.Contains(term)) || (x.Email != null && x.Email.Contains(term)) || (x.c.LastMessagePreview != null && x.c.LastMessagePreview.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.c.LastMessageAtUtc).ThenByDescending(x => x.c.ClientConversationId).Skip(skip).Take(take).ToListAsync(ct);
        var ids = rows.Select(r => r.c.ClientConversationId).ToList();
        var unread = await db.ClientMessages.AsNoTracking().Where(m => ids.Contains(m.ClientConversationId) && m.SenderKind == ClientMessageSenders.Client)
            .Join(db.ClientConversations.AsNoTracking(), m => m.ClientConversationId, c => c.ClientConversationId, (m, c) => new { m.ClientConversationId, Unread = m.ClientMessageId > c.TeamLastReadMessageId })
            .Where(x => x.Unread).GroupBy(x => x.ClientConversationId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var items = rows.Select(r => new ClientConversationSummaryDto(r.c.PublicId, r.ClientPublicId, r.ClientName, r.c.PortalUserId, r.FullName ?? r.Email ?? "—", r.Email, r.c.LastMessagePreview, r.c.LastMessageAtUtc, r.c.LastSenderKind,
            unread.GetValueOrDefault(r.c.ClientConversationId))).ToList();
        return new PagedResult<ClientConversationSummaryDto>(items, total, skip, take);
    }

    public async Task<StaffUnreadDto> UnreadAsync(CancellationToken ct)
    {
        var visible = await VisibleAsync(ct);
        return new StaffUnreadDto(await visible.CountAsync(c => c.LastSenderKind == ClientMessageSenders.Client && c.LastMessageId > c.TeamLastReadMessageId, ct));
    }

    /// <summary>Una conversación que veo (404 si no existe o no es de un cliente mío: igual respuesta).</summary>
    private async Task<(ClientConversation Conv, ClientInfo Info)> RequireVisibleAsync(Guid publicId, bool tracked, CancellationToken ct)
    {
        var visible = await VisibleAsync(ct);
        var id = await visible.Where(c => c.PublicId == publicId).Select(c => c.ClientConversationId).FirstOrDefaultAsync(ct);
        if (id == 0) throw new NotFoundException(ClientMessageRules.ConversationNotFound, feminine: true);
        var conv = tracked ? await db.ClientConversations.FirstAsync(c => c.ClientConversationId == id, ct) : await db.ClientConversations.AsNoTracking().FirstAsync(c => c.ClientConversationId == id, ct);
        var cl = await db.Clients.AsNoTracking().Where(c => c.ClientId == conv.ClientId).Select(c => new { c.PublicId, c.Name }).FirstAsync(ct);
        var person = await db.Users.AsNoTracking().Where(u => u.Id == conv.PortalUserId).Select(u => new { u.FullName, u.Email }).FirstAsync(ct);
        return (conv, new ClientInfo(cl.PublicId, cl.Name, person.FullName ?? person.Email ?? "—", person.Email));
    }

    private sealed record ClientInfo(Guid ClientPublicId, string ClientName, string PersonName, string? PersonEmail);

    public async Task<ClientConversationDto> GetAsync(Guid publicId, long? before, int take, CancellationToken ct)
    {
        var (conv, info) = await RequireVisibleAsync(publicId, false, ct);
        var (messages, hasMore) = await PageAsync(conv.ClientConversationId, before, take, T.RequireUserId(), ct);
        return new ClientConversationDto(conv.PublicId, info.ClientPublicId, info.ClientName, conv.PortalUserId, info.PersonName, info.PersonEmail, messages, hasMore);
    }

    /// <summary>Cualquiera del equipo contesta; marca la conversación como leída para el equipo.</summary>
    public async Task<ClientMessageDto> ReplyAsync(Guid publicId, ClientMessageSendRequest req, CancellationToken ct)
    {
        var body = ValidBody(req.Body);
        var me = T.RequireUserId();
        await RequireVisibleAsync(publicId, false, ct);   // 404 si no es de un cliente mío
        var m = await db.RunInTransactionAsync(async ct2 =>
        {
            var conv = await db.ClientConversations.FirstAsync(c => c.PublicId == publicId, ct2);   // la transacción limpia el seguimiento: se vuelve a leer dentro
            var message = await AppendAsync(conv, ClientMessageSenders.Staff, me, body, ct2);
            conv.TeamLastReadMessageId = message.ClientMessageId;
            await db.SaveChangesAsync(ct2);
            return message;
        }, ct);
        return ToDto(m, await NameOfAsync(me, ct), me);
    }

    public async Task MarkReadAsync(Guid publicId, CancellationToken ct)
    {
        var (conv, _) = await RequireVisibleAsync(publicId, true, ct);
        if (conv.TeamLastReadMessageId >= conv.LastMessageId) return;
        conv.TeamLastReadMessageId = conv.LastMessageId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Las personas del cliente (usuarios de portal activos) a quienes se les puede escribir.</summary>
    public async Task<IReadOnlyList<ClientPersonDto>> PeopleAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        await EnsureCanHandleAsync(client.ClientId, ct);
        var active = PortalUserStatuses.Active;
        return await (from p in db.PortalUsers.AsNoTracking()
                      join u in db.Users.AsNoTracking() on p.UserId equals u.Id
                      where p.ClientId == client.ClientId && p.IsActive && p.Status!.InternalCode == active && u.IsActive
                      orderby u.FullName, u.Email
                      select new ClientPersonDto(u.Id, u.FullName ?? u.Email ?? "—", u.Email)).ToListAsync(ct);
    }

    /// <summary>El equipo le escribe primero a una persona del cliente (o, si ya hay conversación con ella, agrega el mensaje a esa). 404 si la persona no es un usuario activo de portal de ese cliente.</summary>
    public async Task<ClientConversationDto> StartAsync(ClientConversationStartRequest req, CancellationToken ct)
    {
        var body = ValidBody(req.Body);
        var me = T.RequireUserId();
        var tid = T.RequireTenantId();
        var client = await db.ResolveClientAsync(req.ClientPublicId, ct);
        ClientQueries.EnsureClientActive(client);
        await EnsureCanHandleAsync(client.ClientId, ct);
        var active = PortalUserStatuses.Active;
        var isPerson = await db.PortalUsers.AsNoTracking().AnyAsync(p => p.ClientId == client.ClientId && p.UserId == req.PersonId && p.IsActive && p.Status!.InternalCode == active, ct);
        if (!isPerson) throw new ValidationException("personId", ClientMessageRules.PersonNotFound);

        var convId = await db.RunInTransactionAsync(async ct2 =>
        {
            var conv = await db.ClientConversations.FirstOrDefaultAsync(c => c.ClientId == client.ClientId && c.PortalUserId == req.PersonId, ct2);
            if (conv is null)
            {
                conv = new ClientConversation { PublicId = Guid.NewGuid(), TenantId = tid, ClientId = client.ClientId, PortalUserId = req.PersonId, StartedBy = ClientMessageSenders.Staff };
                db.ClientConversations.Add(conv);
                await db.SaveChangesAsync(ct2);
            }
            var m = await AppendAsync(conv, ClientMessageSenders.Staff, me, body, ct2);
            conv.TeamLastReadMessageId = m.ClientMessageId;
            await db.SaveChangesAsync(ct2);
            return conv.PublicId;
        }, ct);
        return await GetAsync(convId, null, PageSize, ct);
    }

    /// <summary>¿Puedo atender a este cliente? (administro, estoy en su equipo, o no tiene equipo): si no, 404 igual que un cliente que no existe.</summary>
    private async Task EnsureCanHandleAsync(int clientId, CancellationToken ct)
    {
        if (await SeesAllAsync(ct)) return;
        var me = T.RequireUserId();
        var hasTeam = await db.ClientServiceTeamMembers.AnyAsync(m => m.ClientId == clientId, ct);
        if (!hasTeam) return;
        if (!await db.ClientServiceTeamMembers.AnyAsync(m => m.ClientId == clientId && m.UserId == me, ct)) throw new NotFoundException("Cliente");
    }

    // ================================================================ el equipo de cada cliente

    public async Task<ClientServiceTeamDto> GetTeamAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        var members = await (from m in db.ClientServiceTeamMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                             where m.ClientId == client.ClientId && u.IsActive
                             orderby u.FullName, u.Email
                             select new ClientServiceTeamMemberDto(u.Id, u.FullName ?? u.Email ?? "—", u.Email)).ToListAsync(ct);
        var fallback = members.Count == 0 ? (await UsersWithPermissionAsync(PermissionCatalog.ClientMessagesReply, ct)).Count : 0;
        return new ClientServiceTeamDto(members, members.Count == 0, fallback);
    }

    /// <summary>Reemplaza el equipo del cliente por esas personas (vacío = sin equipo: lo atiende quien tenga el permiso). 400 si alguna no es un usuario interno activo de la compañía.</summary>
    public async Task<ClientServiceTeamDto> SetTeamAsync(Guid clientPublicId, ClientServiceTeamRequest req, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        ClientQueries.EnsureClientActive(client);
        var tid = T.RequireTenantId();
        var wanted = (req.UserIds ?? Array.Empty<int>()).Distinct().ToList();
        if (wanted.Count > 0)
        {
            var valid = (await CandidatesAsync(ct)).Select(c => c.UserId).ToHashSet();
            if (wanted.Any(id => !valid.Contains(id))) throw new ValidationException("userIds", ClientMessageRules.TeamUserInvalid);
        }
        var current = await db.ClientServiceTeamMembers.Where(m => m.ClientId == client.ClientId).ToListAsync(ct);
        db.ClientServiceTeamMembers.RemoveRange(current.Where(m => !wanted.Contains(m.UserId)));
        foreach (var id in wanted.Where(id => current.All(m => m.UserId != id)))
            db.ClientServiceTeamMembers.Add(new ClientServiceTeamMember { TenantId = tid, ClientId = client.ClientId, UserId = id, AddedBy = T.UserId });
        await db.SaveChangesAsync(ct);
        return await GetTeamAsync(clientPublicId, ct);
    }

    /// <summary>Las personas de la compañía que se pueden designar: usuarios internos activos.</summary>
    public async Task<IReadOnlyList<TeamCandidateDto>> CandidatesAsync(CancellationToken ct)
    {
        var internalKindIds = db.LookupCodes.AsNoTracking().IgnoreQueryFilters().Where(l => l.Entity == LookupDomains.UserKind && l.InternalCode != UserKinds.Portal).Select(l => (int?)l.LookupCodeId);
        var memberIds = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        return await db.Users.AsNoTracking().Where(u => u.IsActive && memberIds.Contains(u.Id) && (u.UserKindLookupId == null || internalKindIds.Contains(u.UserKindLookupId)))
            .OrderBy(u => u.FullName).ThenBy(u => u.Email).Select(u => new TeamCandidateDto(u.Id, u.FullName ?? u.Email ?? "—", u.Email)).ToListAsync(ct);
    }

    /// <summary>Las personas de la compañía con ese permiso (por rol o directo): la base del «equipo por omisión».</summary>
    private async Task<List<int>> UsersWithPermissionAsync(string code, CancellationToken ct)
    {
        var tid = T.RequireTenantId();
        var viaRoles = await (from ur in db.AppUserRoles.AsNoTracking().IgnoreQueryFilters()
                              where ur.TenantId == tid && ur.Role!.IsActive && ur.Role.Permissions.Any(rp => rp.Permission!.Code == code)
                              select ur.UserId).ToListAsync(ct);
        var direct = await db.UserPermissions.AsNoTracking().IgnoreQueryFilters().Where(up => up.TenantId == tid && up.Permission!.Code == code).Select(up => up.UserId).ToListAsync(ct);
        var ids = viaRoles.Concat(direct).Distinct().ToList();
        var memberIds = db.UserTenants.AsNoTracking().Select(m => m.UserId);
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && memberIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
    }

    // ================================================================ comunes

    private static string ValidBody(string? raw)
    {
        var body = ClientMessageRules.Normalize(raw) ?? throw new ValidationException("body", ClientMessageRules.EmptyMessage);
        if (body.Length > ClientMessageRules.MaxBodyLength) throw new ValidationException("body", ClientMessageRules.TooLongMessage);
        return body;
    }

    private async Task<ClientMessage> AppendAsync(ClientConversation conv, string senderKind, int senderUserId, string body, CancellationToken ct)
    {
        var m = new ClientMessage { TenantId = conv.TenantId, ClientConversationId = conv.ClientConversationId, SenderKind = senderKind, SenderUserId = senderUserId, Body = body, CreatedAtUtc = DateTime.UtcNow };
        db.ClientMessages.Add(m);
        await db.SaveChangesAsync(ct);
        conv.LastMessageId = m.ClientMessageId;
        conv.LastMessageAtUtc = m.CreatedAtUtc;
        conv.LastMessagePreview = ClientMessageRules.Preview(body);
        conv.LastSenderKind = senderKind;
        return m;
    }

    /// <summary>Una página de mensajes: los más recientes (o los anteriores a <paramref name="before"/>), devueltos en orden cronológico; HasMore = hay más viejos.</summary>
    private async Task<(IReadOnlyList<ClientMessageDto> Messages, bool HasMore)> PageAsync(long conversationId, long? before, int take, int viewerUserId, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? PageSize : take, 1, 100);
        var q = db.ClientMessages.AsNoTracking().Where(m => m.ClientConversationId == conversationId);
        if (before is long b) q = q.Where(m => m.ClientMessageId < b);
        var rows = await q.OrderByDescending(m => m.ClientMessageId).Take(take + 1).ToListAsync(ct);
        var hasMore = rows.Count > take;
        rows = rows.Take(take).OrderBy(m => m.ClientMessageId).ToList();
        var names = await NamesAsync(rows.Select(r => r.SenderUserId).Distinct().ToList(), ct);
        return (rows.Select(m => ToDto(m, names.GetValueOrDefault(m.SenderUserId, "—"), viewerUserId)).ToList(), hasMore);
    }

    private static ClientMessageDto ToDto(ClientMessage m, string name, int viewer) => new(m.ClientMessageId, m.SenderKind, name, m.Body, m.CreatedAtUtc, m.SenderUserId == viewer);

    private async Task<Dictionary<int, string>> NamesAsync(List<int> ids, CancellationToken ct)
        => await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? "—" }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private async Task<string> NameOfAsync(int userId, CancellationToken ct) => (await NamesAsync(new List<int> { userId }, ct)).GetValueOrDefault(userId, "—");
}
