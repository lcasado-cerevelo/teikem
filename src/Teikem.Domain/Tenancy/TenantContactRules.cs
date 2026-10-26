namespace Teikem.Domain.Tenancy;

/// <summary>
/// 2026-10-26 — teléfono y direcciones de la compañía (para cartas y documentos). El teléfono se guarda tal como se escribe (sin reformatear) pero debe tener entre 7 y 15 dígitos
/// (E.164 llega a 15) y solo puede llevar dígitos, espacios, guiones, puntos, paréntesis, «+» y «ext»/«x» para la extensión. El domicilio y la dirección postal son filas de dbo.Location
/// (tipos COMPANY y COMPANY_POSTAL); aquí van sus límites y mensajes, los mismos de las demás localizaciones.
/// </summary>
public static class TenantContactRules
{
    public const int PhoneMax = 30;
    public const int LineMax = 200, CityMax = 100, StateMax = 100, PostalMax = 20;
    public const string DefaultCountry = "PR";

    public const string PhoneRequiredMessage = "El teléfono de la compañía es obligatorio.";
    public const string PhoneInvalidMessage = "El teléfono debe tener entre 7 y 15 dígitos (puede llevar espacios, guiones, paréntesis, «+» y «ext»).";
    public const string Line1RequiredMessage = "La dirección (línea 1) es obligatoria.";
    public const string CityRequiredMessage = "La ciudad es obligatoria.";
    public const string NameTakenMessage = "Ya existe una compañía con ese nombre.";
    public const string NameOnlyPlatformMessage = "Solo el administrador de la plataforma cambia el nombre de la compañía.";
    public static string MaxMessage(int max) => $"No puede pasar de {max} caracteres.";

    /// <summary>Teléfono normalizado (recortado) o el mensaje de error. Vacío = sin teléfono (null) cuando <paramref name="required"/> es false.</summary>
    public static (string? Phone, string? Error) NormalizePhone(string? raw, bool required)
    {
        var s = raw?.Trim() ?? string.Empty;
        if (s.Length == 0) return required ? (null, PhoneRequiredMessage) : (null, null);
        if (s.Length > PhoneMax) return (null, PhoneInvalidMessage);
        var digits = 0;
        foreach (var ch in s)
        {
            if (char.IsAsciiDigit(ch)) digits++;
            else if (!(ch is ' ' or '-' or '.' or '(' or ')' or '+' or 'x' or 'X' or 'e' or 'E' or 't' or 'T')) return (null, PhoneInvalidMessage);
        }
        return digits is >= 7 and <= 15 ? (s, null) : (null, PhoneInvalidMessage);
    }

    /// <summary>Primer error de una dirección de la compañía, o null si es válida. Devuelve (campo, mensaje).</summary>
    public static (string Field, string Message)? ValidateAddress(string? line1, string? line2, string? city, string? state, string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(line1)) return ("line1", Line1RequiredMessage);
        if (line1.Trim().Length > LineMax) return ("line1", MaxMessage(LineMax));
        if ((line2?.Trim().Length ?? 0) > LineMax) return ("line2", MaxMessage(LineMax));
        if (string.IsNullOrWhiteSpace(city)) return ("city", CityRequiredMessage);
        if (city.Trim().Length > CityMax) return ("city", MaxMessage(CityMax));
        if ((state?.Trim().Length ?? 0) > StateMax) return ("state", MaxMessage(StateMax));
        if ((postalCode?.Trim().Length ?? 0) > PostalMax) return ("postalCode", MaxMessage(PostalMax));
        return null;
    }
}
