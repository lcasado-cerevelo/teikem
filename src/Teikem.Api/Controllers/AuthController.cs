using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, DeviceService devices, ITenantContext tenant) : ControllerBase
{
    private long SessionId => long.TryParse(User.FindFirst(TeikemClaims.SessionId)?.Value, out var s) ? s : 0;

    /// <summary>Paso 1: correo + contraseña (+ tenant opcional). Puede devolver mfa_required o tenant_selection.</summary>
    [HttpPost("login"), AllowAnonymous]
    public Task<AuthResultDto> Login([FromBody] LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ct);

    /// <summary>Paso 2 (con el challenge token como Bearer): código TOTP o de recuperación.</summary>
    [HttpPost("mfa/verify"), Authorize(Policy = Policies.MfaChallenge)]
    public Task<AuthResultDto> VerifyMfa([FromBody] MfaVerifyRequest req, CancellationToken ct)
        => auth.VerifyMfaAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), int.Parse(User.FindFirst(TeikemClaims.TenantId)!.Value), User.FindFirst("device")?.Value, req, ct);

    // ---------------- Lote 8A: aparatos de confianza (app de almacén) ----------------

    /// <summary>
    /// Usuarios que pueden entrar en el aparato (anónimo: aparato + secreto). Internos, activos, con membresía activa, con PIN
    /// definido y con inventory.view, ordenados por nombre. Aparato inválido o desactivado → 401
    /// 'El aparato no está registrado o fue desactivado.'.
    /// </summary>
    [HttpPost("device/users"), AllowAnonymous]
    public Task<IReadOnlyList<DeviceUserDto>> DeviceUsers([FromBody] DeviceUsersRequest req, CancellationToken ct) => devices.GetDeviceUsersAsync(req, ct);

    /// <summary>
    /// Login por aparato + PIN (sin contraseña ni MFA). Devuelve el par de tokens de una sesión ligada al aparato
    /// (Tenant.DeviceSessionDays días, se renueva en cada refresh; el access token lleva el claim `did`).
    /// 401 'PIN incorrecto.' (cuenta el intento), 423 'PIN bloqueado por 15 minutos.' (5 fallos seguidos),
    /// 401 'El aparato no está registrado o fue desactivado.', 403 si el usuario no tiene inventory.view.
    /// </summary>
    [HttpPost("device/login"), AllowAnonymous]
    public Task<TokenPairDto> DeviceLogin([FromBody] DeviceLoginRequest req, CancellationToken ct) => auth.DeviceLoginAsync(req, ct);

    [HttpPost("refresh"), AllowAnonymous]
    public Task<TokenPairDto> Refresh([FromBody] RefreshRequest req, CancellationToken ct) => auth.RefreshAsync(req.RefreshToken, Request.Headers.UserAgent.FirstOrDefault(), ct);

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct) { await auth.LogoutAsync(req.RefreshToken, ct); return NoContent(); }

    [HttpPost("logout-all"), Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken ct) { await auth.LogoutAllAsync(ct); return NoContent(); }

    /// <summary>Cambio de compañía: recarga el contexto completo (menú, datos, permisos) con el TenantId elegido.</summary>
    [HttpPost("switch-tenant"), AllowAnonymous]
    public Task<TokenPairDto> SwitchTenant([FromBody] SwitchTenantRequest req, CancellationToken ct) => auth.SwitchTenantAsync(req, ct);

    /// <summary>Step-up AAL2: reautenticación reciente para acciones sensibles.</summary>
    [HttpPost("reauth"), Authorize]
    public Task<ReauthResultDto> Reauth([FromBody] ReauthRequest req, CancellationToken ct) => auth.ReauthAsync(SessionId, req, ct);

    [HttpGet("sessions"), Authorize]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => auth.GetSessionsAsync(SessionId, ct);

    [HttpDelete("sessions/{id:long}"), Authorize]
    public async Task<IActionResult> RevokeSession(long id, CancellationToken ct) { await auth.RevokeSessionAsync(id, SessionId, ct); return NoContent(); }

    [HttpPut("password"), Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct) { await auth.ChangePasswordAsync(req, SessionId, ct); return NoContent(); }

    /// <summary>Enrolar TOTP (con access token, o con challenge token cuando el tenant exige MFA y el usuario aún no lo tiene).</summary>
    [HttpPost("mfa/totp/enroll"), Authorize(Policy = Policies.AccessOrMfa)]
    public Task<MfaEnrollResultDto> EnrollTotp(CancellationToken ct) => auth.EnrollTotpAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), ct);

    [HttpPost("mfa/totp/confirm"), Authorize(Policy = Policies.AccessOrMfa)]
    public Task<MfaConfirmResultDto> ConfirmTotp([FromBody] MfaConfirmRequest req, CancellationToken ct) => auth.ConfirmTotpAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), req, ct);

    [HttpDelete("mfa/totp"), Authorize, RequireAal2]
    public async Task<IActionResult> DisableTotp(CancellationToken ct) { await auth.DisableTotpAsync(ct); return NoContent(); }
}
