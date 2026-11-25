using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Migration;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Persistence;
using R = Teikem.Domain.Sales.SalesAnalysisRules;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;
using RR = Teikem.Domain.Sales.SalesRouteRules;
using K = Teikem.Domain.Sales.SalesRouteCodes;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 241 — lo que se administra del análisis de ventas (sales.analysis.manage; leer con sales.analysis.view): ajustes (fecha de corte, días de entrega, nivel de
/// servicio), rutas, representantes, cuotas por mes, líneas y sus marcas, el representante de cada cliente, el paso del campo personalizado «Representante» a
/// representantes y el enlace a mano de nombres del historial sin emparejar. Todo bajo el filtro de tenant (el TenantId sale de la sesión); bajas lógicas (IsActive).
/// <para>
/// Lote 244 (Lote A del plan de rutas): el usuario del representante es obligatorio (regla del servicio; un usuario no puede estar en dos representantes activos) y, al
/// crearlo, ponerle usuario, cambiarle la ruta o reactivarlo, si su ruta está activa y sin titular queda como TITULAR AUTOMÁTICO desde hoy en la misma transacción. La
/// cuota también puede ser de una ruta (SalesQuota.SalesRouteId) y el ajuste QuotaBy escoge cuál cuenta. Desactivar una ruta con clientes da 409; si pasa, cierra hoy sus
/// asignaciones vigentes. La lista de clientes trae la ruta propia, la efectiva y el vendedor preferido.
/// </para>
/// </summary>
public sealed class SalesAnalysisSetupService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, ILookupCache lookups)
{
    private int TenantId => ((TenantContext)tenant).RequireTenantId();

    private static (string Name, string? Error) NormalizeName(string? raw, string required)
    {
        var n = raw?.Trim();
        if (string.IsNullOrEmpty(n)) return (string.Empty, required);
        if (n.Length > R.NameMaxLength) return (string.Empty, R.NameTooLong);
        return (n, null);
    }

    // ================================================================ ajustes

    private static SalesAnalysisSettingsDto SettingsDto(SalesAnalysisSetting s)
        => new(s.CutoffDate, s.LeadTimeDays, s.ServiceLevelZ, RR.ParseQuotaBy(s.QuotaBy) ?? K.QuotaByRep);

    private async Task<string> QuotaByAsync(CancellationToken ct)
        => RR.ParseQuotaBy(await db.Set<SalesAnalysisSetting>().AsNoTracking().Select(x => x.QuotaBy).FirstOrDefaultAsync(ct)) ?? K.QuotaByRep;

    public async Task<SalesAnalysisSettingsDto> GetSettingsAsync(CancellationToken ct)
        => SettingsDto(await db.Set<SalesAnalysisSetting>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new SalesAnalysisSetting());

    public async Task<SalesAnalysisSettingsDto> UpdateSettingsAsync(SalesAnalysisSettingsRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        if (req.LeadTimeDays is int lt && (lt < 1 || lt > R.MaxLeadTimeDays)) throw new ValidationException("leadTimeDays", R.LeadTimeInvalid);
        if (req.ServiceLevelZ is decimal z && (z <= 0m || z > R.MaxServiceLevelZ)) throw new ValidationException("serviceLevelZ", R.ServiceLevelInvalid);
        string? quotaBy = null;
        if (req.QuotaBy is not null) quotaBy = RR.ParseQuotaBy(req.QuotaBy) ?? throw new ValidationException("quotaBy", RR.QuotaByInvalid);
        var s = await db.Set<SalesAnalysisSetting>().AsTracking().FirstOrDefaultAsync(ct);
        if (s is null)
        {
            s = new SalesAnalysisSetting { TenantId = TenantId };
            db.Set<SalesAnalysisSetting>().Add(s);
        }
        if (req.ClearCutoff) s.CutoffDate = null;
        else if (req.CutoffDate is DateOnly d) s.CutoffDate = d;
        if (req.LeadTimeDays is int l) s.LeadTimeDays = l;
        if (req.ServiceLevelZ is decimal zz) s.ServiceLevelZ = Math.Round(zz, 3);
        if (quotaBy is not null) s.QuotaBy = quotaBy;
        s.UpdatedAtUtc = DateTime.UtcNow;
        s.UpdatedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return SettingsDto(s);
    }

    // ================================================================ rutas

