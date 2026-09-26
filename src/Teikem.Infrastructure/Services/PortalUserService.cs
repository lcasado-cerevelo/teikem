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
/// reactivar y dar de baja. Lote 3 (ajuste B) — portal multi-cliente: una misma cuenta (ApplicationUser PORTAL) puede
/// pertenecer a varios clientes del tenant, con una fila PortalUser por (cliente, cuenta). Reglas:
/// - Nadie escribe la contraseña de otro (R41): la cuenta se crea sin contraseña y el invitado la fija al aceptar.
/// - La invitación reutiliza el token de restablecimiento de Identity (DataProtector, vida Portal:InviteHours, un solo
///   uso porque ResetPassword rota el SecurityStamp); reenviar rota el stamp e invalida el enlace anterior; no hay tabla nueva.
/// - Invitar desde otro cliente un correo que ya es cuenta de portal del MISMO tenant crea la fila nueva: si la cuenta ya
///   tiene contraseña y está viva, nace INVITED y pasa de inmediato a ACTIVE (sin token; SecurityEvent 'portal_client_added');
///   si no tiene contraseña (o la cuenta quedó desactivada porque no le quedaba ningún cliente vivo) nace INVITED con token.
/// - Un correo no puede ser a la vez usuario interno y de portal (RequireUniqueEmail, bidireccional: UserAdminService rechaza
///   el sentido contrario). Todo choque de correo responde el MISMO 409 neutro (usuario interno, cuenta de portal de otro
///   tenant, o ya invitado/activo/suspendido en el mismo cliente) para no revelar dónde está registrado; queda SecurityEvent
///   ROLE_CHANGE/FAILURE 'portal_invite_conflict' para detectar enumeración masiva. Residual aceptado: 409 vs 200 revela
///   existencia global del correo.
/// - Una fila dada de baja (DISABLED) del mismo cliente renace con el mismo correo (nacimiento null → INVITED con historial).
/// - accept-invite activa TODAS las filas INVITED de la cuenta (un solo enlace sirve para todos los clientes pendientes).
/// - Suspender/reactivar/quitar actúan por fila (por cliente); PortalUserStatusEffect desactiva la cuenta solo cuando no le
///   queda ninguna otra fila viva en el tenant, y la reactiva cuando vuelve a haber una.
/// - Los usuarios de portal no reciben UserTenant (no aparecen en /api/v1/users, R39): su alcance es PortalUser.ClientId.
/// - Todo cambio de estatus pasa por StatusService (INVITED → ACTIVE; SUSPENDED lateral reversible; DISABLED terminal).
/// - Toda fila se alcanza a través del cliente del tenant (ResolveClientAsync + ClientId); IgnoreQueryFilters solo en
///   accept-invite, porque no hay tenant en el principal.
/// - Las acciones del admin de plataforma quedan atribuidas a él en SecurityEvent y AuditLog (R42).
/// - Cliente dado de baja (Lote 3, ajuste A): la lista sigue consultándose; invitar y reenviar responden 409.
/// </summary>
public sealed class PortalUserService(
    TeikemDbContext db, UserManager<ApplicationUser> users, ITenantContext tenant, ILookupCache lookups, StatusService statuses,
    ModuleService modules, ISecurityEventWriter security, IInvitationSender inviter, IPasswordBreachChecker breachChecker, IConfiguration config)
{
    private const string InvalidInvite = "Invitación inválida o vencida.";
    /// <summary>Único mensaje para todo choque de correo (mismo cliente, otro tenant o usuario interno): no revela la causa.</summary>
    public const string DuplicateEmail = "Ese correo no está disponible para el portal de esta compañía.";
    /// <summary>Comentario del historial cuando la fila nueva pasa a ACTIVE sin invitación (la cuenta ya tenía contraseña).</summary>
    public const string ClientAddedComment = "Cliente agregado a una cuenta de portal existente";

    /// <summary>Resultado interno de la transacción de invitación.</summary>
    private sealed record InviteOutcome(int PortalUserId, int ClientId, string ClientName, ApplicationUser Account, bool Reinvited, bool ActivatedImmediately);

    // ---------------- Consulta ----------------

    public async Task<IReadOnlyList<PortalUserDto>> GetForClientAsync(Guid clientPublicId, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct); // un cliente dado de baja sigue mostrando sus usuarios
        var rows = await db.PortalUsers.AsNoTracking().Include(p => p.Role).Include(p => p.Status)
            .Where(p => p.ClientId == client.ClientId)
            .OrderBy(p => p.Email).ToListAsync(ct);
        return await ToDtosAsync(rows, client.PublicId, ct);
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

        InviteOutcome result;
        try
        {
            result = await db.RunInTransactionAsync(async ct2 =>
            {
                var client = await db.ResolveClientAsync(clientPublicId, ct2);
                ClientQueries.EnsureClientActive(client); // Lote 3 (ajuste A): cliente dado de baja ⇒ 409, nada nuevo

                // Filas de ese correo en el tenant (filtro global), en cualquier cliente: una por cliente.
                var rows = await db.PortalUsers.Include(p => p.Status).ThenInclude(st => st!.StageKind)
                    .Where(p => p.Email == email).ToListAsync(ct2);
                var sameClient = rows.FirstOrDefault(p => p.ClientId == client.ClientId);
                if (sameClient is not null && sameClient.Status?.StageKind?.InternalCode != StageKinds.Terminal)
                    throw new ConflictException(DuplicateEmail); // ya invitado, activo o suspendido en este cliente

                var account = await users.FindByEmailAsync(email);
                if (account is not null)
                {
                    // Solo sirve una cuenta de portal que ya pertenezca a este tenant (alguna fila suya, aunque esté dada de baja).
                    // Usuario interno o cuenta de portal de otro tenant ⇒ mismo 409 neutro.
                    var kindCode = account.UserKindLookupId is null ? null : (await lookups.GetAsync(account.UserKindLookupId.Value, ct2))?.InternalCode;
                    if (kindCode != UserKinds.Portal || !rows.Any(p => p.UserId == account.Id)) throw new ConflictException(DuplicateEmail);
                }
                else
                {
                    if (rows.Any(p => p.UserId is not null)) throw new ConflictException(DuplicateEmail); // fila huérfana: no se rehace la cuenta a ciegas
                    // Cuenta SIN contraseña (R41) y sin UserTenant: el alcance del portal es el cliente.
                    account = new ApplicationUser
                    {
                        UserName = email, Email = email, FullName = fullName, UserKindLookupId = portalKindId,
                        DefaultTenantId = tenantId, IsActive = true, EmailConfirmed = false,
                    };
                    var created = await users.CreateAsync(account);
                    if (!created.Succeeded) ThrowIdentityErrors(created, "email");
                }

                // La cuenta ya vive en el portal (tiene contraseña y sigue habilitada): solo se le agrega este cliente, sin enlace.
                // Si está desactivada (no le quedaba ningún cliente vivo) o nunca fijó contraseña, el enlace de invitación manda.
                var activateNow = account.IsActive && await users.HasPasswordAsync(account);
                if (!activateNow && !account.IsActive)
                {
                    account.IsActive = true;
                    account.EmailConfirmed = false;
                    if (fullName is not null) account.FullName = fullName;
                    var updated = await users.UpdateAsync(account);
                    if (!updated.Succeeded) ThrowIdentityErrors(updated, "email");
                }

                PortalUser pu;
                var reinvited = sameClient is not null;
                if (sameClient is not null)
                {
                    // Renacimiento de la fila DISABLED del mismo cliente: se reutiliza la fila (rol y nombre nuevos) con nacimiento null → INVITED.
                    pu = sameClient;
                    pu.UserId = account.Id;
                    pu.FullName = fullName ?? pu.FullName;
                    pu.RoleLookupId = roleId;
                    pu.IsActive = true;
                }
                else
                {
                    var initial = await statuses.GetInitialAsync(StatusDomains.PortalUserStatus, ct2);
                    pu = new PortalUser
                    {
                        TenantId = tenantId, ClientId = client.ClientId, UserId = account.Id, Email = email,
                        FullName = fullName ?? account.FullName, RoleLookupId = roleId, StatusCodeId = initial.StatusCodeId, IsActive = true,
                    };
                    db.PortalUsers.Add(pu);
                    await db.SaveGuardedAsync(DuplicateEmail, ct2); // UQ_PortalUser (TenantId, ClientId, Email) es la segunda barrera
                }

                // Historial de estatus desde el nacimiento (INVITED es la etapa inicial tras el reorden del seed).
                var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, null,
                    PortalUserStatuses.Invited, reinvited ? "Reinvitación tras baja" : null, ct2);
                pu.StatusCodeId = to.StatusCodeId;
                await db.SaveChangesAsync(ct2);

                if (activateNow)
                {
                    // INVITED → ACTIVE de inmediato: la cuenta ya tiene contraseña; el efecto no toca la cuenta (sigue activa).
                    var active = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, pu.StatusCodeId,
                        PortalUserStatuses.Active, ClientAddedComment, ct2);
                    pu.StatusCodeId = active.StatusCodeId;
                    await db.SaveChangesAsync(ct2);
                }
                return new InviteOutcome(pu.PortalUserId, client.ClientId, client.Name, account, reinvited, activateNow);
            }, ct);
        }
        catch (ConflictException ex) when (ex.Message == DuplicateEmail)
        {
            // Sin la causa: solo deja rastro de que alguien pidió un correo no disponible (detección de enumeración).
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Failure, tenant.UserId, tenantId,
                new { action = "portal_invite_conflict", client = clientPublicId, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
            throw;
        }

        var role = req.Role.Trim().ToUpperInvariant();
        if (result.ActivatedImmediately)
        {
            await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
                new { action = "portal_client_added", portalUser = result.PortalUserId, client = result.ClientId, email, role, reinvited = result.Reinvited, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);
            var activeDto = await LoadDtoAsync(clientPublicId, result.PortalUserId, ct);
            return new PortalInviteResultDto(activeDto, null, null);
        }

        var (token, expiresAt) = await IssueInviteTokenAsync(result.Account, result.ClientName, ct);
        await security.WriteAsync(SecurityEventTypes.RoleChange, SecurityOutcomes.Success, tenant.UserId, tenantId,
            new { action = result.Reinvited ? "portal_reinvite" : "portal_invite", portalUser = result.PortalUserId, client = result.ClientId, email, role, byPlatformAdmin = tenant.IsPlatformAdmin }, ct);

        var dto = await LoadDtoAsync(clientPublicId, result.PortalUserId, ct);
        return new PortalInviteResultDto(dto, ReturnTokenInResponse ? token : null, expiresAt);
    }

    public async Task<PortalInviteResultDto> ResendInviteAsync(Guid clientPublicId, int id, CancellationToken ct)
    {
        var client = await db.ResolveClientAsync(clientPublicId, ct);
        ClientQueries.EnsureClientActive(client); // Lote 3 (ajuste A): cliente dado de baja ⇒ 409, no se reinvita
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
        var dtos = await ToDtosAsync([pu], client.PublicId, ct);
        return new PortalInviteResultDto(dtos[0], ReturnTokenInResponse ? token : null, expiresAt);
    }

    // ---------------- Suspender / reactivar / dar de baja (por fila = por cliente) ----------------

    /// <summary>ACTIVE → SUSPENDED (lateral, reversible) en este cliente. El efecto desactiva la cuenta solo si no le queda otro cliente vivo.</summary>
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

    /// <summary>SUSPENDED → ACTIVE (salida del lateral) en este cliente. La contraseña se conserva; el efecto vuelve a habilitar la cuenta.</summary>
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

    /// <summary>Cualquier estatus no terminal → DISABLED (terminal = baja definitiva en este cliente, nunca DELETE).</summary>
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
    /// El invitado fija su contraseña con el token recibido y quedan ACTIVE todas sus filas INVITED (uno o varios clientes).
    /// Cualquier fallo (no existe, ninguna fila INVITED, token inválido o vencido, contraseña débil o en brecha, módulo
    /// CLIENT_PORTAL apagado) responde el MISMO 400 sin enumerar, y queda como SecurityEvent PASSWORD_CHANGE/FAILURE sin
    /// userId (con el tenant del PortalUser si se resolvió).
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
        // Todas las filas de la cuenta viven en el mismo tenant (DefaultTenantId); se toma el de la primera por seguridad.
        var rows = await db.PortalUsers.IgnoreQueryFilters().Include(p => p.Status)
            .Where(p => p.UserId == user.Id && p.IsActive).OrderBy(p => p.PortalUserId).ToListAsync(ct);
        if (rows.Count == 0) { await FailAsync("no_portal_user"); return; }
        var tenantId = rows[0].TenantId;
        tenantIdForEvent = tenantId;
        var invited = rows.Where(p => p.TenantId == tenantId && Is(p, PortalUserStatuses.Invited)).ToList();
        if (invited.Count == 0) { await FailAsync("not_invited"); return; }
        // Una compañía desactivada no acepta invitaciones emitidas antes (misma respuesta neutra que el login la rechaza).
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId && t.IsActive, ct)) { await FailAsync("tenant_inactive"); return; }

        var enabled = await modules.GetEnabledKeysAsync(tenantId, ct);
        if (!enabled.Contains(ModuleKeys.ClientPortal)) { await FailAsync("module_disabled"); return; }

        var password = req.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(req.Token) || password.Length == 0) { await FailAsync("missing_token_or_password"); return; }
        if (await breachChecker.IsBreachedAsync(password, ct)) { await FailAsync("password_breached"); return; }

        // Desde aquí la acción queda atribuida al propio invitado dentro de su tenant (AuditLog y SecurityEvent).
        using (((TenantContext)tenant).As(tenantId, user.Id))
        {
            // Valida token (vida Portal:InviteHours, un solo uso: rota el SecurityStamp) y contraseña (NIST: ≥ 12, sin composición).
            var reset = await users.ResetPasswordAsync(user, req.Token.Trim(), password);
            if (!reset.Succeeded) { await FailAsync("token_or_password_rejected:" + string.Join(",", reset.Errors.Select(e => e.Code))); return; }

            user.EmailConfirmed = true;
            await users.UpdateAsync(user);

            // Un solo enlace activa todas las invitaciones pendientes de la cuenta (una por cliente).
            foreach (var pu in invited)
            {
                var to = await statuses.TransitionAsync(StatusDomains.PortalUserStatus, EntityTypes.PortalUser, pu.PortalUserId, pu.StatusCodeId, PortalUserStatuses.Active, null, ct);
                pu.StatusCodeId = to.StatusCodeId;
            }
            await db.SaveChangesAsync(ct);

            await security.WriteAsync(SecurityEventTypes.PasswordChange, SecurityOutcomes.Success, user.Id, tenantId,
                new { action = "portal_invite_accepted", portalUser = invited[0].PortalUserId, client = invited[0].ClientId,
                      portalUsers = invited.Select(p => p.PortalUserId).ToArray(), clients = invited.Select(p => p.ClientId).ToArray() }, ct);
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
        return (await ToDtosAsync([pu], client.PublicId, ct))[0];
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

    /// <summary>
    /// DTOs con los datos de la cuenta que no viven en la fila: HasPassword (AspNetUsers.PasswordHash) y ClientsCount
    /// (filas no dadas de baja de la misma cuenta en el tenant). Dos consultas por lote, sin N+1.
    /// </summary>
    private async Task<IReadOnlyList<PortalUserDto>> ToDtosAsync(IReadOnlyList<PortalUser> rows, Guid clientPublicId, CancellationToken ct)
    {
        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var passwords = new Dictionary<int, bool>();
        var counts = new Dictionary<int, int>();
        if (userIds.Count > 0)
        {
            passwords = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, Has = u.PasswordHash != null }).ToDictionaryAsync(x => x.Id, x => x.Has, ct);
            counts = await db.PortalUsers.AsNoTracking()
                .Where(p => p.UserId != null && userIds.Contains(p.UserId.Value) && p.IsActive && p.Status!.StageKind!.InternalCode != StageKinds.Terminal)
                .GroupBy(p => p.UserId!.Value).Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        }
        return rows.Select(p => ToDto(p, clientPublicId,
            p.UserId.HasValue && passwords.GetValueOrDefault(p.UserId.Value),
            p.UserId.HasValue ? counts.GetValueOrDefault(p.UserId.Value) : 0)).ToList();
    }

    private PortalUserDto ToDto(PortalUser p, Guid clientPublicId, bool hasPassword, int clientsCount) => new(
        p.PortalUserId, clientPublicId, p.Email, p.FullName,
        p.Role?.InternalCode ?? "", MultilingualText.Resolve(p.Role?.LabelJson, tenant.Lang),
        p.Status?.InternalCode ?? "", MultilingualText.Resolve(p.Status?.LabelJson, tenant.Lang),
        p.LastLoginUtc, p.IsActive, p.UserId.HasValue, hasPassword, clientsCount);

    /// <summary>Largo máximo del correo (= PortalUser.Email NVARCHAR(150)); AspNetUsers admite más, la fila de portal no.</summary>
    public const int EmailMaxLength = 150;

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("email", "El correo es obligatorio.");
        string v;
        try { v = ContactPointService.ValidateValue("EMAIL", email); }
        catch (ValidationException) { throw new ValidationException("email", "Correo inválido."); }
        if (v.Length > EmailMaxLength) throw new ValidationException("email", $"Máximo {EmailMaxLength} caracteres.");
        return v;
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
