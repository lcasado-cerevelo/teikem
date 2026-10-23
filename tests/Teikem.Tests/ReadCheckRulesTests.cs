using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23: chequeos de COMPLETITUD contra los totales que declara el propio documento (lo que la confianza del OCR no ve: una línea o una columna que el OCR dejó de leer no baja su porcentaje).
/// LINE_COUNT compara las líneas leídas con las que el documento dice traer; SUM compara la suma de una columna con el total que declara. Si no cuadran, o el total no aparece, la lectura es dudosa.
/// </summary>
public class ReadCheckRulesTests
{
    private static IReadOnlyList<IReadOnlyDictionary<int, string>> Rows(params (int Position, string Value)[][] rows)
        => rows.Select(r => (IReadOnlyDictionary<int, string>)r.ToDictionary(x => x.Position, x => x.Value)).ToList();

    [Fact]
    public void The_line_count_must_match_what_the_document_declares_and_a_repeated_key_counts_once_when_asked()
    {
        const string text = "INVOICES:\t3\tCASES:\t5";
        var rows = Rows([(1, "A-1"), (2, "x")], [(1, "A-1"), (2, "y")], [(1, "B-2"), (2, "z")]);
        var plain = new ReadCheck(ReadCheckKinds.LineCount, @"INVOICES:\s*(\d+)", 1, Name: "Facturas");
        var distinct = plain with { Distinct = true };
        Assert.Equal(new[] { ReadCheckRules.LineCountFailed("Facturas", 3, 3) }.Where(_ => false), ReadCheckRules.Evaluate([plain], text, rows));   // 3 líneas declaradas, 3 leídas: cuadra
        Assert.Equal(ReadCheckRules.LineCountFailed("Facturas", 3, 2), Assert.Single(ReadCheckRules.Evaluate([distinct], text, rows)));            // contadas por valor distinto: 2 de 3
        Assert.Empty(ReadCheckRules.Evaluate([plain with { Tolerance = 1 }, distinct with { Tolerance = 1 }], text, rows));
    }

    [Fact]
    public void A_missing_line_makes_the_count_fail_and_a_missing_total_is_a_failure_too()
    {
        var one = Rows([(1, "A-1")]);
        Assert.Equal(ReadCheckRules.LineCountFailed("Líneas", 2, 1), Assert.Single(ReadCheckRules.Evaluate([new ReadCheck(ReadCheckKinds.LineCount, @"LINES:\s*(\d+)", 1, Name: "Líneas")], "LINES: 2", one)));
        Assert.Equal(ReadCheckRules.TotalNotFound("Líneas"), Assert.Single(ReadCheckRules.Evaluate([new ReadCheck(ReadCheckKinds.LineCount, @"LINES:\s*(\d+)", 1, Name: "Líneas")], "sin total", one)));
    }

    [Fact]
    public void The_sum_of_a_column_must_match_the_declared_total_within_the_tolerance()
    {
        var rows = Rows([(4, "10,440.80")], [(4, "2,935.95")], [(4, "1,215.24")]);
        var sum = new ReadCheck(ReadCheckKinds.Sum, @"Total Amount USD\s*([\d,]+\.\d{2})", 4, Name: "Importes");
        Assert.Empty(ReadCheckRules.Evaluate([sum], "Total Amount USD\t14,591.99", rows));
        Assert.Equal(ReadCheckRules.SumFailed("Importes", 39167.01m, 14591.99m), Assert.Single(ReadCheckRules.Evaluate([sum], "Total Amount USD\t39,167.01", rows)));
        Assert.Empty(ReadCheckRules.Evaluate([sum with { Tolerance = 30000 }], "Total Amount USD\t39,167.01", rows));   // tolerancia explícita
    }

    [Fact]
    public void The_last_match_is_the_declared_total_and_numbers_are_read_with_thousands_commas()
    {
        Assert.Equal(92975.53m, ReadCheckRules.FindTotal("Total Amount USD 1.00\nTotal Amount USD 92,975.53", @"Total Amount USD\s*([\d,.]+)"));
        Assert.Null(ReadCheckRules.FindTotal("nada", @"Total:\s*(\d+)"));
        Assert.Null(ReadCheckRules.FindTotal("Total: x", @"Total:\s*(\w+)"));
        Assert.Equal(2845.15m, ReadCheckRules.ParseNumber("$2,845.15"));
        Assert.Null(ReadCheckRules.ParseNumber("abc"));
    }

