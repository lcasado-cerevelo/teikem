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

    /// <summary>
    /// Lote 213 — marca o desmarca varios consignatarios de una vez por el mismo medio (hasta 200), con una sola aceptación de la advertencia para todo el grupo. 400 sin consignatarios, con más de 200 o sin la advertencia
    /// (antes de tocar a nadie); 422 si el aviso está apagado. El que no se puede marcar queda con su mensaje en el resultado y los demás siguen.
    /// </summary>
    [HttpPut("consignees/bulk")]
    public Task<ConsigneeNoticeBulkResultDto> MarkMany([FromBody] ConsigneeNoticeBulkMarkRequest req, CancellationToken ct) => notices.SetConsigneesAsync(req, ct);

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
public sealed class PortalConsigneeController(ConsigneeSelfService self, ConsigneeMessagingService messages, ConsigneeOrdersService orders) : ControllerBase
{
    [HttpGet("me")]
    public Task<ConsigneeMeDto> Me(CancellationToken ct) => self.GetMeAsync(ct);

    /// <summary>Apaga o vuelve a prender un medio de aviso (EMAIL o SMS). 409 si intenta prender lo que solo el cliente puede prender.</summary>
    [HttpPut("me/notices/{channel}")]
    public Task<ConsigneeMeDto> SetChannel(string channel, [FromBody] ConsigneeChannelRequest req, CancellationToken ct) => self.SetChannelAsync(channel, req, ct);

    /// <summary>La bandeja: los avisos que se le enviaron (más nuevos primero).</summary>
    [HttpGet("notices")]
    public Task<PagedResult<ConsigneeInboxItemDto>> Notices([FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => self.ListNoticesAsync(skip, take, ct);

    /// <summary>Mis envíos: las órdenes de mi cliente que se entregan en mi consignatario (solo lectura; el consignatario NO crea órdenes). Búsqueda por conduce; paginado (máximo 100).</summary>
    [HttpGet("orders")]
    public Task<PagedResult<ConsigneeOrderListItemDto>> Orders([FromQuery] string? search, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => orders.ListAsync(search, skip, take, ct);

    /// <summary>Un envío mío con su avance y línea de tiempo; 404 si no es mío o no existe (igual respuesta).</summary>
    [HttpGet("orders/{publicId:guid}")]
    public Task<ConsigneeOrderDto> Order(Guid publicId, CancellationToken ct) => orders.GetAsync(publicId, ct);

    /// <summary>Mi conversación con mi cliente: los últimos mensajes (o los anteriores a <c>before</c>) en orden cronológico y cuántos del cliente no he leído.</summary>
    [HttpGet("messages")]
    public Task<PortalMessagesDto> Messages([FromQuery] long? before, [FromQuery] int take, CancellationToken ct) => messages.GetMineAsync(before, take, ct);

    /// <summary>Escribirle a mi cliente. Crea mi conversación con el primer mensaje. 400 si está vacío o pasa de 2000 caracteres; 429 si mando demasiados seguidos (30 por hora).</summary>
    [HttpPost("messages")]
    public Task<ClientMessageDto> SendMessage([FromBody] ClientMessageSendRequest req, CancellationToken ct) => messages.SendAsConsigneeAsync(req, ct);

    /// <summary>Marcar como leído lo que escribió el cliente.</summary>
    [HttpPost("messages/read")]
    public async Task<IActionResult> ReadMessages(CancellationToken ct) { await messages.MarkReadAsConsigneeAsync(ct); return NoContent(); }

    /// <summary>Cuántos mensajes del cliente no he leído (para el número de la pestaña).</summary>
    [HttpGet("messages/unread")]
    public Task<PortalUnreadDto> UnreadMessages(CancellationToken ct) => messages.UnreadMineAsync(ct);
}

/// <summary>
/// Lote 117b — el cliente lee y contesta los mensajes de sus consignatarios con acceso. Sesión de portal de una persona del cliente (no de un consignatario) con el módulo CLIENT_PORTAL; contestar exige rol de administrador u
/// operador (quien solo consulta lee). Todo se limita al cliente de la sesión: la conversación de otro cliente responde 404.
/// </summary>
[ApiController]
[Route("api/v1/portal/consignee-messages")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class PortalConsigneeMessagesController(ConsigneeMessagingService messages) : ControllerBase
{
    /// <summary>Las conversaciones de mis consignatarios (más recientes primero); <c>unreadOnly</c> trae las que tienen mensajes sin leer; <c>q</c> busca por consignatario, persona, correo o texto.</summary>
    [HttpGet]
    public Task<PagedResult<ConsigneeConversationSummaryDto>> List([FromQuery] bool unreadOnly, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => messages.ListAsync(unreadOnly, q, skip, take, ct);

    /// <summary>Cuántas conversaciones tienen mensajes del consignatario sin leer.</summary>
    [HttpGet("unread")]
    public Task<PortalUnreadDto> Unread(CancellationToken ct) => messages.UnreadAsync(ct);

    [HttpGet("{publicId:guid}")]
    public Task<ConsigneeConversationDto> Get(Guid publicId, [FromQuery] long? before, [FromQuery] int take, CancellationToken ct) => messages.GetAsync(publicId, before, take, ct);

    /// <summary>Contestar (administrador u operador; solo consulta = 403). La marca como leída para todos los de mi cliente.</summary>
    [HttpPost("{publicId:guid}")]
    public Task<ClientMessageDto> Reply(Guid publicId, [FromBody] ClientMessageSendRequest req, CancellationToken ct) => messages.ReplyAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/read")]
    public async Task<IActionResult> Read(Guid publicId, CancellationToken ct) { await messages.MarkReadAsClientAsync(publicId, ct); return NoContent(); }
}
