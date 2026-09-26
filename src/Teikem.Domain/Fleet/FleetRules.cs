using System.Globalization;
using System.Text;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Reglas puras compartidas por las piezas de Flota (Lote 4): normalización de códigos, búsqueda libre sin acentos,
/// tope efectivo de paradas, fechas y estado de vencimiento de documentos, precisión DECIMAL y odómetro monotónico.
/// </summary>
public static class FleetRules
{
    /// <summary>Ventana por defecto de 'por vencer' (días).</summary>
    public const int DefaultExpiringWithinDays = 30;

    public const string DocumentDatesMessage = "La fecha de vencimiento no puede ser anterior a la de emisión.";

    public static string CodeTooLongMessage(int maxLen) => $"El código no puede exceder {maxLen} caracteres.";

    /// <summary>Recorta y pasa a mayúsculas. Vacío → requiredMessage; más largo que maxLen → 'El código no puede exceder {n} caracteres.'</summary>
    public static (string? Code, string? Error) NormalizeCode(string? raw, int maxLen, string requiredMessage)
    {
        var code = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return (null, requiredMessage);
        if (code.Length > maxLen) return (null, CodeTooLongMessage(maxLen));
        return (code, null);
    }

    /// <summary>
    /// Búsqueda libre (qbox) sin distinguir acentos ni mayúsculas: cada palabra de la consulta debe aparecer en alguno de
    /// los campos. Consulta vacía = todo coincide.
    /// </summary>
    public static bool MatchesSearch(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var haystack = (fields ?? Array.Empty<string?>()).Where(f => !string.IsNullOrEmpty(f)).Select(f => Fold(f!)).ToList();
        if (haystack.Count == 0) return false;
        var tokens = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.All(t => haystack.Any(h => h.Contains(t, StringComparison.Ordinal)));
    }

    /// <summary>Texto en minúsculas invariantes y sin marcas diacríticas ('Diésel' → 'diesel').</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Tope de paradas efectivo del chofer (R4): el propio o, si no tiene, el default del tenant.</summary>
    public static int? EffectiveMaxStops(int? own, int? tenantDefault) => own ?? tenantDefault;

    /// <summary>El vencimiento no puede ser anterior a la emisión (CK_*_Dates).</summary>
    public static string? ValidateDocumentDates(DateOnly? issued, DateOnly? expiry)
        => issued is DateOnly i && expiry is DateOnly e && e < i ? DocumentDatesMessage : null;

    /// <summary>
    /// Estado de vencimiento en <paramref name="today"/>: sin fecha NO_EXPIRY; ya pasó EXPIRED; vence hoy o dentro de
    /// <paramref name="withinDays"/> días EXPIRING; después OK.
    /// </summary>
    public static string ExpiryState(DateOnly? expiry, DateOnly today, int withinDays = DefaultExpiringWithinDays)
    {
        if (expiry is not DateOnly e) return ExpiryStates.NoExpiry;
        var days = DaysTo(e, today);
        if (days < 0) return ExpiryStates.Expired;
        return days <= withinDays ? ExpiryStates.Expiring : ExpiryStates.Ok;
    }

    /// <summary>Días que faltan para el vencimiento (negativo si ya venció).</summary>
    public static int DaysTo(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    /// <summary>
    /// Validación previa de una columna DECIMAL(precision, scale): null si cabe; si no, 'El valor admite como máximo
    /// {scale} decimales y debe ser menor que {10^(precision-scale)}.' (miles con coma, cultura invariante). Se aplica antes
    /// de guardar para que un desbordamiento nunca termine en 500.
    /// </summary>
    public static string? DecimalError(decimal? value, int precision, int scale)
    {
        if (value is not decimal v) return null;
        if (precision <= 0 || scale < 0 || scale > precision) throw new ArgumentOutOfRangeException(nameof(scale));
        var integerDigits = precision - scale;
        var limit = Pow10(integerDigits);
        var tooManyDecimals = decimal.Round(v, scale, MidpointRounding.ToZero) != v;
        var tooLarge = limit is decimal l && Math.Abs(v) >= l;
        if (!tooManyDecimals && !tooLarge) return null;
        var limitText = limit is decimal lt
            ? lt.ToString("N0", CultureInfo.InvariantCulture)
            : "1" + new string('0', integerDigits);
        return $"El valor admite como máximo {scale} decimales y debe ser menor que {limitText}.";
    }

    /// <summary>Odómetro monotónico: el mayor entre el actual y la lectura (una lectura nula no cambia nada).</summary>
    public static decimal? RaiseOdometer(decimal? current, decimal? reading)
    {
        if (reading is not decimal r) return current;
        if (current is not decimal c) return r;
        return Math.Max(c, r);
    }

    private static decimal? Pow10(int exponent)
    {
        if (exponent > 28) return null; // fuera del rango de System.Decimal: cualquier decimal cabe
        decimal result = 1m;
        for (var i = 0; i < exponent; i++) result *= 10m;
        return result;
    }
}
