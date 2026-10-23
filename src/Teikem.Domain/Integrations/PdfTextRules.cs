using System.Text;

namespace Teikem.Domain.Integrations;

/// <summary>Una palabra de una página de PDF con su caja (coordenadas con el origen ABAJO a la izquierda, como el PDF; el OCR las convierte antes).</summary>
public sealed record PdfWord(string Text, double Left, double Right, double Bottom, double Top, double Confidence = 100);

/// <summary>La confianza con que el OCR leyó un documento (0–100): Mean = el promedio de todas sus palabras; Numeric = el de las palabras que traen dígitos (números de orden, SKU, cantidades), que son las que importan; Words = cuántas palabras leyó.</summary>
public sealed record OcrConfidence(int Mean, int Numeric, int Words);

/// <summary>
/// Lectura DUDOSA (2026-10-23): la confianza del OCR de los números del documento queda por debajo del mínimo del lector (80 por omisión; 0 = no se vigila). OJO: es solo UNA señal. Medido con los conduces de Axiscare, la confianza NO
/// detecta lo que el OCR dejó de leer (una columna perdida entera no la baja: una lectura peor tuvo 86 % y una completa 82 %), así que no sustituye a las validaciones de formato y de totales; sirve para avisar de lo leído con dudas.
/// </summary>
public static class ReadConfidenceRules
{
    public const int DefaultMin = 80;
    public static string MinInvalidMessage => "La confianza mínima debe estar entre 0 y 100.";
    public static string DoubtfulMessage(int confidence, int min) => $"Lectura dudosa ({confidence} % < {min} %): verifique lo creado contra el documento.";

    public static OcrConfidence? Summarize(IEnumerable<PdfWord> words)
    {
        var all = words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (all.Count == 0) return null;
        var numbers = all.Where(w => w.Text.Any(char.IsDigit)).ToList();
        var mean = (int)Math.Round(all.Average(w => w.Confidence));
        return new OcrConfidence(mean, numbers.Count == 0 ? mean : (int)Math.Round(numbers.Average(w => w.Confidence)), all.Count);
    }

    /// <summary>Une las confianzas de varias páginas escaneadas, ponderadas por sus palabras.</summary>
    public static OcrConfidence? Combine(IEnumerable<OcrConfidence?> parts)
    {
        var list = parts.Where(p => p is not null).Select(p => p!).ToList();
        if (list.Count == 0) return null;
        var words = list.Sum(p => p.Words);
        if (words == 0) return null;
        return new OcrConfidence((int)Math.Round(list.Sum(p => (double)p.Mean * p.Words) / words), (int)Math.Round(list.Sum(p => (double)p.Numeric * p.Words) / words), words);
    }

    public static int MinOf(int? configured) => configured ?? DefaultMin;

    /// <summary>La cifra que se compara: la de los números del documento.</summary>
    public static bool IsDoubtful(OcrConfidence? confidence, int? configuredMin)
    {
        var min = MinOf(configuredMin);
        return confidence is not null && min > 0 && confidence.Numeric < min;
    }
}

/// <summary>
/// Lectura de PDF en las integraciones por correo (2026-10-22, dictado de Luis): el PDF se convierte en TEXTO con renglones y, entre columnas separadas, un TABULADOR; ese texto se lee con los mismos
/// «campos a leer» del cuerpo del correo (etiqueta, expresión regular o bloque de renglones). Con texto real se usa el texto del PDF; si el PDF es una imagen (escaneado) se pasa por OCR y sale el mismo formato.
/// Reglas puras: armar los renglones a partir de las palabras y decidir si hace falta OCR.
/// </summary>
public static class PdfTextRules
{
    /// <summary>Menos de estos caracteres con contenido por página = el PDF no trae texto real (es una imagen) y hay que pasarlo por OCR.</summary>
    public const int MinCharsPerPage = 25;

    public static bool NeedsOcr(int pages, int meaningfulChars) => pages > 0 && meaningfulChars < pages * MinCharsPerPage;

    public static int MeaningfulChars(string? text) => text is null ? 0 : text.Count(char.IsLetterOrDigit);

    /// <summary>
    /// Palabras de una página → texto: de arriba abajo, un renglón por línea visual; dentro del renglón, de izquierda a derecha, con un espacio entre palabras juntas y un tabulador cuando hay un hueco de columna
    /// (más ancho que ~1,2 caracteres). Así una tabla del PDF sale como renglones con las columnas separadas por tabulador.
    /// </summary>
    public static string PageToText(IReadOnlyList<PdfWord> words)
    {
        var usable = words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (usable.Count == 0) return string.Empty;
        var lines = new List<List<PdfWord>>();
        var lineCenters = new List<double>();
        foreach (var w in usable.OrderByDescending(w => (w.Top + w.Bottom) / 2).ThenBy(w => w.Left))
        {
            var center = (w.Top + w.Bottom) / 2;
            var height = Math.Max(0.01, w.Top - w.Bottom);
            var idx = lines.Count - 1;
            if (idx >= 0 && Math.Abs(center - lineCenters[idx]) <= height * 0.55) { lines[idx].Add(w); lineCenters[idx] = (lineCenters[idx] * (lines[idx].Count - 1) + center) / lines[idx].Count; }
            else { lines.Add(new List<PdfWord> { w }); lineCenters.Add(center); }
        }
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(w => w.Left).ToList();
            var charWidth = Median(ordered.Where(w => w.Text.Length > 0).Select(w => Math.Max(0.01, (w.Right - w.Left) / w.Text.Length)).ToList());
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    var gap = ordered[i].Left - ordered[i - 1].Right;
                    sb.Append(gap > charWidth * 1.2 ? '\t' : ' ');
                }
                sb.Append(ordered[i].Text.Trim());
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Texto de todo el documento: las páginas una tras otra, separadas por un renglón en blanco.</summary>
    public static string JoinPages(IEnumerable<string> pages) => string.Join("\n\n", pages.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 5;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
