using Teikem.Domain.Integrations;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// 2026-10-22 — lectura genérica de documentos (cuerpo de correo o PDF convertido a texto): bloques por expresión regular por línea, bloques que se repiten en cada página y etiquetas que se leen por celda.
/// Los textos de prueba son SINTÉTICOS (imitan la estructura de un packing list, una factura y un pedido por formulario; no traen datos de clientes).
/// </summary>
public class DocumentReadingTests
{
    // Un packing list de 3 páginas: el encabezado y la línea de pie se repiten; algunas descripciones se parten en celdas y hay renglones sueltos con «-».
    private const string PackingList =
        "Ship To:\nACME DISTRIBUTOR LLC\tFREIGHT CARRIER\n1 Main Street\n" +
        "Ship Date 10/7/26\tOrder #\tGRP0001\tCustomer PO Number\t990011\n" +
        "Product / Description\tOrdered\tShipped\n" +
        "T-100\tSHAMPOO 300ML / 10.1 FL.OZ\t24\t24\n" +
        "T-101\tMASK 550G\t60\t60\n" +
        "Page 1 of 3\n\n" +
        "Ship To:\nACME DISTRIBUTOR LLC\tFREIGHT CARRIER\n" +
        "Ship Date 10/7/26\tOrder #\tGRP0001\tCustomer PO Number\t990011\n" +
        "Product / Description\tOrdered\tShipped\n" +
        "T-200\tMINI\tAMINO 30ML\t50\t50\n-\n" +
        "T-201\tTONER\t10.72 LIGHTEST BLONDE 60 G\t24\t24\n-\t-\n" +
        "Page 2 of 3\n\n" +
        "Ship To:\nACME DISTRIBUTOR LLC\n" +
        "Product / Description\tOrdered\tShipped\n" +
        "T-300\tSACHET 15ML (EACH)\t1,000\t1,000\n" +
        "Page 3 of 3";

