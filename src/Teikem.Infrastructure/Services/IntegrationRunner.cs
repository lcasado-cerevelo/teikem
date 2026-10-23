using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Clients;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>Opciones de las integraciones (sección «Integrations»).</summary>
public sealed class IntegrationOptions
{
    public const string Section = "Integrations";
    /// <summary>Carpeta raíz de las carpetas de entrada: «raíz/{compañía}/{carpeta de la integración}» (relativa al directorio de trabajo del API o absoluta).</summary>
    public string FolderRoot { get; set; } = "App_Data/integrations";
    /// <summary>Tamaño máximo de un archivo de entrada, en megabytes (el importador además limita las filas).</summary>
    public int MaxFileMegabytes { get; set; } = 2;
    /// <summary>Tamaño máximo de un PDF adjunto, en megabytes (un escaneado pesa más que un CSV).</summary>
    public int MaxPdfMegabytes { get; set; } = 15;
    /// <summary>Páginas que se leen de un PDF (las demás se ignoran con un aviso).</summary>
    public int MaxPdfPages { get; set; } = 30;
    /// <summary>Carpeta con los datos de idioma de Tesseract (spa.traineddata, eng.traineddata…) para el OCR de PDF escaneados; vacía = sin OCR (los PDF con texto real se leen igual).</summary>
    public string OcrTessdataPath { get; set; } = "";
    /// <summary>Idiomas del OCR, como los pide Tesseract («spa+eng»).</summary>
    public string OcrLanguages { get; set; } = "spa+eng";
    /// <summary>Escala con que se dibuja cada página para el OCR (1 = 72 ppp). 4 ≈ 288 ppp: en los conduces escaneados de Axiscare subió los números de orden y factura leídos de 5/14 a 11–14/14.</summary>
    public float OcrScale { get; set; } = 4f;
}

/// <summary>Una orden que no se pudo procesar: va a la bandeja con su motivo y los datos tal como llegaron.</summary>
public sealed record RejectedOrder(string? ReferenceKey, int? SourceRow, string Reason, string? DataJson);

/// <summary>Lo que pasó con un archivo: su registro, las órdenes creadas y las que van a la bandeja.</summary>
public sealed record FileOutcome(IntegrationFileLog Log, IReadOnlyList<Guid> Created, IReadOnlyList<RejectedOrder> Rejected);

