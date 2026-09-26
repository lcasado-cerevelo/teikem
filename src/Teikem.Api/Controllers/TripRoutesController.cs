using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P3): optimización de la ruta (motor detrás de IRouteOptimizer; hoy HEURISTIC), reordenamiento manual con ETAs,
/// pin manual por parada y bitácora de corridas. Módulo núcleo LTL_GROUND. Optimizar exige trips.optimize; reordenar y el
/// pin, trips.plan; consultar las corridas, trips.view. Todo solo en rutas DRAFT/PLANNED (422 si ya fue despachada).
/// Ninguna solicitud lleva TenantId ni ids internos de la ruta; routeStopId se valida contra la versión vigente de ESA ruta.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripRoutesController(RouteOptimizationService routes) : ControllerBase
{
    /// <summary>
    /// Optimiza la ruta: crea la versión n+1 (OPTIMIZED), archiva la anterior, libera lo que no cabe en el vehículo (con su
    /// motivo) y pasa la ruta de DRAFT a PLANNED. 409 si la ruta cambió durante el cálculo o si el motor falló (la corrida
    /// queda registrada con error); 422 si no tiene paradas. Cuerpo opcional (rowVersion).
    /// </summary>
    [HttpPost("{publicId:guid}/optimize"), RequirePermission(PermissionCatalog.TripsOptimize)]
    public Task<OptimizationResultDto> Optimize(Guid publicId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] OptimizeRequest? req, CancellationToken ct)
        => routes.OptimizeAsync(publicId, req, ct);

    /// <summary>Reordena a mano las paradas de la versión vigente (misma versión, sin corrida) y recalcula las ETAs.</summary>
    [HttpPut("{publicId:guid}/route/sequence"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Sequence(Guid publicId, [FromBody] RouteSequenceRequest req, CancellationToken ct)
        => routes.ReorderAsync(publicId, req, ct);

    /// <summary>Pin manual de una parada: fija la coordenada, marca la precisión MANUAL y recalcula las ETAs.</summary>
    [HttpPut("{publicId:guid}/stops/{routeStopId:int}/location"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Location(Guid publicId, int routeStopId, [FromBody] StopLocationRequest req, CancellationToken ct)
        => routes.SetStopLocationAsync(publicId, routeStopId, req, ct);

    /// <summary>Corridas de optimización de la ruta, de la más reciente a la más antigua.</summary>
    [HttpGet("{publicId:guid}/optimization-runs"), RequirePermission(PermissionCatalog.TripsView)]
    public Task<IReadOnlyList<OptimizationRunDto>> Runs(Guid publicId, CancellationToken ct)
        => routes.ListRunsAsync(publicId, ct);
}
