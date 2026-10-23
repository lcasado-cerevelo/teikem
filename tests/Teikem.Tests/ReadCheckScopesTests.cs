using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Integrations;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-23: chequeos que comparan lo que el documento LISTA (no un número que declara) y chequeos sobre el encabezado o el documento entero, contra todas las filas leídas. El caso: la hoja de ruta de CMS lista sus
/// paradas en la primera página y trae una orden por parada; si el OCR deja de leer una orden, el chequeo lo marca.
/// </summary>
public class ReadCheckScopesTests
{
    private static IReadOnlyList<IReadOnlyDictionary<int, string>> Rows(params string[] keys)
        => keys.Select(k => (IReadOnlyDictionary<int, string>)new Dictionary<int, string> { [1] = k }).ToList();

    private static readonly ReadCheck Stops = new(ReadCheckKinds.MatchCount, @"^\S{1,2}\t[^\t]{3,}\t[^\t]{3,}\t[^\t]{2,}\s*$", 1, Distinct: true, Name: "Paradas", Scope: ReadCheckScopes.Header);

    [Fact]
    public void The_listed_items_are_counted_by_the_lines_that_match_and_compared_with_the_rows_read()
    {
        const string header = "ROUTE SHEET #: 1\nCOMPLETE?\tPATIENT\tDELIVERY ADDRESS\tCITY\nO\tSylvia Alvarado\tBo Santa Amelia III\tGuaynabo\nO\tJuan Perez\tCalle Luna 5\tCarolina\n";
        Assert.Empty(ReadCheckRules.Evaluate([Stops], header, Rows("A-1", "A-2", "A-2")));   // 2 paradas, 2 órdenes distintas (una con varias líneas)
        Assert.Equal(ReadCheckRules.MatchCountFailed("Paradas", 2, 1), Assert.Single(ReadCheckRules.Evaluate([Stops], header, Rows("A-1", "A-1"))));
        Assert.Equal(ReadCheckRules.NothingListed("Paradas"), Assert.Single(ReadCheckRules.Evaluate([Stops], "sin tabla", Rows("A-1"))));
    }

    [Fact]
    public void The_scope_is_validated_and_the_new_kind_is_known()
    {
        Assert.Empty(ReadCheckRules.Validate([Stops, Stops with { Scope = null }, Stops with { Scope = "document" }], "k"));
        Assert.Equal(ReadCheckRules.UnknownScopeMessage("TODO"), ReadCheckRules.Validate([Stops with { Scope = "TODO" }], "k")["k[0].scope"]);
        Assert.Equal("Chequeo desconocido: X. Use LINE_COUNT, SUM o MATCH_COUNT.", ReadCheckRules.UnknownKindMessage("X"));
    }

    private static DocumentReader CmsReader() => NoticeDocumentTargetsTests.Readers().Single(r => r.Name == "Hoja de ruta de CMS");

    [Fact]
    public void The_route_sheet_model_has_the_stops_check_in_the_header_scope()
    {
        var check = Assert.Single(CmsReader().Checks!);
        Assert.Equal((ReadCheckKinds.MatchCount, ReadCheckScopes.Header, true), (check.Kind, check.Scope, check.Distinct));
    }

    [Fact]
    public void An_order_the_ocr_did_not_read_makes_the_route_sheet_doubtful_while_a_complete_one_passes()
    {
        var cms = CmsReader();
        string Header(int stops) => "10/8/2026\nROUTE SHEET #: 1414062\nCIS\tTRUCK: Advances Logistics\nClinical Medical Services, lr\tDRIVER: ERD\tPick Up\t1\nCOMPLETE?\tPATIENT\tDELIVERY ADDRESS\tCITY\tARRIVAL\tDEPARTURE\n"
            + string.Concat(Enumerable.Range(1, stops).Select(i => $"O\tPaciente {i}\tCalle {i}\tGuaynabo\n"));
        string Order(int i) => $"Clinical Medical Services\nOrder #: 2026073115{i:00}\tOrder Type: Referral\nOrder Entry Date: 7/31/2026\tDelivery Date: 10/9/2026\nName: Paciente {i}\nAddress: Calle {i}\nCity: Guaynabo, PR\t00696\nPhone #: 787-501-6941\n"
            + "ORDER TYPE QTY\tTYPE\tDESCRIPTION\tHCPC CODE\tPRODUCT CODE\nPick Up\t1\tFFS\tMOTOR\tE1499\tMOTOR";

        var complete = Header(2) + Order(1) + "\n" + Order(2);
        var okReading = EmailRules.ReadDocument(complete, cms.Fields!, cms.RecordStart);
        Assert.Empty(IntegrationRunner.EvaluateChecks(cms, okReading, complete));

        var missing = Header(2) + Order(1);   // la hoja lista 2 pacientes y el OCR solo leyó 1 orden
        var badReading = EmailRules.ReadDocument(missing, cms.Fields!, cms.RecordStart);
        Assert.Equal(ReadCheckRules.MatchCountFailed("Paradas de la hoja de ruta", 2, 1), Assert.Single(IntegrationRunner.EvaluateChecks(cms, badReading, missing)));
    }

    [Fact]
    public void The_real_route_sheet_passes_its_stops_check()
    {
        var path = "F:/Download/Ruta Example.pdf";
        var tessdata = Environment.GetEnvironmentVariable("TEIKEM_TESSDATA") ?? "F:/Download/TeikemMigracion/tessdata";
        if (!File.Exists(path) || !File.Exists(Path.Combine(tessdata, "spa.traineddata"))) return;
        var read = new PdfTextReader(Options.Create(new IntegrationOptions { OcrTessdataPath = tessdata }), NullLogger<PdfTextReader>.Instance).Read(File.ReadAllBytes(path));
        var cms = CmsReader();
        var reading = EmailRules.ReadDocument(read.Text, cms.Fields!, cms.RecordStart);
        Assert.Empty(reading.Missing);
        Assert.Empty(IntegrationRunner.EvaluateChecks(cms, reading, read.Text));   // 1 parada listada, 1 orden leída
    }
}
