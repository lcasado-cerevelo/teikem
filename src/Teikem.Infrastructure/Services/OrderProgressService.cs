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

    /// <summary>Las cuatro etapas de la barra (Sometida, Recibida, En ruta, Completada) en el idioma de quien mira.</summary>
    public Task<IReadOnlyList<ProgressStatus>> StepsAsync(CancellationToken ct) => Task.FromResult(OrderProgress.Steps(tenant.Lang));

    /// <summary>El avance de cada orden (id → avance). Las que están Sin asignar o Asignadas consultan, en una sola lectura, si alguna vez tuvieron un Intento fallido (entonces son «Reprogramadas»).</summary>
    public async Task<Dictionary<int, OrderProgressResult>> ComputeAsync(IEnumerable<OrderProgressInput> orders, CancellationToken ct)
    {
        var list = orders.ToList();
        var all = await StatusesAsync(ct);
        var byCode = all.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

        var maybeRescheduled = list.Where(o => string.Equals(o.StatusCode, OrderStatuses.Unassigned, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(o.StatusCode, OrderStatuses.Assigned, StringComparison.OrdinalIgnoreCase)).Select(o => o.OrderId).Distinct().ToList();
        var failedBefore = new HashSet<int>();
        if (maybeRescheduled.Count > 0)
        {
            var typeId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
            var ids = await db.EntityStatusHistories.AsNoTracking()
                .Where(h => h.EntityTypeLookupId == typeId && maybeRescheduled.Contains(h.EntityId) && h.ToStatus!.InternalCode == OrderStatuses.Failed)
                .Select(h => h.EntityId).Distinct().ToListAsync(ct);
            failedBefore = ids.ToHashSet();
        }

        var result = new Dictionary<int, OrderProgressResult>();
        foreach (var o in list)
        {
            byCode.TryGetValue(o.StatusCode, out var current);
            result[o.OrderId] = OrderProgress.Compute(o.StatusCode, current?.Label ?? o.StatusCode, current?.Kind, failedBefore.Contains(o.OrderId), tenant.Lang);
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
