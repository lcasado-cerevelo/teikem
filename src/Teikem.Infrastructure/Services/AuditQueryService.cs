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
                items.Add(new ActivityRowDto("change", x.Id, x.CreatedAtUtc, $"{x.Action} · {x.EntityType} {x.EntityName ?? $"#{x.EntityId}"}", x.ChangesJson, x.UserId, x.UserName, x.IpAddress, x.CorrelationId, x.ActionCode));
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
        var resolver = await BuildResolverAsync(rows.Select(r => r.ChangesJson), ct);
        var entityNames = await EntityNamesAsync(rows, ct);
        var items = new List<AuditLogDto>(rows.Count);
        foreach (var r in rows)
            items.Add(new AuditLogDto(r.AuditLogId, r.CreatedAtUtc, await EntityTypeLabelAsync(r, ct), r.EntityId,
                await Label(LookupDomains.AuditAction, r.ActionLookupId, ct), r.UserId, r.UserId.HasValue ? names.GetValueOrDefault(r.UserId.Value) : null,
                AuditChangesHumanizer.Humanize(r.ChangesJson, tenant.Lang, resolver), r.CorrelationId, r.IpAddress, await Code(r.ActionLookupId, ct), entityNames.GetValueOrDefault(r.AuditLogId)));
        return items;
    }

    /// <summary>
    /// 2026-10-22 (Luis): el renglón nombra QUÉ cambió (el almacén, la zona, la posición…), no un número. Se resuelve por tipo en lote; lo que no se puede nombrar queda sin nombre (la pantalla cae al «#id»).
    /// </summary>
    /// <summary>Código del tipo del renglón; en el historial viejo de zonas y posiciones (guardado como WAREHOUSE) es el tipo que le toca por sus campos.</summary>
    private async Task<string?> EffectiveTypeCodeAsync(Domain.Audit.AuditLog r, CancellationToken ct)
    {
        var code = await Code(r.EntityTypeLookupId, ct);
        return code == EntityTypes.Warehouse ? AuditChangesHumanizer.LegacyWarehouseChildType(r.ChangesJson) ?? code : code;
    }

    private async Task<string> EntityTypeLabelAsync(Domain.Audit.AuditLog r, CancellationToken ct)
    {
        var code = await EffectiveTypeCodeAsync(r, ct);
        if (code is not null && code != await Code(r.EntityTypeLookupId, ct) && await lookups.TryGetIdAsync(LookupDomains.EntityType, code, ct) is { } id)
            return await Label(LookupDomains.EntityType, id, ct);
        return await Label(LookupDomains.EntityType, r.EntityTypeLookupId, ct);
    }

    private async Task<Dictionary<long, string>> EntityNamesAsync(IReadOnlyList<Domain.Audit.AuditLog> rows, CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        var typed = new List<(long LogId, string Type, int Id)>();
        foreach (var r in rows)
            if (r.EntityId > 0 && await EffectiveTypeCodeAsync(r, ct) is { } code) typed.Add((r.AuditLogId, code, r.EntityId));
        List<int> Ids(string type) => typed.Where(t => t.Type == type).Select(t => t.Id).Distinct().ToList();
        var names = new Dictionary<(string, int), string>();

        var wh = Ids(EntityTypes.Warehouse);
        var zn = Ids(EntityTypes.WarehouseZone);
        var bn = Ids(EntityTypes.WarehouseBin);
        var dk = Ids(EntityTypes.WarehouseDock);
        var warehouseIds = new HashSet<int>(wh);
        var zones = zn.Count + bn.Count == 0 ? new() : await db.WarehouseZones.AsNoTracking().Where(z => zn.Contains(z.WarehouseZoneId) || db.WarehouseBins.Any(b => bn.Contains(b.WarehouseBinId) && b.WarehouseZoneId == z.WarehouseZoneId))
            .Select(z => new { z.WarehouseZoneId, z.WarehouseId, z.Name }).ToListAsync(ct);
        var bins = bn.Count == 0 ? new() : await db.WarehouseBins.AsNoTracking().Where(b => bn.Contains(b.WarehouseBinId)).Select(b => new { b.WarehouseBinId, b.WarehouseId, b.WarehouseZoneId, b.Code }).ToListAsync(ct);
        var docks = dk.Count == 0 ? new() : await db.WarehouseDocks.AsNoTracking().Where(d => dk.Contains(d.WarehouseDockId)).Select(d => new { d.WarehouseDockId, d.WarehouseId, d.Code }).ToListAsync(ct);
        foreach (var z in zones) warehouseIds.Add(z.WarehouseId);
        foreach (var b in bins) warehouseIds.Add(b.WarehouseId);
        foreach (var d in docks) warehouseIds.Add(d.WarehouseId);
        var whNames = warehouseIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.WarehouseId)).ToDictionaryAsync(w => w.WarehouseId, w => w.Name, ct);
        string Wh(int id) => whNames.GetValueOrDefault(id) ?? "?";
        foreach (var id in wh) if (whNames.TryGetValue(id, out var n)) names[(EntityTypes.Warehouse, id)] = n;
        foreach (var z in zones.Where(z => zn.Contains(z.WarehouseZoneId))) names[(EntityTypes.WarehouseZone, z.WarehouseZoneId)] = $"{z.Name} ({Wh(z.WarehouseId)})";
        foreach (var b in bins)
        {
            var zone = zones.FirstOrDefault(z => z.WarehouseZoneId == b.WarehouseZoneId);
            names[(EntityTypes.WarehouseBin, b.WarehouseBinId)] = zone is null ? $"{b.Code} ({Wh(b.WarehouseId)})" : $"{b.Code} ({zone.Name} · {Wh(b.WarehouseId)})";
        }
        foreach (var d in docks) names[(EntityTypes.WarehouseDock, d.WarehouseDockId)] = $"{d.Code} ({Wh(d.WarehouseId)})";

        var cl = Ids(EntityTypes.Client);
        if (cl.Count > 0) foreach (var c in await db.Clients.AsNoTracking().Where(c => cl.Contains(c.ClientId)).Select(c => new { c.ClientId, c.Name }).ToListAsync(ct)) names[(EntityTypes.Client, c.ClientId)] = c.Name;
        var pr = Ids(EntityTypes.Product);
        if (pr.Count > 0) foreach (var p in await db.Set<Domain.Wms.Product>().AsNoTracking().Where(p => pr.Contains(p.ProductId)).Select(p => new { p.ProductId, p.Sku, p.Name }).ToListAsync(ct)) names[(EntityTypes.Product, p.ProductId)] = $"{p.Sku} · {p.Name}";
        var su = Ids(EntityTypes.Supplier);
        if (su.Count > 0) foreach (var s in await db.Set<Domain.Wms.Supplier>().AsNoTracking().Where(s => su.Contains(s.SupplierId)).Select(s => new { s.SupplierId, s.Name }).ToListAsync(ct)) names[(EntityTypes.Supplier, s.SupplierId)] = s.Name;

        foreach (var t in typed) if (names.TryGetValue((t.Type, t.Id), out var n)) result[t.LogId] = n;
        return result;
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

    /// <summary>Nombres para los campos-llave de los detalles de una página de la bitácora (se piden todos juntos: una consulta por tipo de registro).</summary>
    private async Task<Func<AuditChangesHumanizer.FkKind, int, string?>> BuildResolverAsync(IEnumerable<string?> details, CancellationToken ct)
    {
        var wanted = details.SelectMany(AuditChangesHumanizer.Keys).Distinct().ToList();
        var names = new Dictionary<(AuditChangesHumanizer.FkKind, int), string>();
        List<int> Ids(AuditChangesHumanizer.FkKind k) => wanted.Where(w => w.Kind == k).Select(w => w.Id).ToList();

        foreach (var id in Ids(AuditChangesHumanizer.FkKind.Lookup))
            if (await lookups.GetAsync(id, ct) is { } l) names[(AuditChangesHumanizer.FkKind.Lookup, id)] = MultilingualText.Resolve(l.LabelJson, tenant.Lang);
        var status = Ids(AuditChangesHumanizer.FkKind.Status);
        if (status.Count > 0)
            foreach (var s in await db.StatusCodes.AsNoTracking().Where(x => status.Contains(x.StatusCodeId)).Select(x => new { x.StatusCodeId, x.LabelJson }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Status, s.StatusCodeId)] = MultilingualText.Resolve(s.LabelJson, tenant.Lang);
        foreach (var (id, name) in await UserNamesAsync(Ids(AuditChangesHumanizer.FkKind.User).Select(i => (int?)i), ct))
            if (!string.IsNullOrWhiteSpace(name)) names[(AuditChangesHumanizer.FkKind.User, id)] = name!;
        var wh = Ids(AuditChangesHumanizer.FkKind.Warehouse);
        if (wh.Count > 0)
            foreach (var w in await db.Set<Domain.Wms.Warehouse>().AsNoTracking().Where(x => wh.Contains(x.WarehouseId)).Select(x => new { x.WarehouseId, x.Name }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Warehouse, w.WarehouseId)] = w.Name;
        var cl = Ids(AuditChangesHumanizer.FkKind.Client);
        if (cl.Count > 0)
            foreach (var c in await db.Clients.AsNoTracking().Where(x => cl.Contains(x.ClientId)).Select(x => new { x.ClientId, x.Name }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Client, c.ClientId)] = c.Name;
        var pr = Ids(AuditChangesHumanizer.FkKind.Product);
        if (pr.Count > 0)
            foreach (var p in await db.Set<Domain.Wms.Product>().AsNoTracking().Where(x => pr.Contains(x.ProductId)).Select(x => new { x.ProductId, x.Sku, x.Name }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Product, p.ProductId)] = $"{p.Sku} · {p.Name}";
        var bn = Ids(AuditChangesHumanizer.FkKind.Bin);
        if (bn.Count > 0)
            foreach (var b in await db.Set<Domain.Wms.WarehouseBin>().AsNoTracking().Where(x => bn.Contains(x.WarehouseBinId)).Select(x => new { x.WarehouseBinId, x.Code }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Bin, b.WarehouseBinId)] = b.Code;
        var sp = Ids(AuditChangesHumanizer.FkKind.Supplier);
        if (sp.Count > 0)
            foreach (var s in await db.Set<Domain.Wms.Supplier>().AsNoTracking().Where(x => sp.Contains(x.SupplierId)).Select(x => new { x.SupplierId, x.Name }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Supplier, s.SupplierId)] = s.Name;
        var zn = Ids(AuditChangesHumanizer.FkKind.Zone);
        if (zn.Count > 0)
            foreach (var z in await db.Set<Domain.Wms.WarehouseZone>().AsNoTracking().Where(x => zn.Contains(x.WarehouseZoneId)).Select(x => new { x.WarehouseZoneId, x.Name }).ToListAsync(ct))
                names[(AuditChangesHumanizer.FkKind.Zone, z.WarehouseZoneId)] = z.Name;
        return (kind, id) => names.TryGetValue((kind, id), out var n) ? n : null;
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
