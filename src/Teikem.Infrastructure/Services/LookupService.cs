using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Capa A: catálogos. Resuelve LookupCode base + LookupCodeOverride del tenant (renombrar, reordenar, deshabilitar),
/// etiquetas por idioma, y el catálogo de listas propias del tenant (para campos personalizados tipo lista).
/// </summary>
public sealed class LookupService(TeikemDbContext db, ITenantContext tenant, ILookupCache cache)
{
    public async Task<IReadOnlyList<CatalogDomainDto>> GetDomainsAsync(byte? scope, CancellationToken ct)
    {
        var q = db.CatalogDomains.AsNoTracking().Where(d => d.IsActive);
        if (scope.HasValue) q = q.Where(d => d.Scope == scope.Value);
        var list = await q.OrderBy(d => d.DomainKey).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<CatalogDomainDto> GetDomainAsync(string domainKey, CancellationToken ct)
    {
        var d = await db.CatalogDomains.AsNoTracking().FirstOrDefaultAsync(x => x.DomainKey == domainKey, ct)
                ?? throw new NotFoundException("Dominio de catálogo", domainKey);
        return ToDto(d);
    }

    /// <summary>Valores del dominio resueltos para el tenant activo (override aplicado, deshabilitados opcionales).</summary>
    public async Task<IReadOnlyList<LookupValueDto>> GetValuesAsync(string entity, bool includeDisabled, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var codes = await db.LookupCodes.AsNoTracking()
            .Where(l => l.Entity == entity && l.IsActive)
            .ToListAsync(ct);
        if (codes.Count == 0 && !await db.CatalogDomains.AnyAsync(d => d.DomainKey == entity, ct))
            throw new NotFoundException("Dominio de catálogo", entity);
        var ids = codes.Select(c => c.LookupCodeId).ToList();
        var overrides = tenantId is null
            ? new Dictionary<int, LookupCodeOverride>()
            : await db.LookupCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.LookupCodeId)).ToDictionaryAsync(o => o.LookupCodeId, ct);

