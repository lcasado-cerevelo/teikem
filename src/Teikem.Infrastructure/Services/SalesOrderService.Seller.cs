using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — el vendedor de la orden de venta:
/// - En BORRADOR se PROPONE (el preferido activo del cliente → el titular vigente activo de su ruta → quien crea la orden, solo si es persona vigente de la ruta →
///   ninguno) y quien tiene sales.create puede ESCOGER otro, pero solo entre el preferido y las personas vigentes y activas de la ruta (400 si no).
/// - Al SOLICITAR (DRAFT → REQUESTED) se CONGELA la copia: la ruta efectiva del cliente, el vendedor (la elección si sigue valiendo; si no, la propuesta; puede
///   quedar «Sin vendedor») y el momento. Es una llamada explícita dentro de la transacción de solicitar, antes de la transición y del aviso del Lote G; el
///   regreso de un despacho a REQUESTED y cancelar no la tocan, y ninguna ruta del API la cambia después.
/// Todo se lee con <see cref="SalesSellerQueries"/> (sin dependencias nuevas en el constructor).
/// </summary>
public sealed partial class SalesOrderService
{
    /// <summary>El equipo de venta del cliente hoy y el propuesto para una orden creada por <paramref name="requestedBy"/>. NULL = el cliente no existe en la compañía.</summary>
    private Task<SalesRouteTeam?> SellerTeamAsync(int clientId, int? requestedBy, CancellationToken ct)
        => SalesSellerQueries.TeamAsync(db, T.RequireTenantId(), clientId, requestedBy, ct);

    /// <summary>La elección del borrador vale si es el preferido activo del cliente o una persona vigente y activa de su ruta efectiva; si no, 400 SellerNotAllowed.</summary>
    private async Task ValidateSellerChoiceAsync(int clientId, int userId, CancellationToken ct)
    {
        var team = await SellerTeamAsync(clientId, null, ct) ?? throw new NotFoundException("Cliente");
        if (!SalesRouteRules.IsAllowedSellerChoice(userId, team.Candidates))
            throw new ValidationException("sellerUserId", SalesRouteRules.SellerNotAllowed);
    }

    /// <summary>¿La elección guardada en el borrador sigue valiendo para ese cliente? (sin elección = true: vale la propuesta).</summary>
    private async Task<bool> SellerChoiceStillValidAsync(int clientId, int? chosen, CancellationToken ct)
    {
        if (chosen is not int userId) return true;
        var team = await SellerTeamAsync(clientId, null, ct);
        return team is not null && SalesRouteRules.IsAllowedSellerChoice(userId, team.Candidates);
    }

