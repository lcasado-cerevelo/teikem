using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using A = Teikem.Domain.Sales.SalesAnalysisRules;
using R = Teikem.Domain.Sales.SalesRouteRules;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — personas de la ruta (titular y apoyo, con su historial), la ruta directa y el vendedor preferido de cada cliente (uno a uno y
/// en bloque) y «Pasar rutas desde representantes». Leer con sales.analysis.view|manage; escribir con sales.analysis.manage (lo pone el controlador).
/// <para>
/// El TenantId sale de la sesión (además del filtro global, las consultas lo llevan explícito). Todo lo que escribe va dentro de RunInTransactionAsync con las lecturas
/// DENTRO del trabajo (al reintentar se limpia el ChangeTracker). Las escrituras de asignaciones pasan por <see cref="SalesRouteAssignmentOps"/>; las lecturas del
/// equipo de venta, por <see cref="SalesSellerQueries"/>. Nunca DELETE: cerrar pone ValidTo, anular pone IsActive 0.
/// </para>
/// Vendedor preferido (ajuste del dueño, 2026-11-25): al ASIGNARLO debe ser persona activa (titular o apoyo vigente) de la ruta del cliente; un cliente sin ruta no
/// admite preferido. Si después deja de serlo no se bloquea nada: <see cref="SalesClientSalesDto.PreferredOutOfRoute"/> lo señala en la ficha.
/// </summary>
public sealed class SalesRouteService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock)
{
    /// <summary>Tope del buscador de personas para una ruta.</summary>
    public const int MaxCandidates = 50;

    private int TenantId => ((TenantContext)tenant).RequireTenantId();

    // ================================================================ personas de la ruta

    /// <summary>
    /// Titular y apoyos vigentes de la ruta y, con <paramref name="history"/>, el historial cerrado (ValidFrom descendente). Las anuladas no salen, salvo con
    /// <paramref name="includeVoided"/> (auditoría). 404 «Ruta no encontrada.» si no existe en la compañía.
    /// </summary>
    public async Task<SalesRoutePeopleDto> GetPeopleAsync(int routeId, bool history, bool includeVoided, CancellationToken ct)
    {
        var tid = TenantId;
        var route = await FindRouteAsync(tid, routeId, ct);
        var q = db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == tid && a.SalesRouteId == routeId);
        if (!includeVoided) q = q.Where(a => a.IsActive);
        if (!history) q = q.Where(a => a.IsActive && a.ValidTo == null);
        var rows = await q.ToListAsync(ct);

