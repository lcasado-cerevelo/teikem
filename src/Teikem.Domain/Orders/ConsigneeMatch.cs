using System.Globalization;
using System.Text;

namespace Teikem.Domain.Orders;

/// <summary>
/// Coincidencia de un consignatario escrito libre contra el directorio del cliente (Lote 3, injerto L244/L720):
/// la clave es nombre + línea 1 normalizados (Trim, espacios colapsados, minúsculas, sin diacríticos), la misma normalización
/// que SpecialServiceRules.NormalizeName pero sin tope de longitud (la línea 1 admite 200 caracteres). Nunca se compara solo
/// por nombre: dos sucursales con el mismo nombre y distinta dirección son consignatarios distintos. Lógica pura, sin EF.
/// </summary>
public static class ConsigneeMatch
{
    /// <summary>Clave 'nombre|linea1' normalizada, o null si cualquiera de las dos partes está vacía.</summary>
    public static string? Key(string? name, string? line1)
    {
        var n = Normalize(name);
        var l = Normalize(line1);
        if (n is null || l is null) return null;
        return n + "|" + l;
    }

    /// <summary>LocationId de la primera candidata cuya clave coincide exactamente con la de (name, line1), o null.</summary>
    public static int? FindMatch(IEnumerable<(int LocationId, string Name, string Line1)> candidates, string name, string line1)
    {
        var key = Key(name, line1);
        if (key is null) return null;
        foreach (var c in candidates)
        {
            if (string.Equals(Key(c.Name, c.Line1), key, StringComparison.Ordinal)) return c.LocationId;
        }
        return null;
    }

    /// <summary>Trim, colapsa espacios interiores, minúsculas y sin diacríticos; null si queda vacío.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // tilde, diéresis...
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            sb.Append(char.ToLowerInvariant(ch));
            lastSpace = false;
        }
        var result = sb.ToString().Normalize(NormalizationForm.FormC);
        return result.Length == 0 ? null : result;
    }
}
