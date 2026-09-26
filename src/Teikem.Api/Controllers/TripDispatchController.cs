using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P4) — Sala de despacho y salida: selector de rutas no despachadas con sus bloqueantes, despacho individual y en
/// lote (la ruta se congela y las órdenes avanzan hasta PLANNED) y la salida (la ruta pasa a IN_PROGRESS y las órdenes a
/// IN_TRANSIT; misma costura que usará la app del chofer del Lote 7). Módulo LTL_GROUND; el servicio exige además CATALOG
/// al despachar. Todo con trips.dispatch. El TenantId sale del principal; las rutas se identifican solo por su PublicId.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripDispatchController(TripDispatchService dispatch, TripLifecycleService lifecycle) : ControllerBase
{
    /// <summary>Rutas activas en DRAFT/PLANNED del día (sin fecha = hoy UTC) con avisos, bloqueantes y canDispatch.</summary>
    [HttpGet("dispatchable"), RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<IReadOnlyList<DispatchableTripDto>> Dispatchable([FromQuery] DateOnly? date, CancellationToken ct)
        => dispatch.ListDispatchableAsync(date, ct);

    /// <summary>Despacha una ruta (irreversible). Cuerpo opcional {comment, rowVersion}. 422 con los bloqueantes si no se puede.</summary>
    [HttpPost("{publicId:guid}/dispatch"), RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<TripDetailDto> Dispatch(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TripDispatchRequest? req, CancellationToken ct)
        => dispatch.DispatchAsync(publicId, req, ct);

    /// <summary>Despacho en lote (máximo 50): cada ruta por separado; siempre 200 con el resultado por ruta.</summary>
    [HttpPost("dispatch"), RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<TripBatchDispatchResultDto> DispatchBatch([FromBody] TripBatchDispatchRequest req, CancellationToken ct)
        => dispatch.DispatchBatchAsync(req, ct);

    /// <summary>Salida de una ruta despachada (→ IN_PROGRESS). Cuerpo opcional {comment, rowVersion}.</summary>
    [HttpPost("{publicId:guid}/start"), RequirePermission(PermissionCatalog.TripsDispatch)]
    public Task<TripDetailDto> Start(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TripStartRequest? req, CancellationToken ct)
        => lifecycle.StartAsync(publicId, req, ct);
}
