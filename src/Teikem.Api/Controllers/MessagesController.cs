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
public sealed class MessagesController(MessagingService messages) : ControllerBase
{
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
