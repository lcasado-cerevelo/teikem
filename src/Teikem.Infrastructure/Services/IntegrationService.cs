using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Security;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 132 (P11) — alta, edición y consulta de integraciones. Al crear una integración se crea su CUENTA DE SERVICIO (usuario de clase SERVICE que no inicia sesión) con un rol que solo
/// trae los permisos de los alcances elegidos; quien crea no puede dar más de lo que él mismo tiene (ni de un módulo que la compañía no tiene). Nunca se borra: se apaga.
/// </summary>
public sealed class IntegrationService(
    TeikemDbContext db, ITenantContext tenant, UserManager<ApplicationUser> users, PermissionService permissions, ModuleService modules, ILookupCache lookups,
    ImportTemplateService templates, IntegrationNotifier notifier, ISecretProtector protector, IMailboxReader mailbox, IRemoteFileClient remote, Microsoft.Extensions.Options.IOptions<WebhookOptions> webhookOptions)
{
    public const string NotFoundLabel = "Integración";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- opciones del alta

    /// <summary>Lo que quien mira puede dar en el alta: alcances (con el motivo si alguno no se puede), conectores y a quién avisar.</summary>
    public async Task<IntegrationOptionsDto> GetOptionsAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (perms, mods) = await CreatorAsync(ct);
        var scopes = IntegrationScopes.All.Select(s =>
        {
            var errors = IntegrationScopes.Validate(new[] { s.Code }, perms, mods);
            return new IntegrationScopeOptionDto(s.Code, s.Family, tenant.Lang == "en" ? s.LabelEn : s.LabelEs, s.ModuleKey, errors.Count == 0, errors.Count == 0 ? null : errors.Values.First(), IntegrationScopes.IsApiScope(s.Code), s.NeedsClient);
        }).ToList();
        var lang = tenant.Lang == "en" ? "en" : "es";
        return new IntegrationOptionsDto(scopes, IntegrationConnectorKinds.Available.Where(c => c != IntegrationConnectorKinds.Http).ToList(), await NotifyCandidatesAsync(tenantId, ct),
            OutputRules.Fields.Select(f => new OutputFieldDto(f.Code, lang == "en" ? f.LabelEn : f.LabelEs, f.Kind.ToString().ToUpperInvariant())).ToList(), OutputRules.OrderStatusCodes, IntegrationConnectorKinds.Outbound);
    }

    private async Task<List<IntegrationNotifyUserDto>> NotifyCandidatesAsync(int tenantId, CancellationToken ct)
    {
        var serviceKind = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Service, ct);
        var portalKind = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Portal, ct);
        var ids = await db.UserTenants.AsNoTracking().Select(m => m.UserId).ToListAsync(ct);
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive && !u.IsPlatformAdmin && u.UserKindLookupId != serviceKind && u.UserKindLookupId != portalKind)
            .OrderBy(u => u.FullName).ThenBy(u => u.Email)
            .Select(u => new IntegrationNotifyUserDto(u.Id, u.FullName ?? u.Email ?? "", u.Email)).ToListAsync(ct);
    }

    private async Task<(ISet<string> Perms, ISet<string> Modules)> CreatorAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var userId = ((TenantContext)tenant).RequireUserId();
        ISet<string> perms = ((TenantContext)tenant).IsPlatformAdmin
            ? PermissionCatalog.All.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : (await permissions.GetEffectivePermissionsAsync(userId, tenantId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mods = (await modules.GetEnabledKeysAsync(tenantId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (perms, mods);
    }

    // ---------------------------------------------------------------- consulta

    public async Task<IReadOnlyList<IntegrationDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Integrations.AsNoTracking().Include(i => i.ImportTemplate).AsQueryable();
        if (!includeInactive) q = q.Where(i => i.IsActive);
        return await ToDtosAsync(await q.OrderBy(i => i.Name).ToListAsync(ct), ct);
    }

    public async Task<IntegrationDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Integrations.AsNoTracking().Include(i => i.ImportTemplate).FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        return (await ToDtosAsync(new List<Integration> { row }, ct))[0];
    }

    internal async Task<List<IntegrationDto>> ToDtosAsync(List<Integration> rows, CancellationToken ct)
    {
        var clientIds = rows.Where(r => r.ClientId is not null).Select(r => r.ClientId!.Value).Distinct().ToList();
        var clients = await db.Clients.AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).Select(c => new { c.ClientId, c.PublicId, c.Name }).ToDictionaryAsync(c => c.ClientId, ct);
        var notifyIds = rows.SelectMany(r => ParseIds(r.NotifyUserIdsJson)).Distinct().ToList();
        var serviceIds = rows.Where(r => r.ServiceUserId is not null).Select(r => r.ServiceUserId!.Value).ToList();
        var people = await db.Users.AsNoTracking().Where(u => notifyIds.Contains(u.Id) || serviceIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, ct);
        var integrationIds = rows.Select(r => r.IntegrationId).ToList();
        var pending = await db.IntegrationInboxItems.AsNoTracking().Where(i => integrationIds.Contains(i.IntegrationId) && i.Status == InboxStatuses.Pending)
            .GroupBy(i => i.IntegrationId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return rows.Select(r =>
        {
            var client = r.ClientId is int cid && clients.TryGetValue(cid, out var c) ? c : null;
            return new IntegrationDto(r.IntegrationId, r.PublicId, r.Name, r.Description, r.Direction, client?.PublicId, client?.Name, r.ConnectorKind, FolderOf(r),
                r.ImportTemplate?.PublicId, r.ImportTemplate?.Name, r.ScheduleKind, r.IntervalSeconds, r.NextRunUtc, ParseStrings(r.ScopesJson),
                r.ServiceUserId is int sid && people.TryGetValue(sid, out var su) ? su.FullName : null,
                ParseIds(r.NotifyUserIdsJson).Where(people.ContainsKey).Select(id => new IntegrationNotifyUserDto(id, people[id].FullName ?? people[id].Email ?? "", people[id].Email)).ToList(),
                r.RetentionDays, r.IsActive, r.LastStartedUtc, r.LastFinishedUtc, r.LastStatus, r.LastMessage, pending.GetValueOrDefault(r.IntegrationId), r.CreatedAtUtc,
                r.ConnectorKind == IntegrationConnectorKinds.Email ? ParseEmail(r.ConnectorConfigJson) : null, r.ConnectorKind == IntegrationConnectorKinds.Email && r.SecretEnc is not null,
                IntegrationConnectorKinds.IsRemote(r.ConnectorKind) ? ParseRemote(r.ConnectorConfigJson) : null, IntegrationConnectorKinds.IsRemote(r.ConnectorKind) && r.SecretEnc is not null, ParseFormat(r.FormatJson),
                r.Direction == IntegrationDirections.Outbound ? ParseOutput(r) : null, r.ConnectorKind == IntegrationConnectorKinds.Http && r.SecretEnc is not null);
        }).ToList();
    }

    public static IReadOnlyList<string> ParseStrings(string? json)
        => string.IsNullOrWhiteSpace(json) ? Array.Empty<string>() : JsonSerializer.Deserialize<List<string>>(json, Json) ?? new List<string>();

    public static IReadOnlyList<int> ParseIds(string? json)
        => string.IsNullOrWhiteSpace(json) ? Array.Empty<int>() : JsonSerializer.Deserialize<List<int>>(json, Json) ?? new List<int>();

    /// <summary>La configuración del conector de correo guardada (null si no hay o no es de correo).</summary>
    public static OutputConfig? ParseOutputJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<OutputConfig>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>La salida de una integración con la API saliente (HTTP) que guarda aparte, en la configuración del conector.</summary>
    public static OutputConfig? ParseOutput(Integration r)
    {
        var c = ParseOutputJson(r.OutputJson);
        return c is not null && r.ConnectorKind == IntegrationConnectorKinds.Http ? c with { Http = ParseHttp(r.ConnectorConfigJson) } : c;
    }

    public static HttpOutConfig? ParseHttp(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<HttpOutConfig>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static OutputConfig NormalizeOutput(OutputConfig c) => c with
    {
        Statuses = c.Statuses.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList(),
        Trigger = c.Trigger.Trim().ToUpperInvariant(),
        Format = c.Format.Trim().ToUpperInvariant(),
        Delimiter = c.Format.Trim().ToUpperInvariant() == OutputFormats.Csv ? (OutputRules.DelimiterOf(c.Delimiter) ?? ',').ToString() : null,
        FileName = string.IsNullOrWhiteSpace(c.FileName) ? null : c.FileName.Trim(),
        Columns = (c.Columns ?? []).Select(x => new OutputColumn(x.Header.Trim(), OutputRules.FindField(x.Field)!.Code)).ToList(),
        Http = c.Http is null ? null : c.Http with
        {
            Url = c.Http.Url.Trim(), Method = c.Http.Method.Trim().ToUpperInvariant(), AuthKind = (c.Http.AuthKind ?? "NONE").Trim().ToUpperInvariant(),
            AuthName = string.IsNullOrWhiteSpace(c.Http.AuthName) ? null : c.Http.AuthName.Trim(),
            Headers = (c.Http.Headers ?? []).Select(h => new HttpHeaderItem(h.Name.Trim(), h.Value ?? string.Empty)).ToList(),
        },
    };

    public static FileFormatConfig? ParseFormat(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<FileFormatConfig>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static FileFormatConfig NormalizeFormat(FileFormatConfig c) => c with
    {
        Kind = (c.Kind ?? FileFormatKinds.Auto).Trim().ToUpperInvariant(),
        Sheet = string.IsNullOrWhiteSpace(c.Sheet) ? null : c.Sheet.Trim(),
        RecordsPath = string.IsNullOrWhiteSpace(c.RecordsPath) ? null : c.RecordsPath.Trim(),
        LinesPath = string.IsNullOrWhiteSpace(c.LinesPath) ? null : c.LinesPath.Trim(),
        Columns = (c.Columns ?? []).Select(x => new FormatColumn(x.Header.Trim(), x.Path.Trim())).ToList(),
    };

    /// <summary>Valida y normaliza el formato de una integración de archivos; null si no se manda. Una integración API no lleva formato.</summary>
    private static string? FormatToJson(FileFormatConfig? format, bool isApi)
    {
        if (format is null) return null;
        if (isApi) throw new ValidationException("format", FileFormatRules.FormatNotForApiMessage);
        ThrowIfErrors(FileFormatRules.Validate(format));
        var n = NormalizeFormat(format);
        var plain = n.Kind == FileFormatKinds.Auto && n.Sheet is null && n.RecordsPath is null && n.LinesPath is null && n.Columns!.Count == 0;
        return plain ? null : JsonSerializer.Serialize(n, Json);
    }

    /// <summary>Vista previa de un archivo con un formato (sin guardar nada): las hojas, el encabezado y las primeras 20 filas como las leería la plantilla.</summary>
    public FormatPreviewDto PreviewFormat(byte[] bytes, string fileName, FileFormatConfig? format, char delimiter, bool hasHeader)
    {
        if (bytes.Length == 0) throw new ValidationException("file", IntegrationRunner.EmptyFileMessage);
        if (format is not null) ThrowIfErrors(FileFormatRules.Validate(format));
        var kind = FileFormatRules.Resolve(format is null ? null : NormalizeFormat(format), fileName, bytes);
        IReadOnlyList<string> sheets = [];
        IReadOnlyList<string> header;
        List<IReadOnlyList<string>> rows;
        switch (kind)
        {
            case FileFormatKinds.Excel:
            {
                if (!ImportFileReader.LooksLikeXlsx(bytes, fileName)) throw new ValidationException("file", FileFormatConverter.NotExcelMessage);
                var t = FileFormatConverter.Read(bytes, fileName, delimiter, hasHeader, format?.Sheet);
                sheets = t.Sheets;
                header = t.Header ?? [];
                rows = t.Rows.Select(r => (IReadOnlyList<string>)r.Cells).ToList();
                break;
            }
            case FileFormatKinds.Json or FileFormatKinds.Xml:
            {
                var t = FileFormatRules.ToTable(bytes, kind, format ?? new FileFormatConfig(), out var error) ?? throw new ValidationException("file", error!);
                header = t.Header;
                rows = t.Rows.ToList();
                break;
            }
            default:
            {
                var text = IntegrationRunner.Decode(bytes);
                List<CsvRow> parsed;
                try { parsed = CsvParser.Parse(text, delimiter, hasHeader: false).ToList(); }
                catch (CsvLimitException) { throw new ValidationException("file", CsvParser.LimitMessage); }
                header = hasHeader && parsed.Count > 0 ? parsed[0].Cells : [];
                rows = parsed.Skip(hasHeader ? 1 : 0).Select(r => (IReadOnlyList<string>)r.Cells).ToList();
                break;
            }
        }
        return new FormatPreviewDto(kind, sheets, header, rows.Take(20).ToList(), rows.Count);
    }

    public static EmailConfig? ParseEmail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<EmailConfig>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static EmailConfig NormalizeEmail(EmailConfig c) => c with
    {
        Host = c.Host.Trim(), Username = c.Username.Trim(), Mailbox = string.IsNullOrWhiteSpace(c.Mailbox) ? "INBOX" : c.Mailbox.Trim(),
        ProcessedFolder = string.IsNullOrWhiteSpace(c.ProcessedFolder) ? string.Empty : c.ProcessedFolder.Trim(),
        AllowedSenders = (c.AllowedSenders ?? Array.Empty<string>()).Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList(),
        AttachmentMode = (c.AttachmentMode ?? EmailAttachmentModes.All).Trim().ToUpperInvariant(),
        Subject = c.Subject is null ? null : c.Subject with { Operator = c.Subject.Operator.Trim().ToUpperInvariant(), Value = c.Subject.Value.Trim() },
        AttachmentName = c.AttachmentName is null ? null : c.AttachmentName with { Operator = c.AttachmentName.Operator.Trim().ToUpperInvariant(), Value = c.AttachmentName.Value.Trim() },
        AttachmentTypes = (c.AttachmentTypes is { Count: > 0 } ? c.AttachmentTypes : EmailRules.DefaultAttachmentTypes).Select(t => t.Trim().ToLowerInvariant()).Distinct().ToList(),
        BodyFields = (c.BodyFields ?? Array.Empty<EmailBodyField>()).Select(f => f with { Kind = f.Kind.Trim().ToUpperInvariant(), Transform = (f.Transform ?? EmailTransforms.None).Trim().ToUpperInvariant() }).ToList(),
    };

    public const string RemotePurpose = "integration-remote";

    public static RemoteConfig? ParseRemote(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RemoteConfig>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static RemoteConfig NormalizeRemote(RemoteConfig c) => c with
    {
        Host = c.Host.Trim(), Username = c.Username.Trim(), Dir = string.IsNullOrWhiteSpace(c.Dir) ? "/" : c.Dir.Trim(),
        ProcessedDir = c.ProcessedDir?.Trim() ?? string.Empty, ErrorDir = c.ErrorDir?.Trim() ?? string.Empty,
        AuthKind = (c.AuthKind ?? RemoteAuthKinds.Password).Trim().ToUpperInvariant(),
        HostFingerprint = RemoteRules.NormalizeFingerprint(c.HostFingerprint) is string f ? "SHA256:" + f : null,
        FileName = c.FileName is null ? null : c.FileName with { Operator = c.FileName.Operator.Trim().ToUpperInvariant(), Value = c.FileName.Value.Trim() },
        FileTypes = (c.FileTypes is { Count: > 0 } ? c.FileTypes : EmailRules.DefaultAttachmentTypes).Select(t => t.Trim().ToLowerInvariant().TrimStart('.')).Where(t => t.Length > 0).Distinct().ToList(),
    };

    private static bool HasUsableSecret(string authKind, RemoteSecret? s)
        => s is not null && (authKind == RemoteAuthKinds.Key ? !string.IsNullOrWhiteSpace(s.PrivateKey) : !string.IsNullOrEmpty(s.Password));

    public RemoteSecret? ReadRemoteSecret(Integration row)
        => row.SecretEnc is null ? null : JsonSerializer.Deserialize<RemoteSecret>(protector.Unprotect(row.TenantId, RemotePurpose, row.SecretEnc), Json);

    /// <summary>Prueba la conexión de la integración (correo, SFTP o FTP) sin tocar los correos ni los archivos.</summary>
    public async Task<ConnectionTestDto> TestConnectionAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (row.ConnectorKind == IntegrationConnectorKinds.Email)
        {
            var r = await TestEmailAsync(publicId, ct);
            return new ConnectionTestDto(r.Ok, r.Message, r.Unseen, null);
        }
        if (!IntegrationConnectorKinds.IsRemote(row.ConnectorKind)) throw new ValidationException("connectorKind", "Esta integración no tiene una conexión que probar.");
        var cfg = ParseRemote(row.ConnectorConfigJson) ?? throw new ValidationException("remote", RemoteRules.HostRequiredMessage);
        var secret = ReadRemoteSecret(row) ?? throw new ValidationException("remote.secret", RemoteRules.SecretRequiredMessage(cfg.AuthKind));
        var info = await remote.TestAsync(row.ConnectorKind, cfg, secret, ct);
        var warning = row.ConnectorKind == IntegrationConnectorKinds.Ftp && !cfg.UseFtps && info.Ok ? " " + RemoteRules.FtpPlainWarning : string.Empty;
        return new ConnectionTestDto(info.Ok, info.Message + warning, info.Files, info.Fingerprint is null ? null : "SHA256:" + info.Fingerprint);
    }

    /// <summary>Prueba la conexión con el buzón de la integración (servidor, usuario, contraseña y carpeta) sin tocar los correos.</summary>
    public async Task<EmailTestResultDto> TestEmailAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (row.ConnectorKind != IntegrationConnectorKinds.Email) throw new ValidationException("connectorKind", "Esta integración no es de correo.");
        var cfg = ParseEmail(row.ConnectorConfigJson) ?? throw new ValidationException("email", EmailRules.HostRequiredMessage);
        if (row.SecretEnc is null) throw new ValidationException("email.password", EmailRules.PasswordRequiredMessage);
        try
        {
            var password = protector.Unprotect(row.TenantId, MailPurpose, row.SecretEnc);
            var unseen = await mailbox.TestAsync(cfg, password, ct);
            return new EmailTestResultDto(true, $"Conexión correcta: {unseen} correo(s) sin leer en {cfg.Mailbox}.", unseen);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new EmailTestResultDto(false, $"{ImapMailboxReader.ConnectionFailedMessage}: {ex.Message}", 0);
        }
    }

    public const string MailPurpose = "integration-mail";

    public static string? FolderOf(Integration i)
    {
        if (string.IsNullOrWhiteSpace(i.ConnectorConfigJson)) return null;
        try { return JsonDocument.Parse(i.ConnectorConfigJson).RootElement.TryGetProperty("folder", out var f) ? f.GetString() : null; }
        catch (JsonException) { return null; }
    }

    // ---------------------------------------------------------------- alta

    public async Task<IntegrationDto> CreateAsync(IntegrationCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var creatorId = ((TenantContext)tenant).RequireUserId();
        var name = RequireName(req.Name);
        if (await db.Integrations.AnyAsync(i => i.Name == name, ct)) throw new ConflictException(IntegrationRules.NameTakenMessage);
        var scopes = req.Scopes?.Select(s => IntegrationScopes.Find(s)?.Code ?? s?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        var (perms, mods) = await CreatorAsync(ct);
        ThrowIfErrors(IntegrationScopes.Validate(scopes, perms, mods));
        var isApi = string.Equals(req.ConnectorKind?.Trim(), IntegrationConnectorKinds.Api, StringComparison.OrdinalIgnoreCase);
        var isOut = string.Equals(req.Direction?.Trim(), IntegrationDirections.Outbound, StringComparison.OrdinalIgnoreCase);
        if (isOut && isApi) throw new ValidationException("connectorKind", OutputRules.OutboundConnectorMessage);
        ThrowIfErrors(ValidateScopeKinds(scopes, isApi, isOut));

        Client? client = null;
        ImportTemplate? template = null;
        if (req.ClientPublicId is Guid clientGuid && clientGuid != Guid.Empty) client = await db.ResolveClientAsync(clientGuid, ct);
        if (isApi)
        {
            if (client is null && IntegrationScopes.AnyNeedsClient(scopes)) throw new ValidationException("clientPublicId", IntegrationScopes.ApiNeedsClientMessage);
        }
        else
        {
            if (client is null) throw new ValidationException("clientPublicId", IntegrationRules.ClientRequiredMessage);
            if (!isOut)
            {
                if (req.TemplatePublicId is not Guid templateGuid) throw new ValidationException("templatePublicId", IntegrationRules.TemplateRequiredMessage);
                template = await templates.ResolveForImportAsync(templateGuid, client.ClientId, ImportKinds.Order, ct);
            }
        }
        var (connector, folder, scheduleKind, interval) = ValidateConnectorAndSchedule(req.ConnectorKind, req.Folder, req.ScheduleKind, req.IntervalSeconds, isOut);
        var formatJson = isOut ? null : FormatToJson(req.Format, isApi);
        OutputConfig? outputCfg = null;
        HttpOutConfig? httpCfg = null;
        var httpSecretValue = req.HttpSecret?.Value;
        if (isOut)
        {
            ThrowIfErrors(OutputRules.Validate(req.Output, IntegrationConnectorKinds.IsRemote(connector), connector == IntegrationConnectorKinds.Http, webhookOptions.Value.AllowInsecure, !string.IsNullOrEmpty(httpSecretValue)));
            outputCfg = NormalizeOutput(req.Output!);
            if (outputCfg.Trigger == OutputTriggers.Event) { scheduleKind = IntegrationScheduleKinds.Manual; interval = null; }
            else if (scheduleKind != IntegrationScheduleKinds.Interval) throw new ValidationException("output.trigger", OutputRules.ScheduleNeedsIntervalMessage);
            httpCfg = outputCfg.Http;
            outputCfg = outputCfg with { Http = null };
            if (connector == IntegrationConnectorKinds.Http && !protector.IsConfigured && !string.IsNullOrEmpty(httpSecretValue)) throw new ValidationException("output.http.secret", SecretProtector.NotConfiguredMessage);
        }
        EmailConfig? email = null;
        if (connector == IntegrationConnectorKinds.Email)
        {
            ThrowIfErrors(EmailRules.Validate(req.Email, !string.IsNullOrEmpty(req.MailPassword)));
            email = NormalizeEmail(req.Email!);
            if (!protector.IsConfigured) throw new ValidationException("email.password", SecretProtector.NotConfiguredMessage);
        }
        RemoteConfig? remoteCfg = null;
        if (IntegrationConnectorKinds.IsRemote(connector))
        {
            var authKind = (req.Remote?.AuthKind ?? RemoteAuthKinds.Password).Trim().ToUpperInvariant();
            ThrowIfErrors(RemoteRules.Validate(req.Remote, connector, HasUsableSecret(authKind, req.Secret)));
            remoteCfg = NormalizeRemote(req.Remote!);
            if (!protector.IsConfigured) throw new ValidationException("remote.secret", SecretProtector.NotConfiguredMessage);
        }
        var retention = ValidateRetention(req.RetentionDays);
        var notify = await ValidateNotifyAsync(req.NotifyUserIds, req.IncludeMe ? creatorId : null, ct);

        var now = DateTime.UtcNow;
        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var publicId = Guid.NewGuid();
            var role = await CreateServiceRoleAsync(tenantId, name, scopes, ct2);
            var serviceUser = await CreateServiceUserAsync(tenantId, publicId, name, role.RoleId, ct2);
            var row = new Integration
            {
                PublicId = publicId, TenantId = tenantId, Name = name, Description = NullIfEmpty(req.Description), Direction = isOut ? IntegrationDirections.Outbound : IntegrationDirections.Inbound, ClientId = client?.ClientId, OutputJson = outputCfg is null ? null : JsonSerializer.Serialize(outputCfg, Json),
                ConnectorKind = connector, ConnectorConfigJson = email is not null ? JsonSerializer.Serialize(email, Json) : remoteCfg is not null ? JsonSerializer.Serialize(remoteCfg, Json) : httpCfg is not null ? JsonSerializer.Serialize(httpCfg, Json) : folder is null ? null : JsonSerializer.Serialize(new { folder }, Json),
                SecretEnc = email is not null ? protector.Protect(tenantId, MailPurpose, req.MailPassword!) : remoteCfg is not null ? protector.Protect(tenantId, RemotePurpose, JsonSerializer.Serialize(req.Secret, Json)) : httpCfg is not null && !string.IsNullOrEmpty(httpSecretValue) ? protector.Protect(tenantId, OutputSender.HttpPurpose, JsonSerializer.Serialize(new HttpOutSecret(httpSecretValue), Json)) : null, ImportTemplateId = template?.ImportTemplateId,
                ScheduleKind = scheduleKind, IntervalSeconds = interval, NextRunUtc = IntegrationRules.NextRun(scheduleKind, interval, now), ScopesJson = JsonSerializer.Serialize(scopes, Json),
                ServiceUserId = serviceUser, ServiceRoleId = role.RoleId, NotifyUserIdsJson = JsonSerializer.Serialize(notify, Json), RetentionDays = retention, IsActive = true, FormatJson = formatJson,
                CreatedAtUtc = now, CreatedBy = creatorId,
            };
            db.Integrations.Add(row);
            await db.SaveChangesAsync(ct2);
            await notifier.SyncRuleAsync(row, ct2);
            await db.SaveChangesAsync(ct2);
            return row.IntegrationId;
        }, ct);
        var created = await db.Integrations.AsNoTracking().FirstAsync(i => i.IntegrationId == id, ct);
        if (created.ServiceUserId is int sid) permissions.Invalidate(sid, tenantId);
        return await GetAsync(created.PublicId, ct);
    }

    // ---------------------------------------------------------------- edición

    public async Task<IntegrationDto> UpdateAsync(Guid publicId, IntegrationPatchRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var editorId = ((TenantContext)tenant).RequireUserId();
        var publicOut = publicId;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.Integrations.FirstOrDefaultAsync(i => i.PublicId == publicId, ct2) ?? throw new NotFoundException(NotFoundLabel);
            var oldName = row.Name;
            if (req.Name is not null)
            {
                var name = RequireName(req.Name);
                if (name != row.Name && await db.Integrations.AnyAsync(i => i.Name == name && i.IntegrationId != row.IntegrationId, ct2)) throw new ConflictException(IntegrationRules.NameTakenMessage);
                row.Name = name;
            }
            if (req.Description is not null) row.Description = NullIfEmpty(req.Description);
            if (req.TemplatePublicId is Guid tpl)
                row.ImportTemplateId = (await templates.ResolveForImportAsync(tpl, row.ClientId ?? 0, ImportKinds.Order, ct2)).ImportTemplateId;
            if (row.ConnectorKind == IntegrationConnectorKinds.Email && (req.Email is not null || req.MailPassword is not null))
            {
                var cfg = req.Email ?? ParseEmail(row.ConnectorConfigJson);
                ThrowIfErrors(EmailRules.Validate(cfg, row.SecretEnc is not null || !string.IsNullOrEmpty(req.MailPassword)));
                if (!string.IsNullOrEmpty(req.MailPassword))
                {
                    if (!protector.IsConfigured) throw new ValidationException("email.password", SecretProtector.NotConfiguredMessage);
                    row.SecretEnc = protector.Protect(row.TenantId, MailPurpose, req.MailPassword);
                }
                row.ConnectorConfigJson = JsonSerializer.Serialize(NormalizeEmail(cfg!), Json);
            }
            if (IntegrationConnectorKinds.IsRemote(row.ConnectorKind) && (req.Remote is not null || req.Secret is not null))
            {
                var cfg = req.Remote ?? ParseRemote(row.ConnectorConfigJson);
                var authKind = (cfg?.AuthKind ?? RemoteAuthKinds.Password).Trim().ToUpperInvariant();
                var current = row.SecretEnc is null ? null : ReadRemoteSecret(row);
                var effective = req.Secret is not null && HasUsableSecret(authKind, req.Secret) ? req.Secret : current;
                ThrowIfErrors(RemoteRules.Validate(cfg, row.ConnectorKind, HasUsableSecret(authKind, effective)));
                if (req.Secret is not null && HasUsableSecret(authKind, req.Secret))
                {
                    if (!protector.IsConfigured) throw new ValidationException("remote.secret", SecretProtector.NotConfiguredMessage);
                    row.SecretEnc = protector.Protect(row.TenantId, RemotePurpose, JsonSerializer.Serialize(req.Secret, Json));
                }
                row.ConnectorConfigJson = JsonSerializer.Serialize(NormalizeRemote(cfg!), Json);
            }
            if (row.Direction == IntegrationDirections.Outbound && (req.Output is not null || req.HttpSecret is not null || req.ScheduleKind is not null || req.IntervalSeconds is not null))
            {
                var current = ParseOutput(row);
                var cfg = req.Output ?? current ?? throw new ValidationException("output", OutputRules.OutputRequiredMessage);
                if (row.ConnectorKind == IntegrationConnectorKinds.Http) cfg = cfg with { Http = cfg.Http ?? current?.Http };
                var newSecret = req.HttpSecret?.Value;
                ThrowIfErrors(OutputRules.Validate(cfg, IntegrationConnectorKinds.IsRemote(row.ConnectorKind), row.ConnectorKind == IntegrationConnectorKinds.Http, webhookOptions.Value.AllowInsecure, row.SecretEnc is not null || !string.IsNullOrEmpty(newSecret)));
                var norm = NormalizeOutput(cfg);
                var wantedKind = norm.Trigger == OutputTriggers.Event ? IntegrationScheduleKinds.Manual : IntegrationScheduleKinds.Interval;
                var (_, _, scheduleKind, interval) = ValidateConnectorAndSchedule(row.ConnectorKind, null, wantedKind, wantedKind == IntegrationScheduleKinds.Manual ? null : req.IntervalSeconds ?? row.IntervalSeconds, true);
                row.ScheduleKind = scheduleKind;
                row.IntervalSeconds = interval;
                row.NextRunUtc = row.IsActive ? IntegrationRules.NextRun(scheduleKind, interval, DateTime.UtcNow) : null;
                row.OutputJson = JsonSerializer.Serialize(norm with { Http = null }, Json);
                if (row.ConnectorKind == IntegrationConnectorKinds.Http)
                {
                    row.ConnectorConfigJson = JsonSerializer.Serialize(norm.Http, Json);
                    if (!string.IsNullOrEmpty(newSecret))
                    {
                        if (!protector.IsConfigured) throw new ValidationException("output.http.secret", SecretProtector.NotConfiguredMessage);
                        row.SecretEnc = protector.Protect(row.TenantId, OutputSender.HttpPurpose, JsonSerializer.Serialize(new HttpOutSecret(newSecret), Json));
                    }
                }
            }
            if (row.Direction != IntegrationDirections.Outbound && row.ConnectorKind != IntegrationConnectorKinds.Email && !IntegrationConnectorKinds.IsRemote(row.ConnectorKind) && (req.Folder is not null || req.ScheduleKind is not null || req.IntervalSeconds is not null)
                || row.Direction != IntegrationDirections.Outbound && (row.ConnectorKind == IntegrationConnectorKinds.Email || IntegrationConnectorKinds.IsRemote(row.ConnectorKind)) && (req.ScheduleKind is not null || req.IntervalSeconds is not null))
            {
                var (_, folder, scheduleKind, interval) = ValidateConnectorAndSchedule(row.ConnectorKind, req.Folder ?? FolderOf(row), req.ScheduleKind ?? row.ScheduleKind,
                    req.IntervalSeconds ?? (req.ScheduleKind == IntegrationScheduleKinds.Manual ? null : row.IntervalSeconds));
                if (row.ConnectorKind == IntegrationConnectorKinds.Folder) row.ConnectorConfigJson = folder is null ? null : JsonSerializer.Serialize(new { folder }, Json);
                row.ScheduleKind = scheduleKind;
                row.IntervalSeconds = interval;
                row.NextRunUtc = row.IsActive ? IntegrationRules.NextRun(scheduleKind, interval, DateTime.UtcNow) : null;
            }
            if (req.Format is not null && row.Direction != IntegrationDirections.Outbound) row.FormatJson = FormatToJson(req.Format, row.ConnectorKind == IntegrationConnectorKinds.Api);
            if (req.RetentionDays is not null) row.RetentionDays = ValidateRetention(req.RetentionDays);
            if (req.Scopes is not null)
            {
                var scopes = req.Scopes.Select(s => IntegrationScopes.Find(s)?.Code ?? s?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var (perms, mods) = await CreatorAsync(ct2);
                ThrowIfErrors(IntegrationScopes.Validate(scopes, perms, mods));
                ThrowIfErrors(ValidateScopeKinds(scopes, row.ConnectorKind == IntegrationConnectorKinds.Api, row.Direction == IntegrationDirections.Outbound));
                if (row.ConnectorKind == IntegrationConnectorKinds.Api && row.ClientId is null && IntegrationScopes.AnyNeedsClient(scopes)) throw new ValidationException("scopes", IntegrationScopes.ApiNeedsClientMessage);
                row.ScopesJson = JsonSerializer.Serialize(scopes, Json);
                await ReplaceRolePermissionsAsync(row.ServiceRoleId, scopes, ct2);
            }
            if (req.NotifyUserIds is not null || req.IncludeMe is not null)
            {
                var current = ParseIds(row.NotifyUserIdsJson).ToList();
                var wanted = req.NotifyUserIds ?? current;
                var withMe = req.IncludeMe == true ? wanted.Append(editorId).Distinct().ToList() : wanted.ToList();
                row.NotifyUserIdsJson = JsonSerializer.Serialize(await ValidateNotifyAsync(withMe, null, ct2), Json);
            }
            if (row.Name != oldName) await RenameServiceAccountAsync(row, ct2);
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = editorId;
            await notifier.SyncRuleAsync(row, ct2);
            await db.SaveGuardedAsync(IntegrationRules.NameTakenMessage, ct2);
        }, ct);
        var serviceUser = await db.Integrations.AsNoTracking().Where(i => i.PublicId == publicOut).Select(i => i.ServiceUserId).FirstOrDefaultAsync(ct);
        if (serviceUser is int sid) permissions.Invalidate(sid, tenantId);
        return await GetAsync(publicOut, ct);
    }

    /// <summary>Apagar o encender: la cuenta de servicio, su rol y la regla de avisos siguen a la integración (se apaga, nunca se borra).</summary>
    public async Task<IntegrationDto> SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var editorId = ((TenantContext)tenant).RequireUserId();
        int? serviceUser = null;
        await db.RunInTransactionAsync(async ct2 =>
        {
            var row = await db.Integrations.FirstOrDefaultAsync(i => i.PublicId == publicId, ct2) ?? throw new NotFoundException(NotFoundLabel);
            if (row.IsActive == active) throw new ConflictException(active ? "La integración ya está encendida." : "La integración ya está apagada.");
            row.IsActive = active;
            row.NextRunUtc = active ? IntegrationRules.NextRun(row.ScheduleKind, row.IntervalSeconds, DateTime.UtcNow) : null;
            row.UpdatedAtUtc = DateTime.UtcNow;
            row.UpdatedBy = editorId;
            serviceUser = row.ServiceUserId;
            if (row.ServiceUserId is int uid) await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, active), ct2);
            if (row.ServiceRoleId is int rid) await db.AppRoles.Where(r => r.RoleId == rid).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, active), ct2);
            await notifier.SyncRuleAsync(row, ct2);
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
        }, ct);
        if (serviceUser is int sid) permissions.Invalidate(sid, tenantId);
        return await GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- historial

    public async Task<IReadOnlyList<IntegrationRunDto>> RunsAsync(Guid publicId, int take, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId, ct) ?? throw new NotFoundException(NotFoundLabel);
        var runs = await db.IntegrationRuns.AsNoTracking().Where(r => r.IntegrationId == integration.IntegrationId).OrderByDescending(r => r.IntegrationRunId).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var runIds = runs.Select(r => r.IntegrationRunId).ToList();
        var files = await db.IntegrationFileLogs.AsNoTracking().Where(f => runIds.Contains(f.IntegrationRunId)).OrderBy(f => f.IntegrationFileLogId).ToListAsync(ct);
        var userIds = runs.Where(r => r.TriggeredByUserId != null).Select(r => r.TriggeredByUserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        return runs.Select(r => new IntegrationRunDto(r.IntegrationRunId, integration.PublicId, integration.Name, r.StartedUtc, r.FinishedUtc, r.Status, r.TriggeredBy,
            r.TriggeredByUserId is int u && names.TryGetValue(u, out var n) ? n : null, r.FilesRead, r.OrdersCreated, r.Rejected, r.Message,
            files.Where(f => f.IntegrationRunId == r.IntegrationRunId).Select(f => new IntegrationFileDto(f.IntegrationFileLogId, f.FileName, f.Result, f.SizeBytes, f.OrdersCreated, f.Rejected, f.Message, f.ProcessedUtc)).ToList())).ToList();
    }

    // ---------------------------------------------------------------- cuenta de servicio

    private async Task<Role> CreateServiceRoleAsync(int tenantId, string integrationName, IReadOnlyList<string> scopes, CancellationToken ct)
    {
        var roleName = IntegrationRules.ServiceRoleName(integrationName);
        if (await db.AppRoles.AnyAsync(r => r.TenantId == tenantId && r.Name == roleName, ct)) throw new ConflictException($"Ya existe el rol '{roleName}'.");
        var role = new Role { TenantId = tenantId, Name = roleName, IsSystem = false, IsActive = true, DescriptionJson = MultilingualText.Serialize(new Dictionary<string, string> { ["es"] = "Rol de la cuenta de servicio de una integración", ["en"] = "Role of an integration's service account" }) };
        db.AppRoles.Add(role);
        await db.SaveChangesAsync(ct);
        await ReplaceRolePermissionsAsync(role.RoleId, scopes, ct);
        return role;
    }

    private async Task ReplaceRolePermissionsAsync(int? roleId, IReadOnlyList<string> scopes, CancellationToken ct)
    {
        if (roleId is not int rid) return;
        var codes = IntegrationScopes.PermissionsFor(scopes);
        var ids = await db.Permissions.AsNoTracking().Where(p => codes.Contains(p.Code)).Select(p => p.PermissionId).ToListAsync(ct);
        await db.RolePermissions.Where(rp => rp.RoleId == rid).ExecuteDeleteAsync(ct);
        foreach (var pid in ids) db.RolePermissions.Add(new RolePermission { RoleId = rid, PermissionId = pid });
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> CreateServiceUserAsync(int tenantId, Guid integrationPublicId, string integrationName, int roleId, CancellationToken ct)
    {
        var email = IntegrationRules.ServiceEmail(integrationPublicId);
        var kindId = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Service, ct);
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FullName = IntegrationRules.ServiceUserName(integrationName), UserKindLookupId = kindId, DefaultTenantId = tenantId, IsActive = true,
            OnboardingRequired = false, MustChangePassword = false, EmailConfirmed = true, EmailVerifiedUtc = DateTime.UtcNow,
        };
        var created = await users.CreateAsync(user);   // sin contraseña: no puede iniciar sesión
        if (!created.Succeeded) throw new ValidationException("name", string.Join(" ", created.Errors.Select(e => e.Description)));
        var activeId = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active).Select(s => s.StatusCodeId).FirstAsync(ct);
        db.UserTenants.Add(new UserTenant { UserId = user.Id, TenantId = tenantId, StatusCodeId = activeId, IsDefault = true, InvitedBy = tenant.UserId, JoinedAtUtc = DateTime.UtcNow });
        db.AppUserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId, TenantId = tenantId, GrantedBy = tenant.UserId });
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private async Task RenameServiceAccountAsync(Integration row, CancellationToken ct)
    {
        if (row.ServiceUserId is int uid) await db.Users.Where(u => u.Id == uid).ExecuteUpdateAsync(s => s.SetProperty(u => u.FullName, IntegrationRules.ServiceUserName(row.Name)), ct);
        if (row.ServiceRoleId is int rid) await db.AppRoles.Where(r => r.RoleId == rid).ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, IntegrationRules.ServiceRoleName(row.Name)), ct);
    }

    // ---------------------------------------------------------------- validaciones

    /// <summary>Una integración de tipo API solo admite alcances de la API; las demás, solo el de entrada de órdenes por archivo.</summary>
    private static IReadOnlyDictionary<string, string> ValidateScopeKinds(IReadOnlyList<string> scopes, bool isApi, bool outbound = false)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (outbound) { if (scopes.Any(s => !string.Equals(s, IntegrationScopes.FilesOrdersExport, StringComparison.OrdinalIgnoreCase))) errors["scopes"] = IntegrationScopes.OutboundScopeOnlyMessage; }
        else if (!isApi && scopes.Any(s => string.Equals(s, IntegrationScopes.FilesOrdersExport, StringComparison.OrdinalIgnoreCase))) errors["scopes"] = IntegrationScopes.InboundScopeOnlyMessage;
        else if (isApi && scopes.Any(s => !IntegrationScopes.IsApiScope(s))) errors["scopes"] = IntegrationScopes.ApiScopesOnlyMessage;
        else if (!isApi && scopes.Any(IntegrationScopes.IsApiScope)) errors["scopes"] = IntegrationScopes.FileScopeOnlyMessage;
        return errors;
    }

    private static string RequireName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n)) throw new ValidationException("name", IntegrationRules.NameRequiredMessage);
        if (n.Length > IntegrationRules.NameMax) throw new ValidationException("name", IntegrationRules.NameMaxMessage);
        return n;
    }

    private static string? NullIfEmpty(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > IntegrationRules.DescriptionMax) throw new ValidationException("description", $"Máximo {IntegrationRules.DescriptionMax} caracteres.");
        return t;
    }

    private static int ValidateRetention(int? days)
    {
        var d = days ?? IntegrationRules.DefaultRetentionDays;
        if (d < 1 || d > IntegrationRules.MaxRetentionDays) throw new ValidationException("retentionDays", IntegrationRules.RetentionMessage);
        return d;
    }

    private static (string Connector, string? Folder, string ScheduleKind, int? Interval) ValidateConnectorAndSchedule(string? connectorKind, string? folder, string? scheduleKind, int? interval, bool outbound = false)
    {
        var connector = (connectorKind ?? string.Empty).Trim().ToUpperInvariant();
        if (!IntegrationConnectorKinds.All.Contains(connector)) throw new ValidationException("connectorKind", $"Tipo de conector desconocido: {connectorKind}.");
        if (!IntegrationConnectorKinds.Available.Contains(connector)) throw new ValidationException("connectorKind", IntegrationRules.ConnectorNotAvailableMessage);
        if (outbound && !IntegrationConnectorKinds.Outbound.Contains(connector)) throw new ValidationException("connectorKind", OutputRules.OutboundConnectorMessage);
        if (!outbound && connector == IntegrationConnectorKinds.Http) throw new ValidationException("connectorKind", "El conector HTTP es solo para integraciones de salida."); 
        string? folderName = null;
        if (connector == IntegrationConnectorKinds.Folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ValidationException("folder", IntegrationRules.FolderRequiredMessage);
            if (!IntegrationRules.IsValidFolder(folder)) throw new ValidationException("folder", IntegrationRules.FolderInvalidMessage);
            folderName = folder.Trim();
        }
        var kind = (scheduleKind ?? IntegrationScheduleKinds.Manual).Trim().ToUpperInvariant();
        if (kind == IntegrationScheduleKinds.Interval)
        {
            if (connector == IntegrationConnectorKinds.Manual) throw new ValidationException("scheduleKind", IntegrationRules.ManualNoScheduleMessage);
            if (interval is not int s || s < IntegrationScheduleKinds.MinIntervalSeconds || s > IntegrationScheduleKinds.MaxIntervalSeconds) throw new ValidationException("intervalSeconds", IntegrationRules.IntervalMessage);
            return (connector, folderName, kind, s);
        }
        if (kind != IntegrationScheduleKinds.Manual) throw new ValidationException("scheduleKind", $"Horario desconocido: {scheduleKind}. Use MANUAL o INTERVAL.");
        return (connector, folderName, kind, null);
    }

    private async Task<List<int>> ValidateNotifyAsync(IEnumerable<int>? ids, int? extra, CancellationToken ct)
    {
        var wanted = (ids ?? Array.Empty<int>()).Concat(extra is int e ? new[] { e } : Array.Empty<int>()).Distinct().ToList();
        if (wanted.Count == 0) return wanted;
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var valid = (await NotifyCandidatesAsync(tenantId, ct)).Select(u => u.Id).ToHashSet();
        // un administrador de plataforma que se incluye a sí mismo no está en la lista de candidatos: se acepta si es quien pide
        if (wanted.Any(id => !valid.Contains(id) && id != tenant.UserId)) throw new ValidationException("notifyUserIds", IntegrationRules.NotifyUserMessage);
        return wanted;
    }

    private static void ThrowIfErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (errors.Count > 0) throw new ValidationException(errors.ToDictionary(kv => kv.Key, kv => new[] { kv.Value }));
    }
}
