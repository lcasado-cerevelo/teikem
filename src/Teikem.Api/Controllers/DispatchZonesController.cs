using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P2) — Zonas de despacho (código, nombre, activo): el nombre de la zona primaria es el 'Área' del chofer.
/// Módulo CATALOG; lectura con fleet.view y escritura con fleet.manage. La zona nunca se borra (baja lógica).
/// Lote 5 (P5) — Miembros de la zona (código postal, rango postal, municipio) sin solapamiento entre zonas activas y
/// resolución 'ZIP/pueblo → zona'. Quitar un miembro sí es un DELETE físico auditado.
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

    // ---------------- Lote 5: miembros y resolución ----------------

    /// <summary>Resolución 'ZIP/pueblo → zona' (CP &gt; rango postal &gt; municipio; empate = ambigua). 400 sin parámetros.</summary>
    [HttpGet("resolve"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<ZoneResolutionDto> Resolve([FromQuery] string? postalCode, [FromQuery] string? city, CancellationToken ct)
        => zones.ResolveAsync(postalCode, city, ct);

    /// <summary>Miembros de la zona (activa o no).</summary>
    [HttpGet("{id:int}/members"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<DispatchZoneMembersDto> ListMembers(int id, CancellationToken ct) => zones.ListMembersAsync(id, ct);

    /// <summary>Agrega un criterio (POSTAL_CODE, POSTAL_RANGE o MUNICIPALITY); 409 si choca con otra zona activa.</summary>
    [HttpPost("{id:int}/members"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DispatchZoneMembersDto> AddMember(int id, [FromBody] DispatchZoneMemberRequest req, CancellationToken ct)
        => zones.AddMemberAsync(id, req, ct);

    /// <summary>Quita un criterio de ESA zona (DELETE físico auditado); 404 si el miembro es de otra zona.</summary>
    [HttpDelete("{id:int}/members/{memberId:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> RemoveMember(int id, int memberId, CancellationToken ct)
    {
        await zones.RemoveMemberAsync(id, memberId, ct);
        return NoContent();
    }
}
