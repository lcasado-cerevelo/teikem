using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 51 — reglas de aviso de la compañía (permiso notifications.manage): eventos, reglas con sus canales y destinatarios, envío de
/// prueba y registro de envíos.
/// - Una regla = un evento + un nombre (único en la compañía) + canales con plantilla es/en + destinatarios. Nunca DELETE: se apaga.
/// - Los cuatro canales (EMAIL, SMS, APP, PUSH) existen siempre en la regla; nacen APAGADOS con los textos por omisión del evento.
/// - Un canal NO configurado en Teikem (SMS y PUSH sin proveedor; EMAIL sin Brevo) no se puede encender (400).
/// - Un canal encendido necesita texto (y asunto en EMAIL y APP); las plantillas solo pueden usar las {variables} del evento.
/// - Activar una regla exige al menos un destinatario y un canal encendido.
/// - Destinatarios: rol de la compañía, usuario activo de la compañía, correo o teléfono sueltos (el teléfono se guarda; sin SMS no se usa).
/// - En el PATCH, Recipients REEMPLAZA la lista; Channels cambia solo los canales que vengan.
/// </summary>
public sealed class NotificationRuleService(
    TeikemDbContext db,
    ITenantContext tenant,
    NotificationEventCatalog catalog,
    IEnumerable<INotificationChannelSender> senders,
    INotificationPublisher publisher)
{
    private readonly Dictionary<string, INotificationChannelSender> _senders = senders.ToDictionary(s => s.Channel, StringComparer.Ordinal);

    public IReadOnlyList<NotificationEventDto> ListEvents()
        => catalog.All.OrderBy(e => e.NameEs, StringComparer.CurrentCultureIgnoreCase).Select(ToDto).ToList();

    public IReadOnlyList<NotificationChannelStatusDto> ListChannels()
        => NotificationChannels.All.Select(c => new NotificationChannelStatusDto(c, _senders.TryGetValue(c, out var s) && s.IsConfigured)).ToList();

    public async Task<IReadOnlyList<NotificationRuleDto>> ListRulesAsync(CancellationToken ct)
    {
        var rules = await db.NotificationRules.AsNoTracking().AsSplitQuery().Include(r => r.Channels).Include(r => r.Recipients)
            .OrderBy(r => r.Name).ThenBy(r => r.NotificationRuleId).ToListAsync(ct);
        var labels = await LabelsAsync(rules.SelectMany(r => r.Recipients), ct);
        return rules.Select(r => ToDto(r, labels)).ToList();
    }

    public async Task<NotificationRuleDto> GetRuleAsync(Guid publicId, CancellationToken ct)
    {
        var rule = await LoadAsync(publicId, ct, track: false);
        return ToDto(rule, await LabelsAsync(rule.Recipients, ct));
    }

    public async Task<NotificationRuleDto> CreateRuleAsync(NotificationRuleRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.EventCode)) throw new ValidationException("eventCode", NotificationRules.EventRequiredMessage);
        var def = catalog.Find(req.EventCode) ?? throw new ValidationException("eventCode", NotificationRules.UnknownEventMessage(req.EventCode.Trim()));
        var name = NormalizeName(req.Name);

        var rule = new NotificationRule { PublicId = Guid.NewGuid(), TenantId = tenantId, EventCode = def.Code, Name = name, IsActive = false };
        foreach (var channel in NotificationChannels.All)
            rule.Channels.Add(new NotificationRuleChannel
            {
                TenantId = tenantId, Channel = channel, IsEnabled = false,
                SubjectJson = MultilingualText.Build(def.DefaultSubjectEs, def.DefaultSubjectEn), BodyJson = MultilingualText.Build(def.DefaultBodyEs, def.DefaultBodyEn),
            });
        ApplyChannels(rule, req.Channels, def);
        if (req.Recipients is not null) await ReplaceRecipientsAsync(rule, req.Recipients, ct);
        if (req.IsActive == true) rule.IsActive = true;
        ValidateFinalState(rule, def);

        db.NotificationRules.Add(rule);
        await db.SaveGuardedAsync(NotificationRules.DuplicateRuleMessage, ct);
        return ToDto(rule, await LabelsAsync(rule.Recipients, ct));
    }

    public async Task<NotificationRuleDto> UpdateRuleAsync(Guid publicId, NotificationRuleRequest req, CancellationToken ct)
    {
        var rule = await LoadAsync(publicId, ct);
        db.ApplyRowVersion(rule, req.RowVersion);
        var def = catalog.Find(rule.EventCode) ?? throw new ValidationException("eventCode", NotificationRules.UnknownEventMessage(rule.EventCode));
        if (req.EventCode is not null && !string.Equals(req.EventCode.Trim(), rule.EventCode, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("eventCode", "El evento de una regla no se cambia; cree otra regla.");
        if (req.Name is not null) rule.Name = NormalizeName(req.Name);
        ApplyChannels(rule, req.Channels, def);
        if (req.Recipients is not null)
        {
            db.NotificationRuleRecipients.RemoveRange(rule.Recipients);
            rule.Recipients.Clear();
            await ReplaceRecipientsAsync(rule, req.Recipients, ct);
        }
        if (req.IsActive is bool active) rule.IsActive = active;
        ValidateFinalState(rule, def);
        await db.SaveGuardedAsync(NotificationRules.DuplicateRuleMessage, ct);
        return ToDto(rule, await LabelsAsync(rule.Recipients, ct));
    }

    /// <summary>«Enviar prueba»: manda el texto de ese canal de la regla, con valores de ejemplo, a quien la pide (no queda en el registro).</summary>
    public async Task<NotificationTestResultDto> TestAsync(Guid publicId, NotificationTestRequest req, CancellationToken ct)
    {
        var channel = NotificationRules.NormalizeChannel(req.Channel) ?? throw new ValidationException("channel", string.IsNullOrWhiteSpace(req.Channel)
            ? NotificationRules.ChannelRequiredMessage : NotificationRules.UnknownChannelMessage(req.Channel.Trim()));
        var rule = await LoadAsync(publicId, ct, track: false);
        var def = catalog.Find(rule.EventCode)!;
        var row = rule.Channels.First(c => c.Channel == channel);
        var t = (TenantContext)tenant;
        var user = await db.Users.AsNoTracking().Where(u => u.Id == t.RequireUserId()).Select(u => new { u.Id, u.Email, u.PhoneNumber }).SingleAsync(ct);
        var company = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(x => x.TenantId == rule.TenantId).Select(x => x.Name).SingleAsync(ct);
        var samples = def.Variables.ToDictionary(v => v.Name, v => v.Sample);
        var subject = NotificationRules.Render(NotificationRules.Pick(row.SubjectJson, t.Lang), samples);
        var body = NotificationRules.Render(NotificationRules.Pick(row.BodyJson, t.Lang), samples);
        var link = def.LinkTemplate is null ? null : NotificationRules.Render(def.LinkTemplate, samples);
        var sender = _senders[channel];
        SendOutcome outcome;
        try
        {
            outcome = await sender.SendAsync(new NotificationDelivery(rule.TenantId, company, rule.EventCode, null, rule.NotificationRuleId, user.Id, user.Email, user.PhoneNumber,
                subject, body, NotificationHtml.Paragraphs(body), link, false), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { outcome = SendOutcome.Failed(ex.Message); }
        return new NotificationTestResultDto(channel, outcome.Status, outcome.Message);
    }

    /// <summary>Dispara el evento de prueba (TEST) por la cola real: lo reciben las reglas ACTIVAS de ese evento.</summary>
    public async Task<NotificationQueuedDto> PublishTestEventAsync(NotificationTestEventRequest req, CancellationToken ct)
    {
        var t = (TenantContext)tenant;
        var who = await db.Users.AsNoTracking().Where(u => u.Id == t.RequireUserId()).Select(u => u.FullName ?? u.Email).SingleAsync(ct);
        var message = string.IsNullOrWhiteSpace(req.Message) ? "Esto es una prueba." : req.Message.Trim();
        await publisher.PublishAsync(NotificationCodes.TestEvent, new Dictionary<string, string?> { ["message"] = message, ["user"] = who }, null, ct);
        await db.SaveChangesAsync(ct);
        return new NotificationQueuedDto(true);
    }

    public async Task<PagedResult<NotificationLogDto>> ListLogAsync(string? eventCode, string? channel, string? status, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, 200);
        var q = db.NotificationLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(eventCode)) q = q.Where(l => l.EventCode == eventCode.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(l => l.Channel == channel.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status.Trim().ToUpper());
        if (fromUtc is DateTime from) q = q.Where(l => l.CreatedAtUtc >= from);
        if (toUtc is DateTime to) q = q.Where(l => l.CreatedAtUtc < to);
        var total = await q.CountAsync(ct);
        var rows = await (from l in q
                          join r in db.NotificationRules.AsNoTracking() on l.NotificationRuleId equals r.NotificationRuleId
                          orderby l.NotificationLogId descending
                          select new { l, r.PublicId, r.Name }).Skip(skip).Take(take).ToListAsync(ct);
        return new PagedResult<NotificationLogDto>(rows.Select(x => new NotificationLogDto(x.l.NotificationLogId, x.l.NotificationOutboxId, x.PublicId, x.Name, x.l.EventCode, x.l.Channel,
            x.l.Recipient, x.l.Status, x.l.Error, x.l.Attempts, x.l.CreatedAtUtc, x.l.SentAtUtc)).ToList(), total, skip, take);
    }

    // ---------------------------------------------------------------- validación y armado

    private static string NormalizeName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new ValidationException("name", NotificationRules.NameRequiredMessage);
        if (n.Length > NotificationRules.MaxNameLength) throw new ValidationException("name", NotificationRules.NameTooLongMessage);
        return n;
    }

    private static void ApplyChannels(NotificationRule rule, IReadOnlyList<NotificationChannelRequest>? requests, NotificationEventDef def)
    {
        if (requests is null) return;
        foreach (var req in requests)
        {
            var channel = NotificationRules.NormalizeChannel(req.Channel) ?? throw new ValidationException("channels", string.IsNullOrWhiteSpace(req.Channel)
                ? NotificationRules.ChannelRequiredMessage : NotificationRules.UnknownChannelMessage(req.Channel.Trim()));
            var row = rule.Channels.First(c => c.Channel == channel);
            if (req.IsEnabled is bool on) row.IsEnabled = on;
            row.SubjectJson = Merge(row.SubjectJson, req.SubjectEs, req.SubjectEn, NotificationRules.MaxSubjectLength, NotificationRules.SubjectTooLongMessage(channel));
            row.BodyJson = Merge(row.BodyJson, req.BodyEs, req.BodyEn, NotificationRules.MaxBodyLength, NotificationRules.BodyTooLongMessage(channel));
        }
    }

    /// <summary>Cambia solo el idioma que venga (null = sin cambio, "" = quitarlo) y valida el largo.</summary>
    private static string? Merge(string? existing, string? es, string? en, int max, string tooLong)
    {
        if (es is null && en is null) return existing;
        var d = MultilingualText.Parse(existing);
        foreach (var (lang, value) in new[] { ("es", es), ("en", en) })
        {
            if (value is null) continue;
            var v = value.Trim();
            if (v.Length > max) throw new ValidationException("channels", tooLong);
            if (v.Length == 0) d.Remove(lang); else d[lang] = v;
        }
        return d.Count == 0 ? null : MultilingualText.Serialize(d);
    }

    private async Task ReplaceRecipientsAsync(NotificationRule rule, IReadOnlyList<NotificationRecipientRequest> requests, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            var kind = NotificationRules.NormalizeKind(req.Kind) ?? throw new ValidationException("recipients", NotificationRules.UnknownRecipientKindMessage(req.Kind?.Trim()));
            var row = new NotificationRuleRecipient { TenantId = rule.TenantId, Kind = kind };
            string key;
            switch (kind)
            {
                case NotificationRecipientKinds.Role:
                    var role = req.RoleId is int rid ? await db.AppRoles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleId == rid && r.TenantId == rule.TenantId && r.IsActive, ct) : null;
                    row.RoleId = (role ?? throw new ValidationException("recipients", NotificationRules.RoleNotFoundMessage)).RoleId;
                    key = $"R{row.RoleId}";
                    break;
                case NotificationRecipientKinds.User:
                    var ok = req.UserId is int uid && await db.UserTenants.AsNoTracking().AnyAsync(m => m.UserId == uid, ct)
                        && await db.Users.AsNoTracking().AnyAsync(u => u.Id == uid && u.IsActive, ct);
                    row.UserId = ok ? req.UserId : throw new ValidationException("recipients", NotificationRules.UserNotFoundMessage);
                    key = $"U{row.UserId}";
                    break;
                case NotificationRecipientKinds.Email:
                    if (!NotificationRules.IsValidEmail(req.Value)) throw new ValidationException("recipients", NotificationRules.InvalidEmailMessage(req.Value?.Trim()));
                    row.Value = req.Value!.Trim();
                    key = "E" + row.Value;
                    break;
                default:
                    if (!NotificationRules.IsValidPhone(req.Value)) throw new ValidationException("recipients", NotificationRules.InvalidPhoneMessage(req.Value?.Trim()));
                    row.Value = req.Value!.Trim();
                    key = "P" + row.Value;
                    break;
            }
            if (!seen.Add(key)) continue;   // repetido en la misma lista: se ignora
            rule.Recipients.Add(row);
        }
    }

    private void ValidateFinalState(NotificationRule rule, NotificationEventDef def)
    {
        var allowed = def.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var c in rule.Channels)
        {
            foreach (var json in new[] { c.SubjectJson, c.BodyJson })
                foreach (var lang in MultilingualText.Parse(json).Values)
                    foreach (var variable in NotificationRules.VariablesOf(lang).Where(v => !allowed.Contains(v)))
                        throw new ValidationException("channels", NotificationRules.UnknownVariableMessage(variable));
            if (!c.IsEnabled) continue;
            if (!(_senders.TryGetValue(c.Channel, out var sender) && sender.IsConfigured)) throw new ValidationException("channels", NotificationRules.NotConfiguredMessage(c.Channel));
            var needsSubject = c.Channel is NotificationChannels.Email or NotificationChannels.App;
            if (needsSubject && string.IsNullOrEmpty(NotificationRules.Pick(c.SubjectJson, null))) throw new ValidationException("channels", NotificationRules.SubjectRequiredMessage(c.Channel));
            if (string.IsNullOrEmpty(NotificationRules.Pick(c.BodyJson, null))) throw new ValidationException("channels", NotificationRules.BodyRequiredMessage(c.Channel));
        }
        if (!rule.IsActive) return;
        if (rule.Recipients.Count == 0) throw new ValidationException("recipients", NotificationRules.NeedRecipientMessage);
        if (!rule.Channels.Any(c => c.IsEnabled)) throw new ValidationException("channels", NotificationRules.NeedChannelMessage);
    }

    private async Task<NotificationRule> LoadAsync(Guid publicId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.NotificationRules.AsQueryable() : db.NotificationRules.AsNoTracking();
        return await q.AsSplitQuery().Include(r => r.Channels).Include(r => r.Recipients).FirstOrDefaultAsync(r => r.PublicId == publicId, ct)
            ?? throw new NotFoundException("Regla de aviso", null, feminine: true);
    }

    // ---------------------------------------------------------------- DTO

    private async Task<Dictionary<(string Kind, int Id), string>> LabelsAsync(IEnumerable<NotificationRuleRecipient> recipients, CancellationToken ct)
    {
        var list = recipients.ToList();
        var roleIds = list.Where(r => r.RoleId is not null).Select(r => r.RoleId!.Value).Distinct().ToList();
        var userIds = list.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var labels = new Dictionary<(string, int), string>();
        if (roleIds.Count > 0)
            foreach (var r in await db.AppRoles.AsNoTracking().IgnoreQueryFilters().Where(r => roleIds.Contains(r.RoleId)).Select(r => new { r.RoleId, r.Name }).ToListAsync(ct))
                labels[(NotificationRecipientKinds.Role, r.RoleId)] = r.Name;
        if (userIds.Count > 0)
            foreach (var u in await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct))
                labels[(NotificationRecipientKinds.User, u.Id)] = string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? $"#{u.Id}") : $"{u.FullName} ({u.Email})";
        return labels;
    }

    private NotificationRuleDto ToDto(NotificationRule r, Dictionary<(string Kind, int Id), string> labels)
        => new(r.PublicId, r.EventCode, r.Name, r.IsActive,
            r.Channels.OrderBy(c => Array.IndexOf(NotificationChannels.All, c.Channel)).Select(c =>
            {
                var s = MultilingualText.Parse(c.SubjectJson);
                var b = MultilingualText.Parse(c.BodyJson);
                return new NotificationRuleChannelDto(c.Channel, c.IsEnabled, s.GetValueOrDefault("es"), s.GetValueOrDefault("en"), b.GetValueOrDefault("es"), b.GetValueOrDefault("en"),
                    _senders.TryGetValue(c.Channel, out var sender) && sender.IsConfigured);
            }).ToList(),
            r.Recipients.OrderBy(x => x.NotificationRuleRecipientId).Select(x => new NotificationRecipientDto(x.Kind, x.RoleId, x.UserId, x.Value,
                x.Kind switch
                {
                    NotificationRecipientKinds.Role => labels.GetValueOrDefault((x.Kind, x.RoleId ?? 0)) ?? $"#{x.RoleId}",
                    NotificationRecipientKinds.User => labels.GetValueOrDefault((x.Kind, x.UserId ?? 0)) ?? $"#{x.UserId}",
                    _ => x.Value ?? string.Empty,
                })).ToList(),
            Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>()));

    private static NotificationEventDto ToDto(NotificationEventDef e)
        => new(e.Code, e.ModuleKey, e.NameEs, e.NameEn, e.IsOptional, e.Variables.Select(v => new NotificationVariableDto(v.Name, v.LabelEs, v.LabelEn, v.Sample)).ToList(),
            e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn);
}
