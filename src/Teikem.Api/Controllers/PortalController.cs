using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 101 — portal del cliente. Login SEPARADO del del staff y lecturas limitadas al cliente de la sesión (claim <c>cid</c>) además de la compañía. Solo cuentas de tipo PORTAL con acceso ACTIVO a un cliente;
/// el módulo CLIENT_PORTAL debe estar encendido. Una sesión del staff no entra aquí y una de portal no entra a las rutas del staff (<c>PortalScopeMiddleware</c>).
/// </summary>
[ApiController]
[Route("api/v1/portal")]
public sealed class PortalController(AuthService auth, PortalService portal, ClientMessagingService messages, PortalOrderEntryService entry) : ControllerBase
{
    /// <summary>
    /// Login del portal: correo y contraseña (+ cliente opcional). Puede responder: <c>ok</c> (tokens), <c>mfa_required</c> (segundo paso; con challenge), <c>browser_confirmation_required</c> (computadora nueva: se manda un correo) o
    /// <c>captcha_required</c> (reto anti-robot tras varios fallos). 401 'Credenciales inválidas.' igual para cuenta inexistente, del staff o bloqueada; 403 si no tiene acceso activo a ningún cliente. 60 por minuto por IP.
    /// </summary>
    [HttpPost("auth/login"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Login)]
    public Task<AuthResultDto> Login([FromBody] PortalLoginRequest req, CancellationToken ct) => auth.PortalLoginAsync(req, ct);

    /// <summary>Si el login pide reto anti-robot (Cloudflare Turnstile) y con qué llave del sitio.</summary>
    [HttpGet("auth/config"), AllowAnonymous]
    public PortalAuthConfigDto Config() => auth.PortalAuthConfig();

    /// <summary>Cambia el cliente de la sesión (cuentas con acceso a varios): 403 'No tiene acceso a ese cliente.' si no es suyo.</summary>
    [HttpPost("auth/switch-client"), AllowAnonymous]
    public Task<TokenPairDto> SwitchClient([FromBody] PortalSwitchClientRequest req, CancellationToken ct) => auth.PortalSwitchClientAsync(req, ct);

    /// <summary>Quién soy en el portal: la cuenta, el cliente actual y los demás clientes a los que tengo acceso.</summary>
    [HttpGet("me"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalMeDto> Me(CancellationToken ct) => portal.GetMeAsync(ct);

    /// <summary>Inicio del portal: total de mis órdenes, cuántas hay por estatus y las 5 más recientes.</summary>
    [HttpGet("summary"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalSummaryDto> Summary(CancellationToken ct) => portal.GetSummaryAsync(ct);

    /// <summary>Bandeja de avisos: los resúmenes que la compañía configuró mandar a mi cliente por el canal «bandeja del portal» (los más nuevos primero; máximo 100 por página).</summary>
    [HttpGet("notifications"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PagedResult<PortalNotificationDto>> Notifications([FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => portal.ListNotificationsAsync(skip, take, ct);

    /// <summary>Mi conversación con el equipo de servicio al cliente (la mía, no la de nadie más de mi empresa): los últimos mensajes (o los anteriores a <c>before</c>) en orden cronológico y cuántos del equipo no he leído.</summary>
    [HttpGet("messages"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalMessagesDto> Messages([FromQuery] long? before, [FromQuery] int take, CancellationToken ct) => messages.GetMineAsync(before, take, ct);

    /// <summary>Escribirle al equipo. Crea mi conversación con el primer mensaje. 400 si está vacío o pasa de 2000 caracteres; 429 si mando demasiados seguidos (30 por hora).</summary>
    [HttpPost("messages"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<ClientMessageDto> SendMessage([FromBody] ClientMessageSendRequest req, CancellationToken ct) => messages.SendAsPortalAsync(req, ct);

    /// <summary>Marcar como leído lo que escribió el equipo.</summary>
    [HttpPost("messages/read"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public async Task<IActionResult> ReadMessages(CancellationToken ct) { await messages.MarkReadAsPortalAsync(ct); return NoContent(); }

    /// <summary>Cuántos mensajes del equipo no he leído (para el número de la pestaña).</summary>
    [HttpGet("messages/unread"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalUnreadDto> UnreadMessages(CancellationToken ct) => messages.UnreadMineAsync(ct);

    /// <summary>Mis órdenes (solo las del cliente de la sesión): búsqueda por conduce, orden, factura o consignatario; estatus y fechas; paginado (máximo 100).</summary>
    [HttpGet("orders"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PagedResult<PortalOrderListItemDto>> Orders([FromQuery] string? search, [FromQuery] string? status, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
        [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => portal.ListOrdersAsync(search, status, fromUtc, toUtc, skip, take, ct);

    /// <summary>Formulario de «Nueva orden»: si mi rol puede crear y mis consignatarios y puntos de recogida (solo los de mi cliente).</summary>
    [HttpGet("order-form"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalOrderFormDto> OrderForm(CancellationToken ct) => entry.GetFormAsync(ct);

    /// <summary>Crear una orden (solo lo básico; entra al flujo normal). Roles CLIENT_ADMIN y CLIENT_OPERATOR; el cliente sale de la sesión.</summary>
    [HttpPost("orders"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalOrderCreatedDto> CreateOrder([FromBody] PortalOrderCreateRequest req, CancellationToken ct) => entry.CreateAsync(req, ct);

    /// <summary>Una orden mía con su línea de tiempo; 404 si es de otro cliente o no existe (igual respuesta).</summary>
    [HttpGet("orders/{publicId:guid}"), Authorize, RequireModule(ModuleKeys.ClientPortal)]
    public Task<PortalOrderDto> Order(Guid publicId, CancellationToken ct) => portal.GetOrderAsync(publicId, ct);
}
