using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Cod;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.ExitNotices;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 98 (P10) — avisos al cliente (el sender; nunca al consignatario). La COMPAÑÍA decide, por cliente y por aviso, qué canales se encienden (sin configuración = apagado). Los hechos (orden recogida, en ruta o
/// entregada; remesa COD remitida; factura emitida) se anotan SOLO si el cliente tiene ese aviso encendido; el trabajo CLIENT_NOTIFICATION_DIGEST los junta en RESÚMENES (un aviso por cliente y ruta; uno por
/// remesa; uno por factura) cuando el grupo lleva unos minutos sin hechos nuevos, y manda cada resumen por correo (a los usuarios activos del portal del cliente y a los correos extra), lo deja en la bandeja del
/// portal (canal APP) y guarda la preferencia de SMS (no sale hasta contratar el proveedor). Los envíos fallidos se reintentan a 1, 5 y 30 minutos.
/// </summary>
public sealed class ClientNotificationService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ITransactionalEmailSender email)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) };

    // ================================================================ configuración (la decide la compañía)

    public async Task<ClientNotificationSettingsDto> GetSettingsAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        var rows = await db.Set<ClientNotificationSetting>().AsNoTracking().Where(s => s.ClientId == client.ClientId).ToListAsync(ct);
        var list = ClientNotificationKinds.All.Select(k =>
        {
            var r = rows.FirstOrDefault(x => x.Kind == k);
            return new ClientNotificationSettingDto(k, r?.EmailEnabled ?? false, r?.AppEnabled ?? false, r?.SmsEnabled ?? false, r?.ExtraEmails, r?.ExtraPhones);
        }).ToList();
        return new ClientNotificationSettingsDto(client.PublicId, client.Name, (await PortalEmailsAsync(client.ClientId, null, ct)).Count, email.IsConfigured, false, list, client.NoticeDigestHour ?? ClientNotificationRules.DefaultDigestHour);
    }

    /// <summary>Prefijo internacional por omisión de la compañía en dígitos («1»).</summary>
    private async Task<string> DefaultCountryAsync(CancellationToken ct)
    {
        var tenantId = T.RequireTenantId();
        var cc = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.PhoneCountryCode).FirstOrDefaultAsync(ct) ?? "+1";
        return PhoneRules.NormalizeCountry(cc) ?? "1";
    }

    public async Task<ClientNotificationSettingsDto> SaveSettingsAsync(Guid clientPublicId, ClientNotificationSettingsRequest? req, CancellationToken ct)
    {
        if (req?.Settings is null) throw new ValidationException("settings", "El cuerpo de la solicitud es obligatorio.");
        if (req.DigestHour is int digestHour && (digestHour < 0 || digestHour > 23)) throw new ValidationException("digestHour", ClientNotificationRules.DigestHourInvalid);
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        var kinds = req.Settings.Select(s => s.Kind?.Trim().ToUpperInvariant() ?? string.Empty).ToList();
        if (kinds.Any(k => !ClientNotificationKinds.IsKnown(k))) throw new ValidationException("settings", ClientNotificationRules.KindInvalid);
        if (kinds.Distinct().Count() != kinds.Count) throw new ValidationException("settings", ClientNotificationRules.DuplicateKind);
        // validación de todos antes de guardar nada
        var cleaned = new List<(string Kind, ClientNotificationSettingDto S, string? Emails, string? Phones)>();
        var defaultCc = await DefaultCountryAsync(ct);
        foreach (var s in req.Settings)
        {
            var kind = s.Kind!.Trim().ToUpperInvariant();
            if ((s.ExtraEmails ?? string.Empty).Length > ClientNotificationRules.MaxListLength) throw new ValidationException("extraEmails", ClientNotificationRules.ExtraEmailsTooLong);
            if ((s.ExtraPhones ?? string.Empty).Length > ClientNotificationRules.MaxListLength) throw new ValidationException("extraPhones", ClientNotificationRules.ExtraPhonesTooLong);
            IReadOnlyList<string> emails, phones;
            try { emails = ClientNotificationRules.ParseEmails(s.ExtraEmails); } catch (FormatException ex) { throw new ValidationException("extraEmails", ex.Message); }
            try { phones = ClientNotificationRules.ParsePhones(s.ExtraPhones); } catch (FormatException ex) { throw new ValidationException("extraPhones", ex.Message); }
            // los teléfonos extra se guardan con su prefijo internacional (E.164, «+17875550100»): el de la compañía si no traen otro
            phones = phones.Select(p => PhoneRules.ToE164Text(p, defaultCc)).Distinct().ToList();
            cleaned.Add((kind, s, emails.Count == 0 ? null : string.Join(", ", emails), phones.Count == 0 ? null : string.Join(", ", phones)));
        }
        var tenantId = T.RequireTenantId();
        var existing = await db.Set<ClientNotificationSetting>().AsTracking().Where(x => x.ClientId == client.ClientId).ToListAsync(ct);
        if (req.DigestHour is int hour)
        {
            var tracked = await db.Set<Client>().AsTracking().FirstAsync(c => c.ClientId == client.ClientId, ct);
            tracked.NoticeDigestHour = hour;
        }
        foreach (var (kind, s, emails, phones) in cleaned)
        {
            var row = existing.FirstOrDefault(x => x.Kind == kind);
            if (row is null)
            {
                if (!s.EmailEnabled && !s.AppEnabled && !s.SmsEnabled && emails is null && phones is null) continue;   // apagado y sin nada: no hace falta fila
                row = new ClientNotificationSetting { TenantId = tenantId, ClientId = client.ClientId, Kind = kind };
                db.Set<ClientNotificationSetting>().Add(row);
            }
            (row.EmailEnabled, row.AppEnabled, row.SmsEnabled, row.ExtraEmails, row.ExtraPhones) = (s.EmailEnabled, s.AppEnabled, s.SmsEnabled, emails, phones);
            (row.UpdatedAtUtc, row.UpdatedBy) = (clock.UtcNow, tenant.UserId);
        }
        await db.SaveGuardedAsync(ClientNotificationRules.Concurrency, ct);
        return await GetSettingsAsync(clientPublicId, ct);
    }

    private async Task<List<string>> PortalEmailsAsync(int clientId, string? kind, CancellationToken ct)
    {
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.PortalUserStatus && s.InternalCode == PortalUserStatuses.Active).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
        if (activeId is null) return new List<string>();
        // Lote 119: la persona que apagó el correo de este aviso en sus preferencias no lo recibe (la compañía manda: solo se apaga, nunca se enciende)
        var optedOut = db.Set<PortalNoticePref>().AsNoTracking().Where(x => kind != null && x.Kind == kind && !x.EmailEnabled).Select(x => x.PortalUserId);
        return await db.Set<PortalUser>().AsNoTracking().Where(p => p.ClientId == clientId && p.IsActive && p.StatusCodeId == activeId && p.Email != "" && !optedOut.Contains(p.PortalUserId)).Select(p => p.Email).ToListAsync(ct);
    }

    // ================================================================ capturar hechos (solo si el cliente tiene el aviso encendido)

    private async Task<bool> EnabledAsync(int clientId, string kind, CancellationToken ct)
    {
        var local = db.Set<ClientNotificationSetting>().Local.FirstOrDefault(s => s.ClientId == clientId && s.Kind == kind);
        if (local is not null) return local.AnyChannel;
        return await db.Set<ClientNotificationSetting>().AsNoTracking().AnyAsync(s => s.ClientId == clientId && s.Kind == kind && (s.EmailEnabled || s.AppEnabled || s.SmsEnabled), ct);
    }

    /// <summary>Anota que una orden se recogió, salió a entrega o se entregó. No guarda (va en la unidad de trabajo del cambio de estatus).</summary>
    public async Task RecordOrderEventAsync(int orderId, string kind, CancellationToken ct)
    {
        var order = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == orderId)
                    ?? await db.TransportOrders.AsNoTracking().FirstOrDefaultAsync(o => o.TransportOrderId == orderId, ct);
        if (order is null || !await EnabledAsync(order.ClientId, kind, ct)) return;
        var tripId = await db.TripOrders.AsNoTracking().Where(x => x.TransportOrderId == orderId && x.IsCurrent).Select(x => (int?)x.TripId).FirstOrDefaultAsync(ct);
        db.Set<ClientNotificationEvent>().Add(new ClientNotificationEvent
        {
            TenantId = order.TenantId, ClientId = order.ClientId, Kind = kind, TransportOrderId = orderId, TripId = tripId, Pieces = order.TotalPieces ?? 0, OccurredAtUtc = clock.UtcNow,
        });
    }

    /// <summary>Anota una remesa COD remitida o una factura emitida (RefId = la remesa o la factura). No guarda.</summary>
    public async Task RecordRefEventAsync(int clientId, string kind, int refId, CancellationToken ct)
    {
        if (!await EnabledAsync(clientId, kind, ct)) return;
        db.Set<ClientNotificationEvent>().Add(new ClientNotificationEvent { TenantId = T.RequireTenantId(), ClientId = clientId, Kind = kind, RefId = refId, OccurredAtUtc = clock.UtcNow });
    }

    // ================================================================ resumir y enviar (el trabajo)

    private sealed record Group(int ClientId, string Kind, int? RefId, List<ClientNotificationEvent> Events);

    /// <summary>
    /// El último momento de salida del resumen del día de un cliente: la última vez que el reloj de la compañía marcó la hora que el cliente escogió. Lo que ocurrió hasta ese momento sale ahora; lo posterior
    /// espera al día siguiente. Si el trabajo se atrasó o no corrió, no se pierde nada: en la pasada siguiente sale lo que quedó.
    /// </summary>
    public static DateTime LastDigestMomentUtc(DateTime nowUtc, int hour, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var at = DateTime.SpecifyKind(local.Date.AddHours(hour), DateTimeKind.Unspecified);
        if (local < at) at = at.AddDays(-1);
        if (zone.IsInvalidTime(at)) at = at.AddHours(1);   // la hora que el cambio de horario se saltó
        return TimeZoneInfo.ConvertTimeToUtc(at, zone);
    }

    /// <summary>
    /// Arma los avisos que ya toca mandar y manda los pendientes. Remesas COD y facturas: un aviso por documento cuando el grupo se asentó. Transporte y almacén: UN solo resumen por cliente y día, a la hora que el cliente
    /// escogió, con una sección por cada aviso que tenga encendido; si no hubo nada, no sale nada. Devuelve (resúmenes creados, envíos hechos, envíos que fallaron en esta pasada).
    /// </summary>
    public async Task<(int Summaries, int Sent, int Failed)> DigestAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var summaries = 0;
        var pending = await db.Set<ClientNotificationEvent>().AsTracking().Where(e => e.NotificationId == null).OrderBy(e => e.ClientNotificationEventId).ToListAsync(ct);
        if (pending.Count > 0)
        {
            var lang = (await db.Tenants.AsNoTracking().Where(t => t.TenantId == T.RequireTenantId()).Select(t => t.DefaultLangCode).FirstOrDefaultAsync(ct)) ?? "es";

            // 1) Remesas COD y facturas: un aviso por documento
            var documents = pending.Where(e => !ClientNotificationKinds.IsDaily(e.Kind))
                .GroupBy(e => (e.ClientId, e.Kind, e.RefId))
                .Select(g => new Group(g.Key.ClientId, g.Key.Kind, g.Key.RefId, g.ToList()))
                .Where(g => g.Events.Max(e => e.OccurredAtUtc) <= now.AddMinutes(-ClientNotificationRules.SettleMinutes))
                .ToList();
            foreach (var g in documents)
            {
                var setting = await db.Set<ClientNotificationSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == g.ClientId && s.Kind == g.Kind, ct);
                if (setting is null || !setting.AnyChannel)
                {
                    db.Set<ClientNotificationEvent>().RemoveRange(g.Events);   // lo apagaron mientras tanto: no hay nada que avisar
                    continue;
                }
                var notification = await BuildAsync(g, setting, lang, ct);
                db.Set<ClientNotification>().Add(notification);
                await db.SaveChangesAsync(ct);
                foreach (var e in g.Events) e.NotificationId = notification.ClientNotificationId;
                await AddDeliveriesAsync(notification, new[] { setting }, ct);
                await db.SaveChangesAsync(ct);
                summaries++;
            }

            // 2) El resumen del día de cada cliente (transporte y almacén)
            foreach (var byClient in pending.Where(e => ClientNotificationKinds.IsDaily(e.Kind)).GroupBy(e => e.ClientId))
            {
                var hour = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == byClient.Key).Select(c => c.NoticeDigestHour).FirstOrDefaultAsync(ct) ?? ClientNotificationRules.DefaultDigestHour;
                var dueUtc = LastDigestMomentUtc(now, hour, clock.Zone);
                var due = byClient.Where(e => e.OccurredAtUtc <= dueUtc).ToList();
                if (due.Count == 0) continue;   // nada para esta hora (lo de hoy espera a la siguiente)
                var settings = await db.Set<ClientNotificationSetting>().AsNoTracking().Where(s => s.ClientId == byClient.Key && ClientNotificationKinds.DailyKinds.Contains(s.Kind)).ToListAsync(ct);
                var on = settings.Where(s => s.AnyChannel).ToList();
                var onKinds = on.Select(s => s.Kind).ToHashSet();
                db.Set<ClientNotificationEvent>().RemoveRange(due.Where(e => !onKinds.Contains(e.Kind)));   // lo apagaron mientras tanto: no hay nada que avisar
                var keep = due.Where(e => onKinds.Contains(e.Kind)).ToList();
                if (keep.Count == 0) continue;
                var notification = await BuildDailyAsync(byClient.Key, keep, on, lang, ct);
                db.Set<ClientNotification>().Add(notification);
                await db.SaveChangesAsync(ct);
                foreach (var e in keep) e.NotificationId = notification.ClientNotificationId;
                await AddDeliveriesAsync(notification, on, ct);
                await db.SaveChangesAsync(ct);
                summaries++;
            }
            await db.SaveChangesAsync(ct);   // también las bajas de los hechos de avisos apagados
        }
        var (sent, failed) = await SendPendingAsync(now, ct);
        return (summaries, sent, failed);
    }

    /// <summary>Los envíos de un aviso: correo (usuarios activos del portal que no lo apagaron + correos extra de cada aviso incluido), la bandeja del portal (en el aviso) y SMS (se guarda; no sale hasta contratar el proveedor).</summary>
    private async Task AddDeliveriesAsync(ClientNotification n, IReadOnlyList<ClientNotificationSetting> settings, CancellationToken ct)
    {
        void Add(string channel, string recipient, string status, string? message) => db.Set<ClientNotificationDelivery>().Add(new ClientNotificationDelivery
        {
            TenantId = n.TenantId, ClientNotificationId = n.ClientNotificationId, Channel = channel, Recipient = recipient, Status = status, Message = message,
            NextAttemptUtc = status == ClientNotificationDeliveryStatuses.Pending ? clock.UtcNow : null,
        });
        if (settings.Any(s => s.EmailEnabled))
        {
            var to = new List<string>();
            foreach (var s in settings.Where(x => x.EmailEnabled)) to.AddRange((await PortalEmailsAsync(n.ClientId, s.Kind, ct)).Concat(ClientNotificationRules.ParseEmails(s.ExtraEmails)));
            to = to.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (to.Count == 0) Add(ClientNotificationChannels.Email, string.Empty, ClientNotificationDeliveryStatuses.Skipped, "No hay destinatarios: el cliente no tiene usuarios activos en el portal ni correos extra.");
            foreach (var r in to) Add(ClientNotificationChannels.Email, r, ClientNotificationDeliveryStatuses.Pending, null);
        }
        if (settings.Any(s => s.SmsEnabled))
        {
            var phones = settings.Where(x => x.SmsEnabled).SelectMany(x => ClientNotificationRules.ParsePhones(x.ExtraPhones)).Distinct().ToList();
            if (phones.Count == 0) Add(ClientNotificationChannels.Sms, string.Empty, ClientNotificationDeliveryStatuses.Skipped, "No hay teléfonos para el SMS.");
            foreach (var p in phones) Add(ClientNotificationChannels.Sms, p, ClientNotificationDeliveryStatuses.Skipped, "El envío de SMS todavía no está configurado.");
        }
    }

    private async Task<(int Sent, int Failed)> SendPendingAsync(DateTime now, CancellationToken ct)
    {
        var due = await db.Set<ClientNotificationDelivery>().AsTracking().Where(d => d.Status == ClientNotificationDeliveryStatuses.Pending && d.NextAttemptUtc != null && d.NextAttemptUtc <= now).Take(200).ToListAsync(ct);
        if (due.Count == 0) return (0, 0);
        var ids = due.Select(d => d.ClientNotificationId).Distinct().ToList();
        var notes = await db.Set<ClientNotification>().AsNoTracking().Where(n => ids.Contains(n.ClientNotificationId)).ToDictionaryAsync(n => n.ClientNotificationId, ct);
        var company = (await db.Tenants.AsNoTracking().Where(t => t.TenantId == T.RequireTenantId()).Select(t => t.Name).FirstOrDefaultAsync(ct)) ?? "Teikem";
        int sent = 0, failed = 0;
        foreach (var d in due)
        {
            var n = notes[d.ClientNotificationId];
            if (!email.IsConfigured)
            {
                (d.Status, d.Message, d.NextAttemptUtc) = (ClientNotificationDeliveryStatuses.Skipped, "El envío de correo no está configurado.", null);
                continue;
            }
            d.Attempts++;
            try
            {
                await email.SendAsync(d.Recipient, null, n.Subject, Html(company, n), ct, company);
                (d.Status, d.SentAtUtc, d.NextAttemptUtc, d.Message) = (ClientNotificationDeliveryStatuses.Sent, now, null, null);
                sent++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failed++;
                d.Message = Trim(ex.Message, 400);
                if (d.Attempts > RetryDelays.Length) (d.Status, d.NextAttemptUtc) = (ClientNotificationDeliveryStatuses.Failed, null);
                else d.NextAttemptUtc = now + RetryDelays[d.Attempts - 1];
            }
        }
        await db.SaveChangesAsync(ct);
        return (sent, failed);
    }

    // ================================================================ el texto del resumen

    private static string Named(IReadOnlyList<string> items, bool en)
    {
        var shown = items.Take(ClientNotificationRules.MaxNamedInSummary).ToList();
        var rest = items.Count - shown.Count;
        return string.Join(", ", shown) + (rest > 0 ? (en ? $" and {rest} more" : $" y {rest} más") : string.Empty);
    }

    /// <summary>El texto de un aviso de documento: una remesa COD o una factura.</summary>
    private async Task<ClientNotification> BuildAsync(Group g, ClientNotificationSetting setting, string lang, CancellationToken ct)
    {
        var en = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo("en-US");
        string Money(decimal v) => v.ToString("C2", culture);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == g.ClientId, ct);
        string subject, body;
        var summary = new Dictionary<string, object?>();
        switch (g.Kind)
        {
            case ClientNotificationKinds.CodProcessed:
            {
                var b = await db.Set<CodRemittanceBatch>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.CodRemittanceBatchId == g.RefId, ct);
                var net = b is null ? 0m : b.GrossCollected - b.CommissionAmount;
                subject = en ? $"Your COD was processed — {client.Name}" : $"Se procesó su COD — {client.Name}";
                body = b is null ? (en ? "Your COD was processed." : "Se procesó su COD.")
                    : en ? $"COD remittance {b.Number}: collected {Money(b.GrossCollected)}, commission {Money(b.CommissionAmount)}, net {Money(net)} ({b.Lines.Select(l => l.TransportOrderId).Distinct().Count()} order(s))."
                         : $"Remesa de COD {b.Number}: cobrado {Money(b.GrossCollected)}, comisión {Money(b.CommissionAmount)}, neto {Money(net)} ({b.Lines.Select(l => l.TransportOrderId).Distinct().Count()} envío(s)).";
                summary["remittance"] = b?.Number; summary["gross"] = b?.GrossCollected; summary["commission"] = b?.CommissionAmount; summary["net"] = net;
                break;
            }
            default:
            {
                var inv = await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(x => x.InvoiceId == g.RefId, ct);
                subject = en ? $"New invoice — {client.Name}" : $"Nueva factura — {client.Name}";
                body = inv is null ? (en ? "A new invoice was issued." : "Se emitió una factura nueva.")
                    : en ? $"Invoice {inv.InvoiceNumber} for {Money(inv.Subtotal + inv.TaxAmount)}{(inv.DueDate is DateOnly d ? $", due {d:yyyy-MM-dd}" : "")}."
                         : $"Factura {inv.InvoiceNumber} por {Money(inv.Subtotal + inv.TaxAmount)}{(inv.DueDate is DateOnly d2 ? $", vence el {d2:yyyy-MM-dd}" : "")}.";
                summary["invoice"] = inv?.InvoiceNumber; summary["total"] = inv is null ? null : inv.Subtotal + inv.TaxAmount;
                break;
            }
        }
        return new ClientNotification
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), ClientId = g.ClientId, Kind = g.Kind, Subject = Trim(subject, 200), BodyText = Trim(body, 2000),
            SummaryJson = JsonSerializer.Serialize(summary), InPortal = setting.AppEnabled, CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>El resumen único del día de un cliente: una línea por cada aviso encendido que tuvo algo (recogido, en ruta, entregado, llegadas recibidas, salidas despachadas).</summary>
    private async Task<ClientNotification> BuildDailyAsync(int clientId, IReadOnlyList<ClientNotificationEvent> events, IReadOnlyList<ClientNotificationSetting> settings, string lang, CancellationToken ct)
    {
        var en = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo("en-US");
        string Money(decimal v) => v.ToString("C2", culture);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == clientId, ct);
        var lines = new List<string>();
        var summary = new Dictionary<string, object?>();

        async Task OrderLineAsync(string kind)
        {
            var mine = events.Where(e => e.Kind == kind).ToList();
            if (mine.Count == 0) return;
            var orderIds = mine.Where(e => e.TransportOrderId != null).Select(e => e.TransportOrderId!.Value).Distinct().ToList();
            var pieces = mine.Where(e => e.TransportOrderId != null).GroupBy(e => e.TransportOrderId).Sum(x => x.First().Pieces);
            var tripIds = mine.Where(e => e.TripId != null).Select(e => e.TripId!.Value).Distinct().ToList();
            var tripCodes = tripIds.Count == 0 ? new List<string>() : await db.Set<Domain.Trips.Trip>().AsNoTracking().Where(t => tripIds.Contains(t.TripId)).OrderBy(t => t.Code).Select(t => t.Code).ToListAsync(ct);
            summary[kind] = new { orders = orderIds.Count, pieces, routes = tripCodes };
            switch (kind)
            {
                case ClientNotificationKinds.PickedUp:
                    lines.Add(en ? $"Picked up: {pieces} package(s) from {orderIds.Count} order(s)." : $"Recogido: {pieces} caja(s) de {orderIds.Count} envío(s).");
                    break;
                case ClientNotificationKinds.OutForDelivery:
                {
                    var routes = tripCodes.Count == 0 ? string.Empty : en ? $" on {tripCodes.Count} route(s) ({Named(tripCodes, en)})" : $" en {tripCodes.Count} ruta(s) ({Named(tripCodes, en)})";
                    lines.Add(en ? $"Out for delivery: {pieces} package(s) from {orderIds.Count} order(s){routes}." : $"Salió a entrega: {pieces} caja(s) de {orderIds.Count} envío(s){routes}.");
                    break;
                }
                default:
                {
                    var voidId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CodCodes.CollectionStatusDomain && s.InternalCode == CodCollectionStatuses.Void).Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);
                    var cod = await db.Set<CodCollection>().AsNoTracking().Where(c => orderIds.Contains(c.TransportOrderId) && c.StatusCodeId != voidId).SumAsync(c => (decimal?)c.AmountCollected, ct) ?? 0m;
                    var codText = cod > 0 ? (en ? $" COD collected: {Money(cod)}." : $" COD cobrado: {Money(cod)}.") : string.Empty;
                    lines.Add(en ? $"Delivered: {pieces} package(s) from {orderIds.Count} order(s).{codText}" : $"Entregado: {pieces} caja(s) de {orderIds.Count} envío(s).{codText}");
                    summary[kind] = new { orders = orderIds.Count, pieces, routes = tripCodes, cod };
                    break;
                }
            }
        }

        await OrderLineAsync(ClientNotificationKinds.PickedUp);
        await OrderLineAsync(ClientNotificationKinds.OutForDelivery);
        await OrderLineAsync(ClientNotificationKinds.Delivered);

        var receivedIds = events.Where(e => e.Kind == ClientNotificationKinds.WhReceived && e.RefId != null).Select(e => e.RefId!.Value).Distinct().ToList();
        if (receivedIds.Count > 0)
        {
            var numbers = await db.Set<Asn>().AsNoTracking().Where(a => receivedIds.Contains(a.AsnId)).OrderBy(a => a.Number).Select(a => a.Number).ToListAsync(ct);
            var units = await db.Set<ReceiptLine>().AsNoTracking().Where(l => db.Set<ReceiptHeader>().Any(h => h.ReceiptHeaderId == l.ReceiptHeaderId && h.AsnId != null && receivedIds.Contains(h.AsnId.Value))).SumAsync(l => (decimal?)l.ReceivedQty, ct) ?? 0m;
            summary[ClientNotificationKinds.WhReceived] = new { notices = numbers, units };
            lines.Add(en ? $"Received at the warehouse: {numbers.Count} arrival notice(s) ({Named(numbers, en)}), {units:0.##} unit(s)." : $"Recibido en el almacén: {numbers.Count} aviso(s) de llegada ({Named(numbers, en)}), {units:0.##} unidad(es).");
        }
        var dispatchedIds = events.Where(e => e.Kind == ClientNotificationKinds.WhDispatched && e.RefId != null).Select(e => e.RefId!.Value).Distinct().ToList();
        if (dispatchedIds.Count > 0)
        {
            var numbers = await db.Set<ExitNotice>().AsNoTracking().Where(x => dispatchedIds.Contains(x.ExitNoticeId)).OrderBy(x => x.Number).Select(x => x.Number).ToListAsync(ct);
            var units = await db.Set<ExitNoticeLine>().AsNoTracking().Where(l => dispatchedIds.Contains(l.ExitNoticeId)).SumAsync(l => (decimal?)l.QtyShipped, ct) ?? 0m;
            summary[ClientNotificationKinds.WhDispatched] = new { notices = numbers, units };
            lines.Add(en ? $"Dispatched from the warehouse: {numbers.Count} exit notice(s) ({Named(numbers, en)}), {units:0.##} unit(s)." : $"Despachado del almacén: {numbers.Count} aviso(s) de salida ({Named(numbers, en)}), {units:0.##} unidad(es).");
        }

        return new ClientNotification
        {
            PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), ClientId = clientId, Kind = ClientNotificationKinds.Daily,
            Subject = Trim(en ? $"Your daily summary — {client.Name}" : $"Su resumen del día — {client.Name}", 200), BodyText = Trim(string.Join("\n", lines), 2000),
            SummaryJson = Trim(JsonSerializer.Serialize(summary), 1000), InPortal = settings.Any(x => x.AppEnabled), CreatedAtUtc = clock.UtcNow,
        };
    }

    private static string Html(string company, ClientNotification n)
    {
        static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1b2430;font-size:14px\">");
        var body = E(n.BodyText).Replace("\n", "<br>");
        sb.Append($"<h2 style=\"margin:0 0 8px\">{E(n.Subject)}</h2><p style=\"margin:0 0 14px\">{body}</p>");
        sb.Append($"<p style=\"color:#5b6573;margin:0\">{E(company)}</p></div>");
        return sb.ToString();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];

    // ================================================================ consulta

    public async Task<PagedResult<ClientNotificationDto>> ListAsync(Guid clientPublicId, int skip, int take, CancellationToken ct)
    {
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 25 : take, 1, MaxPageSize);
        var query = db.Set<ClientNotification>().AsNoTracking().Where(n => n.ClientId == client.ClientId);
        var total = await query.CountAsync(ct);
        var page = await query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.ClientNotificationId).Skip(skip).Take(take).ToListAsync(ct);
        var ids = page.Select(n => n.ClientNotificationId).ToList();
        var deliveries = await db.Set<ClientNotificationDelivery>().AsNoTracking().Where(d => ids.Contains(d.ClientNotificationId)).OrderBy(d => d.ClientNotificationDeliveryId).ToListAsync(ct);
        var items = page.Select(n => new ClientNotificationDto(n.PublicId, n.Kind, n.Subject, n.BodyText, n.InPortal, n.CreatedAtUtc,
            deliveries.Where(d => d.ClientNotificationId == n.ClientNotificationId).Select(d => new ClientNotificationDeliveryDto(d.Channel, d.Recipient, d.Status, d.Message, d.Attempts, d.SentAtUtc)).ToList())).ToList();
        return new PagedResult<ClientNotificationDto>(items, total, skip, take);
    }
}

