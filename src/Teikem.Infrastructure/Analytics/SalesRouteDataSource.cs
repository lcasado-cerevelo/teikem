using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — fuente SALES_ROUTE, «Rutas de venta»: una fila por ruta de venta de la compañía (activas e inactivas) con su estado ACTUAL (sin
/// rango de fecha): el titular vigente (nombre, si hay y si sigue siendo persona activa), los apoyos vigentes (cuántos y sus nombres), cuántos clientes activos tiene
/// (su ruta propia o, si no tienen, la de su representante; la misma regla que Configuración › Rutas) y su cuota de ruta vigente del mes en curso (SalesQuota con
/// SalesRouteId, con la herencia mensual de <see cref="SalesAnalysisRules.EffectiveQuota"/>). Sirve, p. ej., al indicador «rutas sin titular» (IsActive y no HasPrimary).
/// Filtro de tenant (además del global, el TenantId de la sesión) y tope ClientDataSourceHelpers.MaxRows. Lectura: sales.analysis.view y el módulo Órdenes de venta.
/// </summary>
public sealed class SalesRouteDataSource(TeikemDbContext db, ITenantContext tenant, ITenantClock? clock = null) : IDataSource
{
    public string Key => SalesRouteCodes.RouteDataSourceKey;
    public string LabelEs => "Rutas de venta";
    public string LabelEn => "Sales routes";
    public string? EntityTypeCode => null;
    public string? DateField => null;
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("Name", "Ruta de venta", "Sales route", DataFieldType.Text),
        new DataField("IsActive", "Activa", "Active", DataFieldType.Bool),
        new DataField("SortOrder", "Orden", "Sort order", DataFieldType.Number),
        new DataField("PrimaryName", "Titular", "Primary", DataFieldType.Text),
        new DataField("HasPrimary", "Con titular", "Has primary", DataFieldType.Bool),
        new DataField("PrimaryIsActiveMember", "Titular activo", "Primary is active", DataFieldType.Bool),
        new DataField("SupportCount", "Apoyos", "Support people", DataFieldType.Number),
        new DataField("SupportNames", "Nombres de los apoyos", "Support names", DataFieldType.Text),
        new DataField("ClientCount", "Clientes", "Clients", DataFieldType.Number),
        new DataField("CurrentQuota", "Cuota vigente", "Current quota", DataFieldType.Number, IsMoney: true),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = Array.Empty<DataRelation>();

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        if (tenant.TenantId is not int tenantId) return new List<DataRow>();
        var query = db.Set<SalesRoute>().AsNoTracking().Where(r => r.TenantId == tenantId);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(r => wanted.Contains(r.SalesRouteId));
        }
        var routes = (await query.OrderBy(r => r.SortOrder).ThenBy(r => r.SalesRouteId).Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct))
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (routes.Count == 0) return new List<DataRow>();
        var ids = routes.Select(r => r.SalesRouteId).ToList();

        // Titular y apoyos VIGENTES (activos y sin cierre)
        var people = (await db.Set<SalesRouteAssignment>().AsNoTracking()
                .Where(a => a.TenantId == tenantId && ids.Contains(a.SalesRouteId) && a.IsActive && a.ValidTo == null)
                .Select(a => new { a.SalesRouteId, a.UserId, a.IsPrimary, a.ValidFrom, a.SalesRouteAssignmentId }).ToListAsync(ct))
            .GroupBy(a => a.SalesRouteId).ToDictionary(g => g.Key, g => g.ToList());
        var userIds = people.Values.SelectMany(l => l.Select(a => a.UserId)).Distinct().ToList();
        var names = await AssigneeCheck.NamesAsync(db, userIds, ct);
        var primaryIds = people.Values.SelectMany(l => l.Where(a => a.IsPrimary).Select(a => a.UserId)).Distinct();
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tenantId, primaryIds, ct);

        var clients = await ClientsByEffectiveRouteAsync(tenantId, ct);
        var month = SalesAnalysisRules.MonthStart(ClientDataSourceHelpers.Today(clock));
        var quotas = (await db.Set<SalesQuota>().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive && x.SalesRouteId != null && ids.Contains(x.SalesRouteId!.Value) && x.Month <= month)
                .Select(x => new { x.SalesRouteId, x.Month, x.Amount }).ToListAsync(ct))
            .GroupBy(x => x.SalesRouteId!.Value)
            .ToDictionary(g => g.Key, g => SalesAnalysisRules.EffectiveQuota(g.Select(x => (x.Month, x.Amount)), month));

        var rows = new List<DataRow>(routes.Count);
        foreach (var r in routes)
        {
            var list = people.GetValueOrDefault(r.SalesRouteId);
            var primary = list?.Where(a => a.IsPrimary).OrderByDescending(a => a.ValidFrom).FirstOrDefault();
            var supports = list?.Where(a => !a.IsPrimary).Select(a => names.GetValueOrDefault(a.UserId) ?? string.Empty)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList() ?? new List<string>();
            rows.Add(new DataRow
            {
                ["Id"] = r.SalesRouteId,
                ["Name"] = r.Name,
                ["IsActive"] = r.IsActive,
                ["SortOrder"] = r.SortOrder,
                ["PrimaryName"] = primary is null ? null : names.GetValueOrDefault(primary.UserId),
                ["HasPrimary"] = primary is not null,
                ["PrimaryIsActiveMember"] = primary is not null && active.Contains(primary.UserId),
                ["SupportCount"] = supports.Count,
                ["SupportNames"] = supports.Count == 0 ? null : string.Join(", ", supports),
                ["ClientCount"] = clients.GetValueOrDefault(r.SalesRouteId),
                ["CurrentQuota"] = quotas.GetValueOrDefault(r.SalesRouteId),
            });
        }
        return rows;
    }

    /// <summary>Clientes activos por ruta efectiva: su ruta propia o, si no tienen, la de su representante.</summary>
    private async Task<Dictionary<int, int>> ClientsByEffectiveRouteAsync(int tenantId, CancellationToken ct)
    {
        var repRoute = await db.Set<SalesRep>().AsNoTracking().Where(r => r.TenantId == tenantId && r.SalesRouteId != null)
            .ToDictionaryAsync(r => r.SalesRepId, r => r.SalesRouteId!.Value, ct);
        var clients = await db.Clients.AsNoTracking().Where(c => c.TenantId == tenantId && c.IsActive && (c.SalesRouteId != null || c.SalesRepId != null))
            .Select(c => new { c.SalesRouteId, c.SalesRepId }).ToListAsync(ct);
        return clients
            .Select(c => c.SalesRouteId ?? (c.SalesRepId is int rep && repRoute.TryGetValue(rep, out var rr) ? rr : (int?)null))
            .Where(r => r is not null).GroupBy(r => r!.Value).ToDictionary(g => g.Key, g => g.Count());
    }
}