    private static EmailBodyField Items(bool repeat) => new(2, EmailBodyKinds.Block, Start: "Product / Description", End: "Page", Repeat: repeat,
        LineRegex: @"^(T-[\w-]+)\t.*?\t([\d,]+)\t([\d,]+)$",
        Columns: new[] { new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 3) }, Transform: EmailTransforms.None);

    [Fact]
    public void A_repeated_block_reads_every_page_and_skips_noise_lines()
    {
        var reading = EmailRules.ReadBody(PackingList, new[] { Items(repeat: true) });
        Assert.Empty(reading.Missing);
        Assert.Equal(new[] { "T-100", "T-101", "T-200", "T-201", "T-300" }, reading.Lines.Select(l => l[2]).ToArray());
        Assert.Equal(new[] { "24", "60", "50", "24", "1,000" }, reading.Lines.Select(l => l[3]).ToArray());
    }

    [Fact]
    public void Without_repeat_only_the_first_block_is_read_as_before()
    {
        var reading = EmailRules.ReadBody(PackingList, new[] { Items(repeat: false) });
        Assert.Equal(new[] { "T-100", "T-101" }, reading.Lines.Select(l => l[2]).ToArray());
    }

    [Fact]
    public void A_label_is_read_from_any_cell_of_the_row_and_stops_at_the_cell()
    {
        var fields = new[]
        {
            new EmailBodyField(1, EmailBodyKinds.Label, Label: "Order #"),
            new EmailBodyField(2, EmailBodyKinds.Label, Label: "Customer PO Number"),
            new EmailBodyField(3, EmailBodyKinds.Label, Label: "Ship Date"),
        };
        var reading = EmailRules.ReadBody(PackingList, fields);
        Assert.Equal("GRP0001", reading.Single[1]);
        Assert.Equal("990011", reading.Single[2]);
        Assert.Equal("10/7/26", reading.Single[3]);
    }

    [Fact]
    public void A_label_at_the_start_of_the_line_keeps_reading_the_whole_line_when_there_are_no_cells()
        => Assert.Equal("17768992", EmailRules.ReadBody("Reference #     17768992\nStatus  Confirmed", new[] { new EmailBodyField(1, EmailBodyKinds.Label, Label: "Reference #") }).Single[1]);

    // Una factura: cada renglón de producto se parte en 2 o 3 líneas; el SKU puede ir pegado a la descripción.
    private const string Invoice =
        "Invoice no.: INV-5001\tTerms: Net 30\n" +
        "#\tProduct or service\tSKU\tDescription\tQty\tRate\tAmount\n" +
        "1.\tSACHET DELUXE PLUS\tT-63769\tSACHET DELUXE PLUS S&C\t400\t$0.50\t$200.00\n" +
        "S&C 15ML / 0.5 FL.OZ (each)\t15ML / 0.5 FL.OZ (each) EX\n" +
        "2.\tBLOND REVOLUTION IMPASSABLE T-8402\tBLOND REVOLUTION IMPASSABLE\t48\t$7.22\t$346.56\n" +
        "BLOND FINISHER 100ML\tBLOND FINISHER 100ML\n\n" +
        "3.\tNET MASK\tT-203\tNET MASK 550G EX\t60\t$10.00\t$600.00\n" +
        "4.\tIlluminatte Oil\tT-8533\tOIL 60ML\t168\t$9.00\t$1,512.00\n" +
        "5.\tShopify B2B\tdiscounts\tShopify B2B\tdiscounts\t-$200.00\n" +
        "Total\t$2,458.56";

    [Fact]
    public void An_invoice_with_wrapped_cells_is_read_by_a_regex_per_line()
    {
        var fields = new[]
        {
            new EmailBodyField(1, EmailBodyKinds.Label, Label: "Invoice no.", Required: true),
            new EmailBodyField(2, EmailBodyKinds.Block, Start: "Product or service", End: "Total", Transform: EmailTransforms.None,
                LineRegex: @"^\d+\.\t.*?(T-\d+).*\t(\d+)\t\$([\d.,]+)\t\$([\d.,]+)$",
                Columns: new[] { new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 2), new EmailBlockColumn(4, 4) }),
        };
        var reading = EmailRules.ReadBody(Invoice, fields);
        Assert.Empty(reading.Missing);
        Assert.Equal("INV-5001", reading.Single[1]);
        Assert.Equal(new[] { "T-63769", "T-8402", "T-203", "T-8533" }, reading.Lines.Select(l => l[2]).ToArray());   // la línea de descuento y las de continuación se ignoran
        Assert.Equal(new[] { "400", "48", "60", "168" }, reading.Lines.Select(l => l[3]).ToArray());
        Assert.Equal("1,512.00", reading.Lines[3][4]);   // el grupo no incluye el $
    }

    [Fact]
    public void A_block_without_start_and_end_can_run_on_the_regex_alone()
    {
        const string body = "Saludos\nOrder Summary\nItem Description        Quantity        Amount\n101.006 Miracle Shampoo 300ml   1       $14.03\nColorBrush Nueva Gratis 1       $0.00\nOrder Sub Total $14.03\n";
        var field = new EmailBodyField(2, EmailBodyKinds.Block, LineRegex: @"^(\S+)\s+(.+?)\s+(\d+)\s+\$([\d.,]+)$",
            Columns: new[] { new EmailBlockColumn(2, 1), new EmailBlockColumn(3, 3), new EmailBlockColumn(4, 4) });
        var reading = EmailRules.ReadBody(body, new[] { field });
        Assert.Equal(new[] { "101.006", "ColorBrush" }, reading.Lines.Select(l => l[2]).ToArray());
        Assert.Equal(new[] { "1", "1" }, reading.Lines.Select(l => l[3]).ToArray());
    }

    [Fact]
    public void Block_validation_accepts_a_line_regex_instead_of_markers_and_checks_it()
    {
        EmailConfig Cfg(EmailBodyField f) => new("h", 993, true, "u", ReadBody: true, BodyFields: new[] { f });
        var cols = new[] { new EmailBlockColumn(2, 1) };
        Assert.DoesNotContain("email.bodyFields[0].start", EmailRules.Validate(Cfg(new EmailBodyField(2, EmailBodyKinds.Block, LineRegex: @"^(\d+)$", Columns: cols)), true).Keys);
        Assert.Equal(EmailRules.BadRegexMessage("(["), EmailRules.Validate(Cfg(new EmailBodyField(2, EmailBodyKinds.Block, LineRegex: "([", Columns: cols)), true)["email.bodyFields[0].lineRegex"]);
        Assert.Equal(EmailRules.BlockMarkersMessage, EmailRules.Validate(Cfg(new EmailBodyField(2, EmailBodyKinds.Block, Columns: cols)), true)["email.bodyFields[0].start"]);
        Assert.Equal(EmailRules.RepeatNeedsStartMessage, EmailRules.Validate(Cfg(new EmailBodyField(2, EmailBodyKinds.Block, LineRegex: @"^(\d+)$", Repeat: true, Columns: cols)), true)["email.bodyFields[0].start"]);
    }
}
