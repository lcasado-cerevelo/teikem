using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lecturas comunes de la rotación (2026-10-20): los dos métodos de la compañía (con lote / sin lote) y cuáles productos son por lote. Quien asigna una salida
/// (recolección, orden de salida, reserva de ventas, reabasto) las junta con la excepción de cada producto en <see cref="RotationRules.Resolve(string?, CompanyRotation, bool)"/>.
/// </summary>
public static class RotationQueries
{
    public static async Task<CompanyRotation> CompanyAsync(TeikemDbContext db, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().Where(x => x.TenantId == db.CurrentTenantId)
            .Select(x => new { x.RotationMethod, x.RotationMethodNoLot }).FirstOrDefaultAsync(ct);
        return new CompanyRotation(t?.RotationMethod, t?.RotationMethodNoLot);
    }

    /// <summary>Los productos (de los pedidos) que se controlan POR LOTE; los demás (sin seguimiento o por serie) usan el método de «sin lote».</summary>
    public static async Task<HashSet<int>> LotTrackedAsync(TeikemDbContext db, IReadOnlyCollection<int> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return new HashSet<int>();
        var ids = await (from p in db.Set<Product>().AsNoTracking()
                         join l in db.LookupCodes.AsNoTracking() on p.TrackingTypeLookupId equals l.LookupCodeId
                         where productIds.Contains(p.ProductId) && l.InternalCode == TrackingTypes.Lot
                         select p.ProductId).ToListAsync(ct);
        return ids.ToHashSet();
    }
}
