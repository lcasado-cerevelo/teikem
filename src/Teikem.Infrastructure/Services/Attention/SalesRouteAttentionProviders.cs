using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — fila de «Necesita tu atención» por cada RUTA DE VENTA activa sin titular vigente (reason NO_PRIMARY) o cuyo titular vigente ya no es
/// persona activa de la compañía (usuario inactivo, membresía suspendida o invitada; reason INACTIVE_PRIMARY). La ve quien administra el análisis de ventas
/// (sales.analysis.manage) con el módulo de órdenes de venta. Solo señala: no bloquea solicitar órdenes ni los avisos (el vendedor propuesto cae en la cadena).
/// Tono warn; danger si la ruta tiene clientes activos (su ruta propia o, si no tienen, la de su representante). Primero las que tienen clientes, luego por orden y nombre.
/// Params: routeId, name, clients, reason. «Revisar» abre Configuración › Rutas del análisis de ventas. Se calcula al leer, sin tabla.
/// </summary>
public sealed class SalesRouteNoPrimaryAttentionProvider(TeikemDbContext db, ITenantClock? clock = null) : IAttentionItemProvider
{
    public const string ItemCode = SalesRouteCodes.NoPrimaryAttentionCode;
    public const string Route = "/analytics/sales";
    public const string ReasonNoPrimary = "NO_PRIMARY";
    public const string ReasonInactivePrimary = "INACTIVE_PRIMARY";

    private static readonly IReadOnlyDictionary<string, string> RouteQuery = new Dictionary<string, string> { ["tab"] = "setup", ["sec"] = "routes" };

    public string Code => ItemCode;
    public string BusinessModule => BusinessModules.Accounting;
    public string? TenantModule => ModuleKeys.SalesOrders;
    public string RequiredPermission => PermissionCatalog.SalesAnalysisManage;

    public async Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct)
    {
        var tenantId = scope.TenantId;
        var routes = await db.Set<SalesRoute>().AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive)
            .Select(r => new { r.SalesRouteId, r.Name, r.SortOrder }).ToListAsync(ct);
        if (routes.Count == 0) return Empty;

        // Titulares vigentes (activos y sin cierre) de la compañía
        var primaries = (await db.Set<SalesRouteAssignment>().AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.IsActive && a.IsPrimary && a.ValidTo == null)
                .Select(a => new { a.SalesRouteId, a.UserId, a.ValidFrom }).ToListAsync(ct))
            .GroupBy(a => a.SalesRouteId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.ValidFrom).First().UserId);
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tenantId, primaries.Values, ct);

        var flagged = routes
            .Select(r => new
            {
                Route = r,
                Reason = !primaries.TryGetValue(r.SalesRouteId, out var u) ? ReasonNoPrimary : active.Contains(u) ? null : ReasonInactivePrimary,
            })
            .Where(x => x.Reason is not null).ToList();
        if (flagged.Count == 0) return Empty;

        var clients = await ClientsByEffectiveRouteAsync(tenantId, ct);
        // Desde cuándo falta el titular: el cierre del último titular (no anulado) de las rutas sin titular; sin dato (nunca tuvo, o titular inactivo) = sin fecha.
        var noPrimaryIds = flagged.Where(x => x.Reason == ReasonNoPrimary).Select(x => x.Route.SalesRouteId).ToList();
        var lastClosed = noPrimaryIds.Count == 0 ? new Dictionary<int, DateOnly>()
            : (await db.Set<SalesRouteAssignment>().AsNoTracking()
                    .Where(a => a.TenantId == tenantId && a.IsActive && a.IsPrimary && a.ValidTo != null && noPrimaryIds.Contains(a.SalesRouteId))
                    .Select(a => new { a.SalesRouteId, a.ValidTo }).ToListAsync(ct))
                .GroupBy(a => a.SalesRouteId).ToDictionary(g => g.Key, g => g.Max(a => a.ValidTo!.Value));
        var c = clock ?? TenantClock.Default;

        var rows = flagged
            .Select(x => new { x.Route, x.Reason, Clients = clients.GetValueOrDefault(x.Route.SalesRouteId) })
            .OrderBy(x => x.Clients > 0 ? 0 : 1).ThenBy(x => x.Route.SortOrder).ThenBy(x => x.Route.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Route.SalesRouteId)
            .ToList();
        var items = rows.Take(Math.Max(0, take)).Select(x => new AttentionItemDto(ItemCode, BusinessModules.Accounting, x.Clients > 0 ? "danger" : "warn", 1,
            new Dictionary<string, string>
            {
                ["routeId"] = x.Route.SalesRouteId.ToString(CultureInfo.InvariantCulture),
                ["name"] = x.Route.Name,
                ["clients"] = x.Clients.ToString(CultureInfo.InvariantCulture),
                ["reason"] = x.Reason!,
            },
            Route, RouteQuery, lastClosed.TryGetValue(x.Route.SalesRouteId, out var since) ? (DateTime?)c.StartOfDayUtc(since) : null)).ToList();
        return new AttentionProviderResult(rows.Count, items, new AttentionGroupDto(ItemCode, BusinessModules.Accounting, rows.Count, Route, RouteQuery));
    }

    private static AttentionProviderResult Empty => new(0, Array.Empty<AttentionItemDto>(), null);

    /// <summary>Clientes activos por ruta efectiva: su ruta propia o, si no tienen, la de su representante (la misma regla que la lista de rutas de Configuración).</summary>
    private async Task<Dictionary<int, int>> ClientsByEffectiveRouteAsync(int tenantId, CancellationToken ct)
    {
        var repRoute = await db.Set<SalesRep>().AsNoTracking().Where(r => r.TenantId == tenantId && r.SalesRouteId != null)
            .ToDictionaryAsync(r => r.SalesRepId, r => r.SalesRouteId!.Value, ct);
        var clients = await db.Clients.AsNoTracking().Where(cl => cl.TenantId == tenantId && cl.IsActive && (cl.SalesRouteId != null || cl.SalesRepId != null))
            .Select(cl => new { cl.SalesRouteId, cl.SalesRepId }).ToListAsync(ct);
        return clients
            .Select(cl => cl.SalesRouteId ?? (cl.SalesRepId is int r && repRoute.TryGetValue(r, out var rr) ? rr : (int?)null))
            .Where(r => r is not null).GroupBy(r => r!.Value).ToDictionary(g => g.Key, g => g.Count());
    }
}
