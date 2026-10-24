using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 213 — la app móvil para el personal (la misma app del chofer, que crece). Todo con mobile.use y sin módulo: cada pantalla de la app pide sus propios datos a los endpoints de siempre
/// (atención, pulso, órdenes, mensajes…) con los permisos de siempre; aquí solo va lo propio del teléfono.
/// </summary>
[ApiController]
[Route("api/v1/mobile")]
[Authorize]
[RequirePermission(PermissionCatalog.MobileUse)]
public sealed class MobileController(DriverPushService push) : ControllerBase
{
    /// <summary>Registra el teléfono para los avisos emergentes (`{platform: ANDROID|IOS, pushToken, appVersion}`). Idempotente. Un chofer queda en su ficha; el personal, en su usuario.</summary>
    [HttpPost("device")]
    public async Task<IActionResult> RegisterDevice([FromBody] DriverDeviceRegisterRequest? req, CancellationToken ct)
    {
        await push.RegisterDeviceAsync(req, ct);
        return NoContent();
    }
}
