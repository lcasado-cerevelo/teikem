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
    IOptions<IntegrationOptions> options, ILogger<IntegrationRunner> logger, ISecretProtector protector, IMailboxReader mailbox)
{
    public static readonly string InstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private static readonly string[] Extensions = [".csv", ".txt"];
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
        if (integration.ConnectorKind is not (IntegrationConnectorKinds.Folder or IntegrationConnectorKinds.Email)) throw new ValidationException("connectorKind", NotSupportedMessage);
        if (integration.ConnectorKind == IntegrationConnectorKinds.Email)
            return await ExecuteAsync(integration, trigger, byUserId, (run, template, client) => ReadMailboxAsync(integration, template, client, run, ct), ct);
        return await ExecuteAsync(integration, trigger, byUserId, async (run, template, client) =>
        {
            var dir = FolderOf(integration);
            var outcomes = new List<FileOutcome>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
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
        var name = string.IsNullOrWhiteSpace(fileName) ? "archivo.csv" : Path.GetFileName(fileName.Trim());
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
                : $"{outcomes.Count} archivo(s): {run.OrdersCreated} orden(es) creada(s), {run.Rejected} a la bandeja" + (failedFiles > 0 ? $", {failedFiles} archivo(s) con error." : ".");
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
        var selected = EmailRules.SelectAttachments(m.Attachments, cfg);
        if (cfg.RequireAttachment && selected.Count == 0)
        {
            outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.NoAttachmentMessage));
            return outcomes;
        }
        foreach (var a in selected) outcomes.Add(await ProcessBytesAsync(integration, template, client, run, a.Name, a.Content, checkDuplicate: true, ct));
        if (cfg.ReadBody)
        {
            var reading = EmailRules.ReadBody(m.TextBody, cfg.BodyFields ?? Array.Empty<EmailBodyField>());
            if (reading.Missing.Count > 0) outcomes.Add(Failed(integration, run, label, messageSha, 0, string.Join(" | ", reading.Missing)));
            else if (!reading.HasData) outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.NoBodyDataMessage));
            else
            {
                var csv = EmailRules.ToCsv(reading, template.HasHeader, ImportTemplateService.DelimiterOf(template));
                outcomes.Add(await ProcessBytesAsync(integration, template, client, run, $"{label}.csv", Encoding.UTF8.GetBytes(csv), checkDuplicate: true, ct));
            }
        }
        else if (selected.Count == 0) outcomes.Add(Failed(integration, run, label, messageSha, 0, EmailRules.NoAttachmentMessage));
        return outcomes;
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
        var content = Decode(bytes);
        var scope = new OrderScope(client.ClientId);
        var created = new List<Guid>();
        var rejected = new List<RejectedOrder>();
        try
        {
            using var asService = ((TenantContext)tenant).As(integration.TenantId, integration.ServiceUserId);
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
        return parts.Count == 0 ? "La orden no se pudo procesar." : string.Join(" | ", parts);
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