    /// <summary>
    /// Copia CONGELADA al solicitar (la llama RequestAsync dentro de su transacción, justo antes de la transición a REQUESTED): ruta efectiva del cliente en
    /// ese momento (la directa o la de su representante), el vendedor (<see cref="SalesRouteRules.FreezeSeller"/>: la elección si sigue valiendo y activa, si no
    /// la propuesta; puede quedar NULL) y la marca SalesCopyAtUtc.
    /// </summary>
    /// <remarks>
    /// Lote 249: la orden interna de ADICIÓN copia la ruta y el vendedor de su orden madre (así las comisiones y los análisis por vendedor la juntan con la
    /// original), sin volver a resolverlos con la regla de hoy.
    /// </remarks>
    private async Task FreezeSellerAsync(SalesOrder order, CancellationToken ct)
    {
        if (order.ParentSalesOrderId is int parentId)
        {
            var parent = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == parentId)
                .Select(o => new { o.SalesRouteId, o.SellerUserId }).FirstOrDefaultAsync(ct);
            if (parent is not null)
            {
                order.SalesRouteId = parent.SalesRouteId;
                order.SellerUserId = parent.SellerUserId;
                order.SalesCopyAtUtc = DateTime.UtcNow;
                return;
            }
        }
        var team = await SellerTeamAsync(order.ClientId, order.RequestedBy, ct);
        var chosen = order.SellerUserId;
        var chosenOk = chosen is int c && team is not null && SalesRouteRules.IsAllowedSellerChoice(c, team.Candidates);
        order.SalesRouteId = team?.RouteId;
        order.SellerUserId = team is null ? null : SalesRouteRules.FreezeSeller(chosen, chosenOk, team.Candidates);
        order.SalesCopyAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// GET /sales-orders/seller-options?client= — para el campo «Vendedor» del borrador: la ruta efectiva del cliente, el vendedor propuesto para quien la está
    /// capturando y las opciones que se pueden escoger (solo personas activas; titular, apoyos y, al final, el preferido si no es de la ruta). Sin correos.
    /// 400 si falta el cliente; 404 «Cliente no encontrado.» si no existe en la compañía.
    /// </summary>
    public async Task<SalesSellerOptionsDto> SellerOptionsAsync(Guid? clientPublicId, CancellationToken ct)
    {
        if (clientPublicId is not Guid pid) throw new ValidationException("client", SalesRules.ClientRequired);
        var clientId = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == pid).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("Cliente");
        var team = await SellerTeamAsync(clientId, tenant.UserId, ct) ?? throw new NotFoundException("Cliente");
        return BuildSellerOptions(team);
    }

    /// <summary>Arma las opciones a partir del equipo (pura, para que la lista y el propuesto salgan de la misma foto).</summary>
    private static SalesSellerOptionsDto BuildSellerOptions(SalesRouteTeam team)
    {
        var options = new List<SalesSellerOptionDto>();
        var seen = new HashSet<int>();
        void Add(SalesTeamPerson p, string kind)
        {
            if (!p.IsActive || !seen.Add(p.UserId)) return;
            options.Add(new SalesSellerOptionDto(p.UserId, p.Name, kind, team.Candidates.Preferred == p.UserId, team.ProposedUserId == p.UserId));
        }
        if (team.Primary is not null) Add(team.Primary, SalesRouteCodes.SellerKindPrimary);
        foreach (var s in team.Supports) Add(s, SalesRouteCodes.SellerKindSupport);
        if (team.Preferred is not null) Add(team.Preferred, SalesRouteCodes.SellerKindPreferred);
        var proposedName = team.ProposedUserId is int pu ? options.FirstOrDefault(o => o.UserId == pu)?.Name : null;
        return new SalesSellerOptionsDto(team.RouteId, team.RouteName, team.RouteFromRep, team.ProposedUserId, proposedName, team.ProposedKind, options);
    }

    /// <summary>Ruta y vendedor que muestra el detalle de la orden (ver <see cref="SalesOrderDto"/>).</summary>
    private sealed record SellerView(int? RouteId, string? RouteName, int? SellerUserId, string? SellerName, bool Proposed);

    /// <summary>
    /// Para el detalle: la copia de las órdenes solicitadas (con nombres) y, en los borradores, la ruta efectiva de hoy con la elección válida o la propuesta.
    /// Las órdenes anteriores al lote (sin copia y ya no en borrador) quedan sin datos.
    /// </summary>
    private async Task<Dictionary<int, SellerView>> SellerViewsAsync(IReadOnlyList<SalesOrder> orders, IReadOnlySet<int> draftIds, CancellationToken ct)
    {
        var result = new Dictionary<int, SellerView>();
        var copied = orders.Where(o => o.SalesCopyAtUtc is not null).ToList();
        if (copied.Count > 0)
        {
            var routeIds = copied.Where(o => o.SalesRouteId is not null).Select(o => o.SalesRouteId!.Value).Distinct().ToList();
            var routes = routeIds.Count == 0 ? new Dictionary<int, string>()
                : await db.Set<SalesRoute>().AsNoTracking().Where(r => routeIds.Contains(r.SalesRouteId)).ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
            var names = await UserNamesAsync(copied.Where(o => o.SellerUserId is not null).Select(o => o.SellerUserId!.Value), ct);
            foreach (var o in copied)
                result[o.SalesOrderId] = new SellerView(o.SalesRouteId, o.SalesRouteId is int r ? routes.GetValueOrDefault(r) : null,
                    o.SellerUserId, o.SellerUserId is int u ? names.GetValueOrDefault(u) : null, false);
        }
        foreach (var o in orders.Where(o => o.SalesCopyAtUtc is null && draftIds.Contains(o.SalesOrderId)))
        {
            var team = await SellerTeamAsync(o.ClientId, o.RequestedBy, ct);
            if (team is null) continue;
            if (o.SellerUserId is int chosen && SalesRouteRules.IsAllowedSellerChoice(chosen, team.Candidates))
            {
                var name = (await UserNamesAsync(new[] { chosen }, ct)).GetValueOrDefault(chosen);
                result[o.SalesOrderId] = new SellerView(team.RouteId, team.RouteName, chosen, name, false);
                continue;
            }
            // sin elección (o la elección ya no vale): lo que se congelaría al solicitar hoy
            var proposed = team.ProposedUserId;
            var proposedName = proposed is int p ? (await UserNamesAsync(new[] { p }, ct)).GetValueOrDefault(p) : null;
            result[o.SalesOrderId] = new SellerView(team.RouteId, team.RouteName, proposed, proposedName, proposed is not null);
        }
        return result;
    }

    /// <summary>Nombres de las rutas guardadas en las órdenes de una página de la lista (solo de las columnas).</summary>
    private async Task<Dictionary<int, string>> RouteNamesAsync(IEnumerable<int> routeIds, CancellationToken ct)
    {
        var ids = routeIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        return await db.Set<SalesRoute>().AsNoTracking().Where(r => ids.Contains(r.SalesRouteId)).ToDictionaryAsync(r => r.SalesRouteId, r => r.Name, ct);
    }
}
