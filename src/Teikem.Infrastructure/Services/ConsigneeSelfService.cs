using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 117 — el espacio del consignatario con acceso propio (docs/p10-portal-diseno.md; aclaración de Luis 2026-10-18). Sale de la sesión: el consignatario (<c>loc</c>) y el cliente (<c>cid</c>); nunca de lo que mande la pantalla.
/// Reglas: el CLIENTE prende los avisos y el medio; el consignatario los puede APAGAR cuando quiera y volver a prender solo lo que apagó él mismo (si el cliente nunca lo prendió, o se dio de baja por el enlace de un aviso,
/// no puede prenderlo). Cada cambio queda en la bitácora (ConsigneeNoticeConsent, origen CONSIGNEE). Ve la bandeja de lo que se le avisó. Solo ve lo suyo.
/// </summary>
public sealed class ConsigneeSelfService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ConsigneeNoticeService notices)
{
    public const string NotConsigneeMessage = "Este espacio es solo para consignatarios con acceso.";
    public const string CannotTurnOnMessage = "Solo su cliente puede prender este aviso: usted puede volver a prender únicamente el que apagó.";
    public const string NoContactMessage = "No tiene registrado ese medio de contacto: pídale a su cliente que lo agregue.";
    public const int MaxPageSize = 100;

    private TenantContext T => (TenantContext)tenant;
    private int LocationId => T.PortalLocationId ?? throw new ForbiddenException(NotConsigneeMessage);
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(NotConsigneeMessage);

    private async Task<(Location Loc, Client Client)> LoadAsync(bool track, CancellationToken ct)
    {
        var lid = LocationId;
        var cid = ClientId;
        var q = track ? db.Locations.AsTracking() : db.Locations.AsNoTracking();
        var loc = await q.FirstOrDefaultAsync(l => l.LocationId == lid && l.ClientId == cid && l.IsActive, ct) ?? throw new ForbiddenException(NotConsigneeMessage);
        var client = await db.Clients.AsNoTracking().FirstAsync(c => c.ClientId == cid, ct);
        return (loc, client);
    }

    private static string ChannelOf(string? raw) => ConsigneeNoticeChannels.Normalize(raw) ?? throw new ValidationException("channel", ConsigneeNoticeRules.InvalidChannelMessage);

    /// <summary>¿Lo último que pasó con ese medio fue que el propio consignatario lo apagó? (única razón para que pueda volver a prenderlo).</summary>
    private async Task<bool> TurnedOffByHimAsync(int locationId, string channel, CancellationToken ct)
    {
        var last = await db.ConsigneeNoticeConsents.AsNoTracking().Where(c => c.LocationId == locationId && c.Channel == channel)
            .OrderByDescending(c => c.ChangedAtUtc).ThenByDescending(c => c.ConsigneeNoticeConsentId).Select(c => new { c.Source, c.Accepted }).FirstOrDefaultAsync(ct);
        return last is { Source: ConsigneeNoticeSources.Consignee, Accepted: false };
    }

    private async Task<ConsigneeChannelDto> ChannelAsync(Location loc, Client client, string channel, string? contact, CancellationToken ct)
    {
        var enabled = channel == ConsigneeNoticeChannels.Email ? loc.NoticeEmailOptIn : loc.NoticeSmsOptIn;
        var unsub = contact is not null && await notices.IsUnsubscribedAsync(channel, contact, ct);
        var canTurnOn = !enabled && contact is not null && !unsub && client.NotifyConsignee && await TurnedOffByHimAsync(loc.LocationId, channel, ct);
        return new ConsigneeChannelDto(contact, enabled, canTurnOn, unsub);
    }

    public async Task<ConsigneeMeDto> GetMeAsync(CancellationToken ct)
    {
        var (loc, client) = await LoadAsync(track: false, ct);
        var userId = T.RequireUserId();
        var cid = ClientId;
        var person = await db.PortalUsers.AsNoTracking().Where(p => p.UserId == userId && p.ClientId == cid && p.LocationId == loc.LocationId && p.IsActive)
            .Select(p => new { p.FullName, p.Email }).FirstOrDefaultAsync(ct);
        var (mail, phone) = await notices.ContactsAsync(loc.LocationId, ct);
        return new ConsigneeMeDto(person?.FullName, person?.Email, loc.Name, loc.City, client.Name, client.NotifyConsignee,
            await ChannelAsync(loc, client, ConsigneeNoticeChannels.Email, mail, ct), await ChannelAsync(loc, client, ConsigneeNoticeChannels.Sms, phone, ct));
    }

