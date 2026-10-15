using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    /// <summary>Envía el aviso por todas las reglas activas del evento. Devuelve true si algún envío FALLÓ (y hay que reintentar).</summary>
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
        var senders = sp.GetServices<INotificationChannelSender>().ToDictionary(s => s.Channel, StringComparer.Ordinal);
        var link = def.LinkTemplate is null ? null : NotificationRules.Render(def.LinkTemplate, values);
        var anyFailed = false;

        foreach (var rule in rules)
        {
            var people = await ResolveAsync(db, rule, ct);
            foreach (var channel in rule.Channels.Where(c => c.IsEnabled).OrderBy(c => c.Channel))
            {
                if (!senders.TryGetValue(channel.Channel, out var sender)) continue;
                var subject = NotificationRules.Render(NotificationRules.Pick(channel.SubjectJson, tenant.DefaultLangCode), values);
                var body = NotificationRules.Render(NotificationRules.Pick(channel.BodyJson, tenant.DefaultLangCode), values);
                foreach (var person in people.Where(p => p.Reaches(channel.Channel)).DistinctBy(p => p.LogKey(channel.Channel), StringComparer.OrdinalIgnoreCase))
                {
                    var recipient = person.LogKey(channel.Channel);
                    var log = await db.NotificationLogs.FirstOrDefaultAsync(l => l.NotificationOutboxId == outbox.NotificationOutboxId && l.NotificationRuleId == rule.NotificationRuleId
                        && l.Channel == channel.Channel && l.Recipient == recipient, ct);
                    if (log is not null && log.Status is NotificationLogStatuses.Sent or NotificationLogStatuses.Skipped) continue;   // ya resuelto en un intento anterior
                    if (log is null)
                    {
                        log = new NotificationLog
                        {
                            TenantId = outbox.TenantId, NotificationOutboxId = outbox.NotificationOutboxId, NotificationRuleId = rule.NotificationRuleId,
                            EventCode = outbox.EventCode, Channel = channel.Channel, Recipient = recipient, CreatedAtUtc = DateTime.UtcNow,
                        };
                        db.NotificationLogs.Add(log);
                    }
                    SendOutcome outcome;
                    try
                    {
                        outcome = await sender.SendAsync(new NotificationDelivery(outbox.TenantId, tenant.Name, outbox.EventCode, outbox.NotificationOutboxId, rule.NotificationRuleId,
                            person.UserId, person.Email, person.Phone, subject, body, NotificationHtml.Paragraphs(body), link, def.IsOptional), ct);
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
        return anyFailed;
    }

    /// <summary>Un destinatario resuelto: un usuario de la compañía (con su correo y teléfono) o un correo o teléfono suelto.</summary>
    public sealed record Person(int? UserId, string? Name, string? Email, string? Phone)
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
            var members = await db.UserTenants.AsNoTracking().Where(m => userIds.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
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
