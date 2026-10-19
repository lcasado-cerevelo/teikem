using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lo que se necesita de una orden para evaluar su SLA.</summary>
public sealed record SlaOrderInput(int OrderId, int? ContractId, int ServiceTypeLookupId, DateTime CreatedAtUtc, DateTime? ConfirmedAtUtc, string? StatusCode);

/// <summary>
/// Lote 122 (P12) — evalúa el SLA de muchas órdenes con pocas consultas: el nivel de servicio activo del contrato por tipo de servicio (<c>ContractServiceLevel.MaxTransitHours</c>) y, del historial de estatus, cuándo se recogió y cuándo
/// se entregó cada una. Las reglas están en <see cref="SlaRules"/>. Lo usan la fuente de datos de órdenes (campos <c>SlaState</c>, <c>SlaDueUtc</c>, <c>SlaHoursLeft</c>, <c>SlaMaxHours</c>) y el aviso «SLA en riesgo» del Pulso.
/// </summary>
public sealed class SlaService(TeikemDbContext db, ILookupCache lookups)
{
    public async Task<Dictionary<int, SlaResult>> EvaluateAsync(IReadOnlyCollection<SlaOrderInput> orders, DateTime nowUtc, CancellationToken ct)
    {
        var result = new Dictionary<int, SlaResult>();
        if (orders.Count == 0) return result;
        var contractIds = orders.Where(o => o.ContractId != null).Select(o => o.ContractId!.Value).Distinct().ToList();
        var levels = contractIds.Count == 0
            ? new Dictionary<(int, int), int?>()
            : (await db.Set<ContractServiceLevel>().AsNoTracking().Where(l => l.IsActive && contractIds.Contains(l.ContractId)).ToListAsync(ct))
                .GroupBy(l => (l.ContractId, l.ServiceTypeLookupId)).ToDictionary(g => g.Key, g => g.First().MaxTransitHours);

        // cuándo se recogió y cuándo se entregó, del historial de estatus (solo de las que lo necesitan)
        var needHistory = orders.Where(o => o.ContractId != null && levels.ContainsKey((o.ContractId.Value, o.ServiceTypeLookupId))).Select(o => o.OrderId).ToList();
        var pickedAt = new Dictionary<int, DateTime>();
        var deliveredAt = new Dictionary<int, DateTime>();
        if (needHistory.Count > 0)
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
            var codes = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && (s.InternalCode == OrderStatuses.Pickup || s.InternalCode == OrderStatuses.Delivered))
                .ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
            var ids = codes.Keys.ToList();
            var rows = await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && needHistory.Contains(h.EntityId) && ids.Contains(h.ToStatusCodeId))
                .Select(h => new { h.EntityId, h.ToStatusCodeId, h.ChangedAtUtc }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.EntityId))
            {
                var pick = g.Where(r => codes[r.ToStatusCodeId] == OrderStatuses.Pickup).Select(r => (DateTime?)r.ChangedAtUtc).Min();
                var done = g.Where(r => codes[r.ToStatusCodeId] == OrderStatuses.Delivered).Select(r => (DateTime?)r.ChangedAtUtc).Max();
                if (pick is DateTime p) pickedAt[g.Key] = p;
                if (done is DateTime d) deliveredAt[g.Key] = d;
            }
        }

        foreach (var o in orders)
        {
            if (o.ContractId is not int cid || !levels.TryGetValue((cid, o.ServiceTypeLookupId), out var max)) { result[o.OrderId] = SlaResult.None; continue; }
            DateTime? start = pickedAt.TryGetValue(o.OrderId, out var p) ? p : o.ConfirmedAtUtc;
            if (start is null && string.Equals(o.StatusCode, OrderStatuses.Delivered, StringComparison.OrdinalIgnoreCase)) start = o.CreatedAtUtc;
            result[o.OrderId] = SlaRules.Evaluate(max, start, deliveredAt.TryGetValue(o.OrderId, out var d) ? d : null, o.StatusCode, nowUtc);
        }
        return result;
    }
}
