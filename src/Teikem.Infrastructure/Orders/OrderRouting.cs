using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Orders;

/// <summary>
/// 2026-10-22 (dictado de Luis) — a qué dirección va el chofer. Un envío de «ida y vuelta» (marca <c>roundTrip</c> del tipo de servicio) se recoge en la
/// tienda y se trae de vuelta: lo que se visita es la RECOGIDA, así que la zona del envío, su parada en la ruta y el mapa salen de la parada PICKUP.
/// Todo lo demás se ruta por la parada DELIVERY, como siempre.
/// </summary>
public static class OrderRouting
{
    /// <summary>Ids de los tipos de servicio del tenant marcados «ida y vuelta» (el valor global y, encima, el ajuste de la compañía). Casi siempre vacío o de uno.</summary>
    public static async Task<HashSet<int>> RoundTripServiceTypeIdsAsync(TeikemDbContext db, CancellationToken ct)
    {
        var rows = await db.LookupCodes.AsNoTracking().Where(l => l.Entity == LookupDomains.ServiceType && l.IsActive)
            .Select(l => new { l.LookupCodeId, l.ExtraJson }).ToListAsync(ct);
        var overrides = await db.LookupCodeOverrides.AsNoTracking().Where(o => o.CustomExtraJson != null)
            .Select(o => new { o.LookupCodeId, o.CustomExtraJson }).ToListAsync(ct);
        var customById = overrides.GroupBy(o => o.LookupCodeId).ToDictionary(g => g.Key, g => g.First().CustomExtraJson);
        var result = new HashSet<int>();
        foreach (var r in rows)
            if (ServiceTypeMarks.Parse(r.ExtraJson).Apply(customById.GetValueOrDefault(r.LookupCodeId)).RoundTrip) result.Add(r.LookupCodeId);
        return result;
    }

    /// <summary>La parada que el chofer visita: PICKUP si el envío es de ida y vuelta (y la tiene); si no, DELIVERY.</summary>
    public static OrderStop? RoutingStop(IEnumerable<OrderStop> stops, bool roundTrip, int pickupTypeId, int deliveryTypeId)
    {
        var list = stops as IReadOnlyCollection<OrderStop> ?? stops.ToList();
        if (roundTrip && list.Where(s => s.StopTypeLookupId == pickupTypeId).OrderBy(s => s.Sequence).FirstOrDefault() is { } pickup) return pickup;
        return list.Where(s => s.StopTypeLookupId == deliveryTypeId).OrderBy(s => s.Sequence).FirstOrDefault();
    }

    /// <summary>Zona de despacho del envío: por el ZIP/pueblo de la parada que se visita (recogida si es de ida y vuelta, entrega si no).</summary>
    public static async Task<int?> ResolveZoneAsync(TeikemDbContext db, int serviceTypeLookupId, OrderStop? pickupStop, OrderStop deliveryStop, CancellationToken ct)
    {
        var roundTrip = pickupStop is not null && (await RoundTripServiceTypeIdsAsync(db, ct)).Contains(serviceTypeLookupId);
        var stop = roundTrip ? pickupStop! : deliveryStop;
        return await OrderZoneResolver.ResolveAsync(db, stop.SnapPostalCode, stop.SnapCity, ct);
    }
}
