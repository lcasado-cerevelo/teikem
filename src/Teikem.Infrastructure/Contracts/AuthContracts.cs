namespace Teikem.Infrastructure.Contracts;

/// <summary>TrustedBrowserToken (Lote 100): marca secreta aleatoria que la web guarda en el navegador para que el servidor reconozca la computadora.</summary>
public sealed record LoginRequest(string Email, string Password, int? TenantId, string? DeviceInfo, string? TrustedBrowserToken = null);
public sealed record TokenPairDto(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc, int TenantId);
public sealed record TenantOptionDto(int TenantId, string Name, bool IsDefault);
/// <summary>Status: ok | mfa_required | onboarding_required. (tenant_selection ya no se emite desde 2026-09-30: el login escoge la
/// compañía solo.) onboarding_required (2026-09-30): primer ingreso pendiente; MfaChallengeToken sirve para los pasos de
/// /auth/onboarding/* y para enrolar el MFA, y Onboarding dice qué falta.</summary>
public sealed record AuthResultDto(string Status, TokenPairDto? Tokens, string? MfaChallengeToken, bool MfaEnrollmentRequired, IReadOnlyList<TenantOptionDto>? Tenants,
    OnboardingStateDto? Onboarding = null, BrowserConfirmationDto? Browser = null, string? DevMfaCode = null);

public sealed record ConfirmBrowserRequest(string? Token);

/// <summary>Login del portal (Lote 101). DoNotTrust = la casilla «No confiar en esta computadora» (equipo compartido): entra una vez, no se recuerda y el MFA se pide siempre. ClientPublicId opcional: con qué cliente entrar si la cuenta tiene varios (por omisión el primero por nombre). CaptchaToken: el del widget de Turnstile cuando se pide.</summary>
public sealed record PortalLoginRequest(string Email, string Password, Guid? ClientPublicId, string? DeviceInfo, string? TrustedBrowserToken, string? CaptchaToken, bool DoNotTrust = false);
public sealed record PortalSwitchClientRequest(string RefreshToken, Guid ClientPublicId);
/// <summary>Configuración pública del login del portal: si pide reto anti-robot y con qué llave del sitio.</summary>
public sealed record PortalAuthConfigDto(bool CaptchaEnabled, string? CaptchaSiteKey, int CaptchaAfterFailures);

/// <summary>Status browser_confirmation_required (Lote 100): la computadora no se reconoce. Sent = salió el correo con el enlace; Message = por qué no; DevLink solo en desarrollo.</summary>
public sealed record BrowserConfirmationDto(string Email, bool Sent, string? Message, string? DevLink);

/// <summary>Primer ingreso (2026-09-30): en orden, verificar el correo → poner contraseña propia → configurar el MFA.
/// Email va enmascarado (j***@empresa.com).</summary>
public sealed record OnboardingStateDto(string Email, bool EmailVerified, bool PasswordChangeRequired, bool MfaConfigured);

/// <summary>Código de 6 dígitos enviado al correo. DevCode solo con Auth:Onboarding:ReturnEmailCodeInResponse (desarrollo).</summary>
public sealed record OnboardingEmailSentDto(string Email, bool Sent, string? DevCode = null);

public sealed record OnboardingCodeRequest(string? Code);

public sealed record OnboardingPasswordRequest(string? NewPassword);

/// <summary>Resultado de un paso: estado nuevo y, si el paso rotó el sello de seguridad (contraseña), un challenge token nuevo.</summary>
public sealed record OnboardingStepDto(OnboardingStateDto State, string? MfaChallengeToken);
public sealed record RefreshRequest(string RefreshToken);
public sealed record SwitchTenantRequest(string RefreshToken, int TenantId);
public sealed record MfaVerifyRequest(string Code, string? DeviceInfo, string? TrustedBrowserToken = null);
/// <remarks>DevMfaCode (2026-10-22): el código vigente, SOLO con el ambiente en Development (para no tener que abrir el autenticador al desarrollar); en cualquier otro ambiente siempre null.</remarks>
public sealed record MfaEnrollResultDto(string Secret, string OtpAuthUri, string? DevMfaCode = null);
public sealed record MfaConfirmRequest(string Code);
public sealed record MfaConfirmResultDto(IReadOnlyList<string> RecoveryCodes);
public sealed record ReauthRequest(string Password, string? MfaCode);
public sealed record ReauthResultDto(string AccessToken, DateTime AccessExpiresAtUtc, DateTime Aal2VerifiedAtUtc);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>2026-10-07: «Olvidé mi contraseña» en el login (anónimo).</summary>
public sealed record ForgotPasswordRequest(string? Email);
/// <summary>Respuesta siempre igual exista o no el correo; <c>Link</c> solo en desarrollo (sin proveedor de correo).</summary>
public sealed record ForgotPasswordResultDto(string Message, string? Link);
/// <summary>Poner la contraseña nueva con el enlace del correo. MfaCode (2026-10-12): código de la app de autenticación o de recuperación; se exige si la
/// persona tiene su MFA inscrito (sin él, 400 <c>mfa_required</c> y el enlace sigue vigente).</summary>
public sealed record ResetPasswordRequest(string? Email, string? Token, string? NewPassword, string? MfaCode = null);
/// <summary>Administración de usuarios: contraseña temporal (vacía = el servidor genera una).</summary>
public sealed record TemporaryPasswordRequest(string? Password);
public sealed record TemporaryPasswordDto(string Password, DateTime ExpiresAtUtc, int ValidMinutes);

/// <summary>
/// 2026-10-23 (app del chofer): el acceso de UNA compañía donde la persona es chofer, a partir de la misma sesión. `Blocked` = mfa_required cuando esa compañía exige el segundo factor y esta sesión no lo cumplió
/// (no se da token: entra de nuevo con contraseña y código). El token de acceso vale lo de siempre (minutos); se vuelve a pedir con la sesión.
/// </summary>
public sealed record DriverCompanyTokenDto(int TenantId, string TenantName, string? AccessToken, DateTime? AccessExpiresAtUtc, string? Blocked);
public sealed record DriverCompanyTokensRequest(string RefreshToken);
/// <summary>La sesión del chofer renovada: el refresh token NUEVO (el anterior queda revocado; reusarlo cierra la sesión por posible robo) y el acceso a cada compañía.</summary>
public sealed record DriverSessionDto(string RefreshToken, DateTime RefreshExpiresAtUtc, IReadOnlyList<DriverCompanyTokenDto> Companies);
