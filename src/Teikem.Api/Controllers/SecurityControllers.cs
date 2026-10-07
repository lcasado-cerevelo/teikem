using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Capa D: catálogo de permisos (solo lectura) y roles componibles del tenant.</summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class RolesController(PermissionService permissions) : ControllerBase
{
    [HttpGet("permissions")]
    public Task<IReadOnlyList<PermissionDto>> Permissions(CancellationToken ct) => permissions.GetCatalogAsync(ct);

    [HttpGet("roles")]
    public Task<IReadOnlyList<RoleDto>> Roles([FromQuery] bool includeTemplates, CancellationToken ct) => permissions.GetRolesAsync(includeTemplates, ct);

    [HttpPost("roles"), RequirePermission(PermissionCatalog.AdminRoles)]
    public Task<RoleDto> Create([FromBody] RoleUpsertRequest req, CancellationToken ct) => permissions.CreateRoleAsync(req, ct);

    /// <summary>Cambiar permisos de un rol afecta a todos los que lo tengan: acción sensible (AAL2).</summary>
    [HttpPut("roles/{id:int}"), RequirePermission(PermissionCatalog.AdminRoles), RequireAal2]
    public Task<RoleDto> Update(int id, [FromBody] RoleUpsertRequest req, CancellationToken ct) => permissions.UpdateRoleAsync(id, req, ct);

    [HttpDelete("roles/{id:int}"), RequirePermission(PermissionCatalog.AdminRoles), RequireAal2]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) { await permissions.DeleteRoleAsync(id, ct); return NoContent(); }
}

