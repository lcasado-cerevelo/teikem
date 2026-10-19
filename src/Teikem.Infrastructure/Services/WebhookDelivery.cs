using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Security;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 136 — ajustes de los webhooks (sección «Integrations:Webhooks»). AllowInsecure SOLO para desarrollo y pruebas: admite http y direcciones internas.</summary>
public sealed class WebhookOptions
{
    public const string Section = "Integrations:Webhooks";
    public bool AllowInsecure { get; set; }
}

/// <summary>Resultado de un intento de envío: éxito (2xx), código HTTP si respondieron y el motivo si falló.</summary>
public sealed record WebhookSendResult(bool Ok, int? ResponseCode, string? Error, int ElapsedMs);

/// <summary>
/// Lote 136 (P11) — envía un webhook: POST con el cuerpo mínimo y la firma HMAC-SHA256 de «marca de tiempo.cuerpo» con el secreto del webhook (cabeceras X-Teikem-*). Nunca sigue redirecciones y,
/// salvo AllowInsecure, la conexión se niega a direcciones internas (se revisa la dirección ya resuelta, contra el rebote por DNS).
/// </summary>
public sealed class WebhookSender(IHttpClientFactory http, ISecretProtector protector, IOptions<WebhookOptions> options)
{
    public const string ClientName = "teikem-webhooks";
    public const string SecretPurpose = "webhook-secret";

    /// <summary>Crea el manejador del cliente HTTP con la protección de direcciones internas (ver DependencyInjection).</summary>
    public static SocketsHttpHandler CreateHandler(bool allowInternal) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(WebhookRules.TimeoutSeconds),
        ConnectCallback = async (ctx, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
            var allowed = addresses.Where(a => allowInternal || !WebhookRules.IsInternalAddress(a)).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException(WebhookRules.UrlInternalMessage);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, ctx.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        },
    };

    public bool AllowInsecure => options.Value.AllowInsecure;

    public async Task<WebhookSendResult> SendAsync(IntegrationWebhook webhook, Guid eventId, string eventType, string body, CancellationToken ct)
    {
        string secret;
        try { secret = protector.Unprotect(webhook.TenantId, SecretPurpose, webhook.SecretEnc); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return new WebhookSendResult(false, null, "No se pudo leer el secreto de firma del webhook; rótelo.", 0); }
        var urlError = WebhookRules.ValidateUrl(webhook.Url, options.Value.AllowInsecure);
        if (urlError is not null) return new WebhookSendResult(false, null, urlError, 0);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("User-Agent", "Teikem-Webhooks/1.0");
        request.Headers.TryAddWithoutValidation(WebhookRules.EventHeader, eventType);
        request.Headers.TryAddWithoutValidation(WebhookRules.DeliveryHeader, eventId.ToString());
        request.Headers.TryAddWithoutValidation(WebhookRules.TimestampHeader, timestamp.ToString());
        request.Headers.TryAddWithoutValidation(WebhookRules.SignatureHeader, WebhookRules.Sign(secret, timestamp, body));

        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(WebhookRules.TimeoutSeconds));
        try
        {
            using var response = await http.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var code = (int)response.StatusCode;
            return code is >= 200 and <= 299
                ? new WebhookSendResult(true, code, null, (int)watch.ElapsedMilliseconds)
                : new WebhookSendResult(false, code, $"El servidor respondió {code}.", (int)watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new WebhookSendResult(false, null, $"Tiempo de espera agotado ({WebhookRules.TimeoutSeconds} s).", (int)watch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            return new WebhookSendResult(false, null, Trim($"No se pudo conectar: {reason}"), (int)watch.ElapsedMilliseconds);
        }
    }

    private static string Trim(string s) => s.Length <= 480 ? s : s[..480];
}

