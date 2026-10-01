namespace Teikem.Infrastructure.Contracts;

public sealed record LoginRequest(string Email, string Password, int? TenantId, string? DeviceInfo);
public sealed record TokenPairDto(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc, int TenantId);
public sealed record TenantOptionDto(int TenantId, string Name, bool IsDefault);
/// <summary>Status: ok | mfa_required | onboarding_required. (tenant_selection ya no se emite desde 2026-09-30: el login escoge la
/// compañía solo.) onboarding_required (2026-09-30): primer ingreso pendiente; MfaChallengeToken sirve para los pasos de
/// /auth/onboarding/* y para enrolar el MFA, y Onboarding dice qué falta.</summary>
public sealed record AuthResultDto(string Status, TokenPairDto? Tokens, string? MfaChallengeToken, bool MfaEnrollmentRequired, IReadOnlyList<TenantOptionDto>? Tenants,
    OnboardingStateDto? Onboarding = null);

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
public sealed record MfaVerifyRequest(string Code, string? DeviceInfo);
public sealed record MfaEnrollResultDto(string Secret, string OtpAuthUri);
public sealed record MfaConfirmRequest(string Code);
public sealed record MfaConfirmResultDto(IReadOnlyList<string> RecoveryCodes);
public sealed record ReauthRequest(string Password, string? MfaCode);
public sealed record ReauthResultDto(string AccessToken, DateTime AccessExpiresAtUtc, DateTime Aal2VerifiedAtUtc);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
