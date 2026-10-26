using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-26 — «¿la compañía sigue activa?» para cortar de inmediato a quien ya tenía una sesión cuando la plataforma desactiva su compañía (el login y el cambio de compañía ya lo validan).
/// Se recuerda unos segundos por compañía para no consultar la base en cada petición; al activar o desactivar se olvida al instante en este servidor.
/// </summary>
public static class TenantStatusGuard
{
    public const string InactiveMessage = "La compañía está inactiva.";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);
    private static readonly ConcurrentDictionary<int, (bool Active, DateTime AtUtc)> Cache = new();

    public static void Invalidate(int tenantId) => Cache.TryRemove(tenantId, out _);

    public static async Task<bool> IsActiveAsync(TeikemDbContext db, int tenantId, CancellationToken ct)
    {
        if (Cache.TryGetValue(tenantId, out var hit) && DateTime.UtcNow - hit.AtUtc < Ttl) return hit.Active;
        var active = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => (bool?)t.IsActive).FirstOrDefaultAsync(ct) ?? true;
        Cache[tenantId] = (active, DateTime.UtcNow);
        return active;
    }
}
