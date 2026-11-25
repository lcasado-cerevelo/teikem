namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 244 — quiénes pueden ser el vendedor de una orden de un cliente: el vendedor preferido (y si es una persona activa), el titular vigente de su ruta (y si es
/// activo), quien crea la orden y las personas vigentes y activas de la ruta (titular y apoyos).
/// </summary>
public sealed record SellerCandidates(int? Preferred, bool PreferredActive, int? Primary, bool PrimaryActive, int? RequestedBy, IReadOnlySet<int> ActiveRoutePeople);

/// <summary>
/// Lote 244 — a quién se le cuenta una venta: un representante (RepId) o un usuario (UserId). Un usuario que es representante se normaliza a ese representante
/// (<see cref="SalesRouteRules.NormalizePerson"/>). Key = «R{id}» (representante) o «U{id}» (usuario); vacío si no hay ninguno.
/// </summary>
public sealed record SalesPersonRef(int? RepId, int? UserId)
{
    public string Key => RepId is int r ? "R" + r : UserId is int u ? "U" + u : string.Empty;
    public bool IsEmpty => RepId is null && UserId is null;
}

/// <summary>
/// Lote 244 — lo que hace falta para atribuir una línea de venta: la copia de la orden (ruta y vendedor congelados al solicitar; NULL = borrador u orden anterior al
/// lote), la ruta directa del cliente, su representante heredado (si sigue activo y su ruta) y el día de la línea.
/// </summary>
public sealed record AttributionInput(int? OrderRouteId, int? OrderSellerUserId, int? ClientRouteId, int? ClientRepId, bool ClientRepActive, int? ClientRepRouteId, DateOnly LineDate);

/// <summary>Lote 244 — resultado de la atribución: la ruta (NULL = «Sin ruta») y la persona (NULL = «Sin vendedor»).</summary>
public sealed record SalesAttribution(int? RouteId, SalesPersonRef? Person);

/// <summary>Lote 244 — un tramo de titular de una ruta: [ValidFrom, ValidTo). Las asignaciones anuladas no forman tramos.</summary>
public sealed record SalesPrimarySpan(int UserId, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>
/// Lote 244 — la línea de tiempo de titulares por ruta, para el «primaryOn» de la atribución (el titular vigente EL DÍA DE LA LÍNEA).
/// </summary>
public sealed class SalesPrimaryTimeline(IReadOnlyDictionary<int, IReadOnlyList<SalesPrimarySpan>> spans)
{
    public static readonly SalesPrimaryTimeline Empty = new(new Dictionary<int, IReadOnlyList<SalesPrimarySpan>>());

    public IReadOnlyDictionary<int, IReadOnlyList<SalesPrimarySpan>> Spans { get; } = spans;

    /// <summary>El titular de la ruta ese día (NULL = la ruta no tenía titular).</summary>
    public int? On(int routeId, DateOnly day)
        => Spans.TryGetValue(routeId, out var list) ? SalesRouteRules.PrimaryOn(list, day) : null;
}

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — reglas PURAS de las rutas de venta (sin BD; las prueba xunit):
/// - Vigencias en días de la compañía, intervalo medio abierto [ValidFrom, ValidTo); sin fechas futuras; un titular nuevo no empieza antes que el actual.
/// - Vendedor propuesto de una orden: el preferido activo → el titular vigente activo → quien crea la orden, SOLO si es titular o apoyo vigente de la ruta → ninguno.
///   En el borrador se puede escoger entre el preferido y las personas vigentes de la ruta; al solicitar se congela la elección si sigue valiendo (si no, la propuesta).
/// - Atribución de una venta: ruta = la copia de la orden → la ruta directa del cliente → la de su representante heredado; persona = el vendedor copiado → el
///   representante heredado (activo y de la misma ruta) → el titular vigente el día de la línea → «Sin vendedor».
/// - Vendedor preferido del cliente: una persona activa de la ruta del cliente; un cliente sin ruta no admite preferido. Si después deja de ser de la ruta no se bloquea
///   nada: la ficha del cliente lo señala.
/// </summary>
public static class SalesRouteRules
{
    // ---------------------------------------------------------------- mensajes exactos (el manual y la FAQ los citan)
    public const string RoleInvalid = "El papel debe ser PRIMARY (titular) o SUPPORT (apoyo).";
    public const string UserRequired = "Escoja el usuario.";
    public const string UserNotMember = "El usuario no pertenece a esta compañía.";
    public const string UserSuspended = "El usuario no está activo en esta compañía: actívelo o escoja otro.";
    public const string RepUserRequired = "Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem.";
    public static string RepUserTaken(string repName) => $"El usuario ya es el representante «{repName}».";
    public const string ValidFromFuture = "La fecha de inicio no puede ser futura.";
    public static string ValidFromBeforePrimary(DateOnly primaryFrom) => $"La fecha de inicio no puede ser anterior a la del titular actual ({primaryFrom:yyyy-MM-dd}).";
    public const string AlreadyPrimary = "La persona ya es titular de esta ruta.";
    public const string AlreadySupport = "La persona ya es apoyo de esta ruta.";
    public const string PrimaryCannotBeSupport = "La persona es la titular de esta ruta: cierre su asignación antes de ponerla de apoyo.";
    /// <summary>Para NotFoundException («Asignación '{id}' no encontrada.»).</summary>
    public const string AssignmentNotFound = "Asignación";
    public const string AssignmentClosed = "La asignación ya está cerrada.";
    public const string AssignmentVoided = "La asignación ya está anulada.";
    public const string ValidToBeforeFrom = "La fecha de cierre no puede ser anterior a la de inicio.";
    public const string ValidToFuture = "La fecha de cierre no puede ser futura.";
    public const string PeopleChangedConcurrently = "Otra persona cambió las personas de la ruta al mismo tiempo: recargue e intente de nuevo.";
    public const string RouteHasClients = "La ruta tiene clientes asignados: páselos a otra ruta antes de desactivarla.";
    public const string QuotaByInvalid = "La cuota se lleva por ruta (ROUTE) o por persona (REP).";
    public const string QuotaTargetInvalid = "Cada renglón lleva el representante (repId) o la ruta (routeId), uno de los dos.";
    public const string SellerNotAllowed = "El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.";
    /// <summary>Ajuste del dueño (2026-11-25): el preferido se escoge entre las personas activas de la ruta del cliente.</summary>
    public const string PreferredNotInRoute = "El vendedor preferido debe ser una persona activa de la ruta del cliente.";
    public const string PreferredNeedsRoute = "El cliente necesita una ruta antes de asignarle un vendedor preferido.";
    /// <summary>Aviso (no bloquea) cuando el preferido ya no es persona activa de la ruta del cliente; la web lo muestra con su propio texto es/en.</summary>
    public const string PreferredOutOfRouteNotice = "El vendedor preferido ya no pertenece a la ruta del cliente.";

    // ---------------------------------------------------------------- etiquetas de grupos (en las respuestas del API)
    public const string NoSellerName = "Sin vendedor";
    public const string NoRouteName = "Sin ruta";

    // ---------------------------------------------------------------- avisos con conteo y nombres (Estado, Configuración)
    public const int MaxNamesInWarning = 10;

    public static string RoutesWithoutPrimary(int count, IEnumerable<string> names)
        => (count == 1 ? "1 ruta activa sin titular: " : $"{count} rutas activas sin titular: ") + NameList(names) + ".";

    public static string RoutesWithInactivePrimary(int count, IEnumerable<string> names)
        => (count == 1 ? "1 ruta con el titular inactivo: " : $"{count} rutas con el titular inactivo: ") + NameList(names) + ".";

    public static string RepsWithoutUser(int count, IEnumerable<string> names)
        => (count == 1 ? "1 representante sin usuario: " : $"{count} representantes sin usuario: ") + NameList(names)
           + ". El vendedor siempre es un usuario de Teikem: el administrador crea el usuario y se le pone al representante.";

    public static string ClientsWithoutRoute(int count)
        => count == 1 ? "1 cliente activo sin ruta: asígnele una ruta." : $"{count} clientes activos sin ruta: asígneles una ruta.";

    public static string UserInSeveralReps(string userName, IEnumerable<string> repNames)
        => $"El usuario «{userName}» está en varios representantes activos ({NameList(repNames)}): su venta se cuenta en el primero que se creó.";

    /// <summary>«A, B y C»; con más de <see cref="MaxNamesInWarning"/>, «A, B, … y N más».</summary>
    public static string NameList(IEnumerable<string> names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (list.Count == 0) return "-";
        if (list.Count > MaxNamesInWarning)
            return string.Join(", ", list.Take(MaxNamesInWarning)) + $" y {list.Count - MaxNamesInWarning} más";
        return list.Count == 1 ? list[0] : string.Join(", ", list.Take(list.Count - 1)) + " y " + list[^1];
    }

    // ---------------------------------------------------------------- vigencias

    /// <summary>¿La asignación cuenta ese día? Activa (no anulada) y day en [validFrom, validTo).</summary>
    public static bool IsCurrent(DateOnly validFrom, DateOnly? validTo, bool isActive, DateOnly day)
        => isActive && validFrom <= day && (validTo is not DateOnly to || day < to);

    /// <summary>Fecha de inicio de una asignación: no futura y, si hay titular vigente (al poner uno nuevo), no antes que la suya. NULL = vale.</summary>
    public static string? CheckValidFrom(DateOnly validFrom, DateOnly today, DateOnly? currentPrimaryFrom)
    {
        if (validFrom > today) return ValidFromFuture;
        if (currentPrimaryFrom is DateOnly p && validFrom < p) return ValidFromBeforePrimary(p);
        return null;
    }

    /// <summary>Fecha de cierre: no antes del inicio y no futura. NULL = vale.</summary>
    public static string? CheckClose(DateOnly validFrom, DateOnly validTo, DateOnly today)
    {
        if (validTo < validFrom) return ValidToBeforeFrom;
        if (validTo > today) return ValidToFuture;
        return null;
    }

    /// <summary>El titular de una lista de tramos ese día (si se traslapan por datos viejos, el que empezó más tarde).</summary>
    public static int? PrimaryOn(IEnumerable<SalesPrimarySpan> spans, DateOnly day)
        => spans.Where(s => IsCurrent(s.ValidFrom, s.ValidTo, true, day)).OrderByDescending(s => s.ValidFrom).Select(s => (int?)s.UserId).FirstOrDefault();

    // ---------------------------------------------------------------- papel

    /// <summary>«PRIMARY» → true, «SUPPORT» → false (sin distinguir mayúsculas); otro valor → null.</summary>
    public static bool? ParseRole(string? raw)
        => raw?.Trim().ToUpperInvariant() switch
        {
            SalesRouteCodes.RolePrimary => true,
            SalesRouteCodes.RoleSupport => false,
            _ => null,
        };

    public static string RoleOf(bool isPrimary) => isPrimary ? SalesRouteCodes.RolePrimary : SalesRouteCodes.RoleSupport;

    // ---------------------------------------------------------------- vendedor de la orden

    /// <summary>El vendedor propuesto: el preferido activo → el titular activo → quien crea la orden, si es persona vigente y activa de la ruta → null.</summary>
    public static int? ProposeSeller(SellerCandidates c)
    {
        if (c.Preferred is int p && c.PreferredActive) return p;
        if (c.Primary is int t && c.PrimaryActive) return t;
        if (c.RequestedBy is int r && c.ActiveRoutePeople.Contains(r)) return r;
        return null;
    }

    /// <summary>De dónde sale ese vendedor: PREFERRED, PRIMARY o SUPPORT (null = no es ninguno de los tres).</summary>
    public static string? SellerKindOf(int? userId, SellerCandidates c)
    {
        if (userId is not int u) return null;
        if (c.Preferred == u) return SalesRouteCodes.SellerKindPreferred;
        if (c.Primary == u) return SalesRouteCodes.SellerKindPrimary;
        return c.ActiveRoutePeople.Contains(u) ? SalesRouteCodes.SellerKindSupport : null;
    }

    /// <summary>¿Se puede escoger a esa persona como vendedor en el borrador? El preferido activo o una persona vigente y activa de la ruta.</summary>
    public static bool IsAllowedSellerChoice(int userId, SellerCandidates c)
        => (c.Preferred == userId && c.PreferredActive) || c.ActiveRoutePeople.Contains(userId);

    /// <summary>El vendedor que se congela al solicitar: la elección, si sigue valiendo y es una persona activa; si no, la propuesta.</summary>
    public static int? FreezeSeller(int? chosen, bool chosenAllowedAndActive, SellerCandidates c)
        => chosen is int x && chosenAllowedAndActive ? x : ProposeSeller(c);

    // ---------------------------------------------------------------- vendedor preferido del cliente

    /// <summary>
    /// Al ASIGNAR el preferido (ajuste del dueño, 2026-11-25): el cliente necesita ruta (directa o heredada) y la persona debe ser activa y vigente en esa ruta.
    /// NULL = vale. Quitar el preferido siempre vale (no pasa por aquí).
    /// </summary>
    public static string? CheckPreferred(int? clientRouteId, bool isActiveRoutePerson)
    {
        if (clientRouteId is null) return PreferredNeedsRoute;
        return isActiveRoutePerson ? null : PreferredNotInRoute;
    }

    /// <summary>¿El preferido ya no es persona activa de la ruta del cliente? Solo señala (la ficha muestra el aviso); no bloquea nada.</summary>
    public static bool PreferredOutOfRoute(int? preferred, IReadOnlySet<int> activeRoutePeople)
        => preferred is int p && !activeRoutePeople.Contains(p);

    // ---------------------------------------------------------------- atribución de la venta

    /// <summary>
    /// Ruta y persona de una línea de venta.
    /// - Ruta = la copia de la orden, si tiene valor → la ruta directa del cliente → la de su representante heredado (solo si el cliente no tiene ruta directa).
    /// - Persona = el vendedor copiado, si tiene valor → el representante heredado, si está activo y su ruta es la ruta resuelta (un representante de otra ruta no se
    ///   usa) → el titular de la ruta el día de la línea (<paramref name="primaryOn"/>) → null («Sin vendedor»).
    /// </summary>
    public static SalesAttribution Attribute(AttributionInput i, Func<int, DateOnly, int?> primaryOn)
    {
        var route = i.OrderRouteId ?? i.ClientRouteId ?? i.ClientRepRouteId;
        if (i.OrderSellerUserId is int seller) return new SalesAttribution(route, new SalesPersonRef(null, seller));
        if (i.ClientRepId is int rep && i.ClientRepActive && i.ClientRepRouteId == route) return new SalesAttribution(route, new SalesPersonRef(rep, null));
        if (route is int r && primaryOn(r, i.LineDate) is int primary) return new SalesAttribution(route, new SalesPersonRef(null, primary));
        return new SalesAttribution(route, null);
    }

    /// <summary>Un usuario que es representante cuenta como ese representante (paridad con el Lote 241). Lo demás queda igual.</summary>
    public static SalesPersonRef? NormalizePerson(SalesPersonRef? p, IReadOnlyDictionary<int, int> userToRep)
    {
        if (p is null || p.RepId is not null || p.UserId is not int u) return p;
        return userToRep.TryGetValue(u, out var rep) ? new SalesPersonRef(rep, u) : p;
    }

    // ---------------------------------------------------------------- cuota por ruta o por persona

    public static bool IsValidQuotaBy(string? raw) => ParseQuotaBy(raw) is not null;

    /// <summary>«REP» o «ROUTE» (sin distinguir mayúsculas ni espacios); vacío u otro valor → null.</summary>
    public static string? ParseQuotaBy(string? raw)
        => raw?.Trim().ToUpperInvariant() switch
        {
            SalesRouteCodes.QuotaByRep => SalesRouteCodes.QuotaByRep,
            SalesRouteCodes.QuotaByRoute => SalesRouteCodes.QuotaByRoute,
            _ => null,
        };
}
