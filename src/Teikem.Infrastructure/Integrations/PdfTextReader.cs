using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Services;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace Teikem.Infrastructure.Integrations;

/// <summary>Lo leído de un PDF: el texto (renglones, columnas separadas por tabulador), cuántas páginas, si hizo falta OCR y, si no se pudo leer, el motivo (mensaje para el usuario).</summary>
public sealed record PdfReadResult(string Text, int Pages, bool UsedOcr, string? Problem)
{
    public static PdfReadResult Fail(string problem) => new(string.Empty, 0, false, problem);
}

/// <summary>PDF → texto. Primero el texto real del PDF; si el PDF es una imagen (escaneado) cada página se dibuja y pasa por OCR.</summary>
public interface IPdfTextReader
{
    PdfReadResult Read(byte[] pdf);
}

/// <summary>
/// Lectura de PDF de las integraciones por correo (2026-10-22). El contenido del PDF es entrada NO confiable: solo se lee texto y se dibuja la página; nunca se ejecuta nada (ni JavaScript ni acciones del documento).
/// PdfPig extrae las palabras con su posición; las páginas sin texto se dibujan (PdfPig.Rendering.Skia) y las lee Tesseract (datos de idioma en <c>Integrations:OcrTessdataPath</c>).
/// </summary>
public sealed class PdfTextReader(IOptions<IntegrationOptions> options, ILogger<PdfTextReader> logger) : IPdfTextReader
{
    public const string NotPdfMessage = "El archivo no es un PDF válido o está dañado.";
    public const string EncryptedMessage = "El PDF está protegido con contraseña: no se puede leer.";
    public const string NoOcrMessage = "El PDF es una imagen (escaneado) y la lectura por OCR no está configurada: falta la carpeta de idiomas de Tesseract (Integrations:OcrTessdataPath con spa.traineddata y eng.traineddata).";
    public static string TooManyPagesMessage(int pages, int max) => $"El PDF trae {pages} páginas; solo se leen las primeras {max}.";

    public PdfReadResult Read(byte[] pdf)
    {
        var opt = options.Value;
        try
        {
            using var document = PdfDocument.Open(pdf);
            var pages = Math.Min(document.NumberOfPages, Math.Max(1, opt.MaxPdfPages));
            var texts = new List<string>();
            for (var n = 1; n <= pages; n++)
            {
                var words = document.GetPage(n).GetWords()
                    .Select(w => new PdfWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.BoundingBox.Top)).ToList();
                texts.Add(PdfTextRules.PageToText(words));
            }
            // la decisión es POR PÁGINA: un PDF mixto (unas páginas con texto y otras escaneadas) lee cada una a su manera
            var scanned = Enumerable.Range(0, pages).Where(i => PdfTextRules.NeedsOcr(1, PdfTextRules.MeaningfulChars(texts[i]))).ToList();
            if (scanned.Count == 0)
                return new PdfReadResult(PdfTextRules.JoinPages(texts), pages, false, null);

            // páginas sin texto real: son escaneados
            if (string.IsNullOrWhiteSpace(opt.OcrTessdataPath) || !Directory.Exists(opt.OcrTessdataPath)) return new PdfReadResult(PdfTextRules.JoinPages(texts), pages, false, NoOcrMessage);
            document.AddSkiaPageFactory();
            using var engine = new TesseractEngine(opt.OcrTessdataPath, string.IsNullOrWhiteSpace(opt.OcrLanguages) ? "spa+eng" : opt.OcrLanguages, EngineMode.Default);
            foreach (var i in scanned)
            {
                using var image = document.GetPageAsPng(i + 1, opt.OcrScale > 0 ? opt.OcrScale : 4f);
                texts[i] = OcrPage(engine, image.ToArray());
            }
            return new PdfReadResult(PdfTextRules.JoinPages(texts), pages, true, null);
        }
        catch (UglyToad.PdfPig.Core.PdfDocumentFormatException ex) { logger.LogWarning(ex, "PDF ilegible."); return PdfReadResult.Fail(NotPdfMessage); }
        catch (UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException) { return PdfReadResult.Fail(EncryptedMessage); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or TesseractException or NotSupportedException)
        {
            logger.LogWarning(ex, "No se pudo leer el PDF.");
            return PdfReadResult.Fail($"{NotPdfMessage} ({ex.Message})");
        }
    }

    /// <summary>
    /// Una página escaneada → texto. Se lee con dos modos de página de Tesseract (automático y «un solo bloque») y se queda con el que reconoce más contenido: en los conduces con rayas el automático
    /// perdía columnas enteras (facturas, guiones de la orden) que el modo de bloque conservaba, y en los formularios con varias zonas ocurre lo contrario.
    /// </summary>
    private static string OcrPage(TesseractEngine engine, byte[] png)
    {
        var best = string.Empty;
        var bestChars = -1;
        foreach (var mode in new[] { PageSegMode.Auto, PageSegMode.SingleBlock })
        {
            var text = PdfTextRules.PageToText(OcrWords(engine, png, mode));
            var chars = PdfTextRules.MeaningfulChars(text);
            if (chars > bestChars) { best = text; bestChars = chars; }
        }
        return best;
    }

    /// <summary>Palabras de una página (imagen PNG) con su caja, pasadas al origen abajo-izquierda como el PDF.</summary>
    private static List<PdfWord> OcrWords(TesseractEngine engine, byte[] png, PageSegMode mode)
    {
        var result = new List<PdfWord>();
        using var pix = Pix.LoadFromMemory(png);
        using var page = engine.Process(pix, mode);
        using var it = page.GetIterator();
        it.Begin();
        var height = pix.Height;
        do
        {
            var text = it.GetText(PageIteratorLevel.Word);
            if (string.IsNullOrWhiteSpace(text) || !it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;
            result.Add(new PdfWord(text.Trim(), r.X1, r.X2, height - r.Y2, height - r.Y1));
        } while (it.Next(PageIteratorLevel.Word));
        return result;
    }
}
