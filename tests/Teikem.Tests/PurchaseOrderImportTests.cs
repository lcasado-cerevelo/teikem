using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 239 (2026-11-12) — importar órdenes de compra (genérico y QuickBooks). Reglas puras (clave de SKU, celda «SKU (Descripción)», fechas y números) y el servicio contra InMemory:
/// una orden de compra no mueve inventario; producto o proveedor que no existe → la orden se bloquea y se dice por qué; los SKU de QuickBooks y de Teikem se igualan sin guiones ni
/// espacios; importar dos veces no duplica; los recibos de QuickBooks se omiten.
/// </summary>
public sealed class PurchaseOrderImportTests
{
    // Un extracto REAL de «Solutions Purchases.CSV» (QuickBooks Desktop): primera columna vacía, encabezado de proveedor, filas «Total», recibo y factura con la línea repetida y con costo vacío.
    private const string QuickBooksCsv =
        ",\"Type\",\"Date\",\"Num\",\"Source Name\",\"Memo\",\"Adj\",\"P. O. #\",\"Name\",\"Ship Date\",\"Deliv Date\",\"Due Date\",\"Billed Date\",\"Paid Through\",\"Item\",\"Item Description\",\"SSN/Tax ID\",\"Account\",\"Qty\",\"Cost Price\",\"Debit\",\"Credit\",\"Amount\",\"Balance\"\r\n" +
        "\"ADVANCE LOGISTICS LLC\",,,,,,,,,,,,,,,,,,,,,,,\r\n" +
        ",\"Bill\",\"04/20/2026\",\"PDC04132026\",\"ADVANCE LOGISTICS LLC\",\"PRODIGY STRIPS\",,,\"ADVANCE LOGISTICS LLC\",,,\"04/30/2026\",,,\"52800 (PRODIGY STRIPS 1/50CT.)\",\"PRODIGY STRIPS 1/50CT.\",,\"Inventory Asset\",15000,3.05,45750.00,\"\",45750.00,45750.00\r\n" +
        ",\"Bill\",\"04/20/2026\",\"PDC04132026\",\"ADVANCE LOGISTICS LLC\",\"MONITOR\",,,\"ADVANCE LOGISTICS LLC\",,,\"04/30/2026\",,,\"070120 (PRODIGY MONITOR 1EA.)\",\"PRODIGY MONITOR 1EA.\",,\"Inventory Asset\",2000,5.25,10500.00,\"\",10500.00,56250.00\r\n" +
        ",\"Bill\",\"04/20/2026\",\"PDC04132026\",\"ADVANCE LOGISTICS LLC\",\"MONITOR\",,,\"ADVANCE LOGISTICS LLC\",,,\"04/30/2026\",,,\"070120 (PRODIGY MONITOR 1EA.)\",\"PRODIGY MONITOR 1EA.\",,\"Inventory Asset\",600,\"\",0.00,\"\",0.00,56250.00\r\n" +
        "\"Total ADVANCE LOGISTICS LLC\",,,,,,,,,,,,,,,,,,17600,,56250.00,0.00,56250.00,56250.00\r\n" +
        "\"PADIMONT\",,,,,,,,,,,,,,,,,,,,,,,\r\n" +
        ",\"Bill\",\"05/02/2026\",\"PAD-77\",\"PADIMONT\",\"GLOVES\",,,\"PADIMONT\",,,\"05/12/2026\",,,\"NECH 1001 (Padimont Gloves Small 1/100ct.)\",\"Padimont Gloves Small\",,\"Inventory Asset\",3600,4.1,14760.00,\"\",14760.00,14760.00\r\n" +
        ",\"Bill\",\"05/02/2026\",\"PAD-77\",\"PADIMONT\",\"BOXES\",,,\"PADIMONT\",,,\"05/12/2026\",,,\"101010 (CAJA CORRUGADA 10 X 10 X 10)\",\"CAJA CORRUGADA 10 X 10 X 10\",,\"Inventory Asset\",200,0.42,84.00,\"\",84.00,14844.00\r\n" +
        ",\"Item Receipt\",\"05/03/2026\",\"\",\"PADIMONT\",\"RECIBO\",,,\"PADIMONT\",,,,,,\"NECH 1001 (Padimont Gloves Small 1/100ct.)\",\"Padimont Gloves Small\",,\"Inventory Asset\",3600,4.1,14760.00,\"\",14760.00,14760.00\r\n" +
        "\"Total PADIMONT\",,,,,,,,,,,,,,,,,,3800,,14844.00,0.00,14844.00,14844.00\r\n";

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static Task<WmsFixture> CreateAsync() => WmsFixture.CreateAsync(s =>
    {
        s.AddSingleton<PurchaseOrderService>();
        s.AddSingleton<PurchaseOrderImportService>();
    });

