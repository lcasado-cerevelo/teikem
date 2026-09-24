using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Teikem.Domain.Identity;

namespace Teikem.Infrastructure.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "teikem";
    public string Audience { get; set; } = "teikem-api";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int MfaChallengeMinutes { get; set; } = 5;
}

/// <summary>Claims propios del access token.</summary>
public static class TeikemClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string TenantId = "tid";
    public const string PlatformAdmin = "pa";
    public const string UserKind = "kind";
    public const string SessionId = "sid";
    public const string SecurityStamp = "ss";
    public const string Aal2At = "aal2";
    public const string Purpose = "purpose";
    public const string PurposeAccess = "access";
    public const string PurposeMfa = "mfa";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_o.SigningKey));

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, int tenantId, long sessionId, string? userKind, DateTime? aal2At)
    {
        var claims = new List<Claim>
        {
            new(TeikemClaims.Subject, user.Id.ToString()),
            new(TeikemClaims.Email, user.Email ?? string.Empty),
            new(TeikemClaims.Name, user.FullName ?? user.Email ?? string.Empty),
            new(TeikemClaims.TenantId, tenantId.ToString()),
            new(TeikemClaims.SessionId, sessionId.ToString()),
            new(TeikemClaims.SecurityStamp, StampHash(user.SecurityStamp)),
            new(TeikemClaims.Purpose, TeikemClaims.PurposeAccess),
            new(TeikemClaims.UserKind, userKind ?? "INTERNAL"),
        };
        if (user.IsPlatformAdmin) claims.Add(new Claim(TeikemClaims.PlatformAdmin, "1"));
        if (aal2At.HasValue) claims.Add(new Claim(TeikemClaims.Aal2At, new DateTimeOffset(aal2At.Value, TimeSpan.Zero).ToUnixTimeSeconds().ToString()));
        return Create(claims, TimeSpan.FromMinutes(_o.AccessTokenMinutes));
    }

    public (string Token, DateTime ExpiresAtUtc) CreateMfaChallengeToken(ApplicationUser user, int tenantId, string? deviceInfo)
    {
        var claims = new List<Claim>
        {
            new(TeikemClaims.Subject, user.Id.ToString()),
            new(TeikemClaims.TenantId, tenantId.ToString()),
            new(TeikemClaims.Purpose, TeikemClaims.PurposeMfa),
            new(TeikemClaims.SecurityStamp, StampHash(user.SecurityStamp)),
        };
        if (!string.IsNullOrEmpty(deviceInfo)) claims.Add(new Claim("device", deviceInfo));
        return Create(claims, TimeSpan.FromMinutes(_o.MfaChallengeMinutes));
    }

    private (string, DateTime) Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, now, expires,
            new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string StampHash(string? stamp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? ""))).ToLowerInvariant()[..16];

    public static string NewRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(48));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
