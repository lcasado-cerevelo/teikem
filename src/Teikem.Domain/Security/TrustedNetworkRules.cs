using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Teikem.Domain.Security;

/// <summary>Rango de red normalizado (IPv4 o IPv6): la dirección de red y el prefijo. Una sola IP es un rango /32 (IPv4) o /128 (IPv6).</summary>
public sealed record TrustedCidr(IPAddress Network, int Prefix)
{
    public bool IsIPv6 => Network.AddressFamily == AddressFamily.InterNetworkV6;

    /// <summary>Texto normalizado, p. ej. <c>203.0.113.10/32</c> o <c>192.168.10.0/24</c>.</summary>
    public string Text => $"{Network}/{Prefix}";

    /// <summary>true si es una sola dirección (prefijo completo).</summary>
    public bool IsSingle => Prefix == (IsIPv6 ? 128 : 32);
}

/// <summary>Resultado de leer una IP o un rango escrito por una persona. <see cref="WasAdjusted"/>: el texto traía bits de host y se llevó a la dirección de red.</summary>
public sealed record TrustedCidrParse(TrustedCidr? Cidr, string? Error, bool WasAdjusted);

/// <summary>
/// IP preferida / red de confianza (2026-10-12; docs/ip-preferida-mfa-diseno.md): lógica pura de IPs y rangos CIDR. Una entrada es una IP o un
/// rango; una IP desde la que llega la conexión «coincide» si cae dentro. Se lee con rigor (nada de formas sueltas como «127.1»), se normaliza
/// (IPv6 mapeada a IPv4 → IPv4; dirección de red) y se rechazan los rangos que confían en medio internet.
/// </summary>
public static class TrustedNetworkRules
{
    public const int NameMax = 100;
    public const int NoteMax = 300;
    /// <summary>Rango más ancho que se acepta: IPv4 /16 (65,536 direcciones) e IPv6 /48.</summary>
    public const int MinPrefixV4 = 16;
    public const int MinPrefixV6 = 48;

    public const string NameRequired = "Escriba un nombre para la red (p. ej. el almacén o la oficina).";
    public const string NameTooLong = "El nombre no puede exceder 100 caracteres.";
    public const string NoteTooLong = "La nota no puede exceder 300 caracteres.";
    public const string CidrRequired = "Escriba la IP o el rango de IPs.";
    public const string CidrInvalid = "No es una IP ni un rango válido. Ejemplos: 203.0.113.10 o 192.168.10.0/24.";
    public const string CidrPrefixInvalid = "La parte después de «/» no es válida (IPv4: de 0 a 32; IPv6: de 0 a 128).";
    public const string CidrNotRoutable = "Esa dirección no se puede usar como red de confianza (no es una IP de una red: vacía, de loopback o de multidifusión).";
    public const string CidrMappedWithPrefix = "Escriba la IP en formato IPv4 (p. ej. 203.0.113.10), sin la forma ::ffff:.";
    public const string NeedsInternalOnlyConfirmation = "Confirme que esta IP corresponde solo a la red interna de la empresa, no a una red de invitados ni a una red pública.";
    public const string ExpiryInPast = "La fecha de vencimiento debe ser futura.";
    public static string CidrTooWide(bool ipv6) => ipv6
        ? $"El rango es demasiado ancho: use /{MinPrefixV6} o más estrecho."
        : $"El rango es demasiado ancho (más de 65,536 direcciones): use /{MinPrefixV4} o más estrecho.";

    private static readonly Regex Ipv4 = new(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// IP suelta de una conexión → <see cref="IPAddress"/> normalizada: una IPv6 mapeada a IPv4 (::ffff:1.2.3.4) pasa a IPv4; null si no es una IP
    /// (vacío, texto raro, zona de IPv6). Acepta SOLO IPv4 con cuatro números o IPv6 con «:».
    /// </summary>
    public static IPAddress? NormalizeIp(string? text)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s) || s.Contains('%')) return null;
        IPAddress? ip;
        if (s.Contains(':')) { if (!IPAddress.TryParse(s, out ip) || ip.AddressFamily != AddressFamily.InterNetworkV6) return null; }
        else
        {
            if (!Ipv4.IsMatch(s) || !IPAddress.TryParse(s, out ip)) return null;
        }
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    /// <summary>Lee una IP («203.0.113.10» = /32) o un rango («192.168.10.0/24»); con bits de host («192.168.10.5/24») lo lleva a la red y lo avisa.</summary>
    public static TrustedCidrParse Parse(string? text)
    {
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return new TrustedCidrParse(null, CidrRequired, false);

        var slash = s.IndexOf('/');
        var addressText = slash >= 0 ? s[..slash] : s;
        int? prefix = null;
        if (slash >= 0)
        {
            if (!int.TryParse(s[(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p)) return new TrustedCidrParse(null, CidrPrefixInvalid, false);
            prefix = p;
        }

        var ip = NormalizeIp(addressText);
        if (ip is null) return new TrustedCidrParse(null, CidrInvalid, false);
        // una IPv6 mapeada con prefijo no se interpreta (la mezcla es confusa): se pide el formato IPv4
        if (prefix is not null && addressText.Contains(':') && ip.AddressFamily == AddressFamily.InterNetwork) return new TrustedCidrParse(null, CidrMappedWithPrefix, false);

        var v6 = ip.AddressFamily == AddressFamily.InterNetworkV6;
        var max = v6 ? 128 : 32;
        var len = prefix ?? max;
        if (len < 0 || len > max) return new TrustedCidrParse(null, CidrPrefixInvalid, false);
        if (len < (v6 ? MinPrefixV6 : MinPrefixV4)) return new TrustedCidrParse(null, CidrTooWide(v6), false);
        if (IsUnusable(ip)) return new TrustedCidrParse(null, CidrNotRoutable, false);

        var bytes = ip.GetAddressBytes();
        var network = (byte[])bytes.Clone();
        for (var i = 0; i < network.Length; i++)
        {
            var bitsInByte = Math.Clamp(len - i * 8, 0, 8);
            var mask = bitsInByte == 0 ? 0 : (byte)(0xFF << (8 - bitsInByte));
            network[i] = (byte)(network[i] & mask);
        }
        var adjusted = !network.AsSpan().SequenceEqual(bytes);
        return new TrustedCidrParse(new TrustedCidr(new IPAddress(network), len), null, adjusted);
    }

    /// <summary>¿Esta IP cae dentro del rango? Misma familia (IPv4 con IPv4); una IPv6 mapeada se compara como IPv4.</summary>
    public static bool Contains(TrustedCidr cidr, IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != cidr.Network.AddressFamily) return false;
        var a = ip.GetAddressBytes();
        var n = cidr.Network.GetAddressBytes();
        for (var i = 0; i < n.Length; i++)
        {
            var bitsInByte = Math.Clamp(cidr.Prefix - i * 8, 0, 8);
            if (bitsInByte == 0) break;
            var mask = (byte)(0xFF << (8 - bitsInByte));
            if ((a[i] & mask) != n[i]) return false;
        }
        return true;
    }

    /// <summary>Cantidad de direcciones que cubre el rango (como texto: en IPv6 el número no cabe en un entero de 64 bits).</summary>
    public static string AddressCount(TrustedCidr cidr)
    {
        var hostBits = (cidr.IsIPv6 ? 128 : 32) - cidr.Prefix;
        return (System.Numerics.BigInteger.One << hostBits).ToString();
    }

    private static bool IsUnusable(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork) return b[0] >= 224; // multidifusión, reservadas y difusión
        return b[0] == 0xFF; // multidifusión IPv6
    }
}