/// <summary>
/// Lote 132 (P11) — el ejecutor de integraciones de entrada: lee los archivos (carpeta de entrada o subida manual), los pasa por el importador de órdenes con la plantilla de la
/// integración, EN NOMBRE DE SU CUENTA DE SERVICIO, y deja lo que no pasó en la bandeja. Una sola corrida a la vez por integración (candado atómico). Nunca borra archivos: los
/// procesados van a «procesados» y los que fallaron a «errores». El mismo contenido (hash SHA-256) no se procesa dos veces.
/// </summary>
public sealed class IntegrationRunner(
    TeikemDbContext db, ITenantContext tenant, OrderImportService import, ImportTemplateService templates, IntegrationService integrations, IntegrationNotifier notifier,
    IOptions<IntegrationOptions> options, ILogger<IntegrationRunner> logger, ISecretProtector protector, IMailboxReader mailbox, IRemoteFileClient remote, IntegrationOutputService outputs, IPdfTextReader pdfReader, IEnumerable<IDocumentTarget> extraTargets)
{
    private Dictionary<string, IDocumentTarget>? _targets;
    /// <summary>Los destinos registrados por código: ORDER (el de siempre) y los que se agreguen por inyección (2026-10-22).</summary>
    private Dictionary<string, IDocumentTarget> Targets => _targets ??= new IDocumentTarget[] { new OrderDocumentTarget((r, c) => ProcessBytesAsync(r.Integration, r.Template, r.Client, r.Run, r.FileName, r.Content, r.CheckDuplicate, c)) }
        .Concat(extraTargets).ToDictionary(t => t.Kind, StringComparer.OrdinalIgnoreCase);

    private Task<FileOutcome> DispatchAsync(string target, Integration integration, ImportTemplate template, Client client, IntegrationRun run, string fileName, byte[] content, CancellationToken ct, DocumentReader? reader = null)
        => Targets.TryGetValue(target, out var t)
            ? t.ProcessAsync(new DocumentTargetRequest(integration, template, client, run, fileName, content, CheckDuplicate: true, Reader: reader), ct)
            : Task.FromResult(Failed(integration, run, fileName, string.Empty, content.Length, DocumentReaderRules.UnknownTargetMessage(target)));

    public static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);

    public const string FileTooBigMessage = "El archivo excede el tamaño máximo permitido.";
    public const string NotSupportedMessage = "Esta integración es de subida manual: suba el archivo desde la pantalla de Integraciones.";
    public const string EmptyFileMessage = "El archivo está vacío.";
    public const string NoFilesMessage = "Sin archivos nuevos.";
    public const string DuplicateFileMessage = "Este archivo ya se procesó (mismo contenido); no se vuelve a procesar.";

    // ---------------------------------------------------------------- correr (carpeta)

    /// <summary>Corre la integración ahora: lee los archivos nuevos de su carpeta de entrada. Subida manual: usar UploadAsync.</summary>
    public async Task<IntegrationRunDto> RunAsync(Guid publicId, string trigger, int? byUserId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.Direction == IntegrationDirections.Outbound) return await outputs.RunAsync(publicId, trigger, byUserId, ct);   // Lote 137b: las de salida arman y mandan
        if (integration.ConnectorKind is not (IntegrationConnectorKinds.Folder or IntegrationConnectorKinds.Email or IntegrationConnectorKinds.Sftp or IntegrationConnectorKinds.Ftp)) throw new ValidationException("connectorKind", NotSupportedMessage);
        if (IntegrationConnectorKinds.IsRemote(integration.ConnectorKind))
            return await ExecuteAsync(integration, trigger, byUserId, (run, template, client) => ReadRemoteAsync(integration, template, client, run, ct), ct);
        if (integration.ConnectorKind == IntegrationConnectorKinds.Email)
            return await ExecuteAsync(integration, trigger, byUserId, (run, template, client) => ReadMailboxAsync(integration, template, client, run, ct), ct);
        return await ExecuteAsync(integration, trigger, byUserId, async (run, template, client) =>
        {
            var dir = FolderOf(integration);
            var outcomes = new List<FileOutcome>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(f => FileFormatRules.ExtensionsOf(IntegrationService.ParseFormat(integration.FormatJson)).Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var outcome = await ProcessFileAsync(integration, template, client, run, path, ct);
                outcomes.Add(outcome);
            }
            return outcomes;
        }, ct);
    }

    /// <summary>Sube un archivo a mano a la integración (cualquier conector): se procesa igual que uno de la carpeta.</summary>
    public async Task<IntegrationRunDto> UploadAsync(Guid publicId, string fileName, byte[] bytes, int? byUserId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        if (integration.Direction == IntegrationDirections.Outbound) throw new ValidationException("direction", "Esta integración es de salida: no recibe archivos.");
        var name = string.IsNullOrWhiteSpace(fileName) ? "archivo.csv" : Path.GetFileName(fileName.Trim());
        // un PDF subido a mano a una integración de correo con «leer PDF»: se lee como si hubiera llegado adjunto a un correo
        if (integration.ConnectorKind == IntegrationConnectorKinds.Email && name.EndsWith(EmailRules.PdfExtension, StringComparison.OrdinalIgnoreCase)
            && IntegrationService.ParseEmail(integration.ConnectorConfigJson) is { } pdfCfg && (pdfCfg.ReadPdf || (pdfCfg.Readers?.Any(r => string.Equals(r.Source, ReaderSources.Pdf, StringComparison.OrdinalIgnoreCase)) ?? false)))
            return await ExecuteAsync(integration, IntegrationTriggers.Upload, byUserId, async (run, template, client) =>
            {
                var pdf = new MailAttachmentData(name, "application/pdf", bytes);
                if (pdfCfg.Readers is { Count: > 0 })   // con lectores: se evalúan sus condiciones como con un correo que trae solo este adjunto
                {
                    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    var message = new MailMessageData($"subida:{sha}", null, DateTime.UtcNow, string.Empty, null, new Dictionary<string, string>(), name, string.Empty, new[] { pdf });
                    return await ProcessMessageByReadersAsync(integration, template, client, run, pdfCfg, message, $"PDF: {name}", sha, ct);
                }
                return new List<FileOutcome> { await ProcessPdfAsync(integration, template, client, run, pdfCfg.BodyFields ?? Array.Empty<EmailBodyField>(), DocumentTargets.Order, pdf, null, ct) };
            }, ct);
        if (integration.ConnectorKind == IntegrationConnectorKinds.Email && name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            return await ExecuteAsync(integration, IntegrationTriggers.Upload, byUserId, async (run, template, client) =>
            {
                var cfg = IntegrationService.ParseEmail(integration.ConnectorConfigJson) ?? throw new ValidationException("email", EmailRules.HostRequiredMessage);
                return await ProcessMessageAsync(integration, template, client, run, cfg, MailMessageMapper.FromEml(bytes), ct);
            }, ct);
        return await ExecuteAsync(integration, IntegrationTriggers.Upload, byUserId, async (run, template, client) =>
        {
            var outcome = await ProcessBytesAsync(integration, template, client, run, name, bytes, checkDuplicate: true, ct);
            return new List<FileOutcome> { outcome };
        }, ct);
    }

    // ---------------------------------------------------------------- el ciclo de una corrida

    private async Task<IntegrationRunDto> ExecuteAsync(Integration integration, string trigger, int? byUserId,
        Func<IntegrationRun, ImportTemplate, Client, Task<List<FileOutcome>>> work, CancellationToken ct)
    {
        if (!integration.IsActive) throw new StatusRuleException(IntegrationRules.InactiveMessage);
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var now = DateTime.UtcNow;
        var claimed = await db.Integrations.Where(i => i.IntegrationId == integration.IntegrationId && (i.LockedUntilUtc == null || i.LockedUntilUtc < now))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LockedBy, InstanceId).SetProperty(i => i.LockedUntilUtc, now + Lease)
                .SetProperty(i => i.LastStatus, IntegrationRunStatuses.Running).SetProperty(i => i.LastStartedUtc, now), ct);
        if (claimed == 0) throw new ConflictException(IntegrationRules.RunningMessage);

        var run = new IntegrationRun { TenantId = tenantId, IntegrationId = integration.IntegrationId, StartedUtc = now, TriggeredBy = trigger, TriggeredByUserId = byUserId };
        db.IntegrationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        string status;
        string message;
        try
        {
            var template = await db.ImportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ImportTemplateId == integration.ImportTemplateId, ct)
                           ?? throw new ValidationException("template", IntegrationRules.TemplateRequiredMessage);
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == integration.ClientId, ct) ?? throw new ValidationException("client", IntegrationRules.ClientRequiredMessage);
            var outcomes = await work(run, template, client);
            run.FilesRead = outcomes.Count;
            run.OrdersCreated = outcomes.Sum(o => o.Created.Count);
            run.Rejected = outcomes.Sum(o => o.Rejected.Count);
            var failedFiles = outcomes.Count(o => o.Log.Result == IntegrationFileResults.Failed);
            status = outcomes.Count > 0 && failedFiles == outcomes.Count && run.OrdersCreated == 0 ? IntegrationRunStatuses.Failed
                : run.Rejected > 0 || failedFiles > 0 ? IntegrationRunStatuses.Partial : IntegrationRunStatuses.Ok;
            message = outcomes.Count == 0 ? NoFilesMessage
                : $"{outcomes.Count} archivo(s): {run.OrdersCreated} envío(s) creado(s), {run.Rejected} a la bandeja" + (failedFiles > 0 ? $", {failedFiles} archivo(s) con error." : ".");
            foreach (var o in outcomes) await StoreInboxAsync(integration, run, o, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Falló la corrida de la integración {Integration}.", integration.IntegrationId);
            status = IntegrationRunStatuses.Failed;
            message = ex is TeikemException te ? te.Message : "Error inesperado; revise el historial y la bitácora del servidor.";
            run = await db.IntegrationRuns.FirstAsync(r => r.IntegrationRunId == run.IntegrationRunId, ct);
        }

        var finished = DateTime.UtcNow;
        run.Status = status;
        run.FinishedUtc = finished;
        run.Message = Truncate(message, 1000);
        db.IntegrationRuns.Update(run);
        await db.SaveChangesAsync(ct);
        var next = trigger == IntegrationTriggers.Schedule || integration.NextRunUtc is not null ? IntegrationRules.NextRun(integration.ScheduleKind, integration.IntervalSeconds, finished) : null;
        await db.Integrations.Where(i => i.IntegrationId == integration.IntegrationId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.LastStatus, status).SetProperty(i => i.LastFinishedUtc, finished).SetProperty(i => i.LastMessage, Truncate(message, 400))
            .SetProperty(i => i.LockedBy, (string?)null).SetProperty(i => i.LockedUntilUtc, (DateTime?)null).SetProperty(i => i.NextRunUtc, next), ct);
        try { await notifier.PublishRunAsync(integration, run, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "No se pudo anotar el aviso de la integración {Integration}.", integration.IntegrationId); }
        return (await integrations.RunsAsync(integration.PublicId, 1, ct))[0];
    }

    private async Task StoreInboxAsync(Integration integration, IntegrationRun run, FileOutcome outcome, CancellationToken ct)
    {
        db.IntegrationFileLogs.Add(outcome.Log);
        await db.SaveChangesAsync(ct);
        foreach (var r in outcome.Rejected)
            db.IntegrationInboxItems.Add(new IntegrationInboxItem
            {
                TenantId = integration.TenantId, IntegrationId = integration.IntegrationId, IntegrationRunId = run.IntegrationRunId, IntegrationFileLogId = outcome.Log.IntegrationFileLogId,
                SourceRow = r.SourceRow, ReferenceKey = Truncate(r.ReferenceKey, 80), Reason = Truncate(r.Reason, 1000)!, DataJson = r.DataJson,
            });
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- SFTP / FTP

    public const string RemoteConnectionFailedMessage = "No se pudo conectar con el servidor";

    /// <summary>
    /// Lee la carpeta remota (SFTP o FTP): lista, toma los archivos por nombre y tipo (sin los que todavía se están escribiendo), los baja, los pasa por la plantilla y los MUEVE a «procesados» o «errores» del
    /// servidor (nunca los borra). Si no se puede mover, el hash del contenido evita procesarlos otra vez.
    /// </summary>
    private async Task<List<FileOutcome>> ReadRemoteAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, CancellationToken ct)
    {
        var cfg = IntegrationService.ParseRemote(integration.ConnectorConfigJson) ?? throw new ValidationException("remote", RemoteRules.HostRequiredMessage);
        if (integration.SecretEnc is null) throw new ValidationException("remote.secret", RemoteRules.SecretRequiredMessage(cfg.AuthKind));
        var secret = System.Text.Json.JsonSerializer.Deserialize<RemoteSecret>(protector.Unprotect(integration.TenantId, IntegrationService.RemotePurpose, integration.SecretEnc), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new RemoteSecret();
        IRemoteSession session;
        try { session = await remote.OpenAsync(integration.ConnectorKind, cfg, secret, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException and not TeikemException) { throw new ValidationException("remote", $"{RemoteConnectionFailedMessage}: {ex.Message}"); }
        var outcomes = new List<FileOutcome>();
        await using (session)
        {
            IReadOnlyList<RemoteFileInfo> listing;
            try { listing = await session.ListAsync(cfg.Dir, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw new ValidationException("remote", $"No se pudo leer la carpeta {cfg.Dir}: {ex.Message}"); }
            foreach (var file in RemoteRules.SelectFiles(listing, cfg, DateTime.UtcNow))
            {
                ct.ThrowIfCancellationRequested();
                FileOutcome outcome;
                try
                {
                    if (file.Size > options.Value.MaxFileMegabytes * 1024L * 1024L) outcome = Failed(integration, run, file.Name, string.Empty, file.Size, FileTooBigMessage);
                    else outcome = await ProcessBytesAsync(integration, template, client, run, file.Name, await session.DownloadAsync(cfg.Dir, file.Name, ct), checkDuplicate: true, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not TeikemException)
                {
                    outcome = Failed(integration, run, file.Name, string.Empty, file.Size, $"No se pudo bajar el archivo: {ex.Message}");
                }
                outcomes.Add(outcome);
                try { await session.MoveAsync(cfg.Dir, file.Name, outcome.Log.Result == IntegrationFileResults.Failed ? cfg.ErrorDir : cfg.ProcessedDir, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "No se pudo mover {File} en el servidor de la integración {Integration}.", file.Name, integration.IntegrationId); }
            }
        }
        return outcomes;
    }

    // ---------------------------------------------------------------- correo

    public const string MailTooManyNote = "Quedan correos por leer; se leen en la próxima corrida.";
    private const int MaxMessagesPerRun = 50;

    /// <summary>Lee el buzón de la integración: los correos nuevos (NO leídos), uno por uno por las reglas; al terminar cada uno se marca como leído y se mueve a «Procesados» (nunca se borra).</summary>
    private async Task<List<FileOutcome>> ReadMailboxAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, CancellationToken ct)
    {
        var cfg = IntegrationService.ParseEmail(integration.ConnectorConfigJson) ?? throw new ValidationException("email", EmailRules.HostRequiredMessage);
        if (integration.SecretEnc is null) throw new ValidationException("email.password", EmailRules.PasswordRequiredMessage);
        var password = protector.Unprotect(integration.TenantId, IntegrationService.MailPurpose, integration.SecretEnc);
        IReadOnlyList<MailboxMessage> messages;
        try { messages = await mailbox.FetchAsync(cfg, password, MaxMessagesPerRun, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException and not TeikemException) { throw new ValidationException("email", $"{ImapMailboxReader.ConnectionFailedMessage}: {ex.Message}"); }
        var outcomes = new List<FileOutcome>();
        var handled = new List<uint>();
        foreach (var m in messages)
        {
            ct.ThrowIfCancellationRequested();
            outcomes.AddRange(await ProcessMessageAsync(integration, template, client, run, cfg, m.Data, ct));
            handled.Add(m.Uid);
        }
        try { await mailbox.MarkProcessedAsync(cfg, password, handled, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "No se pudieron marcar los correos de la integración {Integration}; el hash evita procesarlos otra vez.", integration.IntegrationId); }
        return outcomes;
    }

    /// <summary>
    /// Un correo por las reglas de la integración: asunto, remitente ORIGINAL permitido, adjuntos elegidos (cada uno por la plantilla) y/o el cuerpo (convertido en filas de la misma plantilla).
    /// Lo que no cumple va a la bandeja con su motivo; nada se pierde ni se ejecuta.
    /// </summary>
    public async Task<List<FileOutcome>> ProcessMessageAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, EmailConfig cfg, MailMessageData m, CancellationToken ct)
    {
        var outcomes = new List<FileOutcome>();
        if (!EmailRules.TextMatches(m.Subject, cfg.Subject)) return outcomes;   // otro tema: no es de esta integración
        var label = $"correo: {(string.IsNullOrWhiteSpace(m.Subject) ? m.MessageId : m.Subject)}";
        var messageSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.MessageId))).ToLowerInvariant();
        var sender = EmailRules.OriginalSender(m);
        if (!EmailRules.SenderAllowed(sender, cfg.AllowedSenders))
        {
            outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.SenderNotAllowedMessage(sender ?? m.From)));
            return outcomes;
        }
        if (cfg.Readers is { Count: > 0 }) return await ProcessMessageByReadersAsync(integration, template, client, run, cfg, m, label, messageSha, ct);
        var selected = EmailRules.SelectAttachments(m.Attachments, cfg);
        if (cfg.RequireAttachment && selected.Count == 0)
        {
            outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.NoAttachmentMessage));
            return outcomes;
        }
        foreach (var a in selected)
        {
            if (cfg.ReadPdf && a.Name.EndsWith(EmailRules.PdfExtension, StringComparison.OrdinalIgnoreCase)) outcomes.Add(await ProcessPdfAsync(integration, template, client, run, cfg.BodyFields ?? Array.Empty<EmailBodyField>(), DocumentTargets.Order, a, null, ct));
            else outcomes.Add(await DispatchAsync(DocumentTargets.Order, integration, template, client, run, a.Name, a.Content, ct));
        }
        if (cfg.ReadBody) outcomes.Add(await ProcessBodyAsync(integration, template, client, run, cfg.BodyFields ?? Array.Empty<EmailBodyField>(), DocumentTargets.Order, label, messageSha, m.TextBody, ct));
        else if (selected.Count == 0) outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.NoAttachmentMessage));
        return outcomes;
    }

    /// <summary>
    /// Un PDF adjunto (2026-10-22): se convierte en texto (el texto real del PDF o, si es un escaneado, OCR) y se lee con los mismos campos a leer del cuerpo; las filas resultantes pasan por la plantilla como cualquier archivo.
    /// Lo que no se puede leer va a la bandeja con el motivo y un pedazo del texto leído para poder ajustar los campos.
    /// </summary>
    private async Task<FileOutcome> ProcessPdfAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, IReadOnlyList<EmailBodyField> fields, string target, MailAttachmentData pdf, PdfReadResult? preRead, CancellationToken ct, string? recordStart = null, DocumentReader? reader = null)
    {
        var sha = Convert.ToHexString(SHA256.HashData(pdf.Content)).ToLowerInvariant();
        if (pdf.Content.Length > options.Value.MaxPdfMegabytes * 1024L * 1024L) return Failed(integration, run, pdf.Name, sha, pdf.Content.Length, $"El PDF pesa más de {options.Value.MaxPdfMegabytes} MB.");
        if (await db.IntegrationFileLogs.AsNoTracking().AnyAsync(f => f.IntegrationId == integration.IntegrationId && f.Sha256 == sha && f.Result == IntegrationFileResults.Processed, ct))
        {
            var dup = NewLog(integration, run, pdf.Name, sha, pdf.Content.Length);
            dup.Result = IntegrationFileResults.Duplicate;
            dup.Message = DuplicateFileMessage;
            return new FileOutcome(dup, Array.Empty<Guid>(), Array.Empty<RejectedOrder>());
        }
        var read = preRead ?? await Task.Run(() => pdfReader.Read(pdf.Content), ct);   // el OCR usa CPU: fuera del hilo de la corrida
        var sample = Truncate(string.Join(" ", read.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(8)).Replace('\t', ' '), 300);
        if (read.Problem is not null) return Failed(integration, run, pdf.Name, sha, pdf.Content.Length, EmailRules.PdfProblemMessage(pdf.Name, read.Problem));
        var reading = EmailRules.ReadDocument(read.Text, fields, recordStart);
        if (reading.Missing.Count > 0) return Failed(integration, run, pdf.Name, sha, pdf.Content.Length, Truncate($"PDF {pdf.Name}: {string.Join(" | ", reading.Missing)} Texto leído: «{sample}»", 1000));
        if (!reading.HasData) return Failed(integration, run, pdf.Name, sha, pdf.Content.Length, Truncate(EmailRules.NoPdfDataMessage(pdf.Name, sample), 1000));
        var csv = EmailRules.ToCsv(reading, template.HasHeader, ImportTemplateService.DelimiterOf(template));
        var outcome = await DispatchAsync(target, integration, template, client, run, $"{pdf.Name}.csv", Encoding.UTF8.GetBytes(csv), ct, reader);
        if (read.UsedOcr && outcome.Log.Message is { } msg) outcome.Log.Message = Truncate($"{msg} (PDF leído con OCR)", 1000);
        return outcome;
    }

    /// <summary>El cuerpo de un correo (o el texto ya leído de un documento) → filas por los campos a leer → el destino. Lo que no se puede leer va a la bandeja con el motivo.</summary>
    private async Task<FileOutcome> ProcessBodyAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, IReadOnlyList<EmailBodyField> fields, string target, string label, string messageSha, string text, CancellationToken ct, string? recordStart = null, DocumentReader? reader = null)
    {
        var reading = EmailRules.ReadDocument(text, fields, recordStart);
        if (reading.Missing.Count > 0) return Failed(integration, run, label, messageSha, 0, string.Join(" | ", reading.Missing));
        if (!reading.HasData) return Failed(integration, run, label, messageSha, 0, EmailRules.NoBodyDataMessage);
        var csv = EmailRules.ToCsv(reading, template.HasHeader, ImportTemplateService.DelimiterOf(template));
        return await DispatchAsync(target, integration, template, client, run, $"{label}.csv", Encoding.UTF8.GetBytes(csv), ct, reader);
    }

    /// <summary>
    /// Un correo por LECTORES (2026-10-22): cada documento que trae (el cuerpo, cada PDF, cada archivo de datos) lo toma el primer lector cuyas condiciones cumple; ese lector lo lee con sus campos y lo manda a su
    /// destino con su plantilla y su cliente (o los de la integración). Un documento que ningún lector toma no se pierde: si NINGUNO aplicó a nada, el correo va a la bandeja con el motivo.
    /// </summary>
    private async Task<List<FileOutcome>> ProcessMessageByReadersAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, EmailConfig cfg, MailMessageData m, string label, string messageSha, CancellationToken ct)
    {
        var readers = cfg.Readers!;
        var outcomes = new List<FileOutcome>();
        var dataExtensions = FileFormatRules.ExtensionsOf(IntegrationService.ParseFormat(integration.FormatJson));
        var inputs = new List<ReaderInput>();
        var pdfs = new Dictionary<string, (MailAttachmentData Attachment, PdfReadResult? Read)>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, MailAttachmentData>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(m.TextBody)) inputs.Add(new ReaderInput(ReaderSources.Body, string.Empty, m.Subject, m.TextBody));
        var anyPdfReader = readers.Any(r => string.Equals(r.Source, ReaderSources.Pdf, StringComparison.OrdinalIgnoreCase));
        foreach (var a in m.Attachments.Where(a => !string.IsNullOrWhiteSpace(a.Name)))
        {
            if (a.Name.EndsWith(EmailRules.PdfExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (!anyPdfReader) continue;
                if (a.Content.Length > options.Value.MaxPdfMegabytes * 1024L * 1024L) { outcomes.Add(Failed(integration, run, a.Name, string.Empty, a.Content.Length, $"El PDF pesa más de {options.Value.MaxPdfMegabytes} MB.")); continue; }
                var read = await Task.Run(() => pdfReader.Read(a.Content), ct);   // se lee una sola vez; las condiciones sobre el texto y el lector que lo toma usan este texto
                pdfs[a.Name] = (a, read);
                inputs.Add(new ReaderInput(ReaderSources.Pdf, a.Name, m.Subject, read.Problem is null ? read.Text : string.Empty));
            }
            else if (dataExtensions.Contains(Path.GetExtension(a.Name), StringComparer.OrdinalIgnoreCase))
            {
                files[a.Name] = a;
                inputs.Add(new ReaderInput(ReaderSources.File, a.Name, m.Subject, null));
            }
        }

        var assigned = DocumentReaderRules.Assign(readers, inputs);
        if (assigned.Count == 0 && outcomes.Count == 0)
        {
            outcomes.Add(Failed(integration, run, label, messageSha, 0, DocumentReaderRules.NoReaderMatchedMessage));
            return outcomes;
        }
        foreach (var (input, reader) in assigned)
        {
            ct.ThrowIfCancellationRequested();
            var (readerTemplate, readerClient) = await ResolveReaderContextAsync(reader, template, client, ct);
            var target = (reader.Target ?? DocumentTargets.Order).Trim().ToUpperInvariant();
            var fields = reader.Fields ?? Array.Empty<EmailBodyField>();
            switch (input.Source)
            {
                case ReaderSources.Body:
                    outcomes.Add(await ProcessBodyAsync(integration, readerTemplate, readerClient, run, fields, target, $"{label} [{reader.Name}]", messageSha, input.Text ?? string.Empty, ct, reader.RecordStart, reader));
                    break;
                case ReaderSources.Pdf:
                    var (attachment, read) = pdfs[input.Name];
                    outcomes.Add(await ProcessPdfAsync(integration, readerTemplate, readerClient, run, fields, target, attachment, read, ct, reader.RecordStart, reader));
                    break;
                default:
                    var file = files[input.Name];
                    outcomes.Add(await DispatchAsync(target, integration, readerTemplate, readerClient, run, file.Name, file.Content, ct, reader));
                    break;
            }
        }
        return outcomes;
    }

    /// <summary>La plantilla y el cliente de un lector: los suyos si los trae (del tenant, con el tipo de plantilla de su destino) y, si no, los de la integración.</summary>
    private async Task<(ImportTemplate Template, Client Client)> ResolveReaderContextAsync(DocumentReader reader, ImportTemplate defaultTemplate, Client defaultClient, CancellationToken ct)
    {
        var client = defaultClient;
        if (reader.ClientPublicId is Guid clientGuid)
            client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientGuid, ct) ?? throw new ValidationException("client", IntegrationRules.ClientRequiredMessage);
        var template = defaultTemplate;
        if (reader.TemplatePublicId is Guid templateGuid)
        {
            var resolved = await templates.ResolveForImportAsync(templateGuid, client.ClientId, ImportKinds.Order, ct);
            template = await db.ImportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ImportTemplateId == resolved.ImportTemplateId, ct) ?? throw new ValidationException("template", IntegrationRules.TemplateRequiredMessage);
        }
        return (template, client);
    }

    // ---------------------------------------------------------------- un archivo

    private async Task<FileOutcome> ProcessFileAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var folder = Path.GetDirectoryName(path)!;
        FileOutcome outcome;
        string destination;
        if (info.Length > options.Value.MaxFileMegabytes * 1024L * 1024L)
        {
            outcome = Failed(integration, run, info.Name, string.Empty, info.Length, FileTooBigMessage);
            destination = "errores";
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            outcome = await ProcessBytesAsync(integration, template, client, run, info.Name, bytes, checkDuplicate: true, ct);
            destination = outcome.Log.Result == IntegrationFileResults.Failed ? "errores" : "procesados";
        }
        // nunca se borra: se mueve a «procesados» o «errores» con la fecha por delante (si no se puede mover, el hash evita procesarlo otra vez)
        try
        {
            var target = Path.Combine(folder, destination);
            Directory.CreateDirectory(target);
            File.Move(path, Path.Combine(target, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{info.Name}"), overwrite: false);
        }
        catch (IOException ex) { logger.LogWarning(ex, "No se pudo mover el archivo {File} de la integración {Integration}.", path, integration.IntegrationId); }
        return outcome;
    }

    /// <summary>Procesa el contenido de un archivo: hash, importador y reparto entre órdenes creadas y bandeja. checkDuplicate = false al reprocesar la bandeja.</summary>
    public async Task<FileOutcome> ProcessBytesAsync(Integration integration, ImportTemplate template, Client client, IntegrationRun run, string fileName, byte[] bytes, bool checkDuplicate, CancellationToken ct)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.Length > options.Value.MaxFileMegabytes * 1024L * 1024L) return Failed(integration, run, fileName, sha, bytes.Length, FileTooBigMessage);
        if (bytes.Length == 0) return Failed(integration, run, fileName, sha, 0, EmptyFileMessage);
        var log = NewLog(integration, run, fileName, sha, bytes.Length);
        if (checkDuplicate && await db.IntegrationFileLogs.AsNoTracking().AnyAsync(f => f.IntegrationId == integration.IntegrationId && f.Sha256 == sha && f.Result == IntegrationFileResults.Processed, ct))
        {
            log.Result = IntegrationFileResults.Duplicate;
            log.Message = DuplicateFileMessage;
            return new FileOutcome(log, Array.Empty<Guid>(), Array.Empty<RejectedOrder>());
        }
        var content = string.Empty;
        var scope = new OrderScope(client.ClientId);
        var created = new List<Guid>();
        var rejected = new List<RejectedOrder>();
        try
        {
            using var asService = ((TenantContext)tenant).As(integration.TenantId, integration.ServiceUserId);
            // Excel, JSON y XML se convierten al CSV de la plantilla; un CSV sigue su camino de siempre
            content = FileFormatConverter.Convert(bytes, fileName, IntegrationService.ParseFormat(integration.FormatJson), template)?.Content ?? Decode(bytes);
            var preview = await import.ValidateAsync(new ImportValidateRequest(template.PublicId, client.PublicId, content, fileName), scope, ct);
            IReadOnlyList<ImportRowDto> rows = preview.Rows;
            if (preview.ValidRows > 0) rows = (await import.ConfirmAsync(preview.BatchPublicId, new ImportConfirmRequest(), scope, ct)).Rows;
            else await import.DiscardAsync(preview.BatchPublicId, scope, ct);
            var raw = RawRows(content, template);
            foreach (var r in rows)
            {
                if (r.OrderPublicId is Guid orderId && r.Error is null) { created.Add(orderId); continue; }
                if (r.IsValid && r.Error is null && r.Skipped) continue;
                var sourceRows = new HashSet<int> { r.Row };
                foreach (var l in r.Lines ?? Array.Empty<ImportLineDto>()) sourceRows.Add(l.Row);
                rejected.Add(new RejectedOrder(r.Values.GetValueOrDefault(ImportFields.OrderNumber), r.Row, ReasonOf(r), RawJson(raw, sourceRows)));
            }
            log.Result = checkDuplicate ? IntegrationFileResults.Processed : IntegrationFileResults.Reprocessed;
            log.OrdersCreated = created.Count;
            log.Rejected = rejected.Count;
            log.Message = $"{created.Count} creada(s), {rejected.Count} a la bandeja.";
        }
        catch (Exception ex) when (ex is TeikemException)
        {
            // el archivo en sí no se pudo leer (columnas, tamaño, cliente suspendido…): queda anotado y en la bandeja sin datos para reprocesar
            db.ChangeTracker.Clear();
            log.Result = IntegrationFileResults.Failed;
            log.Message = Truncate(ex.Message, 1000);
            rejected.Add(new RejectedOrder(null, null, $"Archivo {fileName}: {ex.Message}", null));
        }
        return new FileOutcome(log, created, rejected);
    }

    private FileOutcome Failed(Integration integration, IntegrationRun run, string fileName, string sha, long size, string message)
    {
        var log = NewLog(integration, run, fileName, sha.Length == 64 ? sha : new string('0', 64), size);
        log.Result = IntegrationFileResults.Failed;
        log.Message = message;
        return new FileOutcome(log, Array.Empty<Guid>(), new[] { new RejectedOrder(null, null, $"Archivo {fileName}: {message}", null) });
    }

    private static IntegrationFileLog NewLog(Integration integration, IntegrationRun run, string fileName, string sha, long size)
        => new() { TenantId = integration.TenantId, IntegrationId = integration.IntegrationId, IntegrationRunId = run.IntegrationRunId, FileName = Truncate(fileName, 260)!, Sha256 = sha, SizeBytes = size };

    // ---------------------------------------------------------------- datos crudos y motivo

    /// <summary>El motivo de la bandeja: los errores de la orden con el campo, los de cada línea con su fila, y el error de crear si lo hubo.</summary>
    public static string ReasonOf(ImportRowDto r)
    {
        var parts = r.Errors.Select(e => e.Key == "lines" ? e.Value : $"{e.Key}: {e.Value}").ToList();
        foreach (var l in r.Lines ?? Array.Empty<ImportLineDto>()) parts.AddRange(l.Errors.Select(e => $"Fila {l.Row} · {e.Key}: {e.Value}"));
        if (!string.IsNullOrWhiteSpace(r.Error)) parts.Add(r.Error!);
        return parts.Count == 0 ? "El envío no se pudo procesar." : string.Join(" | ", parts);
    }

    private sealed record RawData(List<string>? Header, Dictionary<int, List<string>> Rows);

    private static RawData RawRows(string content, ImportTemplate template)
    {
        var delimiter = ImportTemplateService.DelimiterOf(template);
        var rows = CsvParser.Parse(content, delimiter, hasHeader: template.HasHeader).ToDictionary(r => r.RowNumber, r => r.Cells.ToList());
        List<string>? header = null;
        if (template.HasHeader) header = CsvParser.Parse(content, delimiter, hasHeader: false).FirstOrDefault()?.Cells.ToList();
        return new RawData(header, rows);
    }

    private static string? RawJson(RawData raw, IEnumerable<int> rowNumbers)
    {
        var rows = rowNumbers.Where(raw.Rows.ContainsKey).OrderBy(n => n).Select(n => new InboxRowDto(n, raw.Rows[n])).ToList();
        return rows.Count == 0 ? null : JsonSerializer.Serialize(new InboxData(raw.Header, rows), Json);
    }

    /// <summary>Forma guardada de los datos crudos de un ítem de la bandeja.</summary>
    public sealed record InboxData(List<string>? Header, List<InboxRowDto> Rows);

    public static InboxData? ParseData(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<InboxData>(json, Json);

    /// <summary>Texto CSV con las filas (y el encabezado) y el delimitador de la plantilla: lo que se vuelve a mandar al importador al reprocesar.</summary>
    public static string ToCsv(InboxData data, char delimiter)
    {
        static string Cell(string value, char d) => value.IndexOfAny(new[] { d, '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        var sb = new StringBuilder();
        if (data.Header is not null) sb.AppendLine(string.Join(d(delimiter), data.Header.Select(c => Cell(c, delimiter))));
        foreach (var r in data.Rows.OrderBy(r => r.Row)) sb.AppendLine(string.Join(d(delimiter), r.Cells.Select(c => Cell(c, delimiter))));
        return sb.ToString();
        static string d(char c) => c.ToString();
    }

    // ---------------------------------------------------------------- carpeta y texto

    private string RootDir()
    {
        var root = options.Value.FolderRoot;
        return Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root));
    }

    /// <summary>Carpeta de entrada de la integración (se crea si falta). Nunca sale de la raíz: el nombre solo admite letras, números, guion y guion bajo.</summary>
    public string FolderOf(Integration integration)
    {
        var folder = IntegrationService.FolderOf(integration);
        if (!IntegrationRules.IsValidFolder(folder)) throw new ValidationException("folder", IntegrationRules.FolderInvalidMessage);
        var dir = Path.GetFullPath(Path.Combine(RootDir(), integration.TenantId.ToString(), folder!.Trim()));
        if (!dir.StartsWith(RootDir(), StringComparison.OrdinalIgnoreCase)) throw new ValidationException("folder", IntegrationRules.FolderInvalidMessage);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>UTF-8 (con o sin BOM); si no es UTF-8 válido se lee como Latin-1 (lo que exporta Excel en Windows).</summary>
    public static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string? Truncate(string? text, int max) => text is { Length: > 0 } && text.Length > max ? text[..max] : text;
}
