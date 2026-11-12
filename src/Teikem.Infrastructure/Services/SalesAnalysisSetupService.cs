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

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 241 — lo que se administra del análisis de ventas (sales.analysis.manage; leer con sales.analysis.view): ajustes (fecha de corte, días de entrega, nivel de
/// servicio), rutas, representantes, cuotas por mes, líneas y sus marcas, el representante de cada cliente, el paso del campo personalizado «Representante» a
/// representantes y el enlace a mano de nombres del historial sin emparejar. Todo bajo el filtro de tenant (el TenantId sale de la sesión); bajas lógicas (IsActive).
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

    public async Task<SalesAnalysisSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await db.Set<SalesAnalysisSetting>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new SalesAnalysisSetting();
        return new SalesAnalysisSettingsDto(s.CutoffDate, s.LeadTimeDays, s.ServiceLevelZ);
    }

    public async Task<SalesAnalysisSettingsDto> UpdateSettingsAsync(SalesAnalysisSettingsRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        if (req.LeadTimeDays is int lt && (lt < 1 || lt > R.MaxLeadTimeDays)) throw new ValidationException("leadTimeDays", R.LeadTimeInvalid);
        if (req.ServiceLevelZ is decimal z && (z <= 0m || z > R.MaxServiceLevelZ)) throw new ValidationException("serviceLevelZ", R.ServiceLevelInvalid);
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
        s.UpdatedAtUtc = DateTime.UtcNow;
        s.UpdatedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return new SalesAnalysisSettingsDto(s.CutoffDate, s.LeadTimeDays, s.ServiceLevelZ);
    }

    // ================================================================ rutas

    public async Task<IReadOnlyList<SalesRouteDto>> ListRoutesAsync(bool includeInactive, CancellationToken ct)
    {
        var counts = await db.Set<SalesRep>().AsNoTracking().Where(r => r.IsActive && r.SalesRouteId != null).GroupBy(r => r.SalesRouteId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        return (await db.Set<SalesRoute>().AsNoTracking().Where(r => includeInactive || r.IsActive).ToListAsync(ct))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new SalesRouteDto(r.SalesRouteId, r.Name, r.SortOrder, r.IsActive, counts.GetValueOrDefault(r.SalesRouteId))).ToList();
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

    public async Task<SalesRouteDto> SetRouteActiveAsync(int id, bool active, CancellationToken ct)
    {
        var route = await db.Set<SalesRoute>().AsTracking().FirstOrDefaultAsync(r => r.SalesRouteId == id, ct) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
        if (!active && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.SalesRouteId == id, ct)) throw new ConflictException(R.RouteHasReps);
        if (active && !route.IsActive && await db.Set<SalesRoute>().AnyAsync(r => r.IsActive && r.Name == route.Name && r.SalesRouteId != id, ct))
            throw new ConflictException(R.RouteNameTaken);
        route.IsActive = active;
        await db.SaveGuardedAsync(R.RouteNameTaken, ct);
        return await RouteDtoAsync(id, ct);
    }

    // ================================================================ representantes

    public async Task<IReadOnlyList<SalesRepDto>> ListRepsAsync(bool includeInactive, CancellationToken ct)
    {
        var reps = await db.Set<SalesRep>().AsNoTracking().Where(r => includeInactive || r.IsActive).ToListAsync(ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
        var clients = await db.Clients.AsNoTracking().Where(c => c.IsActive && c.SalesRepId != null).GroupBy(c => c.SalesRepId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var userIds = reps.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email ?? string.Empty, ct);
        var month = R.MonthStart(clock.Today);
        var quotas = (await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive && q.Month <= month).Select(q => new { q.SalesRepId, q.Month, q.Amount }).ToListAsync(ct))
            .GroupBy(q => q.SalesRepId).ToDictionary(g => g.Key, g => R.EffectiveQuota(g.Select(q => (q.Month, q.Amount)), month));
        return reps.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new SalesRepDto(r.SalesRepId, r.Name, r.SalesRouteId, r.SalesRouteId is int ro ? routes.GetValueOrDefault(ro) : null, r.UserId,
                r.UserId is int u ? users.GetValueOrDefault(u) : null, r.IsActive, clients.GetValueOrDefault(r.SalesRepId), quotas.GetValueOrDefault(r.SalesRepId)))
            .ToList();
    }

    private async Task<SalesRepDto> RepDtoAsync(int id, CancellationToken ct)
        => (await ListRepsAsync(true, ct)).FirstOrDefault(r => r.Id == id) ?? throw new NotFoundException(R.RepNotFound);

    private async Task CheckRouteAsync(int routeId, CancellationToken ct)
    {
        var route = await db.Set<SalesRoute>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRouteId == routeId, ct) ?? throw new NotFoundException(R.RouteNotFound, feminine: true);
        if (!route.IsActive) throw new ValidationException("routeId", R.RouteInactive);
    }

    private async Task CheckUserAsync(int userId, CancellationToken ct)
    {
        if (!await db.UserTenants.AsNoTracking().AnyAsync(u => u.UserId == userId, ct)) throw new ValidationException("userId", R.UserNotInCompany);
    }

    public async Task<SalesRepDto> CreateRepAsync(SalesRepRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var (name, err) = NormalizeName(req.Name, R.RepNameRequired);
        if (err is not null) throw new ValidationException("name", err);
        if (req.RouteId is int rid) await CheckRouteAsync(rid, ct);
        if (req.UserId is int uid) await CheckUserAsync(uid, ct);
        if (await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == name, ct)) throw new ConflictException(R.RepNameTaken);
        var rep = new SalesRep { TenantId = TenantId, Name = name, SalesRouteId = req.RouteId, UserId = req.UserId };
        db.Set<SalesRep>().Add(rep);
        await db.SaveGuardedAsync(R.RepNameTaken, ct);
        return await RepDtoAsync(rep.SalesRepId, ct);
    }

    public async Task<SalesRepDto> UpdateRepAsync(int id, SalesRepRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", R.BodyRequired);
        var rep = await db.Set<SalesRep>().AsTracking().FirstOrDefaultAsync(r => r.SalesRepId == id, ct) ?? throw new NotFoundException(R.RepNotFound);
        if (req.Name is not null)
        {
            var (name, err) = NormalizeName(req.Name, R.RepNameRequired);
            if (err is not null) throw new ValidationException("name", err);
            if (rep.IsActive && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == name && r.SalesRepId != id, ct)) throw new ConflictException(R.RepNameTaken);
            rep.Name = name;
        }
        if (req.ClearRoute) rep.SalesRouteId = null;
        else if (req.RouteId is int rid) { await CheckRouteAsync(rid, ct); rep.SalesRouteId = rid; }
        if (req.ClearUser) rep.UserId = null;
        else if (req.UserId is int uid) { await CheckUserAsync(uid, ct); rep.UserId = uid; }
        await db.SaveGuardedAsync(R.RepNameTaken, ct);
        return await RepDtoAsync(id, ct);
    }

    public async Task<SalesRepDto> SetRepActiveAsync(int id, bool active, CancellationToken ct)
    {
        var rep = await db.Set<SalesRep>().AsTracking().FirstOrDefaultAsync(r => r.SalesRepId == id, ct) ?? throw new NotFoundException(R.RepNotFound);
        if (!active && await db.Clients.AnyAsync(c => c.IsActive && c.SalesRepId == id, ct)) throw new ConflictException(R.RepHasClients);
        if (active && !rep.IsActive && await db.Set<SalesRep>().AnyAsync(r => r.IsActive && r.Name == rep.Name && r.SalesRepId != id, ct))
            throw new ConflictException(R.RepNameTaken);
        rep.IsActive = active;
        await db.SaveGuardedAsync(R.RepNameTaken, ct);
        return await RepDtoAsync(id, ct);
    }

    // ================================================================ cuotas

    private static DateOnly RequireMonth(string? raw) => R.ParseMonth(raw) ?? throw new ValidationException("month", R.MonthInvalid);

    /// <summary>Las cuotas de un mes: la propia de cada representante y la vigente (la del mes o la anterior más reciente).</summary>
    public async Task<SalesQuotaMonthDto> GetQuotasAsync(string? month, CancellationToken ct)
    {
        var m = string.IsNullOrWhiteSpace(month) ? R.MonthStart(clock.Today) : RequireMonth(month);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToListAsync(ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
        var quotas = (await db.Set<SalesQuota>().AsNoTracking().Where(q => q.IsActive && q.Month <= m).Select(q => new { q.SalesRepId, q.Month, q.Amount }).ToListAsync(ct))
            .GroupBy(q => q.SalesRepId).ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.Month).ToList());
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
        return new SalesQuotaMonthDto(R.MonthKey(m), rows, rows.Sum(r => r.Effective ?? 0m));
    }

    /// <summary>Edición en bloque de un mes: cada renglón pone la cuota propia del representante (Amount null = la quita: vuelve a valer la anterior).</summary>
    public async Task<SalesQuotaMonthDto> SetQuotasAsync(string? month, SalesQuotaBulkRequest req, CancellationToken ct)
    {
        if (req?.Items is null) throw new ValidationException("body", R.BodyRequired);
        var m = RequireMonth(month);
        if (req.Items.Count > R.MaxBulk) throw new ValidationException("items", R.TooMany);
        foreach (var i in req.Items)
            if (i.Amount is decimal a && (a < 0m || decimal.Round(a, 2) != a)) throw new ValidationException("items", R.QuotaInvalid);
        var repIds = req.Items.Select(i => i.RepId).Distinct().ToList();
        var known = await db.Set<SalesRep>().AsNoTracking().Where(r => repIds.Contains(r.SalesRepId)).Select(r => r.SalesRepId).ToListAsync(ct);
        if (known.Count != repIds.Count) throw new NotFoundException(R.RepNotFound);
        var existing = await db.Set<SalesQuota>().AsTracking().Where(q => q.Month == m && repIds.Contains(q.SalesRepId)).ToDictionaryAsync(q => q.SalesRepId, ct);
        foreach (var item in req.Items.GroupBy(i => i.RepId).Select(g => g.Last()))
        {
            if (existing.TryGetValue(item.RepId, out var q))
            {
                if (item.Amount is decimal a) { q.Amount = a; q.IsActive = true; }
                else q.IsActive = false;
            }
            else if (item.Amount is decimal a2)
                db.Set<SalesQuota>().Add(new SalesQuota { TenantId = TenantId, SalesRepId = item.RepId, Month = m, Amount = a2 });
        }
        await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        return await GetQuotasAsync(R.MonthKey(m), ct);
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

    public async Task<PagedResult<SalesClientRepDto>> ListClientsAsync(int? repId, bool unassigned, string? q, int skip, int take, CancellationToken ct)
    {
        if (skip < 0) throw new ValidationException("skip", R.PageInvalid);
        if (take < 1 || take > R.MaxPageSize) throw new ValidationException("take", R.PageSizeInvalid);
        var query = db.Clients.AsNoTracking().Where(c => c.IsActive);
        if (unassigned) query = query.Where(c => c.SalesRepId == null);
        else if (repId is int r) query = query.Where(c => c.SalesRepId == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(c => c.Name.Contains(s) || c.Code.Contains(s));
        }
        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(c => c.Name).Skip(skip).Take(take).Select(c => new { c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive, c.SalesRepId }).ToListAsync(ct);
        var reps = await db.Set<SalesRep>().AsNoTracking().ToDictionaryAsync(x => x.SalesRepId, x => (x.Name, x.SalesRouteId), ct);
        var routes = await db.Set<SalesRoute>().AsNoTracking().ToDictionaryAsync(x => x.SalesRouteId, x => x.Name, ct);
        var items = page.Select(c =>
        {
            (string Name, int? SalesRouteId)? rep = c.SalesRepId is int id && reps.TryGetValue(id, out var x) ? x : null;
            return new SalesClientRepDto(c.ClientId, c.PublicId, c.Code, c.Name, c.IsActive, c.SalesRepId, rep?.Name,
                rep?.SalesRouteId is int ro ? routes.GetValueOrDefault(ro) : null);
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
