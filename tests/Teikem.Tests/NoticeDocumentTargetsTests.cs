using System.Text.Json;
using Teikem.Domain.Integrations;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Destinos de aviso de llegada y de salida (2026-10-23): el texto REAL que sale de los PDF de Axiscare (despacho y envío de mercancía) y de una factura escaneada de Drive Medical, leído con los modelos de lector
/// de la pantalla de Integraciones (<c>web-app/src/features/integrations/readerPresets.json</c>, la fuente única), pasa a avisos: un aviso por referencia/destino/fecha, sin avisos a medias.
/// </summary>
public class NoticeDocumentTargetsTests
{
    private const string Despacho1 = "Despacho de Mercancia\nDESCRIPCION DE PRODUCTOS\tNUMERO DE ITEMS\tCANTDIDAD EACH\tCANTIDAD DE PALETAS\n1\tSUR-DRY 10 SCOPE DRYING\t481-SCSS8030DRDP\t1\t1\tCARIBBEAN SURGICARE\n2\t2500 CALLE OBISPADO\n3\tJARDINES DE FAGOT\n4\tPONCE\tPR\n5\n6\n30";

    private const string DespachoVarios = "Despacho de Mercancia\nDESCRIPCION DE PRODUCTOS\tNUMERO DE ITEMS\tCANTDIDAD EACH\tCANTIDAD DE PALETAS\n"
        + "1\tHOWARD INSTRUMENT TABLE 72\"W X\t516-0117837000\t1\tFARMACIA INSTITUCIONAL\n2\tHOWARD INSTRUMENT TABLE\t516-0117831000\t1\tBAYAMON MEDICAL CENTER\n3\tCARR #2  KM 11.7\n4\tBAYAMON\tPR\n5\n6\n"
        + "7\tCELLULOSE FIBER MATTRESS\t29-15007\t25\tHOSPICIO SANTA RITA ISABELA\n8\tBO ARENALES\n9\tCARR #2 KM 111.5 CALLE MARGINA\n10\tISABELA\tPR\n"
        + "13\tCELLULOSE FIBER MATTRESS\t29-15007\t25\tHOSPICIO SANTA RITA-CABO ROJO\n14\tCARR 100 KM 3.8\n16\tCABO ROJO\tPR\n"
        + "19\tCELLULOSE FIBER MATTRESS\t29-15007\t20\tHOSPICIO SANTA RITA-PONCE\n20\tORENGO MEDICAL BUILDING 9105\n22\tPONCE\tPR\n23\n30";

    private const string Envio = "ENVIO DE MERCANCIA PARA ADVANCE\nDESCRIPCION DE PRODUCTOS\tSTOCKING\tNUMERO DE ITEMS\tCANTDIDAD EACH\tCANTIDAD DE PALETAS\n"
        + "1\tOXYGEN CANNULA ADULT 7FT HME\t50UN\t402-RE-351001-U\t400bx\t4\n2\tNEBULIZER KIT ADULT HME SWIVEL\t50UN\t402-RE-343002U-S\t165bx\t3\n3\n4\n30\tTOTAL";

    private const string Drive = "Invoice\nInformation\nCustomer Number\t100361\nBILLING ADDRESS\nInvoice Number\t35549809\nInvoice Date\t09/23/2026\nDelivery Number\t57513128\nInvoice Details\nItem\tMaterial\tQuantity\tUnit Price\tAmount\nDescription\n"
        + "0010\t14003\t=\t310EA\t13.05\t4,045.50\nAPP Pad Only, 1/ea, RTL\tper EA\n0040\t14027\t=\t48CV\t302.50\t14,520.00\n0050\tRTLPC23278\t29.14\t961.62\nArm Sling, RTL,1EA,6/CS\tper CV\n"
        + "0060\t12202KD-4\t=~\t300 CV\t98.22\t29,466.00\nTotal Amount USD\t48,993.12\n";

