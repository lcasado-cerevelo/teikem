using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Persistence;

/// <summary>Caché singleton de LookupCode (todos los tenants; el filtrado por tenant lo hace ILookupService).</summary>
public sealed class LookupCache(IServiceScopeFactory scopes, IMemoryCache cache) : ILookupCache
{
    private const string Key = "lookups:all";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private sealed record Snapshot(
        Dictionary<(string Entity, string Code), LookupCode> ByCode,
        Dictionary<int, LookupCode> ById,
        ILookup<string, LookupCode> ByEntity);

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(Key, out Snapshot? s) && s is not null) return s;
        await Gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(Key, out s) && s is not null) return s;
            using var scope = scopes.CreateScope();
            var tc = (TenantContext)scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var _ = tc.BypassTenantFilter();
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var all = await db.LookupCodes.AsNoTracking().ToListAsync(ct);
            var byCode = new Dictionary<(string, string), LookupCode>();
            foreach (var l in all) byCode[(l.Entity.ToUpperInvariant(), l.InternalCode.ToUpperInvariant())] = l;
            s = new Snapshot(byCode, all.ToDictionary(l => l.LookupCodeId), all.ToLookup(l => l.Entity.ToUpperInvariant()));
            cache.Set(Key, s, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(30) });
            return s;
        }
        finally { Gate.Release(); }
    }

    public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default)
        => await TryGetIdAsync(entity, code, ct) ?? throw new NotFoundException($"Catálogo {entity}", code);

    public async Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
    {
        var s = await LoadAsync(ct);
        return s.ByCode.TryGetValue((entity.ToUpperInvariant(), code.ToUpperInvariant()), out var l) ? l.LookupCodeId : null;
    }

    public async Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
        => (await LoadAsync(ct)).ById.GetValueOrDefault(lookupCodeId);

    public async Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
        => (await LoadAsync(ct)).ByEntity[entity.ToUpperInvariant()].OrderBy(l => l.SortOrder).ThenBy(l => l.InternalCode).ToList();

    public void Invalidate() => cache.Remove(Key);
}
