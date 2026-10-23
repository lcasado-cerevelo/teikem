using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Services;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-22 (dictado de Luis) — PDF adjuntos de las integraciones por correo: el PDF se convierte en texto (renglones, columnas separadas por tabulador) y se lee con los mismos campos a leer del cuerpo;
/// si es un escaneado se pasa por OCR (Tesseract).
/// </summary>
public class PdfReadingTests
{
    private static PdfWord W(string text, double left, double bottom, double width = 40, double height = 10) => new(text, left, left + width, bottom, bottom + height);

    [Fact]
    public void Words_become_lines_top_to_bottom_with_a_tab_between_columns()
    {
        var text = PdfTextRules.PageToText(new[]
        {
            W("Order", 50, 700, 30), W("Summary", 85, 700, 40),
            W("SKU", 50, 650, 25), W("Qty", 200, 650, 20), W("Price", 320, 650, 30),
            W("A-100", 50, 630, 35), W("2", 200, 630, 5), W("$10.00", 320, 630, 35),
        });
        Assert.Equal("Order Summary\nSKU\tQty\tPrice\nA-100\t2\t$10.00", text);
    }

    [Fact]
    public void Words_slightly_off_the_baseline_stay_on_the_same_line()
        => Assert.Equal("Reference # 123", PdfTextRules.PageToText(new[] { W("Reference", 50, 700, 45), W("#", 100, 702, 5), W("123", 110, 699, 15) }));

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 24, true)]
    [InlineData(1, 25, false)]
    [InlineData(3, 60, true)]
    [InlineData(3, 75, false)]
    [InlineData(0, 0, false)]
    public void A_pdf_without_real_text_needs_ocr(int pages, int chars, bool needsOcr) => Assert.Equal(needsOcr, PdfTextRules.NeedsOcr(pages, chars));

    [Fact]
    public void Only_letters_and_digits_count_as_text() => Assert.Equal(5, PdfTextRules.MeaningfulChars(" a-b\t1 2\n 3 . "));

    private static byte[] BuildPdf(params (string Text, double X, double Y)[] items)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(612, 792);
        foreach (var (text, x, y) in items) page.AddText(text, 11, new PdfPoint(x, y), font);
        return builder.Build();
    }

    private static PdfTextReader Reader() => new(Options.Create(new IntegrationOptions()), NullLogger<PdfTextReader>.Instance);

    [Fact]
    public void A_pdf_with_real_text_is_read_with_its_table_columns_and_feeds_the_body_fields()
    {
        var pdf = BuildPdf(
            ("Reference #: PO-7788", 50, 720),
            ("Order Summary", 50, 690),
            ("SKU", 50, 660), ("Qty", 250, 660), ("Description", 400, 660),
            ("A-100", 50, 640), ("2", 250, 640), ("Boxes", 400, 640),
            ("B-200", 50, 620), ("5", 250, 620), ("Pallets", 400, 620),
            ("Total", 50, 590));
        var read = Reader().Read(pdf);

        Assert.Null(read.Problem);
        Assert.False(read.UsedOcr);
        Assert.Equal(1, read.Pages);
        Assert.Contains("Reference #: PO-7788", read.Text);
        Assert.Contains("A-100\t2\tBoxes", read.Text);

        var fields = new[]
        {
            new EmailBodyField(1, EmailBodyKinds.Label, Label: "Reference #", Required: true),
            new EmailBodyField(2, EmailBodyKinds.Block, Start: "SKU", End: "Total", Separator: "\t", Columns: new[] { new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 2) }),
        };
        var reading = EmailRules.ReadBody(read.Text, fields);
        Assert.Empty(reading.Missing);
        Assert.Equal(2, reading.Lines.Count);
        Assert.Equal("PO-7788", reading.Single[1]);
    }

    /// <summary>Carpeta de los idiomas del OCR en esta máquina (variable TEIKEM_TESSDATA o la de la migración); sin ella la prueba de OCR no corre.</summary>
    private static string? TessdataFolder()
    {
        var dir = Environment.GetEnvironmentVariable("TEIKEM_TESSDATA") ?? "F:/Download/TeikemMigracion/tessdata";
        return File.Exists(Path.Combine(dir, "eng.traineddata")) && File.Exists(Path.Combine(dir, "spa.traineddata")) ? dir : null;
    }

    /// <summary>PDF «escaneado»: una sola imagen (PNG) con el texto dibujado y ninguna capa de texto.</summary>
    private static byte[] BuildScannedPdf(params (string Text, float X, float Y)[] items)
    {
        using var bitmap = new SKBitmap(1700, 2200);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, TextSize = 38, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("Arial") };
            foreach (var (text, x, y) in items) canvas.DrawText(text, x, y, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(612, 792);
        page.AddPng(png, new PdfRectangle(0, 0, 612, 792));
        return builder.Build();
    }

    [Fact]
    public void A_scanned_pdf_is_read_with_ocr_and_feeds_the_body_fields()
    {
        var dir = TessdataFolder();
        if (dir is null) return;   // sin los idiomas de Tesseract en esta máquina no se puede probar el OCR
        var pdf = BuildScannedPdf(
            ("Reference #: PO-7788", 150, 200),
            ("Order Summary", 150, 300),
            ("SKU", 150, 400), ("Qty", 750, 400), ("Description", 1150, 400),
            ("A-100", 150, 470), ("2", 750, 470), ("Boxes", 1150, 470),
            ("B-200", 150, 540), ("5", 750, 540), ("Pallets", 1150, 540),
            ("Total", 150, 640));
        var reader = new PdfTextReader(Options.Create(new IntegrationOptions { OcrTessdataPath = dir }), NullLogger<PdfTextReader>.Instance);
        var read = reader.Read(pdf);

        Assert.Null(read.Problem);
        Assert.True(read.UsedOcr);
        Assert.Contains("PO-7788", read.Text);

        var fields = new[]
        {
            new EmailBodyField(1, EmailBodyKinds.Label, Label: "Reference #", Required: true),
            new EmailBodyField(2, EmailBodyKinds.Block, Start: "SKU", End: "Total", Separator: "	", Columns: new[] { new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 2) }),
        };
        var reading = EmailRules.ReadBody(read.Text, fields);
        Assert.Empty(reading.Missing);
        Assert.Equal("PO-7788", reading.Single[1]);
        Assert.Equal(2, reading.Lines.Count);
        Assert.Equal("A-100", reading.Lines[0][2]);
        Assert.Equal("5", reading.Lines[1][3]);   // columna 2 del renglón (Qty) → posición 3
    }

    [Fact]
    public void Something_that_is_not_a_pdf_is_reported_not_thrown()
    {
        var read = Reader().Read(System.Text.Encoding.UTF8.GetBytes("esto no es un pdf"));
        Assert.NotNull(read.Problem);
        Assert.Contains("PDF", read.Problem);
    }

    [Fact]
    public void A_scanned_pdf_without_the_ocr_data_says_so()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(612, 792);   // página sin texto = como un escaneado sin capa de texto
        var read = Reader().Read(builder.Build());
        Assert.Equal(PdfTextReader.NoOcrMessage, read.Problem);
        Assert.False(read.UsedOcr);
    }

    [Fact]
    public void Reading_pdf_adds_the_pdf_extension_to_the_attachments_taken()
    {
        var atts = new[] { new MailAttachmentData("orden.pdf", "application/pdf", new byte[] { 1 }), new MailAttachmentData("datos.csv", "text/csv", new byte[] { 1 }), new MailAttachmentData("foto.png", "image/png", new byte[] { 1 }) };
        var plain = new EmailConfig("h", 993, true, "u");
        Assert.Equal(new[] { "datos.csv" }, EmailRules.SelectAttachments(atts, plain).Select(a => a.Name));
        var withPdf = plain with { ReadPdf = true };
        Assert.Equal(new[] { "orden.pdf", "datos.csv" }, EmailRules.SelectAttachments(atts, withPdf).Select(a => a.Name));
    }

    [Fact]
    public void Reading_pdf_requires_fields_like_reading_the_body()
    {
        var cfg = new EmailConfig("h", 993, true, "u", ReadPdf: true);
        Assert.Equal(EmailRules.NoBodyFieldsMessage, EmailRules.Validate(cfg, hasPassword: true)["email.bodyFields"]);
        var ok = cfg with { BodyFields = new[] { new EmailBodyField(1, EmailBodyKinds.Label, Label: "PO") } };
        Assert.DoesNotContain("email.bodyFields", EmailRules.Validate(ok, hasPassword: true).Keys);
    }
}
