using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 8A (P1) — PIN de otros usuarios para los aparatos de almacén (pantalla Usuarios). Requiere devices.manage o
/// admin.users (lo valida el servicio: 403 'Falta el permiso …'); el usuario debe ser interno y de la misma compañía (404
/// 'Usuario no encontrado.'). El resto de /api/v1/users vive en UsersController (SecurityControllers.cs); la lista de
/// usuarios expone hasPin.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class UserPinsController(PinService pins) : ControllerBase
{
    /// <summary>Asigna o restablece el PIN del usuario (4 a 6 dígitos, no trivial); reinicia intentos y bloqueo.</summary>
    [HttpPut("{id:int}/pin")]
    public Task<PinStatusDto> SetPin(int id, [FromBody] PinAdminSetRequest req, CancellationToken ct) => pins.SetForUserAsync(id, req, ct);

    /// <summary>Quita el PIN del usuario y cierra sus sesiones en los aparatos de la compañía.</summary>
    [HttpDelete("{id:int}/pin")]
    public async Task<IActionResult> RemovePin(int id, CancellationToken ct) { await pins.RemoveForUserAsync(id, ct); return NoContent(); }
}