    private static async Task AddSupplierAsync(WmsFixture f, string name, bool isActive = true)
    {
        f.Db.Set<Supplier>().Add(new Supplier { TenantId = WmsFixture.TenantId, Name = name, IsActive = isActive });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- reglas puras

    [Theory]
    [InlineData("NECH 1001", "NECH1001")]
    [InlineData("NECH-1001", "NECH1001")]
    [InlineData("PD-23*36/15/10", "PD23361510")]
    [InlineData("PD-23-36/15/10", "PD23361510")]
    [InlineData("  ", "")]
    public void SkuKey_ignora_guiones_espacios_y_signos(string sku, string expected) => Assert.Equal(expected, PurchaseOrderImportRules.SkuKey(sku));

    [Fact]
    public void Celda_Item_de_QuickBooks_separa_sku_y_descripcion()
    {
        Assert.Equal(("979", "CAJA CORRUGADA 9 X 7 X 9"), PurchaseOrderImportRules.SplitQuickBooksItem("979 (CAJA CORRUGADA 9 X 7 X 9)"));
        Assert.Equal(("53310/53350", "PRODIGY CONTROL SOLUTION LOW/HIGH 4ML"), PurchaseOrderImportRules.SplitQuickBooksItem("53310/53350 (PRODIGY CONTROL SOLUTION LOW/HIGH 4ML)"));
        Assert.Equal(("SOLO-SKU", ""), PurchaseOrderImportRules.SplitQuickBooksItem("SOLO-SKU"));
    }

    [Fact]
    public void Fechas_y_numeros_del_archivo()
    {
        Assert.Equal(new DateOnly(2026, 6, 10), PurchaseOrderImportRules.ParseDate("06/10/2026"));
        Assert.Equal(new DateOnly(2026, 6, 10), PurchaseOrderImportRules.ParseDate("2026-06-10"));
        Assert.Null(PurchaseOrderImportRules.ParseDate("ayer"));
        Assert.Equal(1234.5m, PurchaseOrderImportRules.ParseNumber("$1,234.50"));
        Assert.Null(PurchaseOrderImportRules.ParseNumber(""));
    }

    [Fact]
    public void SimilarSkus_sugiere_los_que_se_parecen()
        => Assert.Equal(new[] { "53310", "53350" }, PurchaseOrderImportRules.SimilarSkus("53310/53350", new[] { "53310", "53350", "00052-1" }));

    // ---------------------------------------------------------------- servicio

    [Fact]
    public async Task QuickBooks_bloquea_la_orden_sin_producto_importa_la_completa_y_omite_el_recibo()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ADVANCE LOGISTICS LLC");
        await AddSupplierAsync(f, "PADIMONT");
        await f.AddProductAsync("52800");
        await f.AddProductAsync("070120", purchaseCost: 5.25m);
        await f.AddProductAsync("NECH-1001");   // en QuickBooks es «NECH 1001»
        var import = f.Get<PurchaseOrderImportService>();

        var preview = await import.ImportAsync("QUICKBOOKS", Bytes(QuickBooksCsv), "Solutions Purchases.CSV", commit: false, "DRAFT", null, default);

        Assert.False(preview.Committed);
        Assert.Equal(3, preview.TotalDocuments);                    // PDC04132026, PAD-77 y el recibo omitido
        var ready = Assert.Single(preview.Documents, d => d.Number == "PDC04132026");
        Assert.Equal("READY", ready.Status);
        Assert.Equal(2, ready.Lines);                              // el monitor venía en 2 líneas: se sumó
        Assert.Contains(ready.Issues, i => i.Code == PurchaseOrderImportRules.IssueMergedLines);
        var blocked = Assert.Single(preview.Documents, d => d.Number == "PAD-77");
        Assert.Equal("BLOCKED", blocked.Status);
        Assert.Contains(blocked.Issues, i => i.Blocking && i.Code == PurchaseOrderImportRules.IssueMissingProduct && i.Sku == "101010");
        Assert.Contains(blocked.Issues, i => i.Code == PurchaseOrderImportRules.IssueAlias);   // NECH 1001 → NECH-1001
        var skipped = Assert.Single(preview.Documents, d => d.Status == "SKIPPED");
        Assert.Contains("Item Receipt", skipped.Issues[0].Message);
        var missing = Assert.Single(preview.MissingProducts);
        Assert.Equal(("101010", 200m), (missing.Sku, missing.Qty));
        Assert.Equal(1, preview.Ready);
        Assert.Equal(1, preview.Blocked);
        Assert.Equal(0, await f.Db.Set<PurchaseOrder>().CountAsync());   // la vista previa no guardó nada
    }

