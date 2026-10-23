using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Teikem.Api.Auth;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-23 — acceso de la app del chofer. Mismo motor que el login de la web (contraseña → MFA → sesión; primer ingreso con el correo personal y contraseña propia), con cuatro diferencias: (1) los choferes
/// «solo app» (tipo de usuario DRIVER) entran SOLO por aquí y la web los rechaza; (2) la sesión dura lo que fija DriverSessionRules (por compañía, la más permisiva, con el tope de la aplicación); (3) una sola sesión da
/// acceso a todas las compañías donde es chofer (<c>company-tokens</c>); (4) el challenge del MFA de esta app solo lo acepta este controlador, y el de la web solo el de la web. La renovación y el cierre son los de siempre (<c>/auth/refresh</c>, <c>/auth/logout</c>).
/// </summary>
[ApiController]
[Route("api/v1/driver-app/auth")]
public sealed class DriverAppAuthController(AuthService auth) : ControllerBase
{
    private bool ChallengeIsDriver => string.Equals(User.FindFirst(TeikemClaims.App)?.Value, TeikemClaims.AppDriver, StringComparison.Ordinal);
    private int ChallengeUserId => int.Parse(User.FindFirst(TeikemClaims.Subject)!.Value);
    private int ChallengeTenantId => int.Parse(User.FindFirst(TeikemClaims.TenantId)!.Value);

    /// <summary>Paso 1: correo y contraseña. 401 «Credenciales inválidas.»; 403 «Su usuario no está vinculado a un chofer activo.» si no es chofer en ninguna compañía.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.Login)]
    public Task<AuthResultDto> Login([FromBody] LoginRequest req, CancellationToken ct) => auth.LoginAsync(req, ct, driverApp: true);

    /// <summary>Paso 2 (con el challenge de esta app): código TOTP o de recuperación. 403 si el challenge es de la web.</summary>
    [HttpPost("mfa/verify"), Authorize(Policy = Policies.MfaChallenge), EnableRateLimiting(DeviceRateLimits.Login)]
    public Task<AuthResultDto> VerifyMfa([FromBody] MfaVerifyRequest req, CancellationToken ct)
    {
        if (!ChallengeIsDriver) throw new ForbiddenException(Teikem.Domain.Tenancy.DriverAuthMessages.NotADriverSessionMessage);
        return auth.VerifyMfaAsync(ChallengeUserId, ChallengeTenantId, User.FindFirst("device")?.Value, req, ct, driverApp: true);
    }

    /// <summary>Renueva la sesión del chofer: devuelve el refresh token NUEVO y un token de acceso por cada compañía donde es chofer (una sola sesión). Una compañía que exige MFA y esta sesión no lo cumplió responde `blocked: mfa_required`. 401 si el token ya se usó o venció.</summary>
    [HttpPost("company-tokens"), AllowAnonymous, EnableRateLimiting(DeviceRateLimits.DeviceAuth)]
    public Task<DriverSessionDto> CompanyTokens([FromBody] DriverCompanyTokensRequest req, CancellationToken ct) => auth.DriverCompanyTokensAsync(req.RefreshToken, ct);
}
