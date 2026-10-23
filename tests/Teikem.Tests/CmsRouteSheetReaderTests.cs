using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lectores por registro (2026-10-23): la hoja de ruta de CMS trae en la primera página la ruta y el chofer y, desde la segunda, una orden por parada. El texto de abajo es el que salió del PDF real escaneado
/// («Ruta Example.pdf», con OCR); la segunda orden es la misma con otros datos para probar varios registros. Se lee con el modelo «Hoja de ruta de CMS» de <c>readerPresets.json</c>.
/// </summary>
public class CmsRouteSheetReaderTests
{
    private const string Header = "10/8/2026\nROUTE SHEET #: 1414062\nCIS\tTRUCK: Advances Logistics\nClinical Medical Services, lr\tDRIVER: ERD\tPick Up\t1\nLlevando salud o lodo Puerto fie\nCOMPLETE?\tPATIENT\tDELIVERY ADDRESS\tCITY\tARRIVAL\tDEPARTURE\nO\tSylvia Alvarado\tBo Santa Amelia III\tGuaynabo\n";

    private const string Order1 = "Clinical Medical Services\nCarrera PR-3 KM 9.5 Ave 65 Infanteria\nCarolina, PR 00985\n02 Permits:\n(787) 620-2900\nOrder #: 202607311567\tOrder Type: Referral\nOrder Entry Date: 7/31/2026 11:02:57 AM\tDelivery Date: 10/9/2026\n2026 0731 1567\n"
        + "HIC1: 9JD5C28VK59\tHT: 62\tWT: 160\tPRIMARY CARE PHYSICIAN INFORMATION\nHIC2: ZUA001347471742\tDOB: 3/11/1946\nRicardo Rodriguez Navarro,\nHIC3:\tMT ID: 1821545\nUrb San Francisco 225 Calle Violeta\nName: Alvarado, Sylvia\tSan Juan, PR 00745\nUPIN #: G41668\nAddress: Bo Amelia Ill\nPhone #: 787-887-1110\nComplex:\nCity: Guaynabo, PR\t00696\nPhone #: 787-501-6941\nPATIENT DIRECTIONS\nPRIMARY INSURER\nMEDICARE CIGNA\t(FFS)\nDelivery Instructions:\n(10/9/2026):LLAMAR UNICAMNTE A ESTE NUMERO (7875025766) sylvia espinosa hija;\n"
        + "ORDER TYPE QTY\tTYPE\tDESCRIPTION\tHCPC CODE\tPRODUCT CODE\nPick Up\t4\tFFS\tHOSPITAL BED, SEMI-ELEC.W/MATT.& RAILS\tE0260\tE0260\nPick Up\t4\tFFS\tHAND CONTROL\tE1499\tCONTROL\nPick Up\t4\tFFS\tMOTOR\tE1499\tMOTOR\nPick Up\t4\tFFS\tBOARDS (PAIR)\tE1399\tBOARD\nPick Up\t4\tFFS\tRAILING (PAIR)\tE1499\tRAILING\nPick Up\t4\tFFS\tSCHAFT\tE1499\tSCHAFT\nPick Up\t1\tFFS\tHOSPITAL BED MANUAL CRANK DRIVE MEDICAL\tE1399\tCRANK\nNew DME\t1\tFFS\tPROVIDE WAIVER AND RELEASE OF LIABILITY\tNOTES\tNOTES";

    private const string Order2 = "Clinical Medical Services\nOrder #: 202608010001\tOrder Type: Referral\nOrder Entry Date: 8/1/2026 9:00:00 AM\tDelivery Date: 10/9/2026\n"
        + "Name: Perez, Juan\nAddress: Calle Luna 5\nCity: Carolina, PR\t00985\nPhone #: 787-111-2222\nDelivery Instructions:\n\nORDER TYPE QTY\tTYPE\tDESCRIPTION\tHCPC CODE\tPRODUCT CODE\nPick Up\t1\tFFS\tWHEELCHAIR, STANDARD, D-A, D-F\tK0001\tK0001";

    private static DocumentReader Cms() => NoticeDocumentTargetsTests.Readers().Single(r => r.Name == "Hoja de ruta de CMS");

    [Fact]
    public void The_route_sheet_goes_to_the_cms_reader()
    {
        var input = new ReaderInput(ReaderSources.Pdf, "ruta.pdf", "Ruta", Header + Order1);
        Assert.Equal("Hoja de ruta de CMS", DocumentReaderRules.Assign(NoticeDocumentTargetsTests.Readers(), [input]).Single().Reader.Name);
        Assert.Equal(DocumentTargets.Order, Cms().Target);
    }

