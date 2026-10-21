using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 132 (P11) — la bandeja de las integraciones: lo que entró y no se pudo procesar solo. Cada ítem trae el motivo exacto y los datos tal como llegaron; se puede corregir en pantalla
/// (las mismas celdas del archivo), reprocesar (vuelve a pasar por el importador con la plantilla de la integración; si ya cuadra, nace la orden), descartar con nota y descargar en CSV.
/// Una orden de la bandeja nunca crea una orden a medias; pasados los días de retención de la integración se purga el detalle y queda el motivo.
/// </summary>
public sealed class IntegrationInboxService(TeikemDbContext db, ITenantContext tenant, IntegrationRunner runner)
{
    public const string NotFoundLabel = "Ítem de la bandeja";
    public const string NotPendingMessage = "Este ítem ya se resolvió (reprocesado o descartado).";
    public const string NoDataMessage = "Los datos de este ítem ya no se guardan (pasó la retención o el archivo no se pudo leer): no se puede corregir ni reprocesar; descártelo y pida el archivo de nuevo.";
    public const string RowsMismatchMessage = "Las filas no coinciden con las del ítem; corrija solo las celdas de las filas que se muestran.";
    public const string CellsMismatchMessage = "Cada fila debe traer la misma cantidad de celdas que tenía.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    // ---------------------------------------------------------------- consulta

    public async Task<InboxPageDto> ListAsync(Guid? integrationPublicId, string? status, int skip, int take, CancellationToken ct)
    {
        var q = db.IntegrationInboxItems.AsNoTracking().AsQueryable();
        if (integrationPublicId is Guid ip)
        {
            var id = await db.Integrations.AsNoTracking().Where(i => i.PublicId == ip).Select(i => (int?)i.IntegrationId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
            q = q.Where(i => i.IntegrationId == id);
        }
        var pending = await q.CountAsync(i => i.Status == InboxStatuses.Pending, ct);
        var st = status?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(st)) q = q.Where(i => i.Status == st);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(i => i.IntegrationInboxItemId).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        return new InboxPageDto(await ToDtosAsync(items, withData: false, ct), total, pending);
    }

    public async Task<InboxItemDto> GetAsync(long id, CancellationToken ct)
    {
        var item = await db.IntegrationInboxItems.AsNoTracking().FirstOrDefaultAsync(i => i.IntegrationInboxItemId == id, ct) ?? throw new NotFoundException(NotFoundLabel);
        return (await ToDtosAsync(new List<IntegrationInboxItem> { item }, withData: true, ct))[0];
    }

