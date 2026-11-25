using System.Globalization;
using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

// Lote 246 (Lote F, P2): el generador PURO del archivo IIF de órdenes de compra (PURCHORD) — archivo exacto, bloques !INVITEM / !VEND opcionales, cuadre a 0.00,
// redondeo por línea, limpieza de textos y caracteres fuera de Windows-1252. El formato aún no se ha probado contra QuickBooks real (pendiente F-15).
public sealed class QuickBooksIifPurchaseOrderTests
{
    private static string R(params string[] cells) => string.Join("\t", cells) + "\r\n";

    private static readonly string PoHeaders =
        R("!TRNS", "TRNSID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "TOPRINT", "DUEDATE")
        + R("!SPL", "SPLID", "TRNSTYPE", "DATE", "ACCNT", "NAME", "AMOUNT", "DOCNUM", "MEMO", "CLEAR", "QNTY", "PRICE", "INVITEM")
        + R("!ENDTRNS");

    private static QuickBooksPurchaseOrder Simple(DateOnly? expected = null) => new(
        "PO-00012", new DateOnly(2026, 11, 20), expected ?? new DateOnly(2026, 12, 1), "Distribuidora Caribe", "Entregar en el muelle 2",
        new[]
        {
            new QuickBooksPurchaseOrderLine("MED-1", "Guantes de nitrilo", 10m, 4.5m),
            new QuickBooksPurchaseOrderLine("MED-2", "Mascarillas", 2.5m, 3.3333m),
        });

    private static IReadOnlyList<QuickBooksItem> NoItems => Array.Empty<QuickBooksItem>();
    private static IReadOnlyList<QuickBooksVendor> NoVendors => Array.Empty<QuickBooksVendor>();

    [Fact]
    public void A_purchase_order_is_written_exactly_with_negative_header_and_positive_lines()
    {
        var iif = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, new[] { Simple() });
        // 10 × 4.50 = 45.00; 2.5 × 3.3333 = 8.33325 → 8.33; total 53.33
        var expected = PoHeaders
            + R("TRNS", "", "PURCHORD", "11/20/2026", "Purchase Orders", "Distribuidora Caribe", "-53.33", "PO-00012", "Entregar en el muelle 2", "N", "N", "12/01/2026")
            + R("SPL", "", "PURCHORD", "11/20/2026", "Inventory Asset", "", "45.00", "PO-00012", "Guantes de nitrilo", "N", "10", "4.50", "MED-1")
            + R("SPL", "", "PURCHORD", "11/20/2026", "Inventory Asset", "", "8.33", "PO-00012", "Mascarillas", "N", "2.5", "3.3333", "MED-2")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
    }

