using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Integrations;
using Teikem.Domain.Notifications;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 132 (P11) — los acuses de una integración son avisos del sistema a quienes se definieron en ella. Cada integración administra UNA regla de aviso (NotificationRule con
/// ScopeKey = «integration:{id}») con esos usuarios y los canales APP y correo; el ejecutor publica el aviso de cada corrida con esa clave.
/// </summary>
public sealed class IntegrationNotifier(TeikemDbContext db, ITenantContext tenant, NotificationEventCatalog catalog, INotificationPublisher publisher)
{
    public static string ScopeKeyOf(int integrationId) => $"integration:{integrationId}";

    /// <summary>Crea o actualiza la regla de avisos de la integración (activa solo si la integración está encendida y tiene a quién avisar). Guarda los cambios.</summary>
    public async Task SyncRuleAsync(Integration row, CancellationToken ct)
    {
        await SyncOneAsync(row, IntegrationEventCodes.RunFinished, "avisos", true, ct);
        await SyncOneAsync(row, IntegrationEventCodes.WebhookSuspended, "webhooks", false, ct);   // Lote 136: misma gente, otro aviso
    }

    private async Task SyncOneAsync(Integration row, string eventCode, string suffix, bool setRuleId, CancellationToken ct)
    {
        var def = catalog.Find(eventCode) ?? throw new InvalidOperationException($"Falta el evento {eventCode}.");
        var scopeKey = ScopeKeyOf(row.IntegrationId);
        var userIds = IntegrationService.ParseIds(row.NotifyUserIdsJson).Distinct().ToList();
        var rule = await db.NotificationRules.Include(r => r.Channels).Include(r => r.Recipients).FirstOrDefaultAsync(r => r.ScopeKey == scopeKey && r.EventCode == eventCode, ct);
        if (rule is null && userIds.Count == 0) return;
        var name = $"Integración: {row.Name} ({suffix})";
        if (name.Length > 100) name = name[..100];
        var now = DateTime.UtcNow;
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (rule is null)
        {
            rule = new NotificationRule { TenantId = tenantId, EventCode = def.Code, Name = name, ScopeKey = scopeKey, CreatedAtUtc = now, CreatedBy = tenant.UserId };
            foreach (var channel in new[] { NotificationChannels.App, NotificationChannels.Email })
                rule.Channels.Add(new NotificationRuleChannel
                {
                    TenantId = tenantId, Channel = channel, IsEnabled = true,
                    SubjectJson = MultilingualText.Serialize(new Dictionary<string, string> { ["es"] = def.DefaultSubjectEs, ["en"] = def.DefaultSubjectEn }),
                    BodyJson = MultilingualText.Serialize(new Dictionary<string, string> { ["es"] = def.DefaultBodyEs, ["en"] = def.DefaultBodyEn }),
                });
            db.NotificationRules.Add(rule);
        }
        else
        {
            rule.Name = name;
            rule.UpdatedAtUtc = now;
            rule.UpdatedBy = tenant.UserId;
            db.RemoveRange(rule.Recipients);
            rule.Recipients.Clear();
        }
        foreach (var id in userIds) rule.Recipients.Add(new NotificationRuleRecipient { TenantId = tenantId, Kind = NotificationRecipientKinds.User, UserId = id });
        rule.IsActive = row.IsActive && userIds.Count > 0;
        await db.SaveChangesAsync(ct);
        if (setRuleId) row.NotifyRuleId = rule.NotificationRuleId;
    }

    /// <summary>Avisa del resultado de una corrida (solo si hizo algo que reportar: órdenes creadas, filas a la bandeja o una falla).</summary>
    public async Task PublishRunAsync(Integration integration, IntegrationRun run, CancellationToken ct)
    {
        if (run.OrdersCreated == 0 && run.Rejected == 0 && run.Status != IntegrationRunStatuses.Failed) return;
        var status = run.Status switch
        {
            IntegrationRunStatuses.Ok => "Terminó bien",
            IntegrationRunStatuses.Partial => "Terminó con pendientes en la bandeja",
            _ => "Falló",
        };
        var values = new Dictionary<string, string?>
        {
            ["integration"] = integration.Name, ["status"] = status, ["files"] = run.FilesRead.ToString(), ["created"] = run.OrdersCreated.ToString(), ["rejected"] = run.Rejected.ToString(),
            ["message"] = run.Message ?? string.Empty, ["integrationId"] = integration.IntegrationId.ToString(), ["scopeKey"] = ScopeKeyOf(integration.IntegrationId),
        };
        await publisher.PublishAsync(IntegrationEventCodes.RunFinished, values, $"integration-run-{run.IntegrationRunId}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lote 136: avisa a quienes se definieron en la integración que un webhook se suspendió solo.</summary>
    public async Task PublishWebhookSuspendedAsync(Integration integration, IntegrationWebhook webhook, string? reason, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>
        {
            ["integration"] = integration.Name, ["url"] = webhook.Url, ["failures"] = webhook.ConsecutiveFailures.ToString(), ["message"] = reason ?? string.Empty,
            ["integrationId"] = integration.IntegrationId.ToString(), ["scopeKey"] = ScopeKeyOf(integration.IntegrationId),
        };
        await publisher.PublishAsync(IntegrationEventCodes.WebhookSuspended, values, $"integration-webhook-{webhook.IntegrationWebhookId}-{webhook.SuspendedAtUtc:yyyyMMddHHmmss}", ct);
        await db.SaveChangesAsync(ct);
    }
}
