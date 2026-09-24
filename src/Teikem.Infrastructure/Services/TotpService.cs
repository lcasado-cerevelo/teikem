using System.Security.Cryptography;
using System.Text;

namespace Teikem.Infrastructure.Services;

/// <summary>TOTP RFC 6238 (HMAC-SHA1, 6 dígitos, 30 s) + Base32, sin dependencias externas. Compatible con Google/Microsoft Authenticator.</summary>
public static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const int Digits = 6;
    public const int PeriodSeconds = 30;

    public static byte[] GenerateSecret(int bytes = 20) => RandomNumberGenerator.GetBytes(bytes);

    public static string BuildOtpAuthUri(string issuer, string account, byte[] secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={Base32Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";

    public static string ComputeCode(byte[] secret, long unixSeconds)
    {
        var counter = unixSeconds / PeriodSeconds;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(msg);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | (hash[offset + 3] & 0xFF);
        return (binary % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }

    /// <summary>Acepta el código del paso actual ± window pasos (tolerancia de reloj).</summary>
    public static bool Verify(byte[] secret, string? code, int window = 1, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim().Replace(" ", "");
        if (c.Length != Digits || !c.All(char.IsDigit)) return false;
        var t = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        for (var i = -window; i <= window; i++)
        {
            var expected = ComputeCode(secret, t + i * PeriodSeconds);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(c))) return true;
        }
        return false;
    }

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int bits = 0, value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Base32Alphabet[(value >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(value << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        s = s.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var output = new List<byte>(s.Length * 5 / 8);
        int bits = 0, value = 0;
        foreach (var ch in s)
        {
            var idx = Base32Alphabet.IndexOf(ch);
            if (idx < 0) throw new FormatException("Base32 inválido.");
            value = (value << 5) | idx; bits += 5;
            if (bits >= 8) { output.Add((byte)((value >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return output.ToArray();
    }

    /// <summary>Códigos de recuperación de un solo uso (se devuelven una vez; se guardan hasheados).</summary>
    public static IReadOnlyList<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var raw = Base32Encode(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
            codes.Add($"{raw[..4]}-{raw[4..8]}-{raw[8..12]}");
        }
        return codes;
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
