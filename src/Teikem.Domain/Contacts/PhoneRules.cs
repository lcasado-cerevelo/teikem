namespace Teikem.Domain.Contacts;

/// <summary>Teléfono separado en prefijo de país (solo dígitos, sin «+») y número nacional (solo dígitos).</summary>
public readonly record struct PhoneParts(string CountryCallingCode, string National);

/// <summary>
/// Reglas del teléfono (2026-10-20, dictado de Luis): se guarda SOLO con dígitos y con su prefijo internacional en un campo aparte
/// (`ContactPoint.CountryCallingCode`, «1», «593»…); el formato con que se muestra lo pone la máscara de la compañía (`Tenant.PhoneMask`).
/// Lógica pura, sin EF. El prefijo se guarda siempre (el de la compañía si el número no trae otro): así el número queda completo aunque
/// después cambie el prefijo por omisión de la compañía.
/// </summary>
public static class PhoneRules
{
    public const string InvalidMessage = "Teléfono inválido.";
    public const string InvalidCountryMessage = "Prefijo de país inválido: use de 1 a 4 dígitos, por ejemplo 1.";
    public const string CountryMismatchMessage = "El teléfono no empieza con el prefijo de país indicado.";
    public const string UnknownCountryMessage = "El teléfono con «+» necesita el prefijo de país en su campo.";
    public const int MinNationalDigits = 7;
    public const int MaxTotalDigits = 15;
    public const int MaxCountryDigits = 4;

    /// <summary>Prefijos de dos dígitos (el resto de los que empiezan distinto de 1 y 7 son de tres). Sirve para separar «+593…» cuando no se dice el prefijo.</summary>
    private static readonly HashSet<string> TwoDigit = new()
    {
        "20", "27", "30", "31", "32", "33", "34", "36", "39", "40", "41", "43", "44", "45", "46", "47", "48", "49",
        "51", "52", "53", "54", "55", "56", "57", "58", "60", "61", "62", "63", "64", "65", "66", "81", "82", "84", "86",
        "90", "91", "92", "93", "94", "95", "98",
    };

    /// <summary>Solo los dígitos de un texto.</summary>
    public static string Digits(string? raw) => new((raw ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Prefijo de país en dígitos: acepta «+1», «1», «+593». Vacío → null. Fuera de 1–4 dígitos → FormatException.</summary>
    public static string? NormalizeCountry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.Any(c => !(char.IsAsciiDigit(c) || c == '+' || c == ' '))) throw new FormatException(InvalidCountryMessage);
        var d = Digits(t);
        if (d.Length is < 1 or > MaxCountryDigits || d[0] == '0') throw new FormatException(InvalidCountryMessage);
        return d;
    }

    /// <summary>
    /// Separa un teléfono escrito a mano. `explicitCountry` = el prefijo que se capturó aparte (puede ser null); `defaultCountry` = el de la compañía
    /// (dígitos). Con «+» el prefijo sale del que se indicó o, si no hay, se reconoce de los primeros dígitos. Sin «+»: el indicado o el de la compañía; y
    /// con prefijo «1» un número de 11 dígitos que empieza por 1 pierde ese 1. FormatException con el mensaje exacto si no calza.
    /// </summary>
    public static PhoneParts Parse(string? raw, string? explicitCountry, string defaultCountry)
    {
        var text = (raw ?? string.Empty).Trim();
        var digits = Digits(text);
        if (digits.Length == 0 || text.Any(c => !(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '.')) || text.LastIndexOf('+') > 0)
            throw new FormatException(InvalidMessage);
        var explicitCc = NormalizeCountry(explicitCountry);
        string cc, national;
        if (text.StartsWith('+'))
        {
            if (explicitCc is not null)
            {
                if (!digits.StartsWith(explicitCc, StringComparison.Ordinal)) throw new FormatException(CountryMismatchMessage);
                cc = explicitCc;
            }
            else cc = Detect(digits) ?? throw new FormatException(UnknownCountryMessage);
            national = digits[cc.Length..];
        }
        else
        {
            cc = explicitCc ?? NormalizeCountry(defaultCountry) ?? "1";
            national = digits;
            if (cc == "1" && digits.Length == 11 && digits[0] == '1') national = digits[1..];
        }
        if (national.Length < MinNationalDigits || cc.Length + national.Length > MaxTotalDigits) throw new FormatException(InvalidMessage);
        return new PhoneParts(cc, national);
    }

    /// <summary>
    /// Forma que viaja entre quien valida y quien guarda (importaciones): el teléfono sin formato — solo dígitos, o «+» y dígitos si traía «+» —
    /// ya comprobado. El prefijo definitivo lo decide después `Parse` con el de la compañía.
    /// </summary>
    public static string Clean(string? raw, string defaultCountry)
    {
        var text = (raw ?? string.Empty).Trim();
        _ = Parse(text, null, defaultCountry);
        return (text.StartsWith('+') ? "+" : string.Empty) + Digits(text);
    }

    /// <summary>E.164 para mandar un SMS o abrir WhatsApp: «+» + prefijo + número. El prefijo, si falta, es el de la compañía.</summary>
    public static string ToE164(string? countryCallingCode, string? national, string defaultCountry)
    {
        var cc = NormalizeCountry(countryCallingCode) ?? NormalizeCountry(defaultCountry) ?? "1";
        var n = Digits(national);
        if (cc == "1" && n.Length == 11 && n[0] == '1') n = n[1..];
        return "+" + cc + n;
    }

    /// <summary>Prefijo al inicio de unos dígitos que empiezan con «+» (ITU: los prefijos no se contienen unos a otros): 1 dígito si es 1 o 7, 2 si está en la lista, si no 3.</summary>
    private static string? Detect(string digits)
    {
        if (digits.Length == 0) return null;
        if (digits[0] is '1' or '7') return digits[..1];
        if (digits.Length >= 2 && TwoDigit.Contains(digits[..2])) return digits[..2];
        return digits.Length >= 3 ? digits[..3] : null;
    }
}
