using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Notifications;

public sealed class NotificationsOptions
{
    public const string Section = "Notifications";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 10;
}

/// <summary>
/// Lote 51 — procesa la cola de avisos. Cada aviso se «reclama» con un UPDATE atómico (varias copias del API nunca lo procesan a la
/// vez; el reclamo vence a los 5 minutos si la copia se cae) y se procesa como la compañía SIN usuario («Sistema»):
/// 1. reglas ACTIVAS del evento de esa compañía; 2. destinatarios (roles → sus usuarios activos de la compañía; usuarios; correos y
/// teléfonos sueltos), sin repetir; 3. por canal encendido, el texto (plantilla es/en según el idioma de la compañía con {variables});
/// 4. cada envío queda en NotificationLog (único por cola × regla × canal × destinatario: un reintento no repite lo ya enviado).
/// Si algún envío FALLA, el aviso se reintenta a 1, 5 y 30 minutos y después queda FAILED; SKIPPED (canal no configurado, sin correo,
/// silenciado) no es una falla. Una falla de un destinatario no detiene a los demás.
/// Lote 243: destinatarios «del evento» (Kind CONTEXT) que resuelve el INotificationContentProvider del evento; el texto se arma una vez
/// por grupo (personal o cliente × idioma) y canal; el correo lleva las variables bloque en HTML y los adjuntos (solo si el texto usó un
/// bloque). Un destinatario del evento que no deja a nadie alcanzable anota una fila SKIPPED «(etiqueta)» con el motivo; un contenido
/// con SkipReason (p. ej. la orden se canceló) deja SKIPPED a cada destinatario de la regla; un error del proveedor reintenta el aviso.
/// Los métodos son públicos para las pruebas (xunit los llama sin esperar al ciclo del worker).
/// </summary>
public sealed class NotificationProcessor(IServiceScopeFactory scopes, NotificationEventCatalog catalog, ILogger<NotificationProcessor> logger)
{
    public static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(5);
    public const int BatchSize = 100;
    public string InstanceId { get; } = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>Procesa lo que toca (todas las compañías). Devuelve cuántos avisos tomó esta copia.</summary>
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<(int TenantId, long Id)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var rows = await db.NotificationOutbox.AsNoTracking().IgnoreQueryFilters()
                .Where(o => o.Status == NotificationOutboxStatuses.Pending && o.NextAttemptUtc <= nowUtc && (o.LockedUntilUtc == null || o.LockedUntilUtc < nowUtc))
                .OrderBy(o => o.NotificationOutboxId).Take(BatchSize).Select(o => new { o.TenantId, o.NotificationOutboxId }).ToListAsync(ct);
            due = rows.Select(r => (r.TenantId, r.NotificationOutboxId)).ToList();
        }
        var taken = 0;
        foreach (var (tenantId, id) in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryProcessAsync(tenantId, id, nowUtc, ct)) taken++;
        }
        return taken;
    }

    public async Task<bool> TryProcessAsync(int tenantId, long outboxId, DateTime nowUtc, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
        using var asTenant = context.AsAnonymous(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();

        var claimed = await db.NotificationOutbox.IgnoreQueryFilters()
            .Where(o => o.NotificationOutboxId == outboxId && o.TenantId == tenantId && o.Status == NotificationOutboxStatuses.Pending && o.NextAttemptUtc <= nowUtc
                && (o.LockedUntilUtc == null || o.LockedUntilUtc < nowUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LockedBy, InstanceId).SetProperty(o => o.LockedUntilUtc, nowUtc + ClaimDuration).SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
        if (claimed == 0) return false;

        var outbox = await db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.NotificationOutboxId == outboxId, ct);
        string status;
        string? error = null;
        DateTime nextAttempt = nowUtc;
        try
        {
            var anyFailed = await DeliverAsync(scope.ServiceProvider, db, outbox, ct);
            if (!anyFailed) status = NotificationOutboxStatuses.Done;
            else
            {
                var (retry, delay) = NotificationRules.AfterFailure(outbox.Attempts);
                status = retry ? NotificationOutboxStatuses.Pending : NotificationOutboxStatuses.Failed;
                nextAttempt = retry ? DateTime.UtcNow + delay : nowUtc;
                error = "Algún envío falló; vea el registro de avisos.";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Aviso {OutboxId} (compañía {TenantId}) falló al procesarse.", outboxId, tenantId);
            var (retry, delay) = NotificationRules.AfterFailure(outbox.Attempts);
            status = retry ? NotificationOutboxStatuses.Pending : NotificationOutboxStatuses.Failed;
            nextAttempt = retry ? DateTime.UtcNow + delay : nowUtc;
            error = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
        }

        var done = status != NotificationOutboxStatuses.Pending ? (DateTime?)DateTime.UtcNow : null;
        await db.NotificationOutbox.IgnoreQueryFilters().Where(o => o.NotificationOutboxId == outboxId && o.LockedBy == InstanceId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status).SetProperty(o => o.NextAttemptUtc, nextAttempt).SetProperty(o => o.LastError, error)
                .SetProperty(o => o.ProcessedUtc, done).SetProperty(o => o.LockedBy, (string?)null).SetProperty(o => o.LockedUntilUtc, (DateTime?)null));
        return true;
    }

    /// <summary>
    /// Envía el aviso por todas las reglas activas del evento. Devuelve true si algún envío FALLÓ (y hay que reintentar).
    /// Lote 243: destinatarios del evento (Kind CONTEXT) y contenido armado al enviar (variables bloque, adjuntos) que se le piden
    /// al <see cref="INotificationContentProvider"/> del evento, de forma PEREZOSA: solo si la regla tiene CONTEXT o su plantilla usa
    /// un bloque o una variable que el aviso no trae. Una regla sin nada de eso sale idéntica a como salía y nunca resuelve el proveedor.
    /// </summary>
    private async Task<bool> DeliverAsync(IServiceProvider sp, TeikemDbContext db, NotificationOutbox outbox, CancellationToken ct)
    {
        var def = catalog.Find(outbox.EventCode);
        if (def is null) return false;   // el evento ya no existe en esta versión: no hay a quién avisar
        var rules = await db.NotificationRules.AsNoTracking().AsSplitQuery()
            .Include(r => r.Channels).Include(r => r.Recipients)
            .Where(r => r.EventCode == outbox.EventCode && r.IsActive).OrderBy(r => r.NotificationRuleId).ToListAsync(ct);
        if (rules.Count == 0) return false;

        var tenant = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == outbox.TenantId).Select(t => new { t.Name, t.DefaultLangCode }).SingleAsync(ct);
        var values = NotificationRules.ParseValues(outbox.PayloadJson);
        // Lote 132: una regla administrada por una integración (ScopeKey) solo dispara con los avisos de esa integración
        values.TryGetValue("scopeKey", out var scopeKey);
        rules = rules.Where(r => r.ScopeKey is null || string.Equals(r.ScopeKey, scopeKey, StringComparison.Ordinal)).ToList();
        if (rules.Count == 0) return false;
        var senders = sp.GetServices<INotificationChannelSender>().ToDictionary(s => s.Channel, StringComparer.Ordinal);
        var companyLang = NormalizeLang(tenant.DefaultLangCode) ?? tenant.DefaultLangCode;
        var blockNames = def.BlockNames;
        var anyFailed = false;

        // Lote 243: el proveedor se resuelve UNA vez por aviso y solo si alguna regla lo necesita; el contenido se arma una vez por (Audience, Lang)
        var providerResolved = false;
        INotificationContentProvider? provider = null;
        INotificationContentProvider? Provider()
        {
            if (providerResolved) return provider;
            providerResolved = true;
            provider = sp.GetServices<INotificationContentProvider>().FirstOrDefault(p => p.EventCodes.Contains(outbox.EventCode, StringComparer.Ordinal));
            return provider;
        }
        var contentCache = new Dictionary<(NotificationAudience, string), NotificationContent?>();
        async Task<NotificationContent?> ContentAsync(NotificationAudience audience, string lang)
        {
            if (contentCache.TryGetValue((audience, lang), out var cached)) return cached;
            var p = Provider();
            // una excepción del proveedor es una falla transitoria: sube y el aviso se reintenta a 1, 5 y 30 minutos
            var built = p is null ? null : await p.BuildAsync(new NotificationContentRequest(outbox.TenantId, outbox.EventCode, values, audience, lang), ct);
            contentCache[(audience, lang)] = built;
            return built;
        }

        foreach (var rule in rules)
        {
            // destinatarios fijos (ROLE, USER, EMAIL, PHONE): personal, en el idioma de la compañía
            var people = (await ResolveAsync(db, rule, ct)).Select(p => p with { Audience = NotificationAudience.Staff, Lang = companyLang }).ToList();
            var contextKeys = rule.Recipients.Where(r => r.Kind == NotificationRecipientKinds.Context && !string.IsNullOrWhiteSpace(r.Value))
                .OrderBy(r => r.NotificationRuleRecipientId).Select(r => r.Value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var contexts = new List<(NotificationContextRecipientDef Def, string Label, List<Person> People, string? EmptyReason)>();
            if (contextKeys.Count > 0)
            {
                var p = Provider();
                if (p is null)
                    logger.LogWarning("La regla de aviso {RuleId} tiene destinatarios del evento pero el evento {EventCode} no tiene proveedor de contenido; se omiten.", rule.NotificationRuleId, outbox.EventCode);
                else
                    foreach (var key in contextKeys)
                    {
                        var cdef = def.ContextRecipient(key);
                        if (cdef is null)
                        {
                            logger.LogWarning("La regla de aviso {RuleId} tiene el destinatario del evento {Key}, que el evento {EventCode} ya no declara; se omite.", rule.NotificationRuleId, key, outbox.EventCode);
                            continue;
                        }
                        var resolution = await p.RecipientsAsync(outbox.TenantId, outbox.EventCode, cdef.Key, values, ct);
                        var label = companyLang == "en" ? cdef.LabelEn : cdef.LabelEs;
                        var persons = (resolution?.People ?? Array.Empty<NotificationContextPerson>())
                            .Select(x => new Person(x.UserId, x.Name, x.Email?.Trim(), x.Phone?.Trim(), x.Audience, NormalizeLang(x.Lang) ?? companyLang)).ToList();
                        contexts.Add((cdef, label, persons, resolution?.EmptyReason));
                    }
            }

            foreach (var channel in rule.Channels.Where(c => c.IsEnabled).OrderBy(c => c.Channel))
            {
                if (!senders.TryGetValue(channel.Channel, out var sender)) continue;
                var isEmail = channel.Channel == NotificationChannels.Email;
                var customerChannel = channel.Channel is NotificationChannels.Email or NotificationChannels.Sms;   // el cliente solo por correo o SMS

                // DIAGNÓSTICO: un destinatario del evento que no deja a nadie alcanzable en el canal anota UNA fila SKIPPED con el motivo
                foreach (var c in contexts)
                {
                    if (c.Def.Audience == NotificationAudience.Customer && !customerChannel) continue;
                    if (c.People.Any(x => x.Reaches(channel.Channel))) continue;
                    var reason = !string.IsNullOrWhiteSpace(c.EmptyReason) ? c.EmptyReason! : NotificationRules.ContextUnreachableMessage(NotificationRules.ShortContextLabel(c.Label), channel.Channel);
                    await WriteSkippedAsync(db, outbox, rule, channel.Channel, NotificationRules.ContextLogRecipient(c.Label), reason, ct);
                }

                // todos los que alcanza el canal, sin repetir por la clave del registro: gana el primero (los fijos, luego los del evento en orden)
                var reach = people.Concat(contexts.SelectMany(c => c.People))
                    .Where(x => x.Reaches(channel.Channel) && (x.Audience == NotificationAudience.Staff || customerChannel))
                    .DistinctBy(x => x.LogKey(channel.Channel), StringComparer.OrdinalIgnoreCase).ToList();
                if (reach.Count == 0) continue;

                foreach (var group in reach.GroupBy(x => (x.Audience, Lang: x.Lang ?? companyLang)))
                {
                    var lang = group.Key.Lang;
                    var subjectTpl = NotificationRules.Pick(channel.SubjectJson, lang);
                    var bodyTpl = NotificationRules.Pick(channel.BodyJson, lang);
                    var usesBlock = NotificationRules.UsesAny(bodyTpl, blockNames);
                    var needsContent = contextKeys.Count > 0 || usesBlock || NotificationRules.UsesAny(subjectTpl, blockNames)
                        || NotificationRules.VariablesOf(subjectTpl).Concat(NotificationRules.VariablesOf(bodyTpl)).Any(v => !values.ContainsKey(v));
                    var content = needsContent ? await ContentAsync(group.Key.Audience, lang) : null;
                    var vals = content is null ? values : NotificationRules.Merge(values, content.Values);
                    var blocks = content?.Blocks;
                    var subject = NotificationRules.Render(subjectTpl, vals);
                    var text = NotificationRules.RenderText(bodyTpl, vals, blocks);
                    var html = isEmail ? NotificationHtml.Compose(bodyTpl, vals, blocks) : NotificationHtml.Paragraphs(text);
                    var attachments = isEmail && usesBlock && content is { Attachments.Count: > 0 } ? content.Attachments : null;
                    var link = def.LinkTemplate is null ? null : NotificationRules.Render(def.LinkTemplate, vals);

                    foreach (var person in group)
                    {
                        var recipient = person.LogKey(channel.Channel);
                        var log = await db.NotificationLogs.FirstOrDefaultAsync(l => l.NotificationOutboxId == outbox.NotificationOutboxId && l.NotificationRuleId == rule.NotificationRuleId
                            && l.Channel == channel.Channel && l.Recipient == recipient, ct);
                        if (log is not null && log.Status is NotificationLogStatuses.Sent or NotificationLogStatuses.Skipped) continue;   // ya resuelto en un intento anterior
                        if (log is null)
                        {
                            log = NewLog(outbox, rule, channel.Channel, recipient);
                            db.NotificationLogs.Add(log);
                        }
                        SendOutcome outcome;
                        if (content?.SkipReason is { } skip) outcome = SendOutcome.Skipped(skip);   // p. ej. la orden se canceló antes de enviar el aviso
                        else
                            try
                            {
                                outcome = await sender.SendAsync(new NotificationDelivery(outbox.TenantId, tenant.Name, outbox.EventCode, outbox.NotificationOutboxId, rule.NotificationRuleId,
                                    person.UserId, person.Email, person.Phone, subject, text, html, link, def.IsOptional, attachments), ct);
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                            catch (Exception ex) { outcome = SendOutcome.Failed(ex.Message); }
                        log.Attempts++;
                        log.Status = outcome.Status;
                        log.Error = outcome.Message is { Length: > 1000 } m ? m[..1000] : outcome.Message;
                        log.SentAtUtc = outcome.Status == NotificationLogStatuses.Sent ? DateTime.UtcNow : null;
                        await db.SaveChangesAsync(ct);
                        if (outcome.Status == NotificationLogStatuses.Failed) anyFailed = true;
                    }
                }
            }
        }
        return anyFailed;
    }

    private static NotificationLog NewLog(NotificationOutbox outbox, NotificationRule rule, string channel, string recipient) => new()
    {
        TenantId = outbox.TenantId, NotificationOutboxId = outbox.NotificationOutboxId, NotificationRuleId = rule.NotificationRuleId,
        EventCode = outbox.EventCode, Channel = channel, Recipient = recipient.Length <= 200 ? recipient : recipient[..200], CreatedAtUtc = DateTime.UtcNow,
    };

    /// <summary>Lote 243: anota (una sola vez) una fila SKIPPED con su motivo, p. ej. «(Vendedor de la orden)» sin usuario activo.</summary>
    private static async Task WriteSkippedAsync(TeikemDbContext db, NotificationOutbox outbox, NotificationRule rule, string channel, string recipient, string reason, CancellationToken ct)
    {
        var key = recipient.Length <= 200 ? recipient : recipient[..200];
        if (await db.NotificationLogs.AnyAsync(l => l.NotificationOutboxId == outbox.NotificationOutboxId && l.NotificationRuleId == rule.NotificationRuleId
            && l.Channel == channel && l.Recipient == key, ct)) return;
        var log = NewLog(outbox, rule, channel, key);
        log.Attempts = 1;
        log.Status = NotificationLogStatuses.Skipped;
        log.Error = reason.Length <= 1000 ? reason : reason[..1000];
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>«es» o «en» (sin distinguir mayúsculas); cualquier otro valor = null (se usa el de la compañía).</summary>
    private static string? NormalizeLang(string? lang)
    {
        var l = (lang ?? string.Empty).Trim().ToLowerInvariant();
        return l is "es" or "en" ? l : null;
    }

    /// <summary>
    /// Un destinatario resuelto: un usuario de la compañía (con su correo y teléfono) o un correo o teléfono suelto. Lote 243: Audience
    /// (personal o cliente) y Lang (idioma del texto; null = el de la compañía) para armar el texto por grupo.
    /// </summary>
    public sealed record Person(int? UserId, string? Name, string? Email, string? Phone, NotificationAudience Audience = NotificationAudience.Staff, string? Lang = null)
    {
        public bool Reaches(string channel) => channel switch
        {
            NotificationChannels.Email => !string.IsNullOrWhiteSpace(Email),
            NotificationChannels.Sms => !string.IsNullOrWhiteSpace(Phone),
            _ => UserId is not null,   // APP y PUSH: solo usuarios
        };

        /// <summary>Texto con el que se anota en el registro (y que evita duplicados): correo, teléfono o el usuario.</summary>
        public string LogKey(string channel) => channel switch
        {
            NotificationChannels.Email => Email!.Trim(),
            NotificationChannels.Sms => Phone!.Trim(),
            _ => string.IsNullOrWhiteSpace(Email) ? $"usuario {UserId}" : Email.Trim(),
        };
    }

    /// <summary>Destinatarios de una regla: roles → usuarios activos con ese rol en la compañía; usuarios activos; correos y teléfonos sueltos.</summary>
    public static async Task<List<Person>> ResolveAsync(TeikemDbContext db, NotificationRule rule, CancellationToken ct)
    {
        var people = new List<Person>();
        var roleIds = rule.Recipients.Where(r => r.Kind == NotificationRecipientKinds.Role && r.RoleId is not null).Select(r => r.RoleId!.Value).ToList();
        var userIds = rule.Recipients.Where(r => r.Kind == NotificationRecipientKinds.User && r.UserId is not null).Select(r => r.UserId!.Value).ToList();
        if (roleIds.Count > 0)
            userIds.AddRange(await db.AppUserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync(ct));
        userIds = userIds.Distinct().ToList();
        if (userIds.Count > 0)
        {
            // Lote 243: solo la membresía ACTIVE (un suspendido en la compañía conserva su fila y su usuario activo, pero ya no tiene acceso)
            var members = await db.UserTenants.AsNoTracking()
                .Where(m => userIds.Contains(m.UserId)
                    && db.StatusCodes.Any(s => s.StatusCodeId == m.StatusCodeId && s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active))
                .Select(m => m.UserId).ToListAsync(ct);
            var users = await db.Users.AsNoTracking().Where(u => members.Contains(u.Id) && u.IsActive).Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber }).ToListAsync(ct);
            people.AddRange(users.OrderBy(u => u.Id).Select(u => new Person(u.Id, u.FullName, u.Email, u.PhoneNumber)));
        }
        foreach (var r in rule.Recipients.Where(r => r.Kind == NotificationRecipientKinds.Email && !string.IsNullOrWhiteSpace(r.Value))) people.Add(new Person(null, null, r.Value!.Trim(), null));
        foreach (var r in rule.Recipients.Where(r => r.Kind == NotificationRecipientKinds.Phone && !string.IsNullOrWhiteSpace(r.Value))) people.Add(new Person(null, null, null, r.Value!.Trim()));
        return people;
    }
}

/// <summary>
/// Lote 51 — ciclo de fondo de los avisos: cada PollSeconds (10 por omisión) procesa lo pendiente. Los comandos de consola no llegan a
/// app.Run(): no arranca. Notifications:Enabled = false lo apaga. Un error nunca detiene el ciclo.
/// </summary>
public sealed class NotificationWorker(NotificationProcessor processor, IOptions<NotificationsOptions> options, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Avisos apagados (Notifications:Enabled = false).");
            return;
        }
        var poll = TimeSpan.FromSeconds(Math.Max(2, opts.PollSeconds));
        logger.LogInformation("Avisos iniciados (cada {Seconds} s, copia {Instance}).", poll.TotalSeconds, processor.InstanceId);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await processor.ProcessDueAsync(DateTime.UtcNow, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { logger.LogError(ex, "Error en el ciclo de avisos; se continúa."); }
                await Task.Delay(poll, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
