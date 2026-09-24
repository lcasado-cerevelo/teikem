using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Módulos G/H/I: Vistas, Indicadores y Gráficos. Reglas compartidas:
///  - IsSystem: nadie edita ni elimina. Usuario: solo el dueño (OwnerUserId) edita/elimina.
///  - Visibilidad: TENANT / PRIVATE / SHARED (ReportShare/IndicatorShare/ChartShare por rol o usuario). De sistema = todos.
///  - Rango de fecha y "mostrar en Pulso" son preferencias POR USUARIO (UserAnalyticsPreference) con el valor de la
///    definición como default; cambiar el default de una definición ajena/de sistema exige `analytics.dates`.
/// </summary>
public sealed class AnalyticsService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IDataSourceRegistry registry, AnalyticsEngine engine, PermissionService permissions)
{
    // =====================================================================
    // Fuentes de datos
    // =====================================================================
    public async Task<IReadOnlyList<DataSourceDto>> GetDataSourcesAsync(CancellationToken ct)
    {
        var list = new List<DataSourceDto>();
        foreach (var s in registry.All) list.Add(await ToDtoAsync(s, ct));
        return list;
    }

    private async Task<DataSourceDto> ToDtoAsync(IDataSource s, CancellationToken ct)
    {
        var lang = tenant.Lang;
        var cf = new List<DataFieldDto>();
        if (s.EntityTypeCode is not null)
        {
            var defs = await db.CustomFieldDefinitions.AsNoTracking().Include(d => d.DataType)
                .Where(d => d.IsActive && d.EntityType!.InternalCode == s.EntityTypeCode).OrderBy(d => d.SortOrder).ToListAsync(ct);
            cf.AddRange(defs.Select(d => new DataFieldDto(AnalyticsEngine.CustomFieldPrefix + d.FieldKey, MultilingualText.Resolve(d.LabelJson, lang),
                d.DataType!.InternalCode switch { CustomFieldDataTypes.Number => "Number", CustomFieldDataTypes.Bool => "Bool", CustomFieldDataTypes.Date or CustomFieldDataTypes.DateTime => "Date", _ => "Text" }, false)));
        }
        return new DataSourceDto(s.Key, lang == "en" ? s.LabelEn : s.LabelEs, s.EntityTypeCode, s.DateField, s.DefaultBusinessModule,
            s.Fields.Select(f => new DataFieldDto(f.Key, lang == "en" ? f.LabelEn : f.LabelEs, f.Type.ToString(), f.IsMoney)).ToList(),
            s.Relations.Select(r => new DataRelationDto(r.Key, r.TargetSourceKey, lang == "en" ? r.LabelEn : r.LabelEs)).ToList(), cf);
    }

    // =====================================================================
    // Visibilidad / edición (regla común)
    // =====================================================================
    private bool CanEdit(bool isSystem, int? ownerUserId) => !isSystem && ownerUserId.HasValue && ownerUserId == tenant.UserId;

    private async Task<bool> CanChangeDateAsync(bool isSystem, int? ownerUserId, CancellationToken ct)
        => CanEdit(isSystem, ownerUserId) || await permissions.HasPermissionAsync(PermissionCatalog.AnalyticsDates, ct);

    private async Task<HashSet<int>> MyRoleIdsAsync(CancellationToken ct)
        => (await db.AppUserRoles.AsNoTracking().Where(ur => ur.UserId == tenant.UserId).Select(ur => ur.RoleId).ToListAsync(ct)).ToHashSet();

    private bool IsVisible(bool isSystem, int? ownerUserId, string visibility, IEnumerable<(int? UserId, int? RoleId)> shares, HashSet<int> myRoles)
    {
        if (isSystem || tenant.IsPlatformAdmin) return true;
        if (ownerUserId == tenant.UserId) return true;
        return visibility.ToUpperInvariant() switch
        {
            ReportVisibilities.Tenant => true,
            ReportVisibilities.Shared => shares.Any(s => (s.UserId.HasValue && s.UserId == tenant.UserId) || (s.RoleId.HasValue && myRoles.Contains(s.RoleId.Value))),
            _ => false,
        };
    }

    private static string ValidateDateRange(string? mode, DateOnly? from, DateOnly? to)
    {
        var m = (mode ?? DateRangeModes.Last7).ToUpperInvariant();
        if (m is not (DateRangeModes.Last7 or DateRangeModes.Last30 or DateRangeModes.ThisMonth or DateRangeModes.Custom or DateRangeModes.All))
            throw new ValidationException("dateRangeMode", "Modo de rango inválido.");
        if (m == DateRangeModes.Custom && (from is null || to is null)) throw new ValidationException("dateRangeMode", "El rango personalizado necesita Desde y Hasta.");
        if (from is not null && to is not null && from > to) throw new ValidationException("dateFrom", "Desde no puede ser mayor que Hasta.");
        return m;
    }

