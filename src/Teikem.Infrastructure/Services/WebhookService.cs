using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 136 (P11) — administración de los webhooks de salida de una integración de tipo API y de la bandeja de SALIDAS no entregadas. Lo usan el personal (integrations.manage / integrations.inbox, desde
/// la pantalla) y el propio cliente con su llave (alcance «webhooks:manage», solo sobre los de SU integración). El secreto de firma se muestra una sola vez (al crear o rotar) y se guarda cifrado.
/// </summary>
public sealed class WebhookService(TeikemDbContext db, ITenantContext tenant, ISecretProtector protector, WebhookSender sender, ApiKeyService apiKeys)
{
    public const string NotFoundLabel = "Webhook";
    public const string DeliveryNotFoundLabel = "Salida";
    public const string SecretNotConfiguredMessage = "No se pueden guardar secretos de webhooks: falta la llave maestra del servidor (Files:MasterKey).";
    public const string SuspendedMessage = "El webhook está suspendido: reactívelo antes de reintentar.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>La integración de la llave con la que se llamó (para el API del cliente: solo ve y toca lo de SU integración).</summary>
    public Task<Guid> IntegrationOfKeyAsync(string keyId, CancellationToken ct) => apiKeys.IntegrationPublicIdOfKeyAsync(keyId, ct);

    // ---------------------------------------------------------------- webhooks

    public async Task<IReadOnlyList<WebhookDto>> ListAsync(Guid integrationPublicId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var rows = await db.IntegrationWebhooks.AsNoTracking().Where(w => w.IntegrationId == integration.IntegrationId && w.RemovedAtUtc == null).OrderBy(w => w.IntegrationWebhookId).ToListAsync(ct);
        return rows.Select(w => ToDto(w, integration)).ToList();
    }

    public async Task<WebhookDto> GetAsync(Guid integrationPublicId, Guid webhookId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        return ToDto(await LoadWebhookAsync(integration, webhookId, ct), integration);
    }

    public async Task<WebhookCreatedDto> CreateAsync(Guid integrationPublicId, WebhookCreateRequest req, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        if (!integration.IsActive) throw new StatusRuleException(IntegrationRules.InactiveMessage);
        if (integration.ClientId is null) throw new ValidationException("clientPublicId", WebhookRules.NeedsClientMessage);
        if (!IntegrationService.ParseStrings(integration.ScopesJson).Contains(ApiScopes.WebhooksManage, StringComparer.OrdinalIgnoreCase)) throw new ValidationException("scopes", WebhookRules.NeedsScopeMessage);
        var urlError = WebhookRules.ValidateUrl(req.Url, sender.AllowInsecure);
        if (urlError is not null) throw new ValidationException("url", urlError);
        var eventsError = WebhookRules.ValidateEvents(req.Events, out var events);
        if (eventsError is not null) throw new ValidationException("events", eventsError);
        if (await db.IntegrationWebhooks.CountAsync(w => w.IntegrationId == integration.IntegrationId && w.RemovedAtUtc == null, ct) >= WebhookRules.MaxPerIntegration) throw new ConflictException(WebhookRules.TooManyMessage);
        if (!protector.IsConfigured) throw new ValidationException("secret", SecretNotConfiguredMessage);

        var secret = WebhookRules.NewSecret();
        var row = new IntegrationWebhook
        {
            TenantId = integration.TenantId, IntegrationId = integration.IntegrationId, Url = req.Url.Trim(), EventsJson = JsonSerializer.Serialize(events, Json),
            SecretEnc = protector.Protect(integration.TenantId, WebhookSender.SecretPurpose, secret), SecretLast4 = WebhookRules.Last4(secret), CreatedBy = tenant.UserId, CreatedAtUtc = DateTime.UtcNow,
        };
        db.IntegrationWebhooks.Add(row);
        await db.SaveChangesAsync(ct);
        return new WebhookCreatedDto(ToDto(row, integration), secret);
    }

    public async Task<WebhookDto> UpdateAsync(Guid integrationPublicId, Guid webhookId, WebhookPatchRequest req, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var row = await LoadWebhookAsync(integration, webhookId, ct);
        if (req.Url is not null)
        {
            var urlError = WebhookRules.ValidateUrl(req.Url, sender.AllowInsecure);
            if (urlError is not null) throw new ValidationException("url", urlError);
            row.Url = req.Url.Trim();
        }
        if (req.Events is not null)
        {
            var eventsError = WebhookRules.ValidateEvents(req.Events, out var events);
            if (eventsError is not null) throw new ValidationException("events", eventsError);
            row.EventsJson = JsonSerializer.Serialize(events, Json);
        }
        if (req.IsActive is bool active)
        {
            row.IsActive = active;
            if (active && row.SuspendedAtUtc is not null) { row.SuspendedAtUtc = null; row.ConsecutiveFailures = 0; }   // encenderlo a mano también lo reactiva
        }
        await db.SaveChangesAsync(ct);
        return ToDto(row, integration);
    }

    /// <summary>Reactiva un webhook suspendido (borra el conteo de fallas).</summary>
    public async Task<WebhookDto> ResumeAsync(Guid integrationPublicId, Guid webhookId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var row = await LoadWebhookAsync(integration, webhookId, ct);
        if (row.SuspendedAtUtc is null) throw new ConflictException(WebhookRules.NotSuspendedMessage);
        row.SuspendedAtUtc = null;
        row.ConsecutiveFailures = 0;
        row.IsActive = true;
        await db.SaveChangesAsync(ct);
        return ToDto(row, integration);
    }

    /// <summary>Quita el webhook (no se borra: se desactiva y deja de aparecer; sus salidas pendientes se descartan y el historial queda).</summary>
    public async Task RemoveAsync(Guid integrationPublicId, Guid webhookId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var row = await LoadWebhookAsync(integration, webhookId, ct);
        row.IsActive = false;
        row.RemovedAtUtc = DateTime.UtcNow;
        await db.IntegrationWebhookDeliveries.Where(d => d.IntegrationWebhookId == row.IntegrationWebhookId && d.Status == WebhookDeliveryStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, WebhookDeliveryStatuses.Discarded), ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cambia el secreto de firma: el nuevo viene en la respuesta (una sola vez) y el anterior deja de servir ya.</summary>
    public async Task<WebhookCreatedDto> RotateSecretAsync(Guid integrationPublicId, Guid webhookId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var row = await LoadWebhookAsync(integration, webhookId, ct);
        if (!protector.IsConfigured) throw new ValidationException("secret", SecretNotConfiguredMessage);
        var secret = WebhookRules.NewSecret();
        row.SecretEnc = protector.Protect(row.TenantId, WebhookSender.SecretPurpose, secret);
        row.SecretLast4 = WebhookRules.Last4(secret);
        await db.SaveChangesAsync(ct);
        return new WebhookCreatedDto(ToDto(row, integration), secret);
    }

    /// <summary>Manda ya un evento «webhook.ping» firmado a la URL y dice cómo respondió (no queda en la bandeja ni cuenta como falla).</summary>
    public async Task<WebhookTestResultDto> TestAsync(Guid integrationPublicId, Guid webhookId, CancellationToken ct)
    {
        var integration = await LoadIntegrationAsync(integrationPublicId, ct);
        var row = await LoadWebhookAsync(integration, webhookId, ct);
        var eventId = Guid.NewGuid();
        var body = WebhookRules.BuildBody(eventId, WebhookEvents.Ping, DateTime.UtcNow, new { webhookId = row.PublicId, message = "Prueba de conexión de Teikem." });
        var r = await sender.SendAsync(row, eventId, WebhookEvents.Ping, body, ct);
        return new WebhookTestResultDto(r.Ok, r.ResponseCode, r.Ok ? $"Respondió {r.ResponseCode} en {r.ElapsedMs} ms." : r.Error ?? "No se pudo entregar.");
    }

    // ---------------------------------------------------------------- salidas (la bandeja)

    /// <summary>Las salidas de una integración (o de todas) por estatus. «Undelivered» cuenta las no entregadas pendientes de atender (el contador de la pestaña).</summary>
    public async Task<WebhookDeliveryPageDto> DeliveriesAsync(Guid? integrationPublicId, Guid? webhookPublicId, string? status, int skip, int take, bool withPayload, CancellationToken ct)
    {
        var query = from d in db.IntegrationWebhookDeliveries.AsNoTracking()
                    join w in db.IntegrationWebhooks.AsNoTracking() on d.IntegrationWebhookId equals w.IntegrationWebhookId
                    join i in db.Integrations.AsNoTracking() on w.IntegrationId equals i.IntegrationId
                    select new { d, w, i };
        if (integrationPublicId is Guid ip) query = query.Where(x => x.i.PublicId == ip);
        if (webhookPublicId is Guid wp) query = query.Where(x => x.w.PublicId == wp);
        var undelivered = await query.CountAsync(x => x.d.Status == WebhookDeliveryStatuses.Undelivered, ct);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            if (!WebhookDeliveryStatuses.All.Contains(s)) throw new ValidationException("status", $"Estatus desconocido: {status}. Use {string.Join(", ", WebhookDeliveryStatuses.All)}.");
            query = query.Where(x => x.d.Status == s);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.d.IntegrationWebhookDeliveryId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take <= 0 ? 50 : take, 1, 200)).ToListAsync(ct);
        return new WebhookDeliveryPageDto(rows.Select(x => ToDto(x.d, x.w, x.i, withPayload)).ToList(), total, undelivered);
    }

    /// <summary>Reintenta una salida no entregada: vuelve a PENDING con el conteo de intentos en cero y sale en la siguiente vuelta del trabajo.</summary>
    public async Task<WebhookDeliveryDto> RetryAsync(long id, CancellationToken ct)
    {
        var (d, w, i) = await LoadDeliveryAsync(id, ct);
        if (d.Status != WebhookDeliveryStatuses.Undelivered) throw new ConflictException(WebhookRules.NotUndeliveredMessage);
        if (w.RemovedAtUtc is not null) throw new ConflictException(WebhookRules.RemovedMessage);
        if (w.SuspendedAtUtc is not null || !w.IsActive) throw new ConflictException(SuspendedMessage);
        d.Status = WebhookDeliveryStatuses.Pending;
        d.AttemptCount = 0;
        d.NextAttemptUtc = DateTime.UtcNow;
        d.Error = null;
        d.ResolvedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(d, w, i, true);
    }

    /// <summary>Descarta una salida que ya no se quiere entregar.</summary>
    public async Task<WebhookDeliveryDto> DiscardAsync(long id, CancellationToken ct)
    {
        var (d, w, i) = await LoadDeliveryAsync(id, ct);
        if (d.Status == WebhookDeliveryStatuses.Discarded) throw new ConflictException(WebhookRules.AlreadyDiscardedMessage);
        if (d.Status == WebhookDeliveryStatuses.Delivered) throw new ConflictException(WebhookRules.NotPendingMessage);
        d.Status = WebhookDeliveryStatuses.Discarded;
        d.ResolvedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(d, w, i, true);
    }

    // ---------------------------------------------------------------- internos

    private async Task<Integration> LoadIntegrationAsync(Guid publicId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.ConnectorKind != IntegrationConnectorKinds.Api) throw new ValidationException("connectorKind", WebhookRules.NotApiMessage);
        return integration;
    }

    private async Task<IntegrationWebhook> LoadWebhookAsync(Integration integration, Guid webhookId, CancellationToken ct)
    {
        var row = await db.IntegrationWebhooks.FirstOrDefaultAsync(w => w.PublicId == webhookId && w.IntegrationId == integration.IntegrationId, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (row.RemovedAtUtc is not null) throw new NotFoundException(NotFoundLabel);   // un webhook quitado ya no existe para el cliente
        return row;
    }

    private async Task<(IntegrationWebhookDelivery D, IntegrationWebhook W, Integration I)> LoadDeliveryAsync(long id, CancellationToken ct)
    {
        var d = await db.IntegrationWebhookDeliveries.FirstOrDefaultAsync(x => x.IntegrationWebhookDeliveryId == id, ct) ?? throw new NotFoundException(DeliveryNotFoundLabel);
        var w = await db.IntegrationWebhooks.FirstAsync(x => x.IntegrationWebhookId == d.IntegrationWebhookId, ct);
        var i = await db.Integrations.AsNoTracking().FirstAsync(x => x.IntegrationId == w.IntegrationId, ct);
        return (d, w, i);
    }

    private static WebhookDto ToDto(IntegrationWebhook w, Integration i)
        => new(w.PublicId, i.PublicId, i.Name, w.Url, IntegrationService.ParseStrings(w.EventsJson), w.SecretLast4, w.IsActive, w.ConsecutiveFailures, w.SuspendedAtUtc, w.LastDeliveryUtc, w.LastStatus, w.CreatedAtUtc);

    private static WebhookDeliveryDto ToDto(IntegrationWebhookDelivery d, IntegrationWebhook w, Integration i, bool withPayload)
        => new(d.IntegrationWebhookDeliveryId, w.PublicId, i.PublicId, i.Name, w.Url, d.EventType, d.Status, d.AttemptCount, d.NextAttemptUtc, d.LastAttemptUtc, d.ResponseCode, d.Error, d.CreatedAtUtc, d.DeliveredAtUtc,
            withPayload ? d.PayloadJson : null);
}
