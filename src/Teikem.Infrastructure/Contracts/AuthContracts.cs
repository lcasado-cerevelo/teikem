namespace Teikem.Infrastructure.Contracts;

public sealed record LoginRequest(string Email, string Password, int? TenantId, string? DeviceInfo);
public sealed record TokenPairDto(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc, int TenantId);
public sealed record TenantOptionDto(int TenantId, string Name, bool IsDefault);
/// <summary>Status: ok | mfa_required | tenant_selection.</summary>
public sealed record AuthResultDto(string Status, TokenPairDto? Tokens, string? MfaChallengeToken, bool MfaEnrollmentRequired, IReadOnlyList<TenantOptionDto>? Tenants);
public sealed record RefreshRequest(string RefreshToken);
public sealed record SwitchTenantRequest(string RefreshToken, int TenantId);
public sealed record MfaVerifyRequest(string Code, string? DeviceInfo);
public sealed record MfaEnrollResultDto(string Secret, string OtpAuthUri);
public sealed record MfaConfirmRequest(string Code);
public sealed record MfaConfirmResultDto(IReadOnlyList<string> RecoveryCodes);
public sealed record ReauthRequest(string Password, string? MfaCode);
public sealed record ReauthResultDto(string AccessToken, DateTime AccessExpiresAtUtc, DateTime Aal2VerifiedAtUtc);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
