using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P2) — Zonas de despacho (código, nombre, activo): el nombre de la zona primaria es el 'Área' del chofer.
/// Módulo CATALOG; lectura con fleet.view y escritura con fleet.manage. Nunca DELETE.
/// </summary>
[ApiController]
[Route("api/v1/dispatch-zones")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class DispatchZonesController(DispatchZoneService zones) : ControllerBase
{
    /// <summary>Zonas del tenant con la cantidad de choferes activos asignados.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<DispatchZoneDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => zones.ListAsync(includeInactive, ct);

    [HttpPost, RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DispatchZoneDto> Create([FromBody] DispatchZoneRequest req, CancellationToken ct) => zones.CreateAsync(req, ct);

    /// <summary>Solo el nombre es editable; el código se fija al crear la zona (400 si llega).</summary>
    [HttpPatch("{id:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DispatchZoneDto> Update(int id, [FromBody] DispatchZoneRequest req, CancellationToken ct) => zones.UpdateAsync(id, req, ct);

    /// <summary>Baja lógica; 409 si la zona tiene choferes activos asignados.</summary>
    [HttpPost("{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DispatchZoneDto> Deactivate(int id, CancellationToken ct) => zones.SetActiveAsync(id, false, ct);

    [HttpPost("{id:int}/reactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DispatchZoneDto> Reactivate(int id, CancellationToken ct) => zones.SetActiveAsync(id, true, ct);
}
