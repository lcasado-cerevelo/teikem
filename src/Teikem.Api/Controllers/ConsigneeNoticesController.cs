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

/// <summary>
/// Lote 117 — el cliente le da acceso a un consignatario suyo (invitación por correo). Sesión de portal de una persona del cliente con el módulo CLIENT_PORTAL; invitar, reenviar y quitar exigen rol de administrador u operador.
/// </summary>
[ApiController]
[Route("api/v1/portal/consignee-access")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalConsigneeAccessController(ConsigneeAccessService access) : ControllerBase
{
    /// <summary>Quién tiene acceso a ese consignatario.</summary>
    [HttpGet("{locationId:guid}")]
    public Task<IReadOnlyList<ConsigneeAccessDto>> List(Guid locationId, CancellationToken ct) => access.ListAsync(locationId, ct);

    /// <summary>Invita a un correo (sin correo, el principal del consignatario). 400 sin correo; 409 si ese correo no está disponible.</summary>
    [HttpPost("{locationId:guid}")]
    public Task<ConsigneeAccessInviteResultDto> Invite(Guid locationId, [FromBody] ConsigneeAccessInviteRequest req, CancellationToken ct) => access.InviteAsync(locationId, req, ct);

    /// <summary>Reenvía la invitación (el enlace anterior deja de servir).</summary>
    [HttpPost("{locationId:guid}/{id:int}/resend")]
    public Task<ConsigneeAccessInviteResultDto> Resend(Guid locationId, int id, CancellationToken ct) => access.ResendAsync(locationId, id, ct);

    /// <summary>Quita el acceso de esa persona.</summary>
    [HttpDelete("{locationId:guid}/{id:int}")]
    public async Task<IActionResult> Remove(Guid locationId, int id, CancellationToken ct) { await access.RemoveAsync(locationId, id, ct); return NoContent(); }
}

/// <summary>Lote 117 — el espacio del consignatario con acceso propio: su perfil, cómo recibe los avisos y la bandeja de lo que se le avisó. Solo una sesión de consignatario (claim loc) entra; solo ve lo suyo.</summary>
[ApiController]
[Route("api/v1/portal/consignee")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalConsigneeController(ConsigneeSelfService self) : ControllerBase
{
    [HttpGet("me")]
    public Task<ConsigneeMeDto> Me(CancellationToken ct) => self.GetMeAsync(ct);

    /// <summary>Apaga o vuelve a prender un medio de aviso (EMAIL o SMS). 409 si intenta prender lo que solo el cliente puede prender.</summary>
    [HttpPut("me/notices/{channel}")]
    public Task<ConsigneeMeDto> SetChannel(string channel, [FromBody] ConsigneeChannelRequest req, CancellationToken ct) => self.SetChannelAsync(channel, req, ct);

    /// <summary>La bandeja: los avisos que se le enviaron (más nuevos primero).</summary>
    [HttpGet("notices")]
    public Task<PagedResult<ConsigneeInboxItemDto>> Notices([FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => self.ListNoticesAsync(skip, take, ct);
}
