using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 116 — avisos al consignatario (dictado por Luis; docs/p12). <b>El cliente</b> (desde el portal, rol administrador u operador) prende el aviso, escoge su idioma y marca, por consignatario, si acepta correo y/o texto
/// (con la advertencia aceptada; queda en la bitácora y se le avisa al consignatario). Cuando una orden de un cliente con el aviso prendido sale En ruta, tiene un intento fallido o llega a un estatus final, se encola un aviso para el
/// consignatario marcado, con un enlace de seguimiento por TOKEN propio y otro de baja. Nunca se avisa de los movimientos dentro del almacén ni de los estatus brincados. Un correo que se dio de baja no vuelve a recibir nada.
/// </summary>
public sealed class ConsigneeNoticeService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ITransactionalEmailSender email, ILookupCache lookups, IConfiguration config)
{
    public const int MaxPageSize = 100;
    public const string ReadOnlyMessage = "Su rol en el portal es de solo consulta: no puede cambiar los avisos al consignatario.";
    public const string SmsNotConfiguredMessage = "El envío de texto todavía no está configurado.";
    public const string NoBaseUrlMessage = "No está configurada la dirección de la app del cliente.";
    public const string UnsubscribedMessage = "El consignatario se dio de baja de los avisos por ese medio.";
    public const string CannotMarkUnsubscribedMessage = "El consignatario se dio de baja de los avisos por ese medio: no se puede volver a marcar.";
    public const string EmailNotConfiguredMessage = "El envío de correo no está configurado.";
    public const string BaseUrlKey = "ConsigneeNotices:ClientAppBaseUrl";

    private TenantContext T => (TenantContext)tenant;
    private int ClientId => T.PortalClientId ?? throw new ForbiddenException(AuthService.PortalNoAccessMessage);

    // ================================================================ lo que decide el cliente (portal)

    private async Task<bool> CanEditAsync(CancellationToken ct)
    {
        var userId = T.RequireUserId();
        var cid = ClientId;
        var role = await (from p in db.PortalUsers.AsNoTracking()
                          join r in db.LookupCodes.AsNoTracking() on p.RoleLookupId equals r.LookupCodeId
                          where p.UserId == userId && p.ClientId == cid && p.IsActive
                          select r.InternalCode).FirstOrDefaultAsync(ct);
        return role is PortalRoles.ClientAdmin or PortalRoles.ClientOperator;
    }

    private async Task EnsureCanEditAsync(CancellationToken ct)
    {
        if (!await CanEditAsync(ct)) throw new ForbiddenException(ReadOnlyMessage);
    }

    public async Task<ConsigneeNoticeSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var cid = ClientId;
        var row = await db.Clients.AsNoTracking().Where(c => c.ClientId == cid).Select(c => new { c.NotifyConsignee, c.NoticeLang }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Cliente");
        return new ConsigneeNoticeSettingsDto(row.NotifyConsignee, await LangOfAsync(row.NoticeLang, ct), email.IsConfigured, await CanEditAsync(ct));
    }

