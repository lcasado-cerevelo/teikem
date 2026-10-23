using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// La hoja «Ruta completada» de CMS (texto real del PDF; DR, Referral #, tipo, cantidad, descripción…): cada renglón de una recogida (PU) trae el Referral # de lo recogido; el lector de cierre (ORDER_COMPLETION) saca
/// una clave por renglón y el destino deja UNA propuesta por orden. Las líneas de notas («N») no son recogidas. Se lee con el modelo «Ruta completada de CMS» de <c>readerPresets.json</c>.
/// </summary>
public class CmsCompletedRouteReaderTests
{
    private const string Completed = "10/21/2026\nROUTE SHEET #: 1414725\nTRUCK: Advances Logistics\nDRIVER: ERD Pick Up 1\tET-60\nDR\tREFERRAL #\tTYPE\tQTY\tHCPC Description\tHT\tWT\tAGE\tS/N\n"
        + "R\t202608241109\tPU\t1\tBUE0424\tO2 STATIONARY H-TANK**BACK UP\t68\t190\t67\nONLY****\nINSTRUCTIONS: RX VENCIDA\nR\t202608241109\tPU\t1\tE1353\tOXYGEN TANK REGULATOR FOR H TANK\t68\t190\t67\n"
        + "R\t202608241109\tN\t1\tNOTES\tPROVIDE WAIVER AND RELEASE OF\t68\t190\t67\nLIABILITY\nR\t202609171826\tPU\t1\tE0260\tHOSPITAL BED, SEMI-ELEC.W/MATT.& RAILS\t64\t160\t92\n"
        + "10/21/2026\nROUTE SHEET #: 1414725\nDR\tREFERRAL #\tTYPE\tQTY\tHCPC Description\tHT\tWT\tAGE\tS/N\nC\t202610071703\tPU\t1\tBUE0424\tO2 STATIONARY H-TANK\t62\t100\t94\nR\t202609171826\tPU\t1\tE1399\tCRANK\t64\t160\t92";

    private const string RouteWithOrders = "10/8/2026\nROUTE SHEET #: 1414062\nDRIVER: ERD Pick Up 1\nOrder #: 202607311567\tOrder Type: Referral\nOrder Entry Date: 7/31/2026 11:02:57 AM\tDelivery Date: 10/9/2026\nName: Alvarado, Sylvia\nAddress: Bo Amelia III\n"
        + "ORDER TYPE QTY\tTYPE\tDESCRIPTION\tHCPC CODE\tPRODUCT CODE\nPick Up\t1\tFFS\tMOTOR\tE1499\tMOTOR";

    [Fact]
    public void The_completed_route_sheet_goes_to_the_closing_reader_and_yields_a_key_per_pickup_line()
    {
        var readers = NoticeDocumentTargetsTests.Readers();
        var reader = DocumentReaderRules.Assign(readers, [new ReaderInput(ReaderSources.Pdf, "ruta-completada.pdf", "Ruta", Completed)]).Single().Reader;
        Assert.Equal("Ruta completada de CMS", reader.Name);
        Assert.Equal(DocumentTargets.OrderCompletion, reader.Target);
        Assert.Equal(ReaderModes.Suggest, reader.Mode);   // por omisión una persona confirma
        Assert.Equal(CompletionTargets.Pickup, reader.ToStatus);

        var reading = EmailRules.ReadDocument(Completed, reader.Fields!, reader.RecordStart);
        Assert.Empty(reading.Missing);
        var keys = CsvParser.Parse(EmailRules.ToCsv(reading, withHeader: false, ','), ',', hasHeader: false).Select(r => r.Cells[0]).ToList();
        // la línea de notas (N) no es una recogida; la clave repetida deja una sola propuesta por orden (lo hace el destino)
        Assert.Equal(new[] { "202608241109", "202608241109", "202609171826", "202610071703", "202609171826" }, keys);
        Assert.Equal(3, keys.Distinct().Count());
    }

    [Fact]
    public void The_route_sheet_with_orders_and_the_completed_one_each_go_to_their_own_reader()
    {
        var readers = NoticeDocumentTargetsTests.Readers();
        Assert.Equal("Hoja de ruta de CMS", DocumentReaderRules.Assign(readers, [new ReaderInput(ReaderSources.Pdf, "a.pdf", "", RouteWithOrders)]).Single().Reader.Name);
        Assert.Equal("Ruta completada de CMS", DocumentReaderRules.Assign(readers, [new ReaderInput(ReaderSources.Pdf, "b.pdf", "", Completed)]).Single().Reader.Name);
    }
}
