using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P1) — panel Vehículos y documentos de vehículo. Vive bajo el módulo CATALOG ('Clientes y contratos, choferes y
/// tarifas, flota'); lectura con fleet.view y escritura con fleet.manage. Las rutas usan el PublicId del vehículo; los
/// documentos se exponen con id entero SOLO bajo su vehículo (404 si el documento es de otro vehículo u otro tenant).
/// Historial de estatus: /api/v1/status/history/VEHICLE/{id}. Contactos y campos personalizados: VEHICLE (Lote 1).
/// </summary>
[ApiController]
[Route("api/v1/vehicles")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class VehiclesController(VehicleService vehicles, VehicleDocumentService documents) : ControllerBase
{
    /// <summary>Lista con buscador libre (search) y filtros multi-valor por código (?vehicleType=VAN&amp;vehicleType=TRUCK o VAN,TRUCK).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<VehicleListItemDto>> List([FromQuery] string? search, [FromQuery] string[]? vehicleType, [FromQuery] string[]? ownership,
        [FromQuery] string[]? fuelType, [FromQuery] string[]? status, [FromQuery] bool includeInactive, CancellationToken ct)
        => vehicles.ListAsync(new VehicleListQuery(search, NullIfEmpty(vehicleType), NullIfEmpty(ownership), NullIfEmpty(fuelType), NullIfEmpty(status), includeInactive), ct);

    /// <summary>Alta: el código es obligatorio, único por compañía e inmutable; nace en el estatus inicial (ACTIVE).</summary>
    [HttpPost, RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDetailDto> Create([FromBody] VehicleCreateRequest req, CancellationToken ct) => vehicles.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<VehicleDetailDto> Get(Guid publicId, CancellationToken ct) => vehicles.GetAsync(publicId, ct);

    /// <summary>Edición en línea: null = sin cambio, "" = quitar el valor; rowVersion opcional (409 si cambió). code no se edita.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDetailDto> Update(Guid publicId, [FromBody] VehiclePatchRequest req, CancellationToken ct) => vehicles.UpdateAsync(publicId, req, ct);

    /// <summary>Cambio de estatus (VehicleStatus) vía StatusService: ACTIVE ↔ MAINTENANCE y → INACTIVE (baja definitiva).</summary>
    [HttpPost("{publicId:guid}/status"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDetailDto> TransitionStatus(Guid publicId, [FromBody] StatusChangeRequest req, CancellationToken ct)
        => vehicles.TransitionStatusAsync(publicId, req, ct);

    /// <summary>Checkbox Activo: baja lógica reversible (IsActive = 0). Nunca DELETE.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> Deactivate(Guid publicId, CancellationToken ct) { await vehicles.SetActiveAsync(publicId, false, ct); return NoContent(); }

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> Reactivate(Guid publicId, CancellationToken ct) { await vehicles.SetActiveAsync(publicId, true, ct); return NoContent(); }

    // ---------------------------------------------------------------- documentos

    [HttpGet("{publicId:guid}/documents"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<VehicleDocumentDto>> Documents(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => documents.ListAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/documents"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDocumentDto> AddDocument(Guid publicId, [FromBody] VehicleDocumentRequest req, CancellationToken ct)
        => documents.AddAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/documents/{id:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDocumentDto> UpdateDocument(Guid publicId, int id, [FromBody] VehicleDocumentRequest req, CancellationToken ct)
        => documents.UpdateAsync(publicId, id, req, ct);

    [HttpPost("{publicId:guid}/documents/{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<VehicleDocumentDto> DeactivateDocument(Guid publicId, int id, CancellationToken ct)
        => documents.DeactivateAsync(publicId, id, ct);

    private static string[]? NullIfEmpty(string[]? values) => values is { Length: > 0 } ? values : null;
}