    private async Task<string?> UserNameAsync(int? userId, CancellationToken ct)
        => userId is null ? null : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName ?? u.Email).FirstOrDefaultAsync(ct);

    // =====================================================================
    // G — Vistas / informes
    // =====================================================================
    public async Task<IReadOnlyList<ReportDto>> GetReportsAsync(string? baseEntityType, CancellationToken ct)
    {
        var q = db.ReportDefinitions.AsNoTracking().Include(r => r.BaseEntityType).Include(r => r.Visibility).Include(r => r.Shares).Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(baseEntityType)) q = q.Where(r => r.BaseEntityType!.InternalCode == baseEntityType);
        var list = await q.OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name).ToListAsync(ct);
        var myRoles = await MyRoleIdsAsync(ct);
        var result = new List<ReportDto>();
        foreach (var r in list.Where(r => IsVisible(r.IsSystem, r.OwnerUserId, r.Visibility!.InternalCode, r.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)))
            result.Add(await ToDtoAsync(r, ct));
        return result;
    }

    public async Task<ReportDto> GetReportAsync(int id, CancellationToken ct) => await ToDtoAsync(await LoadReportAsync(id, ct), ct);

    public async Task<ReportDto> CreateReportAsync(string baseEntityType, ReportUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(baseEntityType);
        var baseId = await lookups.TryGetIdAsync(LookupDomains.EntityType, source.Key, ct)
                     ?? throw new ValidationException("baseEntityType", $"La fuente '{source.Key}' no está registrada como EntityType.");
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.ReportDefinitions.AnyAsync(r => r.BaseEntityTypeLookupId == baseId && r.Name == req.Name.Trim() && r.IsActive, ct))
            throw new ConflictException($"Ya existe una vista '{req.Name}' para {source.Key}.");
        ValidateReportSpec(source, req);

        var r = new ReportDefinition
        {
            TenantId = tenantId, BaseEntityTypeLookupId = baseId, Name = req.Name.Trim(),
            OwnerUserId = tenant.UserId, IsSystem = false,
            VisibilityLookupId = await lookups.GetIdAsync(LookupDomains.ReportVisibility, req.Visibility ?? ReportVisibilities.Private, ct),
        };
        Apply(r, req);
        await ApplySharesAsync(r.Shares, req.Shares, (u, ro, e) => new ReportShare { UserId = u, RoleId = ro, CanEdit = e }, ct);
        db.ReportDefinitions.Add(r);
        await db.SaveChangesAsync(ct);
        return await GetReportAsync(r.ReportDefinitionId, ct);
    }

    public async Task<ReportDto> UpdateReportAsync(int id, ReportUpsertRequest req, CancellationToken ct)
    {
        var r = await LoadReportAsync(id, ct, track: true);
        EnsureEditable(r.IsSystem, r.OwnerUserId, r.Shares.Any(s => s.CanEdit && s.UserId == tenant.UserId));
        var source = registry.Get(r.BaseEntityType!.InternalCode);
        ValidateReportSpec(source, req);
        if (!string.IsNullOrWhiteSpace(req.Name)) r.Name = req.Name.Trim();
        if (req.Visibility is not null) r.VisibilityLookupId = await lookups.GetIdAsync(LookupDomains.ReportVisibility, req.Visibility, ct);
        Apply(r, req);
        if (req.Shares is not null)
        {
            db.ReportShares.RemoveRange(r.Shares);
            r.Shares.Clear();
            await ApplySharesAsync(r.Shares, req.Shares, (u, ro, e) => new ReportShare { UserId = u, RoleId = ro, CanEdit = e }, ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetReportAsync(id, ct);
    }

    public async Task DeleteReportAsync(int id, CancellationToken ct)
    {
        var r = await LoadReportAsync(id, ct, track: true);
        EnsureEditable(r.IsSystem, r.OwnerUserId, false);
        r.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReportRunResultDto> RunReportAsync(int id, ReportRunRequest run, CancellationToken ct)
    {
        var r = await LoadReportAsync(id, ct);
        var source = registry.Get(r.BaseEntityType!.InternalCode);
        var (from, to) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(run.DateRangeMode ?? DateRangeModes.All, run.DateFrom, run.DateTo);
        var (by, aggs, totals) = AnalyticsEngine.ParseGroup(r.GroupJson);
        var spec = new ReportSpec
        {
            SourceKey = source.Key, Secondary = AnalyticsEngine.ParseStringList(r.SecondaryJson), Columns = AnalyticsEngine.ParseStringList(r.ColumnsJson),
            FilterJson = r.FilterJson, GroupBy = by, Aggregates = aggs, ShowTotals = totals, Sort = AnalyticsEngine.ParseSort(r.SortJson),
            FromUtc = from, ToUtc = to, Skip = run.Skip ?? 0, Take = run.Take ?? 500,
        };
        var res = await engine.RunReportAsync(spec, ct);
        return new ReportRunResultDto(res.Columns, res.Rows.Select(x => (IDictionary<string, object?>)x).ToList(), res.Totals, res.TotalCount);
    }

    /// <summary>Ejecuta una especificación ad hoc (vista previa del constructor) sin guardarla.</summary>
    public async Task<ReportRunResultDto> PreviewReportAsync(string baseEntityType, ReportUpsertRequest req, ReportRunRequest run, CancellationToken ct)
    {
        var source = registry.Get(baseEntityType);
        ValidateReportSpec(source, req);
        var (from, to) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(run.DateRangeMode ?? DateRangeModes.All, run.DateFrom, run.DateTo);
        var (by, aggs, totals) = AnalyticsEngine.ParseGroup(req.GroupJson);
        var res = await engine.RunReportAsync(new ReportSpec
        {
            SourceKey = source.Key, Secondary = req.Secondary?.ToList() ?? new List<string>(), Columns = req.Columns.ToList(), FilterJson = req.FilterJson,
            GroupBy = by, Aggregates = aggs, ShowTotals = totals, Sort = AnalyticsEngine.ParseSort(req.SortJson), FromUtc = from, ToUtc = to, Skip = 0, Take = run.Take ?? 50,
        }, ct);
        return new ReportRunResultDto(res.Columns, res.Rows.Select(x => (IDictionary<string, object?>)x).ToList(), res.Totals, res.TotalCount);
    }

    private static void ValidateReportSpec(IDataSource source, ReportUpsertRequest req)
    {
        var (by, _, _) = AnalyticsEngine.ParseGroup(req.GroupJson);
        if ((req.Columns is null || req.Columns.Count == 0) && by.Count == 0)
            throw new ValidationException("columns", "Una vista necesita al menos una columna o una agrupación.");
        foreach (var sec in req.Secondary ?? new List<string>())
            if (!source.Relations.Any(r => r.Key.Equals(sec, StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException("secondary", $"La fuente {source.Key} no se combina con '{sec}'.");
        if (!string.IsNullOrWhiteSpace(req.FilterJson))
        {
            try { Dsl.RuleEvaluator.CompileFilter(req.FilterJson); }
            catch (JsonException) { throw new ValidationException("filterJson", "FilterJson no es JSON válido."); }
            catch (InvalidOperationException ex) { throw new ValidationException("filterJson", ex.Message); }
        }
    }

    private static void Apply(ReportDefinition r, ReportUpsertRequest req)
    {
        if (req.Descriptions is not null) r.DescriptionJson = req.Descriptions.Count == 0 ? null : MultilingualText.Serialize(req.Descriptions);
        r.ColumnsJson = JsonSerializer.Serialize(req.Columns ?? new List<string>());
        r.SecondaryJson = req.Secondary is { Count: > 0 } ? JsonSerializer.Serialize(req.Secondary) : null;
        r.FilterJson = req.FilterJson; r.SortJson = req.SortJson; r.GroupJson = req.GroupJson;
        r.ScheduleCron = req.ScheduleCron; r.DeliveryEmails = req.DeliveryEmails;
    }

    private async Task<ReportDefinition> LoadReportAsync(int id, CancellationToken ct, bool track = false)
    {
        var q = db.ReportDefinitions.Include(r => r.BaseEntityType).Include(r => r.Visibility).Include(r => r.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var r = await q.FirstOrDefaultAsync(x => x.ReportDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Vista", id);
        var myRoles = await MyRoleIdsAsync(ct);
        if (!IsVisible(r.IsSystem, r.OwnerUserId, r.Visibility!.InternalCode, r.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) throw new NotFoundException("Vista", id);
        return r;
    }

    private async Task<ReportDto> ToDtoAsync(ReportDefinition r, CancellationToken ct)
    {
        var chartType = r.ChartTypeLookupId is null ? null : (await lookups.GetAsync(r.ChartTypeLookupId.Value, ct))?.InternalCode;
        return new ReportDto(r.ReportDefinitionId, r.PublicId, r.BaseEntityType?.InternalCode ?? "", r.Name, MultilingualText.Resolve(r.DescriptionJson, tenant.Lang),
            MultilingualText.Parse(r.DescriptionJson), r.Visibility?.InternalCode ?? "", r.OwnerUserId, await UserNameAsync(r.OwnerUserId, ct), r.IsSystem,
            CanEdit(r.IsSystem, r.OwnerUserId) || r.Shares.Any(s => s.CanEdit && s.UserId == tenant.UserId),
            AnalyticsEngine.ParseStringList(r.SecondaryJson), AnalyticsEngine.ParseStringList(r.ColumnsJson), r.FilterJson, r.SortJson, r.GroupJson,
            chartType, r.ScheduleCron, r.DeliveryEmails, r.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, s.CanEdit)).ToList(), r.IsActive);
    }

    private void EnsureEditable(bool isSystem, int? ownerUserId, bool sharedWithEdit)
    {
        if (isSystem) throw new ForbiddenException("Los elementos por default de la plataforma no se editan ni se eliminan.");
        if (!CanEdit(isSystem, ownerUserId) && !sharedWithEdit && !tenant.IsPlatformAdmin) throw new ForbiddenException("Solo el dueño puede editar o eliminar este elemento.");
    }

    private async Task ApplySharesAsync<TShare>(ICollection<TShare> target, IList<ShareDto>? shares, Func<int?, int?, bool, TShare> factory, CancellationToken ct)
    {
        if (shares is null) return;
        var memberIds = (await db.UserTenants.AsNoTracking().Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
        var roleIds = (await db.AppRoles.AsNoTracking().Where(r => r.TenantId == tenant.TenantId).Select(r => r.RoleId).ToListAsync(ct)).ToHashSet();
        foreach (var s in shares)
        {
            if (s.UserId is null && s.RoleId is null) continue;
            if (s.UserId is int u && !memberIds.Contains(u)) throw new ValidationException("shares", $"El usuario {u} no pertenece a esta compañía.");
            if (s.RoleId is int ro && !roleIds.Contains(ro)) throw new ValidationException("shares", $"El rol {ro} no pertenece a esta compañía.");
            target.Add(factory(s.UserId, s.RoleId, s.CanEdit));
        }
    }

    // =====================================================================
    // Preferencias por usuario (Pulso / rango)
    // =====================================================================
    private async Task<UserAnalyticsPreference?> PrefAsync(int? indicatorId, int? chartId, bool track, CancellationToken ct)
    {
        var q = db.UserAnalyticsPreferences.Where(p => p.UserId == tenant.UserId && p.IndicatorDefinitionId == indicatorId && p.ChartDefinitionId == chartId);
        return track ? await q.FirstOrDefaultAsync(ct) : await q.AsNoTracking().FirstOrDefaultAsync(ct);
    }

    private async Task<UserAnalyticsPreference> PrefOrCreateAsync(int? indicatorId, int? chartId, CancellationToken ct)
    {
        var p = await PrefAsync(indicatorId, chartId, track: true, ct);
        if (p is null)
        {
            p = new UserAnalyticsPreference { TenantId = ((TenantContext)tenant).RequireTenantId(), UserId = ((TenantContext)tenant).RequireUserId(), IndicatorDefinitionId = indicatorId, ChartDefinitionId = chartId };
            db.UserAnalyticsPreferences.Add(p);
        }
        p.UpdatedAtUtc = DateTime.UtcNow;
        return p;
    }

    private async Task<(string? Mode, DateOnly? From, DateOnly? To, bool Pulse)> EffectiveAsync(AnalyticsDefinitionBase d, UserAnalyticsPreference? p, CancellationToken ct)
    {
        var defMode = d.DateRangeModeLookupId is null ? null : (await lookups.GetAsync(d.DateRangeModeLookupId.Value, ct))?.InternalCode;
        var prefMode = p?.DateRangeModeLookupId is null ? null : (await lookups.GetAsync(p.DateRangeModeLookupId.Value, ct))?.InternalCode;
        return (prefMode ?? defMode, prefMode is null ? d.DateFrom : p!.DateFrom, prefMode is null ? d.DateTo : p!.DateTo, p?.ShowInPulse ?? d.ShowInPulse);
    }

    // =====================================================================
    // H — Indicadores
    // =====================================================================
    public async Task<IReadOnlyList<AnalyticsDefinitionDto>> GetIndicatorsAsync(CancellationToken ct)
    {
        var list = await db.IndicatorDefinitions.AsNoTracking().Include(i => i.Shares).Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
        var myRoles = await MyRoleIdsAsync(ct);
        var result = new List<AnalyticsDefinitionDto>();
        foreach (var i in list)
        {
            var vis = (await lookups.GetAsync(i.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Private;
            if (!IsVisible(i.IsSystem, i.OwnerUserId, vis, i.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) continue;
            result.Add(await ToDtoAsync(i, i.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), null, null, ct));
        }
        return result;
    }

    public async Task<AnalyticsDefinitionDto> GetIndicatorAsync(int id, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct);
        return await ToDtoAsync(i, i.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), null, null, ct);
    }

    public async Task<AnalyticsDefinitionDto> CreateIndicatorAsync(IndicatorUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(req.DataSource);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.IndicatorDefinitions.AnyAsync(i => i.Name == req.Name.Trim() && i.IsActive, ct)) throw new ConflictException($"Ya existe el indicador '{req.Name}'.");
        var i = new IndicatorDefinition { TenantId = tenantId, OwnerUserId = tenant.UserId, IsSystem = false, Name = req.Name.Trim() };
        await ApplyAsync(i, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility ?? ReportVisibilities.Private, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        await ApplySharesAsync(i.Shares, req.Shares, (u, ro, _) => new IndicatorShare { UserId = u, RoleId = ro }, ct);
        db.IndicatorDefinitions.Add(i);
        await db.SaveChangesAsync(ct);
        return await GetIndicatorAsync(i.IndicatorDefinitionId, ct);
    }

    public async Task<AnalyticsDefinitionDto> UpdateIndicatorAsync(int id, IndicatorUpsertRequest req, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct, track: true);
        EnsureEditable(i.IsSystem, i.OwnerUserId, false);
        var source = registry.Get(req.DataSource);
        if (!string.IsNullOrWhiteSpace(req.Name)) i.Name = req.Name.Trim();
        await ApplyAsync(i, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        if (req.Shares is not null) { db.IndicatorShares.RemoveRange(i.Shares); i.Shares.Clear(); await ApplySharesAsync(i.Shares, req.Shares, (u, ro, _) => new IndicatorShare { UserId = u, RoleId = ro }, ct); }
        await db.SaveChangesAsync(ct);
        return await GetIndicatorAsync(id, ct);
    }

    public async Task DeleteIndicatorAsync(int id, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct, track: true);
        EnsureEditable(i.IsSystem, i.OwnerUserId, false);
        i.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Preferencia por usuario del rango (no edita la definición). Sin permiso especial: es cómo YO lo veo.</summary>
    public async Task<AnalyticsDefinitionDto> SetIndicatorMyDateRangeAsync(int id, DateRangeRequest req, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct);
        var mode = ValidateDateRange(req.DateRangeMode, req.DateFrom, req.DateTo);
        var p = await PrefOrCreateAsync(id, null, ct);
        p.DateRangeModeLookupId = await lookups.GetIdAsync(LookupDomains.DateRangeMode, mode, ct);
        p.DateFrom = mode == DateRangeModes.Custom ? req.DateFrom : null; p.DateTo = mode == DateRangeModes.Custom ? req.DateTo : null;
        await db.SaveChangesAsync(ct);
        return await GetIndicatorAsync(id, ct);
    }

    /// <summary>Rango por DEFECTO de la definición (para todos): dueño siempre; ajeno/sistema requiere analytics.dates.</summary>
    public async Task<AnalyticsDefinitionDto> SetIndicatorDefaultDateRangeAsync(int id, DateRangeRequest req, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct, track: true);
        if (!await CanChangeDateAsync(i.IsSystem, i.OwnerUserId, ct)) throw new ForbiddenException($"Cambiar el rango por defecto de un indicador ajeno o de sistema requiere '{PermissionCatalog.AnalyticsDates}'.");
        var mode = ValidateDateRange(req.DateRangeMode, req.DateFrom, req.DateTo);
        i.DateRangeModeLookupId = await lookups.GetIdAsync(LookupDomains.DateRangeMode, mode, ct);
        i.DateFrom = mode == DateRangeModes.Custom ? req.DateFrom : null; i.DateTo = mode == DateRangeModes.Custom ? req.DateTo : null;
        await db.SaveChangesAsync(ct);
        return await GetIndicatorAsync(id, ct);
    }

    public async Task<AnalyticsDefinitionDto> SetIndicatorMyPulseAsync(int id, bool show, CancellationToken ct)
    {
        await LoadIndicatorAsync(id, ct);
        var p = await PrefOrCreateAsync(id, null, ct);
        p.ShowInPulse = show;
        await db.SaveChangesAsync(ct);
        return await GetIndicatorAsync(id, ct);
    }

    public async Task<IndicatorValueDto> EvaluateIndicatorAsync(int id, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct);
        return await EvaluateAsync(i, ct);
    }

    private async Task<IndicatorValueDto> EvaluateAsync(IndicatorDefinition i, CancellationToken ct)
    {
        var source = registry.Get(i.DataSourceKey);
        var (mode, from, to, _) = await EffectiveAsync(i, await PrefAsync(i.IndicatorDefinitionId, null, false, ct), ct);
        var (fromUtc, toUtc) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(mode, from, to);
        var fn = (await lookups.GetAsync(i.AggregateFnLookupId, ct))?.InternalCode ?? AggregateFns.Count;
        var value = await engine.EvaluateIndicatorAsync(i.DataSourceKey, fn, i.FieldKey, i.FilterJson, fromUtc, toUtc, ct);
        return new IndicatorValueDto(i.IndicatorDefinitionId, i.Name, value, i.IsMoney, source.DateField is null ? null : mode ?? DateRangeModes.Last7, fromUtc, toUtc);
    }

    private async Task<IndicatorDefinition> LoadIndicatorAsync(int id, CancellationToken ct, bool track = false)
    {
        var q = db.IndicatorDefinitions.Include(i => i.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var i = await q.FirstOrDefaultAsync(x => x.IndicatorDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Indicador", id);
        var vis = (await lookups.GetAsync(i.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Private;
        if (!IsVisible(i.IsSystem, i.OwnerUserId, vis, i.Shares.Select(s => (s.UserId, s.RoleId)), await MyRoleIdsAsync(ct))) throw new NotFoundException("Indicador", id);
        return i;
    }

    // =====================================================================
    // I — Gráficos
    // =====================================================================
    public async Task<IReadOnlyList<AnalyticsDefinitionDto>> GetChartsAsync(CancellationToken ct)
    {
        var list = await db.ChartDefinitions.AsNoTracking().Include(c => c.Shares).Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var myRoles = await MyRoleIdsAsync(ct);
        var result = new List<AnalyticsDefinitionDto>();
        foreach (var c in list)
        {
            var vis = (await lookups.GetAsync(c.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Tenant;
            if (!IsVisible(c.IsSystem, c.OwnerUserId, vis, c.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) continue;
            result.Add(await ToDtoAsync(c, c.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), c.GroupByField, (await lookups.GetAsync(c.ChartTypeLookupId, ct))?.InternalCode, ct));
        }
        return result;
    }

    public async Task<AnalyticsDefinitionDto> GetChartAsync(int id, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct);
        return await ToDtoAsync(c, c.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), c.GroupByField, (await lookups.GetAsync(c.ChartTypeLookupId, ct))?.InternalCode, ct);
    }

    public async Task<AnalyticsDefinitionDto> CreateChartAsync(ChartUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(req.DataSource);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.ChartDefinitions.AnyAsync(c => c.Name == req.Name.Trim() && c.IsActive, ct)) throw new ConflictException($"Ya existe el gráfico '{req.Name}'.");
        var c = new ChartDefinition { TenantId = tenantId, OwnerUserId = tenant.UserId, IsSystem = false, Name = req.Name.Trim() };
        await ApplyChartAsync(c, source, req, ct);
        await ApplyAsync(c, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility ?? ReportVisibilities.Tenant, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        await ApplySharesAsync(c.Shares, req.Shares, (u, ro, _) => new ChartShare { UserId = u, RoleId = ro }, ct);
        db.ChartDefinitions.Add(c);
        await db.SaveChangesAsync(ct);
        return await GetChartAsync(c.ChartDefinitionId, ct);
    }

    public async Task<AnalyticsDefinitionDto> UpdateChartAsync(int id, ChartUpsertRequest req, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct, track: true);
        EnsureEditable(c.IsSystem, c.OwnerUserId, false);
        var source = registry.Get(req.DataSource);
        if (!string.IsNullOrWhiteSpace(req.Name)) c.Name = req.Name.Trim();
        await ApplyChartAsync(c, source, req, ct);
        await ApplyAsync(c, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        if (req.Shares is not null) { db.ChartShares.RemoveRange(c.Shares); c.Shares.Clear(); await ApplySharesAsync(c.Shares, req.Shares, (u, ro, _) => new ChartShare { UserId = u, RoleId = ro }, ct); }
        await db.SaveChangesAsync(ct);
        return await GetChartAsync(id, ct);
    }

    public async Task DeleteChartAsync(int id, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct, track: true);
        EnsureEditable(c.IsSystem, c.OwnerUserId, false);
        c.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<AnalyticsDefinitionDto> SetChartMyDateRangeAsync(int id, DateRangeRequest req, CancellationToken ct)
    {
        await LoadChartAsync(id, ct);
        var mode = ValidateDateRange(req.DateRangeMode, req.DateFrom, req.DateTo);
        var p = await PrefOrCreateAsync(null, id, ct);
        p.DateRangeModeLookupId = await lookups.GetIdAsync(LookupDomains.DateRangeMode, mode, ct);
        p.DateFrom = mode == DateRangeModes.Custom ? req.DateFrom : null; p.DateTo = mode == DateRangeModes.Custom ? req.DateTo : null;
        await db.SaveChangesAsync(ct);
        return await GetChartAsync(id, ct);
    }

    public async Task<AnalyticsDefinitionDto> SetChartDefaultDateRangeAsync(int id, DateRangeRequest req, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct, track: true);
        if (!await CanChangeDateAsync(c.IsSystem, c.OwnerUserId, ct)) throw new ForbiddenException($"Cambiar el rango por defecto de un gráfico ajeno o de sistema requiere '{PermissionCatalog.AnalyticsDates}'.");
        var mode = ValidateDateRange(req.DateRangeMode, req.DateFrom, req.DateTo);
        c.DateRangeModeLookupId = await lookups.GetIdAsync(LookupDomains.DateRangeMode, mode, ct);
        c.DateFrom = mode == DateRangeModes.Custom ? req.DateFrom : null; c.DateTo = mode == DateRangeModes.Custom ? req.DateTo : null;
        await db.SaveChangesAsync(ct);
        return await GetChartAsync(id, ct);
    }

    public async Task<AnalyticsDefinitionDto> SetChartMyPulseAsync(int id, bool show, CancellationToken ct)
    {
        await LoadChartAsync(id, ct);
        var p = await PrefOrCreateAsync(null, id, ct);
        p.ShowInPulse = show;
        await db.SaveChangesAsync(ct);
        return await GetChartAsync(id, ct);
    }

    public async Task<ChartDataDto> EvaluateChartAsync(int id, CancellationToken ct) => await EvaluateAsync(await LoadChartAsync(id, ct), ct);

    private async Task<ChartDataDto> EvaluateAsync(ChartDefinition c, CancellationToken ct)
    {
        var source = registry.Get(c.DataSourceKey);
        var (mode, from, to, _) = await EffectiveAsync(c, await PrefAsync(null, c.ChartDefinitionId, false, ct), ct);
        var (fromUtc, toUtc) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(mode, from, to);
        var fn = (await lookups.GetAsync(c.AggregateFnLookupId, ct))?.InternalCode ?? AggregateFns.Count;
        var type = (await lookups.GetAsync(c.ChartTypeLookupId, ct))?.InternalCode ?? ChartTypes.Bar;
        var points = await engine.EvaluateChartAsync(c.DataSourceKey, c.GroupByField, fn, c.FieldKey, c.FilterJson, type, fromUtc, toUtc, ct);
        return new ChartDataDto(c.ChartDefinitionId, c.Name, type, c.IsMoney, points, source.DateField is null ? null : mode ?? DateRangeModes.Last7, fromUtc, toUtc);
    }

    private async Task ApplyChartAsync(ChartDefinition c, IDataSource source, ChartUpsertRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.GroupByField)) throw new ValidationException("groupByField", "El campo de agrupación es obligatorio.");
        EnsureField(source, req.GroupByField, "groupByField");
        c.GroupByField = req.GroupByField;
        var type = (req.ChartType ?? SuggestChartType(source, req.GroupByField)).ToUpperInvariant();
        if (type is not (ChartTypes.Bar or ChartTypes.Donut or ChartTypes.Line)) throw new ValidationException("chartType", "Tipo de gráfico: BAR, DONUT o LINE.");
        c.ChartTypeLookupId = await lookups.GetIdAsync(LookupDomains.ReportChartType, type, ct);
    }

    /// <summary>Sugerencia: línea si agrupa por la fecha de actividad; barra en el resto (dona la decide el FE por cardinalidad).</summary>
    public static string SuggestChartType(IDataSource source, string groupBy)
        => source.DateField is not null && source.DateField.Equals(groupBy, StringComparison.OrdinalIgnoreCase) ? ChartTypes.Line : ChartTypes.Bar;

    private async Task<ChartDefinition> LoadChartAsync(int id, CancellationToken ct, bool track = false)
    {
        var q = db.ChartDefinitions.Include(c => c.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var c = await q.FirstOrDefaultAsync(x => x.ChartDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Gráfico", id);
        var vis = (await lookups.GetAsync(c.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Tenant;
        if (!IsVisible(c.IsSystem, c.OwnerUserId, vis, c.Shares.Select(s => (s.UserId, s.RoleId)), await MyRoleIdsAsync(ct))) throw new NotFoundException("Gráfico", id);
        return c;
    }

    // =====================================================================
    // Común H/I
    // =====================================================================
    private async Task ApplyAsync(AnalyticsDefinitionBase d, IDataSource source, IDictionary<string, string>? descriptions, string? field, string aggregateFn, string? filterJson,
        string? businessModule, bool? isMoney, string? visibility, string? dateRangeMode, DateOnly? from, DateOnly? to, bool? showInPulse, int? sortOrder, CancellationToken ct)
    {
        d.DataSourceKey = source.Key;
        if (descriptions is not null) d.DescriptionJson = descriptions.Count == 0 ? null : MultilingualText.Serialize(descriptions);
        var fn = (aggregateFn ?? AggregateFns.Count).ToUpperInvariant();
        if (fn != AggregateFns.Count && string.IsNullOrWhiteSpace(field)) throw new ValidationException("field", $"La función {fn} requiere un campo.");
        if (!string.IsNullOrWhiteSpace(field)) EnsureField(source, field, "field");
        d.FieldKey = string.IsNullOrWhiteSpace(field) ? null : field;
        d.AggregateFnLookupId = await lookups.GetIdAsync(LookupDomains.AggregateFn, fn, ct);
        if (!string.IsNullOrWhiteSpace(filterJson))
        {
            try { Dsl.RuleEvaluator.CompileFilter(filterJson); }
            catch (JsonException) { throw new ValidationException("filterJson", "FilterJson no es JSON válido."); }
            catch (InvalidOperationException ex) { throw new ValidationException("filterJson", ex.Message); }
        }
        d.FilterJson = filterJson;
        d.BusinessModuleLookupId = await lookups.GetIdAsync(LookupDomains.BusinessModule, businessModule ?? source.DefaultBusinessModule, ct);
        if (isMoney.HasValue) d.IsMoney = isMoney.Value;
        else if (field is not null) d.IsMoney = source.Fields.Any(f => f.Key.Equals(field, StringComparison.OrdinalIgnoreCase) && f.IsMoney);
        if (visibility is not null) d.VisibilityLookupId = await lookups.GetIdAsync(LookupDomains.ReportVisibility, visibility, ct);
        if (source.DateField is not null)
        {
            var mode = ValidateDateRange(dateRangeMode, from, to);
            d.DateRangeModeLookupId = await lookups.GetIdAsync(LookupDomains.DateRangeMode, mode, ct);
            d.DateFrom = mode == DateRangeModes.Custom ? from : null; d.DateTo = mode == DateRangeModes.Custom ? to : null;
        }
        else { d.DateRangeModeLookupId = null; d.DateFrom = null; d.DateTo = null; }
        if (showInPulse.HasValue) d.ShowInPulse = showInPulse.Value;
        if (sortOrder.HasValue) d.SortOrder = sortOrder.Value;
    }

    private static void EnsureField(IDataSource source, string field, string param)
    {
        if (field.StartsWith(AnalyticsEngine.CustomFieldPrefix, StringComparison.OrdinalIgnoreCase)) return;
        if (!source.Fields.Any(f => f.Key.Equals(field, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException(param, $"La fuente {source.Key} no tiene el campo '{field}'.");
    }

    private async Task<AnalyticsDefinitionDto> ToDtoAsync(AnalyticsDefinitionBase d, IReadOnlyList<ShareDto> shares, string? groupBy, string? chartType, CancellationToken ct)
    {
        var isIndicator = d is IndicatorDefinition;
        var id = isIndicator ? ((IndicatorDefinition)d).IndicatorDefinitionId : ((ChartDefinition)d).ChartDefinitionId;
        var pref = await PrefAsync(isIndicator ? id : null, isIndicator ? null : id, false, ct);
        var (mode, from, to, pulse) = await EffectiveAsync(d, pref, ct);
        registry.TryGet(d.DataSourceKey, out var source);
        return new AnalyticsDefinitionDto(id, d.PublicId, d.Name, MultilingualText.Resolve(d.DescriptionJson, tenant.Lang), d.DataSourceKey, d.FieldKey,
            (await lookups.GetAsync(d.AggregateFnLookupId, ct))?.InternalCode ?? "", d.FilterJson,
            (await lookups.GetAsync(d.BusinessModuleLookupId, ct))?.InternalCode ?? "", d.IsMoney, d.IsSystem, d.OwnerUserId, await UserNameAsync(d.OwnerUserId, ct),
            (await lookups.GetAsync(d.VisibilityLookupId, ct))?.InternalCode ?? "",
            d.DateRangeModeLookupId is null ? null : (await lookups.GetAsync(d.DateRangeModeLookupId.Value, ct))?.InternalCode, d.DateFrom, d.DateTo, d.ShowInPulse,
            CanEdit(d.IsSystem, d.OwnerUserId), await CanChangeDateAsync(d.IsSystem, d.OwnerUserId, ct), source?.DateField is not null,
            mode, from, to, pulse, groupBy, chartType, shares, d.SortOrder);
    }

    // =====================================================================
    // Pulso del día: indicadores y gráficos que ESTE usuario marcó (o default), y que puede ver
    // =====================================================================
    public async Task<PulseDto> GetPulseAsync(CancellationToken ct)
    {
        var indicators = new List<IndicatorValueDto>();
        foreach (var dto in await GetIndicatorsAsync(ct))
            if (dto.EffectiveShowInPulse) indicators.Add(await EvaluateAsync(await LoadIndicatorAsync(dto.Id, ct), ct));
        var charts = new List<ChartDataDto>();
        foreach (var dto in await GetChartsAsync(ct))
            if (dto.EffectiveShowInPulse) charts.Add(await EvaluateAsync(await LoadChartAsync(dto.Id, ct), ct));
        return new PulseDto(indicators, charts);
    }
}
