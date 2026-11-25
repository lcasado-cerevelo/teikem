using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 244 — una persona de la ruta en un día: la asignación, la ruta, el usuario, si es titular y su vigencia.</summary>
public sealed record SalesRoutePerson(int AssignmentId, int RouteId, int UserId, bool IsPrimary, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>Lote 244 — una persona del equipo de venta de un cliente: usuario, nombre, si sigue siendo persona activa de la compañía y desde cuándo está en la ruta.</summary>
public sealed record SalesTeamPerson(int UserId, string Name, bool IsActive, DateOnly? ValidFrom);

/// <summary>
/// Lote 244 — el equipo de venta de un cliente HOY:
/// - RouteId / RouteName: la ruta efectiva (la directa, o la de su representante con RouteFromRep = true; NULL = «Sin ruta»). DirectRouteId = la directa.
/// - Primary y Supports: titular y apoyos vigentes de la ruta (cada uno con si sigue activo).
/// - Preferred: el vendedor preferido (con si sigue activo) y PreferredOutOfRoute = ya no es persona activa de la ruta (solo se señala).
/// - ProposedUserId / ProposedKind: el vendedor propuesto para una orden creada por <c>requestedBy</c> (PREFERRED, PRIMARY o SUPPORT).
/// - Candidates: lo que usan las reglas de <see cref="SalesRouteRules"/> para proponer, validar la elección y congelar.
/// </summary>
public sealed record SalesRouteTeam(
    int ClientId,
    int? RouteId,
    string? RouteName,
    bool RouteFromRep,
    int? DirectRouteId,
    SalesTeamPerson? Primary,
    IReadOnlyList<SalesTeamPerson> Supports,
    SalesTeamPerson? Preferred,
    bool PreferredOutOfRoute,
    int? ProposedUserId,
    string? ProposedKind,
    SellerCandidates Candidates);

/// <summary>Lote 244 — el vendedor de una orden: el usuario guardado (elección del borrador o copia congelada), si es persona activa, su nombre y cuándo se congeló la copia.</summary>
public sealed record SalesOrderSellerRef(int? SellerUserId, bool IsActiveMember, string? Name, DateTime? SalesCopyAtUtc);

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — consultas de SOLO LECTURA del equipo de venta (AsNoTracking y TenantId explícito además del filtro global). Las usan el servicio
/// de rutas, el análisis por ruta, la orden de venta (vendedor propuesto y copia), las fuentes de datos, el correo de la orden (Lote G) y los avisos del Lote D.
/// «Persona activa» = AspNetUsers.IsActive y UserTenant de la compañía con MembershipStatus ACTIVE (el estatus se busca en StatusCode, sin id fijo); un usuario del
/// portal no tiene UserTenant y nunca es persona activa.
/// </summary>
public static class SalesSellerQueries
{
    /// <summary>De esos usuarios, los que son personas activas de la compañía.</summary>
    public static async Task<HashSet<int>> ActiveMemberIdsAsync(TeikemDbContext db, int tenantId, IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new HashSet<int>();
        var found = await db.UserTenants.AsNoTracking()
            .Where(m => m.TenantId == tenantId && ids.Contains(m.UserId)
                && db.StatusCodes.Any(s => s.StatusCodeId == m.StatusCodeId && s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active)
                && db.Users.Any(u => u.Id == m.UserId && u.IsActive))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);
        return found.ToHashSet();
    }

    /// <summary>Titular y apoyos que contaban ese día en esas rutas (intervalo medio abierto; sin las anuladas). Sin rutas = vacío.</summary>
    public static async Task<List<SalesRoutePerson>> PeopleOnAsync(TeikemDbContext db, int tenantId, IEnumerable<int> routeIds, DateOnly day, CancellationToken ct)
    {
        var ids = routeIds.Distinct().ToList();
        if (ids.Count == 0) return new List<SalesRoutePerson>();
        return await db.Set<SalesRouteAssignment>().AsNoTracking()
            .Where(a => a.TenantId == tenantId && ids.Contains(a.SalesRouteId) && a.IsActive && a.ValidFrom <= day && (a.ValidTo == null || a.ValidTo > day))
            .OrderBy(a => a.SalesRouteId).ThenByDescending(a => a.IsPrimary).ThenBy(a => a.ValidFrom).ThenBy(a => a.SalesRouteAssignmentId)
            .Select(a => new SalesRoutePerson(a.SalesRouteAssignmentId, a.SalesRouteId, a.UserId, a.IsPrimary, a.ValidFrom, a.ValidTo))
            .ToListAsync(ct);
    }

    /// <summary>Línea de tiempo de titulares (sin anuladas) de esas rutas; NULL = de todas las rutas de la compañía. Para el «primaryOn» del análisis.</summary>
    public static async Task<SalesPrimaryTimeline> PrimaryTimelineAsync(TeikemDbContext db, int tenantId, IEnumerable<int>? routeIds, CancellationToken ct)
    {
        var q = db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == tenantId && a.IsActive && a.IsPrimary);
        if (routeIds is not null)
        {
            var ids = routeIds.Distinct().ToList();
            if (ids.Count == 0) return SalesPrimaryTimeline.Empty;
            q = q.Where(a => ids.Contains(a.SalesRouteId));
        }
        var rows = await q.Select(a => new { a.SalesRouteId, a.UserId, a.ValidFrom, a.ValidTo }).ToListAsync(ct);
        var spans = rows.GroupBy(r => r.SalesRouteId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<SalesPrimarySpan>)g.OrderBy(r => r.ValidFrom).Select(r => new SalesPrimarySpan(r.UserId, r.ValidFrom, r.ValidTo)).ToList());
        return new SalesPrimaryTimeline(spans);
    }

    /// <summary>
    /// El equipo de venta del cliente hoy (titular y apoyos VIGENTES: activos y sin fecha de cierre) y el vendedor propuesto para una orden creada por
    /// <paramref name="requestedBy"/>. NULL = el cliente no existe en la compañía.
    /// </summary>
    public static async Task<SalesRouteTeam?> TeamAsync(TeikemDbContext db, int tenantId, int clientId, int? requestedBy, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId && c.TenantId == tenantId)
            .Select(c => new { c.ClientId, c.SalesRouteId, c.SalesRepId, c.PreferredSalesUserId }).FirstOrDefaultAsync(ct);
        if (client is null) return null;

        var (routeId, fromRep) = await EffectiveRouteAsync(db, tenantId, client.SalesRouteId, client.SalesRepId, ct);
        var routeName = routeId is int rid
            ? await db.Set<SalesRoute>().AsNoTracking().Where(r => r.SalesRouteId == rid && r.TenantId == tenantId).Select(r => r.Name).FirstOrDefaultAsync(ct)
            : null;
        var people = routeId is int r2 ? await CurrentPeopleAsync(db, tenantId, r2, ct) : new List<SalesRoutePerson>();

        var userIds = people.Select(p => p.UserId).ToList();
        if (client.PreferredSalesUserId is int pref) userIds.Add(pref);
        if (requestedBy is int rb) userIds.Add(rb);
        var active = await ActiveMemberIdsAsync(db, tenantId, userIds, ct);
        var names = await AssigneeCheck.NamesAsync(db, userIds, ct);

        SalesTeamPerson Person(int userId, DateOnly? from) => new(userId, names.GetValueOrDefault(userId) ?? string.Empty, active.Contains(userId), from);
        var primaryRow = people.FirstOrDefault(p => p.IsPrimary);
        var primary = primaryRow is null ? null : Person(primaryRow.UserId, primaryRow.ValidFrom);
        var supports = people.Where(p => !p.IsPrimary).Select(p => Person(p.UserId, p.ValidFrom))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var preferred = client.PreferredSalesUserId is int pu ? Person(pu, people.FirstOrDefault(p => p.UserId == pu)?.ValidFrom) : null;

        var activeRoutePeople = people.Select(p => p.UserId).Where(active.Contains).ToHashSet();
        var candidates = new SellerCandidates(client.PreferredSalesUserId, preferred?.IsActive == true, primary?.UserId, primary?.IsActive == true, requestedBy, activeRoutePeople);
        var proposed = SalesRouteRules.ProposeSeller(candidates);
        return new SalesRouteTeam(client.ClientId, routeId, routeName, fromRep, client.SalesRouteId, primary, supports, preferred,
            SalesRouteRules.PreferredOutOfRoute(client.PreferredSalesUserId, activeRoutePeople), proposed, SalesRouteRules.SellerKindOf(proposed, candidates), candidates);
    }

    /// <summary>
    /// Quiénes reciben los avisos de ventas de ese cliente (contrato del Lote D): el vendedor preferido y el titular y los apoyos vigentes de su ruta efectiva,
    /// distintos y solo personas activas. Vacío si el cliente no existe.
    /// </summary>
    public static async Task<IReadOnlyList<int>> NoticeUsersAsync(TeikemDbContext db, int tenantId, int clientId, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId && c.TenantId == tenantId)
            .Select(c => new { c.SalesRouteId, c.SalesRepId, c.PreferredSalesUserId }).FirstOrDefaultAsync(ct);
        if (client is null) return Array.Empty<int>();
        var (routeId, _) = await EffectiveRouteAsync(db, tenantId, client.SalesRouteId, client.SalesRepId, ct);
        var ids = new List<int>();
        if (client.PreferredSalesUserId is int pref) ids.Add(pref);
        if (routeId is int r)
            ids.AddRange((await CurrentPeopleAsync(db, tenantId, r, ct)).Select(p => p.UserId));
        var active = await ActiveMemberIdsAsync(db, tenantId, ids, ct);
        return ids.Distinct().Where(active.Contains).ToList();
    }

    /// <summary>El vendedor guardado en la orden (elección del borrador o copia congelada), si es persona activa y su nombre. NULL = la orden no existe en la compañía.</summary>
    public static async Task<SalesOrderSellerRef?> OrderSellerAsync(TeikemDbContext db, int tenantId, int salesOrderId, CancellationToken ct)
    {
        var order = await db.SalesOrders.AsNoTracking().Where(o => o.SalesOrderId == salesOrderId && o.TenantId == tenantId)
            .Select(o => new { o.SellerUserId, o.SalesCopyAtUtc }).FirstOrDefaultAsync(ct);
        if (order is null) return null;
        if (order.SellerUserId is not int seller) return new SalesOrderSellerRef(null, false, null, order.SalesCopyAtUtc);
        var active = await ActiveMemberIdsAsync(db, tenantId, new[] { seller }, ct);
        var names = await AssigneeCheck.NamesAsync(db, new[] { seller }, ct);
        return new SalesOrderSellerRef(seller, active.Contains(seller), names.GetValueOrDefault(seller), order.SalesCopyAtUtc);
    }

    // ---------------------------------------------------------------- apoyo

    /// <summary>La ruta efectiva: la directa del cliente o, si no tiene, la de su representante (RouteFromRep = true).</summary>
    internal static async Task<(int? RouteId, bool FromRep)> EffectiveRouteAsync(TeikemDbContext db, int tenantId, int? directRouteId, int? repId, CancellationToken ct)
    {
        if (directRouteId is int d) return (d, false);
        if (repId is not int rep) return (null, false);
        var repRoute = await db.Set<SalesRep>().AsNoTracking().Where(r => r.SalesRepId == rep && r.TenantId == tenantId).Select(r => r.SalesRouteId).FirstOrDefaultAsync(ct);
        return repRoute is int rr ? (rr, true) : (null, false);
    }

    /// <summary>Titular y apoyos VIGENTES de la ruta (activos y sin fecha de cierre), el titular primero.</summary>
    private static Task<List<SalesRoutePerson>> CurrentPeopleAsync(TeikemDbContext db, int tenantId, int routeId, CancellationToken ct)
        => db.Set<SalesRouteAssignment>().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.SalesRouteId == routeId && a.IsActive && a.ValidTo == null)
            .OrderByDescending(a => a.IsPrimary).ThenBy(a => a.ValidFrom).ThenBy(a => a.SalesRouteAssignmentId)
            .Select(a => new SalesRoutePerson(a.SalesRouteAssignmentId, a.SalesRouteId, a.UserId, a.IsPrimary, a.ValidFrom, a.ValidTo))
            .ToListAsync(ct);
}