    [Fact]
    public void Without_expected_date_or_memo_those_cells_are_empty()
    {
        var po = Simple() with { ExpectedDate = null, Memo = null };
        var iif = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, new[] { po });
        var trns = iif.Split("\r\n").Single(l => l.StartsWith("TRNS\t")).Split('\t');
        Assert.Equal(12, trns.Length);
        Assert.Equal(string.Empty, trns[8]);
        Assert.Equal(string.Empty, trns[11]);
    }

    [Fact]
    public void Items_vendors_and_orders_go_in_that_order_with_the_configured_accounts()
    {
        var acc = new QuickBooksAccounts("AR", "Sales", "COGS", "Inventario:Almacén", "IVU", "IVU", "Envio", "Órdenes de compra");
        var items = new[] { new QuickBooksItem("MED-1", "Guantes de nitrilo", 7.99m, 4.5m, true) };
        var vendors = new[] { new QuickBooksVendor("Distribuidora: Caribe", "Distribuidora Caribe LLC", "Ana Rivera", "+1 7875551234", "ventas@caribe.com", "Net 30", "PROV-0007") };
        var po = Simple() with { VendorName = "Distribuidora: Caribe", Lines = new[] { new QuickBooksPurchaseOrderLine("MED-1", "Guantes de nitrilo", 1m, 4.5m) } };
        var iif = QuickBooksIifWriter.WritePurchaseOrders(acc, items, vendors, new[] { po });
        var expected =
            R("!INVITEM", "NAME", "INVITEMTYPE", "DESC", "PURCHASEDESC", "ACCNT", "ASSETACCNT", "COGSACCNT", "PRICE", "COST", "TAXABLE")
            + R("INVITEM", "MED-1", "INVENTORY", "Guantes de nitrilo", "Guantes de nitrilo", "Sales", "Inventario:Almacén", "COGS", "7.99", "4.50", "Y")
            + R("!VEND", "NAME", "COMPANYNAME", "CONT1", "PHONE1", "EMAIL", "TERMS", "NOTE")
            + R("VEND", "Distribuidora- Caribe", "Distribuidora Caribe LLC", "Ana Rivera", "+1 7875551234", "ventas@caribe.com", "Net 30", "PROV-0007")
            + PoHeaders
            + R("TRNS", "", "PURCHORD", "11/20/2026", "Órdenes de compra", "Distribuidora- Caribe", "-4.50", "PO-00012", "Entregar en el muelle 2", "N", "N", "12/01/2026")
            + R("SPL", "", "PURCHORD", "11/20/2026", "Inventario:Almacén", "", "4.50", "PO-00012", "Guantes de nitrilo", "N", "1", "4.50", "MED-1")
            + R("ENDTRNS");
        Assert.Equal(expected, iif);
    }

    [Fact]
    public void The_item_block_is_byte_identical_to_the_invoice_file()
    {
        var items = new[] { new QuickBooksItem("NECH-1001", "Monitor", 7.99m, 4.5m, true), new QuickBooksItem("24:081028", null, null, null, false) };
        var invoices = QuickBooksIifWriter.Write(QuickBooksAccounts.Defaults, items, Array.Empty<QuickBooksCustomer>(), Array.Empty<QuickBooksInvoice>());
        var purchases = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, items, NoVendors, Array.Empty<QuickBooksPurchaseOrder>());
        Assert.Equal(invoices, purchases);
        Assert.StartsWith("!INVITEM\t", purchases);
    }

    [Fact]
    public void Empty_blocks_are_omitted_and_nothing_gives_an_empty_file()
    {
        Assert.Equal(string.Empty, QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, Array.Empty<QuickBooksPurchaseOrder>()));
        var iif = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, new[] { Simple() });
        Assert.DoesNotContain("!INVITEM", iif);
        Assert.DoesNotContain("!VEND", iif);
        var onlyVendor = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems,
            new[] { new QuickBooksVendor("Prov", null, null, null, null, null, null) }, Array.Empty<QuickBooksPurchaseOrder>());
        Assert.Equal(R("!VEND", "NAME", "COMPANYNAME", "CONT1", "PHONE1", "EMAIL", "TERMS", "NOTE") + R("VEND", "Prov", "", "", "", "", "", ""), onlyVendor);
    }

    [Fact]
    public void Each_transaction_balances_to_zero_even_with_awkward_rounding()
    {
        var rnd = new Random(246);
        var orders = Enumerable.Range(1, 40).Select(n => new QuickBooksPurchaseOrder($"PO-{n:00000}", new DateOnly(2026, 11, 1), null, $"Prov {n}", null,
            Enumerable.Range(0, rnd.Next(1, 9)).Select(i => new QuickBooksPurchaseOrderLine($"SKU-{i}", null,
                Math.Round((decimal)rnd.NextDouble() * 500m, 4), Math.Round((decimal)rnd.NextDouble() * 90m, 4))).ToList())).ToList();
        var iif = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, orders);

        var sum = 0m;
        var transactions = 0;
        foreach (var line in iif.Split("\r\n"))
        {
            var cells = line.Split('\t');
            if (cells[0] is "TRNS" or "SPL") sum += decimal.Parse(cells[6], CultureInfo.InvariantCulture);
            if (cells[0] == "ENDTRNS") { Assert.Equal(0m, sum); sum = 0m; transactions++; }
        }
        Assert.Equal(40, transactions);
        foreach (var po in orders)
        {
            var trns = iif.Split("\r\n").Single(l => l.StartsWith("TRNS\t") && l.Split('\t')[7] == po.Number).Split('\t');
            Assert.Equal(-QuickBooksIifWriter.PurchaseOrderFileTotal(po), decimal.Parse(trns[6], CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void The_file_total_is_the_sum_of_lines_rounded_to_cents_and_can_differ_from_rounding_at_the_end()
    {
        // tres líneas de 1 × 0.3333: Teikem (4 decimales) suma 0.9999 → 1.00; el archivo 0.33 × 3 = 0.99
        var po = Simple() with { Lines = Enumerable.Repeat(new QuickBooksPurchaseOrderLine("A", "a", 1m, 0.3333m), 3).ToList() };
        var detail = QuickBooksIifWriter.PurchaseOrderDetail(po);
        Assert.All(detail, d => Assert.Equal(0.33m, d.Amount));
        Assert.Equal(0.99m, QuickBooksIifWriter.PurchaseOrderFileTotal(po));
        Assert.NotEqual(Math.Round(po.Lines.Sum(l => l.Quantity * l.UnitCost), 2), QuickBooksIifWriter.PurchaseOrderFileTotal(po));
        Assert.Equal(("A", (string?)"a", 1m, 0.3333m, 0.33m), detail[0]);
        Assert.Equal(0.01m, QuickBooksRules.LineAmount(1m, 0.005m));   // medio centavo se va hacia arriba, como en las facturas
    }

    [Fact]
    public void Quantities_go_positive_with_up_to_four_decimals_and_prices_with_the_invariant_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");   // coma decimal: no debe colarse en el archivo
            var po = Simple() with { Lines = new[] { new QuickBooksPurchaseOrderLine("A", null, 1234.5678m, 1.23456m), new QuickBooksPurchaseOrderLine("B", null, 3m, 0m) } };
            var spl = QuickBooksIifWriter.WritePurchaseOrders(QuickBooksAccounts.Defaults, NoItems, NoVendors, new[] { po })
                .Split("\r\n").Where(l => l.StartsWith("SPL\t")).Select(l => l.Split('\t')).ToList();
            Assert.Equal(13, spl[0].Length);
            Assert.Equal("1234.5678", spl[0][10]);
            Assert.Equal("1.23456", spl[0][11]);
            Assert.Equal("1524.15", spl[0][6]);   // 1234.5678 × 1.23456 = 1524.1514… → 1524.15
            Assert.Equal("3", spl[1][10]);
            Assert.Equal("0.00", spl[1][11]);
            Assert.Equal("0.00", spl[1][6]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Special_characters_are_cleaned_and_names_lose_the_colon_but_accounts_keep_it()
    {
        var acc = QuickBooksAccounts.Defaults with { PurchaseOrders = "Purchase Orders:Compras", Inventory = "Inventory Asset:Almacén 1" };
        var po = new QuickBooksPurchaseOrder("PO-3", new DateOnly(2026, 1, 5), null, "Peña: Café\t\"Niño\"", "Nota\ncon salto",
            new[] { new QuickBooksPurchaseOrderLine("PD:23", "Guante\tazul", 1m, 1m) });
        var iif = QuickBooksIifWriter.WritePurchaseOrders(acc, NoItems, NoVendors, new[] { po });
        var trns = iif.Split("\r\n").Single(l => l.StartsWith("TRNS\t")).Split('\t');
        var spl = iif.Split("\r\n").Single(l => l.StartsWith("SPL\t")).Split('\t');
        Assert.Equal(12, trns.Length);   // ningún tabulador de más rompe las columnas
        Assert.Equal(13, spl.Length);
        Assert.Equal("Purchase Orders:Compras", trns[4]);
        Assert.Equal("Peña- Café 'Niño'", trns[5]);
        Assert.Equal("Nota con salto", trns[8]);
        Assert.Equal("Inventory Asset:Almacén 1", spl[4]);
        Assert.Equal("Guante azul", spl[8]);
        Assert.Equal("PD-23", spl[12]);
        Assert.Equal(6, iif.Split("\r\n").Length - 1);   // 3 encabezados + TRNS + SPL + ENDTRNS, cada uno con CRLF
    }

    [Fact]
    public void Unencodable_chars_are_the_distinct_ones_that_windows_1252_cannot_write()
    {
        Assert.Equal(string.Empty, QuickBooksIifWriter.UnencodableChars(null));
        Assert.Equal(string.Empty, QuickBooksIifWriter.UnencodableChars("Peña Café Niño ¿qué? € ñÑáéíóúü"));
        Assert.Equal("✓😀", QuickBooksIifWriter.UnencodableChars("ok ✓ ✓ 😀 listo ✓"));
        Assert.Equal(string.Empty, QuickBooksIifWriter.UnencodableChars("Žižek – “comillas” …"));   // Ž, ž, raya, comillas tipográficas y elipsis sí caben en 1252
        // «ā» no cabe, pero Windows-1252 la escribe como «a» (best fit): no sale como «?» y no se avisa
        Assert.Equal(string.Empty, QuickBooksIifWriter.UnencodableChars("Māori"));
        Assert.Equal(new byte[] { (byte)'?', (byte)'a' }, QuickBooksIifWriter.Encode("✓ā"));
        Assert.Equal("✓", QuickBooksIifWriter.UnencodableChars("¿✓?"));   // un «?» de verdad no cuenta
    }

    [Fact]
    public void Rules_file_name_and_defaults()
    {
        Assert.Equal("Purchase Orders", QuickBooksAccounts.Defaults.PurchaseOrders);
        Assert.Equal("Purchase Orders", new QuickBooksAccounts("a", "b", "c", "d", "e", "f", "g").PurchaseOrders);   // los new(...) de siete valores compilan igual
        Assert.Equal(41, QuickBooksRules.MaxVendorLength);
        Assert.Equal("El nombre del proveedor «Proveedor X» pasa de 41 caracteres: QuickBooks no lo acepta como nombre de proveedor.",
            QuickBooksRules.VendorNameTooLong("Proveedor X"));
        Assert.Equal("teikem-quickbooks-compras-20261107-0705.iif", QuickBooksIifWriter.PurchaseOrderFileName(new DateTime(2026, 11, 7, 7, 5, 0)));
    }
}
