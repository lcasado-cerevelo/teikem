using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 — Usuarios de portal administrados desde el expediente del cliente (permiso portalusers.manage, distinto de
/// admin.users, R40). Todo vive bajo el módulo CLIENT_PORTAL salvo la aceptación de la invitación, que es anónima:
/// ahí el módulo se verifica en el servicio porque no hay tenant en el principal.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class PortalUsersController(PortalUserService portalUsers) : ControllerBase
{
    [HttpGet("clients/{publicId:guid}/portal-users"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public Task<IReadOnlyList<PortalUserDto>> List(Guid publicId, CancellationToken ct) => portalUsers.GetForClientAsync(publicId, ct);

    /// <summary>Invita por correo: crea la cuenta sin contraseña y emite el enlace (token solo en Development).</summary>
    [HttpPost("clients/{publicId:guid}/portal-users/invite"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public Task<PortalInviteResultDto> Invite(Guid publicId, [FromBody] PortalInviteRequest req, CancellationToken ct) => portalUsers.InviteAsync(publicId, req, ct);

    /// <summary>Nuevo enlace de invitación; solo mientras el usuario siga en INVITED (409 si no).</summary>
    [HttpPost("clients/{publicId:guid}/portal-users/{id:int}/resend-invite"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public Task<PortalInviteResultDto> ResendInvite(Guid publicId, int id, CancellationToken ct) => portalUsers.ResendInviteAsync(publicId, id, ct);

    /// <summary>ACTIVE → SUSPENDED (reversible): la cuenta se desactiva y sus sesiones se invalidan.</summary>
    [HttpPost("clients/{publicId:guid}/portal-users/{id:int}/suspend"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public async Task<IActionResult> Suspend(Guid publicId, int id, CancellationToken ct) { await portalUsers.SuspendAsync(publicId, id, ct); return NoContent(); }

    /// <summary>SUSPENDED → ACTIVE: la cuenta vuelve a habilitarse conservando la contraseña.</summary>
    [HttpPost("clients/{publicId:guid}/portal-users/{id:int}/reactivate"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public async Task<IActionResult> Reactivate(Guid publicId, int id, CancellationToken ct) { await portalUsers.ReactivateAsync(publicId, id, ct); return NoContent(); }

    /// <summary>Baja definitiva: → DISABLED (terminal), nunca DELETE.</summary>
    [HttpPost("clients/{publicId:guid}/portal-users/{id:int}/remove"), RequireModule(ModuleKeys.ClientPortal), RequirePermission(PermissionCatalog.PortalUsersManage)]
    public async Task<IActionResult> Remove(Guid publicId, int id, CancellationToken ct) { await portalUsers.RemoveAsync(publicId, id, ct); return NoContent(); }

    /// <summary>El invitado fija su contraseña. Misma respuesta 400 exista o no la invitación (sin enumeración).</summary>
    [HttpPost("portal-users/accept-invite"), AllowAnonymous]
    public async Task<IActionResult> AcceptInvite([FromBody] PortalAcceptInviteRequest req, CancellationToken ct) { await portalUsers.AcceptInviteAsync(req, ct); return NoContent(); }
}
