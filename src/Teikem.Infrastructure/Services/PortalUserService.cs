using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 2 — Usuarios de portal desde el expediente del cliente: invitar, reenviar, aceptar (anónimo), suspender,
/// reactivar y dar de baja. Reglas:
/// - Nadie escribe la contraseña de otro (R41): la cuenta se crea sin contraseña y el invitado la fija al aceptar.
/// - La invitación reutiliza el token de restablecimiento de Identity (DataProtector, vida Portal:InviteHours, un solo
///   uso porque ResetPassword rota el SecurityStamp); reenviar rota el stamp e invalida el enlace anterior; no hay tabla nueva.
/// - Un correo no puede ser a la vez usuario interno y de portal (RequireUniqueEmail, bidireccional: UserAdminService rechaza
///   el sentido contrario). Todo choque de correo responde el MISMO 409 neutro (propio tenant, otro tenant o usuario interno)
///   para no revelar dónde está registrado; queda SecurityEvent ROLE_CHANGE/FAILURE 'portal_invite_conflict' para detectar
///   enumeración masiva. Residual aceptado: 409 vs 200 revela existencia global del correo.
/// - Un usuario dado de baja (DISABLED) se puede volver a invitar con el mismo correo desde el mismo cliente: se reutiliza la
///   fila y la cuenta (nacimiento null → INVITED con historial); desde otro cliente sigue siendo 409.
/// - Los usuarios de portal no reciben UserTenant (no aparecen en /api/v1/users, R39): su alcance es PortalUser.ClientId.
/// - Todo cambio de estatus pasa por StatusService (INVITED → ACTIVE; SUSPENDED lateral reversible; DISABLED terminal);
///   PortalUserStatusEffect desactiva/reactiva la cuenta e invalida tokens.
/// - Toda fila se alcanza a través del cliente del tenant (ResolveClientAsync + ClientId); IgnoreQueryFilters solo en
///   accept-invite, porque no hay tenant en el principal.
/// - Las acciones del admin de plataforma quedan atribuidas a él en SecurityEvent y AuditLog (R42).
/// </summary>
public sealed class PortalUserService(
    TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, ILookupCache lookups, StatusService statuses,
    ModuleService modules, ISecurityEventWriter security, IInvitationSender inviter, IPasswordBreachChecker breachChecker, IConfiguration config)
{
    private const string InvalidInvite = "Invitación inválida o vencida.";
    /// <summary>Único mensaje para todo choque de correo (propio tenant, otro tenant o usuario interno): no revela la causa.</summary>
    public const string DuplicateEmail = "Ese correo no está disponible para el portal de esta compañía.";

    // ---------------- Consulta ----------------

    public async Task<IReadOnlyList<PortalUserDto>> GetForClientAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        var rows = await db.PortalUsers.AsNoTracking().Include(p => p.Role).Include(p => p.Status)
            .Where(p => p.ClientId == client.ClientId)
            .OrderBy(p => p.Email).ToListAsync(ct);
        return rows.Select(p => ToDto(p, client.PublicId)).ToList();
    }

    // ---------------- Invitar / reenviar ----------------

    public async Task<PortalInviteResultDto> InviteAsync(Guid clientPublicId, PortalInviteRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var email = NormalizeEmail(req.Email);
        var fullName = OptionalText(req.FullName, "fullName", 150);
        if (string.IsNullOrWhiteSpace(req.Role)) throw new ValidationException("role", "El rol de portal es obligatorio.");
        var roleId = await lookups.GetIdAsync(LookupDomains.PortalRole, req.Role.Trim(), ct); // 404 si el código no existe
        var portalKindId = await lookups.GetIdAsync(LookupDomains.UserKind, UserKinds.Portal, ct);

        (int PortalUserId, int ClientId, string ClientName, ApplicationUser User, bool Reinvited) result;
        try
        {
            result = await db.RunInTransactionAsync(async ct2 =>
            {
                var client = await db.ResolveClientAsync(clientPublicId, ct2);

                // Fila existente en el tenant (filtro global): si está dada de baja y es del mismo cliente, renace; si no, 409.
                var existing = await db.PortalUsers.Include(p => p.Status).ThenInclude(st => st!.StageKind)
                    .FirstOrDefaultAsync(p => p.Email == email, ct2);
                if (existing is not null)
                {
                    var terminal = existing.Status?.StageKind?.InternalCode == StageKinds.Terminal;
                    if (!terminal || existing.ClientId != client.ClientId) throw new ConflictException(DuplicateEmail);
                    return await ReinviteAsync(existing, client, fullName, roleId, ct2);
                }

                if (await users.FindByEmailAsync(email) is not null) throw new ConflictException(DuplicateEmail);

                // Cuenta SIN contraseña (R41) y sin UserTenant: el alcance del portal es el cliente.
                var account = new ApplicationUser
                {
                    UserName = email, Email = email, FullName = fullName, UserKindLookupId = portalKindId,
                    DefaultTenantId = tenantId, IsActive = true, EmailConfirmed = false,
                };
                var created = await users.CreateAsync(account);
                if (!created.Succeeded) ThrowIdentityErrors(created, "email");

                var initial = await statuses.GetInitialAsync(StatusDomains.PortalUserStatus, ct2);
                var pu = new PortalUser
                {
                    TenantId = tenantId, ClientId = client.ClientId, UserId = account.Id, Email = email, FullName = fullName,
                    RoleLookupId = roleId, StatusCodeId = initial.StatusCodeId, IsActive = true,
                };
                db.PortalUsers.Add(pu);
                await db.SaveGuardedAsync(DuplicateEmail, ct2); // UQ_PortalUser (TenantId, Email) es la segunda barrera

                // Historial de estatus desde el nacimiento (INVITED es la etapa inicial tras el reorden del seed).
                var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, null, PortalUserStatuses.Invited, null, ct2);
                pu.StatusCodeId = to.StatusCodeId;
                await db.SaveChangesAsync(ct2);
                return (pu.PortalUserId, client.ClientId, client.Name, account, false);
            }, ct);
        }
        catch (ConflictException ex) when (ex.Message == DuplicateEmail)
        {
            // Sin la causa: solo deja rastro de que alguien pidió un correo no disponible (detección de enumeración).
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Failure, tenant.UserId, tenantId,
                new { action = "portal_invite_conflict", client = clientPublicId, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
            throw;
        }

        var (portalUserId, clientId, clientName, user, reinvited) = result;
        var (token, expiresAt) = await IssueInviteTokenAsync(user, clientName, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { action = reinvited ? "portal_reinvite" : "portal_invite", portalUser = portalUserId, client = clientId, email, role = req.Role.Trim().ToUpperInvariant(), byPlatformAdmin = tenant.IsPlatformAdmin }, ct);

        var dto = await LoadDtoAsync(clientPublicId, portalUserId, ct);
        return new PortalInviteResultDto(dto, ReturnTokenInResponse ? token : null, expiresAt);
    }

    /// <summary>
    /// Renacimiento de una fila DISABLED del mismo cliente: se reutilizan la fila y la cuenta (IsActive=1, correo sin confirmar,
    /// rol y nombre nuevos) y se registra un nacimiento null → INVITED en el historial (StatusService solo exige etapa inicial).
    /// </summary>
    private async Task<(int, int, string, ApplicationUser, bool)> ReinviteAsync(PortalUser existing, Client client, string? fullName, int roleId, CancellationToken ct)
    {
        var account = await AccountOfAsync(existing);
        account.IsActive = true;
        account.EmailConfirmed = false;
        if (fullName is not null) account.FullName = fullName;
        var updated = await users.UpdateAsync(account);
        if (!updated.Succeeded) ThrowIdentityErrors(updated, "email");

        existing.FullName = fullName ?? existing.FullName;
        existing.RoleLookupId = roleId;
        existing.IsActive = true;
        var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, existing.PortalUserId, null, PortalUserStatuses.Invited, "Reinvitación tras baja", ct);
        existing.StatusCodeId = to.StatusCodeId;
        await db.SaveChangesAsync(ct);
        return (existing.PortalUserId, client.ClientId, client.Name, account, true);
    }

    public async Task<PortalInviteResultDto> ResendInviteAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        var pu = await LoadForClientAsync(client, id, ct);
        if (!Is(pu, PortalUserStatuses.Invited))
            throw new ConflictException("Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED).");
        var user = await AccountOfAsync(pu);
        // Rotar el SecurityStamp mata el enlace anterior: el token DataProtector lo lleva embebido y ResetPassword lo compara.
        // En INVITED no hay contraseña ni sesiones (el login del portal está bloqueado), así que no hay otro efecto.
        await users.UpdateSecurityStampAsync(user);

        var (token, expiresAt) = await IssueInviteTokenAsync(user, client.Name, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, pu.TenantId,
            new { action = "portal_invite_resent", portalUser = pu.PortalUserId, client = client.ClientId, email = pu.Email, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
        return new PortalInviteResultDto(ToDto(pu, client.PublicId), ReturnTokenInResponse ? token : null, expiresAt);
    }

    // ---------------- Suspender / reactivar / dar de baja ----------------

    /// <summary>ACTIVE → SUSPENDED (lateral, reversible). El efecto desactiva la cuenta e invalida tokens.</summary>
    public async Task SuspendAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var (tenantId, portalUserId) = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            var pu = await LoadForClientAsync(client, id, ct2);
            if (!Is(pu, PortalUserStatuses.Active)) throw new ConflictException("Solo se puede suspender un usuario de portal en estatus ACTIVE.");
            await MoveAsync(pu, PortalUserStatuses.Suspended, ct2);
            return (pu.TenantId, pu.PortalUserId);
        }, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { action = "portal_user_suspended", portalUser = portalUserId, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
    }

    /// <summary>SUSPENDED → ACTIVE (salida del lateral). La contraseña se conserva; el efecto vuelve a habilitar la cuenta.</summary>
    public async Task ReactivateAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var (tenantId, portalUserId) = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            var pu = await LoadForClientAsync(client, id, ct2);
            if (!Is(pu, PortalUserStatuses.Suspended)) throw new ConflictException("Solo se puede reactivar un usuario de portal en estatus SUSPENDED.");
            await MoveAsync(pu, PortalUserStatuses.Active, ct2);
            return (pu.TenantId, pu.PortalUserId);
        }, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { action = "portal_user_reactivated", portalUser = portalUserId, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
    }

    /// <summary>Cualquier estatus no terminal → DISABLED (terminal = baja definitiva, nunca DELETE).</summary>
    public async Task RemoveAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var (tenantId, portalUserId) = await db.RunInTransactionAsync(async ct2 =>
        {
            var client = await db.ResolveClientAsync(clientPublicId, ct2);
            var pu = await LoadForClientAsync(client, id, ct2);
            if (pu.Status?.StageKind?.InternalCode == StageKinds.Terminal) throw new ConflictException("El usuario de portal ya está dado de baja.");
            await MoveAsync(pu, PortalUserStatuses.Disabled, ct2);
            return (pu.TenantId, pu.PortalUserId);
        }, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { action = "portal_user_disabled", portalUser = portalUserId, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
    }

    // ---------------- Aceptar invitación (anónimo) ----------------

    /// <summary>
    /// El invitado fija su contraseña con el token recibido. Cualquier fallo (no existe, estatus ≠ INVITED, token inválido
    /// o vencido, contraseña débil o en brecha, módulo CLIENT_PORTAL apagado) responde el MISMO 400 sin enumerar,
    /// y queda como SecurityEvent PASSWORD_CHANGE/FAILURE sin userId (con el tenant del PortalUser si se resolvió).
    /// </summary>
    public async Task AcceptInviteAsync(PortalAcceptInviteRequest req, CancellationToken ct)
    {
        int? tenantIdForEvent = null;
        async Task FailAsync(string reason)
        {
            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Failure, null, tenantIdForEvent,
                new { action = "portal_invite_accept_failed", reason }, ct);
            throw new ValidationException(InvalidInvite);
        }

        string email;
        try { email = NormalizeEmail(req.Email); }
        catch (ValidationException) { await FailAsync("invalid_email"); return; }

        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsActive) { await FailAsync("no_account"); return; }
        var kind = user.UserKindLookupId is null ? null : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        if (kind != UserKinds.Portal) { await FailAsync("not_portal"); return; }

        // Único uso permitido de IgnoreQueryFilters fuera de la revocación: no hay tenant en el principal.
        var pu = await db.PortalUsers.IgnoreQueryFilters().Include(p => p.Status)
            .FirstOrDefaultAsync(p => p.UserId == user.Id && p.IsActive, ct);
        if (pu is null) { await FailAsync("no_portal_user"); return; }
        tenantIdForEvent = pu.TenantId;
        if (!Is(pu, PortalUserStatuses.Invited)) { await FailAsync("not_invited"); return; }
        // Una compañía desactivada no acepta invitaciones emitidas antes (misma respuesta neutra que el login la rechaza).
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == pu.TenantId && t.IsActive, ct)) { await FailAsync("tenant_inactive"); return; }

        var enabled = await modules.GetEnabledKeysAsync(pu.TenantId, ct);
        if (!enabled.Contains(ModuleKeys.ClientPortal)) { await FailAsync("module_disabled"); return; }

        var password = req.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(req.Token) || password.Length == 0) { await FailAsync("missing_token_or_password"); return; }
        if (await breachChecker.IsBreachedAsync(password, ct)) { await FailAsync("password_breached"); return; }

        // Desde aquí la acción queda atribuida al propio invitado dentro de su tenant (AuditLog y SecurityEvent).
        using (((TenantContext)tenant).As(pu.TenantId, user.Id))
        {
            // Valida token (vida Portal:InviteHours, un solo uso: rota el SecurityStamp) y contraseña (NIST: ≥ 12, sin composición).
            var reset = await users.ResetPasswordAsync(user, req.Token.Trim(), password);
            if (!reset.Succeeded) { await FailAsync("token_or_password_rejected:" + string.Join(",", reset.Errors.Select(e => e.Code))); return; }

            user.EmailConfirmed = true;
            await users.UpdateAsync(user);

            var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, pu.StatusCodeId, PortalUserStatuses.Active, null, ct);
            pu.StatusCodeId = to.StatusCodeId;
            await db.SaveChangesAsync(ct);

            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, pu.TenantId,
                new { action = "portal_invite_accepted", portalUser = pu.PortalUserId, client = pu.ClientId }, ct);
        }
    }

    // ---------------- Helpers ----------------

    private bool ReturnTokenInResponse => config.GetValue("Portal:ReturnInviteTokenInResponse", false);

    private async Task<(string Token, DateTime ExpiresAtUtc)> IssueInviteTokenAsync(ApplicationUser user, string clientName, CancellationToken ct)
    {
        // Mismo suelo que TokenLifespan en DependencyInjection (Math.Max(1, ...)) para que el DTO y la vida real del token no diverjan.
        var hours = Math.Max(1, config.GetValue("Portal:InviteHours", 48));
        var expiresAt = DateTime.UtcNow.AddHours(hours);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await inviter.SendInviteAsync(user.Email!, token, clientName, expiresAt, ct);
        return (token, expiresAt);
    }

    /// <summary>La fila se carga SIEMPRE a través del cliente del tenant, nunca por id suelto.</summary>
    private async Task<PortalUser> LoadForClientAsync(Client client, int id, CancellationToken ct)
        => await db.PortalUsers.Include(p => p.Role).Include(p => p.Status).ThenInclude(s => s!.StageKind)
               .FirstOrDefaultAsync(p => p.PortalUserId == id && p.ClientId == client.ClientId, ct)
           ?? throw new NotFoundException("Usuario de portal", id);

    private async Task<PortalUserDto> LoadDtoAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        var pu = await db.PortalUsers.AsNoTracking().Include(p => p.Role).Include(p => p.Status)
            .FirstOrDefaultAsync(p => p.PortalUserId == id && p.ClientId == client.ClientId, ct) ?? throw new NotFoundException("Usuario de portal", id);
        return ToDto(pu, client.PublicId);
    }

    private async Task<ApplicationUser> AccountOfAsync(PortalUser pu)
    {
        if (pu.UserId is null) throw new ConflictException("El usuario de portal no tiene cuenta asociada.");
        return await users.FindByIdAsync(pu.UserId.Value.ToString()) ?? throw new ConflictException("El usuario de portal no tiene cuenta asociada.");
    }

    /// <summary>Transición vía StatusService (efectos incluidos) y asignación del estatus en la misma unidad de trabajo.</summary>
    private async Task MoveAsync(PortalUser pu, string toCode, CancellationToken ct)
    {
        var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, pu.StatusCodeId, toCode, null, ct);
        pu.StatusCodeId = to.StatusCodeId;
        await db.SaveChangesAsync(ct);
    }

    private static bool Is(PortalUser pu, string statusCode)
        => string.Equals(pu.Status?.InternalCode, statusCode, StringComparison.OrdinalIgnoreCase);

    private PortalUserDto ToDto(PortalUser p, Guid clientPublicId) => new(
        p.PortalUserId, clientPublicId, p.Email, p.FullName,
        p.Role?.InternalCode ?? "", MultilingualText.Resolve(p.Role?.LabelJson, tenant.Lang),
        p.Status?.InternalCode ?? "", MultilingualText.Resolve(p.Status?.LabelJson, tenant.Lang),
        p.LastLoginUtc, p.IsActive, p.UserId.HasValue);

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("email", "El correo es obligatorio.");
        try { return ContactPointService.ValidateValue("EMAIL", email); }
        catch (ValidationException) { throw new ValidationException("email", "Correo inválido."); }
    }

    private static string? OptionalText(string? value, string field, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > max) throw new ValidationException(field, $"Máximo {max} caracteres.");
        return v;
    }

    private static void ThrowIdentityErrors(IdentityResult result, string field)
    {
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        if (result.Errors.Any(e => e.Code.StartsWith("Duplicate", StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException(DuplicateEmail);
        throw new ValidationException(field, message);
    }
}
