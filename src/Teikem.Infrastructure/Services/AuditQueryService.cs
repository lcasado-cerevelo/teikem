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
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) { var id = await lookups.GetIdAsync(LookupDomains.EntityType, entityType, ct); q = q.Where(a => a.EntityTypeLookupId == id); }
        if (entityId.HasValue) q = q.Where(a => a.EntityId == entityId);
        if (userId.HasValue) q = q.Where(a => a.UserId == userId);
        if (from.HasValue) q = q.Where(a => a.CreatedAtUtc >= from);
        if (to.HasValue) q = q.Where(a => a.CreatedAtUtc < to);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.AuditLogId).Skip(skip).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        var names = await UserNamesAsync(rows.Select(r => r.UserId), ct);
        var items = new List<AuditLogDto>();
        foreach (var r in rows)
            items.Add(new AuditLogDto(r.AuditLogId, r.CreatedAtUtc, await Label(LookupDomains.EntityType, r.EntityTypeLookupId, ct), r.EntityId,
                await Label(LookupDomains.AuditAction, r.ActionLookupId, ct), r.UserId, r.UserId.HasValue ? names.GetValueOrDefault(r.UserId.Value) : null,
                r.ChangesJson, r.CorrelationId, r.IpAddress));
        return new PagedResult<AuditLogDto>(items, total, skip, take);
    }

    public async Task<PagedResult<SecurityEventDto>> GetSecurityEventsAsync(string? eventType, int? userId, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct)
    {
        var q = db.SecurityEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId || (e.TenantId == null && e.UserId != null));
        if (!string.IsNullOrWhiteSpace(eventType)) { var id = await lookups.GetIdAsync(LookupDomains.SecurityEventType, eventType, ct); q = q.Where(e => e.EventTypeLookupId == id); }
        if (userId.HasValue) q = q.Where(e => e.UserId == userId);
        if (from.HasValue) q = q.Where(e => e.CreatedAtUtc >= from);
        if (to.HasValue) q = q.Where(e => e.CreatedAtUtc < to);
        // Eventos sin tenant (login fallido antes de resolver tenant) solo de usuarios que son miembros de este tenant
        var memberIds = await db.UserTenants.AsNoTracking().Select(m => m.UserId).ToListAsync(ct);
        q = q.Where(e => e.TenantId != null || memberIds.Contains(e.UserId!.Value));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.SecurityEventId).Skip(skip).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        var names = await UserNamesAsync(rows.Select(r => r.UserId), ct);
        var items = new List<SecurityEventDto>();
        foreach (var r in rows)
            items.Add(new SecurityEventDto(r.SecurityEventId, r.CreatedAtUtc, await Label(LookupDomains.SecurityEventType, r.EventTypeLookupId, ct),
                await Label(LookupDomains.SecurityOutcome, r.OutcomeLookupId, ct), r.UserId, r.UserId.HasValue ? names.GetValueOrDefault(r.UserId.Value) : null,
                r.IpAddress, r.UserAgent, r.DetailJson));
        return new PagedResult<SecurityEventDto>(items, total, skip, take);
    }

    /// <summary>Pestaña "Actividad" del mock: AuditLog y SecurityEvent unificados, cada fila conserva su tipo original.</summary>
    public async Task<PagedResult<ActivityRowDto>> GetActivityAsync(string kind, string? text, DateTime? from, DateTime? to, int skip, int take, CancellationToken ct)
    {
        var rows = new List<ActivityRowDto>();
        var includeChanges = kind is "all" or "changes";
        var includeSecurity = kind is "all" or "security";
        var limit = Math.Clamp(take + skip, 1, 2000);
        if (includeChanges)
        {
            var a = await GetAuditLogAsync(null, null, null, from, to, 0, limit, ct);
            rows.AddRange(a.Items.Select(x => new ActivityRowDto("change", x.Id, x.CreatedAtUtc, $"{x.Action} · {x.EntityType} #{x.EntityId}", x.ChangesJson, x.UserId, x.UserName, x.IpAddress, x.CorrelationId)));
        }
        if (includeSecurity)
        {
            var s = await GetSecurityEventsAsync(null, null, from, to, 0, limit, ct);
            rows.AddRange(s.Items.Select(x => new ActivityRowDto("security", x.Id, x.CreatedAtUtc, $"{x.EventType} · {x.Outcome}", x.DetailJson, x.UserId, x.UserName, x.IpAddress, null)));
        }
        if (!string.IsNullOrWhiteSpace(text))
            rows = rows.Where(r => (r.Type + " " + r.Detail + " " + r.UserName + " " + r.IpAddress).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        var ordered = rows.OrderByDescending(r => r.CreatedAtUtc).ToList();
        return new PagedResult<ActivityRowDto>(ordered.Skip(skip).Take(take).ToList(), ordered.Count, skip, take);
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

    private async Task<Dictionary<int, string?>> UserNamesAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
    }
}
