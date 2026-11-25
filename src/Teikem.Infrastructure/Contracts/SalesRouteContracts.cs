namespace Teikem.Infrastructure.Contracts;

// Lote 244 (Lote A del plan de rutas) — personas de la ruta (titular y apoyo), la ruta y el vendedor preferido de cada cliente y «Pasar rutas desde representantes»
// (/api/v1/sales-analysis). Fechas en días de la compañía («AAAA-MM-DD»), intervalo medio abierto [ValidFrom, ValidTo). El papel se expone como PRIMARY (titular) o
// SUPPORT (apoyo).

// ---------------------------------------------------------------- personas de la ruta

/// <summary>
/// Una persona en la ruta: Role PRIMARY | SUPPORT; IsCurrent = vigente (activa y sin fecha de cierre); IsVoided = anulada («registrada por error», solo sale con
/// includeVoided); UserIsActiveMember = el usuario sigue siendo persona activa de la compañía (usuario activo y membresía ACTIVE).
/// </summary>
public sealed record SalesRouteAssignmentDto(int Id, int UserId, string UserName, string Role, DateOnly ValidFrom, DateOnly? ValidTo, bool IsCurrent, bool IsVoided,
    bool UserIsActiveMember);

/// <summary>Personas de una ruta: el titular vigente (null = sin titular), los apoyos vigentes y el historial cerrado (ValidFrom descendente; vacío sin history).</summary>
public sealed record SalesRoutePeopleDto(int RouteId, string RouteName, bool RouteIsActive, SalesRouteAssignmentDto? Primary,
    IReadOnlyList<SalesRouteAssignmentDto> Supports, IReadOnlyList<SalesRouteAssignmentDto> History);

/// <summary>Poner a una persona en la ruta: Role PRIMARY | SUPPORT; ValidFrom null = hoy (no puede ser futura).</summary>
public sealed record SalesRouteAssignRequest(int? UserId, string? Role, DateOnly? ValidFrom = null);

/// <summary>Cerrar una asignación: ValidTo null = hoy (ni futura ni anterior a su inicio). RowVersion opcional (409 si cambió).</summary>
public sealed record SalesRouteCloseRequest(DateOnly? ValidTo = null, string? RowVersion = null);

/// <summary>Una persona activa de la compañía que se puede poner en una ruta.</summary>
public sealed record SalesSellerCandidateDto(int UserId, string Name, string? Email);

// ---------------------------------------------------------------- venta del cliente

/// <summary>Una persona del equipo de venta del cliente: IsActive = sigue siendo persona activa de la compañía; ValidFrom = desde cuándo está en la ruta (null = no está).</summary>
public sealed record SalesTeamPersonDto(int UserId, string Name, bool IsActive, DateOnly? ValidFrom);

/// <summary>
/// La venta de un cliente:
/// - RouteId / RouteName: la ruta efectiva (la directa o, si no tiene, la de su representante con RouteFromRep = true; null = «Sin ruta»). DirectRouteId = la directa.
/// - Primary y Supports: titular y apoyos vigentes de la ruta.
/// - Preferred: el vendedor preferido (con si sigue activo); PreferredOutOfRoute = ya no es persona activa de la ruta del cliente (solo se señala, no bloquea).
/// - ProposedUserId / ProposedName / ProposedKind: el vendedor que se propone en sus órdenes (PREFERRED | PRIMARY | SUPPORT; null = «Sin vendedor»).
/// - RepId / RepName / RepRouteId / RepRouteName / RepIsActive: el representante heredado del Lote 241 (solo lectura).
/// - RowVersion: el del cliente, para el PUT.
/// </summary>
public sealed record SalesClientSalesDto(
    int ClientId, Guid PublicId, string Code, string Name,
    int? RouteId, string? RouteName, bool RouteFromRep, int? DirectRouteId,
    SalesTeamPersonDto? Primary, IReadOnlyList<SalesTeamPersonDto> Supports,
    SalesTeamPersonDto? Preferred, bool PreferredOutOfRoute,
    int? ProposedUserId, string? ProposedName, string? ProposedKind,
    int? RepId, string? RepName, int? RepRouteId, string? RepRouteName, bool RepIsActive,
    string? RowVersion);

/// <summary>
/// Cambiar la ruta directa y el vendedor preferido del cliente: null = sin cambio; ClearRoute / ClearPreferred los quitan. El preferido nuevo debe ser persona activa
/// (titular o apoyo vigente) de la ruta del cliente, ya con el cambio de ruta aplicado; un cliente sin ruta no admite preferido. RowVersion opcional (409 si cambió).
/// </summary>
public sealed record SalesClientSalesRequest(int? RouteId = null, bool ClearRoute = false, int? PreferredUserId = null, bool ClearPreferred = false, string? RowVersion = null);

/// <summary>Poner (o quitar, RouteId null) la ruta directa de varios clientes a la vez (hasta 2000).</summary>
public sealed record SalesClientRouteBulkRequest(IReadOnlyList<int>? ClientIds, int? RouteId);

/// <summary>Cuántos clientes cambiaron de ruta y a cuál.</summary>
public sealed record SalesClientRouteBulkResultDto(int Updated, int? RouteId, string? RouteName);

// ---------------------------------------------------------------- pasar rutas desde representantes

/// <summary>Un representante activo que hay que atender (sin usuario, o en una ruta con varios representantes con usuario).</summary>
public sealed record SalesRepAlertDto(int RepId, string RepName, int? RouteId, string? RouteName);

/// <summary>
/// Resultado de «Pasar rutas desde representantes» (con DryRun no se escribió nada):
/// - ClientsRouted: clientes sin ruta que reciben la de su representante;
/// - PrimariesSet: rutas activas sin titular con UN solo representante con usuario activo, que queda de titular;
/// - SupportsSet: personas puestas de apoyo en rutas sin titular con varios representantes con usuario activo;
/// - ValidFrom: el día desde el que cuentan (el primer día del mes más antiguo con historial, factura o cuota; sin datos, hoy);
/// - RepsWithoutUser y RoutesWithSeveralReps: lo que el administrador tiene que resolver a mano.
/// </summary>
public sealed record SalesRoutesFromRepsDto(bool DryRun, int ClientsRouted, int PrimariesSet, int SupportsSet, DateOnly ValidFrom,
    IReadOnlyList<SalesRepAlertDto> RepsWithoutUser, IReadOnlyList<SalesRepAlertDto> RoutesWithSeveralReps);
