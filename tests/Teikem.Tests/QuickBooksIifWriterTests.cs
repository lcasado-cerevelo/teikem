using System.Text;
using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

// Lote 225 (P5 del pedido de vendedores): el generador PURO del archivo IIF de QuickBooks Desktop — archivo exacto (factura, cliente, producto), kits desglosados con
// prorrateo que cierra al centavo, IVU, términos y caracteres especiales.
public sealed class QuickBooksIifWriterTests
{
    private const string Tab = "\t";
    private static string R(params string[] cells) => string.Join(Tab, cells) + "\r\n";

    private static readonly string InvoiceHeaders =
        R("!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "NAMEISTAXABLE", "DUEDATE", "TERMS")
        + R("!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "QNTY", "PRICE", "INVITEM", "TAXABLE", "EXTRA")
        + R("!ENDTRNS");

    private static QuickBooksInvoice Simple(decimal tax = 2.30m, string? terms = "Net 30") => new(
        "FAC-00001", new DateOnly(2026, 11, 2), new DateOnly(2026, 12, 2), "Farmacia Central", terms, "OV-00012",
        new[] { new QuickBooksInvoiceLine("MED-1", "Guantes", 2m, 10m, true) }, tax);

    [Fact]
    public void An_invoice_is_written_exactly_with_its_tax_line_terms_and_due_date()
    {
        var iif = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { Simple() });
        var expected = InvoiceHeaders
            + R("TRNS", "", "INVOICE", "11/02/2026", "Accounts Receivable", "Farmacia Central", "22.30", "FAC-00001", "OV-00012", "N", "N", "Y", "12/02/2026", "Net 30")
            + R("SPL", "", "INVOICE", "11/02/2026", "Sales", "", "-20.00", "FAC-00001", "Guantes", "N", "-2", "10.00", "MED-1", "Y", "")
            + R("SPL", "", "INVOICE", "11/02/2026", "Sales Tax Payable", "", "-2.30", "FAC-00001", "IVU", "N", "", "", "IVU", "N", "AUTOSTAX")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
    }

    [Fact]
    public void Without_tax_or_terms_there_is_no_tax_line_and_the_terms_cell_is_empty()
    {
        var iif = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { Simple(0m, null) });
        var expected = InvoiceHeaders
            + R("TRNS", "", "INVOICE", "11/02/2026", "Accounts Receivable", "Farmacia Central", "20.00", "FAC-00001", "OV-00012", "N", "N", "N", "12/02/2026", "")
            + R("SPL", "", "INVOICE", "11/02/2026", "Sales", "", "-20.00", "FAC-00001", "Guantes", "N", "-2", "10.00", "MED-1", "Y", "")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
    }

    [Fact]
    public void Products_customers_and_invoices_go_in_that_order_with_the_configured_accounts()
    {
        var acc = new QuickBooksAccounts("AR", "Sales:Monitors", "COGS", "Inventory", "IVU por pagar", "IVU 11.5", "Envio");
        var items = new[] { new QuickBooksItem("NECH-1001", "Monitor de glucosa", 7.99m, 4.5m, true), new QuickBooksItem("24-081028", null, null, null, false) };
        var customers = new[]
        {
            new QuickBooksCustomer("Farmacia Nueva", "C-0042", "Farmacia Nueva LLC", new[] { "Farmacia Nueva LLC", "PO Box 55", "Ponce 00732" },
                new[] { "Farmacia Nueva LLC", "Calle Sol 10", "Ponce 00716" }, "7875551234 x12", "7875559876", "ana@farmacia.com", "Ana Rivera", "Net 30", "SURI-12345", 1500m),
        };
        var iif = QuickBooksIifWriter.Write(acc, items, customers, new[] { Simple() });
        var expected =
            R("!INVITEM", "NAME", "INVITEMTYPE", "DESC", "PURCHASEDESC", "ACCNT", "ASSETACCNT", "COGSACCNT", "PRICE", "COST", "TAXABLE")
            + R("INVITEM", "NECH-1001", "INVENTORY", "Monitor de glucosa", "Monitor de glucosa", "Sales:Monitors", "Inventory", "COGS", "7.99", "4.50", "Y")
            + R("INVITEM", "24-081028", "INVENTORY", "", "", "Sales:Monitors", "Inventory", "COGS", "", "", "N")
            + R("!CUST", "NAME", "COMPANYNAME", "BADDR1", "BADDR2", "BADDR3", "BADDR4", "BADDR5", "SADDR1", "SADDR2", "SADDR3", "SADDR4", "SADDR5",
                "PHONE1", "PHONE2", "EMAIL", "CONT1", "TERMS", "LIMIT", "RESALENUM", "NOTE")
            + R("CUST", "Farmacia Nueva", "Farmacia Nueva LLC", "Farmacia Nueva LLC", "PO Box 55", "Ponce 00732", "", "", "Farmacia Nueva LLC", "Calle Sol 10", "Ponce 00716", "", "",
                "7875551234 x12", "7875559876", "ana@farmacia.com", "Ana Rivera", "Net 30", "1500.00", "SURI-12345", "C-0042")
            + InvoiceHeaders
            + R("TRNS", "", "INVOICE", "11/02/2026", "AR", "Farmacia Central", "22.30", "FAC-00001", "OV-00012", "N", "N", "Y", "12/02/2026", "Net 30")
            + R("SPL", "", "INVOICE", "11/02/2026", "Sales:Monitors", "", "-20.00", "FAC-00001", "Guantes", "N", "-2", "10.00", "MED-1", "Y", "")
            + R("SPL", "", "INVOICE", "11/02/2026", "IVU por pagar", "", "-2.30", "FAC-00001", "IVU 11.5", "N", "", "", "IVU 11.5", "N", "AUTOSTAX")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
    }

