using System.Text;
using Teikem.Domain.Sales;
using Xunit;
using C = Teikem.Domain.Sales.SalesAnalysisCodes;

namespace Teikem.Tests;

/// <summary>
/// Lote 241 — lector de los reportes «Sales by Item Detail» y «Purchases by Item Detail» de QuickBooks (datos INVENTADOS con el mismo formato): encabezados anidados
/// sección → ítem «SKU (descripción)» cerrados con «Total …», comillas dobles escapadas, ítems sin descripción, precio en %, notas de crédito en negativo, Windows-1252,
/// totales que no cuadran y la cabecera de compras (Source Name / Cost Price).
/// </summary>
public sealed class QuickBooksDetailReportParserTests
{
    private const string Sales =
        ",\"Type\",\"Date\",\"Num\",\"Memo\",\"Name\",\"Qty\",\"Sales Price\",\"Amount\",\"Balance\"\r\n" +
        "\"Inventory\",,,,,,,,,\"\"\r\n" +
        "\"AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/01/2026\",\"2025-0001\",\"TIRAS DE PRUEBA 1/50CT.\",\"FARMACIA LA ESQUINA\",120,4.20,504.00,504.00\r\n" +
        ",\"Credit Memo\",\"10/08/2026\",\"2025-0002\",\"TIRAS DE PRUEBA 1/50CT.\",\"FARMACIA CAÑÓN\",-10,4.20,-42.00,462.00\r\n" +
        "\"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,110,,462.00,462.00\r\n" +
        "\"LN 28 (LANCETAS \"\"28\"\")\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/02/2026\",\"2025-0003\",\"LANCETAS \"\"28\"\"\",\"FARMACIA LA ESQUINA\",6,1.25,7.50,7.50\r\n" +
        "\"Total LN 28 (LANCETAS \"\"28\"\")\",,,,,,6,,7.50,7.50\r\n" +
        "\"Total Inventory\",,,,,,116,,469.50,469.50\r\n" +
        "\"Discounts\",,,,,,,,,\"\"\r\n" +
        "\"Discount 2%\",,,,,,,,,\"\"\r\n" +
        ",\"Invoice\",\"10/02/2026\",\"2025-0003\",\"DESCUENTO APROBADO\",\"FARMACIA LA ESQUINA\",\"\",\"-2.00%\",-0.15,-0.15\r\n" +
        "\"Total Discount 2%\",,,,,,0,,-0.15,-0.15\r\n" +
        "\"Total Discounts\",,,,,,0,,-0.15,-0.15\r\n" +
        "\"TOTAL\",,,,,,116,,469.35,469.35\r\n";

