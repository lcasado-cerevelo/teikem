using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Contacts;
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
/// Lote 243:
/// - Destinatarios «del evento» (Kind CONTEXT, Value = clave que declara el evento, p. ej. SELLER «Vendedor de la orden»); la regla nueva
///   trae los que el evento marca por omisión (si no se mandan destinatarios) y cada canal la plantilla por omisión del evento para ese canal.
/// - Variables bloque ({orderDetail}): solo en el texto, nunca en el asunto (400).
/// - Vista previa (PreviewAsync) y «Enviar prueba» con un documento real (Reference): exigen además el permiso y el módulo que declara
///   el proveedor de contenido del evento (p. ej. sales.view y SALES_ORDERS).
/// </summary>
public sealed class NotificationRuleService(
    TeikemDbContext db,
    ITenantContext tenant,
    NotificationEventCatalog catalog,
    IEnumerable<INotificationChannelSender> senders,
    INotificationPublisher publisher,
    IEnumerable<INotificationContentProvider>? contentProviders = null,
    PermissionService? permissions = null,
    ModuleService? modules = null)
{
    private readonly Dictionary<string, INotificationChannelSender> _senders = senders.ToDictionary(s => s.Channel, StringComparer.Ordinal);
    private readonly IReadOnlyList<INotificationContentProvider> _providers = contentProviders?.ToList() ?? new List<INotificationContentProvider>();

    public IReadOnlyList<NotificationEventDto> ListEvents()
        => catalog.All.OrderBy(e => e.NameEs, StringComparer.CurrentCultureIgnoreCase).Select(ToDto).ToList();

    public IReadOnlyList<NotificationChannelStatusDto> ListChannels()
        => NotificationChannels.All.Select(c => _senders.TryGetValue(c, out var s)
            ? new NotificationChannelStatusDto(c, s.IsConfigured, s.IsConfigured && s.IsLocal)
            : new NotificationChannelStatusDto(c, false, false)).ToList();

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
        {
            // Lote 243: el canal toma la plantilla por omisión del evento para ese canal (p. ej. el correo con {orderDetail}); si no, la general
            var cd = def.ChannelDefaultFor(channel);
            rule.Channels.Add(new NotificationRuleChannel
            {
                TenantId = tenantId, Channel = channel, IsEnabled = false,
                SubjectJson = cd is null ? MultilingualText.Build(def.DefaultSubjectEs, def.DefaultSubjectEn) : MultilingualText.Build(cd.SubjectEs, cd.SubjectEn),
                BodyJson = cd is null ? MultilingualText.Build(def.DefaultBodyEs, def.DefaultBodyEn) : MultilingualText.Build(cd.BodyEs, cd.BodyEn),
            });
        }
        ApplyChannels(rule, req.Channels, def);
        if (req.Recipients is not null) await ReplaceRecipientsAsync(rule, req.Recipients, def, ct);
        else   // Lote 243: sin destinatarios, la regla nueva trae los del evento marcados por omisión (p. ej. «Vendedor de la orden»)
            foreach (var cr in (def.ContextRecipients ?? Array.Empty<NotificationContextRecipientDef>()).Where(r => r.IsDefault))
                rule.Recipients.Add(new NotificationRuleRecipient { TenantId = tenantId, Kind = NotificationRecipientKinds.Context, Value = cr.Key });
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
            await ReplaceRecipientsAsync(rule, req.Recipients, def, ct);
        }
        if (req.IsActive is bool active) rule.IsActive = active;
        ValidateFinalState(rule, def);
        await db.SaveGuardedAsync(NotificationRules.DuplicateRuleMessage, ct);
        return ToDto(rule, await LabelsAsync(rule.Recipients, ct));
    }

    /// <summary>
    /// «Enviar prueba»: manda el texto de ese canal de la regla a quien la pide (no queda en el registro). Sin Reference, con los valores
    /// de ejemplo del evento (y el contenido de ejemplo del proveedor, si el evento tiene); Lote 243: con Reference (número o PublicId de
    /// un documento real) arma el aviso con sus datos, con las mismas guardas que la vista previa. El correo lleva los adjuntos si el
    /// texto usa un bloque.
    /// </summary>
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
        var lang = t.Lang;
        var (values, content, _) = await ContentForAsync(rule.TenantId, def, req.Reference, NotificationAudience.Staff, lang, ct);
        if (content?.SkipReason is { } skip) return new NotificationTestResultDto(channel, NotificationLogStatuses.Skipped, skip);
        var r = Build(def, channel, NotificationRules.Pick(row.SubjectJson, lang), NotificationRules.Pick(row.BodyJson, lang), values, content);
        var sender = _senders[channel];
        SendOutcome outcome;
        try
        {
            outcome = await sender.SendAsync(new NotificationDelivery(rule.TenantId, company, rule.EventCode, null, rule.NotificationRuleId, user.Id, user.Email, user.PhoneNumber,
                r.Subject, r.Text, r.Html, r.Link, false, r.Attachments.Count > 0 ? r.Attachments : null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { outcome = SendOutcome.Failed(ex.Message); }
        return new NotificationTestResultDto(channel, outcome.Status, outcome.Message);
    }

    /// <summary>
    /// Lote 243 — vista previa de un aviso: el asunto, el texto, el HTML (en EMAIL) y los adjuntos tal como llegarían, para la Audience
    /// y el Lang pedidos. NO envía ni escribe en el registro. Valida el evento, el canal y las variables como al guardar.
    /// Subject y Body nulos = la plantilla de la primera regla activa del evento con ese canal encendido (RULE) o la de omisión (DEFAULT).
    /// Con Reference (número o PublicId de un documento real): 400 si el evento no tiene proveedor; 403 sin el permiso que declara el
    /// proveedor o con su módulo apagado; 404 si el documento no existe en la compañía. Sin Reference se usa el ejemplo.
    /// </summary>
    public async Task<NotificationPreviewDto> PreviewAsync(NotificationPreviewRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.EventCode)) throw new ValidationException("eventCode", NotificationRules.EventRequiredMessage);
        var def = catalog.Find(req.EventCode) ?? throw new ValidationException("eventCode", NotificationRules.UnknownEventMessage(req.EventCode.Trim()));
        var channel = NotificationRules.NormalizeChannel(req.Channel) ?? throw new ValidationException("channel", string.IsNullOrWhiteSpace(req.Channel)
            ? NotificationRules.ChannelRequiredMessage : NotificationRules.UnknownChannelMessage(req.Channel.Trim()));
        var audience = NotificationAudienceCodes.Parse(req.Audience) ?? throw new ValidationException("audience", NotificationRules.UnknownAudienceMessage(req.Audience?.Trim()));
        string lang;
        if (string.IsNullOrWhiteSpace(req.Lang))
            lang = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(x => x.TenantId == tenantId).Select(x => x.DefaultLangCode).FirstOrDefaultAsync(ct) ?? MultilingualText.DefaultLang;
        else
        {
            lang = req.Lang.Trim().ToLowerInvariant();
            if (lang is not ("es" or "en")) throw new ValidationException("lang", NotificationRules.UnknownLangMessage(req.Lang.Trim()));
        }

        // la plantilla: la mandada (REQUEST), la de la regla activa (RULE) o la de omisión del evento (DEFAULT)
        string subjectTpl, bodyTpl, source;
        if (req.Subject is not null || req.Body is not null)
        {
            subjectTpl = (req.Subject ?? string.Empty).Trim();
            bodyTpl = (req.Body ?? string.Empty).Trim();
            if (subjectTpl.Length > NotificationRules.MaxSubjectLength) throw new ValidationException("channels", NotificationRules.SubjectTooLongMessage(channel));
            if (bodyTpl.Length > NotificationRules.MaxBodyLength) throw new ValidationException("channels", NotificationRules.BodyTooLongMessage(channel));
            ValidateTemplate(def, subjectTpl, bodyTpl);
            source = NotificationTemplateSources.Request;
        }
        else
        {
            var active = await db.NotificationRules.AsNoTracking().Where(r => r.EventCode == def.Code && r.IsActive)
                .SelectMany(r => r.Channels.Where(c => c.Channel == channel && c.IsEnabled).Select(c => new { r.NotificationRuleId, c.SubjectJson, c.BodyJson }))
                .OrderBy(x => x.NotificationRuleId).FirstOrDefaultAsync(ct);
            if (active is not null)
            {
                (subjectTpl, bodyTpl, source) = (NotificationRules.Pick(active.SubjectJson, lang), NotificationRules.Pick(active.BodyJson, lang), NotificationTemplateSources.Rule);
            }
            else
            {
                var cd = def.ChannelDefaultFor(channel);
                var en = lang == "en";
                subjectTpl = cd is null ? (en ? def.DefaultSubjectEn : def.DefaultSubjectEs) : (en ? cd.SubjectEn : cd.SubjectEs);
                bodyTpl = cd is null ? (en ? def.DefaultBodyEn : def.DefaultBodyEs) : (en ? cd.BodyEn : cd.BodyEs);
                source = NotificationTemplateSources.Default;
            }
        }

        var (values, content, usedSample) = await ContentForAsync(tenantId, def, req.Reference, audience, lang, ct);
        if (content?.SkipReason is { } skip) throw new ValidationException("reference", skip);
        var r = Build(def, channel, subjectTpl, bodyTpl, values, content);
        var hasSubject = channel is NotificationChannels.Email or NotificationChannels.App;
        return new NotificationPreviewDto(channel, hasSubject ? r.Subject : null, r.Text, channel == NotificationChannels.Email ? r.Html : null,
            r.Attachments.Select(a => new NotificationPreviewAttachmentDto(a.FileName, a.ContentType, a.Content?.LongLength ?? 0)).ToList(), usedSample, source);
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

    // ---------------------------------------------------------------- contenido (Lote 243)

    /// <summary>El proveedor de contenido del evento, o null si no tiene.</summary>
    private INotificationContentProvider? ProviderFor(string eventCode)
        => _providers.FirstOrDefault(p => p.EventCodes.Contains(eventCode, StringComparer.Ordinal));

    /// <summary>
    /// Variables y contenido para la prueba o la vista previa. Con Reference: guardas del proveedor (400 sin proveedor; permiso → 403;
    /// módulo apagado; documento inexistente → la NotFoundException del proveedor) y su contenido real. Sin Reference: los valores de
    /// ejemplo del evento y el contenido de ejemplo del proveedor (si el evento tiene).
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, string> Values, NotificationContent? Content, bool UsedSample)> ContentForAsync(
        int tenantId, NotificationEventDef def, string? reference, NotificationAudience audience, string lang, CancellationToken ct)
    {
        var provider = ProviderFor(def.Code);
        if (string.IsNullOrWhiteSpace(reference))
        {
            var samples = def.Variables.ToDictionary(v => v.Name, v => v.Sample, StringComparer.Ordinal);
            var sample = provider?.Sample(def.Code, audience, lang);
            return (sample is null ? samples : NotificationRules.Merge(samples, sample.Values), sample, true);
        }
        if (provider is null) throw new ValidationException("reference", NotificationRules.ReferenceNotSupportedMessage);
        if (provider.ReferencePermission is { } perm)
        {
            // sin el servicio de permisos no se arriesga: se niega (en el API siempre está registrado)
            if (permissions is null) throw new ForbiddenException($"Falta el permiso '{perm}'.");
            await permissions.EnsureAsync(perm, ct);
        }
        if (provider.ReferenceModule is { } module)
        {
            if (modules is null) throw new ModuleDisabledException(module);
            await modules.EnsureEnabledAsync(module, ct);
        }
        var values = await provider.ValuesForReferenceAsync(tenantId, def.Code, reference.Trim(), ct);
        // la ficha del cliente (dirección, teléfono, registro de comerciante) solo para quien puede leer clientes (clients.read)
        var clientCard = permissions is not null && await permissions.HasPermissionAsync(Teikem.Domain.Constants.PermissionCatalog.ClientsRead, ct);
        var content = await provider.BuildAsync(new NotificationContentRequest(tenantId, def.Code, values, audience, lang, clientCard), ct);
        return (content is null ? values : NotificationRules.Merge(values, content.Values), content, false);
    }

    private sealed record Rendered(string Subject, string Text, string Html, string? Link, IReadOnlyList<EmailAttachment> Attachments);

    /// <summary>
    /// Arma el aviso como el procesador: asunto con las variables; texto con cada bloque en su versión de una línea; en EMAIL el HTML con
    /// los bloques (Compose) y los adjuntos solo si el texto usó un bloque (y caben en el tope del correo); en los demás canales, los párrafos del texto.
    /// </summary>
    private static Rendered Build(NotificationEventDef def, string channel, string? subjectTpl, string? bodyTpl, IReadOnlyDictionary<string, string> values, NotificationContent? content)
    {
        var blocks = content?.Blocks;
        var text = NotificationRules.RenderText(bodyTpl, values, blocks);
        var isEmail = channel == NotificationChannels.Email;
        var html = isEmail ? NotificationHtml.Compose(bodyTpl, values, blocks) : NotificationHtml.Paragraphs(text);
        IReadOnlyList<EmailAttachment> attachments = isEmail && content is not null && NotificationRules.UsesAny(bodyTpl, def.BlockNames)
            ? EmailAttachmentRules.Fit(content.Attachments).Kept : Array.Empty<EmailAttachment>();
        var link = def.LinkTemplate is null ? null : NotificationRules.Render(def.LinkTemplate, values);
        return new Rendered(NotificationRules.Render(subjectTpl, values), text, html, link, attachments);
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

    /// <summary>Prefijo internacional por omisión de la compañía de la regla, en dígitos («1»).</summary>
    private async Task<string> DefaultCountryAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var cc = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.PhoneCountryCode).FirstOrDefaultAsync(ct) ?? "+1";
        return PhoneRules.NormalizeCountry(cc) ?? "1";
    }

    private async Task ReplaceRecipientsAsync(NotificationRule rule, IReadOnlyList<NotificationRecipientRequest> requests, NotificationEventDef def, CancellationToken ct)
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
                case NotificationRecipientKinds.Context:
                    // Lote 243: una clave que declara el evento (p. ej. SELLER); se guarda como la declara el evento
                    var cr = def.ContextRecipient(req.Value) ?? throw new ValidationException("recipients", NotificationRules.UnknownContextRecipientMessage(req.Value?.Trim()));
                    row.Value = cr.Key;
                    key = "C" + row.Value;
                    break;
                default:
                    if (!NotificationRules.IsValidPhone(req.Value)) throw new ValidationException("recipients", NotificationRules.InvalidPhoneMessage(req.Value?.Trim()));
                    row.Value = PhoneRules.ToE164Text(req.Value, await DefaultCountryAsync(ct));   // con su prefijo internacional (E.164)
                    key = "P" + row.Value;
                    break;
            }
            if (!seen.Add(key)) continue;   // repetido en la misma lista: se ignora
            rule.Recipients.Add(row);
        }
    }

    /// <summary>Las plantillas solo usan variables del evento y ninguna variable bloque va en el asunto (400, campo channels).</summary>
    private static void ValidateTemplate(NotificationEventDef def, IEnumerable<string?> subjects, IEnumerable<string?> bodies)
    {
        var allowed = def.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var subjectList = subjects.ToList();
        foreach (var text in subjectList.Concat(bodies))
            foreach (var variable in NotificationRules.VariablesOf(text).Where(v => !allowed.Contains(v)))
                throw new ValidationException("channels", NotificationRules.UnknownVariableMessage(variable));
        var blocks = def.BlockNames;
        foreach (var text in subjectList)
            foreach (var variable in NotificationRules.VariablesOf(text).Where(blocks.Contains))
                throw new ValidationException("channels", NotificationRules.BlockInSubjectMessage(variable));
    }

    private static void ValidateTemplate(NotificationEventDef def, string subject, string body) => ValidateTemplate(def, new[] { subject }, new[] { body });

    private void ValidateFinalState(NotificationRule rule, NotificationEventDef def)
    {
        foreach (var c in rule.Channels)
        {
            ValidateTemplate(def, MultilingualText.Parse(c.SubjectJson).Values, MultilingualText.Parse(c.BodyJson).Values);
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
                    NotificationRecipientKinds.Context => ContextLabel(r.EventCode, x.Value),
                    _ => x.Value ?? string.Empty,
                })).ToList(),
            Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>()));

    /// <summary>Lote 243: la etiqueta de un destinatario del evento en el idioma de la sesión (o la clave, si el evento ya no la declara).</summary>
    private string ContextLabel(string eventCode, string? key)
    {
        var cr = catalog.Find(eventCode)?.ContextRecipient(key);
        return cr is null ? key ?? string.Empty : tenant.Lang == "en" ? cr.LabelEn : cr.LabelEs;
    }

    private static NotificationEventDto ToDto(NotificationEventDef e)
        => new(e.Code, e.ModuleKey, e.NameEs, e.NameEn, e.IsOptional, e.Variables.Select(v => new NotificationVariableDto(v.Name, v.LabelEs, v.LabelEn, v.Sample, v.IsBlock)).ToList(),
            e.DefaultSubjectEs, e.DefaultSubjectEn, e.DefaultBodyEs, e.DefaultBodyEn, NotificationEventCategories.Of(e.Code),
            (e.ContextRecipients ?? Array.Empty<NotificationContextRecipientDef>())
                .Select(r => new NotificationContextRecipientDto(r.Key, r.LabelEs, r.LabelEn, r.IsDefault, NotificationAudienceCodes.ToCode(r.Audience))).ToList(),
            (e.ChannelDefaults ?? Array.Empty<NotificationChannelDefault>())
                .Select(d => new NotificationChannelDefaultDto(d.Channel, d.SubjectEs, d.SubjectEn, d.BodyEs, d.BodyEn)).ToList());
}
