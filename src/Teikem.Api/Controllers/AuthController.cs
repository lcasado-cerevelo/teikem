using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, DeviceService devices) : ControllerBase
{
    private long SessionId => long.TryParse(User.FindFirst(TeikemClaims.SessionId)?.Value, out var s) ? s : 0;

    /// <summary>Sesión abierta en un aparato con PIN (access token con el claim `did`): sin contraseña ni MFA.</summary>
    private bool IsDeviceSession => User.HasClaim(c => c.Type == DeviceClaims.DeviceId);

    /// <summary>
    /// Lote 8A: la sesión de un aparato (solo PIN) no administra el segundo factor de la cuenta: quien viera el PIN en un
    /// aparato compartido, o quien lo asignó, se quedaría con el secreto TOTP y los códigos de recuperación y dejaría al
    /// dueño fuera de la web. 403 'La sesión de un aparato no administra el segundo factor.'. El challenge token del login
    /// nunca lleva `did`, así que el enrolamiento durante el login sigue igual.
    /// </summary>
    private void EnsureNotDeviceSession()
    {
        if (IsDeviceSession) throw new ForbiddenException(AuthService.DeviceSessionMfaMessage);
    }

    /// <summary>Paso 1: correo + contraseña (+ tenant opcional). Puede devolver mfa_required. Sin tenant entra a la compañía predeterminada (o la primera por nombre).</summary>
    [HttpPost("login"), AllowAnonymous]
    public Task<AuthResultDto> Login([FromBody] LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ct);

    /// <summary>Paso 2 (con el challenge token como Bearer): código TOTP o de recuperación.</summary>
    [HttpPost("mfa/verify"), Authorize(Policy = Policies.MfaChallenge)]
    public Task<AuthResultDto> VerifyMfa([FromBody] MfaVerifyRequest req, CancellationToken ct)
        => auth.VerifyMfaAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), int.Parse(User.FindFirst(TeikemClaims.TenantId)!.Value), User.FindFirst("device")?.Value, req, ct);

    // ---------------- Primer ingreso (2026-09-30): correo → contraseña propia → MFA, con el challenge token del login ----------------

    private int ChallengeUserId => int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value);
    private int ChallengeTenantId => int.Parse(User.FindFirst(TeikemClaims.TenantId)!.Value);

    /// <summary>Qué falta del primer ingreso.</summary>
    [HttpGet("onboarding"), Authorize(Policy = Policies.MfaChallenge)]
    public Task<OnboardingStateDto> Onboarding(CancellationToken ct) => auth.GetOnboardingAsync(ChallengeUserId, ct);

    /// <summary>Paso 1a: manda al correo un código de 6 dígitos. 409 'Su correo ya está verificado.' o
    /// 'No se pudo enviar el correo. Intente de nuevo en unos minutos.'.</summary>
    [HttpPost("onboarding/email/send"), Authorize(Policy = Policies.MfaChallenge), EnableRateLimiting(DeviceRateLimits.DeviceAuth)]
    public Task<OnboardingEmailSentDto> OnboardingSendEmail(CancellationToken ct) => auth.SendOnboardingEmailCodeAsync(ChallengeUserId, ChallengeTenantId, ct);

    /// <summary>Paso 1b: confirma el código. 400 'El código no es válido o venció.'.</summary>
    [HttpPost("onboarding/email/verify"), Authorize(Policy = Policies.MfaChallenge)]
    public Task<OnboardingStepDto> OnboardingVerifyEmail([FromBody] OnboardingCodeRequest req, CancellationToken ct)
        => auth.VerifyOnboardingEmailAsync(ChallengeUserId, ChallengeTenantId, req, ct);

    /// <summary>Paso 2: contraseña propia. 409 'Verifique primero su correo.'; 400 por política, brechas o
    /// 'La contraseña nueva debe ser distinta de la que le dieron.'. Devuelve un challenge token nuevo (el sello rotó).</summary>
    [HttpPost("onboarding/password"), Authorize(Policy = Policies.MfaChallenge)]
    public Task<OnboardingStepDto> OnboardingPassword([FromBody] OnboardingPasswordRequest req, CancellationToken ct)
        => auth.SetOnboardingPasswordAsync(ChallengeUserId, ChallengeTenantId, User.FindFirst("device")?.Value, req, ct);

    // ---------------- Lote 8A: aparatos de confianza (app de almacén) ----------------

    /// <summary>
    /// Usuarios que pueden entrar en el aparato (anónimo: aparato + secreto). Internos, activos, con membresía activa, con PIN
    /// definido y con inventory.view, ordenados por nombre. Aparato inválido o desactivado → 401
    /// 'El aparato no está registrado o fue desactivado.'.
    /// </summary>
    [HttpPost("device/users"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.DeviceAuth)]
    public Task<IReadOnlyList<DeviceUserDto>> DeviceUsers([FromBody] DeviceUsersRequest req, CancellationToken ct) => devices.GetDeviceUsersAsync(req, ct);

    /// <summary>
    /// Login por aparato + PIN (sin contraseña ni MFA). Devuelve el par de tokens de una sesión ligada al aparato
    /// (Tenant.DeviceSessionDays días, se renueva en cada refresh; el access token lleva el claim `did`).
    /// 401 'PIN incorrecto.' (cuenta el intento), 423 'PIN bloqueado por 15 minutos.' (5 fallos seguidos),
    /// 401 'El aparato no está registrado o fue desactivado.', 403 si el usuario no tiene inventory.view.
    /// Límite de intentos (también en device/users y heartbeat): 60 por minuto por IP y ruta → 429.
    /// </summary>
    [HttpPost("device/login"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.DeviceAuth)]
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
    /// <remarks>Con la sesión de un aparato (claim `did`) → 403 'La sesión de un aparato no administra el segundo factor.'.</remarks>
    [HttpPost("mfa/totp/enroll"), Authorize(Policy = Policies.AccessOrMfa)]
    public Task<MfaEnrollResultDto> EnrollTotp(CancellationToken ct)
    {
        EnsureNotDeviceSession();
        return auth.EnrollTotpAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), ct);
    }

    /// <remarks>Con la sesión de un aparato (claim `did`) → 403 'La sesión de un aparato no administra el segundo factor.'.</remarks>
    [HttpPost("mfa/totp/confirm"), Authorize(Policy = Policies.AccessOrMfa)]
    public Task<MfaConfirmResultDto> ConfirmTotp([FromBody] MfaConfirmRequest req, CancellationToken ct)
    {
        EnsureNotDeviceSession();
        return auth.ConfirmTotpAsync(int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value), req, ct);
    }

    [HttpDelete("mfa/totp"), Authorize, RequireAal2]
    public async Task<IActionResult> DisableTotp(CancellationToken ct) { await auth.DisableTotpAsync(ct); return NoContent(); }
}
