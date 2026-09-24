using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Módulo 0B: catálogo de módulos y encendido por tenant. Ausencia de fila = apagado (default seguro).
/// Encender exige la dependencia encendida; apagar apaga en cascada los dependientes; los núcleo no se apagan.
/// </summary>
public sealed class ModuleService(TeikemDbContext db, ITenantContext tenant, IMemoryCache cache)
{
    private static string CacheKey(int tenantId) => $"modules:{tenantId}";

    public async Task<IReadOnlySet<string>> GetEnabledKeysAsync(int tenantId, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(tenantId), out HashSet<string>? set) && set is not null) return set;
        var keys = await db.TenantModules.AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.IsEnabled && m.Module!.IsActive).Select(m => m.ModuleKey).ToListAsync(ct);
        set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        cache.Set(CacheKey(tenantId), set, TimeSpan.FromMinutes(5));
        return set;
    }

    public async Task<bool> IsEnabledAsync(string moduleKey, CancellationToken ct)
    {
        if (tenant.TenantId is null) return false;
        return (await GetEnabledKeysAsync(tenant.TenantId.Value, ct)).Contains(moduleKey);
    }

    public async Task EnsureEnabledAsync(string moduleKey, CancellationToken ct)
    {
        if (!await IsEnabledAsync(moduleKey, ct)) throw new ModuleDisabledException(moduleKey);
    }

    public async Task<IReadOnlyList<ModuleDto>> GetCatalogAsync(CancellationToken ct)
    {
        var defs = await db.ModuleDefinitions.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToListAsync(ct);
        var enabled = await db.TenantModules.AsNoTracking().ToDictionaryAsync(m => m.ModuleKey, ct);
        var lang = tenant.Lang;
        return defs.Select(d =>
        {
            enabled.TryGetValue(d.ModuleKey, out var tm);
            return new ModuleDto(d.ModuleKey, lang == "en" ? d.NameEn : d.Name, d.Description, d.Category, d.DependsOnModuleKey, d.SortOrder, d.IsCore,
                tm?.IsEnabled ?? false, tm?.EnabledAtUtc);
        }).ToList();
    }

    public async Task<IReadOnlyList<ModuleDto>> SetEnabledAsync(string moduleKey, bool enabled, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var defs = await db.ModuleDefinitions.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
        var def = defs.FirstOrDefault(m => m.ModuleKey.Equals(moduleKey, StringComparison.OrdinalIgnoreCase)) ?? throw new NotFoundException("Módulo", moduleKey);
        var rows = await db.TenantModules.ToListAsync(ct);

        if (enabled)
        {
            if (def.DependsOnModuleKey is not null && !(rows.FirstOrDefault(r => r.ModuleKey == def.DependsOnModuleKey)?.IsEnabled ?? false))
                throw new ConflictException($"'{def.ModuleKey}' depende de '{def.DependsOnModuleKey}', que debe encenderse primero.");
            Upsert(rows, tenantId, def.ModuleKey, true);
        }
        else
        {
            if (def.IsCore) throw new ConflictException($"'{def.ModuleKey}' es un módulo núcleo y no se puede apagar.");
            Upsert(rows, tenantId, def.ModuleKey, false);
            // cascada: apagar dependientes
            var stack = new Stack<string>(new[] { def.ModuleKey });
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                foreach (var dep in defs.Where(m => m.DependsOnModuleKey == k))
                {
                    if (rows.FirstOrDefault(r => r.ModuleKey == dep.ModuleKey)?.IsEnabled ?? false) Upsert(rows, tenantId, dep.ModuleKey, false);
                    stack.Push(dep.ModuleKey);
                }
            }
        }
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey(tenantId));
        return await GetCatalogAsync(ct);
    }

    private void Upsert(List<TenantModule> rows, int tenantId, string key, bool enabled)
    {
        var row = rows.FirstOrDefault(r => r.ModuleKey == key);
        if (row is null)
        {
            row = new TenantModule { TenantId = tenantId, ModuleKey = key };
            db.TenantModules.Add(row);
            rows.Add(row);
        }
        row.IsEnabled = enabled;
        row.EnabledAtUtc = DateTime.UtcNow;
        row.EnabledBy = tenant.UserId;
    }

    public void Invalidate(int tenantId) => cache.Remove(CacheKey(tenantId));
}