    [Fact]
    public void A_kit_goes_broken_down_by_component_with_the_amount_prorated_by_list_price_closing_to_the_cent()
    {
        // OFERTA1 = 6 × 52800 ($6.99) + 1 × 070120 ($7.99), se vende a $41.94; 2 kits = $83.88 → 70.46 + 13.42
        var kit = new QuickBooksInvoiceLine("OFERTA1", "Oferta 1", 2m, 41.94m, true, new[]
        {
            new QuickBooksKitPart("52800", "Tirillas", 6m, 6.99m),
            new QuickBooksKitPart("070120", "Monitor", 1m, 7.99m),
        });
        var inv = new QuickBooksInvoice("FAC-00002", new DateOnly(2026, 11, 3), null, "Farmacia Central", null, null, new[] { kit }, 0m);
        var iif = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { inv });
        var expected = InvoiceHeaders
            + R("TRNS", "", "INVOICE", "11/03/2026", "Accounts Receivable", "Farmacia Central", "83.88", "FAC-00002", "", "N", "N", "N", "", "")
            + R("SPL", "", "INVOICE", "11/03/2026", "Sales", "", "-70.46", "FAC-00002", "Tirillas (OFERTA1)", "N", "-12", "5.87167", "52800", "Y", "")
            + R("SPL", "", "INVOICE", "11/03/2026", "Sales", "", "-13.42", "FAC-00002", "Monitor (OFERTA1)", "N", "-2", "6.71", "070120", "Y", "")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
        Assert.DoesNotContain("\tOFERTA1\t", iif);   // el kit no viaja como ítem
    }

    [Fact]
    public void Each_transaction_balances_to_zero()
    {
        var inv = Simple() with
        {
            Lines = new[]
            {
                new QuickBooksInvoiceLine("MED-1", "Guantes", 3m, 1.09m, true),
                new QuickBooksInvoiceLine("OFERTA1", null, 1m, 41.94m, true, new[] { new QuickBooksKitPart("A", null, 3m, 1m), new QuickBooksKitPart("B", null, 3m, 1m), new QuickBooksKitPart("C", null, 3m, 1m) }),
                new QuickBooksInvoiceLine("Shipping", "Cargo de envío", 1m, 12m, false),
            },
            TaxAmount = 5.20m,
        };
        var iif = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { inv });
        var amounts = iif.Split("\r\n").Where(l => l.StartsWith("TRNS\t") || l.StartsWith("SPL\t")).Select(l => decimal.Parse(l.Split('\t')[6], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(0m, amounts.Sum());
        Assert.Equal(3.27m + 41.94m + 12m + 5.20m, amounts[0]);
        // tres partes iguales de $41.94: 13.98 cada una
        Assert.Equal(new[] { 13.98m, 13.98m, 13.98m }, QuickBooksIifWriter.Detail(inv).Where(d => d.Item is "A" or "B" or "C").Select(d => d.Amount));
    }

    [Fact]
    public void Prorating_always_closes_to_the_cent()
    {
        var rnd = new Random(225);
        for (var n = 0; n < 500; n++)
        {
            var parts = rnd.Next(1, 8);
            var weights = Enumerable.Range(0, parts).Select(_ => Math.Round((decimal)rnd.NextDouble() * 50m, 2)).ToList();
            var amount = Math.Round((decimal)rnd.NextDouble() * 1000m, 2);
            var shares = QuickBooksRules.Prorate(amount, weights);
            Assert.Equal(amount, shares.Sum());
            Assert.All(shares, s => Assert.Equal(s, Math.Round(s, 2)));
        }
        Assert.Equal(new[] { 0.34m, 0.33m, 0.33m }, QuickBooksRules.Prorate(1m, new[] { 1m, 1m, 1m }));   // el centavo que sobra, al primero
        Assert.Equal(new[] { 5m, 5m }, QuickBooksRules.Prorate(10m, new[] { 0m, 0m }));   // sin precios de lista: a partes iguales
        Assert.Equal(new[] { -7.50m, -2.50m }, QuickBooksRules.Prorate(-10m, new[] { 3m, 1m }));   // el signo se respeta
        Assert.Throws<ArgumentException>(() => QuickBooksRules.Prorate(10m, Array.Empty<decimal>()));
    }

    [Fact]
    public void Special_characters_are_cleaned_and_names_lose_the_colon_but_accounts_keep_it()
    {
        Assert.Equal("Farmacia 'El Sol'- Ponce PR", QuickBooksIifWriter.Name("Farmacia \"El Sol\": Ponce\tPR"));
        Assert.Equal("línea 1 línea 2", QuickBooksIifWriter.Text("  línea 1\r\nlínea 2  "));
        Assert.Equal(string.Empty, QuickBooksIifWriter.Text(null));
        var acc = QuickBooksAccounts.Defaults with { Sales = "Sales:Global Cotton Balls 100ct" };
        var inv = new QuickBooksInvoice("FAC-3", new DateOnly(2026, 1, 5), null, "Peña: Café\t\"Niño\"", null, "Nota\ncon salto",
            new[] { new QuickBooksInvoiceLine("PD:23", "Guante\tazul", 1m, 1m, false) }, 0m);
        var iif = QuickBooksIifWriter.Write(acc, Array.Empty<QuickBooksItem>(), Array.Empty<QuickBooksCustomer>(), new[] { inv });
        var trns = iif.Split("\r\n").Single(l => l.StartsWith("TRNS\t")).Split('\t');
        var spl = iif.Split("\r\n").Single(l => l.StartsWith("SPL\t")).Split('\t');
        Assert.Equal(14, trns.Length);   // ningún tabulador de más rompe las columnas
        Assert.Equal(15, spl.Length);
        Assert.Equal("Peña- Café 'Niño'", trns[5]);
        Assert.Equal("Nota con salto", trns[8]);
        Assert.Equal("Sales:Global Cotton Balls 100ct", spl[4]);
        Assert.Equal("PD-23", spl[12]);
        Assert.Equal("Guante azul", spl[8]);
        Assert.Equal(6, iif.Split("\r\n").Length - 1);   // 3 encabezados + TRNS + SPL + ENDTRNS, cada uno con CRLF (sin IVU no hay su renglón)
    }

    [Fact]
    public void The_file_is_windows_1252_and_what_does_not_fit_is_a_question_mark()
    {
        var bytes = QuickBooksIifWriter.Encode("Peña Café ✓");
        Assert.Equal(new byte[] { 0x50, 0x65, 0xF1, 0x61, 0x20, 0x43, 0x61, 0x66, 0xE9, 0x20, 0x3F }, bytes);
        Assert.Matches(@"^teikem-quickbooks-20261107-0705\.iif$", QuickBooksIifWriter.FileName(new DateTime(2026, 11, 7, 7, 5, 0)));
        Assert.Equal("1234.50", QuickBooksIifWriter.Money(1234.5m));
        Assert.Equal("-0.01", QuickBooksIifWriter.Money(-0.005m));
    }

    [Fact]
    public void Terms_map_and_defaults()
    {
        var map = QuickBooksRules.ParseTerms("{\"net30\":\"Net 30\",\"COD\":\" \",\"NET15\":\"Net 15\"}");
        Assert.Equal(2, map.Count);
        Assert.Equal("Net 30", map["NET30"]);
        Assert.Equal("Net 30", map["net30"]);   // sin distinguir mayúsculas
        Assert.Empty(QuickBooksRules.ParseTerms("{dañado"));
        Assert.Empty(QuickBooksRules.ParseTerms(null));
        Assert.Equal("{\"NET15\":\"Net 15\",\"NET30\":\"Net 30\"}", QuickBooksRules.SerializeTerms(map));
        Assert.Null(QuickBooksRules.SerializeTerms(new Dictionary<string, string>()));
        Assert.Equal("Sales", QuickBooksRules.Effective("  ", QuickBooksRules.DefaultSalesAccount));
        Assert.Equal("Ventas", QuickBooksRules.Effective(" Ventas ", QuickBooksRules.DefaultSalesAccount));
    }
}