    [Fact]
    public void Checks_are_validated_with_exact_messages()
    {
        Assert.Empty(ReadCheckRules.Validate([new ReadCheck(ReadCheckKinds.Sum, "(\\d+)", 4)], "k"));
        Assert.Equal(ReadCheckRules.UnknownKindMessage("X"), ReadCheckRules.Validate([new ReadCheck("X", "(\\d+)")], "k")["k[0].kind"]);
        Assert.Equal(ReadCheckRules.PatternRequiredMessage, ReadCheckRules.Validate([new ReadCheck(ReadCheckKinds.Sum, " ")], "k")["k[0].pattern"]);
        Assert.Equal(ReadCheckRules.PatternInvalidMessage("(["), ReadCheckRules.Validate([new ReadCheck(ReadCheckKinds.Sum, "([")], "k")["k[0].pattern"]);
        Assert.Equal(ReadCheckRules.PositionInvalidMessage, ReadCheckRules.Validate([new ReadCheck(ReadCheckKinds.Sum, "(\\d)", 0)], "k")["k[0].position"]);
        Assert.Equal(ReadCheckRules.ToleranceInvalidMessage, ReadCheckRules.Validate([new ReadCheck(ReadCheckKinds.Sum, "(\\d)", 1, Tolerance: -1)], "k")["k[0].tolerance"]);
        Assert.Equal(ReadCheckRules.TooManyMessage, ReadCheckRules.Validate(Enumerable.Range(0, 11).Select(_ => new ReadCheck(ReadCheckKinds.Sum, "(\\d)")).ToList(), "k")["k"]);
        // dentro de un lector
        var reader = new DocumentReader("L", ReaderSources.Pdf, DocumentTargets.Order, null, [new EmailBodyField(1, EmailBodyKinds.Label, "A")], Checks: [new ReadCheck("X", "(\\d+)")]);
        Assert.Equal(ReadCheckRules.UnknownKindMessage("X"), DocumentReaderRules.Validate([reader])["email.readers[0].checks[0].kind"]);
    }

    [Fact]
    public void The_failures_make_one_doubtful_message()
        => Assert.Equal("Lectura dudosa: a; b. Verifique lo creado contra el documento.", ReadCheckRules.DoubtfulMessage(["a", "b"]));

    [Fact]
    public void A_column_can_be_computed_as_one_group_over_another_and_a_zero_divisor_leaves_it_empty()
    {
        var field = new EmailBodyField(1, EmailBodyKinds.Block, Start: "Item", Separator: "\t", LineRegex: @"^(\d+)\t([\d.]+)\t([\d.]+)$",
            Columns: [new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 3, 2)]);   // cantidad = importe ÷ precio unitario
        var reading = EmailRules.ReadBody("Item\n10\t13.05\t130.50\n20\t0\t5.00\n30\t2.5\t7.50", [field]);
        Assert.Equal("10", reading.Lines[0][2]);
        Assert.Equal("10", reading.Lines[0][3]);   // 130,50 ÷ 13,05
        Assert.False(reading.Lines[1].ContainsKey(3));   // dividir entre 0: sin valor
        Assert.Equal("3", reading.Lines[2][3]);          // 7,50 ÷ 2,5
    }

    // ------------------------------------------------------------------ con los PDF reales de Drive Medical (solo si están en esta máquina y el OCR está instalado)

    private static (DocumentReader Reader, string Text)? Real(string file)
    {
        var path = Path.Combine("F:/Download", file);
        var tessdata = Environment.GetEnvironmentVariable("TEIKEM_TESSDATA") ?? "F:/Download/TeikemMigracion/tessdata";
        if (!File.Exists(path) || !File.Exists(Path.Combine(tessdata, "spa.traineddata"))) return null;
        var read = new PdfTextReader(Options.Create(new IntegrationOptions { OcrTessdataPath = tessdata }), NullLogger<PdfTextReader>.Instance).Read(File.ReadAllBytes(path));
        Assert.Null(read.Problem);
        return (NoticeDocumentTargetsTests.Readers().Single(r => r.Name == "Factura de Drive Medical"), read.Text);
    }

    [Fact]
    public void The_real_drive_invoice_that_is_complete_passes_the_sum_check_and_the_quantities_come_from_the_prices()
    {
        if (Real("2193_001.pdf") is not { } real) return;
        var reading = EmailRules.ReadDocument(real.Text, real.Reader.Fields!, real.Reader.RecordStart);
        Assert.Empty(reading.Missing);
        var rows = reading.RowsOf(0);
        Assert.Equal(new[] { "15770", "15970", "13016-2", "15011", "15006", "15019", "18080" }, rows.Select(r => r[2]));
        Assert.Equal(new[] { 80m, 15m, 19m, 4m, 80m, 110m, 240m }, rows.Select(r => decimal.Parse(r[3], System.Globalization.CultureInfo.InvariantCulture)));   // lo que se escribió a mano en cada círculo
        Assert.Empty(ReadCheckRules.Evaluate(real.Reader.Checks, real.Text, rows));   // la suma de los importes es 39,167.01, el total del documento
    }

    [Fact]
    public void The_real_four_page_drive_invoice_is_flagged_if_the_ocr_misses_any_line()
    {
        if (Real("2194_001.pdf") is not { } real) return;
        var reading = EmailRules.ReadDocument(real.Text, real.Reader.Fields!, real.Reader.RecordStart);
        var rows = reading.RowsOf(0);
        var failures = ReadCheckRules.Evaluate(real.Reader.Checks, real.Text, rows);
        var sum = rows.Sum(r => ReadCheckRules.ParseNumber(r[4]) ?? 0m);
        // cuadra solo si el OCR leyó TODAS las líneas; si no, el chequeo lo dice: nunca queda en silencio
        Assert.Equal(sum == 92975.53m, failures.Count == 0);
    }
}
