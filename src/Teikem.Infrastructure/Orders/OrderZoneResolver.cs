using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Trips;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// Lote 129 — la zona de despacho de una parada de entrega (ZIP y pueblo contra los miembros de las zonas activas, con DispatchZoneMatcher): la orden la guarda en DeliveryZoneId al crearse o al
/// cambiar su consignatario, y de ahí sale su nivel (Isla/Metro). Ambigua o sin coincidencia = sin zona (null).
/// </summary>
public static class OrderZoneResolver
{
    public static int? Resolve(IReadOnlyList<ZoneMember> members, IReadOnlyDictionary<string, int> zoneIdsByCode, string? postalCode, string? city)
    {
        var pc = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
        var town = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        if (pc is null && town is null) return null;
        var r = DispatchZoneMatcher.Resolve(members, pc, town);
        if (r.Ambiguous || string.IsNullOrEmpty(r.ZoneCode)) return null;
        return zoneIdsByCode.TryGetValue(r.ZoneCode, out var id) ? id : null;
    }

    public static async Task<int?> ResolveAsync(TeikemDbContext db, string? postalCode, string? city, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(postalCode) && string.IsNullOrWhiteSpace(city)) return null;
        var members = await db.ActiveZoneMembersAsync(ct);
        if (members.Count == 0) return null;
        var codes = await db.DispatchZones.AsNoTracking().Where(z => z.IsActive).ToDictionaryAsync(z => z.Code, z => z.DispatchZoneId, StringComparer.OrdinalIgnoreCase, ct);
        return Resolve(members, codes, postalCode, city);
    }
}
