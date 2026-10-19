using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 129 — niveles de zona (Isla/Metro…): catálogo por compañía con su orden y su chofer por omisión. Módulo CATALOG; lectura con fleet.view y escritura con fleet.manage. Baja lógica.
/// </summary>
[ApiController]
[Route("api/v1/zone-groups")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ZoneGroupsController(ZoneGroupService groups) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<ZoneGroupDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => groups.ListAsync(includeInactive, ct);

    [HttpPost, RequirePermission(PermissionCatalog.FleetManage)]
    public Task<ZoneGroupDto> Create([FromBody] ZoneGroupRequest req, CancellationToken ct) => groups.CreateAsync(req, ct);

    /// <summary>Nombre, orden y chofer por omisión; el código no cambia (400).</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<ZoneGroupDto> Update(int id, [FromBody] ZoneGroupRequest req, CancellationToken ct) => groups.UpdateAsync(id, req, ct);

    /// <summary>409 si tiene zonas activas.</summary>
    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<ZoneGroupDto> Deactivate(int id, CancellationToken ct) => groups.SetActiveAsync(id, false, ct);

    [HttpPost("{id:int}/reactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<ZoneGroupDto> Reactivate(int id, CancellationToken ct) => groups.SetActiveAsync(id, true, ct);
}
