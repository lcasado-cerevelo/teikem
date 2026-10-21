using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>El resultado de un intento de envío de una salida: éxito, código HTTP si lo hubo y el motivo si falló.</summary>
public sealed record OutputSendResult(bool Ok, int? ResponseCode, string? Error);

/// <summary>
/// Lote 137b (P11) — anota las órdenes que llegan a un estatus de una integración de SALIDA: una fila en la cola por cada integración encendida del cliente que lo pidió (no repite una orden que ya espera).
/// No guarda: lo guarda quien cambió el estatus, junto con el cambio.
/// </summary>
public sealed class OutputRecorder(TeikemDbContext db)
{
    public async Task RecordAsync(int orderId, int clientId, string statusCode, CancellationToken ct)
    {
        var integrations = await db.Integrations.AsNoTracking()
            .Where(i => i.IsActive && i.Direction == IntegrationDirections.Outbound && i.ClientId == clientId && i.OutputJson != null)
            .Select(i => new { i.IntegrationId, i.TenantId, i.OutputJson }).ToListAsync(ct);
        foreach (var i in integrations)
        {
            var cfg = IntegrationService.ParseOutputJson(i.OutputJson);
            if (cfg is null || !cfg.Statuses.Contains(statusCode, StringComparer.OrdinalIgnoreCase)) continue;
            var waiting = db.IntegrationOutputOrders.Local.Any(q => q.IntegrationId == i.IntegrationId && q.TransportOrderId == orderId && q.StatusCode == statusCode && q.IntegrationOutputId == null)
                          || await db.IntegrationOutputOrders.AnyAsync(q => q.IntegrationId == i.IntegrationId && q.TransportOrderId == orderId && q.StatusCode == statusCode && q.IntegrationOutputId == null, ct);
            if (waiting) continue;
            db.IntegrationOutputOrders.Add(new IntegrationOutputOrder { TenantId = i.TenantId, IntegrationId = i.IntegrationId, TransportOrderId = orderId, StatusCode = statusCode.ToUpperInvariant(), QueuedAtUtc = DateTime.UtcNow });
        }
    }
}

/// <summary>Lote 137b — efecto del dominio OrderStatus: cada orden que llega a un estatus pedido por una integración de salida se anota en su cola.</summary>
public sealed class OutputOrderEffect(TeikemDbContext db, OutputRecorder recorder) : IStatusTransitionEffect
{
    public string StatusDomain => StatusDomains.OrderStatus;

    public async Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct)
    {
        if (!string.Equals(context.EntityTypeCode, EntityTypes.TransportOrder, StringComparison.OrdinalIgnoreCase)) return;
        var order = db.TransportOrders.Local.FirstOrDefault(o => o.TransportOrderId == context.EntityId)
                    ?? await db.TransportOrders.AsNoTracking().FirstOrDefaultAsync(o => o.TransportOrderId == context.EntityId, ct);
        if (order is null) return;
        await recorder.RecordAsync(order.TransportOrderId, order.ClientId, context.To.InternalCode, ct);
    }
}