    [Fact]
    public void One_order_per_page_becomes_rows_of_the_import_template_with_the_route_from_the_header()
    {
        var cms = Cms();
        var reading = EmailRules.ReadDocument(Header + Order1 + "\n" + Order2, cms.Fields!, cms.RecordStart);
        Assert.Empty(reading.Missing);
        Assert.Equal(2, reading.Parts.Count);
        var rows = CsvParser.Parse(EmailRules.ToCsv(reading, withHeader: false, ','), ',', hasHeader: false).ToList();
        Assert.Equal(8, rows.Count);   // 7 equipos de la primera orden + 1 de la segunda (la línea «New DME» no es pieza: es nota)

        var first = rows[0].Cells;
        Assert.Equal("202607311567", first[0]);                  // número de orden
        Assert.Equal("202607311567", first[1]);                  // número de envío (el mismo)
        Assert.Equal("Alvarado, Sylvia", first[2]);              // paciente
        Assert.Equal("Bo Amelia Ill", first[3]);                 // dirección (tal como la leyó el OCR)
        Assert.Equal("Guaynabo", first[4]);
        Assert.Equal("00696", first[5]);
        Assert.Equal("787-501-6941", first[6]);                  // el teléfono del paciente, no el del médico
        Assert.Equal("10/9/2026", first[7]);                     // fecha de entrega
        Assert.Equal("(10/9/2026):LLAMAR UNICAMNTE A ESTE NUMERO (7875025766) sylvia espinosa hija; | PROVIDE WAIVER AND RELEASE OF LIABILITY", first[8]);   // instrucciones + nota «New DME»
        Assert.Equal("ERD Pick Up 1", first[9]);                 // ruta = chofer de la hoja, con los espacios del OCR juntados
        Assert.Equal("HOSPITAL BED, SEMI-ELEC.W/MATT.& RAILS", first[10]);
        Assert.Equal(new[] { "HOSPITAL BED, SEMI-ELEC.W/MATT.& RAILS", "HAND CONTROL", "MOTOR", "BOARDS (PAIR)", "RAILING (PAIR)", "SCHAFT", "HOSPITAL BED MANUAL CRANK DRIVE MEDICAL" }, rows.Take(7).Select(r => r.Cells[10]));
        Assert.All(rows.Take(7), r => Assert.Equal("202607311567", r.Cells[0]));

        var second = rows[7].Cells;   // otra orden: hereda la ruta del encabezado y no trae notas
        Assert.Equal("202608010001", second[0]);
        Assert.Equal("Perez, Juan", second[2]);
        Assert.Equal("ERD Pick Up 1", second[9]);
        Assert.Equal("WHEELCHAIR, STANDARD, D-A, D-F", second[10]);
        Assert.True(second.Count <= 8 || second[8] == string.Empty);
    }

    [Fact]
    public void A_document_without_any_record_says_so_instead_of_creating_nothing_silently()
    {
        var cms = Cms();
        var reading = EmailRules.ReadDocument(Header, cms.Fields!, cms.RecordStart);
        Assert.Equal(EmailRules.NoRecordsMessage, Assert.Single(reading.Missing));
        Assert.False(reading.HasData);
    }

    [Fact]
    public void A_record_missing_a_required_field_names_the_record()
    {
        var cms = Cms();
        var broken = Order2.Replace("Name: Perez, Juan\n", string.Empty);
        var reading = EmailRules.ReadDocument(Header + Order1 + "\n" + broken, cms.Fields!, cms.RecordStart);
        Assert.Contains(reading.Missing, m => m.StartsWith("Registro 2:"));
        Assert.DoesNotContain(reading.Missing, m => m.StartsWith("Registro 1:"));
    }

    [Fact]
    public void Without_a_record_marker_the_document_is_one_record_as_always()
    {
        var fields = new[] { new EmailBodyField(1, EmailBodyKinds.Label, "Reference #", Required: true) };
        var reading = EmailRules.ReadDocument("Reference #: PO-1\nOtra cosa", fields, null);
        Assert.Empty(reading.Missing);
        Assert.Equal("PO-1", Assert.Single(reading.Parts).Single[1]);
    }

    [Fact]
    public void A_field_with_append_joins_to_the_value_already_in_its_column()
    {
        var fields = new[]
        {
            new EmailBodyField(9, EmailBodyKinds.Regex, Pattern: "Uno: (.+)"),
            new EmailBodyField(9, EmailBodyKinds.Regex, Pattern: "Dos: (.+)", AppendWith: " | "),
            new EmailBodyField(8, EmailBodyKinds.Regex, Pattern: "Tres: (.+)", AppendWith: " | "),
        };
        var one = EmailRules.ReadBody("Uno: a\nDos: b\nTres: c", fields);
        Assert.Equal("a | b", one.Single[9]);
        Assert.Equal("c", one.Single[8]);   // sin valor previo, entra tal cual
    }

    [Fact]
    public void Spaces_transform_collapses_cells_split_by_the_ocr()
        => Assert.Equal("ERD Pick Up 1", EmailRules.Transform("ERD\tPick Up\t 1", EmailTransforms.Spaces));
}
