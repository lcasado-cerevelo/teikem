using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Messaging;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 117b — mensajes del consignatario con acceso propio con su cliente (dictado por Luis, 2026-10-18). <b>Lado del consignatario</b>: cada persona del consignatario tiene SU conversación con el cliente (la crea el primer
/// mensaje); la conversación sale del usuario, el cliente y el consignatario de la SESIÓN, nunca del request. <b>Lado del cliente</b>: sus administradores y operadores ven las conversaciones de sus consignatarios, las contestan
/// (cualquiera; la lectura es compartida) y los de solo consulta las leen; nunca ven las de otro cliente. Los mensajes no se editan ni se borran. Mismo tope que el resto del portal (2000 caracteres, 30 por hora por persona).
/// </summary>
public sealed class ConsigneeMessagingService(TeikemDbContext db, ITenantContext tenant, ITransactionalEmailSender? email = null, IConfiguration? config = null)
{
    public const string ReadOnlyMessage = "Su rol en el portal es de solo consulta: puede leer los mensajes de los consignatarios, no contestarlos.";
    public const string ConversationNotFound = "Conversación";
    public const int PageSize = 30;

    private TenantContext T => (TenantContext)tenant;

    // ================================================================ lado del consignatario

    private int OwnClientId => T.PortalClientId ?? throw new ForbiddenException(ConsigneeSelfService.NotConsigneeMessage);
    private int OwnLocationId => T.PortalLocationId ?? throw new ForbiddenException(ConsigneeSelfService.NotConsigneeMessage);

    private async Task<ConsigneeConversation?> MineAsync(CancellationToken ct)
    {
        var cid = OwnClientId; var lid = OwnLocationId; var me = T.RequireUserId();
        return await db.ConsigneeConversations.FirstOrDefaultAsync(c => c.ClientId == cid && c.LocationId == lid && c.ConsigneeUserId == me, ct);
    }

    /// <summary>Mi conversación con el cliente: los últimos mensajes (o los anteriores a <paramref name="before"/>), en orden cronológico, y cuántos del cliente no he leído.</summary>
    public async Task<PortalMessagesDto> GetMineAsync(long? before, int take, CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        if (conv is null) return new PortalMessagesDto(null, Array.Empty<ClientMessageDto>(), false, 0);
        var (messages, hasMore) = await PageAsync(conv.ConsigneeConversationId, before, take, T.RequireUserId(), ct);
        return new PortalMessagesDto(conv.PublicId, messages, hasMore, await UnreadForConsigneeAsync(conv, ct));
    }

    public async Task<PortalUnreadDto> UnreadMineAsync(CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        return new PortalUnreadDto(conv is null ? 0 : await UnreadForConsigneeAsync(conv, ct));
    }

    private Task<int> UnreadForConsigneeAsync(ConsigneeConversation c, CancellationToken ct)
        => db.ConsigneeMessages.AsNoTracking().CountAsync(m => m.ConsigneeConversationId == c.ConsigneeConversationId && m.SenderKind == ConsigneeMessageSenders.Client && m.ConsigneeMessageId > c.ConsigneeLastReadMessageId, ct);

    /// <summary>El consignatario le escribe a su cliente (400 si está vacío o pasa de 2000 caracteres; 429 si manda demasiados seguidos). Crea la conversación con el primer mensaje.</summary>
    public async Task<ClientMessageDto> SendAsConsigneeAsync(ClientMessageSendRequest req, CancellationToken ct)
    {
        var cid = OwnClientId; var lid = OwnLocationId; var me = T.RequireUserId(); var tid = T.RequireTenantId();
        var body = ValidBody(req.Body);
        await EnsureNotTooManyAsync(me, ConsigneeMessageSenders.Consignee, ct);
        var notify = true;   // Lote 117e: se le avisa al cliente por correo cuando no tenía ya un mensaje del consignatario sin leer (así varios seguidos son un solo correo)
        var message = await db.RunInTransactionAsync(async ct2 =>
        {
            var conv = await db.ConsigneeConversations.FirstOrDefaultAsync(c => c.ClientId == cid && c.LocationId == lid && c.ConsigneeUserId == me, ct2);
            if (conv is not null) notify = !(conv.LastSenderKind == ConsigneeMessageSenders.Consignee && conv.LastMessageId > conv.ClientLastReadMessageId);
            if (conv is null)
            {
                conv = new ConsigneeConversation { PublicId = Guid.NewGuid(), TenantId = tid, ClientId = cid, LocationId = lid, ConsigneeUserId = me };
                db.ConsigneeConversations.Add(conv);
                await db.SaveChangesAsync(ct2);
            }
            var m = await AppendAsync(conv, ConsigneeMessageSenders.Consignee, me, body, ct2);
            conv.ConsigneeLastReadMessageId = m.ConsigneeMessageId;   // lo que escribo ya lo leí
            await db.SaveChangesAsync(ct2);
            return m;
        }, ct);
        var dto = ToDto(message, await NameOfAsync(me, ct), me);
        if (notify) await NotifyClientAsync(cid, lid, ct);
        return dto;
    }

    public const string BaseUrlKey = "ConsigneeNotices:ClientAppBaseUrl";

    /// <summary>
    /// Lote 117e (pedido por Luis, 2026-10-18): le avisa por correo a los administradores y operadores del cliente que un consignatario le escribió (sin el texto del mensaje, con el enlace a sus mensajes). Un solo correo mientras haya
    /// mensajes del consignatario sin leer. Es de mejor esfuerzo: si el correo no está configurado o falla, el mensaje igual queda enviado.
    /// </summary>
    private async Task NotifyClientAsync(int clientId, int locationId, CancellationToken ct)
    {
        if (email is null || !email.IsConfigured) return;
        try
        {
            var active = PortalUserStatuses.Active;
            var recipients = await (from p in db.PortalUsers.AsNoTracking()
                                    join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                                    where p.ClientId == clientId && p.IsActive && p.Status!.InternalCode == active && (r.InternalCode == PortalRoles.ClientAdmin || r.InternalCode == PortalRoles.ClientOperator)
                                    select new { p.Email, p.FullName }).ToListAsync(ct);
            if (recipients.Count == 0) return;
            var consignee = await db.Locations.AsNoTracking().Where(l => l.LocationId == locationId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId).Select(c => new { c.Name, c.NoticeLang }).FirstAsync(ct);
            var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == T.RequireTenantId()).Select(t => t.Name).FirstOrDefaultAsync(ct);
            var baseUrl = config?[BaseUrlKey]?.TrimEnd('/');
            var en = client.NoticeLang == "en";
            var subject = en ? $"{consignee} wrote to you" : $"{consignee} le escribió";
            var link = string.IsNullOrEmpty(baseUrl) ? string.Empty : $"{baseUrl}/consignee-messages";
            static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            var html = en
                ? $"<p><b>{E(consignee)}</b> sent you a new message.</p>" + (link.Length > 0 ? $"<p><a href=\"{link}\">Read and answer it</a></p>" : "<p>Open the Messages tab of the client app to read and answer it.</p>")
                : $"<p><b>{E(consignee)}</b> le envió un mensaje nuevo.</p>" + (link.Length > 0 ? $"<p><a href=\"{link}\">Leerlo y contestarlo</a></p>" : "<p>Abra la pestaña Mensajes de la app del cliente para leerlo y contestarlo.</p>");
            foreach (var r in recipients.DistinctBy(x => x.Email.ToLowerInvariant()))
                await email.SendAsync(r.Email, r.FullName, subject, html, ct, company);
        }
        catch (Exception)
        {
            // el aviso es de mejor esfuerzo: nunca impide que el mensaje quede enviado
        }
    }

    /// <summary>Marca como leído lo que escribió el cliente.</summary>
    public async Task MarkReadAsConsigneeAsync(CancellationToken ct)
    {
        var conv = await MineAsync(ct);
        if (conv is null || conv.ConsigneeLastReadMessageId >= conv.LastMessageId) return;
        conv.ConsigneeLastReadMessageId = conv.LastMessageId;
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ lado del cliente

    private int ClientId
    {
        get
        {
            // un consignatario no entra a los mensajes del cliente aunque tenga sesión de portal
            if (T.PortalLocationId is not null) throw new ForbiddenException(AuthService.PortalNoAccessMessage);
            return T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);
        }
    }

    private async Task<bool> CanReplyAsync(CancellationToken ct)
    {
        var userId = T.RequireUserId(); var cid = ClientId;
        var role = await (from p in db.PortalUsers.AsNoTracking()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == userId && p.ClientId == cid && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        return role is PortalRoles.ClientAdmin or PortalRoles.ClientOperator;
    }

    /// <summary>Las conversaciones de los consignatarios de mi cliente (las más recientes primero); solo las no leídas, o búsqueda por consignatario, persona, correo o texto del último mensaje.</summary>
    public async Task<PagedResult<ConsigneeConversationSummaryDto>> ListAsync(bool unreadOnly, string? q, int skip, int take, CancellationToken ct)
    {
        var cid = ClientId;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, 100);
        skip = Math.Max(0, skip);
        var query = from c in db.ConsigneeConversations.AsNoTracking()
                    join l in db.Locations.AsNoTracking() on c.LocationId equals l.LocationId
                    join u in db.Users.AsNoTracking() on c.ConsigneeUserId equals u.Id
                    where c.ClientId == cid
                    select new { c, LocationPublicId = l.PublicId, ConsigneeName = l.Name, u.FullName, u.Email };
        if (unreadOnly) query = query.Where(x => x.c.LastSenderKind == ConsigneeMessageSenders.Consignee && x.c.LastMessageId > x.c.ClientLastReadMessageId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.ConsigneeName.Contains(term) || (x.FullName != null && x.FullName.Contains(term)) || (x.Email != null && x.Email.Contains(term)) || (x.c.LastMessagePreview != null && x.c.LastMessagePreview.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.c.LastMessageAtUtc).ThenByDescending(x => x.c.ConsigneeConversationId).Skip(skip).Take(take).ToListAsync(ct);
        var ids = rows.Select(r => r.c.ConsigneeConversationId).ToList();
        var unread = await db.ConsigneeMessages.AsNoTracking().Where(m => ids.Contains(m.ConsigneeConversationId) && m.SenderKind == ConsigneeMessageSenders.Consignee)
            .Join(db.ConsigneeConversations.AsNoTracking(), m => m.ConsigneeConversationId, c => c.ConsigneeConversationId, (m, c) => new { m.ConsigneeConversationId, Unread = m.ConsigneeMessageId > c.ClientLastReadMessageId })
            .Where(x => x.Unread).GroupBy(x => x.ConsigneeConversationId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var items = rows.Select(r => new ConsigneeConversationSummaryDto(r.c.PublicId, r.LocationPublicId, r.ConsigneeName, r.FullName ?? r.Email ?? "—", r.Email, r.c.LastMessagePreview, r.c.LastMessageAtUtc, r.c.LastSenderKind,
            unread.GetValueOrDefault(r.c.ConsigneeConversationId))).ToList();
        return new PagedResult<ConsigneeConversationSummaryDto>(items, total, skip, take);
    }

    /// <summary>Cuántas conversaciones de mis consignatarios tienen mensajes sin leer (para el número de la pestaña).</summary>
    public async Task<PortalUnreadDto> UnreadAsync(CancellationToken ct)
    {
        var cid = ClientId;
        return new PortalUnreadDto(await db.ConsigneeConversations.AsNoTracking().CountAsync(c => c.ClientId == cid && c.LastSenderKind == ConsigneeMessageSenders.Consignee && c.LastMessageId > c.ClientLastReadMessageId, ct));
    }

    private sealed record Info(Guid LocationPublicId, string ConsigneeName, string PersonName, string? PersonEmail);

    /// <summary>Una conversación de mi cliente (404 si no existe o es de otro cliente: igual respuesta).</summary>
    private async Task<(ConsigneeConversation Conv, Info Info)> RequireAsync(Guid publicId, bool tracked, CancellationToken ct)
    {
        var cid = ClientId;
        var q = tracked ? db.ConsigneeConversations.AsTracking() : db.ConsigneeConversations.AsNoTracking();
        var conv = await q.FirstOrDefaultAsync(c => c.PublicId == publicId && c.ClientId == cid, ct) ?? throw new NotFoundException(ConversationNotFound, feminine: true);
        var loc = await db.Locations.AsNoTracking().Where(l => l.LocationId == conv.LocationId).Select(l => new { l.PublicId, l.Name }).FirstAsync(ct);
        var person = await db.Users.AsNoTracking().Where(u => u.Id == conv.ConsigneeUserId).Select(u => new { u.FullName, u.Email }).FirstAsync(ct);
        return (conv, new Info(loc.PublicId, loc.Name, person.FullName ?? person.Email ?? "—", person.Email));
    }

    public async Task<ConsigneeConversationDto> GetAsync(Guid publicId, long? before, int take, CancellationToken ct)
    {
        var (conv, info) = await RequireAsync(publicId, false, ct);
        var (messages, hasMore) = await PageAsync(conv.ConsigneeConversationId, before, take, T.RequireUserId(), ct);
        return new ConsigneeConversationDto(conv.PublicId, info.LocationPublicId, info.ConsigneeName, info.PersonName, info.PersonEmail, messages, hasMore);
    }

    /// <summary>Un administrador u operador del cliente contesta; la marca como leída para todos los de su cliente. Solo consulta = 403.</summary>
    public async Task<ClientMessageDto> ReplyAsync(Guid publicId, ClientMessageSendRequest req, CancellationToken ct)
    {
        if (!await CanReplyAsync(ct)) throw new ForbiddenException(ReadOnlyMessage);
        var body = ValidBody(req.Body);
        var me = T.RequireUserId();
        var cid = ClientId;
        await RequireAsync(publicId, false, ct);   // 404 si no es de mi cliente
        await EnsureNotTooManyAsync(me, ConsigneeMessageSenders.Client, ct);
        var m = await db.RunInTransactionAsync(async ct2 =>
        {
            var conv = await db.ConsigneeConversations.FirstAsync(c => c.PublicId == publicId && c.ClientId == cid, ct2);   // la transacción limpia el seguimiento: se vuelve a leer dentro
            var message = await AppendAsync(conv, ConsigneeMessageSenders.Client, me, body, ct2);
            conv.ClientLastReadMessageId = message.ConsigneeMessageId;
            await db.SaveChangesAsync(ct2);
            return message;
        }, ct);
        return ToDto(m, await NameOfAsync(me, ct), me);
    }

    /// <summary>Marca como leído lo que escribió el consignatario (para todos los de mi cliente).</summary>
    public async Task MarkReadAsClientAsync(Guid publicId, CancellationToken ct)
    {
        var (conv, _) = await RequireAsync(publicId, true, ct);
        if (conv.ClientLastReadMessageId >= conv.LastMessageId) return;
        conv.ClientLastReadMessageId = conv.LastMessageId;
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ comunes

    private static string ValidBody(string? raw)
    {
        var body = ClientMessageRules.Normalize(raw) ?? throw new ValidationException("body", ClientMessageRules.EmptyMessage);
        if (body.Length > ClientMessageRules.MaxBodyLength) throw new ValidationException("body", ClientMessageRules.TooLongMessage);
        return body;
    }

    private async Task EnsureNotTooManyAsync(int userId, string senderKind, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.ConsigneeMessages.AsNoTracking().CountAsync(m => m.SenderUserId == userId && m.SenderKind == senderKind && m.CreatedAtUtc > since, ct);
        if (recent >= ClientMessageRules.MaxPerHour) throw new TooManyMessagesException();
    }

    private async Task<ConsigneeMessage> AppendAsync(ConsigneeConversation conv, string senderKind, int senderUserId, string body, CancellationToken ct)
    {
        var m = new ConsigneeMessage { TenantId = conv.TenantId, ConsigneeConversationId = conv.ConsigneeConversationId, SenderKind = senderKind, SenderUserId = senderUserId, Body = body, CreatedAtUtc = DateTime.UtcNow };
        db.ConsigneeMessages.Add(m);
        await db.SaveChangesAsync(ct);
        conv.LastMessageId = m.ConsigneeMessageId;
        conv.LastMessageAtUtc = m.CreatedAtUtc;
        conv.LastMessagePreview = ClientMessageRules.Preview(body);
        conv.LastSenderKind = senderKind;
        return m;
    }

    private async Task<(IReadOnlyList<ClientMessageDto> Messages, bool HasMore)> PageAsync(long conversationId, long? before, int take, int viewerUserId, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? PageSize : take, 1, 100);
        var q = db.ConsigneeMessages.AsNoTracking().Where(m => m.ConsigneeConversationId == conversationId);
        if (before is long b) q = q.Where(m => m.ConsigneeMessageId < b);
        var rows = await q.OrderByDescending(m => m.ConsigneeMessageId).Take(take + 1).ToListAsync(ct);
        var hasMore = rows.Count > take;
        rows = rows.Take(take).OrderBy(m => m.ConsigneeMessageId).ToList();
        var names = await NamesAsync(rows.Select(r => r.SenderUserId).Distinct().ToList(), ct);
        return (rows.Select(m => ToDto(m, names.GetValueOrDefault(m.SenderUserId, "—"), viewerUserId)).ToList(), hasMore);
    }

    private static ClientMessageDto ToDto(ConsigneeMessage m, string name, int viewer) => new(m.ConsigneeMessageId, m.SenderKind, name, m.Body, m.CreatedAtUtc, m.SenderUserId == viewer);

    private async Task<Dictionary<int, string>> NamesAsync(List<int> ids, CancellationToken ct)
        => await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, Name = u.FullName ?? u.Email ?? "—" }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private async Task<string> NameOfAsync(int userId, CancellationToken ct) => (await NamesAsync(new List<int> { userId }, ct)).GetValueOrDefault(userId, "—");
}