    internal static IReadOnlyList<DocumentReader> Readers()
    {
        var path = AppContext.BaseDirectory;
        while (path is not null && !File.Exists(Path.Combine(path, "web-app", "src", "features", "integrations", "readerPresets.json"))) path = Path.GetDirectoryName(path);
        Assert.NotNull(path);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(path!, "web-app", "src", "features", "integrations", "readerPresets.json")));
        var list = new List<DocumentReader>();
        foreach (var preset in doc.RootElement.EnumerateArray())
        {
            var r = preset.GetProperty("reader");
            var when = r.GetProperty("conditions").EnumerateArray().Select(c => new ReaderCondition(c.GetProperty("scope").GetString()!, c.GetProperty("operator").GetString()!, c.GetProperty("value").GetString()!)).ToList();
            var fields = r.GetProperty("fields").EnumerateArray().Select(f =>
            {
                string S(string n) => f.GetProperty(n).GetString() ?? string.Empty;
                var columns = S("columns").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.Split('=')).Select(p => p[1].Contains('/') ? new EmailBlockColumn(int.Parse(p[0]), int.Parse(p[1].Split('/')[0]), int.Parse(p[1].Split('/')[1])) : new EmailBlockColumn(int.Parse(p[0]), int.Parse(p[1]))).ToList();
                return new EmailBodyField(int.Parse(S("position")), S("kind"), S("label").Length == 0 ? null : S("label"), S("pattern").Length == 0 ? null : S("pattern"), f.GetProperty("required").GetBoolean(), S("transform"),
                    S("start").Length == 0 ? null : S("start"), null, S("separator").Length == 0 ? null : S("separator"), columns.Count == 0 ? null : columns,
                    S("lineRegex").Length == 0 ? null : S("lineRegex"), f.GetProperty("repeat").GetBoolean(), f.GetProperty("fromHeader").GetBoolean(), S("appendWith").Length == 0 ? null : S("appendWith"));
            }).ToList();
            list.Add(new DocumentReader(r.GetProperty("name").GetString()!, r.GetProperty("source").GetString()!, r.GetProperty("target").GetString()!, when, fields, null, null, false, r.GetProperty("recordStart").GetString() is { Length: > 0 } rs ? rs : null, r.GetProperty("mode").GetString()!, r.GetProperty("toStatus").GetString(), null, r.GetProperty("minConfidence").GetString() is { Length: > 0 } mc ? int.Parse(mc) : null,
                r.GetProperty("checks").EnumerateArray().Select(c => new ReadCheck(c.GetProperty("kind").GetString()!, c.GetProperty("pattern").GetString()!, int.Parse(c.GetProperty("position").GetString()!), c.GetProperty("distinct").GetBoolean(),
                    c.GetProperty("tolerance").GetString() is { Length: > 0 } tol ? decimal.Parse(tol, System.Globalization.CultureInfo.InvariantCulture) : null, c.GetProperty("name").GetString(), c.GetProperty("scope").GetString())).ToList()));
        }
        return list;
    }

    private static (IReadOnlyList<NoticeRowRules.Group> Groups, IReadOnlyList<(int Row, string Reason)> Errors) Read(string text, string target, string readerName)
    {
        var reader = Readers().Single(r => r.Name == readerName);
        Assert.Equal(target, reader.Target);
        var reading = EmailRules.ReadBody(text, reader.Fields!);
        Assert.Empty(reading.Missing);
        var csv = EmailRules.ToCsv(reading, withHeader: false, ',');
        return NoticeRowRules.BuildGroups(CsvParser.Parse(csv, ',', hasHeader: false), withDestination: target == DocumentTargets.ExitNotice);
    }

    [Fact]
    public void The_presets_are_valid_readers()
    {
        var readers = Readers();
        Assert.Equal(5, readers.Count);
        Assert.Empty(DocumentReaderRules.Validate(readers));
    }

    [Fact]
    public void Each_document_goes_to_the_reader_of_its_title_even_though_the_layout_is_the_same()
    {
        var readers = Readers();
        ReaderInput In(string text) => new(ReaderSources.Pdf, "doc.pdf", "Mercancía", text);
        Assert.Equal("Despacho de mercancía", DocumentReaderRules.Assign(readers, [In(Despacho1)]).Single().Reader.Name);
        Assert.Equal("Despacho de mercancía", DocumentReaderRules.Assign(readers, [In(DespachoVarios)]).Single().Reader.Name);
        Assert.Equal("Envío de mercancía", DocumentReaderRules.Assign(readers, [In(Envio)]).Single().Reader.Name);
        Assert.Equal("Factura de Drive Medical", DocumentReaderRules.Assign(readers, [In(Drive)]).Single().Reader.Name);
        Assert.Empty(DocumentReaderRules.Assign(readers, [In("ROUTE SHEET #: 1414725\nTRUCK: Advances Logistics")]));
    }

    [Fact]
    public void Dispatch_with_one_destination_makes_one_exit_notice_line()
    {
        var (groups, errors) = Read(Despacho1, DocumentTargets.ExitNotice, "Despacho de mercancía");
        Assert.Empty(errors);
        var g = Assert.Single(groups);
        Assert.Equal("CARIBBEAN SURGICARE", g.Destination);
        var row = Assert.Single(g.Rows);
        Assert.Equal("481-SCSS8030DRDP", row.Sku);
        Assert.Equal(1m, row.Quantity);
    }

    [Fact]
    public void Dispatch_with_several_destinations_makes_one_exit_notice_per_destination()
    {
        var (groups, errors) = Read(DespachoVarios, DocumentTargets.ExitNotice, "Despacho de mercancía");
        Assert.Empty(errors);
        Assert.Equal(5, groups.Count);
        Assert.Equal(new[] { "FARMACIA INSTITUCIONAL", "BAYAMON MEDICAL CENTER", "HOSPICIO SANTA RITA ISABELA", "HOSPICIO SANTA RITA-CABO ROJO", "HOSPICIO SANTA RITA-PONCE" }, groups.Select(x => x.Destination));
        Assert.Equal(new[] { 1m, 1m, 25m, 25m, 20m }, groups.Select(x => x.Rows.Sum(r => r.Quantity ?? 0)));
        Assert.Equal("29-15007", groups[2].Rows[0].Sku);
    }

    [Fact]
    public void Shipment_makes_one_arrival_notice_with_the_units_not_the_pallets()
    {
        var (groups, errors) = Read(Envio, DocumentTargets.ArrivalNotice, "Envío de mercancía");
        Assert.Empty(errors);
        var g = Assert.Single(groups);
        Assert.Equal(new[] { "402-RE-351001-U", "402-RE-343002U-S" }, g.Rows.Select(r => r.Sku));
        Assert.Equal(new[] { 400m, 165m }, g.Rows.Select(r => r.Quantity ?? 0));
    }

    [Fact]
    public void The_handwritten_quantity_of_a_scanned_invoice_comes_from_amount_over_unit_price()
    {
        var (groups, errors) = Read(Drive, DocumentTargets.ArrivalNotice, "Factura de Drive Medical");
        Assert.Empty(errors);   // la línea 0050 no traía la cantidad (estaba encerrada a mano): sale de 961,62 ÷ 29,14 = 33
        Assert.Equal(new[] { "14003", "14027", "RTLPC23278", "12202KD-4" }, groups.Single().Rows.Select(r => r.Sku));
        Assert.Equal(new[] { 310m, 48m, 33m, 300m }, groups.Single().Rows.Select(r => r.Quantity ?? 0));
        Assert.Equal("35549809", groups.Single().Reference);
    }

    [Theory]
    [InlineData("400bx", 400)]
    [InlineData("1,430", 1430)]
    [InlineData("310EA", 310)]
    [InlineData("12.5", 12.5)]
    public void Quantities_are_read_by_their_number(string raw, double expected) => Assert.Equal((decimal)expected, NoticeRowRules.ParseQuantity(raw));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("sin")]
    public void A_missing_or_zero_quantity_is_not_a_quantity(string raw) => Assert.Null(NoticeRowRules.ParseQuantity(raw));

    [Fact]
    public void Dates_are_read_the_puerto_rico_way_or_as_iso()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), NoticeRowRules.ParseDate("10/12/2026"));
        Assert.Equal(new DateOnly(2026, 10, 12), NoticeRowRules.ParseDate("2026-10-12"));
        Assert.Null(NoticeRowRules.ParseDate("pronto"));
    }

    [Fact]
    public void The_notice_targets_are_valid_destinations_for_a_reader()
    {
        var r = new DocumentReader("x", ReaderSources.Pdf, DocumentTargets.ExitNotice, null, [new EmailBodyField(1, EmailBodyKinds.Label, "A")]);
        Assert.Empty(DocumentReaderRules.Validate([r]));
    }
}
