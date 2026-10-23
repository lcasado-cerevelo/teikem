using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23: lectura DUDOSA. La confianza del OCR se resume por documento (promedio y la de las palabras con dígitos, que son los números de orden, SKU y cantidades); por debajo del mínimo del lector (80 por omisión,
/// 0 = no se vigila) la lectura es dudosa: se crea igual, se avisa que se verifique y un cierre queda por confirmar.
/// </summary>
public class ReadConfidenceRulesTests
{
    private static PdfWord W(string text, double confidence) => new(text, 0, 10, 0, 10, confidence);

    [Fact]
    public void The_summary_separates_the_words_with_digits_because_they_are_the_ones_that_matter()
    {
        var c = ReadConfidenceRules.Summarize([W("Factura", 95), W("29320-63", 60), W("2932063", 80), W("de", 99)])!;
        Assert.Equal(84, c.Mean);      // (95 + 60 + 80 + 99) / 4 = 83,5
        Assert.Equal(70, c.Numeric);   // (60 + 80) / 2
        Assert.Equal(4, c.Words);
    }

    [Fact]
    public void Without_numbers_the_numeric_figure_is_the_mean_and_without_words_there_is_no_confidence()
    {
        Assert.Equal(new OcrConfidence(90, 90, 2), ReadConfidenceRules.Summarize([W("Hola", 90), W("mundo", 90)]));
        Assert.Null(ReadConfidenceRules.Summarize([W("  ", 90)]));
        Assert.Null(ReadConfidenceRules.Summarize([]));
    }

    [Fact]
    public void Pages_combine_weighted_by_their_words()
    {
        var c = ReadConfidenceRules.Combine([new OcrConfidence(90, 90, 100), new OcrConfidence(60, 70, 300), null])!;
        Assert.Equal(68, c.Mean);      // (90*100 + 60*300) / 400 = 67,5
        Assert.Equal(75, c.Numeric);   // (90*100 + 70*300) / 400
        Assert.Equal(400, c.Words);
        Assert.Null(ReadConfidenceRules.Combine([null, null]));
    }

    [Theory]
    [InlineData(79, null, true)]    // por omisión el mínimo es 80
    [InlineData(80, null, false)]
    [InlineData(95, 96, true)]      // un lector exigente
    [InlineData(40, 0, false)]      // 0: no se vigila
    [InlineData(10, 30, true)]
    public void The_reader_minimum_decides_when_a_reading_is_doubtful(int numeric, int? min, bool expected)
        => Assert.Equal(expected, ReadConfidenceRules.IsDoubtful(new OcrConfidence(numeric, numeric, 10), min));

    [Fact]
    public void A_document_that_did_not_go_through_ocr_is_never_doubtful()
        => Assert.False(ReadConfidenceRules.IsDoubtful(null, 99));

    [Fact]
    public void The_reader_minimum_is_validated_between_zero_and_a_hundred_with_the_exact_message_and_the_doubtful_message_names_both_figures()
    {
        DocumentReader R(int? min) => new("L" + min, ReaderSources.Pdf, DocumentTargets.Order, null, [new EmailBodyField(1, EmailBodyKinds.Label, "A")], MinConfidence: min);
        Assert.Empty(DocumentReaderRules.Validate([R(null), R(0), R(100), R(85)]));
        Assert.Equal(ReadConfidenceRules.MinInvalidMessage, DocumentReaderRules.Validate([R(101)])["email.readers[0].minConfidence"]);
        Assert.Equal(ReadConfidenceRules.MinInvalidMessage, DocumentReaderRules.Validate([R(-1)])["email.readers[0].minConfidence"]);
        Assert.Equal("Lectura dudosa (78 % < 85 %): verifique lo creado contra el documento.", ReadConfidenceRules.DoubtfulMessage(78, 85));
    }
}