    private async Task<List<InboxItemDto>> ToDtosAsync(List<IntegrationInboxItem> items, bool withData, CancellationToken ct)
    {
        var integrationIds = items.Select(i => i.IntegrationId).Distinct().ToList();
        var integrations = await db.Integrations.AsNoTracking().Where(i => integrationIds.Contains(i.IntegrationId)).Select(i => new { i.IntegrationId, i.PublicId, i.Name }).ToDictionaryAsync(i => i.IntegrationId, ct);
        var fileIds = items.Where(i => i.IntegrationFileLogId != null).Select(i => i.IntegrationFileLogId!.Value).Distinct().ToList();
        var files = await db.IntegrationFileLogs.AsNoTracking().Where(f => fileIds.Contains(f.IntegrationFileLogId)).Select(f => new { f.IntegrationFileLogId, f.FileName }).ToDictionaryAsync(f => f.IntegrationFileLogId, f => f.FileName, ct);
        var userIds = items.Where(i => i.ResolvedBy != null).Select(i => i.ResolvedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName, u.Email }).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        return items.Select(i =>
        {
            var data = withData ? IntegrationRunner.ParseData(i.DataJson) : null;
            var integration = integrations[i.IntegrationId];
            return new InboxItemDto(i.IntegrationInboxItemId, integration.PublicId, integration.Name, i.IntegrationRunId, i.IntegrationFileLogId is long f && files.TryGetValue(f, out var fn) ? fn : null,
                i.SourceRow, i.ReferenceKey, i.Reason, i.Status, i.CreatedAtUtc, i.ResolvedAtUtc, i.ResolvedBy is int u && names.TryGetValue(u, out var n) ? n : null, i.Note, i.OrderPublicId,
                !string.IsNullOrWhiteSpace(i.DataJson), data?.Header, data?.Rows);
        }).ToList();
    }

    // ---------------------------------------------------------------- corregir

    /// <summary>Corrige las celdas de las filas del ítem. Solo las celdas: las mismas filas y la misma cantidad de columnas (no se agregan ni se quitan filas).</summary>
    public async Task<InboxItemDto> FixAsync(long id, InboxFixRequest req, CancellationToken ct)
    {
        var item = await LoadPendingAsync(id, ct);
        var data = IntegrationRunner.ParseData(item.DataJson) ?? throw new ConflictException(NoDataMessage);
        var incoming = (req.Rows ?? new List<InboxRowDto>()).ToDictionary(r => r.Row);
        if (incoming.Count != data.Rows.Count || data.Rows.Any(r => !incoming.ContainsKey(r.Row))) throw new ValidationException("rows", RowsMismatchMessage);
        foreach (var row in data.Rows)
            if (incoming[row.Row].Cells.Count != row.Cells.Count) throw new ValidationException("rows", CellsMismatchMessage);
        var fixedRows = data.Rows.Select(r => new InboxRowDto(r.Row, incoming[r.Row].Cells.Select(c => c ?? string.Empty).ToList())).ToList();
        item.DataJson = JsonSerializer.Serialize(new IntegrationRunner.InboxData(data.Header, fixedRows), Json);
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- reprocesar

    public async Task<InboxReprocessResultDto> ReprocessAsync(long id, CancellationToken ct)
    {
        var results = await ReprocessManyAsync(new[] { id }, ct);
        return results;
    }

    /// <summary>Reprocesa todos los pendientes de una integración (p. ej. tras cambiar la plantilla o crear el consignatario que faltaba).</summary>
    public async Task<InboxReprocessResultDto> ReprocessAllAsync(Guid integrationPublicId, CancellationToken ct)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == integrationPublicId, ct) ?? throw new NotFoundException(IntegrationService.NotFoundLabel);
        var ids = await db.IntegrationInboxItems.AsNoTracking().Where(i => i.IntegrationId == integration.IntegrationId && i.Status == InboxStatuses.Pending && i.DataJson != null)
            .OrderBy(i => i.IntegrationInboxItemId).Select(i => i.IntegrationInboxItemId).Take(200).ToListAsync(ct);
        return await ReprocessManyAsync(ids, ct);
    }

    private async Task<InboxReprocessResultDto> ReprocessManyAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var done = 0;
        var still = 0;
        var outItems = new List<InboxItemDto>();
        foreach (var id in ids)
        {
            var item = await LoadPendingAsync(id, ct);
            var data = IntegrationRunner.ParseData(item.DataJson) ?? throw new ConflictException(NoDataMessage);
            var integration = await db.Integrations.AsNoTracking().FirstAsync(i => i.IntegrationId == item.IntegrationId, ct);
            if (!integration.IsActive) throw new StatusRuleException(IntegrationRules.InactiveMessage);
            var template = await db.ImportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ImportTemplateId == integration.ImportTemplateId, ct) ?? throw new ValidationException("template", IntegrationRules.TemplateRequiredMessage);
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == integration.ClientId, ct) ?? throw new ValidationException("client", IntegrationRules.ClientRequiredMessage);
            var tenantId = ((TenantContext)tenant).RequireTenantId();

            var run = new IntegrationRun { TenantId = tenantId, IntegrationId = integration.IntegrationId, TriggeredBy = IntegrationTriggers.Reprocess, TriggeredByUserId = tenant.UserId };
            db.IntegrationRuns.Add(run);
            await db.SaveChangesAsync(ct);
            var csv = IntegrationRunner.ToCsv(data, ImportTemplateService.DelimiterOf(template));
            var outcome = await runner.ProcessBytesAsync(integration, template, client, run, $"bandeja-{item.IntegrationInboxItemId}.csv", Encoding.UTF8.GetBytes(csv), checkDuplicate: false, ct);
            db.IntegrationFileLogs.Add(outcome.Log);
            run.FilesRead = 1;
            run.OrdersCreated = outcome.Created.Count;
            run.Rejected = outcome.Rejected.Count;
            run.FinishedUtc = DateTime.UtcNow;
            var created = outcome.Created.Count > 0 && outcome.Rejected.Count == 0;
            run.Status = created ? IntegrationRunStatuses.Ok : IntegrationRunStatuses.Partial;
            run.Message = created ? "Envío de la bandeja reprocesado." : "El envío de la bandeja sigue con problemas.";

            var tracked = await db.IntegrationInboxItems.FirstAsync(i => i.IntegrationInboxItemId == id, ct);
            if (created)
            {
                tracked.Status = InboxStatuses.Reprocessed;
                tracked.OrderPublicId = outcome.Created[0];
                tracked.ResolvedAtUtc = DateTime.UtcNow;
                tracked.ResolvedBy = tenant.UserId;
                done++;
            }
            else
            {
                tracked.Reason = (outcome.Rejected.FirstOrDefault()?.Reason ?? "El envío no se pudo procesar.") is { Length: > 1000 } r ? r[..1000] : outcome.Rejected.FirstOrDefault()?.Reason ?? "El envío no se pudo procesar.";
                still++;
            }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
            outItems.Add(await GetAsync(id, ct));
        }
        return new InboxReprocessResultDto(done, still, outItems);
    }

    // ---------------------------------------------------------------- descartar y descargar

    public async Task<InboxItemDto> DiscardAsync(long id, string? note, CancellationToken ct)
    {
        var item = await LoadPendingAsync(id, ct);
        var n = note?.Trim();
        if (n is { Length: > 400 }) throw new ValidationException("note", "Máximo 400 caracteres.");
        item.Status = InboxStatuses.Discarded;
        item.Note = string.IsNullOrEmpty(n) ? null : n;
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.ResolvedBy = tenant.UserId;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>CSV con los pendientes (integración, fila, referencia, motivo y fecha): para devolverlos al cliente.</summary>
    public async Task<string> ExportPendingAsync(Guid? integrationPublicId, CancellationToken ct)
    {
        var page = await ListAsync(integrationPublicId, InboxStatuses.Pending, 0, 200, ct);
        static string Q(string? s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("integracion,archivo,fila,referencia,motivo,fecha");
        foreach (var i in page.Items) sb.AppendLine(string.Join(',', Q(i.IntegrationName), Q(i.FileName), i.SourceRow?.ToString() ?? "", Q(i.ReferenceKey), Q(i.Reason), i.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm")));
        return sb.ToString();
    }

    /// <summary>Purga el detalle de los ítems más viejos que los días de retención de su integración (queda el motivo). Devuelve cuántos purgó.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var purged = 0;
        foreach (var integration in await db.Integrations.AsNoTracking().Select(i => new { i.IntegrationId, i.RetentionDays }).ToListAsync(ct))
        {
            var cutoff = now.AddDays(-integration.RetentionDays);
            purged += await db.IntegrationInboxItems.Where(i => i.IntegrationId == integration.IntegrationId && i.DataJson != null && i.CreatedAtUtc < cutoff)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DataJson, (string?)null), ct);
        }
        // la bitácora de las llaves de API (Lote 135) se guarda 30 días
        var logCutoff = now.AddDays(-IntegrationRules.DefaultRetentionDays);
        purged += await db.IntegrationApiLogs.Where(l => l.CreatedAtUtc < logCutoff).ExecuteDeleteAsync(ct);
        return purged;
    }

    private async Task<IntegrationInboxItem> LoadPendingAsync(long id, CancellationToken ct)
    {
        var item = await db.IntegrationInboxItems.FirstOrDefaultAsync(i => i.IntegrationInboxItemId == id, ct) ?? throw new NotFoundException(NotFoundLabel);
        if (item.Status != InboxStatuses.Pending) throw new ConflictException(NotPendingMessage);
        return item;
    }
}
