using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 8A (P1) — PIN de otros usuarios para los aparatos de almacén (pantalla Usuarios). Requiere devices.manage o
/// admin.users: la política perm:devices.manage|admin.users rechaza ANTES que [RequireAal2] (403 forbidden y SecurityEvent
/// PERMISSION_DENIED, no 'requiere AAL2'); PinService lo vuelve a validar (defensa en profundidad). El usuario debe ser interno y de la misma compañía (404
/// 'Usuario no encontrado.') y sin más permisos que quien lo cambia (403 'No puede asignar ni quitar el PIN de un usuario
/// con más permisos que usted.'). Asignarlo exige AAL2 (reauth reciente), como roles y permisos del usuario. El resto de /api/v1/users vive en UsersController (SecurityControllers.cs); la lista de
/// usuarios expone hasPin.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
[RequirePermission(PermissionCatalog.DevicesManage + "|" + PermissionCatalog.AdminUsers)]
public sealed class UserPinsController(PinService pins) : ControllerBase
{
    /// <summary>
    /// Asigna o restablece el PIN del usuario (4 a 6 dígitos, no trivial); reinicia intentos y bloqueo y, si ya tenía PIN,
    /// cierra sus sesiones en aparatos (su refresh responde 401).
    /// </summary>
    [HttpPut("{id:int}/pin"), RequireAal2]
    public Task<PinStatusDto> SetPin(int id, [FromBody] PinAdminSetRequest req, CancellationToken ct) => pins.SetForUserAsync(id, req, ct);

    /// <summary>Quita el PIN del usuario y cierra sus sesiones en los aparatos de la compañía.</summary>
    [HttpDelete("{id:int}/pin")]
    public async Task<IActionResult> RemovePin(int id, CancellationToken ct) { await pins.RemoveForUserAsync(id, ct); return NoContent(); }
}