        var userIds = rows.Select(a => a.UserId).Distinct().ToList();
        var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tid, userIds, ct);
        var names = await AssigneeCheck.NamesAsync(db, userIds, ct);
        SalesRouteAssignmentDto Dto(SalesRouteAssignment a) => new(a.SalesRouteAssignmentId, a.UserId, names.GetValueOrDefault(a.UserId) ?? string.Empty,
            R.RoleOf(a.IsPrimary), a.ValidFrom, a.ValidTo, IsOpen(a), !a.IsActive, active.Contains(a.UserId));

        var current = rows.Where(IsOpen).ToList();
        var primary = current.Where(a => a.IsPrimary).OrderByDescending(a => a.ValidFrom).FirstOrDefault();
        var supports = current.Where(a => !a.IsPrimary).Select(Dto).OrderBy(d => d.UserName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var closed = history
            ? rows.Where(a => !IsOpen(a)).OrderByDescending(a => a.ValidFrom).ThenByDescending(a => a.SalesRouteAssignmentId).Select(Dto).ToList()
            : new List<SalesRouteAssignmentDto>();
        return new SalesRoutePeopleDto(route.SalesRouteId, route.Name, route.IsActive, primary is null ? null : Dto(primary), supports, closed);
    }

    /// <summary>
    /// Pone a una persona en la ruta como titular (PRIMARY: cierra al titular vigente con ValidTo = el inicio del nuevo; si era apoyo, cierra esa fila) o de apoyo
    /// (SUPPORT). La ruta debe estar activa; la persona, activa en la compañía; ValidFrom (por omisión hoy) no puede ser futura ni, para un titular, anterior a la del
    /// titular actual.
    /// </summary>
    public async Task<SalesRoutePeopleDto> AssignAsync(int routeId, SalesRouteAssignRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", A.BodyRequired);
        var tid = TenantId;
        await db.RunInTransactionAsync(async c =>
        {
            var route = await FindRouteAsync(tid, routeId, c);
            if (!route.IsActive) throw new ValidationException("routeId", A.RouteInactive);
            var isPrimary = R.ParseRole(req.Role) ?? throw new ValidationException("role", R.RoleInvalid);
            if (req.UserId is not int userId) throw new ValidationException("userId", R.UserRequired);
            await EnsureActiveMemberAsync(tid, userId, c);

            var today = clock.Today;
            var validFrom = req.ValidFrom ?? today;
            if (validFrom > today) throw new ValidationException("validFrom", R.ValidFromFuture);

            if (isPrimary)
            {
                var current = await db.Set<SalesRouteAssignment>().AsNoTracking()
                    .Where(a => a.TenantId == tid && a.SalesRouteId == routeId && a.IsActive && a.IsPrimary && a.ValidTo == null)
                    .Select(a => new { a.UserId, a.ValidFrom }).FirstOrDefaultAsync(c);
                if (current is not null && current.UserId == userId) throw new ConflictException(R.AlreadyPrimary);
                if (R.CheckValidFrom(validFrom, today, current?.ValidFrom) is string error) throw new ValidationException("validFrom", error);
                await SalesRouteAssignmentOps.OpenPrimaryAsync(db, tid, routeId, userId, validFrom, tenant.UserId, clock.UtcNow, c);
            }
            else
                await SalesRouteAssignmentOps.OpenSupportAsync(db, tid, routeId, userId, validFrom, tenant.UserId, clock.UtcNow, c);
        }, ct);
        return await GetPeopleAsync(routeId, true, false, ct);
    }

    /// <summary>
    /// Cierra una asignación vigente de la ruta con ValidTo (por omisión hoy; ni futura ni anterior a su inicio). 404 si la asignación no es de esa ruta; 409 si ya
    /// está cerrada o anulada, o si su RowVersion cambió.
    /// </summary>
    public async Task<SalesRoutePeopleDto> CloseAsync(int routeId, int assignmentId, SalesRouteCloseRequest? req, CancellationToken ct)
    {
        var tid = TenantId;
        await db.RunInTransactionAsync(async c =>
        {
            await FindRouteAsync(tid, routeId, c);
            var a = await FindAssignmentAsync(tid, routeId, assignmentId, c);
            db.ApplyRowVersion(a, req?.RowVersion);
            var today = clock.Today;
            await SalesRouteAssignmentOps.CloseAsync(db, tid, assignmentId, req?.ValidTo ?? today, today, tenant.UserId, clock.UtcNow, c);
        }, ct);
        return await GetPeopleAsync(routeId, true, false, ct);
    }

    /// <summary>
    /// Anula una asignación «registrada por error»: IsActive 0 y ValidTo ??= ValidFrom; deja de contar en el historial y en la atribución. 404 si no es de esa ruta;
    /// 409 si ya está anulada.
    /// </summary>
    public async Task<SalesRoutePeopleDto> VoidAsync(int routeId, int assignmentId, CancellationToken ct)
    {
        var tid = TenantId;
        await db.RunInTransactionAsync(async c =>
        {
            await FindRouteAsync(tid, routeId, c);
            await FindAssignmentAsync(tid, routeId, assignmentId, c);
            await SalesRouteAssignmentOps.VoidAsync(db, tid, assignmentId, tenant.UserId, clock.UtcNow, c);
        }, ct);
        return await GetPeopleAsync(routeId, true, false, ct);
    }

    /// <summary>Personas activas de la compañía (usuario activo y membresía ACTIVE), con buscador por nombre o correo; hasta <see cref="MaxCandidates"/>.</summary>
    public async Task<IReadOnlyList<SalesSellerCandidateDto>> ListCandidatesAsync(string? q, int take, CancellationToken ct)
    {
        var tid = TenantId;
        take = Math.Clamp(take, 1, MaxCandidates);
        var users = db.Users.AsNoTracking().Where(u => u.IsActive
            && db.UserTenants.Any(m => m.TenantId == tid && m.UserId == u.Id
                && db.StatusCodes.Any(s => s.StatusCodeId == m.StatusCodeId && s.Entity == StatusDomains.MembershipStatus && s.InternalCode == MembershipStatuses.Active)));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            users = users.Where(u => (u.FullName != null && u.FullName.Contains(s)) || (u.Email != null && u.Email.Contains(s)));
        }
        var rows = await users.OrderBy(u => u.FullName ?? u.Email).ThenBy(u => u.Id).Take(take)
            .Select(u => new { u.Id, u.FullName, u.Email }).ToListAsync(ct);
        return rows.Select(u => new SalesSellerCandidateDto(u.Id, u.FullName ?? u.Email ?? string.Empty, u.Email)).ToList();
    }

    // ================================================================ venta del cliente

    /// <summary>
    /// La venta del cliente: su ruta (directa o heredada del representante), titular, apoyos, vendedor preferido (y si sigue activo y en la ruta), el vendedor
    /// propuesto, el representante heredado (solo lectura) y el RowVersion. 404 «Cliente no encontrado.».
    /// </summary>
    public async Task<SalesClientSalesDto> GetClientSalesAsync(int clientId, CancellationToken ct)
    {
        var tid = TenantId;
        var client = await db.Clients.AsNoTracking().Where(c => c.ClientId == clientId && c.TenantId == tid)
            .Select(c => new { c.ClientId, c.PublicId, c.Code, c.Name, c.SalesRepId, c.RowVersion }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(A.ClientNotFound);
        var team = await SalesSellerQueries.TeamAsync(db, tid, clientId, null, ct) ?? throw new NotFoundException(A.ClientNotFound);

        var rep = client.SalesRepId is int repId
            ? await db.Set<SalesRep>().AsNoTracking().Where(r => r.SalesRepId == repId && r.TenantId == tid)
                .Select(r => new
                {
                    r.SalesRepId, r.Name, r.SalesRouteId, r.IsActive,
                    RouteName = db.Set<SalesRoute>().Where(x => x.SalesRouteId == r.SalesRouteId && x.TenantId == tid).Select(x => x.Name).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct)
            : null;

        static SalesTeamPersonDto? P(SalesTeamPerson? p) => p is null ? null : new SalesTeamPersonDto(p.UserId, p.Name, p.IsActive, p.ValidFrom);
        var everyone = new[] { team.Preferred, team.Primary }.Concat(team.Supports).Where(p => p is not null).Select(p => p!);
        var proposedName = team.ProposedUserId is int pu ? everyone.FirstOrDefault(p => p.UserId == pu)?.Name : null;
        return new SalesClientSalesDto(
            client.ClientId, client.PublicId, client.Code, client.Name,
            team.RouteId, team.RouteName, team.RouteFromRep, team.DirectRouteId,
            P(team.Primary), team.Supports.Select(s => P(s)!).ToList(),
            P(team.Preferred), team.PreferredOutOfRoute,
            team.ProposedUserId, proposedName, team.ProposedKind,
            rep?.SalesRepId, rep?.Name, rep?.SalesRouteId, rep?.RouteName, rep?.IsActive ?? false,
            client.RowVersion is null ? null : Convert.ToBase64String(client.RowVersion));
    }

    /// <summary>
    /// Cambia la ruta directa (ClearRoute la quita: vuelve a heredar la del representante) y el vendedor preferido (ClearPreferred lo quita) del cliente.
    /// - La ruta debe existir en la compañía (404) y estar activa (400).
    /// - El preferido NUEVO debe ser persona activa (titular o apoyo vigente) de la ruta del cliente, con el cambio de ruta ya aplicado (400); sin ruta, 400.
    ///   Mandar el mismo preferido que ya tiene no es un cambio y no se vuelve a validar (que haya dejado la ruta no bloquea nada).
    /// - RowVersion opcional: 409 si el cliente cambió.
    /// </summary>
    public async Task<SalesClientSalesDto> SetClientSalesAsync(int clientId, SalesClientSalesRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", A.BodyRequired);
        var tid = TenantId;
        await db.RunInTransactionAsync(async c =>
        {
            var client = await db.Clients.AsTracking().FirstOrDefaultAsync(x => x.ClientId == clientId && x.TenantId == tid, c)
                ?? throw new NotFoundException(A.ClientNotFound);
            db.ApplyRowVersion(client, req.RowVersion);

            if (req.ClearRoute) client.SalesRouteId = null;
            else if (req.RouteId is int routeId)
            {
                await EnsureActiveRouteAsync(tid, routeId, c);
                client.SalesRouteId = routeId;
            }

            if (req.ClearPreferred) client.PreferredSalesUserId = null;
            else if (req.PreferredUserId is int preferred && preferred != client.PreferredSalesUserId)
            {
                var (effectiveRoute, _) = await SalesSellerQueries.EffectiveRouteAsync(db, tid, client.SalesRouteId, client.SalesRepId, c);
                var inRoute = effectiveRoute is int er && await IsActiveRoutePersonAsync(tid, er, preferred, c);
                if (R.CheckPreferred(effectiveRoute, inRoute) is string error) throw new ValidationException("preferredUserId", error);
                client.PreferredSalesUserId = preferred;
            }

            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
        }, ct);
        return await GetClientSalesAsync(clientId, ct);
    }

    /// <summary>
    /// Pone (o quita, RouteId null) la ruta directa de varios clientes (hasta 2000). 400 sin clientes o con demasiados; 404 si falta alguno (también los de otra
    /// compañía). Devuelve cuántos cambiaron. No toca el vendedor preferido: si deja de ser de la ruta, la ficha lo señala.
    /// </summary>
    public async Task<SalesClientRouteBulkResultDto> AssignClientsRouteAsync(SalesClientRouteBulkRequest req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", A.BodyRequired);
        if (req.ClientIds is not { Count: > 0 }) throw new ValidationException("clientIds", A.ClientsRequired);
        if (req.ClientIds.Count > A.MaxBulk) throw new ValidationException("clientIds", A.TooMany);
        var tid = TenantId;
        var ids = req.ClientIds.Distinct().ToList();
        return await db.RunInTransactionAsync(async c =>
        {
            string? routeName = null;
            if (req.RouteId is int routeId) routeName = (await EnsureActiveRouteAsync(tid, routeId, c)).Name;
            var clients = await db.Clients.AsTracking().Where(x => x.TenantId == tid && ids.Contains(x.ClientId)).ToListAsync(c);
            if (clients.Count != ids.Count) throw new NotFoundException(A.ClientNotFound);
            var n = 0;
            foreach (var client in clients.Where(x => x.SalesRouteId != req.RouteId)) { client.SalesRouteId = req.RouteId; n++; }
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);
            return new SalesClientRouteBulkResultDto(n, req.RouteId, routeName);
        }, ct);
    }

    // ================================================================ pasar rutas desde representantes

    /// <summary>
    /// La misma regla que la migración de la sección «Lote 244» de logistica-db-update.sql, idempotente (la segunda corrida no cambia nada):
    /// - los clientes sin ruta reciben la de su representante (activo, con ruta activa);
    /// - en cada ruta activa sin titular vigente: si tiene EXACTAMENTE un representante activo con usuario de una persona activa, ese usuario queda de titular; si
    ///   tiene varios, todos quedan de apoyo (los que no lo son ya) y la ruta sigue sin titular;
    /// - devuelve los conteos, los representantes activos sin usuario y los de las rutas con varios representantes.
    /// Con <paramref name="dryRun"/> (por omisión) no escribe nada. ValidFrom = el primer día del mes más antiguo con historial, factura o cuota (sin datos, hoy), nunca
    /// futuro y nunca antes del cierre de una asignación anterior de la misma ruta (titular) o persona (apoyo), para no traslapar el historial.
    /// </summary>
    public async Task<SalesRoutesFromRepsDto> RoutesFromRepsAsync(bool dryRun, CancellationToken ct)
    {
        var tid = TenantId;
        return await db.RunInTransactionAsync(async c =>
        {
            var today = clock.Today;
            var validFrom = await FirstDataDayAsync(tid, today, c);
            var routes = await db.Set<SalesRoute>().AsNoTracking().Where(r => r.TenantId == tid).ToDictionaryAsync(r => r.SalesRouteId, c);
            var activeRoutes = routes.Values.Where(r => r.IsActive).Select(r => r.SalesRouteId).ToHashSet();
            var reps = await db.Set<SalesRep>().AsNoTracking().Where(r => r.TenantId == tid && r.IsActive).ToListAsync(c);
            var repRoute = reps.Where(r => r.SalesRouteId is int ro && activeRoutes.Contains(ro)).ToDictionary(r => r.SalesRepId, r => r.SalesRouteId!.Value);

            // 1) clientes sin ruta → la de su representante
            var clientsQuery = db.Clients.Where(x => x.TenantId == tid && x.SalesRouteId == null && x.SalesRepId != null);
            var clients = dryRun ? await clientsQuery.AsNoTracking().ToListAsync(c) : await clientsQuery.AsTracking().ToListAsync(c);
            var routed = 0;
            foreach (var client in clients)
            {
                if (!repRoute.TryGetValue(client.SalesRepId!.Value, out var ro)) continue;
                if (!dryRun) client.SalesRouteId = ro;
                routed++;
            }
            if (!dryRun && routed > 0) await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, c);

            // 2) titulares y apoyos de las rutas activas sin titular
            var rows = await db.Set<SalesRouteAssignment>().AsNoTracking().Where(a => a.TenantId == tid && a.IsActive).ToListAsync(c);
            var withPrimary = rows.Where(a => a.IsPrimary && a.ValidTo == null).Select(a => a.SalesRouteId).ToHashSet();
            var open = rows.Where(a => a.ValidTo == null).Select(a => (a.SalesRouteId, a.UserId)).ToHashSet();
            var active = await SalesSellerQueries.ActiveMemberIdsAsync(db, tid, reps.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), c);
            string? RouteName(int? id) => id is int x && routes.TryGetValue(x, out var r) ? r.Name : null;

            int primaries = 0, supports = 0;
            var several = new List<SalesRepAlertDto>();
            foreach (var routeId in activeRoutes.OrderBy(id => routes[id].SortOrder).ThenBy(id => routes[id].Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (withPrimary.Contains(routeId)) continue;
                var people = reps.Where(r => r.SalesRouteId == routeId && r.UserId is int u && active.Contains(u)).OrderBy(r => r.SalesRepId).ToList();
                var userIds = people.Select(r => r.UserId!.Value).Distinct().ToList();
                if (userIds.Count == 1)
                {
                    var from = Floor(validFrom, rows.Where(a => a.SalesRouteId == routeId && a.IsPrimary).Select(a => a.ValidTo), today);
                    if (!dryRun) await SalesRouteAssignmentOps.OpenPrimaryAsync(db, tid, routeId, userIds[0], from, tenant.UserId, clock.UtcNow, c);
                    primaries++;
                }
                else if (userIds.Count > 1)
                {
                    several.AddRange(people.Select(r => new SalesRepAlertDto(r.SalesRepId, r.Name, routeId, RouteName(routeId))));
                    foreach (var userId in userIds.Where(u => !open.Contains((routeId, u))))
                    {
                        var from = Floor(validFrom, rows.Where(a => a.SalesRouteId == routeId && a.UserId == userId).Select(a => a.ValidTo), today);
                        if (!dryRun) await SalesRouteAssignmentOps.OpenSupportAsync(db, tid, routeId, userId, from, tenant.UserId, clock.UtcNow, c);
                        supports++;
                    }
                }
            }

            var withoutUser = reps.Where(r => r.UserId is null).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new SalesRepAlertDto(r.SalesRepId, r.Name, r.SalesRouteId, RouteName(r.SalesRouteId))).ToList();
            return new SalesRoutesFromRepsDto(dryRun, routed, primaries, supports, validFrom, withoutUser, several);
        }, ct);
    }

    // ================================================================ apoyo

    /// <summary>Vigente = activa y sin fecha de cierre.</summary>
    private static bool IsOpen(SalesRouteAssignment a) => a.IsActive && a.ValidTo == null;

    /// <summary>El inicio de una asignación nueva: no antes del último cierre de las anteriores (para no traslapar) y nunca después de hoy.</summary>
    private static DateOnly Floor(DateOnly from, IEnumerable<DateOnly?> previousEnds, DateOnly today)
    {
        foreach (var end in previousEnds)
            if (end is DateOnly e && e > from) from = e;
        return from > today ? today : from;
    }

    /// <summary>El primer día del mes más antiguo con historial (de lotes activos), factura activa o cuota activa de la compañía; sin datos, hoy. Nunca futuro.</summary>
    private async Task<DateOnly> FirstDataDayAsync(int tid, DateOnly today, CancellationToken ct)
    {
        var line = await db.Set<SalesHistoryLine>().AsNoTracking()
            .Where(l => l.TenantId == tid && db.Set<SalesHistoryBatch>().Any(b => b.SalesHistoryBatchId == l.SalesHistoryBatchId && b.IsActive))
            .MinAsync(l => (DateOnly?)l.TxnDate, ct);
        var invoice = await db.Set<Invoice>().AsNoTracking().Where(i => i.TenantId == tid && i.IsActive && i.IssueDate != null).MinAsync(i => i.IssueDate, ct);
        var quota = await db.Set<SalesQuota>().AsNoTracking().Where(q => q.TenantId == tid && q.IsActive).MinAsync(q => (DateOnly?)q.Month, ct);
        var dates = new[] { line, invoice, quota }.Where(d => d is not null).Select(d => d!.Value).ToList();
        if (dates.Count == 0) return today;
        var first = dates.Min();
        var start = new DateOnly(first.Year, first.Month, 1);
        return start > today ? today : start;
    }

    private async Task<SalesRoute> FindRouteAsync(int tid, int routeId, CancellationToken ct)
        => await db.Set<SalesRoute>().AsNoTracking().FirstOrDefaultAsync(r => r.SalesRouteId == routeId && r.TenantId == tid, ct)
           ?? throw new NotFoundException(A.RouteNotFound, feminine: true);

    private async Task<SalesRoute> EnsureActiveRouteAsync(int tid, int routeId, CancellationToken ct)
    {
        var route = await FindRouteAsync(tid, routeId, ct);
        if (!route.IsActive) throw new ValidationException("routeId", A.RouteInactive);
        return route;
    }

    /// <summary>La asignación, rastreada, si es de esa ruta; 404 «Asignación no encontrada.» si no.</summary>
    private async Task<SalesRouteAssignment> FindAssignmentAsync(int tid, int routeId, int assignmentId, CancellationToken ct)
        => await db.Set<SalesRouteAssignment>().AsTracking()
               .FirstOrDefaultAsync(a => a.SalesRouteAssignmentId == assignmentId && a.SalesRouteId == routeId && a.TenantId == tid, ct)
           ?? throw new NotFoundException(R.AssignmentNotFound, feminine: true);

    /// <summary>400 si el usuario no tiene membresía en la compañía (portal, otra compañía, inexistente) o si no es persona activa (suspendido, invitado, inactivo).</summary>
    private async Task EnsureActiveMemberAsync(int tid, int userId, CancellationToken ct)
    {
        if (!await db.UserTenants.AsNoTracking().AnyAsync(m => m.TenantId == tid && m.UserId == userId, ct))
            throw new ValidationException("userId", R.UserNotMember);
        if (!(await SalesSellerQueries.ActiveMemberIdsAsync(db, tid, new[] { userId }, ct)).Contains(userId))
            throw new ValidationException("userId", R.UserSuspended);
    }

    /// <summary>¿El usuario es titular o apoyo VIGENTE de la ruta y persona activa de la compañía?</summary>
    private async Task<bool> IsActiveRoutePersonAsync(int tid, int routeId, int userId, CancellationToken ct)
    {
        var inRoute = await db.Set<SalesRouteAssignment>().AsNoTracking()
            .AnyAsync(a => a.TenantId == tid && a.SalesRouteId == routeId && a.UserId == userId && a.IsActive && a.ValidTo == null, ct);
        return inRoute && (await SalesSellerQueries.ActiveMemberIdsAsync(db, tid, new[] { userId }, ct)).Contains(userId);
    }
}
