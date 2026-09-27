using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Sesión actual (/me) y, desde el Lote 8A, el PIN propio para entrar en los aparatos de almacén (Mi cuenta). El PIN es
/// por compañía activa y solo existe con el módulo WMS_LOTSERIAL encendido.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(UserAdminService users, PinService pins) : ControllerBase
{
    /// <summary>Sesión actual: tenant activo, membresías (selector de compañía solo si hay más de una), permisos efectivos y módulos encendidos (menú).</summary>
    [HttpGet]
    public Task<MeDto> Get(CancellationToken ct) => users.GetMeAsync(ct);

    /// <summary>Estado del PIN propio (definido, bloqueado hasta, última actualización). El PIN nunca se devuelve.</summary>
    [HttpGet("pin"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PinStatusDto> GetPin(CancellationToken ct) => pins.GetMineAsync(ct);

    /// <summary>
    /// Define o cambia el PIN propio: exige la contraseña actual (400 'La contraseña actual es incorrecta.'); PIN de 4 a 6
    /// dígitos (400 'El PIN debe tener de 4 a 6 dígitos.') y no trivial (400 'El PIN no puede ser una secuencia trivial.').
    /// Reinicia el contador de intentos y el bloqueo.
    /// </summary>
    [HttpPut("pin"), RequireModule(ModuleKeys.WmsLotSerial)]
    public Task<PinStatusDto> SetPin([FromBody] PinSetRequest req, CancellationToken ct) => pins.SetMineAsync(req, ct);

    /// <summary>Quita el PIN propio y cierra las sesiones del usuario en los aparatos de la compañía.</summary>
    [HttpDelete("pin"), RequireModule(ModuleKeys.WmsLotSerial)]
    public async Task<IActionResult> RemovePin(CancellationToken ct) { await pins.RemoveMineAsync(ct); return NoContent(); }
}
