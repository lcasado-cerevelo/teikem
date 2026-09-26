using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 4 (P2) — Choferes: maestro-detalle de 'Choferes y tarifas' (grupo Catálogo, módulo CATALOG). Lectura con
/// fleet.view y escritura con fleet.manage; vincular o quitar el usuario del chofer exige además admin.users (lo verifica
/// el servicio). Licencias, certificaciones y dispositivos son hijas con id entero SOLO bajo la ruta de su chofer.
/// Las tarifas y los viajes del chofer viven en DriverRatesController / DriverTripsController (driverpay.*).
/// Teléfonos/correos del chofer van por /api/v1/contacts/DRIVER/{id} (Lote 1).
/// </summary>
[ApiController]
[Route("api/v1/drivers")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class DriversController(DriverService drivers, DriverDocumentService documents) : ControllerBase
{
    /// <summary>Lista con buscador libre (código, nombre, zona y área), filtros de estatus y zona primaria.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<DriverListItemDto>> List([FromQuery] string? search, [FromQuery] string[]? status, [FromQuery] int? dispatchZoneId,
        [FromQuery] bool includeInactive, CancellationToken ct)
        => drivers.ListAsync(new DriverListQuery(search, status, dispatchZoneId, includeInactive), ct);

    /// <summary>Alta: código fijo, nombre, zona primaria, tope de paradas, fecha de ingreso y usuario (admin.users). Nace en el estatus inicial.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverDetailDto> Create([FromBody] DriverCreateRequest req, CancellationToken ct) => drivers.CreateAsync(req, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<DriverDetailDto> Get(Guid publicId, CancellationToken ct) => drivers.GetAsync(publicId, ct);

    /// <summary>Edición en línea (null = sin cambio). El código no se cambia (400). Acepta rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverDetailDto> Update(Guid publicId, [FromBody] DriverPatchRequest req, CancellationToken ct) => drivers.UpdateAsync(publicId, req, ct);

    /// <summary>Cambio de estatus (DriverStatus) vía StatusService: ACTIVE ↔ UNAVAILABLE.</summary>
    [HttpPost("{publicId:guid}/status"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverDetailDto> TransitionStatus(Guid publicId, [FromBody] StatusChangeRequest req, CancellationToken ct)
        => drivers.TransitionStatusAsync(publicId, req, ct);

    /// <summary>Checkbox 'Activo' apagado: sale de despacho y conserva todo.</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> Deactivate(Guid publicId, CancellationToken ct) { await drivers.SetActiveAsync(publicId, false, ct); return NoContent(); }

    /// <summary>Checkbox 'Activo' encendido. Un chofer eliminado no se reactiva (409).</summary>
    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> Reactivate(Guid publicId, CancellationToken ct) { await drivers.SetActiveAsync(publicId, true, ct); return NoContent(); }

    /// <summary>'Eliminar chofer': baja definitiva (estatus terminal INACTIVE) sin borrado físico; cuerpo opcional {comment}.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.FleetManage)]
    public async Task<IActionResult> Retire(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DriverRetireRequest? req, CancellationToken ct)
    {
        await drivers.RetireAsync(publicId, req, ct);
        return NoContent();
    }

    // ---------------- Licencias ----------------

    [HttpGet("{publicId:guid}/licenses"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<DriverLicenseDto>> Licenses(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => documents.ListLicensesAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/licenses"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverLicenseDto> AddLicense(Guid publicId, [FromBody] DriverLicenseRequest req, CancellationToken ct)
        => documents.AddLicenseAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/licenses/{id:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverLicenseDto> UpdateLicense(Guid publicId, int id, [FromBody] DriverLicenseRequest req, CancellationToken ct)
        => documents.UpdateLicenseAsync(publicId, id, req, ct);

    [HttpPost("{publicId:guid}/licenses/{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverLicenseDto> DeactivateLicense(Guid publicId, int id, CancellationToken ct)
        => documents.DeactivateLicenseAsync(publicId, id, ct);

    // ---------------- Certificaciones ----------------

    [HttpGet("{publicId:guid}/certifications"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<DriverCertificationDto>> Certifications(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => documents.ListCertificationsAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/certifications"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverCertificationDto> AddCertification(Guid publicId, [FromBody] DriverCertificationRequest req, CancellationToken ct)
        => documents.AddCertificationAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/certifications/{id:int}"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverCertificationDto> UpdateCertification(Guid publicId, int id, [FromBody] DriverCertificationRequest req, CancellationToken ct)
        => documents.UpdateCertificationAsync(publicId, id, req, ct);

    [HttpPost("{publicId:guid}/certifications/{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverCertificationDto> DeactivateCertification(Guid publicId, int id, CancellationToken ct)
        => documents.DeactivateCertificationAsync(publicId, id, ct);

    // ---------------- Dispositivos (los registra la app del Lote 7) ----------------

    /// <summary>Dispositivos del chofer; nunca devuelve el PushToken (solo si existe).</summary>
    [HttpGet("{publicId:guid}/devices"), RequirePermission(PermissionCatalog.FleetView)]
    public Task<IReadOnlyList<DriverDeviceDto>> Devices(Guid publicId, [FromQuery] bool includeInactive, CancellationToken ct)
        => drivers.GetDevicesAsync(publicId, includeInactive, ct);

    [HttpPost("{publicId:guid}/devices/{id:int}/deactivate"), RequirePermission(PermissionCatalog.FleetManage)]
    public Task<DriverDeviceDto> DeactivateDevice(Guid publicId, int id, CancellationToken ct)
        => drivers.DeactivateDeviceAsync(publicId, id, ct);
}
