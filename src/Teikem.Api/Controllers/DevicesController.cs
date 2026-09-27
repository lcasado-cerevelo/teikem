using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 8A (P1) — aparatos de confianza de la app de almacén (UserDevice) bajo el módulo WMS_LOTSERIAL, todo con
/// devices.manage. El aparato se expone por PublicId; uno de otra compañía es 404 'Aparato no encontrado.'. El código de
/// registro (8 caracteres, 24 h, un solo uso) se devuelve en claro UNA sola vez al dar de alta o al regenerarlo.
/// Registro en el aparato y heartbeat son anónimos: ver DeviceEnrollmentController.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class DevicesController(DeviceService devices) : ControllerBase
{
    /// <summary>Aparatos de la compañía por código; por defecto solo los activos (?includeInactive=true para todos).</summary>
    [HttpGet, RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<IReadOnlyList<DeviceDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => devices.ListAsync(includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceDto> Get(Guid publicId, CancellationToken ct) => devices.GetAsync(publicId, ct);

    /// <summary>
    /// Alta: código obligatorio (hasta 30), único por compañía (409 'Ya existe un aparato con ese código.'); almacén por
    /// defecto y tema LIGHT | DARK (LIGHT si no se indica). Devuelve el código de registro en claro una sola vez.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceCreatedDto> Create([FromBody] DeviceCreateRequest req, CancellationToken ct) => devices.CreateAsync(req, ct);

    /// <summary>Edición: nombre, almacén por defecto (o quitarlo) y tema; null = sin cambio. El código no cambia.</summary>
    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceDto> Update(Guid publicId, [FromBody] DevicePatchRequest req, CancellationToken ct) => devices.UpdateAsync(publicId, req, ct);

    /// <summary>Desactiva el aparato y revoca todas las sesiones emitidas a él (deja de sincronizar).</summary>
    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceDto> Deactivate(Guid publicId, CancellationToken ct) => devices.DeactivateAsync(publicId, ct);

    /// <summary>Reactiva el aparato; cada usuario vuelve a entrar con su PIN.</summary>
    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceDto> Reactivate(Guid publicId, CancellationToken ct) => devices.ReactivateAsync(publicId, ct);

    /// <summary>Regenera el código de registro (el anterior deja de servir); 422 si el aparato está desactivado.</summary>
    [HttpPost("{publicId:guid}/enroll-code"), RequirePermission(PermissionCatalog.DevicesManage)]
    public Task<DeviceCreatedDto> RegenerateEnrollCode(Guid publicId, CancellationToken ct) => devices.RegenerateEnrollCodeAsync(publicId, ct);
}

/// <summary>
/// Lote 8A (P1) — endpoints anónimos del aparato (sin JWT): se autentican con el código de registro o con aparato + secreto.
/// El módulo WMS_LOTSERIAL se verifica en el servicio contra la compañía del aparato (no hay tenant en el principal).
/// </summary>
[ApiController]
[Route("api/v1/devices")]
public sealed class DeviceEnrollmentController(DeviceService devices) : ControllerBase
{
    /// <summary>
    /// Registro del aparato con el código de un solo uso: devuelve el PublicId y el secreto del aparato (UNA sola vez), la
    /// compañía, el almacén por defecto y el tema. Código inválido o vencido → 401 'El código de registro no es válido o venció.'.
    /// Límite de intentos: 10 por minuto por IP → 429 'Demasiados intentos; espere un minuto e intente de nuevo.'.
    /// </summary>
    [HttpPost("enroll"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Enroll)]
    public Task<DeviceEnrolledDto> Enroll([FromBody] DeviceEnrollRequest req, CancellationToken ct) => devices.EnrollAsync(req, ct);

    /// <summary>
    /// Aparato + secreto → registra LastSeenUtc y AppVersion; devuelve { isActive, defaultWarehousePublicId, theme,
    /// serverTimeUtc }. Un aparato desactivado recibe isActive = false; secreto inválido → 401.
    /// </summary>
    [HttpPost("heartbeat"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.DeviceAuth)]
    public Task<DeviceHeartbeatDto> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct) => devices.HeartbeatAsync(req, ct);
}
