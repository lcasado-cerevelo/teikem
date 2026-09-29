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
///  - Lote F8a (regla de lectura, loteF8-plan.md §2.2): un indicador o gráfico se lista, se lee y entra al Pulso solo si,
///    además de la visibilidad, el usuario puede leer su fuente de datos (EntityType → PermissionCatalog.DataSourceReadPermission)
///    y su módulo de negocio está encendido (PulsePanels.TenantModulesFor). Lo que no puede leer responde 404.
///  - Lote F8a: Pulso del día por paneles (registro PulsePanels, un permiso pulse.* por panel) con orden y visibilidad en dos
///    niveles: compañía (PulsePanelSetting UserId NULL + SortOrder/ShowInPulse de la definición) y usuario
///    (PulsePanelSetting propio + UserAnalyticsPreference.PulseSortOrder/ShowInPulse).
/// </summary>
public sealed class AnalyticsService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IDataSourceRegistry registry, AnalyticsEngine engine, PermissionService permissions,
    ModuleService modules)
{
    // Mensajes exactos del plan (loteF8-plan.md, P1).
    public const string InvalidScopeMessage = "Alcance inválido: use mine o company.";
    public const string InvalidKindMessage = "Tipo inválido: use indicator o chart.";
    public static string UnknownPanelMessage(string? key) => $"Panel de Pulso desconocido: {key}.";
    public const string ScopeMine = "mine";
    public const string ScopeCompany = "company";
    public const string KindIndicator = "indicator";
    public const string KindChart = "chart";

    // =====================================================================
    // Fuentes de datos
    // =====================================================================
    public async Task<IReadOnlyList<DataSourceDto>> GetDataSourcesAsync(CancellationToken ct)
    {
        var access = await AccessAsync(ct);
        var list = new List<DataSourceDto>();
        foreach (var s in registry.All.Where(s => CanReadSource(s, access))) list.Add(await ToDtoAsync(s, ct));
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

    /// <summary>
    /// Igual que <see cref="CanEdit"/> (dueño, no de sistema), pero exige además que el dueño tenga HOY
    /// analytics.manage — la regla exacta que ya aplica el `[RequirePermission]` de PUT/DELETE en el controlador.
    /// Sin esto, el DTO diría `canEdit: true` a un dueño al que le quitaron el permiso, y el frontend mostraría
    /// Editar/Eliminar/el switch de compañía aunque el servidor respondiera 403 al usarlos.
    /// </summary>
    private async Task<bool> CanEditWithManageAsync(bool isSystem, int? ownerUserId, CancellationToken ct)
        => CanEdit(isSystem, ownerUserId) && await permissions.HasPermissionAsync(PermissionCatalog.AnalyticsManage, ct);

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

    // =====================================================================
    // Regla de lectura (Lote F8a, §2.2): fuente legible + módulo de negocio encendido
    // =====================================================================
    /// <summary>Permisos efectivos y módulos encendidos del usuario actual (el admin de plataforma tiene todos los permisos).</summary>
    private sealed record ReadAccess(IReadOnlySet<string> Permissions, IReadOnlySet<string> Modules, bool IsPlatformAdmin)
    {
        public bool Has(string code) => IsPlatformAdmin || Permissions.Contains(code);
        public bool ModuleOn(string key) => Modules.Contains(key);
    }

    private async Task<ReadAccess> AccessAsync(CancellationToken ct)
    {
        IReadOnlySet<string> none = new HashSet<string>();
        var perms = !tenant.IsPlatformAdmin && tenant.UserId is int u && tenant.TenantId is int t
            ? await permissions.GetEffectivePermissionsAsync(u, t, ct)
            : none;
        var mods = tenant.TenantId is int tid ? await modules.GetEnabledKeysAsync(tid, ct) : none;
        return new ReadAccess(perms, mods, tenant.IsPlatformAdmin);
    }

    /// <summary>
    /// ¿Puede el usuario leer la fuente? Su EntityType (o su clave, que es el código EntityType) se traduce al permiso de lectura
    /// con PermissionCatalog.DataSourceReadPermission; una fuente sin permiso registrado se trata como visible.
    /// </summary>
    private static bool CanReadSource(IDataSource source, ReadAccess access)
    {
        var perm = PermissionCatalog.DataSourceReadPermission(source.EntityTypeCode ?? source.Key);
        return perm is null || access.Has(perm);
    }

    private bool CanReadSource(string sourceKey, ReadAccess access)
    {
        if (registry.TryGet(sourceKey, out var source)) return CanReadSource(source, access);
        var perm = PermissionCatalog.DataSourceReadPermission(sourceKey);
        return perm is null || access.Has(perm);
    }

    /// <summary>Reglas 3 y 4 de §2.2 para una definición ya visible por visibilidad (privado/compartido/compañía).</summary>
    private async Task<bool> CanReadAsync(AnalyticsDefinitionBase d, ReadAccess access, CancellationToken ct)
    {
        if (!CanReadSource(d.DataSourceKey, access)) return false;
        var module = (await lookups.GetAsync(d.BusinessModuleLookupId, ct))?.InternalCode;
        return PulsePanels.IsBusinessModuleEnabled(module, access.ModuleOn);
    }

    private async Task EnsureSourceReadableAsync(IDataSource source, CancellationToken ct)
    {
        if (!CanReadSource(source, await AccessAsync(ct))) throw new NotFoundException("Fuente de datos", source.Key);
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
        var access = await AccessAsync(ct);
        var result = new List<ReportDto>();
        foreach (var r in list.Where(r => IsVisible(r.IsSystem, r.OwnerUserId, r.Visibility!.InternalCode, r.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)
                     && CanReadSource(r.BaseEntityType!.InternalCode, access)))
            result.Add(await ToDtoAsync(r, ct));
        return result;
    }

    public async Task<ReportDto> GetReportAsync(int id, CancellationToken ct) => await ToDtoAsync(await LoadReportAsync(id, ct), ct);

    public async Task<ReportDto> CreateReportAsync(string baseEntityType, ReportUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(baseEntityType);
        await EnsureSourceReadableAsync(source, ct);
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
        var r = await LoadReportAsync(id, ct, track: true, requireReadable: false);
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
        var r = await LoadReportAsync(id, ct, track: true, requireReadable: false);
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
        await EnsureSourceReadableAsync(source, ct);
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

    private async Task<ReportDefinition> LoadReportAsync(int id, CancellationToken ct, bool track = false, bool requireReadable = true)
    {
        var q = db.ReportDefinitions.Include(r => r.BaseEntityType).Include(r => r.Visibility).Include(r => r.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var r = await q.FirstOrDefaultAsync(x => x.ReportDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Vista", id);
        var myRoles = await MyRoleIdsAsync(ct);
        if (!IsVisible(r.IsSystem, r.OwnerUserId, r.Visibility!.InternalCode, r.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) throw new NotFoundException("Vista", id);
        if (requireReadable && !CanReadSource(r.BaseEntityType!.InternalCode, await AccessAsync(ct))) throw new NotFoundException("Vista", id);
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
        var result = new List<AnalyticsDefinitionDto>();
        foreach (var i in await ReadableIndicatorsAsync(await AccessAsync(ct), ct))
            result.Add(await IndicatorDtoAsync(i, ct));
        return result;
    }

    /// <summary>Indicadores activos que el usuario ve (visibilidad) y puede leer (fuente y módulo, §2.2), por SortOrder y nombre.</summary>
    private async Task<List<IndicatorDefinition>> ReadableIndicatorsAsync(ReadAccess access, CancellationToken ct)
    {
        var list = await db.IndicatorDefinitions.AsNoTracking().Include(i => i.Shares).Where(i => i.IsActive).OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToListAsync(ct);
        var myRoles = await MyRoleIdsAsync(ct);
        var result = new List<IndicatorDefinition>();
        foreach (var i in list)
        {
            var vis = (await lookups.GetAsync(i.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Private;
            if (!IsVisible(i.IsSystem, i.OwnerUserId, vis, i.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) continue;
            if (!await CanReadAsync(i, access, ct)) continue;
            result.Add(i);
        }
        return result;
    }

    public async Task<AnalyticsDefinitionDto> GetIndicatorAsync(int id, CancellationToken ct) => await IndicatorDtoAsync(await LoadIndicatorAsync(id, ct), ct);

    private Task<AnalyticsDefinitionDto> IndicatorDtoAsync(IndicatorDefinition i, CancellationToken ct)
        => ToDtoAsync(i, i.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), null, null, ct);

    public async Task<AnalyticsDefinitionDto> CreateIndicatorAsync(IndicatorUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(req.DataSource);
        await EnsureSourceReadableAsync(source, ct);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.IndicatorDefinitions.AnyAsync(i => i.Name == req.Name.Trim() && i.IsActive, ct)) throw new ConflictException($"Ya existe el indicador '{req.Name}'.");
        var i = new IndicatorDefinition { TenantId = tenantId, OwnerUserId = tenant.UserId, IsSystem = false, Name = req.Name.Trim() };
        await ApplyAsync(i, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility ?? ReportVisibilities.Private, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        await ApplySharesAsync(i.Shares, req.Shares, (u, ro, _) => new IndicatorShare { UserId = u, RoleId = ro }, ct);
        db.IndicatorDefinitions.Add(i);
        await db.SaveChangesAsync(ct);
        // Sin la regla de lectura: quien lo crea recibe su definición aunque el módulo de negocio elegido esté apagado.
        return await IndicatorDtoAsync(await LoadIndicatorAsync(i.IndicatorDefinitionId, ct, requireReadable: false), ct);
    }

    public async Task<AnalyticsDefinitionDto> UpdateIndicatorAsync(int id, IndicatorUpsertRequest req, CancellationToken ct)
    {
        // El dueño edita su definición aunque ya no pueda leer la fuente vieja; la nueva sí debe poder leerla.
        var i = await LoadIndicatorAsync(id, ct, track: true, requireReadable: false);
        EnsureEditable(i.IsSystem, i.OwnerUserId, false);
        var source = registry.Get(req.DataSource);
        await EnsureSourceReadableAsync(source, ct);
        if (!string.IsNullOrWhiteSpace(req.Name)) i.Name = req.Name.Trim();
        await ApplyAsync(i, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        if (req.Shares is not null) { db.IndicatorShares.RemoveRange(i.Shares); i.Shares.Clear(); await ApplySharesAsync(i.Shares, req.Shares, (u, ro, _) => new IndicatorShare { UserId = u, RoleId = ro }, ct); }
        await db.SaveChangesAsync(ct);
        return await IndicatorDtoAsync(await LoadIndicatorAsync(id, ct, requireReadable: false), ct);
    }

    public async Task DeleteIndicatorAsync(int id, CancellationToken ct)
    {
        var i = await LoadIndicatorAsync(id, ct, track: true, requireReadable: false);
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
        return await EvaluateAsync(i, await PrefAsync(i.IndicatorDefinitionId, null, false, ct), compute: true, ct);
    }

    /// <summary>
    /// Valor de un indicador con la preferencia del usuario (rango, orden y visibilidad en el Pulso). compute=false devuelve el
    /// elemento sin calcular (Value null): el Pulso no calcula lo que está oculto.
    /// </summary>
    private async Task<IndicatorValueDto> EvaluateAsync(IndicatorDefinition i, UserAnalyticsPreference? pref, bool compute, CancellationToken ct)
    {
        var source = registry.Get(i.DataSourceKey);
        var (mode, from, to, _) = await EffectiveAsync(i, pref, ct);
        var (fromUtc, toUtc) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(mode, from, to);
        decimal? value = null;
        if (compute)
        {
            var fn = (await lookups.GetAsync(i.AggregateFnLookupId, ct))?.InternalCode ?? AggregateFns.Count;
            value = await engine.EvaluateIndicatorAsync(i.DataSourceKey, fn, i.FieldKey, i.FilterJson, fromUtc, toUtc, ct);
        }
        var (visible, sort, origin) = PulsePanels.ResolveItem(pref?.PulseSortOrder, pref?.ShowInPulse, i.SortOrder, i.ShowInPulse);
        var module = (await lookups.GetAsync(i.BusinessModuleLookupId, ct))?.InternalCode ?? "";
        return new IndicatorValueDto(i.IndicatorDefinitionId, i.Name, value, i.IsMoney, source.DateField is null ? null : mode ?? DateRangeModes.Last7, fromUtc, toUtc,
            module, sort, visible, origin);
    }

    /// <summary>
    /// Indicador activo visible para el usuario; con requireReadable (por defecto) además legible por §2.2 (fuente y módulo).
    /// Lo que no ve o no puede leer responde 404 'Indicador' (no se revela).
    /// </summary>
    private async Task<IndicatorDefinition> LoadIndicatorAsync(int id, CancellationToken ct, bool track = false, bool requireReadable = true)
    {
        var q = db.IndicatorDefinitions.Include(i => i.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var i = await q.FirstOrDefaultAsync(x => x.IndicatorDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Indicador", id);
        var vis = (await lookups.GetAsync(i.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Private;
        if (!IsVisible(i.IsSystem, i.OwnerUserId, vis, i.Shares.Select(s => (s.UserId, s.RoleId)), await MyRoleIdsAsync(ct))) throw new NotFoundException("Indicador", id);
        if (requireReadable && !await CanReadAsync(i, await AccessAsync(ct), ct)) throw new NotFoundException("Indicador", id);
        return i;
    }

    // =====================================================================
    // I — Gráficos
    // =====================================================================
    public async Task<IReadOnlyList<AnalyticsDefinitionDto>> GetChartsAsync(CancellationToken ct)
    {
        var result = new List<AnalyticsDefinitionDto>();
        foreach (var c in await ReadableChartsAsync(await AccessAsync(ct), ct))
            result.Add(await ChartDtoAsync(c, ct));
        return result;
    }

    /// <summary>Gráficos activos que el usuario ve (visibilidad) y puede leer (fuente y módulo, §2.2), por SortOrder y nombre.</summary>
    private async Task<List<ChartDefinition>> ReadableChartsAsync(ReadAccess access, CancellationToken ct)
    {
        var list = await db.ChartDefinitions.AsNoTracking().Include(c => c.Shares).Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var myRoles = await MyRoleIdsAsync(ct);
        var result = new List<ChartDefinition>();
        foreach (var c in list)
        {
            var vis = (await lookups.GetAsync(c.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Tenant;
            if (!IsVisible(c.IsSystem, c.OwnerUserId, vis, c.Shares.Select(s => (s.UserId, s.RoleId)), myRoles)) continue;
            if (!await CanReadAsync(c, access, ct)) continue;
            result.Add(c);
        }
        return result;
    }

    public async Task<AnalyticsDefinitionDto> GetChartAsync(int id, CancellationToken ct) => await ChartDtoAsync(await LoadChartAsync(id, ct), ct);

    private async Task<AnalyticsDefinitionDto> ChartDtoAsync(ChartDefinition c, CancellationToken ct)
        => await ToDtoAsync(c, c.Shares.Select(s => new ShareDto(s.UserId, s.RoleId, false)).ToList(), c.GroupByField, (await lookups.GetAsync(c.ChartTypeLookupId, ct))?.InternalCode, ct);

    public async Task<AnalyticsDefinitionDto> CreateChartAsync(ChartUpsertRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var source = registry.Get(req.DataSource);
        await EnsureSourceReadableAsync(source, ct);
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        if (await db.ChartDefinitions.AnyAsync(c => c.Name == req.Name.Trim() && c.IsActive, ct)) throw new ConflictException($"Ya existe el gráfico '{req.Name}'.");
        var c = new ChartDefinition { TenantId = tenantId, OwnerUserId = tenant.UserId, IsSystem = false, Name = req.Name.Trim() };
        await ApplyChartAsync(c, source, req, ct);
        await ApplyAsync(c, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility ?? ReportVisibilities.Tenant, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        await ApplySharesAsync(c.Shares, req.Shares, (u, ro, _) => new ChartShare { UserId = u, RoleId = ro }, ct);
        db.ChartDefinitions.Add(c);
        await db.SaveChangesAsync(ct);
        return await ChartDtoAsync(await LoadChartAsync(c.ChartDefinitionId, ct, requireReadable: false), ct);
    }

    public async Task<AnalyticsDefinitionDto> UpdateChartAsync(int id, ChartUpsertRequest req, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct, track: true, requireReadable: false);
        EnsureEditable(c.IsSystem, c.OwnerUserId, false);
        var source = registry.Get(req.DataSource);
        await EnsureSourceReadableAsync(source, ct);
        if (!string.IsNullOrWhiteSpace(req.Name)) c.Name = req.Name.Trim();
        await ApplyChartAsync(c, source, req, ct);
        await ApplyAsync(c, source, req.Descriptions, req.Field, req.AggregateFn, req.FilterJson, req.BusinessModule, req.IsMoney, req.Visibility, req.DateRangeMode, req.DateFrom, req.DateTo, req.ShowInPulse, req.SortOrder, ct);
        if (req.Shares is not null) { db.ChartShares.RemoveRange(c.Shares); c.Shares.Clear(); await ApplySharesAsync(c.Shares, req.Shares, (u, ro, _) => new ChartShare { UserId = u, RoleId = ro }, ct); }
        await db.SaveChangesAsync(ct);
        return await ChartDtoAsync(await LoadChartAsync(id, ct, requireReadable: false), ct);
    }

    public async Task DeleteChartAsync(int id, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct, track: true, requireReadable: false);
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

    public async Task<ChartDataDto> EvaluateChartAsync(int id, CancellationToken ct)
    {
        var c = await LoadChartAsync(id, ct);
        return await EvaluateAsync(c, await PrefAsync(null, c.ChartDefinitionId, false, ct), compute: true, ct);
    }

    /// <summary>Datos de un gráfico con la preferencia del usuario; compute=false → sin calcular (Points vacío), como en el Pulso.</summary>
    private async Task<ChartDataDto> EvaluateAsync(ChartDefinition c, UserAnalyticsPreference? pref, bool compute, CancellationToken ct)
    {
        var source = registry.Get(c.DataSourceKey);
        var (mode, from, to, _) = await EffectiveAsync(c, pref, ct);
        var (fromUtc, toUtc) = source.DateField is null ? ((DateTime?)null, (DateTime?)null) : DateRangeResolver.Resolve(mode, from, to);
        var type = (await lookups.GetAsync(c.ChartTypeLookupId, ct))?.InternalCode ?? ChartTypes.Bar;
        IReadOnlyList<ChartPoint> points = Array.Empty<ChartPoint>();
        if (compute)
        {
            var fn = (await lookups.GetAsync(c.AggregateFnLookupId, ct))?.InternalCode ?? AggregateFns.Count;
            points = await engine.EvaluateChartAsync(c.DataSourceKey, c.GroupByField, fn, c.FieldKey, c.FilterJson, type, fromUtc, toUtc, ct);
        }
        var (visible, sort, origin) = PulsePanels.ResolveItem(pref?.PulseSortOrder, pref?.ShowInPulse, c.SortOrder, c.ShowInPulse);
        var module = (await lookups.GetAsync(c.BusinessModuleLookupId, ct))?.InternalCode ?? "";
        return new ChartDataDto(c.ChartDefinitionId, c.Name, type, c.IsMoney, points, source.DateField is null ? null : mode ?? DateRangeModes.Last7, fromUtc, toUtc,
            module, sort, visible, origin);
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

    /// <summary>Gráfico activo visible; con requireReadable (por defecto) además legible por §2.2. Si no: 404 'Gráfico'.</summary>
    private async Task<ChartDefinition> LoadChartAsync(int id, CancellationToken ct, bool track = false, bool requireReadable = true)
    {
        var q = db.ChartDefinitions.Include(c => c.Shares).AsQueryable();
        if (!track) q = q.AsNoTracking();
        var c = await q.FirstOrDefaultAsync(x => x.ChartDefinitionId == id && x.IsActive, ct) ?? throw new NotFoundException("Gráfico", id);
        var vis = (await lookups.GetAsync(c.VisibilityLookupId, ct))?.InternalCode ?? ReportVisibilities.Tenant;
        if (!IsVisible(c.IsSystem, c.OwnerUserId, vis, c.Shares.Select(s => (s.UserId, s.RoleId)), await MyRoleIdsAsync(ct))) throw new NotFoundException("Gráfico", id);
        if (requireReadable && !await CanReadAsync(c, await AccessAsync(ct), ct)) throw new NotFoundException("Gráfico", id);
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
        return new AnalyticsDefinitionDto(id, d.PublicId, d.Name, MultilingualText.Resolve(d.DescriptionJson, tenant.Lang), MultilingualText.Parse(d.DescriptionJson), d.DataSourceKey, d.FieldKey,
            (await lookups.GetAsync(d.AggregateFnLookupId, ct))?.InternalCode ?? "", d.FilterJson,
            (await lookups.GetAsync(d.BusinessModuleLookupId, ct))?.InternalCode ?? "", d.IsMoney, d.IsSystem, d.OwnerUserId, await UserNameAsync(d.OwnerUserId, ct),
            (await lookups.GetAsync(d.VisibilityLookupId, ct))?.InternalCode ?? "",
            d.DateRangeModeLookupId is null ? null : (await lookups.GetAsync(d.DateRangeModeLookupId.Value, ct))?.InternalCode, d.DateFrom, d.DateTo, d.ShowInPulse,
            await CanEditWithManageAsync(d.IsSystem, d.OwnerUserId, ct), await CanChangeDateAsync(d.IsSystem, d.OwnerUserId, ct), source?.DateField is not null,
            mode, from, to, pulse, groupBy, chartType, shares, d.SortOrder);
    }

    // =====================================================================
    // Pulso del día por paneles (Lote F8a, P1)
    // =====================================================================
    /// <summary>
    /// Pulso del usuario: solo los paneles del registro cuyo permiso pulse.*, permisos de datos y módulo tiene (sin el módulo
    /// ANALYTICS solo puede quedar WAREHOUSE), en su orden efectivo (usuario → compañía → registro), incluidos los ocultos para el
    /// modo Organizar. Dentro de INDICATORS/CHARTS, solo los elementos que pasan §2.2, con su orden (PulseSortOrder propio → SortOrder
    /// de la definición) y visibilidad (ShowInPulse propio → de la definición); orden por SortOrder efectivo y luego nombre. Solo se
    /// calcula lo visible (elemento visible dentro de un panel visible).
    /// </summary>
    public Task<PulseDto> GetPulseAsync(CancellationToken ct) => GetPulseAsync(companyOnly: false, ct);

    /// <summary>
    /// `companyOnly=true` (`GET pulse?scope=company`, exige pulse.organize_company): el Pulso de la COMPAÑÍA sin la
    /// capa personal del usuario que consulta — lo que usa "Organizar el de la compañía" para partir del estado real
    /// de la compañía, no del Pulso personal de quien lo abre (que puede tener su propio orden/ocultos).
    /// </summary>
    public async Task<PulseDto> GetPulseAsync(bool companyOnly, CancellationToken ct)
    {
        var tc = (TenantContext)tenant;
        tc.RequireTenantId();
        var userId = tc.RequireUserId();
        var access = await AccessAsync(ct);
        if (companyOnly) await permissions.EnsureAsync(PermissionCatalog.PulseOrganizeCompany, ct);

        var settings = companyOnly
            ? await db.Set<PulsePanelSetting>().AsNoTracking().Where(s => s.UserId == null).ToListAsync(ct)
            : await db.Set<PulsePanelSetting>().AsNoTracking().Where(s => s.UserId == null || s.UserId == userId).ToListAsync(ct);
        var panels = new List<PulsePanelDto>();
        foreach (var def in PulsePanels.All.Where(p => PulsePanels.CanSee(p, access.Has, access.ModuleOn)))
        {
            var mine = companyOnly ? null : settings.FirstOrDefault(s => s.UserId == userId && string.Equals(s.PanelKey, def.Key, StringComparison.OrdinalIgnoreCase));
            var company = settings.FirstOrDefault(s => s.UserId == null && string.Equals(s.PanelKey, def.Key, StringComparison.OrdinalIgnoreCase));
            var (visible, sort, source) = PulsePanels.ResolvePanel(def, mine, company);
            panels.Add(new PulsePanelDto(def.Key, visible, sort, source));
        }
        panels = panels.OrderBy(p => p.SortOrder).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();

        var prefs = companyOnly
            ? new List<UserAnalyticsPreference>()
            : await db.UserAnalyticsPreferences.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);
        var names = StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true);

        var indicators = new List<IndicatorValueDto>();
        if (panels.FirstOrDefault(p => p.Key == PulsePanels.Indicators) is { } indicatorsPanel)
            foreach (var i in await ReadableIndicatorsAsync(access, ct))
            {
                var pref = prefs.FirstOrDefault(p => p.IndicatorDefinitionId == i.IndicatorDefinitionId);
                var shown = pref?.ShowInPulse ?? i.ShowInPulse;
                indicators.Add(await EvaluateAsync(i, pref, compute: indicatorsPanel.IsVisible && shown, ct));
            }

        var charts = new List<ChartDataDto>();
        if (panels.FirstOrDefault(p => p.Key == PulsePanels.Charts) is { } chartsPanel)
            foreach (var c in await ReadableChartsAsync(access, ct))
            {
                var pref = prefs.FirstOrDefault(p => p.ChartDefinitionId == c.ChartDefinitionId);
                var shown = pref?.ShowInPulse ?? c.ShowInPulse;
                charts.Add(await EvaluateAsync(c, pref, compute: chartsPanel.IsVisible && shown, ct));
            }

        var hasPersonal = !companyOnly && (settings.Any(s => s.UserId == userId) || prefs.Any(p => p.PulseSortOrder.HasValue || p.ShowInPulse.HasValue));
        return new PulseDto(
            indicators.OrderBy(x => x.SortOrder).ThenBy(x => x.Name, names).ToList(),
            charts.OrderBy(x => x.SortOrder).ThenBy(x => x.Name, names).ToList(),
            panels, hasPersonal, access.Has(PermissionCatalog.PulseOrganizeCompany));
    }

    /// <summary>
    /// PUT /analytics/pulse/layout?scope=mine|company. Idempotente; escribe solo lo que viene (lo ausente no se toca).
    /// mine: UserAnalyticsPreference.PulseSortOrder/ShowInPulse + PulsePanelSetting(UserId = yo). company (pulse.organize_company,
    /// si no 403): IndicatorDefinition/ChartDefinition.SortOrder/ShowInPulse + PulsePanelSetting(UserId NULL). Validaciones (400):
    /// scope, clave de panel desconocida, tipo de elemento; un panel que el usuario no ve o un elemento que no puede leer → 404.
    /// Devuelve el Pulso nuevo.
    /// </summary>
    public async Task<PulseDto> SaveLayoutAsync(string? scope, PulseLayoutRequest? req, CancellationToken ct)
    {
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is not (ScopeMine or ScopeCompany)) throw new ValidationException("scope", InvalidScopeMessage);
        var company = normalized == ScopeCompany;
        var tc = (TenantContext)tenant;
        var tenantId = tc.RequireTenantId();
        var userId = tc.RequireUserId();
        if (company) await permissions.EnsureAsync(PermissionCatalog.PulseOrganizeCompany, ct);

        // 1) Validación de forma (400) de todo el cuerpo antes de tocar nada.
        var panelReqs = req?.Panels ?? new List<PulseLayoutPanel>();
        var itemReqs = req?.Items ?? new List<PulseLayoutItem>();
        var panelWrites = new Dictionary<string, (PulsePanelDef Def, PulseLayoutPanel Req)>(StringComparer.Ordinal);
        for (var n = 0; n < panelReqs.Count; n++)
        {
            var p = panelReqs[n];
            var def = PulsePanels.Find(p?.Key) ?? throw new ValidationException($"panels[{n}].key", UnknownPanelMessage(p?.Key));
            panelWrites[def.Key] = (def, p!);   // repetido: gana el último
        }
        var itemWrites = new Dictionary<(string Kind, int Id), PulseLayoutItem>();
        for (var n = 0; n < itemReqs.Count; n++)
        {
            var it = itemReqs[n];
            var kind = (it?.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is not (KindIndicator or KindChart)) throw new ValidationException($"items[{n}].kind", InvalidKindMessage);
            itemWrites[(kind, it!.Id)] = it;
        }

        // 2) Alcance del usuario (404: no se revela lo que no ve).
        var access = await AccessAsync(ct);
        foreach (var (def, _) in panelWrites.Values)
            if (!PulsePanels.CanSee(def, access.Has, access.ModuleOn)) throw new NotFoundException("Panel de Pulso", def.Key);
        var canIndicators = PulsePanels.CanSee(PulsePanels.Find(PulsePanels.Indicators)!, access.Has, access.ModuleOn);
        var canCharts = PulsePanels.CanSee(PulsePanels.Find(PulsePanels.Charts)!, access.Has, access.ModuleOn);

        // 3) Elementos.
        foreach (var ((kind, id), it) in itemWrites)
        {
            if (kind == KindIndicator)
            {
                if (!canIndicators) throw new NotFoundException("Indicador", id);
                var i = await LoadIndicatorAsync(id, ct, track: company);
                if (company) { i.SortOrder = it.SortOrder; i.ShowInPulse = it.IsVisible; }
                else
                {
                    var pref = await PrefOrCreateAsync(i.IndicatorDefinitionId, null, ct);
                    pref.PulseSortOrder = it.SortOrder; pref.ShowInPulse = it.IsVisible;
                }
            }
            else
            {
                if (!canCharts) throw new NotFoundException("Gráfico", id);
                var c = await LoadChartAsync(id, ct, track: company);
                if (company) { c.SortOrder = it.SortOrder; c.ShowInPulse = it.IsVisible; }
                else
                {
                    var pref = await PrefOrCreateAsync(null, c.ChartDefinitionId, ct);
                    pref.PulseSortOrder = it.SortOrder; pref.ShowInPulse = it.IsVisible;
                }
            }
        }

        // 4) Paneles (fila de compañía UserId NULL o fila propia).
        int? owner = company ? null : userId;
        var rows = await db.Set<PulsePanelSetting>().Where(s => s.UserId == owner).ToListAsync(ct);
        foreach (var (def, p) in panelWrites.Values)
        {
            var row = rows.FirstOrDefault(s => string.Equals(s.PanelKey, def.Key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new PulsePanelSetting { TenantId = tenantId, UserId = owner, PanelKey = def.Key };
                db.Set<PulsePanelSetting>().Add(row);
                rows.Add(row);
            }
            row.IsVisible = p.IsVisible;
            row.SortOrder = p.SortOrder;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return await GetPulseAsync(ct);
    }

    /// <summary>
    /// DELETE /analytics/pulse/layout/mine: vuelve al Pulso de la compañía. Borra PulseSortOrder y ShowInPulse propios de
    /// UserAnalyticsPreference (conserva el rango de fecha propio) y las filas propias de PulsePanelSetting (auditadas).
    /// </summary>
    public async Task ResetMyLayoutAsync(CancellationToken ct)
    {
        var userId = ((TenantContext)tenant).RequireUserId();
        var prefs = await db.UserAnalyticsPreferences
            .Where(p => p.UserId == userId && (p.PulseSortOrder != null || p.ShowInPulse != null)).ToListAsync(ct);
        foreach (var p in prefs)
        {
            p.PulseSortOrder = null;
            p.ShowInPulse = null;
            p.UpdatedAtUtc = DateTime.UtcNow;
        }
        var rows = await db.Set<PulsePanelSetting>().Where(s => s.UserId == userId).ToListAsync(ct);
        db.Set<PulsePanelSetting>().RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }
}