/// <summary>
/// Lote 136 — anota las salidas (webhooks) que provoca un hecho: una fila PENDING por cada webhook activo, no suspendido y suscrito al evento, de las integraciones API encendidas del cliente. No guarda
/// (la guarda quien cambió el estatus, junto con el cambio: si el cambio se deshace, la salida también).
/// </summary>
public sealed class WebhookRecorder(TeikemDbContext db)
{
    public async Task RecordAsync(string eventType, int clientId, object data, CancellationToken ct)
    {
        var webhooks = await db.IntegrationWebhooks.AsNoTracking()
            .Where(w => w.IsActive && w.RemovedAtUtc == null && w.SuspendedAtUtc == null
                && db.Integrations.Any(i => i.IntegrationId == w.IntegrationId && i.IsActive && i.ConnectorKind == IntegrationConnectorKinds.Api && i.ClientId == clientId))
            .ToListAsync(ct);
        foreach (var w in webhooks.Where(w => IntegrationService.ParseStrings(w.EventsJson).Contains(eventType, StringComparer.OrdinalIgnoreCase)))
        {
            var now = DateTime.UtcNow;
            var eventId = Guid.NewGuid();
            db.IntegrationWebhookDeliveries.Add(new IntegrationWebhookDelivery
            {
                TenantId = w.TenantId, IntegrationWebhookId = w.IntegrationWebhookId, EventId = eventId, EventType = eventType,
                PayloadJson = WebhookRules.BuildBody(eventId, eventType, now, data), Status = WebhookDeliveryStatuses.Pending, NextAttemptUtc = now, CreatedAtUtc = now,
            });
        }
    }
}

/// <summary>Lote 136 — efecto del dominio OrderStatus: cada cambio de estatus de una orden anota «order.status_changed» y, al entregarse, también «order.delivered».</summary>
public sealed class WebhookOrderEffect(TeikemDbContext db, WebhookRecorder recorder) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        var order = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == context.EntityId)
                    ?? await db.TransportOrders.AsNoTracking().FirstOrDefaultAsync(o => o.TransportOrderId == context.EntityId, ct);
        if (order is null) return;
        var at = DateTime.UtcNow;
        await recorder.RecordAsync(WebhookEvents.OrderStatusChanged, order.ClientId, new { orderId = order.PublicId, orderNumber = order.OrderNumber, status = context.To.InternalCode, previousStatus = context.From?.InternalCode, occurredAt = at }, ct);
        if (string.Equals(context.To.InternalCode, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase))
            await recorder.RecordAsync(WebhookEvents.OrderDelivered, order.ClientId, new { orderId = order.PublicId, orderNumber = order.OrderNumber, status = context.To.InternalCode, deliveredAt = at }, ct);
    }
}

/// <summary>Lote 136 — efecto del dominio InvoiceStatus: una factura emitida anota «invoice.issued».</summary>
public sealed class WebhookInvoiceEffect(TeikemDbContext db, WebhookRecorder recorder) : IStatusTransitionEffect
{
    public string StatusDomain => BillingCodes.InvoiceStatusDomain;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, BillingCodes.InvoiceEntityType, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(context.To.InternalCode, InvoiceStatuses.Issued, StringComparison.OrdinalIgnoreCase)) return;
        var invoice = db.Set<Invoice>().Local.FirstOrDefault(i => i.InvoiceId == context.EntityId)
                      ?? await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceId == context.EntityId, ct);
        if (invoice is null) return;
        await recorder.RecordAsync(WebhookEvents.InvoiceIssued, invoice.ClientId, new { invoiceId = invoice.PublicId, invoiceNumber = invoice.InvoiceNumber, status = context.To.InternalCode, issuedAt = DateTime.UtcNow }, ct);
    }
}

/// <summary>
/// Lote 136 — el trabajo que entrega: toma las salidas PENDING que ya tocan, las envía y anota el resultado. Éxito → DELIVERED. Fallo → siguiente intento con espera creciente (1 min, 5, 30, 2 h, 12 h);
/// agotados, UNDELIVERED (bandeja de salidas no entregadas) y, tras N seguidas sin lograrse, el webhook se SUSPENDE (y se avisa a quienes se definieron en la integración).
/// </summary>
public sealed class WebhookProcessor(TeikemDbContext db, WebhookSender sender, IntegrationNotifier notifier)
{
    public sealed record Outcome(int Sent, int Retrying, int Undelivered, int Suspended, int Purged);

