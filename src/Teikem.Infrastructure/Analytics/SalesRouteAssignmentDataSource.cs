using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Analytics;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — fuente SALES_ROUTE_ASSIGNMENT, «Personas de las rutas de venta»: una fila por asignación de una persona (usuario de Teikem) a una
/// ruta de venta, como titular (PRIMARY) o apoyo (SUPPORT), con su vigencia en días de la compañía. Actividad de período por ValidFrom (día de calendario; el rango llega
/// en días de la compañía, FromDay/ToDayExclusive). IsCurrent = vigente (activa y sin fecha de cierre); IsVoided = anulada («registrada por error»).
/// Las anuladas NO salen por omisión: solo al pedir ids concretos (resolver una relación). Relación a SALES_ROUTE por RouteId. Filtro de tenant (además del global, el
/// TenantId de la sesión) y tope ClientDataSourceHelpers.MaxRows (las más recientes). Lectura: sales.analysis.view y el módulo Órdenes de venta.
/// </summary>
public sealed class SalesRouteAssignmentDataSource(TeikemDbContext db, ITenantContext tenant) : IDataSource
{
    public string Key => SalesRouteCodes.AssignmentDataSourceKey;
    public string LabelEs => "Personas de las rutas de venta";
    public string LabelEn => "Sales route people";
    public string? EntityTypeCode => null;
    public string? DateField => "ValidFrom";
    public string IdField => "Id";
    public string DefaultBusinessModule => BusinessModules.Warehouse;
    public string? TenantModule => ModuleKeys.SalesOrders;

    public IReadOnlyList<DataField> Fields { get; } = new[]
    {
        new DataField("Id", "Id", "Id", DataFieldType.Number),
        new DataField("RouteId", "Id de la ruta de venta", "Sales route id", DataFieldType.Number),
        new DataField("RouteName", "Ruta de venta", "Sales route", DataFieldType.Text),
        new DataField("UserId", "Id del usuario", "User id", DataFieldType.Number),
        new DataField("UserName", "Persona", "Person", DataFieldType.Text),
        new DataField("Role", "Papel (código)", "Role (code)", DataFieldType.Text),
        new DataField("RoleLabel", "Papel", "Role", DataFieldType.Text),
        new DataField("ValidFrom", "Desde", "From", DataFieldType.Date),
        new DataField("ValidTo", "Hasta", "To", DataFieldType.Date),
        new DataField("IsCurrent", "Vigente", "Current", DataFieldType.Bool),
        new DataField("IsVoided", "Anulada", "Voided", DataFieldType.Bool),
        new DataField("UserIsActiveMember", "Persona activa", "Active person", DataFieldType.Bool),
    };

    public IReadOnlyList<DataRelation> Relations { get; } = new[]
    {
        new DataRelation("Route", SalesRouteCodes.RouteDataSourceKey, "RouteId", "Ruta de venta", "Sales route"),
    };

    /// <summary>Etiqueta del papel en el idioma de la sesión.</summary>
    public static string RoleLabel(bool isPrimary, string? lang)
        => string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ? (isPrimary ? "Primary" : "Support") : (isPrimary ? "Titular" : "Apoyo");

    public async Task<List<DataRow>> LoadAsync(DataQuery q, CancellationToken ct)
    {
        if (tenant.TenantId is not int tenantId) return new List<DataRow>();
        var query = db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == tenantId);
        if (q.Ids is not null)
        {
            var wanted = q.Ids.ToList();
            query = query.Where(a => wanted.Contains(a.SalesRouteAssignmentId));
        }
        else query = query.Where(a => a.IsActive);   // sin anuladas por omisión
        // Rango sobre ValidFrom (DATE): desde inclusivo, hasta exclusivo, en días de la compañía; sin ellos se deriva de FromUtc/ToUtc.
        if ((q.FromDay ?? (q.FromUtc.HasValue ? DateOnly.FromDateTime(q.FromUtc.Value) : (DateOnly?)null)) is DateOnly from)
            query = query.Where(a => a.ValidFrom >= from);
        if ((q.ToDayExclusive ?? (q.ToUtc.HasValue ? DateOnly.FromDateTime(q.ToUtc.Value) : (DateOnly?)null)) is DateOnly to)
            query = query.Where(a => a.ValidFrom < to);
        var rows = await query.OrderByDescending(a => a.ValidFrom).ThenByDescending(a => a.SalesRouteAssignmentId)
            .Take(ClientDataSourceHelpers.MaxRows).ToListAsync(ct);
        if (rows.Count == 0) return new List<DataRow>();

        var routeIds = rows.Select(a => a.SalesRouteId).Distinct().ToList();
        var routes = await db.Set<SalesRoute>().AsNoTracking().Where(r => r.TenantId == tenantId && routeIds.Contains(r.SalesRouteId))
            .ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
        var userIds = rows.Select(a => a.UserId).Distinct().ToList();
        var names = await AssigneeCheck.NamesAsync(db, userIds, ct);
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tenantId, userIds, ct);
        var lang = tenant.Lang;

        return rows.Select(a => new DataRow
        {
            ["Id"] = a.SalesRouteAssignmentId,
            ["RouteId"] = a.SalesRouteId,
            ["RouteName"] = routes.GetValueOrDefault(a.SalesRouteId),
            ["UserId"] = a.UserId,
            ["UserName"] = names.GetValueOrDefault(a.UserId),
            ["Role"] = SalesRouteRules.RoleOf(a.IsPrimary),
            ["RoleLabel"] = RoleLabel(a.IsPrimary, lang),
            ["ValidFrom"] = a.ValidFrom,
            ["ValidTo"] = a.ValidTo,
            ["IsCurrent"] = a.IsActive && a.ValidTo == null,
            ["IsVoided"] = !a.IsActive,
            ["UserIsActiveMember"] = active.Contains(a.UserId),
        }).ToList();
    }
}