        var result = codes.Select(c =>
        {
            overrides.TryGetValue(c.LookupCodeId, out var o);
            return ToDto(c, o);
        });
        if (!includeDisabled) result = result.Where(r => r.IsEnabled);
        return result.OrderBy(r => r.SortOrder).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<LookupValueDto> GetValueAsync(string entity, string code, CancellationToken ct)
    {
        var c = await db.LookupCodes.AsNoTracking().FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct)
                ?? throw new NotFoundException($"Valor {entity}", code);
        var o = await db.LookupCodeOverrides.AsNoTracking().FirstOrDefaultAsync(x => x.LookupCodeId == c.LookupCodeId, ct);
        return ToDto(c, o);
    }

    /// <summary>Etiqueta resuelta por idioma de un LookupCodeId (con override del tenant).</summary>
    public async Task<string> ResolveLabelAsync(int lookupCodeId, CancellationToken ct)
    {
        var c = await cache.GetAsync(lookupCodeId, ct);
        if (c is null) return string.Empty;
        var o = await db.LookupCodeOverrides.AsNoTracking().FirstOrDefaultAsync(x => x.LookupCodeId == lookupCodeId, ct);
        return MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang);
    }

    // ---------------- Override por tenant ----------------

    public async Task<LookupValueDto> SetOverrideAsync(string entity, string code, LookupOverrideRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var c = await db.LookupCodes.FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct)
                ?? throw new NotFoundException($"Valor {entity}", code);
        var o = await db.LookupCodeOverrides.FirstOrDefaultAsync(x => x.LookupCodeId == c.LookupCodeId, ct);
        if (o is null)
        {
            o = new LookupCodeOverride { TenantId = tenantId, LookupCodeId = c.LookupCodeId };
            db.LookupCodeOverrides.Add(o);
        }
        if (req.Labels is not null) o.CustomLabelJson = req.Labels.Count == 0 ? null : MultilingualText.Serialize(req.Labels);
        if (req.ExtraJson is not null) o.CustomExtraJson = req.ExtraJson;
        if (req.IsEnabled.HasValue) o.IsEnabled = req.IsEnabled.Value;
        if (req.SortOverride.HasValue) o.SortOverride = req.SortOverride.Value <= 0 ? null : req.SortOverride;
        await db.SaveChangesAsync(ct);
        return ToDto(c, o);
    }

    public async Task RemoveOverrideAsync(string entity, string code, CancellationToken ct)
    {
        var c = await db.LookupCodes.AsNoTracking().FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct)
                ?? throw new NotFoundException($"Valor {entity}", code);
        var o = await db.LookupCodeOverrides.FirstOrDefaultAsync(x => x.LookupCodeId == c.LookupCodeId, ct);
        if (o is null) return;
        db.LookupCodeOverrides.Remove(o);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Mantenimiento de valores ----------------

    /// <summary>Agrega un valor a un dominio. En dominios globales lo hace el admin de plataforma; en listas del tenant, el tenant.</summary>
    public async Task<LookupValueDto> AddValueAsync(string entity, LookupCodeUpsertRequest req, CancellationToken ct)
    {
        var domain = await db.CatalogDomains.FirstOrDefaultAsync(d => d.DomainKey == entity, ct)
                     ?? throw new NotFoundException("Dominio de catálogo", entity);
        EnsureCanEditDomain(domain);
        var code = NormalizeCode(req.Code);
        // Country alimenta el snapshot CHAR(2) de las paradas de orden (Lote 3): solo códigos ISO alfa-2.
        if (entity == LookupDomains.Country && !CountryCode.IsValid(code)) throw new ValidationException("code", CountryCode.InvalidMessage);
        if (await db.LookupCodes.AnyAsync(l => l.Entity == entity && l.InternalCode == code, ct))
            throw new ConflictException($"Ya existe el valor '{code}' en {entity}.");
        if (req.Labels is null || req.Labels.Count == 0) throw new ValidationException("labels", "La etiqueta es obligatoria.");

        var lc = new LookupCode
        {
            Entity = entity, InternalCode = code, LabelJson = MultilingualText.Serialize(req.Labels),
            DescriptionJson = req.Descriptions is { Count: > 0 } ? MultilingualText.Serialize(req.Descriptions) : null,
            ExtraJson = req.ExtraJson, SortOrder = req.SortOrder ?? 100, IsSystem = false, TenantId = domain.TenantId,
        };
        db.LookupCodes.Add(lc);
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return ToDto(lc, null);
    }

    public async Task<LookupValueDto> UpdateValueAsync(string entity, string code, LookupCodeUpsertRequest req, CancellationToken ct)
    {
        var domain = await db.CatalogDomains.FirstOrDefaultAsync(d => d.DomainKey == entity, ct)
                     ?? throw new NotFoundException("Dominio de catálogo", entity);
        EnsureCanEditDomain(domain);
        var lc = await db.LookupCodes.FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct)
                 ?? throw new NotFoundException($"Valor {entity}", code);
        if (req.Labels is { Count: > 0 }) lc.LabelJson = MultilingualText.Serialize(req.Labels);
        if (req.Descriptions is not null) lc.DescriptionJson = req.Descriptions.Count == 0 ? null : MultilingualText.Serialize(req.Descriptions);
        if (req.ExtraJson is not null) lc.ExtraJson = req.ExtraJson;
        if (req.SortOrder.HasValue) lc.SortOrder = req.SortOrder.Value;
        lc.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return ToDto(lc, null);
    }

    /// <summary>Desactivar, no borrar: un valor con historial sigue leyéndose bien en los registros que lo usan.</summary>
    public async Task DeactivateValueAsync(string entity, string code, bool active, CancellationToken ct)
    {
        var domain = await db.CatalogDomains.FirstOrDefaultAsync(d => d.DomainKey == entity, ct)
                     ?? throw new NotFoundException("Dominio de catálogo", entity);
        EnsureCanEditDomain(domain);
        var lc = await db.LookupCodes.FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct)
                 ?? throw new NotFoundException($"Valor {entity}", code);
        if (lc.IsSystem && !active) throw new ConflictException("Un valor de sistema no se desactiva globalmente; use el override del tenant (IsEnabled=false).");
        lc.IsActive = active;
        lc.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }

    // ---------------- Catálogo de listas del tenant ----------------

    /// <summary>Crea una lista propia del tenant (CatalogDomain Scope=Lookup, TenantId=tenant) con sus valores.</summary>
    public async Task<CatalogDomainDto> CreateTenantListAsync(CatalogListCreateRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("name", "El nombre es obligatorio.");
        var slug = Regex.Replace(req.Name.Trim().ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        if (slug.Length == 0) throw new ValidationException("name", "Nombre inválido.");
        var key = $"T{tenantId}_{slug}";
        if (key.Length > 60) key = key[..60];
        if (await db.CatalogDomains.IgnoreQueryFilters().AnyAsync(d => d.DomainKey == key, ct))
            throw new ConflictException($"Ya existe una lista con la clave '{key}'.");

        var domain = new CatalogDomain
        {
            DomainKey = key, Scope = CatalogScope.Lookup, TenantId = tenantId, IsSystem = false, IsActive = true,
            LabelJson = MultilingualText.Build(req.Name.Trim(), string.IsNullOrWhiteSpace(req.NameEn) ? req.Name.Trim() : req.NameEn.Trim()),
            Description = req.Description,
        };
        db.CatalogDomains.Add(domain);
        var sort = 10;
        foreach (var v in req.Values ?? new List<LookupCodeUpsertRequest>())
        {
            db.LookupCodes.Add(new LookupCode
            {
                Entity = key, InternalCode = NormalizeCode(v.Code), LabelJson = MultilingualText.Serialize(v.Labels),
                SortOrder = v.SortOrder ?? (sort += 10), IsSystem = false, TenantId = tenantId,
            });
        }
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return ToDto(domain);
    }

    /// <summary>Eliminar una lista solo si no está en uso por un campo personalizado.</summary>
    public async Task DeleteTenantListAsync(string domainKey, CancellationToken ct)
    {
        var domain = await db.CatalogDomains.FirstOrDefaultAsync(d => d.DomainKey == domainKey, ct)
                     ?? throw new NotFoundException("Lista", domainKey);
        if (domain.IsSystem || domain.TenantId is null) throw new ConflictException("Las listas de sistema no se eliminan.");
        if (await db.CustomFieldDefinitions.AnyAsync(f => f.IsActive && f.RefEntity == domainKey, ct))
            throw new ConflictException("La lista está en uso por un campo personalizado; desactívela en vez de eliminarla.");
        domain.IsActive = false;
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
    }

    private void EnsureCanEditDomain(CatalogDomain domain)
    {
        if (domain.TenantId is null && !tenant.IsPlatformAdmin)
            throw new ForbiddenException("Los catálogos globales solo los edita el administrador de plataforma; use overrides o cree una lista propia.");
        if (domain.TenantId is not null && domain.TenantId != tenant.TenantId && !tenant.IsPlatformAdmin)
            throw new ForbiddenException();
    }

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException("code", "El código es obligatorio.");
        var n = Regex.Replace(code.Trim().ToUpperInvariant(), "[^A-Z0-9_]+", "_").Trim('_');
        if (n.Length is 0 or > 40) throw new ValidationException("code", "Código inválido (máx. 40 caracteres alfanuméricos).");
        return n;
    }

    private CatalogDomainDto ToDto(CatalogDomain d) => new(
        d.CatalogDomainId, d.DomainKey, d.Scope, MultilingualText.Resolve(d.LabelJson, tenant.Lang), MultilingualText.Parse(d.LabelJson),
        d.Description, d.IsSystem, d.IsActive, d.TenantId);

    private LookupValueDto ToDto(LookupCode c, LookupCodeOverride? o)
    {
        var labelJson = MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson);
        return new LookupValueDto(
            c.LookupCodeId, c.Entity, c.InternalCode, MultilingualText.Resolve(labelJson, tenant.Lang), MultilingualText.Parse(labelJson),
            MultilingualText.Resolve(c.DescriptionJson, tenant.Lang), o?.CustomExtraJson ?? c.ExtraJson,
            o?.SortOverride ?? c.SortOrder, c.IsSystem, o?.IsEnabled ?? true, o is not null, c.TenantId);
    }
}
