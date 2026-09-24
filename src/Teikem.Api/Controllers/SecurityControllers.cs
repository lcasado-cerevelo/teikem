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
    public async Task<object> Create([FromBody] UserCreateRequest req, CancellationToken ct)
    {
        var (user, temp) = await users.CreateUserAsync(req, ct);
        return new { user, temporaryPassword = temp };
    }

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
}

/// <summary>Capa E: pantalla "Seguridad y auditoría" — bitácora de cambios, eventos de seguridad, actividad unificada y exportación CSV.</summary>
[ApiController]
[Route("api/v1/audit")]
[Authorize]
[RequirePermission(PermissionCatalog.AdminAudit)]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
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
}
