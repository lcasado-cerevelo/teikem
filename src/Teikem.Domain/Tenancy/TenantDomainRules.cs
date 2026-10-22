using System.Text.RegularExpressions;

namespace Teikem.Domain.Tenancy;

/// <summary>
/// 2026-10-22 (Luis) — la pantalla de entrada muestra el logo de la compañía según la dirección con la que se abrió: el DOMINIO propio de la compañía
/// (`logistica.micliente.com`) o un SUBDOMINIO de la plataforma (`depot` → `depot.teikem.com`). Una compañía puede tener uno, otro o ambos. Lo fija solo el
/// administrador de la plataforma. Aquí viven la normalización y la comparación (lógica pura, probada con xunit).
/// </summary>
public static class TenantDomainRules
{
    public const int MaxDomainLength = 253;
    public const int MaxSubdomainLength = 63;

    public const string DomainInvalidMessage = "El dominio no es válido. Escríbalo como logistica.miempresa.com, sin https:// ni rutas.";
    public const string SubdomainInvalidMessage = "El subdominio no es válido. Use solo letras minúsculas, números y guiones (por ejemplo depot).";
    public const string DomainTakenMessage = "Ese dominio ya lo usa otra compañía.";
    public const string SubdomainTakenMessage = "Ese subdominio ya lo usa otra compañía.";

    private static readonly Regex Label = new("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>Quita esquema, ruta, puerto y punto final; minúsculas. Devuelve null si no queda nada o no es un nombre de equipo válido (con al menos un punto).</summary>
    public static string? NormalizeDomain(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return string.Empty;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var end = s.IndexOfAny(new[] { '/', '?', '#' });
        if (end >= 0) s = s[..end];
        var colon = s.LastIndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.TrimEnd('.');
        if (s.Length == 0 || s.Length > MaxDomainLength) return null;
        var labels = s.Split('.');
        return labels.Length >= 2 && labels.All(l => Label.IsMatch(l)) ? s : null;
    }

    /// <summary>Minúsculas y sin espacios. Devuelve null si no es una etiqueta DNS válida; la cadena vacía = sin subdominio.</summary>
    public static string? NormalizeSubdomain(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return string.Empty;
        return Label.IsMatch(s) ? s : null;
    }

    /// <summary>
    /// Qué buscar para la dirección con la que se abrió la página: el nombre completo (sin «www.») y, si tiene al menos tres partes, su primera
    /// parte como subdominio (`depot.teikem.com` → `depot`). Una dirección sin punto (localhost) o una IP no busca nada.
    /// </summary>
    public static (string? Domain, string? Subdomain) Candidates(string? host)
    {
        var h = NormalizeDomain(host);
        if (string.IsNullOrEmpty(h) || h.All(c => char.IsDigit(c) || c == '.')) return (null, null);
        if (h.StartsWith("www.", StringComparison.Ordinal) && h.Count(c => c == '.') >= 2) h = h[4..];
        var labels = h.Split('.');
        return (h, labels.Length >= 3 ? labels[0] : null);
    }
}
