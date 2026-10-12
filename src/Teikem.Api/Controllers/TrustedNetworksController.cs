using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md): la lista por compañía de IPs o rangos desde los que NO se pide el código
/// MFA al iniciar sesión (la contraseña sí; las acciones sensibles siguen pidiendo MFA). Solo la administración de la compañía (admin.tenant); alta, cambio,
/// baja y máximo exigen además MFA reciente ([RequireAal2]), así que una sesión abierta desde la red preferida no puede ampliar la lista por sí sola.
/// </summary>
[ApiController]
[Route("api/v1/tenant/trusted-networks")]
[Authorize]
public sealed class TrustedNetworksController(TrustedNetworkService networks) : ControllerBase
{
    /// <summary>
    /// La lista, el máximo de entradas (null = sin tope), si el interruptor global del servidor está encendido (apagado, la lista no salta el MFA), la IP
    /// desde la que se consulta (para «Agregar esta IP») y la entrada vigente que la cubre, si alguna.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<TrustedNetworksDto> Get(CancellationToken ct) => networks.GetAsync(ct);

    /// <summary>
    /// Agrega una IP o rango. name y cidr obligatorios; confirmInternalOnly debe ser true («esta IP es solo de la red interna»). 400 con el campo (cidr
    /// inválido o demasiado ancho, confirmInternalOnly, expiresAtUtc pasado); 409 si ya está en la lista o la compañía llegó a su máximo.
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.AdminTenant), RequireAal2]
    public Task<TrustedNetworkDto> Create([FromBody] TrustedNetworkRequest req, CancellationToken ct) => networks.CreateAsync(req, ct);

    /// <summary>Cambia una entrada: lo que viene null no cambia; cambiar la IP o el rango exige confirmInternalOnly otra vez. clearExpiry quita el vencimiento.</summary>
    [HttpPut("{publicId:guid}"), RequirePermission(PermissionCatalog.AdminTenant), RequireAal2]
    public Task<TrustedNetworkDto> Update(Guid publicId, [FromBody] TrustedNetworkRequest req, CancellationToken ct) => networks.UpdateAsync(publicId, req, ct);

    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.AdminTenant), RequireAal2]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await networks.DeleteAsync(publicId, ct);
        return NoContent();
    }

    /// <summary>Máximo de entradas de la compañía (null = sin tope). Las que ya hay se conservan aunque queden por encima; solo se bloquean las altas nuevas.</summary>
    [HttpPut("limit"), RequirePermission(PermissionCatalog.AdminTenant), RequireAal2]
    public Task<TrustedNetworksDto> SetLimit([FromBody] TrustedNetworkLimitRequest req, CancellationToken ct) => networks.SetMaxEntriesAsync(req.MaxEntries, ct);
}
