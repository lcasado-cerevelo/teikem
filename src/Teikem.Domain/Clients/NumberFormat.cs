using System.Text;

namespace Teikem.Domain.Clients;

/// <summary>
/// Patrones de numeración por cliente (órdenes, facturas, paquetes). Lógica pura, sin BD:
/// cada '#' es un dígito del consecutivo (relleno con ceros a la izquierda; si el número no cabe NO se trunca),
/// cada '@' se sustituye por la letra de serie y el resto del patrón se copia tal cual.
/// El contador atómico (siguiente consecutivo por cliente) lo agrega el lote de Órdenes.
/// </summary>
public static class NumberFormat
{
    public const int MaxLength = 40;
    public const char DigitPlaceholder = '#';
    public const char LetterPlaceholder = '@';
    private const string AllowedSymbols = "-_/.#@";

    /// <summary>Patrones por defecto del sistema (NULL en la ficha del cliente = usar estos).</summary>
    public static class Defaults
    {
        public const string Order = "ORD-#####";
        public const string Invoice = "FAC-#####";
        public const string Package = "PQT-#####";
    }

    /// <summary>
    /// Valida un patrón: 1..40 caracteres, al menos un '#', solo letras, dígitos y '-', '_', '/', '.', '#', '@'.
    /// Devuelve el mensaje de error (en español) o null si es válido.
    /// </summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "El patrón es obligatorio.";
        if (pattern.Length > MaxLength) return $"El patrón no puede exceder {MaxLength} caracteres.";
        if (!pattern.Contains(DigitPlaceholder)) return "El patrón debe incluir al menos un '#' para el consecutivo.";
        foreach (var ch in pattern)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) continue;
            if (AllowedSymbols.Contains(ch)) continue;
            return $"Carácter no permitido en el patrón: '{ch}'. Use letras, dígitos y - _ / . # @.";
        }
        return null;
    }

    public static bool IsValid(string? pattern) => Validate(pattern) is null;

    /// <summary>
    /// Resuelve el patrón para el consecutivo n: 'AX-#####', 1 → 'AX-00001'. Si n tiene más dígitos que '#',
    /// los sobrantes se anteponen al primer '#' (nunca se trunca). '@' → letra de serie.
    /// </summary>
    public static string Resolve(string pattern, long n, char letter = 'A')
    {
        if (pattern is null) throw new ArgumentNullException(nameof(pattern));
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "El consecutivo no puede ser negativo.");
        var slots = pattern.Count(c => c == DigitPlaceholder);
        var digits = slots == 0 ? string.Empty : n.ToString().PadLeft(slots, '0');
        var sb = new StringBuilder(pattern.Length + digits.Length);
        var next = 0;
        var overflow = digits.Length - slots; // dígitos que no caben: van antes del primer '#'
        foreach (var ch in pattern)
        {
            if (ch == DigitPlaceholder)
            {
                if (overflow > 0) { sb.Append(digits, 0, overflow); next = overflow; overflow = 0; }
                sb.Append(digits[next++]);
            }
            else if (ch == LetterPlaceholder) sb.Append(letter);
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