    private static byte[] Cp1252(string s)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetBytes(s);
    }

    [Fact]
    public void Reads_sections_items_and_transactions_of_a_sales_report_in_windows_1252()
    {
        var text = QuickBooksDetailReportParser.Decode(Cp1252(Sales));
        var r = QuickBooksDetailReportParser.Parse(text, "ventas.csv");
        Assert.Equal(C.KindSales, r.Kind);
        Assert.Equal(4, r.Rows.Count);
        Assert.Empty(r.Warnings);
        Assert.Equal(469.35m, r.ReportTotal);

        var first = r.Rows[0];
        Assert.Equal(("Inventory", "AB-100", "TIRAS DE PRUEBA 1/50CT.", "Invoice", new DateOnly(2026, 10, 1), "2025-0001", "FARMACIA LA ESQUINA", 120m, (decimal?)4.20m, 504m),
            (first.Section, first.ItemSku, first.ItemDescription, first.Type, first.Date, first.Number, first.Name, first.Quantity, first.Price, first.Amount));
        Assert.Equal(4, first.Line);

        var credit = r.Rows[1];
        Assert.Equal(("Credit Memo", "FARMACIA CAÑÓN", -10m, -42m), (credit.Type, credit.Name, credit.Quantity, credit.Amount));

        var lancets = r.Rows[2];
        Assert.Equal(("LN 28", "LANCETAS \"28\"", "LN 28 (LANCETAS \"28\")"), (lancets.ItemSku, lancets.ItemDescription, lancets.ItemHeader));

        var discount = r.Rows[3];
        Assert.Equal(("Discounts", "Discount 2%", (string?)null, 0m, (decimal?)null, -0.15m),
            (discount.Section, discount.ItemSku, discount.ItemDescription, discount.Quantity, discount.Price, discount.Amount));
    }

    [Fact]
    public void Warns_when_an_item_total_does_not_add_up()
    {
        var bad = Sales.Replace("\"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,110,,462.00,462.00", "\"Total AB-100 (TIRAS DE PRUEBA 1/50CT.)\",,,,,,110,,460.00,460.00");
        var r = QuickBooksDetailReportParser.Parse(bad, "ventas.csv");
        Assert.Contains(r.Warnings, w => w.Contains("AB-100 (TIRAS DE PRUEBA 1/50CT.)") && w.Contains("460.00") && w.Contains("462.00"));
    }

    [Fact]
    public void Reads_a_purchases_report_and_rejects_a_file_that_is_not_a_report()
    {
        const string purchases =
            ",\"Type\",\"Date\",\"Num\",\"Memo\",\"Source Name\",\"Qty\",\"Cost Price\",\"Amount\",\"Balance\"\n" +
            "\"Inventory\",,,,,,,,,\"\"\n" +
            "\"GL 1001 (GUANTES MEDIUM 1/100CT.)\",,,,,,,,,\"\"\n" +
            ",\"Item Receipt\",\"06/18/2026\",\"PO-77\",\"GUANTES\",\"SUPLIDOR UNO LLC\",1800,3.50,6300.00,6300.00\n" +
            ",\"Bill\",\"07/02/2026\",\"PO-78\",\"GUANTES\",\"SUPLIDOR UNO LLC\",0,3.50,0.00,6300.00\n" +
            ",\"Credit\",\"10/01/2026\",\"RC-1\",\"GUANTES\",\"SUPLIDOR UNO LLC\",-10,3.50,-35.00,6265.00\n" +
            "\"Total GL 1001 (GUANTES MEDIUM 1/100CT.)\",,,,,,1790,,6265.00,6265.00\n" +
            "\"Total Inventory\",,,,,,1790,,6265.00,6265.00\n" +
            "\"TOTAL\",,,,,,1790,,6265.00,6265.00\n";
        var r = QuickBooksDetailReportParser.Parse(purchases, "compras.csv");
        Assert.Equal(C.KindPurchases, r.Kind);
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(("GL 1001", "SUPLIDOR UNO LLC", 1800m, (decimal?)3.50m), (r.Rows[0].ItemSku, r.Rows[0].Name, r.Rows[0].Quantity, r.Rows[0].Price));
        Assert.Equal(-10m, r.Rows[2].Quantity);

        var ex = Assert.Throws<FormatException>(() => QuickBooksDetailReportParser.Parse("Item,Description\nA,B\n", "lista.csv"));
        Assert.Equal(SalesAnalysisRules.NotAReport("lista.csv"), ex.Message);
    }

    [Theory]
    [InlineData("1,234.50", 1234.50)]
    [InlineData("(12.50)", -12.50)]
    [InlineData("-2.00%", -2.00)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void Numbers_tolerate_thousands_parentheses_and_percent(string raw, double? expected)
        => Assert.Equal(expected is null ? null : (decimal?)(decimal)expected.Value, QuickBooksDetailReportParser.ParseNumber(raw));

    [Fact]
    public void Utf8_files_are_read_as_utf8_and_the_hash_identifies_the_content()
    {
        var utf8 = Encoding.UTF8.GetBytes("FARMACIA CAÑÓN");
        Assert.Equal("FARMACIA CAÑÓN", QuickBooksDetailReportParser.Decode(utf8));
        Assert.Equal("FARMACIA CAÑÓN", QuickBooksDetailReportParser.Decode(Cp1252("FARMACIA CAÑÓN")));
        Assert.Equal(QuickBooksDetailReportParser.Hash(utf8), QuickBooksDetailReportParser.Hash(Encoding.UTF8.GetBytes("FARMACIA CAÑÓN")));
        Assert.Equal(64, QuickBooksDetailReportParser.Hash(utf8).Length);
        Assert.Equal(("NECH 1003", "Padimont Gloves Large 1/100ct."), QuickBooksDetailReportParser.SplitItem("NECH 1003 (Padimont Gloves Large 1/100ct.)"));
    }
}