    /// <summary>Apaga (siempre) o vuelve a prender (solo lo que apagó él) un medio de aviso desde el perfil. 409 si intenta prender lo que no puede.</summary>
    public async Task<ConsigneeMeDto> SetChannelAsync(string? channelRaw, ConsigneeChannelRequest? req, CancellationToken ct)
    {
        var channel = ChannelOf(channelRaw);
        if (req?.Enabled is not bool enable) throw new ValidationException("enabled", "Indique si desea recibir o no los avisos por ese medio.");
        var (loc, client) = await LoadAsync(track: true, ct);
        var (mail, phone) = await notices.ContactsAsync(loc.LocationId, ct);
        var contact = channel == ConsigneeNoticeChannels.Email ? mail : phone;
        var current = channel == ConsigneeNoticeChannels.Email ? loc.NoticeEmailOptIn : loc.NoticeSmsOptIn;
        if (enable != current)
        {
            if (enable)
            {
                if (contact is null) throw new ValidationException("channel", NoContactMessage);
                var dto = await ChannelAsync(loc, client, channel, contact, ct);
                if (!dto.CanTurnOn) throw new ConflictException(CannotTurnOnMessage);
            }
            if (channel == ConsigneeNoticeChannels.Email) loc.NoticeEmailOptIn = enable; else loc.NoticeSmsOptIn = enable;
            db.ConsigneeNoticeConsents.Add(new ConsigneeNoticeConsent
            {
                TenantId = T.RequireTenantId(), ClientId = loc.ClientId ?? ClientId, LocationId = loc.LocationId, Channel = channel, Accepted = enable, Contact = contact ?? string.Empty,
                WarningAccepted = enable, Source = ConsigneeNoticeSources.Consignee, ChangedBy = tenant.UserId, ChangedAtUtc = clock.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        return await GetMeAsync(ct);
    }

    /// <summary>La bandeja: los avisos que se le enviaron a este consignatario (los más nuevos primero; máximo 100 por página).</summary>
    public async Task<PagedResult<ConsigneeInboxItemDto>> ListNoticesAsync(int skip, int take, CancellationToken ct)
    {
        var lid = LocationId;
        var cid = ClientId;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxPageSize);
        skip = Math.Max(0, skip);
        var q = db.ConsigneeNotices.AsNoTracking().Where(n => n.LocationId == lid && n.ClientId == cid);
        var total = await q.CountAsync(ct);
        var rows = await (from n in q.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.ConsigneeNoticeId).Skip(skip).Take(take)
                          join o in db.TransportOrders.AsNoTracking() on n.TransportOrderId equals o.TransportOrderId into os
                          from o in os.DefaultIfEmpty()
                          select new { n.ConsigneeNoticeId, n.Kind, n.Channel, n.StatusCode, n.Lang, n.Status, n.CreatedAtUtc, n.SentAtUtc, Waybill = o == null ? null : o.WaybillNumber, TrackToken = o == null ? null : o.TrackToken }).ToListAsync(ct);
        var clientName = await db.Clients.AsNoTracking().Where(c => c.ClientId == cid).Select(c => c.Name).FirstAsync(ct);
        var lang = tenant.Lang == "en" ? "en" : "es";
        var items = rows.Select(r => new ConsigneeInboxItemDto(r.ConsigneeNoticeId, r.Kind, r.Channel,
            r.Kind == ConsigneeNoticeKinds.Consent ? ConsigneeNoticeRules.ConsentSubject(clientName, lang) : (lang == "en" ? $"Your package {ConsigneeNoticeRules.Phrase(r.StatusCode, lang)}" : $"Su paquete {ConsigneeNoticeRules.Phrase(r.StatusCode, lang)}"),
            r.Waybill, r.TrackToken, r.Status, r.CreatedAtUtc, r.SentAtUtc)).ToList();
        return new PagedResult<ConsigneeInboxItemDto>(items, total, skip, take);
    }
}
