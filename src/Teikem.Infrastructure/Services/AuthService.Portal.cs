using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>Un cliente al que un usuario de portal tiene acceso ACTIVO (PortalUser ACTIVE, cliente y compañía vivos).</summary>
public sealed record PortalAccessRow(int TenantId, string TenantName, int ClientId, Guid ClientPublicId, string ClientName, int? LocationId = null);

/// <summary>
/// Lote 101 — acceso del portal por cliente. Login SEPARADO del del staff (<c>POST /api/v1/portal/auth/login</c>): solo cuentas de tipo PORTAL, con acceso ACTIVO a algún cliente. La sesión queda ligada a
/// UN cliente (claim <c>cid</c>, guardado en RefreshToken.PortalClientId y conservado al renovar); ese cliente se vuelve el segundo filtro de datos (además de la compañía) en cada petición.
/// Protecciones: mismo bloqueo de cuenta que el staff (1, 5, 15, 60 min), respuesta y tiempo iguales para cuenta inexistente/bloqueada, reto anti-robot (Turnstile) tras varios fallos, computadora de confianza
/// SIEMPRE (una computadora nueva se confirma por correo) y MFA obligatorio (el código se recuerda 30 días por computadora de confianza).
/// </summary>
public sealed partial class AuthService
{
    public const string PortalNoAccessMessage = "El usuario no tiene acceso activo al portal.";
    public const string PortalClientMessage = "No tiene acceso a ese cliente.";
    public const string PortalCaptchaMessage = "Confirme que no es un robot para continuar.";

    /// <summary>Clientes (de cualquier compañía) a los que la cuenta tiene acceso ACTIVO. Sin filtros globales: no hay compañía en el principal todavía.</summary>
    private async Task<List<PortalAccessRow>> PortalAccessAsync(int userId, CancellationToken ct)
    {
        var active = PortalUserStatuses.Active;
        return await (from p in db.PortalUsers.AsNoTracking().IgnoreQueryFilters()
                      join c in db.Clients.AsNoTracking().IgnoreQueryFilters() on p.ClientId equals c.ClientId
                      join t in db.Tenants.AsNoTracking().IgnoreQueryFilters() on p.TenantId equals t.TenantId
                      where p.UserId == userId && p.IsActive && p.Status!.InternalCode == active && c.IsActive && t.IsActive
                      orderby t.Name, c.Name
                      select new PortalAccessRow(p.TenantId, t.Name, p.ClientId, c.PublicId, c.Name, p.LocationId)).ToListAsync(ct);
    }

