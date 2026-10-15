using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 54 — Sistema → Enviar avisos: avisos manuales a personas, a roles o a todos (permiso notifications.send; módulo SYSTEM). El aviso llega a la
/// campanita de cada persona («De Ana») y no se responde. El historial muestra a quién se envió y cuántas personas lo leyeron. Nunca DELETE ni PATCH.
/// </summary>
[ApiController]
[Route("api/v1/announcements")]
[Authorize]
[RequireModule(ModuleKeys.System)]
public sealed class AnnouncementsController(AnnouncementService announcements) : ControllerBase
{
    /// <summary>Personas a las que se puede avisar (activas de la compañía, sin quien pregunta), con buscador q y el total; hasta 500 por página.</summary>
    [HttpGet("people"), RequirePermission(PermissionCatalog.NotificationsSend)]
    public Task<PagedResult<AnnouncementPersonDto>> People([FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => announcements.PeopleAsync(q, skip, take, ct);

    /// <summary>Los roles de la compañía con cuántas personas activas tiene cada uno.</summary>
    [HttpGet("roles"), RequirePermission(PermissionCatalog.NotificationsSend)]
    public Task<IReadOnlyList<MessageRoleDto>> Roles(CancellationToken ct) => announcements.RolesAsync(ct);

    /// <summary>Envía el aviso a las personas, los roles o a todos (All). 400 con el mensaje exacto si falta el título, el destinatario o pasa de los límites.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.NotificationsSend)]
    public Task<AnnouncementDto> Send([FromBody] AnnouncementRequest req, CancellationToken ct) => announcements.SendAsync(req, ct);

    /// <summary>Avisos enviados (el más reciente primero), con a quién y cuántas personas lo leyeron.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.NotificationsSend)]
    public Task<PagedResult<AnnouncementDto>> List([FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => announcements.ListAsync(skip, take, ct);
}
