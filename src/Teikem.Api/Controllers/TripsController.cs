using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 5 (P1) — Rutas (Trip): listado del día, alta con número automático (chofer y salida por defecto), ficha, edición de
/// la cabecera, 'Eliminar ruta' y reasignación en bloque del chofer por zona. Módulo LTL_GROUND (núcleo); el servicio exige
/// además CATALOG cuando toca choferes o vehículos. Lectura con trips.view; escritura con trips.plan.
/// El TenantId sale del principal; ninguna solicitud lleva ids internos de la ruta (solo su PublicId).
/// Las órdenes de la ruta, la optimización, el despacho, el monitor y 'Planificar el día' viven en sus propios controladores.
/// </summary>
[ApiController]
[Route("api/v1/trips")]
[Authorize]
[RequireModule(ModuleKeys.LtlGround)]
public sealed class TripsController(TripService trips, TripReadService reader) : ControllerBase
{
    /// <summary>Rutas del día (sin fecha = hoy UTC) o de un rango, con filtros de estatus, zona y chofer y búsqueda libre.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.TripsView)]
    public Task<IReadOnlyList<TripListItemDto>> List([FromQuery] DateOnly? date, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string[]? status, [FromQuery] int? dispatchZoneId, [FromQuery] Guid? driverPublicId, [FromQuery] string? search,
        [FromQuery] bool includeCancelled, CancellationToken ct)
        => reader.ListAsync(new TripListQuery(date, from, to, status, dispatchZoneId, driverPublicId, search, includeCancelled), ct);

    /// <summary>Alta de ruta: número AAAA-####, estatus inicial, chofer por defecto de la zona y salida 12:00 UTC si no se indica.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Create([FromBody] TripCreateRequest req, CancellationToken ct) => trips.CreateAsync(req, ct);

    /// <summary>Ficha: cabecera, paradas de la versión vigente con ETAs y pines, avisos, 'no cupieron' y último ping.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.TripsView)]
    public Task<TripDetailDto> Get(Guid publicId, CancellationToken ct) => reader.GetAsync(publicId, ct);

    /// <summary>Edición de la cabecera (fecha, zona, chofer, vehículo, hora de salida). Acepta rowVersion (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<TripDetailDto> Update(Guid publicId, [FromBody] TripPatchRequest req, CancellationToken ct) => trips.UpdateAsync(publicId, req, ct);

    /// <summary>'Eliminar ruta': pasa a CANCELLED y libera sus órdenes. Solo desde DRAFT/PLANNED. Cuerpo opcional {comment, rowVersion}.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.TripsPlan)]
    public async Task<IActionResult> Delete(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TripCancelRequest? req, CancellationToken ct)
    {
        await trips.CancelAsync(publicId, req, ct);
        return NoContent();
    }

    /// <summary>Reasignación en bloque: el chofer pasa a las rutas abiertas de esas zonas en esa fecha (no toca la zona del chofer).</summary>
    [HttpPost("reassign-zone"), RequirePermission(PermissionCatalog.TripsPlan)]
    public Task<ZoneReassignResultDto> ReassignZone([FromBody] ZoneReassignRequest req, CancellationToken ct) => trips.ReassignZoneAsync(req, ct);
}