/// <summary>Lote 137b — arma, para unas órdenes, los valores de los campos de salida (el catálogo de <see cref="OutputRules.Fields"/>): de la orden, de su parada de entrega y de su comprobante de entrega.</summary>
public sealed class OutputBuilder(TeikemDbContext db)
{
    public async Task<List<IReadOnlyDictionary<string, object?>>> BuildAsync(IReadOnlyCollection<int> orderIds, CancellationToken ct)
    {
        var orders = await db.TransportOrders.AsNoTracking().Include(o => o.Status).Where(o => orderIds.Contains(o.TransportOrderId)).OrderBy(o => o.TransportOrderId).ToListAsync(ct);
        var deliveryTypeId = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.StopType && l.InternalCode == StopTypes.Delivery).Select(l => l.LookupCodeId).FirstOrDefaultAsync(ct);
        var stops = (await db.OrderStops.AsNoTracking().Where(s => orderIds.Contains(s.TransportOrderId)).ToListAsync(ct))
            .GroupBy(s => s.TransportOrderId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StopTypeLookupId == deliveryTypeId).ThenByDescending(s => s.Sequence).First());
        var proofs = (await db.Set<DeliveryProof>().AsNoTracking().Where(p => orderIds.Contains(p.TransportOrderId) && p.IsActive).ToListAsync(ct))
            .GroupBy(p => p.TransportOrderId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.DeliveredAtUtc).First());
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var o in orders)
        {
            stops.TryGetValue(o.TransportOrderId, out var stop);
            proofs.TryGetValue(o.TransportOrderId, out var proof);
            rows.Add(new Dictionary<string, object?>
            {
                ["orderNumber"] = o.OrderNumber, ["waybillNumber"] = NullIfEmpty(o.WaybillNumber), ["clientInvoiceNumber"] = NullIfEmpty(o.ClientInvoiceNumber),
                ["status"] = o.Status?.InternalCode, ["statusLabel"] = o.Status is null ? null : MultilingualText.Resolve(o.Status.LabelJson, "es"),
                ["createdAt"] = o.CreatedAtUtc, ["promisedDate"] = o.PromisedDate, ["pieces"] = o.TotalPieces, ["weightKg"] = o.TotalWeightKg, ["codAmount"] = o.CodAmount, ["quotedAmount"] = o.QuotedAmount,
                ["notes"] = NullIfEmpty(o.Notes),
                ["consigneeName"] = NullIfEmpty(stop?.SnapName), ["consigneeAddress"] = stop is null ? null : NullIfEmpty(string.Join(", ", new[] { stop.SnapLine1, stop.SnapLine2 }.Where(x => !string.IsNullOrWhiteSpace(x)))),
                ["consigneeCity"] = NullIfEmpty(stop?.SnapCity), ["consigneePostalCode"] = NullIfEmpty(stop?.SnapPostalCode),
                ["deliveredAt"] = proof?.DeliveredAtUtc, ["recipientName"] = NullIfEmpty(proof?.RecipientName), ["deliveryNotes"] = NullIfEmpty(proof?.Notes),
            });
        }
        return rows;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Lote 137b — manda un envío a su destino: SFTP/FTP (sube el archivo con nombre provisional «.part» y luego el final; nunca pisa uno existente) o HTTP (la API saliente genérica: POST o PUT con el contenido, la
/// autenticación y los encabezados configurados; sin redirecciones y, salvo AllowInsecure, solo a direcciones públicas con https, como los webhooks).
/// </summary>
public sealed class OutputSender(IHttpClientFactory http, ISecretProtector protector, IRemoteFileClient remote, IOptions<WebhookOptions> webhookOptions)
{
    public const string HttpPurpose = "integration-http";
    public bool AllowInsecure => webhookOptions.Value.AllowInsecure;

    public async Task<OutputSendResult> SendAsync(Integration integration, IntegrationOutput output, CancellationToken ct)
    {
        try
        {
            return integration.ConnectorKind == IntegrationConnectorKinds.Http
                ? await SendHttpAsync(integration, output.Content, OutputRules.ContentTypeOf(output.Format), output.IntegrationOutputId, ct)
                : await SendRemoteAsync(integration, output, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new OutputSendResult(false, null, $"Tiempo de espera agotado ({WebhookRules.TimeoutSeconds} s)."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return new OutputSendResult(false, null, Trim($"{(ex is HttpRequestException ? "No se pudo conectar" : "No se pudo enviar")}: {ex.InnerException?.Message ?? ex.Message}")); }
    }

    private async Task<OutputSendResult> SendRemoteAsync(Integration integration, IntegrationOutput output, CancellationToken ct)
    {
        var cfg = IntegrationService.ParseRemote(integration.ConnectorConfigJson) ?? throw new InvalidOperationException(RemoteRules.HostRequiredMessage);
        var secret = integration.SecretEnc is null ? throw new InvalidOperationException(RemoteRules.SecretRequiredMessage(cfg.AuthKind))
            : JsonSerializer.Deserialize<RemoteSecret>(protector.Unprotect(integration.TenantId, IntegrationService.RemotePurpose, integration.SecretEnc), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await using var session = await remote.OpenAsync(integration.ConnectorKind, cfg, secret, ct);
        await session.UploadAsync(cfg.Dir, output.FileName, new UTF8Encoding(false).GetBytes(output.Content), ct);
        return new OutputSendResult(true, null, null);
    }

    /// <summary>Manda un contenido a la API del cliente (también la usa «probar conexión»).</summary>
    public async Task<OutputSendResult> SendHttpAsync(Integration integration, string content, string contentType, long outputId, CancellationToken ct)
    {
        var cfg = IntegrationService.ParseHttp(integration.ConnectorConfigJson) ?? throw new InvalidOperationException(OutputRules.HttpRequiredMessage);
        var urlError = WebhookRules.ValidateUrl(cfg.Url, AllowInsecure);
        if (urlError is not null) return new OutputSendResult(false, null, urlError);
        var secret = integration.SecretEnc is null ? null
            : JsonSerializer.Deserialize<HttpOutSecret>(protector.Unprotect(integration.TenantId, HttpPurpose, integration.SecretEnc), new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Value;
        using var request = new HttpRequestMessage(new HttpMethod((cfg.Method ?? "POST").ToUpperInvariant()), cfg.Url) { Content = new StringContent(content, new UTF8Encoding(false), MediaTypeHeaderValue.Parse(contentType.Split(';')[0]).MediaType) };
        request.Headers.TryAddWithoutValidation("User-Agent", "Teikem-Output/1.0");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"teikem-output-{outputId}");
        request.Headers.TryAddWithoutValidation("X-Teikem-Output", outputId.ToString());
        switch ((cfg.AuthKind ?? "NONE").ToUpperInvariant())
        {
            case "BEARER": request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret); break;
            case "BASIC": request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.AuthName}:{secret}"))); break;
            case "HEADER": request.Headers.TryAddWithoutValidation(cfg.AuthName!, secret); break;
        }
        foreach (var h in cfg.Headers ?? []) request.Headers.TryAddWithoutValidation(h.Name, h.Value);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(WebhookRules.TimeoutSeconds));
        using var response = await http.CreateClient(WebhookSender.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var code = (int)response.StatusCode;
        return code is >= 200 and <= 299 ? new OutputSendResult(true, code, null) : new OutputSendResult(false, code, $"El servidor respondió {code}.");
    }

    private static string Trim(string s) => s.Length <= 480 ? s : s[..480];
}

/// <summary>
/// Lote 137b (P11) — integraciones de SALIDA: arma lo que se manda a partir de la cola de órdenes que llegaron a los estatus pedidos (al instante: un envío por orden; por horario: un envío con todas las que
/// llegaron desde el anterior), lo manda por SFTP/FTP o por la API saliente HTTP y, si falla, lo reintenta con espera creciente (1 min, 5, 30, 2 h, 12 h); agotados, queda «no entregado» en la bandeja de
/// salidas con «Reintentar» y «Descartar». También: vista previa, enviar ahora, probar la API y la lista de envíos.
/// </summary>
public sealed class IntegrationOutputService(TeikemDbContext db, ITenantContext tenant, OutputBuilder builder, OutputSender sender, IntegrationService integrations, ILogger<IntegrationOutputService> logger)
{
    public const int ChunkSize = 1000;
    public const int MaxContentBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);

    // ---------------------------------------------------------------- correr (a mano, por horario o desde el trabajo)

    /// <summary>Corre una integración de salida ahora: pasa la cola a envíos y manda lo que toque. Devuelve el renglón del historial.</summary>
    public async Task<IntegrationRunDto> RunAsync(Guid publicId, string trigger, int? byUserId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.Direction != IntegrationDirections.Outbound) throw new ValidationException("direction", OutputRules.NotOutboundMessage);
        if (!integration.IsActive) throw new StatusRuleException(IntegrationRules.InactiveMessage);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var now = DateTime.UtcNow;
        var claimed = await db.Integrations.Where(i => i.IntegrationId == integration.IntegrationId && (i.LockedUntilUtc == null || i.LockedUntilUtc < now))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LockedBy, IntegrationRunner.InstanceId).SetProperty(i => i.LockedUntilUtc, now + Lease)
                .SetProperty(i => i.LastStatus, IntegrationRunStatuses.Running).SetProperty(i => i.LastStartedUtc, now), ct);
        if (claimed == 0) throw new ConflictException(IntegrationRules.RunningMessage);

        var run = new IntegrationRun { TenantId = tenantId, IntegrationId = integration.IntegrationId, StartedUtc = now, TriggeredBy = trigger, TriggeredByUserId = byUserId };
        db.IntegrationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        string status;
        string message;
        try
        {
            var (created, orders) = await PrepareAsync(integration, ct);
            var (sent, retrying, undelivered) = await SendDueAsync(integration, ct);
            run.FilesRead = created;
            run.OrdersCreated = orders;
            run.Rejected = undelivered;
            status = undelivered > 0 && sent == 0 && retrying == 0 ? IntegrationRunStatuses.Failed : undelivered > 0 || retrying > 0 ? IntegrationRunStatuses.Partial : IntegrationRunStatuses.Ok;
            message = created == 0 && sent + retrying + undelivered == 0 ? OutputRules.NothingToSendMessage
                : $"{created} envío(s) armado(s) con {orders} envío(s): {sent} enviado(s), {retrying} por reintentar, {undelivered} no entregado(s).";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Falló la salida de la integración {Integration}.", integration.IntegrationId);
            status = IntegrationRunStatuses.Failed;
            message = ex is TeikemException te ? te.Message : "Error inesperado; revise el historial y la bitácora del servidor.";
            run = await db.IntegrationRuns.FirstAsync(r => r.IntegrationRunId == run.IntegrationRunId, ct);
        }
        var finished = DateTime.UtcNow;
        run.Status = status;
        run.FinishedUtc = finished;
        run.Message = message.Length > 1000 ? message[..1000] : message;
        await db.SaveChangesAsync(ct);
        var next = trigger == IntegrationTriggers.Schedule || integration.NextRunUtc is not null ? IntegrationRules.NextRun(integration.ScheduleKind, integration.IntervalSeconds, finished) : null;
        var lastMessage = message.Length > 400 ? message[..400] : message;
        await db.Integrations.Where(i => i.IntegrationId == integration.IntegrationId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.LastStatus, status).SetProperty(i => i.LastFinishedUtc, finished).SetProperty(i => i.LastMessage, lastMessage)
            .SetProperty(i => i.LockedBy, (string?)null).SetProperty(i => i.LockedUntilUtc, (DateTime?)null).SetProperty(i => i.NextRunUtc, next), ct);
        return (await integrations.RunsAsync(integration.PublicId, 1, ct))[0];
    }

    /// <summary>
    /// Lo que hace el trabajo cada minuto: pasa a envíos la cola de las integraciones de salida «al instante» y manda lo que toque (primer intento o reintento) de todas las integraciones de salida encendidas.
    /// (Las «por horario» arman su envío cuando les toca, por el trabajo de integraciones por horario.) Purga lo viejo. Devuelve (armados, enviados, por reintentar, no entregados, purgados).
    /// </summary>
    public async Task<(int Created, int Sent, int Retrying, int Undelivered, int Purged)> ProcessAsync(DateTime nowUtc, CancellationToken ct)
    {
        int created = 0, sent = 0, retrying = 0, undelivered = 0;
        var integrations = await db.Integrations.AsNoTracking().Where(i => i.IsActive && i.Direction == IntegrationDirections.Outbound && i.OutputJson != null).ToListAsync(ct);
        foreach (var integration in integrations)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var cfg = IntegrationService.ParseOutput(integration);
                if (cfg is not null && cfg.Trigger.Equals(OutputTriggers.Event, StringComparison.OrdinalIgnoreCase)) created += (await PrepareAsync(integration, ct)).Created;
                var (s, r, u) = await SendDueAsync(integration, ct);
                sent += s; retrying += r; undelivered += u;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Falló la salida de la integración {Integration}.", integration.IntegrationId);
            }
        }
        var cutoff = nowUtc.AddDays(-OutputRules.RetentionDays);
        await db.IntegrationOutputOrders.Where(q => q.IntegrationOutputId != null && q.QueuedAtUtc < cutoff).ExecuteDeleteAsync(ct);
        var purged = await db.IntegrationOutputs.Where(o => o.CreatedAtUtc < cutoff && o.Status != OutputStatuses.Pending && !db.IntegrationOutputOrders.Any(q => q.IntegrationOutputId == o.IntegrationOutputId)).ExecuteDeleteAsync(ct);
        return (created, sent, retrying, undelivered, purged);
    }

    // ---------------------------------------------------------------- armar los envíos

    /// <summary>Pasa la cola de órdenes a envíos con su contenido ya armado: al instante, uno por orden; por horario, uno por tanda de hasta 1.000 órdenes. Devuelve (envíos armados, órdenes incluidas).</summary>
    private async Task<(int Created, int Orders)> PrepareAsync(Integration integration, CancellationToken ct)
    {
        var cfg = IntegrationService.ParseOutput(integration) ?? throw new ValidationException("output", OutputRules.OutputRequiredMessage);
        var queue = await db.IntegrationOutputOrders.Where(q => q.IntegrationId == integration.IntegrationId && q.IntegrationOutputId == null).OrderBy(q => q.IntegrationOutputOrderId).Take(OutputRules.MaxOrdersPerSend).ToListAsync(ct);
        if (queue.Count == 0) return (0, 0);
        var perOrder = cfg.Trigger.Equals(OutputTriggers.Event, StringComparison.OrdinalIgnoreCase);
        int created = 0, orders = 0;
        foreach (var chunk in perOrder ? queue.Select(q => new List<IntegrationOutputOrder> { q }) : queue.Chunk(ChunkSize).Select(c => c.ToList()))
        {
            var rows = await builder.BuildAsync(chunk.Select(q => q.TransportOrderId).Distinct().ToList(), ct);
            var content = OutputRules.Render(cfg, rows);
            var size = Encoding.UTF8.GetByteCount(content);
            var output = new IntegrationOutput
            {
                TenantId = integration.TenantId, IntegrationId = integration.IntegrationId, Format = cfg.Format.ToUpperInvariant(), OrderCount = rows.Count, Content = content, SizeBytes = size, FileName = "pendiente",
                Status = size > MaxContentBytes ? OutputStatuses.Undelivered : OutputStatuses.Pending, Error = size > MaxContentBytes ? OutputRules.SizeMessage : null,
                AttemptCount = size > MaxContentBytes ? OutputRules.MaxAttempts : 0,
            };
            db.IntegrationOutputs.Add(output);
            await db.SaveChangesAsync(ct);
            output.FileName = OutputRules.ExpandFileName(cfg.FileName, cfg.Format, DateTime.UtcNow, output.IntegrationOutputId);
            foreach (var q in chunk) q.IntegrationOutputId = output.IntegrationOutputId;
            await db.SaveChangesAsync(ct);
            created++;
            orders += rows.Count;
        }
        return (created, orders);
    }

    // ---------------------------------------------------------------- mandar y reintentar

    /// <summary>Manda los envíos de la integración que ya tocan. Devuelve (enviados, por reintentar, no entregados).</summary>
    private async Task<(int Sent, int Retrying, int Undelivered)> SendDueAsync(Integration integration, CancellationToken ct)
    {
        int sent = 0, retrying = 0, undelivered = 0;
        var now = DateTime.UtcNow;
        var due = await db.IntegrationOutputs.Where(o => o.IntegrationId == integration.IntegrationId && o.Status == OutputStatuses.Pending && o.NextAttemptUtc <= now).OrderBy(o => o.IntegrationOutputId).Take(50).ToListAsync(ct);
        var consecutiveFailures = 0;
        foreach (var output in due)
        {
            if (consecutiveFailures >= 3) break;   // un destino caído no se come el tiempo del trabajo: lo demás espera a la siguiente vuelta
            var result = await sender.SendAsync(integration, output, ct);
            output.AttemptCount++;
            output.LastAttemptUtc = DateTime.UtcNow;
            output.ResponseCode = result.ResponseCode;
            if (result.Ok)
            {
                output.Status = OutputStatuses.Sent;
                output.SentAtUtc = output.LastAttemptUtc;
                output.Error = null;
                consecutiveFailures = 0;
                sent++;
            }
            else
            {
                output.Error = result.Error;
                consecutiveFailures++;
                if (OutputRules.NextAttemptUtc(output.AttemptCount, DateTime.UtcNow) is DateTime next) { output.NextAttemptUtc = next; retrying++; }
                else { output.Status = OutputStatuses.Undelivered; undelivered++; }
            }
            await db.SaveChangesAsync(ct);
        }
        return (sent, retrying, undelivered);
    }

    // ---------------------------------------------------------------- vista previa y prueba

    /// <summary>Arma el contenido de ejemplo con las últimas órdenes del cliente (de los estatus pedidos; si no hay, las últimas del cliente) sin mandar nada.</summary>
    public async Task<OutputPreviewDto> PreviewAsync(Guid publicId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.Direction != IntegrationDirections.Outbound) throw new ValidationException("direction", OutputRules.NotOutboundMessage);
        var cfg = IntegrationService.ParseOutput(integration) ?? throw new ValidationException("output", OutputRules.OutputRequiredMessage);
        var statuses = cfg.Statuses.Select(s => s.ToUpperInvariant()).ToList();
        var ids = await db.TransportOrders.AsNoTracking().Where(o => o.ClientId == integration.ClientId && o.Status != null && statuses.Contains(o.Status.InternalCode)).OrderByDescending(o => o.TransportOrderId).Take(3).Select(o => o.TransportOrderId).ToListAsync(ct);
        var sample = ids.Count > 0;
        if (!sample) ids = await db.TransportOrders.AsNoTracking().Where(o => o.ClientId == integration.ClientId).OrderByDescending(o => o.TransportOrderId).Take(3).Select(o => o.TransportOrderId).ToListAsync(ct);
        var rows = await builder.BuildAsync(ids, ct);
        var content = OutputRules.Render(cfg, rows);
        return new OutputPreviewDto(OutputRules.ExpandFileName(cfg.FileName, cfg.Format, DateTime.UtcNow, 0), cfg.Format.ToUpperInvariant(), content, rows.Count, sample);
    }

    /// <summary>Prueba la API saliente: manda un cuerpo de prueba (sin órdenes) y dice cómo respondió.</summary>
    public async Task<ConnectionTestDto> TestHttpAsync(Guid publicId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.ConnectorKind != IntegrationConnectorKinds.Http) throw new ValidationException("connectorKind", "Esta integración no tiene una API que probar.");
        var cfg = IntegrationService.ParseOutput(integration);
        var format = (cfg?.Format ?? OutputFormats.Json).ToUpperInvariant();
        var body = format switch
        {
            OutputFormats.Xml => "<?xml version=\"1.0\" encoding=\"utf-8\"?><orders test=\"true\"/>",
            OutputFormats.Csv => "test\r\ntrue\r\n",
            _ => "{\"test\":true,\"orders\":[]}",
        };
        OutputSendResult result;
        try { result = await sender.SendHttpAsync(integration, body, OutputRules.ContentTypeOf(format), 0, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new OutputSendResult(false, null, $"Tiempo de espera agotado ({WebhookRules.TimeoutSeconds} s)."); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { result = new OutputSendResult(false, null, $"No se pudo conectar: {ex.InnerException?.Message ?? ex.Message}"); }
        return new ConnectionTestDto(result.Ok, result.Ok ? $"La API respondió {result.ResponseCode}." : result.Error ?? "No se pudo enviar.", 0, null);
    }

    // ---------------------------------------------------------------- bandeja de salidas (archivos y API)

    public async Task<OutputPageDto> ListAsync(Guid? integrationPublicId, string? status, int skip, int take, CancellationToken ct)
    {
        var query = from o in db.IntegrationOutputs.AsNoTracking() join i in db.Integrations.AsNoTracking() on o.IntegrationId equals i.IntegrationId select new { o, i };
        if (integrationPublicId is Guid ip) query = query.Where(x => x.i.PublicId == ip);
        var undelivered = await query.CountAsync(x => x.o.Status == OutputStatuses.Undelivered, ct);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            if (!OutputStatuses.All.Contains(s)) throw new ValidationException("status", $"Estatus desconocido: {status}. Use {string.Join(", ", OutputStatuses.All)}.");
            query = query.Where(x => x.o.Status == s);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.o.IntegrationOutputId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take <= 0 ? 50 : take, 1, 200)).ToListAsync(ct);
        return new OutputPageDto(rows.Select(x => ToDto(x.o, x.i)).ToList(), total, undelivered);
    }

    public async Task<OutputItemDto> RetryAsync(long id, CancellationToken ct)
    {
        var (o, i) = await LoadAsync(id, ct);
        if (o.Status != OutputStatuses.Undelivered) throw new ConflictException(OutputRules.NotUndeliveredMessage);
        o.Status = OutputStatuses.Pending;
        o.AttemptCount = 0;
        o.NextAttemptUtc = DateTime.UtcNow;
        o.Error = null;
        o.ResolvedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(o, i);
    }

    public async Task<OutputItemDto> DiscardAsync(long id, CancellationToken ct)
    {
        var (o, i) = await LoadAsync(id, ct);
        if (o.Status == OutputStatuses.Discarded) throw new ConflictException(OutputRules.AlreadyDiscardedMessage);
        if (o.Status == OutputStatuses.Sent) throw new ConflictException(OutputRules.NotDiscardableMessage);
        o.Status = OutputStatuses.Discarded;
        o.ResolvedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return ToDto(o, i);
    }

    private async Task<(IntegrationOutput O, Integration I)> LoadAsync(long id, CancellationToken ct)
    {
        var o = await db.IntegrationOutputs.FirstOrDefaultAsync(x => x.IntegrationOutputId == id, ct) ?? throw new NotFoundException("Envío");
        var i = await db.Integrations.AsNoTracking().FirstAsync(x => x.IntegrationId == o.IntegrationId, ct);
        return (o, i);
    }

    private static OutputItemDto ToDto(IntegrationOutput o, Integration i)
        => new(o.IntegrationOutputId, i.PublicId, i.Name, i.ConnectorKind, o.FileName, o.Format, o.OrderCount, o.SizeBytes, o.Status, o.AttemptCount, o.NextAttemptUtc, o.LastAttemptUtc, o.ResponseCode, o.Error, o.CreatedAtUtc, o.SentAtUtc,
            o.Content.Length <= 4000 ? o.Content : o.Content[..4000]);
}
