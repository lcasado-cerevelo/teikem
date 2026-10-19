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
    /// <summary>Lote 101: cliente de la sesión de un usuario de PORTAL (segundo filtro, además del tenant).</summary>
    public const string ClientId = "cid";
    /// <summary>Lote 117: consignatario (localización) de la sesión de un usuario de portal que es consignatario; solo ve y cambia lo suyo.</summary>
    public const string LocationId = "loc";
    public const string SessionId = "sid";
    /// <summary>Lote 135: alcances de una llave de API (separados por espacio) y el id público de la llave.</summary>
    public const string Scopes = "scp";
    public const string ApiKeyId = "akid";
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

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, int tenantId, long sessionId, string? userKind, DateTime? aal2At, int? portalClientId = null, int? portalLocationId = null)
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
        if (portalClientId is int cid) claims.Add(new Claim(TeikemClaims.ClientId, cid.ToString()));
        if (portalLocationId is int loc) claims.Add(new Claim(TeikemClaims.LocationId, loc.ToString()));
        if (aal2At.HasValue) claims.Add(new Claim(TeikemClaims.Aal2At, new DateTimeOffset(aal2At.Value, TimeSpan.Zero).ToUnixTimeSeconds().ToString()));
        return Create(claims, TimeSpan.FromMinutes(_o.AccessTokenMinutes));
    }

    /// <summary>Lote 135: access token de una llave de API: a nombre de la cuenta de servicio de la integración, con sus alcances y, si tiene cliente, limitado a él (claim cid). Sin sesión ni MFA.</summary>
    public (string Token, DateTime ExpiresAtUtc) CreateApiToken(ApplicationUser serviceUser, int tenantId, int? clientId, IReadOnlyCollection<string> scopes, string keyId)
    {
        var claims = new List<Claim>
        {
            new(TeikemClaims.Subject, serviceUser.Id.ToString()),
            new(TeikemClaims.Email, serviceUser.Email ?? string.Empty),
            new(TeikemClaims.Name, serviceUser.FullName ?? serviceUser.Email ?? string.Empty),
            new(TeikemClaims.TenantId, tenantId.ToString()),
            new(TeikemClaims.SecurityStamp, StampHash(serviceUser.SecurityStamp)),
            new(TeikemClaims.Purpose, TeikemClaims.PurposeAccess),
            new(TeikemClaims.UserKind, "API"),
            new(TeikemClaims.Scopes, string.Join(' ', scopes)),
            new(TeikemClaims.ApiKeyId, keyId),
        };
        if (clientId is int cid) claims.Add(new Claim(TeikemClaims.ClientId, cid.ToString()));
        return Create(claims, TimeSpan.FromMinutes(_o.AccessTokenMinutes));
    }

    /// <param name="lifetime">2026-09-30: el primer ingreso (correo, contraseña y MFA) usa una vida más larga (Auth:Onboarding:ChallengeMinutes).</param>
    /// <summary>Clave de la caché (60 s) con el sello de seguridad vigente del usuario, que valida cada token (Program.cs).
    /// Quien rota el sello y emite un token nuevo en la misma petición debe quitarla, o el token nuevo se rechaza hasta 60 s.</summary>
    public static string StampCacheKey(int userId) => $"stamp:{userId}";

    public (string Token, DateTime ExpiresAtUtc) CreateMfaChallengeToken(ApplicationUser user, int tenantId, string? deviceInfo, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim>
        {
            new(TeikemClaims.Subject, user.Id.ToString()),
            new(TeikemClaims.TenantId, tenantId.ToString()),
            new(TeikemClaims.Purpose, TeikemClaims.PurposeMfa),
            new(TeikemClaims.SecurityStamp, StampHash(user.SecurityStamp)),
        };
        if (!string.IsNullOrEmpty(deviceInfo)) claims.Add(new Claim("device", deviceInfo));
        return Create(claims, lifetime ?? TimeSpan.FromMinutes(_o.MfaChallengeMinutes));
    }

    private (string, DateTime) Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(lifetime);
        // `iat` explícito (el constructor de JwtSecurityToken no lo agrega): lo usa el sello de sesiones de aparato.
        claims = claims.Append(IssuedAtClaim(now));
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, now, expires,
            new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>Claim `iat` (segundos Unix, numérico en el JWT).</summary>
    public static Claim IssuedAtClaim(DateTime issuedAtUtc) =>
        new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedAtUtc.ToUniversalTime()).ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);

    public static string StampHash(string? stamp) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? ""))).ToLowerInvariant()[..16];

    public static string NewRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(48));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