    /// <summary>Lote 117: el consignatario (localización) de la persona en ese cliente, si su acceso es de consignatario; null si es una persona del cliente.</summary>
    private async Task<int?> PortalLocationOfAsync(int userId, int? clientId, CancellationToken ct)
    {
        if (clientId is null) return null;
        var active = PortalUserStatuses.Active;
        return await db.PortalUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.UserId == userId && p.ClientId == clientId && p.IsActive && p.Status!.InternalCode == active)
            .Select(p => p.LocationId).FirstOrDefaultAsync(ct);
    }

    /// <summary>El acceso con el que se entra: el pedido (si es de la cuenta), si no el de la compañía predeterminada y, a falta, el primero por nombre.</summary>
    private static PortalAccessRow? PickPortalAccess(ApplicationUser user, IReadOnlyList<PortalAccessRow> access, Guid? requested)
    {
        if (access.Count == 0) return null;
        if (requested is Guid want) return access.FirstOrDefault(a => a.ClientPublicId == want);
        return access.FirstOrDefault(a => a.TenantId == user.DefaultTenantId) ?? access[0];
    }

    /// <summary>En cada renovación: la cuenta sigue activa y con acceso ACTIVO al cliente de la sesión; si no, la sesión se revoca (401).</summary>
    private async Task EnsurePortalSessionStillValidAsync(RefreshToken rt, CancellationToken ct)
    {
        var access = await PortalAccessAsync(rt.UserId, ct);
        if (access.Any(a => a.TenantId == rt.TenantId && a.ClientId == rt.PortalClientId)) return;
        await RevokeAsync(rt, null, ct);
        await security.WriteAsync(SecurityEventTypes.TokenRevoked, SecurityOutcomes.Blocked, rt.UserId, rt.TenantId, new { reason = "portal_access_lost", client = rt.PortalClientId }, ct);
        throw new UnauthorizedException();
    }

    public PortalAuthConfigDto PortalAuthConfig() => new(turnstile?.Enabled == true, turnstile?.Enabled == true ? turnstile.SiteKey : null, turnstile?.RequireAfterFailures ?? 0);

    // ---------------- Login del portal ----------------

    public async Task<AuthResultDto> PortalLoginAsync(PortalLoginRequest req, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
        var kind = user is null || user.UserKindLookupId is null ? null : (await lookups.GetAsync(user.UserKindLookupId.Value, ct))?.InternalCode;
        // Cuenta inexistente, inactiva o que no es de portal (un usuario del staff entra por su propio login): la misma respuesta que una contraseña equivocada.
        if (user is null || !user.IsActive || kind != UserKinds.Portal)
        {
            BurnPasswordCheck(req.Password);
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Failure, null, null, new { portal = true, email = req.Email }, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }
        if (await users.IsLockedOutAsync(user))
        {
            BurnPasswordCheck(req.Password);
            await security.WriteAsync(SecurityEventTypes.Lockout, SecurityOutcomes.Blocked, user.Id, null, new { portal = true }, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        // Reto anti-robot tras varios fallos seguidos (antes de mirar la contraseña, para que no se pueda saltar).
        if (turnstile is { Enabled: true } && user.AccessFailedCount >= turnstile.RequireAfterFailures)
        {
            if (string.IsNullOrWhiteSpace(req.CaptchaToken) || !await turnstile.VerifyAsync(req.CaptchaToken, tenant.IpAddress, ct))
            {
                await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, null, new { portal = true, stage = "captcha", given = !string.IsNullOrWhiteSpace(req.CaptchaToken) }, ct);
                return new AuthResultDto("captcha_required", null, null, false, null);
            }
        }

        if (!await users.CheckPasswordAsync(user, req.Password ?? string.Empty))
        {
            var lockMinutes = await RegisterFailureAsync(user, ct);
            var locked = lockMinutes is not null;
            await security.WriteAsync(locked ? SecurityEventTypes.Lockout : SecurityEventTypes.Login, locked ? SecurityOutcomes.Blocked : SecurityOutcomes.Failure, user.Id, null,
                locked ? new { portal = true, stage = "login", locked = true, minutes = lockMinutes } : new { portal = true }, ct);
            throw new UnauthorizedException(InvalidCredentials);
        }
        await users.ResetAccessFailedCountAsync(user);

        var access = await PortalAccessAsync(user.Id, ct);
        if (access.Count == 0)
        {
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, null, new { portal = true, reason = "no_access" }, ct);
            throw new ForbiddenException(PortalNoAccessMessage);
        }
        var pick = PickPortalAccess(user, access, req.ClientPublicId);
        if (pick is null)
        {
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Blocked, user.Id, null, new { portal = true, reason = "client_not_allowed" }, ct);
            throw new ForbiddenException(PortalClientMessage);
        }

        // Computadora de confianza: en el portal SIEMPRE (no depende de Auth:TrustedBrowsers:Enabled, que es del staff).
        var trusted = new TrustedBrowserCheck(false, false, "Este cliente no identifica la computadora.", null);
        if (trustedBrowsers is not null)
        {
            trusted = await trustedBrowsers.CheckAsync(user, req.TrustedBrowserToken, pick.TenantName, ct, req.DoNotTrust);
            if (!trusted.Trusted)
                return new AuthResultDto("browser_confirmation_required", null, null, false, null, null, new BrowserConfirmationDto(MaskEmail(user.Email ?? string.Empty), trusted.Sent, trusted.Reason, trusted.DevLink));
        }

        // MFA obligatorio; con la computadora de confianza y el código escrito hace menos de 30 días no se repite (la sesión nace sin AAL2: las acciones sensibles lo vuelven a pedir).
        // Lote 117: el consignatario NO tiene MFA (decidido con Luis; docs/p10): su acceso es contraseña + computadora de confianza.
        var totp = pick.LocationId is null ? await ConfirmedTotpAsync(user.Id, ct) : null;
        if (pick.LocationId is null && (totp is null || !trusted.MfaRecent))
        {
            var (challenge, _) = jwt.CreateMfaChallengeToken(user, pick.TenantId, req.DeviceInfo);
            await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, pick.TenantId, new { portal = true, stage = "password", mfa = totp is not null ? "required" : "enrollment_required" }, ct);
            return new AuthResultDto("mfa_required", null, challenge, totp is null, null, DevMfaCode: DevMfaCodeOf(totp));
        }

        var pair = await IssueAsync(user, pick.TenantId, UserKinds.Portal, req.DeviceInfo, aal2At: null, ct, portalClientId: pick.ClientId);
        await security.WriteAsync(SecurityEventTypes.Login, SecurityOutcomes.Success, user.Id, pick.TenantId, new { portal = true, client = pick.ClientId, mfa = "remembered_30_days", device = req.DeviceInfo }, ct);
        if (trustedBrowsers is not null) await trustedBrowsers.ConsumeIfSingleUseAsync(user.Id, req.TrustedBrowserToken, ct);
        return new AuthResultDto("ok", pair, null, false, null);
    }

    /// <summary>Cambia el cliente de la sesión (la cuenta tiene acceso a varios): revoca la sesión actual y emite otra ligada al cliente elegido. 403 si no es suyo.</summary>
    public async Task<TokenPairDto> PortalSwitchClientAsync(PortalSwitchClientRequest req, CancellationToken ct)
    {
        var rt = await FindActiveAsync(req.RefreshToken, ct);
        if (rt.PortalClientId is null) throw new ForbiddenException("Solo la sesión de un usuario de portal cambia de cliente.");
        var user = await users.FindByIdAsync(rt.UserId.ToString()) ?? throw new UnauthorizedException();
        var access = await PortalAccessAsync(user.Id, ct);
        var pick = access.FirstOrDefault(a => a.ClientPublicId == req.ClientPublicId) ?? throw new ForbiddenException(PortalClientMessage);
        await RevokeAsync(rt, null, ct);
        var pair = await IssueAsync(user, pick.TenantId, UserKinds.Portal, rt.DeviceInfo, rt.Aal2VerifiedAtUtc, ct, portalClientId: pick.ClientId);
        rt.ReplacedByTokenHash = JwtTokenService.HashToken(pair.RefreshToken);
        db.SuppressAudit = true; await db.SaveChangesAsync(ct); db.SuppressAudit = false;
        await security.WriteAsync(SecurityEventTypes.TenantSwitch, SecurityOutcomes.Success, user.Id, pick.TenantId, new { portal = true, fromClient = rt.PortalClientId, toClient = pick.ClientId }, ct);
        return pair;
    }

    /// <summary>Para el servicio de /portal/me: los clientes de la cuenta.</summary>
    public Task<List<PortalAccessRow>> PortalClientsOfAsync(int userId, CancellationToken ct) => PortalAccessAsync(userId, ct);
}
