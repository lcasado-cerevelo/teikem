using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 116 — avisos al consignatario desde el portal del cliente: prender el aviso y escoger su idioma, y marcar por consignatario si acepta correo y/o texto (con la advertencia aceptada). Sesión de portal con el módulo CLIENT_PORTAL;
/// cambiar exige rol de administrador u operador. Todo se limita al cliente de la sesión.
/// </summary>
[ApiController]
[Route("api/v1/portal/consignee-notices")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalConsigneeNoticesController(ConsigneeNoticeService notices) : ControllerBase
{
    [HttpGet]
    public Task<ConsigneeNoticeSettingsDto> Get(CancellationToken ct) => notices.GetSettingsAsync(ct);

    [HttpPut]
    public Task<ConsigneeNoticeSettingsDto> Put([FromBody] ConsigneeNoticeSettingsRequest req, CancellationToken ct) => notices.SetSettingsAsync(req, ct);

    [HttpGet("consignees")]
    public Task<PagedResult<ConsigneeNoticeConsigneeDto>> Consignees([FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => notices.ListConsigneesAsync(search, skip, take, ct);

    /// <summary>Marca (accepted = true, con warningAccepted) o desmarca que el consignatario acepta avisos por correo o texto. 400 sin la advertencia o sin el medio; 409 si el consignatario se dio de baja.</summary>
    [HttpPut("consignees/{publicId:guid}")]
    public Task<ConsigneeNoticeConsigneeDto> Mark(Guid publicId, [FromBody] ConsigneeNoticeMarkRequest req, CancellationToken ct) => notices.SetConsigneeAsync(publicId, req, ct);
}

/// <summary>Lote 116 — lo que usa el consignatario sin sesión: el seguimiento por enlace con token y la baja de los avisos.</summary>
[ApiController]
[Route("api/v1/public")]
public sealed class PublicConsigneeController(PublicTrackingService tracking, ConsigneeNoticeService notices) : ControllerBase
{
    /// <summary>Avance de una orden por su enlace (token propio). Mismo 404 genérico si el enlace no existe.</summary>
    [HttpGet("track/link/{token}"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Track)]
    public Task<PublicTrackDto> Track(string token, [FromQuery] string? lang, CancellationToken ct) => tracking.TrackByTokenAsync(token, lang, ct);

    /// <summary>Baja de los avisos con el enlace de un aviso: ese medio no recibe nada más. Idempotente.</summary>
    [HttpPost("consignee-unsubscribe/{token}"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Track)]
    public async Task<IActionResult> Unsubscribe(string token, CancellationToken ct)
    {
        await notices.UnsubscribeAsync(token, ct);
        return NoContent();
    }
}