/// <summary>Lote 98 — efecto del dominio OrderStatus: una orden recogida, en ruta o entregada se anota como hecho avisable (solo si el cliente tiene ese aviso encendido). No guarda.</summary>
public sealed class ClientNotificationOrderEffect(ClientNotificationService notifications) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        if (context.Skipped) return;   // Lote 107: los estatus brincados no se avisan al cliente
        var kind = context.To.InternalCode switch
        {
            OrderStatuses.Pickup => ClientNotificationKinds.PickedUp,
            OrderStatuses.InTransit => ClientNotificationKinds.OutForDelivery,
            OrderStatuses.Delivered => ClientNotificationKinds.Delivered,
            _ => null,
        };
        if (kind is not null) await notifications.RecordOrderEventAsync(context.EntityId, kind, ct);
    }
}

/// <summary>Lote 98 — efecto del dominio InvoiceStatus: una factura emitida se anota como hecho avisable.</summary>
public sealed class ClientNotificationInvoiceEffect(TeikemDbContext db, ClientNotificationService notifications) : IStatusTransitionEffect
{
    public string StatusDomain => BillingCodes.InvoiceStatusDomain;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, BillingCodes.InvoiceEntityType, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, InvoiceStatuses.Issued, StringComparison.OrdinalIgnoreCase)) return;
        var clientId = db.Set<Invoice>().Local.FirstOrDefault(i => i.InvoiceId == context.EntityId)?.ClientId
                       ?? await db.Set<Invoice>().AsNoTracking().Where(i => i.InvoiceId == context.EntityId).Select(i => (int?)i.ClientId).FirstOrDefaultAsync(ct);
        if (clientId is int c) await notifications.RecordRefEventAsync(c, ClientNotificationKinds.InvoiceIssued, context.EntityId, ct);
    }
}

