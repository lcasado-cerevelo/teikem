using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 104 — el equipo de servicio al cliente atiende los mensajes que los clientes mandan desde su portal (módulo CLIENT_PORTAL, permiso clientmessages.reply). Cada persona del equipo ve las conversaciones de los clientes
/// donde está designada (o de los clientes sin equipo, si tiene el permiso; quien administra las ve todas). Una conversación que no es de un cliente suyo responde 404, igual que una que no existe.
/// </summary>
[ApiController]
[Route("api/v1/client-messages")]
[Authorize]
[RequireModule(ModuleKeys.ClientPortal)]
public sealed class ClientMessagesController(ClientMessagingService messages) : ControllerBase
{
    /// <summary>Las conversaciones que veo, la más reciente primero. Filtros: clientId, unread=true (solo con mensajes del cliente sin leer) y q (cliente, persona, correo o texto).</summary>
    [HttpGet("conversations"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<PagedResult<ClientConversationSummaryDto>> List([FromQuery] Guid? clientId, [FromQuery] bool unread, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => messages.ListAsync(clientId, unread, q, skip, take, ct);

    /// <summary>Cuántas conversaciones tienen mensajes del cliente sin leer (para el número del menú).</summary>
    [HttpGet("unread"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<StaffUnreadDto> Unread(CancellationToken ct) => messages.UnreadAsync(ct);

    [HttpGet("conversations/{publicId:guid}"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<ClientConversationDto> Get(Guid publicId, [FromQuery] long? before, [FromQuery] int take, CancellationToken ct) => messages.GetAsync(publicId, before, take, ct);

    /// <summary>Contestar (cualquiera del equipo); la conversación queda leída para el equipo. 400 si está vacío o pasa de 2000 caracteres.</summary>
    [HttpPost("conversations/{publicId:guid}/messages"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<ClientMessageDto> Reply(Guid publicId, [FromBody] ClientMessageSendRequest req, CancellationToken ct) => messages.ReplyAsync(publicId, req, ct);

    [HttpPost("conversations/{publicId:guid}/read"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public async Task<IActionResult> Read(Guid publicId, CancellationToken ct) { await messages.MarkReadAsync(publicId, ct); return NoContent(); }

    /// <summary>Escribirle primero a una persona del cliente (abre su conversación o agrega a la que ya hay). 404 si la persona no es un usuario activo de portal de ese cliente.</summary>
    [HttpPost("conversations"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<ClientConversationDto> Start([FromBody] ClientConversationStartRequest req, CancellationToken ct) => messages.StartAsync(req, ct);

    /// <summary>Las personas del cliente (usuarios de portal activos) a quienes se les puede escribir.</summary>
    [HttpGet("clients/{clientId:guid}/people"), RequirePermission(PermissionCatalog.ClientMessagesReply)]
    public Task<IReadOnlyList<ClientPersonDto>> People(Guid clientId, CancellationToken ct) => messages.PeopleAsync(clientId, ct);

    // ---------------------------------------------------------------- el equipo de cada cliente

    /// <summary>El equipo de servicio al cliente de ese cliente (sin equipo: lo atiende quien tenga el permiso).</summary>
    [HttpGet("clients/{clientId:guid}/team"), RequirePermission(PermissionCatalog.ClientMessagesReply + "|" + PermissionCatalog.ClientMessagesManage)]
    public Task<ClientServiceTeamDto> Team(Guid clientId, CancellationToken ct) => messages.GetTeamAsync(clientId, ct);

    /// <summary>Designar el equipo (reemplaza al actual; vacío = sin equipo). 400 si alguien no es un usuario interno activo de la compañía.</summary>
    [HttpPut("clients/{clientId:guid}/team"), RequirePermission(PermissionCatalog.ClientMessagesManage)]
    public Task<ClientServiceTeamDto> SetTeam(Guid clientId, [FromBody] ClientServiceTeamRequest req, CancellationToken ct) => messages.SetTeamAsync(clientId, req, ct);

    /// <summary>Las personas de la compañía que se pueden designar al equipo.</summary>
    [HttpGet("team-candidates"), RequirePermission(PermissionCatalog.ClientMessagesManage)]
    public Task<IReadOnlyList<TeamCandidateDto>> Candidates(CancellationToken ct) => messages.CandidatesAsync(ct);
}