    [Fact]
    public async Task Importar_crea_en_borrador_o_enviada_no_mueve_inventario_y_no_duplica()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ADVANCE LOGISTICS LLC");
        await AddSupplierAsync(f, "PADIMONT");
        await f.AddProductAsync("52800");
        await f.AddProductAsync("070120");
        await f.AddProductAsync("NECH-1001");
        await f.AddProductAsync("101010");
        var import = f.Get<PurchaseOrderImportService>();

        var done = await import.ImportAsync("QUICKBOOKS", Bytes(QuickBooksCsv), "x.csv", commit: true, "SENT", null, default);

        Assert.Equal(2, done.Created);
        var po = await f.Db.Set<PurchaseOrder>().Include(p => p.Lines).SingleAsync(p => p.Notes!.Contains("PDC04132026"));
        Assert.Equal(PurchaseOrderStatuses.Sent, await PurchasingSupport.StatusCodeOfAsync(f.Db, po.StatusCodeId, default));
        Assert.Equal(new DateOnly(2026, 4, 20), po.OrderDate);
        Assert.Equal(new[] { 15000m, 2600m }, po.Lines.OrderBy(l => l.PurchaseOrderLineId).Select(l => l.QtyOrdered).ToArray());
        Assert.All(po.Lines, l => Assert.Equal(0m, l.QtyReceived));          // una orden de compra no recibe nada
        Assert.Equal(0, await f.Db.Set<InventoryTransaction>().CountAsync()); // ni mueve inventario

        var again = await import.ImportAsync("QUICKBOOKS", Bytes(QuickBooksCsv), "x.csv", commit: true, "SENT", null, default);
        Assert.Equal(0, again.Created);
        Assert.Equal(2, again.Duplicates);
        Assert.Equal(2, await f.Db.Set<PurchaseOrder>().CountAsync());
    }

    [Fact]
    public async Task Proveedor_que_no_existe_bloquea_la_orden_y_se_lista()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await f.AddProductAsync("52800");
        var csv = "Supplier,Number,Date,SKU,Qty,Cost\r\nACME NUEVO,A-1,2026-11-01,52800,10,1.50\r\n";

        var r = await f.Get<PurchaseOrderImportService>().ImportAsync("GENERIC", Bytes(csv), "ordenes.csv", commit: true, "DRAFT", null, default);

        var d = Assert.Single(r.Documents);
        Assert.Equal("BLOCKED", d.Status);
        Assert.Contains(d.Issues, i => i.Code == PurchaseOrderImportRules.IssueMissingSupplier && i.Message.Contains("ACME NUEVO"));
        Assert.Equal(new[] { "ACME NUEVO" }, r.MissingSuppliers);
        Assert.Equal(0, r.Created);
    }

    [Fact]
    public async Task Formato_generico_agrupa_por_proveedor_y_numero_y_respeta_el_estatus_borrador()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        await f.AddProductAsync("B-2");
        var csv = "Supplier,Number,Date,SKU,Qty,Cost\r\nACME,PO-9,2026-11-01,A-1,10,\r\nACME,PO-9,2026-11-01,B 2,5,3.25\r\n";

        var r = await f.Get<PurchaseOrderImportService>().ImportAsync("GENERIC", Bytes(csv), "o.csv", commit: true, "DRAFT", null, default);

        Assert.Equal(1, r.Created);
        var po = await f.Db.Set<PurchaseOrder>().Include(p => p.Lines).SingleAsync();
        Assert.Equal(PurchaseOrderStatuses.Draft, await PurchasingSupport.StatusCodeOfAsync(f.Db, po.StatusCodeId, default));
        Assert.Equal(2, po.Lines.Count);
        Assert.Equal(new[] { 2m, 3.25m }, po.Lines.OrderBy(l => l.PurchaseOrderLineId).Select(l => l.UnitCost).ToArray());   // el costo vacío toma el del producto
    }

    [Fact]
    public async Task Origen_o_estatus_invalidos_se_rechazan()
    {
        await using var f = await CreateAsync();
        var import = f.Get<PurchaseOrderImportService>();
        var s = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("EXCEL", Bytes("x"), "x.csv", false, "DRAFT", null, default));
        Assert.Equal(PurchaseOrderImportRules.SourceUnknown, s.Message);
        var st = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("GENERIC", Bytes("x"), "x.csv", false, "RECEIVED", null, default));
        Assert.Equal(PurchaseOrderImportRules.StatusUnknown, st.Message);
    }
}
