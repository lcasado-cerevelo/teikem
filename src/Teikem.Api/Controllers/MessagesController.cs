using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 53 — mensajería interna con hilos. Todo con messages.use (lo traen los roles del personal; el administrador se lo asigna o quita a
/// cada rol). Cada persona ve y toca SOLO sus hilos de la compañía activa; si no es parte, el hilo «no existe» (404). Rutas por PublicId;
/// los mensajes nunca se editan ni se borran.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize]
public sealed class MessagesController(MessagingService messages, MessageSuggestionService suggestions, ResponsibleService responsibles) : ControllerBase
{
    /// <summary>Lote 212 (B) — la foto de un mensaje (?thumb=true para la miniatura). 404 si no soy parte del hilo; 409 si ya se borró o archivó.</summary>
    [HttpGet("attachments/{publicId:guid}"), RequirePermission(PermissionCatalog.MessagesUse)]
    public async Task<IActionResult> Attachment(Guid publicId, [FromQuery] bool thumb, CancellationToken ct)
    {
        var (content, type) = await messages.GetAttachmentAsync(publicId, thumb, ct);
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(content, type);
    }

    /// <summary>Lote 212 — a quién proponerle primero para hablar de una orden (ORDER) o ruta (TRIP): responsables de la ruta y de la zona, despachadores y conversaciones recientes.</summary>
    [HttpGet("suggested"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<MessageSuggestionDto>> Suggested([FromQuery] string? entity, [FromQuery] Guid publicId, CancellationToken ct) => suggestions.SuggestAsync(entity, publicId, ct);

    /// <summary>Lote 212 — los mensajes de MIS hilos que hablan de esa orden o ruta.</summary>
    [HttpGet("about"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<MessageAboutDto>> About([FromQuery] string? entity, [FromQuery] Guid publicId, CancellationToken ct) => messages.AboutAsync(entity, publicId, ct);

    /// <summary>Lote 212 — responsables de una ruta (TRIP, key = publicId) o zona (DISPATCH_ZONE, key = id). Verlos: mensajería; cambiarlos: trips.plan / fleet.manage.</summary>
    [HttpGet("responsibles"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<ResponsibleDto>> Responsibles([FromQuery] string? entity, [FromQuery] string? key, CancellationToken ct) => responsibles.ListAsync(entity, key, ct);

    [HttpPut("responsibles"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<ResponsibleDto>> SetResponsibles([FromBody] ResponsiblesRequest req, CancellationToken ct) => responsibles.ReplaceAsync(req, ct);

    /// <summary>Las personas activas de la compañía (menos quien pregunta) con su presencia; q = parte del nombre o del correo.</summary>
    [HttpGet("people"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<MessagePersonDto>> People([FromQuery] string? q, [FromQuery] int take, CancellationToken ct) => messages.PeopleAsync(q, take, ct);

    /// <summary>Los roles de la compañía con cuántas personas activas tienen cada uno (para escribirle a todo un rol).</summary>
    [HttpGet("roles"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<MessageRoleDto>> Roles(CancellationToken ct) => messages.RolesAsync(ct);

    /// <summary>Mis hilos, el más reciente primero, con lo no leído de cada uno.</summary>
    [HttpGet("threads"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<IReadOnlyList<MessageThreadSummaryDto>> Threads(CancellationToken ct) => messages.ListThreadsAsync(ct);

    /// <summary>Nuevo hilo con su primer mensaje: a personas (userIds) y/o a un rol (roleId, «en foto»). Una persona sola = hilo entre dos (se reusa el existente).</summary>
    [HttpPost("threads"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageThreadDto> Create([FromBody] MessageThreadRequest req, CancellationToken ct) => messages.CreateThreadAsync(req, ct);

    /// <summary>Un hilo con sus mensajes (los últimos 50; before = Id del primero que ya tiene, para los anteriores). 404 si no es parte.</summary>
    [HttpGet("threads/{publicId:guid}"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageThreadDto> Get(Guid publicId, [FromQuery] long? before, [FromQuery] int take, CancellationToken ct) => messages.GetThreadAsync(publicId, before, take, ct);

    /// <summary>Contestar (o escribir) en un hilo. 400 con el mensaje exacto si el texto falta o pasa de 2000 caracteres.</summary>
    [HttpPost("threads/{publicId:guid}/messages"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageDto> Send(Guid publicId, [FromBody] MessageSendRequest req, CancellationToken ct) => messages.SendAsync(publicId, req, ct);

    /// <summary>Marca el hilo como leído hasta su último mensaje.</summary>
    [HttpPost("threads/{publicId:guid}/read"), RequirePermission(PermissionCatalog.MessagesUse)]
    public async Task<IActionResult> Read(Guid publicId, CancellationToken ct)
    {
        await messages.MarkReadAsync(publicId, ct);
        return NoContent();
    }

    /// <summary>Agrega personas a un grupo (solo quien lo creó; ven el historial desde que entran). 409 en un hilo entre dos.</summary>
    [HttpPost("threads/{publicId:guid}/members"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageThreadDto> AddMembers(Guid publicId, [FromBody] MessageMembersRequest req, CancellationToken ct) => messages.AddMembersAsync(publicId, req, ct);

    /// <summary>Salirse de un grupo (el hilo deja de existir para quien sale). 409 en un hilo entre dos.</summary>
    [HttpPost("threads/{publicId:guid}/leave"), RequirePermission(PermissionCatalog.MessagesUse)]
    public async Task<IActionResult> Leave(Guid publicId, CancellationToken ct)
    {
        await messages.LeaveAsync(publicId, ct);
        return NoContent();
    }

    /// <summary>Silenciar (muted = true, por omisión) o volver a recibir los avisos de un hilo; los mensajes siguen contando como no leídos.</summary>
    [HttpPut("threads/{publicId:guid}/mute"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageThreadSummaryDto> Mute(Guid publicId, [FromBody] MessageMuteRequest req, CancellationToken ct) => messages.MuteAsync(publicId, req, ct);

    /// <summary>Cuántos mensajes tengo sin leer, en cuántos hilos y el último con novedades (para el contador de la cabecera y el globo de Kemi).</summary>
    [HttpGet("unread"), RequirePermission(PermissionCatalog.MessagesUse)]
    public Task<MessageUnreadDto> Unread(CancellationToken ct) => messages.UnreadAsync(ct);
}

/// <summary>Lote 53 — presencia: la web abierta avisa cada ~30 s que sigue ahí («en línea» = visto hace menos de 90 s). Respaldo de la conexión en vivo. Cualquier persona con sesión.</summary>
[ApiController]
[Route("api/v1/presence")]
[Authorize]
public sealed class PresenceController(PresenceService presence, ITenantContext tenant) : ControllerBase
{
    [HttpPost("ping")]
    public async Task<PresencePingDto> Ping(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await presence.PingAsync(((TenantContext)tenant).RequireUserId(), now, ct);
        return new PresencePingDto(now);
    }
}

/// <summary>Lote 212 — quién participa en la mensajería de la compañía (FULL | OFFICE | NONE). Es administración de usuarios: exige <c>admin.users</c>, no <c>messages.use</c>.</summary>
[ApiController]
[Route("api/v1/messaging-modes")]
[Authorize]
public sealed class MessagingModesController(MessagingService messages) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<IReadOnlyList<MessagingModeDto>> List(CancellationToken ct) => messages.ListModesAsync(ct);

    [HttpPut("{userId:int}"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<MessagingModeDto> Set(int userId, [FromBody] MessagingModeRequest req, CancellationToken ct) => messages.SetModeAsync(userId, req, ct);
}

/// <summary>Lote 212 (B) — cuánto viven las fotos del chat y qué pasa al vencer. Es un ajuste de la compañía: exige <c>admin.tenant</c>.</summary>
[ApiController]
[Route("api/v1/messaging-settings")]
[Authorize]
public sealed class MessagingSettingsController(MessageRetentionService retention) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<MessagingSettingsDto> Get(CancellationToken ct) => retention.GetSettingsAsync(ct);

    [HttpPut, RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<MessagingSettingsDto> Put([FromBody] MessagingSettingsRequest req, CancellationToken ct) => retention.UpdateSettingsAsync(req, ct);
}
