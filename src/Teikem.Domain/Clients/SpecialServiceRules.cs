using System.Globalization;
using System.Text;

namespace Teikem.Domain.Clients;

/// <summary>
/// Reglas puras de los tipos de servicio especial (Lote 2, P5). Los tipos viven en una tabla propia por tenant
/// (SpecialServiceType, UNIQUE TenantId+Name) y se comparan normalizados para no duplicar variantes del mismo nombre
/// ('Vagón del muelle' vs 'VAGON DEL MUELLE'). Sin EF ni servicios: se prueba con xunit.
/// Lógica pura: un nombre vacío o mayor al máximo lanza ArgumentException (el servicio la traduce a 400), igual que ClientCode.
/// </summary>
public static class SpecialServiceRules
{
    public const int NameMaxLength = 120;

    /// <summary>
    /// Nombre tal como se guarda y se muestra: recortado y con espacios interiores simples, conservando mayúsculas y acentos.
    /// Vacío (o solo espacios) o mayor a 120 caracteres → ArgumentException.
    /// </summary>
    public static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del tipo de servicio especial es obligatorio.", nameof(name));
        var clean = CollapseWhitespace(name.Trim());
        if (clean.Length > NameMaxLength)
            throw new ArgumentException($"El nombre del tipo de servicio especial no puede exceder {NameMaxLength} caracteres.", nameof(name));
        return clean;
    }

    /// <summary>
    /// Clave de comparación: recorta, colapsa espacios, pasa a minúsculas y quita diacríticos
    /// ('  Vagón   del Muelle ' → 'vagon del muelle'). Vacío o mayor a 120 caracteres → ArgumentException.
    /// </summary>
    public static string NormalizeName(string? name)
    {
        var clean = CleanName(name);
        var decomposed = clean.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // tilde, diéresis...
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>¿Son el mismo tipo? Compara las claves normalizadas; un nombre inválido (vacío o demasiado largo) nunca coincide.</summary>
    public static bool SameName(string? a, string? b)
    {
        if (!TryNormalize(a, out var na) || !TryNormalize(b, out var nb)) return false;
        return string.Equals(na, nb, StringComparison.Ordinal);
    }

    private static bool TryNormalize(string? name, out string normalized)
    {
        try { normalized = NormalizeName(name); return true; }
        catch (ArgumentException) { normalized = ""; return false; }
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return sb.ToString();
    }
}
