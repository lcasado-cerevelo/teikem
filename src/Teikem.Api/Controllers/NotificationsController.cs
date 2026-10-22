using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 51 — Sistema → Reglas de aviso: eventos, reglas (evento + canales con plantilla + destinatarios), envío de prueba y registro de
/// envíos. Módulo SYSTEM (siempre encendido); todo con notifications.manage. Rutas por PublicId; nunca DELETE (una regla se apaga).
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[RequireModule(ModuleKeys.System)]
public sealed class NotificationsController(NotificationRuleService rules) : ControllerBase
{
    /// <summary>El catálogo de eventos (con sus variables y textos por omisión).</summary>
    [HttpGet("events"), RequirePermission(PermissionCatalog.NotificationsManage + "|" + "notifications.view")]
    public IReadOnlyList<NotificationEventDto> Events() => rules.ListEvents();

    /// <summary>Los canales y si están configurados en Teikem (los no configurados no se pueden encender).</summary>
    [HttpGet("channels"), RequirePermission(PermissionCatalog.NotificationsManage + "|" + "notifications.view")]
    public IReadOnlyList<NotificationChannelStatusDto> Channels() => rules.ListChannels();

    [HttpGet("rules"), RequirePermission(PermissionCatalog.NotificationsManage + "|" + "notifications.view")]
    public Task<IReadOnlyList<NotificationRuleDto>> List(CancellationToken ct) => rules.ListRulesAsync(ct);

    /// <summary>Alta: evento y nombre obligatorios; los canales nacen apagados con los textos del evento. 400 con el mensaje exacto; 409 si el nombre ya existe.</summary>
    [HttpPost("rules"), RequirePermission(PermissionCatalog.NotificationsManage)]
    public Task<NotificationRuleDto> Create([FromBody] NotificationRuleRequest req, CancellationToken ct) => rules.CreateRuleAsync(req, ct);

    [HttpGet("rules/{publicId:guid}"), RequirePermission(PermissionCatalog.NotificationsManage + "|" + "notifications.view")]
    public Task<NotificationRuleDto> Get(Guid publicId, CancellationToken ct) => rules.GetRuleAsync(publicId, ct);

    /// <summary>Edición: null = sin cambio; Channels cambia solo los que vengan; Recipients reemplaza la lista; rowVersion opcional (409 si cambió).</summary>
    [HttpPatch("rules/{publicId:guid}"), RequirePermission(PermissionCatalog.NotificationsManage)]
    public Task<NotificationRuleDto> Update(Guid publicId, [FromBody] NotificationRuleRequest req, CancellationToken ct) => rules.UpdateRuleAsync(publicId, req, ct);

    /// <summary>«Enviar prueba»: manda el texto de un canal de la regla, con valores de ejemplo, a quien la pide.</summary>
    [HttpPost("rules/{publicId:guid}/test"), RequirePermission(PermissionCatalog.NotificationsManage)]
    public Task<NotificationTestResultDto> Test(Guid publicId, [FromBody] NotificationTestRequest req, CancellationToken ct) => rules.TestAsync(publicId, req, ct);

    /// <summary>Dispara el evento de prueba (TEST) por la cola real: lo reciben las reglas activas de ese evento.</summary>
    [HttpPost("test-event"), RequirePermission(PermissionCatalog.NotificationsManage)]
    public Task<NotificationQueuedDto> TestEvent([FromBody] NotificationTestEventRequest req, CancellationToken ct) => rules.PublishTestEventAsync(req, ct);

    /// <summary>Registro de envíos (los más recientes primero), con filtros por evento, canal, estado y fechas (UTC; «hasta» exclusivo).</summary>
    [HttpGet("log"), RequirePermission(PermissionCatalog.NotificationsManage + "|" + "notifications.view")]
    public Task<PagedResult<NotificationLogDto>> Log([FromQuery] string? eventCode, [FromQuery] string? channel, [FromQuery] string? status,
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => rules.ListLogAsync(eventCode, channel, status, fromUtc, toUtc, skip, take, ct);
}

/// <summary>
/// Lote 51 — la bandeja de avisos de la persona que inició sesión (la campanita): lista, contador de no leídos, marcar leído y silenciar
/// eventos opcionales. Sin permiso especial: cada usuario ve y toca solo los suyos.
/// </summary>
[ApiController]
[Route("api/v1/inbox")]
[Authorize]
public sealed class InboxController(NotificationInboxService inbox) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserNotificationDto>> List([FromQuery] bool unreadOnly, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => inbox.ListAsync(unreadOnly, skip, take, ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken ct) => inbox.UnreadCountAsync(ct);

    [HttpPost("{id:long}/read")]
    public Task<UserNotificationDto> MarkRead(long id, CancellationToken ct) => inbox.MarkReadAsync(id, ct);

    [HttpPost("read-all")]
    public Task<MarkedReadDto> MarkAllRead(CancellationToken ct) => inbox.MarkAllReadAsync(ct);

    /// <summary>Los eventos opcionales que se pueden silenciar y cuáles tiene silenciados.</summary>
    [HttpGet("prefs")]
    public Task<IReadOnlyList<NotificationPrefDto>> Prefs(CancellationToken ct) => inbox.PrefsAsync(ct);

    /// <summary>Silenciar (muted = true, por omisión) o volver a recibir un evento opcional. 409 si el evento no se puede silenciar.</summary>
    [HttpPut("prefs/{eventCode}")]
    public Task<NotificationPrefDto> SetPref(string eventCode, [FromBody] NotificationPrefRequest req, CancellationToken ct) => inbox.SetMutedAsync(eventCode, req, ct);
}
