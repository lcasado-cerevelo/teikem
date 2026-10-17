using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Una orden a la que se le calcula el avance: su id y el código de su estatus actual.</summary>
public sealed record OrderProgressInput(int OrderId, string StatusCode);

/// <summary>
/// Lote 103 — calcula el avance (la barra de progreso) de las órdenes de la compañía activa a partir de SUS estatus: las etapas son el camino normal de la compañía (con sus nombres propios) y el avance de una orden
/// detenida (lateral) sale del último estatus normal de su historial. Lee los estatus una vez por petición. Lo usan el portal del cliente y la página pública.
/// </summary>
public sealed class OrderProgressService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups)
{
    private List<ProgressStatus>? _all;

    /// <summary>Todos los estatus de orden de la compañía (con su nombre propio y si están habilitados).</summary>
    public async Task<IReadOnlyList<ProgressStatus>> StatusesAsync(CancellationToken ct)
    {
        if (_all is not null) return _all;
        var codes = await db.StatusCodes.AsNoTracking().Include(s => s.StageKind).Where(s => s.Entity == StatusDomains.OrderStatus && s.IsActive).ToListAsync(ct);
        var ids = codes.Select(c => c.StatusCodeId).ToList();
        var overrides = tenant.TenantId is null
            ? new Dictionary<int, Teikem.Domain.Catalogs.StatusCodeOverride>()
            : await db.StatusCodeOverrides.AsNoTracking().Where(o => ids.Contains(o.StatusCodeId)).ToDictionaryAsync(o => o.StatusCodeId, ct);
        _all = codes.Select(c =>
        {
            overrides.TryGetValue(c.StatusCodeId, out var o);
            return new ProgressStatus(c.InternalCode, MultilingualText.Resolve(MultilingualText.Merge(c.LabelJson, o?.CustomLabelJson), tenant.Lang), c.StageKind?.InternalCode ?? string.Empty, o?.SortOverride ?? c.SortOrder, o?.IsEnabled ?? true);
        }).ToList();
        return _all;
    }

    /// <summary>Las etapas de la barra: el camino normal de la compañía.</summary>
    public async Task<IReadOnlyList<ProgressStatus>> StepsAsync(CancellationToken ct) => OrderProgress.StepsOf(await StatusesAsync(ct));

    /// <summary>El avance de cada orden (id → avance). Las detenidas consultan su historial en una sola lectura.</summary>
    public async Task<Dictionary<int, OrderProgressResult>> ComputeAsync(IEnumerable<OrderProgressInput> orders, CancellationToken ct)
    {
        var list = orders.ToList();
        var all = await StatusesAsync(ct);
        var steps = OrderProgress.StepsOf(all);
        var byCode = all.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var stepCodes = steps.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // las que no están en una etapa (laterales y canceladas) necesitan su historial para dejar la barra donde iba
        var needHistory = list.Where(o => !stepCodes.Contains(o.StatusCode)).Select(o => o.OrderId).Distinct().ToList();
        var history = new Dictionary<int, List<string>>();
        if (needHistory.Count > 0)
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
            var rows = await db.EntityStatusHistories.AsNoTracking().Include(h => h.ToStatus)
                .Where(h => h.EntityTypeLookupId == typeId && needHistory.Contains(h.EntityId))
                .OrderByDescending(h => h.ChangedAtUtc).ThenByDescending(h => h.EntityStatusHistoryId).ToListAsync(ct);
            history = rows.GroupBy(h => h.EntityId).ToDictionary(g => g.Key, g => g.Select(h => h.ToStatus?.InternalCode ?? string.Empty).ToList());
        }

        var result = new Dictionary<int, OrderProgressResult>();
        foreach (var o in list)
        {
            byCode.TryGetValue(o.StatusCode, out var current);
            result[o.OrderId] = OrderProgress.Compute(steps, o.StatusCode, current?.Label ?? o.StatusCode, current?.Kind, history.GetValueOrDefault(o.OrderId));
        }
        return result;
    }

    /// <summary>Cuándo fue el último cambio de estatus de la orden.</summary>
    public async Task<DateTime?> LastMovementAsync(int orderId, CancellationToken ct)
    {
        var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        return await db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityTypeLookupId == typeId && h.EntityId == orderId).Select(h => (DateTime?)h.ChangedAtUtc).MaxAsync(ct);
    }
}