/// <summary>Lote 98 — efecto del dominio RemittanceStatus: una remesa COD remitida se anota como «COD procesado».</summary>
public sealed class ClientNotificationRemittanceEffect(TeikemDbContext db, ClientNotificationService notifications) : IStatusTransitionEffect
{
    public string StatusDomain => RemittanceCodes.StatusDomain;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, RemittanceCodes.EntityType, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, RemittanceStatuses.Remitted, StringComparison.OrdinalIgnoreCase)) return;
        var clientId = db.Set<CodRemittanceBatch>().Local.FirstOrDefault(b => b.CodRemittanceBatchId == context.EntityId)?.ClientId
                       ?? await db.Set<CodRemittanceBatch>().AsNoTracking().Where(b => b.CodRemittanceBatchId == context.EntityId).Select(b => (int?)b.ClientId).FirstOrDefaultAsync(ct);
        if (clientId is int c) await notifications.RecordRefEventAsync(c, ClientNotificationKinds.CodProcessed, context.EntityId, ct);
    }
}

/// <summary>Almacén — efecto del dominio AsnStatus: un aviso de llegada de un cliente que pasa a RECEIVED se anota como «recibido» (entra en el resumen del día). Los avisos que nacen de una compra no tienen cliente y no se avisan. No guarda.</summary>
public sealed class ClientNotificationAsnReceivedEffect(TeikemDbContext db, ClientNotificationService notifications) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.AsnStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.Asn, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, AsnStatuses.Received, StringComparison.OrdinalIgnoreCase)) return;
        var clientId = db.Set<Asn>().Local.FirstOrDefault(a => a.AsnId == context.EntityId)?.ClientId
                       ?? await db.Set<Asn>().AsNoTracking().Where(a => a.AsnId == context.EntityId).Select(a => a.ClientId).FirstOrDefaultAsync(ct);
        if (clientId is int c) await notifications.RecordRefEventAsync(c, ClientNotificationKinds.WhReceived, context.EntityId, ct);
    }
}

/// <summary>Almacén — efecto del dominio ExitNoticeStatus: un aviso de salida que se despacha (parcial o totalmente) se anota como «despachado» (entra en el resumen del día; el mismo aviso despachado en dos partes se cuenta una vez). No guarda.</summary>
public sealed class ClientNotificationExitNoticeDispatchedEffect(TeikemDbContext db, ClientNotificationService notifications) : IStatusTransitionEffect
{
    public string StatusDomain => ExitNoticeCodes.StatusDomain;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, ExitNoticeCodes.EntityType, StringComparison.OrdinalIgnoreCase)) return;
        if (context.To.InternalCode is not (ExitNoticeStatuses.Partial or ExitNoticeStatuses.Shipped)) return;
        var clientId = db.Set<ExitNotice>().Local.FirstOrDefault(x => x.ExitNoticeId == context.EntityId)?.ClientId
                       ?? await db.Set<ExitNotice>().AsNoTracking().Where(x => x.ExitNoticeId == context.EntityId).Select(x => (int?)x.ClientId).FirstOrDefaultAsync(ct);
        if (clientId is int c) await notifications.RecordRefEventAsync(c, ClientNotificationKinds.WhDispatched, context.EntityId, ct);
    }
}