    /// <summary>Lote 244 — asignaciones VIGENTES (activas y sin cierre) de la compañía, por ruta.</summary>
    private async Task<Dictionary<int, List<SalesRouteAssignment>>> CurrentPeopleByRouteAsync(CancellationToken ct)
        => (await db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == TenantId && a.IsActive && a.ValidTo == null).ToListAsync(ct))
            .GroupBy(a => a.SalesRouteId).ToDictionary(g => g.Key, g => g.ToList());

    /// <summary>Lote 244 — cuántos clientes activos tiene cada ruta: su ruta propia o, si no tienen, la de su representante.</summary>
    private async Task<Dictionary<int, int>> ClientsByEffectiveRouteAsync(CancellationToken ct)
    {
        var repRoute = await db.Set<SalesRep>().AsNoTracking().Where(r => r.SalesRouteId != null).ToDictionaryAsync(r => r.SalesRepId, r => r.SalesRouteId!.Value, ct);
        var clients = await db.Clients.AsNoTracking().Where(c => c.IsActive && (c.SalesRouteId != null || c.SalesRepId != null))
            .Select(c => new { c.SalesRouteId, c.SalesRepId }).ToListAsync(ct);
        return clients
            .Select(c => c.SalesRouteId ?? (c.SalesRepId is int r && repRoute.TryGetValue(r, out var rr) ? rr : (int?)null))
            .Where(r => r is not null).GroupBy(r => r!.Value).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Lote 244 — las cuotas de ruta activas hasta ese mes, por ruta (para la herencia mensual).</summary>
    private async Task<Dictionary<int, List<(DateOnly Month, decimal Amount)>>> RouteQuotaListsAsync(DateOnly upTo, CancellationToken ct)
        => (await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive && q.SalesRouteId != null && q.Month <= upTo)
                .Select(q => new { q.SalesRouteId, q.Month, q.Amount }).ToListAsync(ct))
            .GroupBy(q => q.SalesRouteId!.Value).ToDictionary(g => g.Key, g => g.Select(q => (q.Month, q.Amount)).ToList());

    /// <summary>
    /// Las rutas con cuántos representantes activos tienen y, Lote 244, su titular vigente (y si sigue siendo persona activa), cuántos apoyos vigentes, cuántos clientes
    /// activos (ruta propia o la del representante) y su cuota de ruta vigente del mes en curso.
    /// </summary>
    public async Task<IReadOnlyList<SalesRouteDto>> ListRoutesAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.Set<SalesRep>().AsNoTracking().Where(r => r.IsActive && r.SalesRouteId != null).GroupBy(r => r.SalesRouteId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var people = await CurrentPeopleByRouteAsync(ct);
        var primaryIds = people.Values.SelectMany(l => l.Where(a => a.IsPrimary).Select(a => a.UserId)).Distinct().ToList();
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, TenantId, primaryIds, ct);
        var names = await AssigneeCheck.NamesAsync(db, primaryIds, ct);
        var clients = await ClientsByEffectiveRouteAsync(ct);
        var month = R.MonthStart(clock.Today);
        var quotas = (await RouteQuotaListsAsync(month, ct)).ToDictionary(kv => kv.Key, kv => R.EffectiveQuota(kv.Value, month));
        return (await db.Set<SalesRoute>().AsNoTracking().Where(r => includeInactive || r.IsActive).ToListAsync(ct))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r =>
            {
                var list = people.GetValueOrDefault(r.SalesRouteId);
                var primary = list?.FirstOrDefault(a => a.IsPrimary);
                return new SalesRouteDto(r.SalesRouteId, r.Name, r.SortOrder, r.IsActive, counts.GetValueOrDefault(r.SalesRouteId),
                    primary?.UserId, primary is null ? null : names.GetValueOrDefault(primary.UserId), primary is not null && active.Contains(primary.UserId),
                    primary is not null, list?.Count(a => !a.IsPrimary) ?? 0, clients.GetValueOrDefault(r.SalesRouteId), quotas.GetValueOrDefault(r.SalesRouteId));
            }).ToList();
    }

    private async Task<SalesRouteDto> RouteDtoAsync(int id, CancellationToken ct)
        => (await ListRoutesAsync(true, ct)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);

    public async Task<SalesRouteDto> CreateRouteAsync(SalesRouteRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (name, err) = NormalizeName(req.Name, R.RouteNameRequired);
        if (err is not null) throw new ValidationException("name", err);
        if (await db.Set<SalesRoute>().AnyAsync(r => r.IsActive && r.Name == name, ct)) throw new ConflictException(R.RouteNameTaken);
        var route = new SalesRoute { TenantId = TenantId, Name = name, SortOrder = req.SortOrder ?? 100 };
        db.Set<SalesRoute>().Add(route);
        await db.SaveGuardedAsync(R.RouteNameTaken, ct);
        return await RouteDtoAsync(route.SalesRouteId, ct);
    }

    public async Task<SalesRouteDto> UpdateRouteAsync(int id, SalesRouteRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var route = await db.Set<SalesRoute>().AsTracking().FirstOrDefaultAsync(r => r.SalesRouteId == id, ct) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
        if (req.Name is not null)
        {
            var (name, err) = NormalizeName(req.Name, R.RouteNameRequired);
            if (err is not null) throw new ValidationException("name", err);
            if (route.IsActive && await db.Set<SalesRoute>().AnyAsync(r => r.IsActive && r.Name == name && r.SalesRouteId != id, ct)) throw new ConflictException(R.RouteNameTaken);
            route.Name = name;
        }
        if (req.SortOrder is int so) route.SortOrder = so;
        await db.SaveGuardedAsync(R.RouteNameTaken, ct);
        return await RouteDtoAsync(id, ct);
    }

    /// <summary>
    /// Baja lógica / reactivación. Desactivar: 409 si tiene representantes activos o, Lote 244, clientes activos con esa ruta propia; si pasa, cierra HOY (día de la
    /// compañía) sus asignaciones vigentes en la misma transacción. Reactivar no las reabre.
    /// </summary>
    public async Task<SalesRouteDto> SetRouteActiveAsync(int id, bool active, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var route = await db.Set<SalesRoute>().AsTracking().FirstOrDefaultAsync(r => r.SalesRouteId == id, ct2) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
            if (!active && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.SalesRouteId == id, ct2)) throw new ConflictException(R.RouteHasReps);
            if (!active && await db.Clients.AnyAsync(c => c.IsActive && c.SalesRouteId == id, ct2)) throw new ConflictException(RR.RouteHasClients);
            if (active && !route.IsActive && await db.Set<SalesRoute>().AnyAsync(r => r.IsActive && r.Name == route.Name && r.SalesRouteId != id, ct2))
                throw new ConflictException(R.RouteNameTaken);
            route.IsActive = active;
            await db.SaveGuardedAsync(R.RouteNameTaken, ct2);
            if (active) return;
            var today = clock.Today;
            var open = await db.Set<SalesRouteAssignment>().AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.SalesRouteId == id && a.IsActive && a.ValidTo == null).Select(a => a.SalesRouteAssignmentId).ToListAsync(ct2);
            foreach (var assignmentId in open)
                await SalesRouteAssignmentOps.CloseAsync(db, TenantId, assignmentId, today, today, tenant.UserId, DateTime.UtcNow, ct2);
        }, ct);
        return await RouteDtoAsync(id, ct);
    }

    /// <summary>Lote 244 — las cuotas propias de una ruta, mes por mes.</summary>
    public async Task<SalesRouteQuotaHistoryDto> GetRouteQuotasAsync(int routeId, CancellationToken ct)
    {
        var route = await db.Set<SalesRoute>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRouteId == routeId, ct) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
        var quotas = await db.Set<SalesQuota>().AsNoTracking().Where(q => q.SalesRouteId == routeId && q.IsActive).OrderBy(q => q.Month).ToListAsync(ct);
        return new SalesRouteQuotaHistoryDto(route.SalesRouteId, route.Name, quotas.Select(q => new SalesQuotaPointDto(R.MonthKey(q.Month), q.Amount)).ToList());
    }

    // ================================================================ representantes

    public Task<IReadOnlyList<SalesRepDto>> ListRepsAsync(bool includeInactive, CancellationToken ct) => ListRepsAsync(includeInactive, false, ct);

    /// <summary>
    /// Representantes (withoutUser = solo los que no tienen usuario) con su ruta, usuario, clientes activos y cuota vigente; Lote 244: si su usuario es persona activa
    /// y si es el titular vigente de su ruta.
    /// </summary>
    public async Task<IReadOnlyList<SalesRepDto>> ListRepsAsync(bool includeInactive, bool withoutUser, CancellationToken ct)
    {
        var reps = await db.Set<SalesRep>().AsNoTracking().Where(r => (includeInactive || r.IsActive) && (!withoutUser || r.UserId == null)).ToListAsync(ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
        var clients = await db.Clients.AsNoTracking().Where(c => c.IsActive && c.SalesRepId != null).GroupBy(c => c.SalesRepId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var userIds = reps.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var activeUsers = await SalesSellerQueries.ActiveMemberIdsAsync(db, TenantId, userIds, ct);
        var primaries = (await db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == TenantId && a.IsActive && a.IsPrimary && a.ValidTo == null)
                .Select(a => new { a.SalesRouteId, a.UserId }).ToListAsync(ct))
            .Select(a => (a.SalesRouteId, a.UserId)).ToHashSet();
        var month = R.MonthStart(clock.Today);
        var quotas = (await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive && q.SalesRepId != null && q.Month <= month)
                .Select(q => new { q.SalesRepId, q.Month, q.Amount }).ToListAsync(ct))
            .GroupBy(q => q.SalesRepId!.Value).ToDictionary(g => g.Key, g => R.EffectiveQuota(g.Select(q => (q.Month, q.Amount)), month));
        return reps.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new SalesRepDto(r.SalesRepId, r.Name, r.SalesRouteId, r.SalesRouteId is int ro ? routes.GetValueOrDefault(ro) : null, r.UserId,
                r.UserId is int u ? users.GetValueOrDefault(u) : null, r.IsActive, clients.GetValueOrDefault(r.SalesRepId), quotas.GetValueOrDefault(r.SalesRepId),
                r.UserId is not null, r.UserId is int au && activeUsers.Contains(au),
                r.UserId is int pu && r.SalesRouteId is int pr && primaries.Contains((pr, pu))))
            .ToList();
    }

    private async Task<SalesRepDto> RepDtoAsync(int id, CancellationToken ct)
        => (await ListRepsAsync(true, ct)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException(R.RepNotFound);

    private async Task CheckRouteAsync(int routeId, CancellationToken ct)
    {
        var route = await db.Set<SalesRoute>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRouteId == routeId, ct) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
        if (!route.IsActive) throw new ValidationException("routeId", R.RouteInactive);
    }

    /// <summary>
    /// Lote 244 — el usuario de un representante: miembro de la compañía (400 UserNotMember), persona activa (400 UserSuspended) y que no sea el usuario de otro
    /// representante activo (409 RepUserTaken).
    /// </summary>
    private async Task CheckRepUserAsync(int userId, int? repId, CancellationToken ct)
    {
        if (!await db.UserTenants.AsNoTracking().AnyAsync(m => m.UserId == userId && m.TenantId == TenantId, ct)) throw new ValidationException("userId", RR.UserNotMember);
        if (!(await SalesSellerQueries.ActiveMemberIdsAsync(db, TenantId, new[] { userId }, ct)).Contains(userId))
            throw new ValidationException("userId", RR.UserSuspended);
        await CheckRepUserFreeAsync(userId, repId, ct);
    }

    /// <summary>Lote 244 — 409 si el usuario ya es el de otro representante activo (el primero que se creó).</summary>
    private async Task CheckRepUserFreeAsync(int userId, int? repId, CancellationToken ct)
    {
        var other = await db.Set<SalesRep>().AsNoTracking().Where(r => r.IsActive && r.UserId == userId && r.SalesRepId != repId)
            .OrderBy(r => r.SalesRepId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        if (other is not null) throw new ConflictException(RR.RepUserTaken(other));
    }

    /// <summary>
    /// Lote 244 — TITULAR AUTOMÁTICO: si el representante está activo, su usuario es persona activa y su ruta está activa y sin titular vigente, su usuario queda como
    /// titular desde hoy (día de la compañía), en la transacción de quien llama. Si la ruta ya tiene titular no se hace nada (el administrador decide).
    /// </summary>
    private async Task<bool> AutoPrimaryAsync(SalesRep rep, CancellationToken ct)
    {
        if (!rep.IsActive || rep.UserId is not int userId || rep.SalesRouteId is not int routeId) return false;
        if (!(await SalesSellerQueries.ActiveMemberIdsAsync(db, TenantId, new[] { userId }, ct)).Contains(userId)) return false;
        return await SalesRouteAssignmentOps.OpenPrimaryIfVacantAsync(db, TenantId, routeId, userId, clock.Today, tenant.UserId, DateTime.UtcNow, ct);
    }

    /// <summary>Alta: nombre, ruta (opcional) y usuario (OBLIGATORIO desde el Lote 244). Puede quedar como titular automático de su ruta (BecamePrimary).</summary>
    public async Task<SalesRepDto> CreateRepAsync(SalesRepRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (name, err) = NormalizeName(req.Name, R.RepNameRequired);
        if (err is not null) throw new ValidationException("name", err);
        if (req.UserId is not int userId) throw new ValidationException("userId", RR.RepUserRequired);
        var (id, became) = await db.RunInTransactionAsync(async ct2 =>
        {
            if (req.RouteId is int rid) await CheckRouteAsync(rid, ct2);
            await CheckRepUserAsync(userId, null, ct2);
            if (await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == name, ct2)) throw new ConflictException(R.RepNameTaken);
            var rep = new SalesRep { TenantId = TenantId, Name = name, SalesRouteId = req.RouteId, UserId = userId };
            db.Set<SalesRep>().Add(rep);
            await db.SaveGuardedAsync(R.RepNameTaken, ct2);
            return (rep.SalesRepId, await AutoPrimaryAsync(rep, ct2));
        }, ct);
        return (await RepDtoAsync(id, ct)) with { BecamePrimary = became };
    }

    /// <summary>
    /// Cambio: el usuario no se puede quitar (ClearUser = 400); cambiarlo lo vuelve a validar. Un representante heredado sin usuario se puede renombrar o cambiar de ruta
    /// sin mandarlo. Si cambia el usuario o la ruta, puede quedar como titular automático (BecamePrimary).
    /// </summary>
    public async Task<SalesRepDto> UpdateRepAsync(int id, SalesRepRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        if (req.ClearUser) throw new ValidationException("userId", RR.RepUserRequired);
        var became = await db.RunInTransactionAsync(async ct2 =>
        {
            var rep = await db.Set<SalesRep>().AsTracking().FirstOrDefaultAsync(r => r.SalesRepId == id, ct2) ?? throw new NotFoundException(R.RepNotFound);
            if (req.Name is not null)
            {
                var (name, err) = NormalizeName(req.Name, R.RepNameRequired);
                if (err is not null) throw new ValidationException("name", err);
                if (rep.IsActive && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == name && r.SalesRepId != id, ct2)) throw new ConflictException(R.RepNameTaken);
                rep.Name = name;
            }
            var changed = false;
            if (req.ClearRoute)
            {
                changed = rep.SalesRouteId is not null;
                rep.SalesRouteId = null;
            }
            else if (req.RouteId is int rid)
            {
                await CheckRouteAsync(rid, ct2);
                changed = rep.SalesRouteId != rid;
                rep.SalesRouteId = rid;
            }
            if (req.UserId is int uid && uid != rep.UserId)
            {
                await CheckRepUserAsync(uid, rep.SalesRepId, ct2);
                rep.UserId = uid;
                changed = true;
            }
            await db.SaveGuardedAsync(R.RepNameTaken, ct2);
            return changed && await AutoPrimaryAsync(rep, ct2);
        }, ct);
        return (await RepDtoAsync(id, ct)) with { BecamePrimary = became };
    }

    /// <summary>
    /// Baja lógica / reactivación. Desactivar: 409 si tiene clientes activos. Reactivar: 409 si el nombre ya lo usa otro activo o, Lote 244, si su usuario ya es el de otro
    /// representante activo; al reactivarlo puede quedar como titular automático (BecamePrimary).
    /// </summary>
    public async Task<SalesRepDto> SetRepActiveAsync(int id, bool active, CancellationToken ct)
    {
        var became = await db.RunInTransactionAsync(async ct2 =>
        {
            var rep = await db.Set<SalesRep>().AsTracking().FirstOrDefaultAsync(r => r.SalesRepId == id, ct2) ?? throw new NotFoundException(R.RepNotFound);
            if (!active && await db.Clients.AnyAsync(c => c.IsActive && c.SalesRepId == id, ct2)) throw new ConflictException(R.RepHasClients);
            var reactivated = active && !rep.IsActive;
            if (reactivated && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == rep.Name && r.SalesRepId != id, ct2))
                throw new ConflictException(R.RepNameTaken);
            if (reactivated && rep.UserId is int userId) await CheckRepUserFreeAsync(userId, rep.SalesRepId, ct2);
            rep.IsActive = active;
            await db.SaveGuardedAsync(R.RepNameTaken, ct2);
            return reactivated && await AutoPrimaryAsync(rep, ct2);
        }, ct);
        return (await RepDtoAsync(id, ct)) with { BecamePrimary = became };
    }

    // ================================================================ cuotas

    private static DateOnly RequireMonth(string? raw) => R.ParseMonth(raw) ?? throw new ValidationException("month", R.MonthInvalid);

    public Task<SalesQuotaMonthDto> GetQuotasAsync(string? month, CancellationToken ct) => GetQuotasAsync(month, null, ct);

    /// <summary>
    /// Las cuotas de un mes: la propia de cada representante y la vigente (la del mes o la anterior más reciente). Lote 244: lo mismo por ruta (RouteRows) y el total de la
    /// clase que cuenta: la pedida en <paramref name="by"/> (REP | ROUTE; otro valor = 400) o, sin ella, la del ajuste QuotaBy.
    /// </summary>
    public async Task<SalesQuotaMonthDto> GetQuotasAsync(string? month, string? by, CancellationToken ct)
    {
        var m = string.IsNullOrWhiteSpace(month) ? R.MonthStart(clock.Today) : RequireMonth(month);
        var quotaBy = string.IsNullOrWhiteSpace(by) ? await QuotaByAsync(ct) : RR.ParseQuotaBy(by) ?? throw new ValidationException("by", RR.QuotaByInvalid);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToListAsync(ct);
        var routeList = await db.Set<SalesRoute>().AsNoTracking().ToListAsync(ct);
        var routes = routeList.ToDictionary(r => r.SalesRouteId, r => r.Name);
        var all = await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive && q.Month <= m)
            .Select(q => new { q.SalesRepId, q.SalesRouteId, q.Month, q.Amount }).ToListAsync(ct);
        var quotas = all.Where(q => q.SalesRepId != null).GroupBy(q => q.SalesRepId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.Month).ToList());
        var rows = reps.Where(r => r.IsActive || (quotas.TryGetValue(r.SalesRepId, out var q) && q[0].Month == m))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r =>
            {
                var list = quotas.GetValueOrDefault(r.SalesRepId);
                var effective = list?.FirstOrDefault();
                decimal? own = effective is not null && effective.Month == m ? effective.Amount : null;
                return new SalesQuotaRowDto(r.SalesRepId, r.Name, r.SalesRouteId is int ro ? routes.GetValueOrDefault(ro) : null, r.IsActive, own, effective?.Amount,
                    effective is null ? null : R.MonthKey(effective.Month));
            }).ToList();
        var routeQuotas = all.Where(q => q.SalesRouteId != null).GroupBy(q => q.SalesRouteId!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.Month).ToList());
        var routeRows = routeList.Where(r => r.IsActive || (routeQuotas.TryGetValue(r.SalesRouteId, out var q) && q[0].Month == m))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r =>
            {
                var effective = routeQuotas.GetValueOrDefault(r.SalesRouteId)?.FirstOrDefault();
                decimal? own = effective is not null && effective.Month == m ? effective.Amount : null;
                return new SalesRouteQuotaRowDto(r.SalesRouteId, r.Name, r.IsActive, own, effective?.Amount, effective is null ? null : R.MonthKey(effective.Month));
            }).ToList();
        var repTotal = rows.Sum(r => r.Effective ?? 0m);
        var routeTotal = routeRows.Sum(r => r.Effective ?? 0m);
        return new SalesQuotaMonthDto(R.MonthKey(m), rows, quotaBy == K.QuotaByRoute ? routeTotal : repTotal, routeRows, quotaBy, repTotal, routeTotal);
    }

    /// <summary>
    /// Edición en bloque de un mes: cada renglón pone la cuota propia del representante o, Lote 244, de la ruta (uno de los dos; si no, 400). Amount null = la quita:
    /// vuelve a valer la anterior.
    /// </summary>
    public async Task<SalesQuotaMonthDto> SetQuotasAsync(string? month, SalesQuotaBulkRequest req, CancellationToken ct)
    {
        if (req?.Items is null) throw new ValidationException("body", R.BodyRequired);
        var m = RequireMonth(month);
        if (req.Items.Count > R.MaxBulk) throw new ValidationException("items", R.TooMany);
        foreach (var i in req.Items)
        {
            if (i is null || (i.RepId is null) == (i.RouteId is null)) throw new ValidationException("items", RR.QuotaTargetInvalid);
            if (i.Amount is decimal a && (a < 0m || decimal.Round(a, 2) != a)) throw new ValidationException("items", R.QuotaInvalid);
        }
        var repIds = req.Items.Where(i => i.RepId is not null).Select(i => i.RepId!.Value).Distinct().ToList();
        var routeIds = req.Items.Where(i => i.RouteId is not null).Select(i => i.RouteId!.Value).Distinct().ToList();
        if (repIds.Count > 0 && await db.Set<SalesRep>().AsNoTracking().CountAsync(r => repIds.Contains(r.SalesRepId), ct) != repIds.Count)
            throw new NotFoundException(R.RepNotFound);
        if (routeIds.Count > 0 && await db.Set<SalesRoute>().AsNoTracking().CountAsync(r => routeIds.Contains(r.SalesRouteId), ct) != routeIds.Count)
            throw new NotFoundException(R.RouteNotFound, feminine: true);
        var byRep = repIds.Count == 0 ? new Dictionary<int, SalesQuota>()
            : await db.Set<SalesQuota>().AsTracking().Where(q => q.Month == m && q.SalesRepId != null && repIds.Contains(q.SalesRepId.Value)).ToDictionaryAsync(q => q.SalesRepId!.Value, ct);
        var byRoute = routeIds.Count == 0 ? new Dictionary<int, SalesQuota>()
            : await db.Set<SalesQuota>().AsTracking().Where(q => q.Month == m && q.SalesRouteId != null && routeIds.Contains(q.SalesRouteId.Value)).ToDictionaryAsync(q => q.SalesRouteId!.Value, ct);
        foreach (var item in req.Items.Where(i => i.RepId is not null).GroupBy(i => i.RepId!.Value).Select(g => g.Last()))
            Upsert(byRep.GetValueOrDefault(item.RepId!.Value), item.Amount, () => new SalesQuota { TenantId = TenantId, SalesRepId = item.RepId, Month = m });
        foreach (var item in req.Items.Where(i => i.RouteId is not null).GroupBy(i => i.RouteId!.Value).Select(g => g.Last()))
            Upsert(byRoute.GetValueOrDefault(item.RouteId!.Value), item.Amount, () => new SalesQuota { TenantId = TenantId, SalesRouteId = item.RouteId, Month = m });
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await GetQuotasAsync(R.MonthKey(m), ct);
    }

    /// <summary>Pone la cuota propia del mes (o la reactiva) o, con Amount null, la quita (IsActive 0).</summary>
    private void Upsert(SalesQuota? existing, decimal? amount, Func<SalesQuota> create)
    {
        if (existing is not null)
        {
            if (amount is decimal a) { existing.Amount = a; existing.IsActive = true; }
            else existing.IsActive = false;
            return;
        }
        if (amount is not decimal a2) return;
        var q = create();
        q.Amount = a2;
        db.Set<SalesQuota>().Add(q);
    }

    public async Task<SalesQuotaHistoryDto> GetRepQuotasAsync(int repId, CancellationToken ct)
    {
        var rep = await db.Set<SalesRep>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRepId == repId, ct) ?? throw new NotFoundException(R.RepNotFound);
        var quotas = await db.Set<SalesQuota>().AsNoTracking().Where(q => q.SalesRepId == repId && q.IsActive).OrderBy(q => q.Month).ToListAsync(ct);
        return new SalesQuotaHistoryDto(rep.SalesRepId, rep.Name, quotas.Select(q => new SalesQuotaPointDto(R.MonthKey(q.Month), q.Amount)).ToList());
    }

    // ================================================================ líneas y marcas

    public async Task<IReadOnlyList<ProductLineDto>> ListLinesAsync(bool includeInactive, CancellationToken ct)
    {
        var brands = (await db.Set<ProductLineBrand>().AsNoTracking().ToListAsync(ct)).GroupBy(b => b.ProductLineId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(b => b.Brand).OrderBy(b => b, StringComparer.CurrentCultureIgnoreCase).ToList());
        return (await db.Set<ProductLine>().AsNoTracking().Where(l => includeInactive || l.IsActive).ToListAsync(ct))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new ProductLineDto(l.ProductLineId, l.Name, l.SortOrder, l.IsActive, brands.GetValueOrDefault(l.ProductLineId) ?? Array.Empty<string>())).ToList();
    }

    private async Task<ProductLineDto> LineDtoAsync(int id, CancellationToken ct)
        => (await ListLinesAsync(true, ct)).FirstOrDefault(l => l.Id == id) ?? throw new NotFoundException(R.LineNotFound, feminine: true);

    public async Task<ProductLineDto> CreateLineAsync(ProductLineRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (name, err) = NormalizeName(req.Name, R.LineNameRequired);
        if (err is not null) throw new ValidationException("name", err);
        if (await db.Set<ProductLine>().AnyAsync(l => l.IsActive && l.Name == name, ct)) throw new ConflictException(R.LineNameTaken);
        var line = new ProductLine { TenantId = TenantId, Name = name, SortOrder = req.SortOrder ?? 100 };
        db.Set<ProductLine>().Add(line);
        await db.SaveGuardedAsync(R.LineNameTaken, ct);
        return await LineDtoAsync(line.ProductLineId, ct);
    }

    public async Task<ProductLineDto> UpdateLineAsync(int id, ProductLineRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var line = await db.Set<ProductLine>().AsTracking().FirstOrDefaultAsync(l => l.ProductLineId == id, ct) ?? throw new NotFoundException(R.LineNotFound, feminine: true);
        if (req.Name is not null)
        {
            var (name, err) = NormalizeName(req.Name, R.LineNameRequired);
            if (err is not null) throw new ValidationException("name", err);
            if (line.IsActive && await db.Set<ProductLine>().AnyAsync(l => l.IsActive && l.Name == name && l.ProductLineId != id, ct)) throw new ConflictException(R.LineNameTaken);
            line.Name = name;
        }
        if (req.SortOrder is int so) line.SortOrder = so;
        await db.SaveGuardedAsync(R.LineNameTaken, ct);
        return await LineDtoAsync(id, ct);
    }

    public async Task<ProductLineDto> SetLineActiveAsync(int id, bool active, CancellationToken ct)
    {
        var line = await db.Set<ProductLine>().AsTracking().FirstOrDefaultAsync(l => l.ProductLineId == id, ct) ?? throw new NotFoundException(R.LineNotFound, feminine: true);
        if (active && !line.IsActive && await db.Set<ProductLine>().AnyAsync(l => l.IsActive && l.Name == line.Name && l.ProductLineId != id, ct))
            throw new ConflictException(R.LineNameTaken);
        line.IsActive = active;
        await db.SaveGuardedAsync(R.LineNameTaken, ct);
        return await LineDtoAsync(id, ct);
    }

    /// <summary>Reemplaza las marcas de la línea. Una marca que ya está en otra línea da 409 (quítela de ahí primero); sin distinguir mayúsculas ni acentos.</summary>
    public async Task<ProductLineDto> SetLineBrandsAsync(int id, ProductLineBrandsRequest req, CancellationToken ct)
    {
        if (req?.Brands is null) throw new ValidationException("body", R.BodyRequired);
        if (req.Brands.Count > R.MaxBulk) throw new ValidationException("brands", R.TooMany);
        var line = await db.Set<ProductLine>().AsNoTracking().FirstOrDefaultAsync(l => l.ProductLineId == id, ct) ?? throw new NotFoundException(R.LineNotFound, feminine: true);
        if (!line.IsActive) throw new ValidationException("brands", R.LineInactive);
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in req.Brands)
        {
            var b = raw?.Trim();
            if (string.IsNullOrEmpty(b)) throw new ValidationException("brands", R.BrandRequired);
            if (b.Length > R.BrandMaxLength) throw new ValidationException("brands", R.BrandTooLong);
            wanted.TryAdd(R.NameKey(b), b);
        }
        var all = await db.Set<ProductLineBrand>().AsTracking().ToListAsync(ct);
        var lineNames = await db.Set<ProductLine>().AsNoTracking().ToDictionaryAsync(l => l.ProductLineId, l => l.Name, ct);
        foreach (var other in all.Where(b => b.ProductLineId != id && wanted.ContainsKey(R.NameKey(b.Brand))))
            throw new ConflictException(R.BrandTaken(other.Brand, lineNames.GetValueOrDefault(other.ProductLineId) ?? string.Empty));
        var mine = all.Where(b => b.ProductLineId == id).ToList();
        foreach (var b in mine.Where(b => !wanted.ContainsKey(R.NameKey(b.Brand)))) db.Set<ProductLineBrand>().Remove(b);
        var have = mine.Select(b => R.NameKey(b.Brand)).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, brand) in wanted.Where(kv => !have.Contains(kv.Key)))
            db.Set<ProductLineBrand>().Add(new ProductLineBrand { TenantId = TenantId, ProductLineId = id, Brand = brand });
        await db.SaveGuardedAsync(R.BrandTaken(string.Join(", ", wanted.Values), line.Name), ct);
        return await LineDtoAsync(id, ct);
    }

    /// <summary>Las marcas de los productos propios activos con su línea efectiva (la de la tabla o la propia marca) y cuántos productos tiene cada una.</summary>
    public async Task<IReadOnlyList<SalesBrandDto>> ListBrandsAsync(CancellationToken ct)
    {
        var lines = await db.Set<ProductLine>().AsNoTracking().Where(l => l.IsActive).ToDictionaryAsync(l => l.ProductLineId, l => l.Name, ct);
        var map = new Dictionary<string, (int Id, string Name)>(StringComparer.Ordinal);
        foreach (var b in await db.Set<ProductLineBrand>().AsNoTracking().ToListAsync(ct))
            if (lines.TryGetValue(b.ProductLineId, out var n)) map[R.NameKey(b.Brand)] = (b.ProductLineId, n);
        var brands = await db.Products.AsNoTracking().Where(p => p.IsActive && p.ClientId == null && p.Brand != null && p.Brand != "")
            .GroupBy(p => p.Brand!).Select(g => new { Brand = g.Key, N = g.Count() }).ToListAsync(ct);
        return brands.GroupBy(b => R.NameKey(b.Brand)).Select(g =>
            {
                var brand = g.OrderByDescending(b => b.N).First().Brand.Trim();
                var found = map.TryGetValue(g.Key, out var line);
                return new SalesBrandDto(brand, g.Sum(b => b.N), found ? line.Id : null, found ? line.Name : brand, found);
            })
            .OrderBy(b => b.HasLine).ThenBy(b => b.Brand, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ================================================================ clientes ↔ representante

    public Task<PagedResult<SalesClientRepDto>> ListClientsAsync(int? repId, bool unassigned, string? q, int skip, int take, CancellationToken ct)
        => ListClientsAsync(repId, unassigned, null, false, q, skip, take, ct);

    /// <summary>
    /// Clientes activos con su representante (repId filtra por representante; unassigned = los que no tienen). Lote 244: routeId filtra por la ruta EFECTIVA (la propia
    /// o, sin ella, la del representante) y noRoute = los que no tienen ninguna; cada fila trae la ruta propia, la efectiva y el vendedor preferido.
    /// </summary>
    public async Task<PagedResult<SalesClientRepDto>> ListClientsAsync(int? repId, bool unassigned, int? routeId, bool noRoute, string? q, int skip, int take,
        CancellationToken ct)
    {
        if (skip < 0) throw new ValidationException("skip", R.PageInvalid);
        if (take < 1 || take > R.MaxPageSize) throw new ValidationException("take", R.PageSizeInvalid);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToDictionaryAsync(x => x.SalesRepId, x => (x.Name, x.SalesRouteId), ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(x => x.SalesRouteId, x => x.Name, ct);
        var query = db.Clients.AsNoTracking().Where(c => c.IsActive);
        if (unassigned) query = query.Where(c => c.SalesRepId == null);
        else if (repId is int r) query = query.Where(c => c.SalesRepId == r);
        if (noRoute)
        {
            var routed = reps.Where(kv => kv.Value.SalesRouteId != null).Select(kv => kv.Key).ToList();
            query = query.Where(c => c.SalesRouteId == null && (c.SalesRepId == null || !routed.Contains(c.SalesRepId.Value)));
        }
        else if (routeId is int ro)
        {
            var ofRoute = reps.Where(kv => kv.Value.SalesRouteId == ro).Select(kv => kv.Key).ToList();
            query = query.Where(c => c.SalesRouteId == ro || (c.SalesRouteId == null && c.SalesRepId != null && ofRoute.Contains(c.SalesRepId.Value)));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.Name.Contains(s) || c.Code.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(c => c.Name).Skip(skip).Take(take)
            .Select(c => new { c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive, c.SalesRepId, c.SalesRouteId, c.PreferredSalesUserId }).ToListAsync(ct);
        var preferred = await AssigneeCheck.NamesAsync(db, page.Where(c => c.PreferredSalesUserId != null).Select(c => c.PreferredSalesUserId!.Value), ct);
        var items = page.Select(c =>
        {
            (string Name, int? SalesRouteId)? rep = c.SalesRepId is int id && reps.TryGetValue(id, out var x) ? x : null;
            var repRoute = rep?.SalesRouteId;
            var effective = c.SalesRouteId ?? repRoute;
            return new SalesClientRepDto(c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive, c.SalesRepId, rep?.Name,
                repRoute is int rr ? routes.GetValueOrDefault(rr) : null,
                c.SalesRouteId, c.SalesRouteId is int dr ? routes.GetValueOrDefault(dr) : null,
                effective, effective is int er ? routes.GetValueOrDefault(er) : null, c.SalesRouteId is null && repRoute is not null,
                c.PreferredSalesUserId, c.PreferredSalesUserId is int pu ? preferred.GetValueOrDefault(pu) : null);
        }).ToList();
        return new PagedResult<SalesClientRepDto>(items, total, skip, take);
    }

    private async Task<(int? Id, string? Name)> CheckRepForAssignAsync(int? repId, CancellationToken ct)
    {
        if (repId is not int id) return (null, null);
        var rep = await db.Set<SalesRep>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRepId == id, ct) ?? throw new NotFoundException(R.RepNotFound);
        if (!rep.IsActive) throw new ValidationException("repId", R.RepInactive);
        return (rep.SalesRepId, rep.Name);
    }

    /// <summary>Asigna (o quita, repId null) el representante de un cliente.</summary>
    public async Task<SalesClientRepBulkResultDto> AssignClientAsync(int clientId, SalesClientRepRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (id, name) = await CheckRepForAssignAsync(req.RepId, ct);
        var client = await db.Clients.AsTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, ct) ?? throw new NotFoundException(R.ClientNotFound);
        client.SalesRepId = id;
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return new SalesClientRepBulkResultDto(1, id, name);
    }

    /// <summary>Asigna (o quita) el representante de varios clientes a la vez (hasta 2000).</summary>
    public async Task<SalesClientRepBulkResultDto> AssignClientsAsync(SalesClientRepBulkRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        if (req.ClientIds is not { Count: > 0 }) throw new ValidationException("clientIds", R.ClientsRequired);
        if (req.ClientIds.Count > R.MaxBulk) throw new ValidationException("clientIds", R.TooMany);
        var (id, name) = await CheckRepForAssignAsync(req.RepId, ct);
        var ids = req.ClientIds.Distinct().ToList();
        var clients = await db.Clients.AsTracking().Where(c => ids.Contains(c.ClientId)).ToListAsync(ct);
        if (clients.Count != ids.Count) throw new NotFoundException(R.ClientNotFound);
        var n = 0;
        foreach (var c in clients.Where(c => c.SalesRepId != id)) { c.SalesRepId = id; n++; }
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return new SalesClientRepBulkResultDto(n, id, name);
    }

    /// <summary>
    /// Pasa los valores del campo personalizado «Representante» (sales_rep, el que dejó la importación de QuickBooks) a representantes y enlaza los clientes que aún no
    /// tienen representante. Idempotente (lo que ya existe o ya está enlazado no cambia); el campo personalizado se deja.
    /// </summary>
    public async Task<SalesRepMigrationDto> RepsFromCustomFieldAsync(CancellationToken ct)
    {
        var clientEntity = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.Client, ct);
        if (clientEntity is null) return new SalesRepMigrationDto(0, 0, 0);
        var defIds = await db.Set<CustomFieldDefinition>().AsNoTracking()
            .Where(d => d.EntityTypeLookupId == clientEntity && d.FieldKey == LegacyImportPlanner.RepFieldKey).Select(d => d.CustomFieldDefinitionId).ToListAsync(ct);
        if (defIds.Count == 0) return new SalesRepMigrationDto(0, 0, 0);
        var values = await db.Set<CustomFieldValue>().AsNoTracking().Where(v => defIds.Contains(v.CustomFieldDefinitionId) && v.ValueText != null && v.ValueText != "")
            .Select(v => new { v.EntityId, v.ValueText }).ToListAsync(ct);
        var reps = await db.Set<SalesRep>().AsTracking().Where(r => r.IsActive).ToListAsync(ct);
        var byName = reps.GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var name in values.Select(v => v.ValueText!.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (byName.ContainsKey(name)) continue;
            var rep = new SalesRep { TenantId = TenantId, Name = name.Length > R.NameMaxLength ? name[..R.NameMaxLength] : name };
            db.Set<SalesRep>().Add(rep);
            byName[name] = rep;
            created++;
        }
        await db.SaveChangesAsync(ct);
        var clientIds = values.Select(v => v.EntityId).Distinct().ToList();
        var clients = await db.Clients.AsTracking().Where(c => clientIds.Contains(c.ClientId)).ToListAsync(ct);
        var linked = 0;
        var already = 0;
        foreach (var c in clients)
        {
            if (c.SalesRepId is not null) { already++; continue; }
            var name = values.First(v => v.EntityId == c.ClientId).ValueText!.Trim();
            if (byName.TryGetValue(name, out var rep)) { c.SalesRepId = rep.SalesRepId; linked++; }
        }
        await db.SaveChangesAsync(ct);
        return new SalesRepMigrationDto(created, linked, already);
    }

    // ================================================================ enlace a mano de nombres del historial

    /// <summary>
    /// Enlaza un nombre crudo del historial (cliente o ítem) a un cliente o producto de Teikem: re-enlaza las líneas ya importadas con ese nombre (mismo nombre normalizado
    /// o mismo SKU normalizado) y guarda el enlace para las próximas importaciones. Un ítem enlazado pasa a ser PRODUCT si era OTHER.
    /// </summary>
    public async Task<SalesAliasResultDto> LinkAsync(SalesAliasRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var kind = req.Kind?.Trim().ToUpperInvariant();
        if (kind is not (C.AliasClient or C.AliasProduct)) throw new ValidationException("kind", R.AliasKindInvalid);
        if (string.IsNullOrWhiteSpace(req.Raw)) throw new ValidationException("raw", R.AliasRawRequired);
        var raw = req.Raw.Trim();
        int relinked;
        if (kind == C.AliasClient)
        {
            if (req.ClientId is not int clientId) throw new ValidationException("clientId", R.AliasTargetRequired);
            if (!await db.Clients.AnyAsync(c => c.ClientId == clientId, ct)) throw new NotFoundException(R.ClientNotFound);
            var key = R.NameKey(raw);
            await UpsertAliasAsync(kind, key, clientId, null, ct);
            var names = (await db.Set<SalesHistoryLine>().AsNoTracking().Select(l => l.ClientNameRaw).Distinct().ToListAsync(ct)).Where(n => R.NameKey(n) == key).ToList();
            relinked = await RelinkAsync(db.Set<SalesHistoryLine>().Where(l => names.Contains(l.ClientNameRaw)), l => l.ClientId = clientId,
                q => q.ExecuteUpdateAsync(s => s.SetProperty(l => l.ClientId, clientId), ct), ct);
            return new SalesAliasResultDto(kind, raw, relinked, clientId, null);
        }
        if (req.ProductId is not int productId) throw new ValidationException("productId", R.AliasTargetRequired);
        if (!await db.Products.AnyAsync(p => p.ProductId == productId && p.ClientId == null, ct)) throw new NotFoundException(R.ProductNotFound);
        var (sku, _) = QuickBooksDetailReportParser.SplitItem(raw);
        var skuKey = LegacyImportRules.SkuKey(sku);
        await UpsertAliasAsync(kind, skuKey, null, productId, ct);
        var skus = (await db.Set<SalesHistoryLine>().AsNoTracking().Select(l => l.ItemSku).Distinct().ToListAsync(ct)).Where(s => LegacyImportRules.SkuKey(s) == skuKey).ToList();
        relinked = await RelinkAsync(db.Set<SalesHistoryLine>().Where(l => skus.Contains(l.ItemSku) && (l.LineClass == C.ClassProduct || l.LineClass == C.ClassOther)),
            l => { l.ProductId = productId; l.LineClass = C.ClassProduct; },
            q => q.ExecuteUpdateAsync(s => s.SetProperty(l => l.ProductId, productId).SetProperty(l => l.LineClass, C.ClassProduct), ct), ct);
        var pskus = (await db.Set<PurchaseHistoryLine>().AsNoTracking().Select(l => l.ItemSku).Distinct().ToListAsync(ct)).Where(s => LegacyImportRules.SkuKey(s) == skuKey).ToList();
        relinked += await RelinkAsync(db.Set<PurchaseHistoryLine>().Where(l => pskus.Contains(l.ItemSku)), l => l.ProductId = productId,
            q => q.ExecuteUpdateAsync(s => s.SetProperty(l => l.ProductId, productId), ct), ct);
        return new SalesAliasResultDto(kind, raw, relinked, null, productId);
    }

    private async Task UpsertAliasAsync(string kind, string key, int? clientId, int? productId, CancellationToken ct)
    {
        var alias = await db.Set<SalesHistoryAlias>().AsTracking().FirstOrDefaultAsync(a => a.Kind == kind && a.RawKey == key, ct);
        if (alias is null)
            db.Set<SalesHistoryAlias>().Add(new SalesHistoryAlias { TenantId = TenantId, Kind = kind, RawKey = key, ClientId = clientId, ProductId = productId, CreatedBy = tenant.UserId });
        else { alias.ClientId = clientId; alias.ProductId = productId; }
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
    }

    /// <summary>Actualización masiva: ExecuteUpdate en SQL Server; en la base en memoria de las pruebas, carga y cambia fila por fila.</summary>
    private async Task<int> RelinkAsync<T>(IQueryable<T> query, Action<T> apply, Func<IQueryable<T>, Task<int>> bulk, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational()) return await bulk(query);
        var rows = await query.AsTracking().ToListAsync(ct);
        foreach (var r in rows) apply(r);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
