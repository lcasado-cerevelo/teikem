using System.Globalization;
using System.Text;

namespace Teikem.Domain.Clients;

/// <summary>
/// Código de cliente autogenerado desde el nombre (el modal '+ Nuevo cliente' solo captura el nombre):
/// mayúsculas, sin acentos ni diacríticos, todo lo no alfanumérico se vuelve '-', guiones colapsados y sin guion
/// en los extremos, máximo 20 caracteres. Si choca con uno existente, el servicio le agrega el sufijo -2, -3…
/// Lógica pura: un nombre sin letras ni dígitos lanza ArgumentException (el servicio la traduce a 400).
/// </summary>
public static class ClientCode
{
    public const int MaxLength = 20;

    public static string FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre del cliente es obligatorio para generar el código.", nameof(name));
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastDash = true; // evita guion inicial
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // tilde, diéresis...
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToUpperInvariant(ch));
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        var code = sb.ToString().TrimEnd('-');
        if (code.Length > MaxLength) code = code[..MaxLength].TrimEnd('-');
        if (code.Length == 0) throw new ArgumentException("El nombre del cliente no contiene letras ni dígitos para generar el código.", nameof(name));
        return code;
    }

    /// <summary>Sufijo de desempate: 'ACME' + 2 → 'ACME-2' (recorta la base si hace falta para no exceder el máximo).</summary>
    public static string WithSuffix(string baseCode, int n)
    {
        var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
        var room = MaxLength - suffix.Length;
        var head = baseCode.Length > room ? baseCode[..room].TrimEnd('-') : baseCode;
        return head + suffix;
    }
}