/// <summary>Pantalla "Roles y usuarios": usuarios del tenant.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(UserAdminService users, AuthService auth) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<IReadOnlyList<UserSummaryDto>> List(CancellationToken ct) => users.GetUsersAsync(ct);

    [HttpGet("{id:int}"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<UserSummaryDto> Get(int id, CancellationToken ct) => users.GetUserAsync(id, ct);

    /// <summary>Alta (por invitación): devuelve la contraseña temporal una sola vez si no se envió una.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.AdminUsers)]
    public async Task<UserCreateResponseDto> Create([FromBody] UserCreateRequest req, CancellationToken ct)
    {
        return await users.CreateUserCoreAsync(req, ct);
    }

    /// <summary>2026-10-01: otras compañías a las que quien crea el usuario puede agregarlo (administra usuarios allí).</summary>
    [HttpGet("assignable-companies"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<IReadOnlyList<AssignableCompanyDto>> AssignableCompanies(CancellationToken ct) => users.GetAssignableCompaniesAsync(ct);

    [HttpPut("{id:int}"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<UserSummaryDto> Update(int id, [FromBody] UserUpdateRequest req, CancellationToken ct) => users.UpdateUserAsync(id, req, ct);

    /// <summary>Asignar el permiso al rol (afecta a todos) vs. al usuario (excepción puntual): dos mecánicas.</summary>
    [HttpPut("{id:int}/roles"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public Task<UserSummaryDto> SetRoles(int id, [FromBody] UserRolesRequest req, CancellationToken ct) => users.SetRolesAsync(id, req, ct);

    [HttpPut("{id:int}/permissions"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public Task<UserSummaryDto> SetExtraPermissions(int id, [FromBody] UserExtraPermissionsRequest req, CancellationToken ct) => users.SetExtraPermissionsAsync(id, req, ct);

    [HttpPut("{id:int}/membership"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<UserSummaryDto> SetMembership(int id, [FromBody] MembershipStatusRequest req, CancellationToken ct) => users.SetMembershipStatusAsync(id, req, ct);

    [HttpGet("{id:int}/data-scopes"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<IReadOnlyList<DataScopeDto>> DataScopes(int id, CancellationToken ct) => users.GetDataScopesAsync(id, ct);

    [HttpPut("{id:int}/data-scopes"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<IReadOnlyList<DataScopeDto>> SetDataScopes(int id, [FromBody] DataScopesRequest req, CancellationToken ct) => users.SetDataScopesAsync(id, req, ct);

    [HttpDelete("{id:int}/sessions"), RequirePermission(PermissionCatalog.AdminUsers)]
    public async Task<IActionResult> RevokeSessions(int id, CancellationToken ct) { await auth.RevokeUserSessionsAsync(id, ct); return NoContent(); }

    /// <summary>Lote F8a: exige (o deja de exigir) MFA a este usuario en la compañía activa.</summary>
    [HttpPut("{id:int}/mfa"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public Task<UserSummaryDto> SetMfaRequired(int id, [FromBody] MfaRequiredRequest req, CancellationToken ct) => users.SetMfaRequiredAsync(id, req.Required, ct);

    /// <summary>
    /// Tarea 25: marca si este usuario ve lo esperado al contar, después de capturar cada línea (value true/false; null = sin marcar, vale el ajuste de la
    /// compañía). Solo cuenta si la compañía lo deja "según el usuario" o "todos"; con "nadie" ninguno lo ve.
    /// </summary>
    [HttpPut("{id:int}/count-see-expected"), RequirePermission(PermissionCatalog.AdminUsers)]
    public Task<UserSummaryDto> SetCountSeeExpected(int id, [FromBody] CountSeeExpectedRequest req, CancellationToken ct) => users.SetCountSeeExpectedAsync(id, req.Value, ct);

    /// <summary>
    /// 2026-10-07: contraseña temporal de 10 minutos (se la da el administrador al usuario). Si nadie entra con ella en ese tiempo vuelve la anterior; si entra, debe
    /// poner la suya. Cierra las sesiones del usuario. Sin contraseña en el cuerpo el servidor genera una (se devuelve una sola vez).
    /// 409 'No puede ponerse una contraseña temporal a sí mismo.'
    /// </summary>
    [HttpPost("{id:int}/temporary-password"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public Task<TemporaryPasswordDto> SetTemporaryPassword(int id, [FromBody] TemporaryPasswordRequest? req, CancellationToken ct) => auth.AdminSetTemporaryPasswordAsync(id, req, ct);

    /// <summary>Lote F8a: resetea el MFA de otro usuario (perdió su dispositivo) — vuelve a enrolar en su próximo login.</summary>
    [HttpDelete("{id:int}/mfa"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public async Task<IActionResult> ResetMfa(int id, CancellationToken ct) { await auth.AdminResetMfaAsync(id, ct); return NoContent(); }
}

/// <summary>Capa E: pantalla "Seguridad y auditoría" — bitácora de cambios, eventos de seguridad, actividad unificada y exportación CSV.</summary>
[ApiController]
[Route("api/v1/audit")]
[Authorize]
[RequirePermission(PermissionCatalog.AdminAudit)]
public sealed class AuditController(AuditQueryService audit, CompanySessionService sessions) : ControllerBase
{
    /// <summary>Id de la sesión de quien llama (claim `sid`): la marca "Esta sesión" y nunca se revoca desde la lista.</summary>
    private long SessionId => long.TryParse(User.FindFirst(TeikemClaims.SessionId)?.Value, out var s) ? s : 0;

    [HttpGet("changes")]
    public Task<PagedResult<AuditLogDto>> Changes([FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] int? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => audit.GetAuditLogAsync(entityType, entityId, userId, from, to, skip, take, ct);

    [HttpGet("security-events")]
    public Task<PagedResult<SecurityEventDto>> SecurityEvents([FromQuery] string? eventType, [FromQuery] int? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => audit.GetSecurityEventsAsync(eventType, userId, from, to, skip, take, ct);

    /// <summary>kind = all | changes | security. text = búsqueda libre.</summary>
    [HttpGet("activity")]
    public Task<PagedResult<ActivityRowDto>> Activity([FromQuery] string kind = "all", [FromQuery] string? text = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => audit.GetActivityAsync(kind, text, from, to, skip, take, ct);

    [HttpGet("activity/export.csv")]
    public async Task<IActionResult> Export([FromQuery] string kind = "all", [FromQuery] string? text = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, CancellationToken ct = default)
    {
        var csv = await audit.ExportActivityCsvAsync(kind, text, from, to, ct);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", $"actividad-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>Lote F10: sesiones activas de todos los usuarios de la compañía activa (admin.audit), la propia marcada.</summary>
    [HttpGet("sessions")]
    public Task<IReadOnlyList<CompanySessionDto>> Sessions(CancellationToken ct) => sessions.ListAsync(SessionId, ct);

    /// <summary>
    /// Lote F10: revoca una sesión de la compañía. Además de admin.audit exige admin.users (es la misma acción que
    /// DELETE /users/{id}/sessions, sobre una sola sesión). 409 'La sesión actual no se revoca desde la lista; use Salir.'; 404 si ya no está activa.
    /// </summary>
    [HttpDelete("sessions/{id:long}"), RequirePermission(PermissionCatalog.AdminUsers)]
    public async Task<IActionResult> RevokeSession(long id, CancellationToken ct) { await sessions.RevokeAsync(id, SessionId, ct); return NoContent(); }

    /// <summary>Lote F10: "Cerrar las demás sesiones" — todas las de la compañía salvo la propia (admin.users + AAL2: es masiva).</summary>
    [HttpPost("sessions/revoke-others"), RequirePermission(PermissionCatalog.AdminUsers), RequireAal2]
    public Task<RevokeSessionsResultDto> RevokeOtherSessions(CancellationToken ct) => sessions.RevokeOthersAsync(SessionId, ct);
}
