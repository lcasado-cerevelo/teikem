using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
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

    /// <summary>Clave de la regla que administra Teikem para avisar de los avisos de llegada / de salida creados por integraciones.</summary>
    public const string ArrivalScopeKey = "integration-notices:arrival";
    public const string ExitScopeKey = "integration-notices:exit";

    /// <summary>
    /// 2026-10-24: avisa de los avisos de llegada y de salida que creó una corrida: UN aviso por tipo y corrida (diez avisos de salida de una misma tanda = una sola notificación), por APP y correo, a los usuarios
    /// de los roles que tienen el permiso de crearlos (llegada: warehouse.receive; salida: exitnotice.create). La regla (NotificationRule con ScopeKey propio) la administra Teikem: en cada aviso se le ponen los roles que
    /// HOY tienen el permiso, así que un rol nuevo entra solo; quien no quiera recibirlos los silencia en sus preferencias. Si ningún rol tiene el permiso, no se avisa a nadie.
    /// </summary>
    public async Task PublishNoticesAsync(Integration integration, IntegrationRun run, IReadOnlyList<(string Target, int Count, string Document)> created, CancellationToken ct)
    {
        foreach (var (target, eventCode, permission, scopeKey) in new[]
        {
            (DocumentTargets.ArrivalNotice, IntegrationEventCodes.ArrivalNotices, PermissionCatalog.WarehouseReceive, ArrivalScopeKey),
            (DocumentTargets.ExitNotice, IntegrationEventCodes.ExitNotices, PermissionCatalog.ExitNoticeCreate, ExitScopeKey),
        })
        {
            var mine = created.Where(c => c.Target == target).ToList();
            if (mine.Count == 0) continue;
            var tenantId = ((TenantContext)tenant).RequireTenantId();
            var roleIds = await db.RolePermissions.AsNoTracking()
                .Where(rp => rp.Permission!.Code == permission && rp.Role!.IsActive && rp.Role.TenantId == tenantId).Select(rp => rp.RoleId).Distinct().ToListAsync(ct);
            await SyncRoleRuleAsync(eventCode, scopeKey, roleIds, ct);
            var documents = string.Join(", ", mine.Select(m => m.Document).Distinct().Take(5)) + (mine.Select(m => m.Document).Distinct().Count() > 5 ? "…" : string.Empty);
            var values = new Dictionary<string, string?>
            {
                ["integration"] = integration.Name, ["count"] = mine.Sum(m => m.Count).ToString(), ["documents"] = documents,
                ["integrationId"] = integration.IntegrationId.ToString(), ["scopeKey"] = scopeKey,
            };
            await publisher.PublishAsync(eventCode, values, $"integration-notices-{target}-{run.IntegrationRunId}", ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>La regla administrada por Teikem de un tipo de aviso: canales APP y correo y, como destinatarios, los roles dados (activa solo si hay alguno).</summary>
    private async Task SyncRoleRuleAsync(string eventCode, string scopeKey, IReadOnlyList<int> roleIds, CancellationToken ct)
    {
        var def = catalog.Find(eventCode) ?? throw new InvalidOperationException($"Falta el evento {eventCode}.");
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var rule = await db.NotificationRules.Include(r => r.Channels).Include(r => r.Recipients).FirstOrDefaultAsync(r => r.ScopeKey == scopeKey && r.EventCode == eventCode, ct);
        var now = DateTime.UtcNow;
        if (rule is null)
        {
            rule = new NotificationRule { TenantId = tenantId, EventCode = def.Code, Name = scopeKey == ArrivalScopeKey ? "Integraciones: avisos de llegada creados" : "Integraciones: avisos de salida creados", ScopeKey = scopeKey, CreatedAtUtc = now, CreatedBy = tenant.UserId };
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
            rule.UpdatedAtUtc = now;
            rule.UpdatedBy = tenant.UserId;
            db.RemoveRange(rule.Recipients);
            rule.Recipients.Clear();
        }
        foreach (var id in roleIds) rule.Recipients.Add(new NotificationRuleRecipient { TenantId = tenantId, Kind = NotificationRecipientKinds.Role, RoleId = id });
        rule.IsActive = roleIds.Count > 0;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Avisa del resultado de una corrida (solo si hizo algo que reportar: órdenes creadas, filas a la bandeja, documentos de lectura dudosa o una falla).</summary>
    public async Task PublishRunAsync(Integration integration, IntegrationRun run, CancellationToken ct, int doubtful = 0)
    {
        if (run.OrdersCreated == 0 && run.Rejected == 0 && doubtful == 0 && run.Status != IntegrationRunStatuses.Failed) return;
        var status = run.Status switch
        {
            IntegrationRunStatuses.Ok when doubtful > 0 => "Terminó con lectura dudosa: verifique lo creado",
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
