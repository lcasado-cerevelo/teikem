using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 (P2): consignatarios y localizaciones (propias de un cliente o compartidas del tenant).
/// Requiere el módulo CATALOG. Nunca hay DELETE: la baja es lógica (deactivate/reactivate).
/// </summary>
[ApiController]
[Route("api/v1/locations")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class LocationsController(LocationService locations) : ControllerBase
{
    /// <summary>Lista. clientId = propias del cliente (+ compartidas si includeShared, default true); sin clientId, todas las del tenant.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.LocationsRead)]
    public Task<IReadOnlyList<LocationDto>> List(
        [FromQuery] Guid? clientId,
        [FromQuery] bool? includeShared,
        [FromQuery] bool includeInactive,
        [FromQuery] string? locationType,
        [FromQuery] string? search,
        CancellationToken ct)
        => locations.GetListAsync(new LocationQuery(clientId, includeShared ?? true, includeInactive, locationType, search), ct);

    [HttpPost, RequirePermission(PermissionCatalog.LocationsCreate)]
    public Task<LocationDto> Create([FromBody] LocationUpsertRequest req, CancellationToken ct) => locations.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.LocationsRead)]
    public Task<LocationDto> Get(Guid publicId, CancellationToken ct) => locations.GetAsync(publicId, ct);

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.LocationsUpdate)]
    public Task<LocationDto> Update(Guid publicId, [FromBody] LocationPatchRequest req, CancellationToken ct) => locations.UpdateAsync(publicId, req, ct);

    /// <summary>Baja lógica (IsActive = 0). Si era el almacén por defecto de su cliente, el recogido vuelve a la corporativa.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.LocationsUpdate)]
    public async Task<IActionResult> Deactivate(Guid publicId, CancellationToken ct) { await locations.SetActiveAsync(publicId, false, ct); return NoContent(); }

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.LocationsUpdate)]
    public async Task<IActionResult> Reactivate(Guid publicId, CancellationToken ct) { await locations.SetActiveAsync(publicId, true, ct); return NoContent(); }
}