    public async Task<ConsigneeNoticeSettingsDto> SetSettingsAsync(ConsigneeNoticeSettingsRequest? req, CancellationToken ct)
    {
        await EnsureCanEditAsync(ct);
        var lang = req?.Lang?.Trim().ToLowerInvariant();
        if (lang is not null and not ("es" or "en")) throw new ValidationException("lang", ConsigneeNoticeRules.InvalidLangMessage);
        var cid = ClientId;
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientId == cid, ct) ?? throw new NotFoundException("Cliente");
        if (req?.Enabled is bool enabled) client.NotifyConsignee = enabled;
        if (lang is not null) client.NoticeLang = lang;
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    /// <summary>Los consignatarios del cliente (localizaciones de entrega) con su correo y teléfono principales y lo que aceptaron. Búsqueda por nombre, ciudad, correo o teléfono.</summary>
    public async Task<PagedResult<ConsigneeNoticeConsigneeDto>> ListConsigneesAsync(string? search, int skip, int take, CancellationToken ct)
    {
        var cid = ClientId;
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxPageSize);
        skip = Math.Max(0, skip);
        var deliveryTypeId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Delivery, ct);
        var q = db.Locations.AsNoTracking().Where(l => l.ClientId == cid && l.IsActive && l.LocationTypeLookupId == deliveryTypeId);
        var s = search?.Trim();
        if (!string.IsNullOrEmpty(s))
            q = q.Where(l => l.Name.Contains(s) || l.City.Contains(s) || (l.Code != null && l.Code.Contains(s)));
        var total = await q.CountAsync(ct);
        var page = await q.OrderBy(l => l.Name).ThenBy(l => l.LocationId).Skip(skip).Take(take).ToListAsync(ct);
        var items = new List<ConsigneeNoticeConsigneeDto>(page.Count);
        foreach (var l in page)
        {
            var (mail, phone) = await ContactsAsync(l.LocationId, ct);
            items.Add(new ConsigneeNoticeConsigneeDto(l.PublicId, l.Name, l.City, mail, phone, l.NoticeEmailOptIn, l.NoticeSmsOptIn));
        }
        return new PagedResult<ConsigneeNoticeConsigneeDto>(items, total, skip, take);
    }

    /// <summary>El correo y el teléfono principales de una localización (los contactos activos, el principal primero). El teléfono sale en E.164 («+17875551234»: prefijo + dígitos), que es lo que piden el SMS y la baja del aviso.</summary>
    public async Task<(string? Email, string? Phone)> ContactsAsync(int locationId, CancellationToken ct)
    {
        var ownerId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.Location, ct);
        var rows = await (from c in db.ContactPoints.AsNoTracking()
                          join t in db.LookupCodes.AsNoTracking() on c.ContactTypeLookupId equals t.LookupCodeId
                          where c.OwnerEntityLookupId == ownerId && c.OwnerId == locationId && c.IsActive
                          orderby c.IsPrimary descending, c.ContactPointId
                          select new { c.Value, c.CountryCallingCode, Type = t.InternalCode }).ToListAsync(ct);
        var mail = rows.FirstOrDefault(r => r.Type == "EMAIL")?.Value?.Trim();
        var phoneRow = rows.FirstOrDefault(r => r.Type is "MOBILE" or "PHONE" or "WHATSAPP");
        string? phone = null;
        if (phoneRow is not null && !string.IsNullOrWhiteSpace(phoneRow.Value))
        {
            var tenantId = T.RequireTenantId();
            var defaultCc = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.PhoneCountryCode).FirstOrDefaultAsync(ct) ?? "+1";
            phone = PhoneRules.ToE164(phoneRow.CountryCallingCode, phoneRow.Value, PhoneRules.NormalizeCountry(defaultCc) ?? "1");
        }
        return (string.IsNullOrEmpty(mail) ? null : mail, string.IsNullOrEmpty(phone) ? null : phone);
    }

    /// <summary>
    /// Marca o desmarca que un consignatario acepta avisos por un medio. Marcar exige tener prendido el aviso, aceptar la advertencia y que el consignatario tenga ese medio registrado; queda en la bitácora y se le avisa al consignatario
    /// (por correo; el texto queda anotado hasta contratar el proveedor). Un contacto que se dio de baja no se puede volver a marcar.
    /// </summary>
    public async Task<ConsigneeNoticeConsigneeDto> SetConsigneeAsync(Guid locationPublicId, ConsigneeNoticeMarkRequest? req, CancellationToken ct)
    {
        await EnsureCanEditAsync(ct);
        var channel = ConsigneeNoticeChannels.Normalize(req?.Channel) ?? throw new ValidationException("channel", ConsigneeNoticeRules.InvalidChannelMessage);
        if (req?.Accepted is not bool accepted) throw new ValidationException("accepted", "Indique si el consignatario acepta o no los avisos.");
        var cid = ClientId;
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == cid, ct) ?? throw new NotFoundException("Cliente");
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.PublicId == locationPublicId && l.ClientId == cid && l.IsActive, ct) ?? throw new NotFoundException(ConsigneeNoticeRules.ConsigneeNotFoundMessage);
        var (mail, phone) = await ContactsAsync(loc.LocationId, ct);
        var contact = channel == ConsigneeNoticeChannels.Email ? mail : phone;
        var current = channel == ConsigneeNoticeChannels.Email ? loc.NoticeEmailOptIn : loc.NoticeSmsOptIn;

        if (accepted)
        {
            if (!client.NotifyConsignee) throw new StatusRuleException(ConsigneeNoticeRules.NotEnabledMessage);
            if (req.WarningAccepted != true) throw new ValidationException("warningAccepted", ConsigneeNoticeRules.WarningRequiredMessage);
            if (contact is null) throw new ValidationException("channel", channel == ConsigneeNoticeChannels.Email ? ConsigneeNoticeRules.NoEmailMessage : ConsigneeNoticeRules.NoPhoneMessage);
            if (await IsUnsubscribedAsync(channel, contact, ct)) throw new ConflictException(CannotMarkUnsubscribedMessage);
        }
        if (accepted == current) return new ConsigneeNoticeConsigneeDto(loc.PublicId, loc.Name, loc.City, mail, phone, loc.NoticeEmailOptIn, loc.NoticeSmsOptIn);

        if (channel == ConsigneeNoticeChannels.Email) loc.NoticeEmailOptIn = accepted; else loc.NoticeSmsOptIn = accepted;
        db.ConsigneeNoticeConsents.Add(new ConsigneeNoticeConsent
        {
            TenantId = T.RequireTenantId(), ClientId = cid, LocationId = loc.LocationId, Channel = channel, Accepted = accepted, Contact = contact ?? string.Empty,
            WarningAccepted = accepted && req.WarningAccepted == true, Source = ConsigneeNoticeSources.Client, ChangedBy = tenant.UserId, ChangedAtUtc = clock.UtcNow,
        });
        if (accepted && contact is not null)
            QueueNotice(new ConsigneeNotice { ClientId = cid, LocationId = loc.LocationId, Kind = ConsigneeNoticeKinds.Consent, Channel = channel, Contact = contact, Lang = LangOf(client.NoticeLang) }, channel == ConsigneeNoticeChannels.Sms);
        await db.SaveChangesAsync(ct);
        return new ConsigneeNoticeConsigneeDto(loc.PublicId, loc.Name, loc.City, mail, phone, loc.NoticeEmailOptIn, loc.NoticeSmsOptIn);
    }

    // ================================================================ cuándo se avisa

    private string LangOf(string? clientLang) => clientLang is "en" or "es" ? clientLang : "es";

    private async Task<string> LangOfAsync(string? clientLang, CancellationToken ct)
    {
        if (clientLang is "en" or "es") return clientLang;
        var tenantLang = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == T.RequireTenantId()).Select(t => t.DefaultLangCode).FirstOrDefaultAsync(ct);
        return tenantLang?.Trim() == "en" ? "en" : "es";
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Encola un aviso (no guarda). Los de texto quedan SKIPPED hasta contratar el proveedor.</summary>
    private void QueueNotice(ConsigneeNotice n, bool skipped)
    {
        n.TenantId = T.RequireTenantId();
        n.Token = NewToken();
        n.CreatedAtUtc = clock.UtcNow;
        n.Status = skipped ? ConsigneeNoticeStatuses.Skipped : ConsigneeNoticeStatuses.Pending;
        n.Message = skipped ? SmsNotConfiguredMessage : null;
        n.NextAttemptUtc = skipped ? null : clock.UtcNow;
        db.ConsigneeNotices.Add(n);
    }

    public async Task<bool> IsUnsubscribedAsync(string channel, string contact, CancellationToken ct)
    {
        var c = contact.Trim().ToLowerInvariant();
        var local = db.ConsigneeUnsubscribes.Local.Any(u => u.Channel == channel && u.Contact == c);
        return local || await db.ConsigneeUnsubscribes.AsNoTracking().AnyAsync(u => u.Channel == channel && u.Contact == c, ct);
    }

    /// <summary>
    /// Anota un aviso para el consignatario de la orden cuando llega a un estatus que se avisa (sale En ruta, intento fallido o estatus final) y el cliente tiene prendido el aviso y marcó al consignatario. No guarda (va en la unidad de
    /// trabajo del cambio de estatus). Crea el token de seguimiento de la orden la primera vez.
    /// </summary>
    public async Task RecordStatusAsync(int orderId, string statusCode, CancellationToken ct)
    {
        if (!ConsigneeNoticeRules.ShouldNotify(statusCode)) return;
        var order = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == orderId) ?? await db.TransportOrders.AsTracking().FirstOrDefaultAsync(o => o.TransportOrderId == orderId, ct);
        if (order is null) return;
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == order.ClientId && c.NotifyConsignee).Select(c => new { c.NoticeLang }).FirstOrDefaultAsync(ct);
        if (client is null) return;
        var locationId = await db.OrderStops.AsNoTracking().Where(s => s.TransportOrderId == orderId && s.LocationId != null).OrderByDescending(s => s.Sequence).Select(s => s.LocationId).FirstOrDefaultAsync(ct);
        if (locationId is null) return;
        var loc = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.LocationId == locationId.Value, ct);
        if (loc is null || (!loc.NoticeEmailOptIn && !loc.NoticeSmsOptIn)) return;
        var (mail, phone) = await ContactsAsync(loc.LocationId, ct);
        var lang = await LangOfAsync(client.NoticeLang, ct);
        var queued = false;
        if (loc.NoticeEmailOptIn && mail is not null && !await IsUnsubscribedAsync(ConsigneeNoticeChannels.Email, mail, ct))
        {
            QueueNotice(new ConsigneeNotice { ClientId = order.ClientId, LocationId = loc.LocationId, TransportOrderId = orderId, Kind = ConsigneeNoticeKinds.Status, Channel = ConsigneeNoticeChannels.Email, Contact = mail, StatusCode = statusCode, Lang = lang }, skipped: false);
            queued = true;
        }
        if (loc.NoticeSmsOptIn && phone is not null && !await IsUnsubscribedAsync(ConsigneeNoticeChannels.Sms, phone, ct))
        {
            QueueNotice(new ConsigneeNotice { ClientId = order.ClientId, LocationId = loc.LocationId, TransportOrderId = orderId, Kind = ConsigneeNoticeKinds.Status, Channel = ConsigneeNoticeChannels.Sms, Contact = phone, StatusCode = statusCode, Lang = lang }, skipped: true);
            queued = true;
        }
        if (queued && string.IsNullOrEmpty(order.TrackToken)) order.TrackToken = NewToken();
    }

    // ================================================================ mandar los pendientes (el trabajo)

    /// <summary>Manda los avisos pendientes que ya tocan; reintenta a 1, 5 y 30 minutos. Devuelve (enviados, omitidos, fallidos en esta pasada).</summary>
    public async Task<(int Sent, int Skipped, int Failed)> DispatchAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.ConsigneeNotices.AsTracking().Where(n => n.Status == ConsigneeNoticeStatuses.Pending && n.NextAttemptUtc != null && n.NextAttemptUtc <= now).OrderBy(n => n.ConsigneeNoticeId).Take(200).ToListAsync(ct);
        if (due.Count == 0) return (0, 0, 0);
        var company = (await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == T.RequireTenantId()).Select(t => t.Name).FirstOrDefaultAsync(ct)) ?? "Teikem";
        var baseUrl = config[BaseUrlKey]?.Trim().TrimEnd('/');
        var clientIds = due.Select(n => n.ClientId).Distinct().ToList();
        var clientNames = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var orderIds = due.Where(n => n.TransportOrderId != null).Select(n => n.TransportOrderId!.Value).Distinct().ToList();
        var orders = await db.TransportOrders.AsNoTracking().Where(o => orderIds.Contains(o.TransportOrderId)).Select(o => new { o.TransportOrderId, o.WaybillNumber, o.TrackToken }).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var locIds = due.Select(n => n.LocationId).Distinct().ToList();
        var locs = await db.Locations.AsNoTracking().Where(l => locIds.Contains(l.LocationId)).ToDictionaryAsync(l => l.LocationId, ct);
        int sent = 0, skipped = 0, failed = 0;
        foreach (var n in due)
        {
            if (!email.IsConfigured) { Skip(n, EmailNotConfiguredMessage); skipped++; continue; }
            if (string.IsNullOrEmpty(baseUrl)) { Skip(n, NoBaseUrlMessage); skipped++; continue; }
            if (await IsUnsubscribedAsync(n.Channel, n.Contact, ct)) { Skip(n, UnsubscribedMessage); skipped++; continue; }
            if (locs.TryGetValue(n.LocationId, out var loc) && n.Channel == ConsigneeNoticeChannels.Email && !loc.NoticeEmailOptIn) { Skip(n, "El consignatario ya no está marcado para recibir avisos."); skipped++; continue; }
            n.Attempts++;
            try
            {
                var clientName = clientNames.GetValueOrDefault(n.ClientId, company);
                var (subject, html) = Compose(n, company, clientName, baseUrl, n.TransportOrderId is int oid ? orders.GetValueOrDefault(oid)?.WaybillNumber : null, n.TransportOrderId is int oid2 ? orders.GetValueOrDefault(oid2)?.TrackToken : null);
                await email.SendAsync(n.Contact, null, subject, html, ct, company);
                (n.Status, n.SentAtUtc, n.NextAttemptUtc, n.Message) = (ConsigneeNoticeStatuses.Sent, now, null, null);
                sent++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                n.Message = ex.Message.Length <= 400 ? ex.Message : ex.Message[..400];
                if (n.Attempts >= ConsigneeNoticeRules.MaxAttempts) (n.Status, n.NextAttemptUtc) = (ConsigneeNoticeStatuses.Failed, null);
                else n.NextAttemptUtc = now + ConsigneeNoticeRules.RetryDelays[Math.Min(n.Attempts - 1, ConsigneeNoticeRules.RetryDelays.Length - 1)];
                failed++;
            }
        }
        await db.SaveChangesAsync(ct);
        return (sent, skipped, failed);
    }

    private static void Skip(ConsigneeNotice n, string message) => (n.Status, n.Message, n.NextAttemptUtc) = (ConsigneeNoticeStatuses.Skipped, message, null);

    private static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>El asunto y el cuerpo del aviso en el idioma del cliente: qué pasó, el enlace de seguimiento (de estatus) y siempre el enlace de baja.</summary>
    public static (string Subject, string Html) Compose(ConsigneeNotice n, string company, string clientName, string baseUrl, string? waybill, string? orderToken)
    {
        var en = n.Lang == "en";
        var unsubscribe = $"{baseUrl}/unsubscribe/{n.Token}";
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1b2430;font-size:14px\">");
        string subject;
        if (n.Kind == ConsigneeNoticeKinds.Consent)
        {
            subject = ConsigneeNoticeRules.ConsentSubject(clientName, n.Lang);
            sb.Append($"<h2 style=\"margin:0 0 8px\">{E(subject)}</h2>");
            sb.Append(en
                ? $"<p>{E(clientName)} marked you to receive notices about your shipments at this address, sent by {E(company)}. You will get one when a package is on its way, has a failed attempt or reaches a final status.</p>"
                : $"<p>{E(clientName)} lo marcó para que reciba avisos sobre sus envíos en esta dirección, enviados por {E(company)}. Recibirá uno cuando un paquete vaya en camino, tenga un intento fallido o llegue a un estatus final.</p>");
        }
        else
        {
            subject = ConsigneeNoticeRules.Subject(n.StatusCode ?? string.Empty, company, n.Lang);
            sb.Append($"<h2 style=\"margin:0 0 8px\">{E(subject)}</h2>");
            sb.Append(en ? $"<p>Package of {E(clientName)}{(waybill is null ? string.Empty : $" ({E(waybill)})")}.</p>" : $"<p>Paquete de {E(clientName)}{(waybill is null ? string.Empty : $" ({E(waybill)})")}.</p>");
            if (!string.IsNullOrEmpty(orderToken))
            {
                var link = $"{baseUrl}/track/{orderToken}";
                sb.Append(en ? $"<p><a href=\"{link}\">Follow your package</a></p>" : $"<p><a href=\"{link}\">Siga su paquete</a></p>");
            }
        }
        sb.Append(en
            ? $"<p style=\"color:#5b6573;font-size:12px;margin-top:18px\">If you do not want to receive these notices: <a href=\"{unsubscribe}\">unsubscribe</a>.</p>"
            : $"<p style=\"color:#5b6573;font-size:12px;margin-top:18px\">Si no desea recibir más avisos: <a href=\"{unsubscribe}\">darse de baja</a>.</p>");
        sb.Append($"<p style=\"color:#5b6573;margin:0\">{E(company)}</p></div>");
        return (subject, sb.ToString());
    }

    // ================================================================ baja del consignatario (sin sesión)

    /// <summary>
    /// El consignatario se da de baja con el enlace de un aviso: ese medio no recibe nada más de la compañía y deja de estar marcado donde estuviera. Queda en la bitácora. 404 con el mensaje genérico si el enlace no existe. Es idempotente.
    /// </summary>
    public async Task UnsubscribeAsync(string? token, CancellationToken ct)
    {
        var t = token?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 64) throw new ConsigneeLinkNotFoundException(ConsigneeNoticeRules.UnsubscribeNotFoundMessage);
        var notice = await db.ConsigneeNotices.AsNoTracking().IgnoreQueryFilters().Where(n => n.Token == t).Select(n => new { n.TenantId, n.ClientId, n.LocationId, n.Channel, n.Contact }).FirstOrDefaultAsync(ct)
                     ?? throw new ConsigneeLinkNotFoundException(ConsigneeNoticeRules.UnsubscribeNotFoundMessage);
        using (T.AsAnonymous(notice.TenantId))
        {
            var contact = notice.Contact.Trim().ToLowerInvariant();
            if (!await db.ConsigneeUnsubscribes.AnyAsync(u => u.Channel == notice.Channel && u.Contact == contact, ct))
                db.ConsigneeUnsubscribes.Add(new ConsigneeUnsubscribe { TenantId = notice.TenantId, Channel = notice.Channel, Contact = contact, AtUtc = clock.UtcNow });
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.LocationId == notice.LocationId, ct);
            var was = loc is not null && (notice.Channel == ConsigneeNoticeChannels.Email ? loc.NoticeEmailOptIn : loc.NoticeSmsOptIn);
            if (loc is not null)
            {
                if (notice.Channel == ConsigneeNoticeChannels.Email) loc.NoticeEmailOptIn = false; else loc.NoticeSmsOptIn = false;
            }
            if (was)
                db.ConsigneeNoticeConsents.Add(new ConsigneeNoticeConsent
                {
                    TenantId = notice.TenantId, ClientId = notice.ClientId, LocationId = notice.LocationId, Channel = notice.Channel, Accepted = false, Contact = notice.Contact,
                    WarningAccepted = false, Source = ConsigneeNoticeSources.Consignee, ChangedAtUtc = clock.UtcNow,
                });
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>404 de un enlace que no existe (de baja o de seguimiento): el mensaje es el mismo exista o no (sin oráculo).</summary>
public sealed class ConsigneeLinkNotFoundException(string message) : TeikemException(message, 404, "not_found");

/// <summary>Lote 116 — efecto del dominio OrderStatus: la orden que sale En ruta, tiene un intento fallido o llega a un estatus final se anota como aviso para su consignatario (si el cliente lo prendió y lo marcó). Los estatus brincados no se avisan. No guarda.</summary>
public sealed class ConsigneeNoticeOrderEffect(ConsigneeNoticeService notices) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        if (context.Skipped) return;   // Lote 107/116: los estatus brincados no se avisan
        await notices.RecordStatusAsync(context.EntityId, context.To.InternalCode, ct);
    }
}