    public async Task<Outcome> ProcessDueAsync(DateTime nowUtc, int max, CancellationToken ct)
    {
        int sent = 0, retrying = 0, undelivered = 0, suspended = 0;
        var due = await db.IntegrationWebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatuses.Pending && d.NextAttemptUtc <= nowUtc
                && db.IntegrationWebhooks.Any(w => w.IntegrationWebhookId == d.IntegrationWebhookId && w.IsActive && w.RemovedAtUtc == null && w.SuspendedAtUtc == null))
            .OrderBy(d => d.IntegrationWebhookDeliveryId).Take(max).ToListAsync(ct);
        // los envíos van en paralelo entre webhooks distintos (8 a la vez) y en orden dentro de cada uno; la base se actualiza después, en un solo hilo. Si un webhook falla 3 veces seguidas en esta
        // vuelta, no se insiste con el resto de sus salidas (esperan a la siguiente: así un servidor caído no se come el tiempo del trabajo)
        var webhooks = await db.IntegrationWebhooks.Where(w => due.Select(d => d.IntegrationWebhookId).Contains(w.IntegrationWebhookId)).ToDictionaryAsync(w => w.IntegrationWebhookId, ct);
        var results = new System.Collections.Concurrent.ConcurrentDictionary<long, WebhookSendResult>();
        await Parallel.ForEachAsync(due.GroupBy(d => d.IntegrationWebhookId), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (group, token) =>
        {
            var consecutiveFailures = 0;
            foreach (var d in group)
            {
                var r = await sender.SendAsync(webhooks[group.Key], d.EventId, d.EventType, d.PayloadJson, token);
                results[d.IntegrationWebhookDeliveryId] = r;
                consecutiveFailures = r.Ok ? 0 : consecutiveFailures + 1;
                if (consecutiveFailures >= 3) break;
            }
        });
        foreach (var delivery in due)
        {
            if (!results.TryGetValue(delivery.IntegrationWebhookDeliveryId, out var result)) continue;
            var webhook = webhooks[delivery.IntegrationWebhookId];
            delivery.AttemptCount++;
            delivery.LastAttemptUtc = DateTime.UtcNow;
            delivery.ResponseCode = result.ResponseCode;
            webhook.LastDeliveryUtc = delivery.LastAttemptUtc;
            if (result.Ok)
            {
                delivery.Status = WebhookDeliveryStatuses.Delivered;
                delivery.DeliveredAtUtc = delivery.LastAttemptUtc;
                delivery.Error = null;
                webhook.ConsecutiveFailures = 0;
                webhook.LastStatus = WebhookDeliveryStatuses.Delivered;
                sent++;
            }
            else
            {
                delivery.Error = result.Error;
                var next = WebhookRules.NextAttemptUtc(delivery.AttemptCount, DateTime.UtcNow);
                if (next is DateTime n) { delivery.NextAttemptUtc = n; retrying++; }
                else
                {
                    delivery.Status = WebhookDeliveryStatuses.Undelivered;
                    webhook.LastStatus = WebhookDeliveryStatuses.Undelivered;
                    webhook.ConsecutiveFailures++;
                    undelivered++;
                    if (webhook.ConsecutiveFailures >= WebhookRules.SuspendAfterUndelivered && webhook.SuspendedAtUtc is null)
                    {
                        webhook.SuspendedAtUtc = DateTime.UtcNow;
                        suspended++;
                        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.IntegrationId == webhook.IntegrationId, ct);
                        await db.SaveChangesAsync(ct);
                        if (integration is not null) await notifier.PublishWebhookSuspendedAsync(integration, webhook, delivery.Error, ct);
                        continue;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // lo viejo se purga a los 30 días (lo pendiente de un webhook suspendido se conserva)
        var cutoff = nowUtc.AddDays(-WebhookRules.RetentionDays);
        var purged = await db.IntegrationWebhookDeliveries.Where(d => d.CreatedAtUtc < cutoff && d.Status != WebhookDeliveryStatuses.Pending).ExecuteDeleteAsync(ct);
        return new Outcome(sent, retrying, undelivered, suspended, purged);
    }
}
