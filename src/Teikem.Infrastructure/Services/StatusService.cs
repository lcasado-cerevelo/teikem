using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Efecto en código de una transición (disparar cotización, congelar ruta...). Se registra por dominio de estatus.</summary>
public interface IStatusTransitionEffect
{
    string StatusDomain { get; }
    Task OnTransitionedAsync(StatusTransitionContext context, CancellationToken ct);
}

public sealed record StatusTransitionContext(string StatusDomain, string EntityTypeCode, int EntityId, StatusCode? From, StatusCode To, string? Comment);

/// <summary>
/// Capa B: motor de estatus sin grafo de workflow. El pipeline de cada tenant es data (StatusCode + override),
/// clasificado por StageKind. El servicio valida etapa-activa + reglas de entrada lateral, registra EntityStatusHistory
/// y dispara los efectos en código. StatusCapability = regla de negocio; Permission = seguridad. Ambas tienen que pasar.
/// </summary>
public sealed class StatusService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, IEnumerable<IStatusTransitionEffect> effects, PermissionService permissions)
{
    // ---------------- Consulta ----------------

    public async Task<IReadOnlyList<StatusDto>> GetPipelineAsync(string entity, bool includeDisabled, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind)
            .Where(s => s.Entity == entity && s.IsActive).ToListAsync(ct);
        if (codes.Count == 0 && !await db.CatalogDomains.AnyAsync(d => d.DomainKey == entity && d.Scope == CatalogScope.Status, ct))
            throw new NotFoundException("Dominio de estatus", entity);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        var list = codes.Select(c => ToDto(c, overrides.GetValueOrDefault(c.StatusCodeId)));
        if (!includeDisabled) list = list.Where(s => s.IsEnabled);
        return list.OrderBy(s => s.SortOrder).ToList();
    }

    public async Task<StatusDto> SetOverrideAsync(string entity, string code, StatusOverrideRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var s = await db.StatusCodes.Include(x => x.StageKind).FirstOrDefaultAsync(x => x.Entity == entity && x.InternalCode == code, ct)
                ?? throw new NotFoundException($"Estatus {entity}", code);
        var o = await db.StatusCodeOverrides.FirstOrDefaultAsync(x => x.StatusCodeId == s.StatusCodeId, ct);
        if (o is null) { o = new StatusCodeOverride { TenantId = tenantId, StatusCodeId = s.StatusCodeId }; db.StatusCodeOverrides.Add(o); }
        if (req.Labels is not null) o.CustomLabelJson = req.Labels.Count == 0 ? null : MultilingualText.Serialize(req.Labels);
        if (req.ColorHex is not null) o.CustomColorHex = string.IsNullOrWhiteSpace(req.ColorHex) ? null : req.ColorHex;
        if (req.IsEnabled.HasValue) o.IsEnabled = req.IsEnabled.Value;
        if (req.SortOverride.HasValue) o.SortOverride = req.SortOverride.Value <= 0 ? null : req.SortOverride;

        // El validador corre al guardar la configuración, no en cada transición.
        var validation = await ValidatePipelineAsync(entity, pending: (s.StatusCodeId, o), ct);
        if (!validation.IsValid) throw new StatusRuleException("Pipeline inválido: " + string.Join(" ", validation.Errors));

        await db.SaveChangesAsync(ct);
        return ToDto(s, o);
    }

    /// <summary>Dependencias duras del pipeline: al menos una etapa inicial y una terminal habilitadas, y ≥1 etapa pipeline.</summary>
    public async Task<PipelineValidationResult> ValidatePipelineAsync(string entity, (int StatusCodeId, StatusCodeOverride Override)? pending, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == entity && s.IsActive).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        if (pending is not null) overrides[pending.Value.StatusCodeId] = pending.Value.Override;

        var enabled = codes.Where(c => overrides.GetValueOrDefault(c.StatusCodeId)?.IsEnabled ?? true).ToList();
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!enabled.Any(c => c.IsInitial)) errors.Add("Debe quedar habilitada al menos una etapa inicial.");
        if (!enabled.Any(c => c.StageKind!.InternalCode == StageKinds.Terminal)) errors.Add("Debe quedar habilitada al menos una etapa terminal.");
        if (!enabled.Any(c => c.StageKind!.InternalCode == StageKinds.Pipeline)) errors.Add("Debe quedar habilitada al menos una etapa del pipeline.");
        if (enabled.Count(c => c.IsInitial) > 1) warnings.Add("Hay más de una etapa inicial habilitada; se usará la de menor orden.");
        return new PipelineValidationResult(errors.Count == 0, errors, warnings);
    }

    // ---------------- Capacidades ----------------

    public async Task<IReadOnlyList<StatusCapabilityDto>> GetCapabilitiesAsync(string entityTypeCode, CancellationToken ct)
    {
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var rows = await db.StatusCapabilities.AsNoTracking().Include(c => c.StatusCode).Include(c => c.Capability)
            .Where(c => c.EntityTypeLookupId == entityTypeId).ToListAsync(ct);
        // La regla del tenant pisa la regla por defecto (TenantId NULL)
        return rows.GroupBy(r => (r.StatusCodeId, r.CapabilityLookupId))
            .Select(g => g.OrderByDescending(r => r.TenantId.HasValue).First())
            .Select(r => new StatusCapabilityDto(r.StatusCode!.InternalCode, r.Capability!.InternalCode, r.IsAllowed, r.TenantId.HasValue))
            .OrderBy(r => r.StatusCode).ThenBy(r => r.Capability).ToList();
    }

    public async Task<IReadOnlyList<StatusCapabilityDto>> SetCapabilitiesAsync(string entityTypeCode, string statusDomain, IEnumerable<StatusCapabilityUpsert> items, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var statuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == statusDomain).ToDictionaryAsync(s => s.InternalCode, s => s.StatusCodeId, ct);
        var existing = await db.StatusCapabilities.Where(c => c.EntityTypeLookupId == entityTypeId && c.TenantId == tenantId).ToListAsync(ct);
        foreach (var it in items)
        {
            if (!statuses.TryGetValue(it.StatusCode, out var statusId)) throw new NotFoundException($"Estatus {statusDomain}", it.StatusCode);
            var capId = await lookups.GetIdAsync(LookupDomains.Capability, it.Capability, ct);
            var row = existing.FirstOrDefault(c => c.StatusCodeId == statusId && c.CapabilityLookupId == capId);
            if (row is null)
            {
                row = new StatusCapability { TenantId = tenantId, EntityTypeLookupId = entityTypeId, StatusCodeId = statusId, CapabilityLookupId = capId };
                db.StatusCapabilities.Add(row);
                existing.Add(row);
            }
            row.IsAllowed = it.IsAllowed;
        }
        await db.SaveChangesAsync(ct);
        return await GetCapabilitiesAsync(entityTypeCode, ct);
    }

    /// <summary>Guard de negocio reaplicado en backend: ¿el estatus actual permite la acción?</summary>
    public async Task<bool> IsAllowedAsync(string entityTypeCode, int statusCodeId, string capabilityCode, CancellationToken ct)
    {
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var capId = await lookups.GetIdAsync(LookupDomains.Capability, capabilityCode, ct);
        var rules = await db.StatusCapabilities.AsNoTracking()
            .Where(c => c.EntityTypeLookupId == entityTypeId && c.StatusCodeId == statusCodeId && c.CapabilityLookupId == capId)
            .ToListAsync(ct);
        if (rules.Count == 0) return true; // sin regla configurada = permitido
        return rules.OrderByDescending(r => r.TenantId.HasValue).First().IsAllowed;
    }

    public async Task EnsureAllowedAsync(string entityTypeCode, int statusCodeId, string capabilityCode, CancellationToken ct)
    {
        if (!await IsAllowedAsync(entityTypeCode, statusCodeId, capabilityCode, ct))
            throw new StatusRuleException($"El estatus actual no permite la acción '{capabilityCode}'.");
    }

    // ---------------- Entradas laterales ----------------

    public async Task<IReadOnlyList<StatusLateralEntryDto>> GetLateralEntriesAsync(string entityTypeCode, CancellationToken ct)
    {
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var rows = await db.StatusLateralEntries.AsNoTracking().Include(l => l.LateralStatus).Include(l => l.FromStatus)
            .Where(l => l.EntityTypeLookupId == entityTypeId).ToListAsync(ct);
        return rows.GroupBy(r => (r.LateralStatusCodeId, r.FromStatusCodeId))
            .Select(g => g.OrderByDescending(r => r.TenantId.HasValue).First())
            .Select(r => new StatusLateralEntryDto(r.LateralStatus!.InternalCode, r.FromStatus!.InternalCode, r.IsAllowed, r.TenantId.HasValue))
            .ToList();
    }

    public async Task<IReadOnlyList<StatusLateralEntryDto>> SetLateralEntriesAsync(string entityTypeCode, string statusDomain, IEnumerable<StatusLateralEntryUpsert> items, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var statuses = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == statusDomain).ToDictionaryAsync(s => s.InternalCode, ct);
        var existing = await db.StatusLateralEntries.Where(l => l.EntityTypeLookupId == entityTypeId && l.TenantId == tenantId).ToListAsync(ct);
        foreach (var it in items)
        {
            if (!statuses.TryGetValue(it.LateralStatusCode, out var lateral)) throw new NotFoundException($"Estatus {statusDomain}", it.LateralStatusCode);
            if (!statuses.TryGetValue(it.FromStatusCode, out var from)) throw new NotFoundException($"Estatus {statusDomain}", it.FromStatusCode);
            if (lateral.StageKind!.InternalCode == StageKinds.Pipeline) throw new ValidationException("lateralStatusCode", $"'{it.LateralStatusCode}' es una etapa del pipeline, no un lateral/terminal.");
            var row = existing.FirstOrDefault(l => l.LateralStatusCodeId == lateral.StatusCodeId && l.FromStatusCodeId == from.StatusCodeId);
            if (row is null)
            {
                row = new StatusLateralEntry { TenantId = tenantId, EntityTypeLookupId = entityTypeId, LateralStatusCodeId = lateral.StatusCodeId, FromStatusCodeId = from.StatusCodeId };
                db.StatusLateralEntries.Add(row);
                existing.Add(row);
            }
            row.IsAllowed = it.IsAllowed;
        }
        await db.SaveChangesAsync(ct);
        return await GetLateralEntriesAsync(entityTypeCode, ct);
    }

    // ---------------- Transición ----------------

    /// <summary>Estatus inicial habilitado del dominio para el tenant (para crear registros nuevos).</summary>
    public async Task<StatusCode> GetInitialAsync(string statusDomain, CancellationToken ct)
    {
        var enabled = await LoadEnabledAsync(statusDomain, ct);
        return enabled.Where(s => s.IsInitial).OrderBy(s => s.SortOrder).FirstOrDefault()
               ?? throw new StatusRuleException($"El pipeline '{statusDomain}' no tiene etapa inicial habilitada.");
    }

    public async Task<StatusCode> GetByCodeAsync(string statusDomain, string code, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).FirstOrDefaultAsync(s => s.Entity == statusDomain && s.InternalCode == code, ct)
           ?? throw new NotFoundException($"Estatus {statusDomain}", code);

    /// <summary>Largo máximo del comentario de una transición (= EntityStatusHistory.Comment NVARCHAR(500)).</summary>
    public const int CommentMaxLength = 500;
    public const string CommentTooLongMessage = "El comentario admite como máximo 500 caracteres.";

    /// <summary>
    /// Transición segura: valida etapa activa + dependencia dura, registra EntityStatusHistory y dispara efectos.
    /// Devuelve el StatusCode destino; el llamador asigna StatusCodeId en su entidad dentro de la misma unidad de trabajo.
    /// </summary>
    public async Task<StatusCode> TransitionAsync(string statusDomain, string entityTypeCode, int entityId, int? fromStatusId, string toCode, string? comment, CancellationToken ct)
    {
        // EntityStatusHistory.Comment es NVARCHAR(500): un comentario más largo terminaría en 500 al guardar (truncado en SQL).
        if (comment is { Length: > CommentMaxLength }) throw new ValidationException("comment", CommentTooLongMessage);
        var enabled = await LoadEnabledAsync(statusDomain, ct);
        var to = enabled.FirstOrDefault(s => s.InternalCode.Equals(toCode, StringComparison.OrdinalIgnoreCase))
                 ?? throw new StatusRuleException($"El estatus '{toCode}' no existe o no está habilitado para esta compañía.");
        var from = fromStatusId.HasValue ? await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).FirstOrDefaultAsync(s => s.StatusCodeId == fromStatusId.Value, ct) : null;
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);

        await EnsureTransitionAllowedAsync(enabled, from, to, entityTypeId, entityId, ct);

        db.EntityStatusHistories.Add(new EntityStatusHistory
        {
            TenantId = ((TenantContext)tenant).RequireTenantId(), EntityTypeLookupId = entityTypeId, EntityId = entityId,
            FromStatusCodeId = from?.StatusCodeId, ToStatusCodeId = to.StatusCodeId, Comment = comment,
            ChangedAtUtc = DateTime.UtcNow, ChangedBy = tenant.UserId,
        });

        var context = new StatusTransitionContext(statusDomain, entityTypeCode, entityId, from, to, comment);
        foreach (var effect in effects.Where(e => e.StatusDomain.Equals(statusDomain, StringComparison.OrdinalIgnoreCase)))
            await effect.OnTransitionedAsync(context, ct);
        return to;
    }

    private async Task EnsureTransitionAllowedAsync(List<StatusCode> enabled, StatusCode? from, StatusCode to, int entityTypeId, int entityId, CancellationToken ct)
    {
        var toKind = to.StageKind!.InternalCode;
        if (from is null)
        {
            if (!to.IsInitial) throw new StatusRuleException($"Un registro nuevo debe empezar en una etapa inicial; '{to.InternalCode}' no lo es.");
            return;
        }
        if (from.StatusCodeId == to.StatusCodeId) throw new StatusRuleException("El registro ya está en ese estatus.");
        var fromKind = from.StageKind!.InternalCode;
        if (fromKind == StageKinds.Terminal) throw new StatusRuleException($"'{from.InternalCode}' es terminal: no admite más transiciones.");

        var pipeline = enabled.Where(s => s.StageKind!.InternalCode == StageKinds.Pipeline).OrderBy(s => s.SortOrder).ToList();
        var ordered = enabled.OrderBy(s => s.SortOrder).ToList();

        if (fromKind == StageKinds.Pipeline)
        {
            // Siguiente etapa habilitada por SortOrder (pipeline o terminal de cierre, ej. ARRIVED → DELIVERED)
            var next = ordered.Where(s => s.SortOrder > from.SortOrder && s.StageKind!.InternalCode != StageKinds.Lateral).OrderBy(s => s.SortOrder).FirstOrDefault();
            if (next is not null && next.StatusCodeId == to.StatusCodeId) return;
            if (toKind == StageKinds.Pipeline) throw new StatusRuleException($"Salto ilegal: de '{from.InternalCode}' solo se puede avanzar a '{next?.InternalCode ?? "(ninguna)"}'.");
            // Lateral o terminal fuera de orden: punto de entrada por tenant
            if (!await LateralEntryAllowedAsync(entityTypeId, to.StatusCodeId, from.StatusCodeId, ct))
                throw new StatusRuleException($"No se permite pasar a '{to.InternalCode}' desde '{from.InternalCode}' según el modelo de esta compañía.");
            return;
        }

        // fromKind == LATERAL: se vuelve al pipeline a la etapa desde la que se desvió (o la siguiente), o a un terminal permitido.
        if (toKind == StageKinds.Pipeline)
        {
            var lastPipelineId = await db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == entityId && h.ToStatus!.StageKind!.InternalCode == StageKinds.Pipeline)
                .OrderByDescending(h => h.EntityStatusHistoryId).Select(h => (int?)h.ToStatusCodeId).FirstOrDefaultAsync(ct);
            var last = pipeline.FirstOrDefault(p => p.StatusCodeId == lastPipelineId);
            var nextOfLast = last is null ? null : pipeline.FirstOrDefault(p => p.SortOrder > last.SortOrder);
            if (to.StatusCodeId == last?.StatusCodeId || to.StatusCodeId == nextOfLast?.StatusCodeId || (last is null && to.IsInitial)) return;
            throw new StatusRuleException($"Desde el lateral '{from.InternalCode}' solo se puede regresar a '{last?.InternalCode ?? "la etapa inicial"}'{(nextOfLast is null ? "" : $" o avanzar a '{nextOfLast.InternalCode}'")}.");
        }
        if (!await LateralEntryAllowedAsync(entityTypeId, to.StatusCodeId, from.StatusCodeId, ct))
            throw new StatusRuleException($"No se permite pasar a '{to.InternalCode}' desde '{from.InternalCode}'.");
    }

    /// <summary>Sin reglas para ese lateral = permitido desde cualquier etapa; con reglas, solo desde las listadas (tenant pisa default).</summary>
    private async Task<bool> LateralEntryAllowedAsync(int entityTypeId, int lateralId, int fromId, CancellationToken ct)
    {
        var rules = await db.StatusLateralEntries.AsNoTracking()
            .Where(l => l.EntityTypeLookupId == entityTypeId && l.LateralStatusCodeId == lateralId).ToListAsync(ct);
        if (rules.Count == 0) return true;
        var tenantRules = rules.Where(r => r.TenantId.HasValue).ToList();
        var effective = tenantRules.Count > 0 ? tenantRules : rules;
        return effective.Any(r => r.FromStatusCodeId == fromId && r.IsAllowed);
    }

    private async Task<List<StatusCode>> LoadEnabledAsync(string statusDomain, CancellationToken ct)
    {
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == statusDomain && s.IsActive).ToListAsync(ct);
        if (codes.Count == 0) throw new NotFoundException("Dominio de estatus", statusDomain);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var disabled = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId) && !o.IsEnabled).Select(o => o.StatusCodeId).ToListAsync(ct);
        var sortOverrides = await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId) && o.SortOverride != null).ToDictionaryAsync(o => o.StatusCodeId, o => o.SortOverride!.Value, ct);
        foreach (var c in codes) if (sortOverrides.TryGetValue(c.StatusCodeId, out var so)) c.SortOrder = so;
        return codes.Where(c => !disabled.Contains(c.StatusCodeId)).ToList();
    }

    public async Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(string entityTypeCode, int entityId, CancellationToken ct)
    {
        // El historial (con comentarios) exige el permiso de lectura de la entidad (CLIENT → clients.read, CONTRACT → contracts.read, ...).
        if (PermissionCatalog.OwnerReadPermission.TryGetValue(entityTypeCode, out var readPerm)) await permissions.EnsureAsync(readPerm, ct);
        var entityTypeId = await lookups.GetIdAsync(LookupDomains.EntityType, entityTypeCode, ct);
        var rows = await db.EntityStatusHistories.AsNoTracking().Include(h => h.FromStatus).Include(h => h.ToStatus)
            .Where(h => h.EntityTypeLookupId == entityTypeId && h.EntityId == entityId)
            .OrderBy(h => h.EntityStatusHistoryId).ToListAsync(ct);
        var userIds = rows.Where(r => r.ChangedBy.HasValue).Select(r => r.ChangedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);
        return rows.Select(h => new StatusHistoryDto(h.EntityStatusHistoryId,
            h.FromStatus?.InternalCode, h.FromStatus is null ? null : MultilingualText.Resolve(h.FromStatus.LabelJson, tenant.Lang),
            h.ToStatus!.InternalCode, MultilingualText.Resolve(h.ToStatus.LabelJson, tenant.Lang),
            h.Comment, h.ChangedAtUtc, h.ChangedBy, h.ChangedBy.HasValue ? users.GetValueOrDefault(h.ChangedBy.Value) : null)).ToList();
    }

    private StatusDto ToDto(StatusCode s, StatusCodeOverride? o)
    {
        var labelJson = MultilingualText.Merge(s.LabelJson, o?.CustomLabelJson);
        return new StatusDto(s.StatusCodeId, s.Entity, s.InternalCode, MultilingualText.Resolve(labelJson, tenant.Lang), MultilingualText.Parse(labelJson),
            o?.CustomColorHex ?? s.ColorHex, s.Icon, o?.SortOverride ?? s.SortOrder, s.StageKind?.InternalCode ?? "", s.IsInitial, o?.IsEnabled ?? true, o is not null);
    }
}
