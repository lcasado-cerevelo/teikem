using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 89 (P12) — Radar de órdenes abiertas del Pulso. Cuenta las órdenes en curso (confirmadas, por recoger, en almacén, planificadas, en ruta, en destino, en espera o parciales) y devuelve los grupos con más
/// órdenes, por pueblo del consignatario (la parada de entrega) o por cliente, con el COD que llevan. La búsqueda de una orden concreta usa el listado de Órdenes (`search` + `statuses`), no este servicio.
/// </summary>
public sealed class PulseRadarService(TeikemDbContext db, ILookupCache lookups)
{
    public async Task<PulseRadarDto> RadarAsync(string? groupBy, int take, CancellationToken ct)
    {
        var by = RadarRules.NormalizeGroupBy(groupBy) ?? throw new ValidationException("groupBy", RadarRules.GroupByInvalid);
        take = RadarRules.ClampTake(take);
        var openIds = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && RadarRules.OpenStatuses.Contains(s.InternalCode)).Select(s => s.StatusCodeId).ToListAsync(ct);
        var open = db.TransportOrders.AsNoTracking().Where(o => o.IsActive && openIds.Contains(o.StatusCodeId));
        var openCount = await open.CountAsync(ct);
        var codTotal = await open.SumAsync(o => (decimal?)o.CodAmount, ct) ?? 0m;

        List<PulseRadarGroupDto> groups;
        if (by == RadarRules.GroupByTown)
        {
            var deliveryType = await lookups.GetIdAsync(LookupDomains.StopType, StopTypes.Delivery, ct);
            var rows = await (from o in open
                              join s in db.Set<OrderStop>().AsNoTracking() on o.TransportOrderId equals s.TransportOrderId
                              where s.StopTypeLookupId == deliveryType
                              group o by s.SnapCity into g
                              select new { Town = g.Key, Count = g.Count(), Cod = g.Sum(x => (decimal?)x.CodAmount) ?? 0m })
                .OrderByDescending(g => g.Count).ThenBy(g => g.Town).Take(take).ToListAsync(ct);
            groups = rows.Select(r => new PulseRadarGroupDto(r.Town, string.IsNullOrWhiteSpace(r.Town) ? "—" : r.Town, r.Count, r.Cod)).ToList();
        }
        else
        {
            var rows = await open.GroupBy(o => o.ClientId).Select(g => new { ClientId = g.Key, Count = g.Count(), Cod = g.Sum(x => (decimal?)x.CodAmount) ?? 0m })
                .OrderByDescending(g => g.Count).ThenBy(g => g.ClientId).Take(take).ToListAsync(ct);
            var ids = rows.Select(r => r.ClientId).ToList();
            var clients = await db.Set<Client>().AsNoTracking().Where(c => ids.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new { c.PublicId, c.Name }, ct);
            groups = rows.Select(r => new PulseRadarGroupDto(clients[r.ClientId].PublicId.ToString(), clients[r.ClientId].Name, r.Count, r.Cod)).ToList();
        }
        return new PulseRadarDto(by, openCount, codTotal, RadarRules.OpenStatuses, groups);
    }
}
