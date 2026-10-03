using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Capa E (consulta): bitácora de cambios, eventos de seguridad y la vista unificada "Actividad" + exportación CSV.</summary>
public sealed class AuditQueryService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    public async Task<PagedResult<AuditLogDto>> GetAuditLogAsync(string? entityType, int? entityId, int? userId, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct)
    {
        var q = ChangesScope(from, to);
        if (!string.IsNullOrWhiteSpace(entityType)) { var id = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct); q = q.Where(a => a.EntityTypeLookupId == id); }
        if (entityId.HasValue) q = q.Where(a => a.EntityId == entityId);
        if (userId.HasValue) q = q.Where(a => a.UserId == userId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.AuditLogId).Skip(skip).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(await ToDtosAsync(rows, ct), total, skip, take);
    }

    public async Task<PagedResult<SecurityEventDto>> GetSecurityEventsAsync(string? eventType, int? userId, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct)
    {
        var q = await EventsScopeAsync(from, to, ct);
        if (!string.IsNullOrWhiteSpace(eventType)) { var id = await lookups.GetIdAsync(LookupDomains.SecurityEventType, eventType, ct); q = q.Where(e => e.EventTypeLookupId == id); }
        if (userId.HasValue) q = q.Where(e => e.UserId == userId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.SecurityEventId).Skip(skip).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return new PagedResult<SecurityEventDto>(await ToDtosAsync(rows, ct), total, skip, take);
    }

    /// <summary>
    /// Pestaña "Actividad": AuditLog y SecurityEvent unificados, cada fila conserva su tipo original, lo más reciente primero.
    /// Lote F10: `Total` es el conteo REAL de las dos bitácoras con los filtros (antes salía de una ventana de skip+take filas y
    /// crecía al paginar) y el texto se filtra en la base: detalle (JSON), IP, nombre o correo del usuario, etiqueta o código del
    /// tipo / acción / entidad / resultado, o el número de la entidad ("#12"). Se leen solo las llaves (fecha, id) de las
    /// primeras skip+take filas de cada bitácora, se mezclan y se cargan completas solo las de la página.
    /// </summary>
    public async Task<PagedResult<ActivityRowDto>> GetActivityAsync(string kind, string? text, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 2000); // 2000 = lo que exporta ExportActivityCsvAsync de una vez
        var includeChanges = kind is "all" or "changes";
        var includeSecurity = kind is "all" or "security";
        var needle = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var changes = includeChanges ? ChangesScope(from, to) : null;
        var events = includeSecurity ? await EventsScopeAsync(from, to, ct) : null;
        if (needle is not null)
        {
            var userIds = await db.Users.AsNoTracking()
                .Where(u => (u.FullName != null && u.FullName.Contains(needle)) || (u.Email != null && u.Email.Contains(needle)))
                .Select(u => u.Id).ToListAsync(ct);
            if (changes is not null)
            {
                var entityIds = await MatchingLookupsAsync(LookupDomains.EntityType, needle, ct);
                var actionIds = await MatchingLookupsAsync(LookupDomains.AuditAction, needle, ct);
                int? number = int.TryParse(needle.TrimStart('#'), out var n) ? n : null;
                changes = changes.Where(a => (a.ChangesJson != null && a.ChangesJson.Contains(needle)) || (a.IpAddress != null && a.IpAddress.Contains(needle))
                    || (a.UserId != null && userIds.Contains(a.UserId.Value)) || entityIds.Contains(a.EntityTypeLookupId) || actionIds.Contains(a.ActionLookupId)
                    || (number != null && a.EntityId == number));
            }
            if (events is not null)
            {
                var typeIds = await MatchingLookupsAsync(LookupDomains.SecurityEventType, needle, ct);
                var outcomeIds = await MatchingLookupsAsync(LookupDomains.SecurityOutcome, needle, ct);
                events = events.Where(e => (e.DetailJson != null && e.DetailJson.Contains(needle)) || (e.IpAddress != null && e.IpAddress.Contains(needle))
                    || (e.UserId != null && userIds.Contains(e.UserId.Value)) || typeIds.Contains(e.EventTypeLookupId) || outcomeIds.Contains(e.OutcomeLookupId));
            }
        }

        var total = (changes is null ? 0 : await changes.CountAsync(ct)) + (events is null ? 0 : await events.CountAsync(ct));
        var window = skip + take;
        var keys = new List<(bool Change, long Id, DateTime At)>();
        if (changes is not null)
            keys.AddRange((await changes.OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.AuditLogId).Take(window)
                .Select(a => new { a.AuditLogId, a.CreatedAtUtc }).ToListAsync(ct)).Select(x => (true, x.AuditLogId, x.CreatedAtUtc)));
        if (events is not null)
            keys.AddRange((await events.OrderByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.SecurityEventId).Take(window)
                .Select(e => new { e.SecurityEventId, e.CreatedAtUtc }).ToListAsync(ct)).Select(x => (false, x.SecurityEventId, x.CreatedAtUtc)));
        var page = keys.OrderByDescending(k => k.At).ThenByDescending(k => k.Id).Skip(skip).Take(take).ToList();

        var changeIds = page.Where(k => k.Change).Select(k => k.Id).ToList();
        var eventIds = page.Where(k => !k.Change).Select(k => k.Id).ToList();
        var changeDtos = changeIds.Count == 0 ? new Dictionary<long, AuditLogDto>()
            : (await ToDtosAsync(await db.AuditLogs.AsNoTracking().Where(a => changeIds.Contains(a.AuditLogId)).ToListAsync(ct), ct)).ToDictionary(x => x.Id);
        var eventDtos = eventIds.Count == 0 ? new Dictionary<long, SecurityEventDto>()
            : (await ToDtosAsync(await db.SecurityEvents.AsNoTracking().Where(e => eventIds.Contains(e.SecurityEventId)).ToListAsync(ct), ct)).ToDictionary(x => x.Id);

        var items = new List<ActivityRowDto>(page.Count);
        foreach (var k in page)
        {
            if (k.Change && changeDtos.TryGetValue(k.Id, out var x))
                items.Add(new ActivityRowDto("change", x.Id, x.CreatedAtUtc, $"{x.Action} · {x.EntityType} #{x.EntityId}", x.ChangesJson, x.UserId, x.UserName, x.IpAddress, x.CorrelationId, x.ActionCode));
            else if (!k.Change && eventDtos.TryGetValue(k.Id, out var e))
                items.Add(new ActivityRowDto("security", e.Id, e.CreatedAtUtc, $"{e.EventType} · {e.Outcome}", e.DetailJson, e.UserId, e.UserName, e.IpAddress, null, e.EventTypeCode, e.OutcomeCode));
        }
        return new PagedResult<ActivityRowDto>(items, total, skip, take);
    }

    /// <summary>Bitácora de cambios de la compañía (filtro global por tenant) en el rango [from, to).</summary>
    private IQueryable<Domain.Audit.AuditLog> ChangesScope(DateTime? from, DateTime? to)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(a => a.CreatedAtUtc >= from);
        if (to.HasValue) q = q.Where(a => a.CreatedAtUtc < to);
        return q;
    }

    /// <summary>
    /// Eventos de seguridad de la compañía en el rango [from, to): los suyos y, sin compañía (login fallido antes de resolverla),
    /// solo los de usuarios que son miembros de esta compañía.
    /// </summary>
    private async Task<IQueryable<Domain.Security.SecurityEvent>> EventsScopeAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = db.SecurityEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId || (e.TenantId == null && e.UserId != null));
        if (from.HasValue) q = q.Where(e => e.CreatedAtUtc >= from);
        if (to.HasValue) q = q.Where(e => e.CreatedAtUtc < to);
        var memberIds = await db.UserTenants.AsNoTracking().Select(m => m.UserId).ToListAsync(ct);
        return q.Where(e => e.TenantId != null || memberIds.Contains(e.UserId!.Value));
    }

    /// <summary>Ids del catálogo cuyo código o etiqueta (en el idioma de la petición) contiene el texto, sin distinguir mayúsculas.</summary>
    private async Task<List<int>> MatchingLookupsAsync(string domain, string needle, CancellationToken ct)
        => (await lookups.GetDomainAsync(domain, ct))
            .Where(l => l.InternalCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        || MultilingualText.Resolve(l.LabelJson, tenant.Lang).Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.LookupCodeId).ToList();

    private async Task<List<AuditLogDto>> ToDtosAsync(IReadOnlyList<Domain.Audit.AuditLog> rows, CancellationToken ct)
    {
        var names = await UserNamesAsync(rows.Select(r => r.UserId), ct);
        var items = new List<AuditLogDto>(rows.Count);
        foreach (var r in rows)
            items.Add(new AuditLogDto(r.AuditLogId, r.CreatedAtUtc, await Label(LookupDomains.EntityType, r.EntityTypeLookupId, ct), r.EntityId,
                await Label(LookupDomains.AuditAction, r.ActionLookupId, ct), r.UserId, r.UserId.HasValue ? names.GetValueOrDefault(r.UserId.Value) : null,
                r.ChangesJson, r.CorrelationId, r.IpAddress, await Code(r.ActionLookupId, ct)));
        return items;
    }

    private async Task<List<SecurityEventDto>> ToDtosAsync(IReadOnlyList<Domain.Security.SecurityEvent> rows, CancellationToken ct)
    {
        var names = await UserNamesAsync(rows.Select(r => r.UserId), ct);
        var items = new List<SecurityEventDto>(rows.Count);
        foreach (var r in rows)
            items.Add(new SecurityEventDto(r.SecurityEventId, r.CreatedAtUtc, await Label(LookupDomains.SecurityEventType, r.EventTypeLookupId, ct),
                await Label(LookupDomains.SecurityOutcome, r.OutcomeLookupId, ct), r.UserId, r.UserId.HasValue ? names.GetValueOrDefault(r.UserId.Value) : null,
                r.IpAddress, r.UserAgent, r.DetailJson, await Code(r.EventTypeLookupId, ct), await Code(r.OutcomeLookupId, ct)));
        return items;
    }

    public async Task<string> ExportActivityCsvAsync(string kind, string? text, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var page = await GetActivityAsync(kind, text, from, to, 0, 2000, ct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Cuando,Tipo,Usuario,Detalle,IP,Correlacion");
        foreach (var r in page.Items)
            sb.AppendLine(string.Join(",", new[] { r.CreatedAtUtc.ToString("O"), r.Kind + " · " + r.Type, r.UserName, r.Detail, r.IpAddress, r.CorrelationId?.ToString() }.Select(Csv)));
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private async Task<string> Label(string domain, int id, CancellationToken ct)
    {
        var l = await lookups.GetAsync(id, ct);
        return l is null ? id.ToString() : MultilingualText.Resolve(l.LabelJson, tenant.Lang);
    }

    /// <summary>InternalCode del catálogo (Lote F10: la pantalla decide por código, no por la etiqueta traducida).</summary>
    private async Task<string?> Code(int id, CancellationToken ct) => (await lookups.GetAsync(id, ct))?.InternalCode;

    private async Task<Dictionary<int, string?>> UserNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
    }
}
