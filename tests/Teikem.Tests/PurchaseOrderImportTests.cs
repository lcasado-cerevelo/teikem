using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 239 (2026-11-12) — importar órdenes de compra (genérico y QuickBooks). Reglas puras (clave de SKU, celda «SKU (Descripción)», fechas y números) y el servicio contra InMemory:
/// una orden de compra no mueve inventario; producto o proveedor que no existe → la orden se bloquea y se dice por qué; los SKU de QuickBooks y de Teikem se igualan sin guiones ni
/// espacios; importar dos veces no duplica; los recibos de QuickBooks se omiten.
/// Lote 246 (Lote F): mensaje de origen con TEMPLATE; origen e ImportKey de la orden creada; SENT atómico; no duplica por ImportKey aunque se borre la marca de las notas;
/// COST_CONFLICT en GENERIC y QUICKBOOKS; Supplier.QuickBooksSentAt solo con el nombre exacto; «ir y venir» de lo que salió de Teikem a QuickBooks.
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
        s.AddSingleton<ImportTemplateService>();
        s.AddSingleton<PurchaseOrderImportService>();
    });

    private static async Task<Supplier> AddSupplierAsync(WmsFixture f, string name, bool isActive = true)
    {
        var supplier = new Supplier { TenantId = WmsFixture.TenantId, Name = name, IsActive = isActive };
        f.Db.Set<Supplier>().Add(supplier);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return supplier;
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
        Assert.Equal("El origen de la importación no es válido: use GENERIC, QUICKBOOKS o TEMPLATE.", s.Message);
        var st = await Assert.ThrowsAsync<ValidationException>(() => import.ImportAsync("GENERIC", Bytes("x"), "x.csv", false, "RECEIVED", null, default));
        Assert.Equal(PurchaseOrderImportRules.StatusUnknown, st.Message);
    }

    // ---------------------------------------------------------------- Lote 246 (Lote F)

    [Fact]
    public void Identidad_y_origen_de_la_importacion()
    {
        Assert.Equal("QB:ACME|PO-9", PurchaseOrderImportRules.ImportKey("QUICKBOOKS", "Acme", "po-9", null));
        Assert.Equal("IMP:ACME|PO-9", PurchaseOrderImportRules.ImportKey("GENERIC", "ACME", "PO-9", null));
        Assert.Equal("IMP:ACME|PO-9", PurchaseOrderImportRules.ImportKey("TEMPLATE", "ACME", "PO-9", null));   // GENERIC y TEMPLATE comparten identidad
        Assert.Equal("IMP:ACME|20261101", PurchaseOrderImportRules.ImportKey("GENERIC", "ACME", null, new DateOnly(2026, 11, 1)));
        Assert.Equal(250, PurchaseOrderImportRules.ImportKey("GENERIC", new string('A', 300), "1", null).Length);
        Assert.Equal("[QB:ACME|PO-9]", PurchaseOrderImportRules.Marker("QUICKBOOKS", "ACME", "PO-9", null));
        Assert.Equal((PurchaseOrderOrigins.QuickBooks, PurchaseOrderOrigins.Excel, PurchaseOrderOrigins.Excel),
            (PurchaseOrderImportRules.OriginOf("QUICKBOOKS"), PurchaseOrderImportRules.OriginOf("GENERIC"), PurchaseOrderImportRules.OriginOf("TEMPLATE")));
        Assert.Equal("TEMPLATE", PurchaseOrderImportRules.NormalizeSource(" template "));
        Assert.Equal("Importada con la plantilla «Excel compras» 1045. [IMP:ACME|1045]", PurchaseOrderImportRules.BuildNotes("TEMPLATE", "ACME", "1045", null, "Excel compras"));
        Assert.Equal("Importada de QuickBooks PO-9. [QB:ACME|PO-9]", PurchaseOrderImportRules.BuildNotes("QUICKBOOKS", "ACME", "PO-9", null));
        Assert.Equal("Importada de archivo PO-9. [IMP:ACME|PO-9]", PurchaseOrderImportRules.BuildNotes("GENERIC", "ACME", "PO-9", null));
        Assert.Equal("El producto A-1 viene en varias líneas de la orden con costos distintos (1, 1.1); corrija el archivo para que todas lleven el mismo costo.",
            PurchaseOrderImportRules.CostConflict("A-1", new[] { 1.00m, 1.10m }));
        Assert.Equal("La orden PO-00007 nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.", PurchaseOrderImportRules.CameFromTeikem("PO-00007"));
    }

    [Fact]
    public async Task QuickBooks_crea_con_origen_e_identidad_enviada_atomica_y_marca_el_proveedor_solo_con_el_nombre_exacto()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        var advance = await AddSupplierAsync(f, "Advance Logistics LLC");   // mismo nombre sin distinguir mayúsculas
        var padimont = await AddSupplierAsync(f, "PADIMONT.");             // solo coincide la clave sin signos
        await f.AddProductAsync("52800");
        await f.AddProductAsync("070120");
        await f.AddProductAsync("NECH-1001");
        await f.AddProductAsync("101010");

        var done = await f.Get<PurchaseOrderImportService>().ImportAsync("QUICKBOOKS", Bytes(QuickBooksCsv), "x.csv", commit: true, "SENT", null, default);

        Assert.Equal(2, done.Created);
        Assert.Null(done.TemplatePublicId);
        f.Db.ChangeTracker.Clear();
        var po = await f.Db.Set<PurchaseOrder>().AsNoTracking().SingleAsync(p => p.SupplierId == advance.SupplierId);
        Assert.Equal(("QB:ADVANCELOGISTICSLLC|PDC04132026", (int?)f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.QuickBooks)), (po.ImportKey, po.OriginLookupId));
        Assert.Equal(new[] { PurchaseOrderStatuses.Draft, PurchaseOrderStatuses.Sent }, await f.HistoryCodesAsync(EntityTypes.PurchaseOrder, po.PurchaseOrderId));
        var sentComment = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == po.PurchaseOrderId).OrderBy(h => h.EntityStatusHistoryId).Select(h => h.Comment).ToListAsync();
        Assert.Equal(PurchaseOrderImportService.SentComment, sentComment[1]);
        var dto = await f.Get<PurchaseOrderService>().GetAsync(po.PublicId, default);
        Assert.Equal((PurchaseOrderOrigins.QuickBooks, "FROM_QUICKBOOKS"), (dto.OriginCode, dto.QuickBooksState));
        Assert.NotNull((await f.Db.Set<Supplier>().AsNoTracking().SingleAsync(s => s.SupplierId == advance.SupplierId)).QuickBooksSentAt);
        Assert.Null((await f.Db.Set<Supplier>().AsNoTracking().SingleAsync(s => s.SupplierId == padimont.SupplierId)).QuickBooksSentAt);
    }

    [Fact]
    public async Task No_duplica_por_la_identidad_aunque_se_borre_la_marca_de_las_notas()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        var csv = "Supplier,Number,Date,SKU,Qty,Cost\r\nACME,PO-9,2026-11-01,A-1,10,2\r\n";
        var import = f.Get<PurchaseOrderImportService>();

        var first = await import.ImportAsync("GENERIC", Bytes(csv), "o.csv", commit: true, "DRAFT", null, default);
        Assert.Equal(1, first.Created);
        var po = await f.Db.Set<PurchaseOrder>().SingleAsync();
        Assert.Equal(("IMP:ACME|PO-9", (int?)f.LookupId(PurchaseOrderOrigins.Domain, PurchaseOrderOrigins.Excel)), (po.ImportKey, po.OriginLookupId));
        po.Notes = "El usuario reescribió las notas.";
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var again = await import.ImportAsync("GENERIC", Bytes(csv), "o.csv", commit: true, "DRAFT", null, default);
        var d = Assert.Single(again.Documents);
        Assert.Equal((PurchaseOrderImportRules.DocDuplicate, po.Number), (d.Status, d.PurchaseOrderNumber));
        Assert.Equal(1, await f.Db.Set<PurchaseOrder>().CountAsync());
    }

    [Fact]
    public async Task Mismo_producto_con_costos_distintos_bloquea_en_generico_y_en_QuickBooks()
    {
        await using var f = await CreateAsync();
        await f.AddWarehouseAsync("W1");
        await AddSupplierAsync(f, "ACME");
        await f.AddProductAsync("A-1", purchaseCost: 2m);
        var import = f.Get<PurchaseOrderImportService>();

        // GENERIC: 1.00 y 1.10 → bloqueada; 1.00, 1.00 y vacío → se suman (MERGED_LINES)
        var csv = "Supplier,Number,Date,SKU,Qty,Cost\r\nACME,PO-1,2026-11-01,A-1,10,1.00\r\nACME,PO-1,2026-11-01,A 1,5,1.10\r\n" +
                  "ACME,PO-2,2026-11-01,A-1,10,1.00\r\nACME,PO-2,2026-11-01,A-1,5,1\r\nACME,PO-2,2026-11-01,A-1,1,\r\n";
        var r = await import.ImportAsync("GENERIC", Bytes(csv), "o.csv", commit: false, "DRAFT", null, default);
        var conflict = Assert.Single(r.Documents, x => x.Number == "PO-1");
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, conflict.Status);
        var issue = Assert.Single(conflict.Issues, i => i.Code == PurchaseOrderImportRules.IssueCostConflict);
        Assert.True(issue.Blocking);
        Assert.Equal("El producto A-1 viene en varias líneas de la orden con costos distintos (1, 1.1); corrija el archivo para que todas lleven el mismo costo.", issue.Message);
        Assert.DoesNotContain(conflict.Issues, i => i.Code == PurchaseOrderImportRules.IssueMergedLines);
        var merged = Assert.Single(r.Documents, x => x.Number == "PO-2");
        Assert.Equal(PurchaseOrderImportRules.DocReady, merged.Status);
        Assert.Contains(merged.Issues, i => i.Code == PurchaseOrderImportRules.IssueMergedLines);

        // QUICKBOOKS: la misma regla
        var qb = ",\"Type\",\"Date\",\"Num\",\"Name\",\"Item\",\"Qty\",\"Cost Price\"\r\n" +
                 ",\"Purchase Order\",\"11/01/2026\",\"Q-1\",\"ACME\",\"A-1 (Guantes)\",10,1.00\r\n" +
                 ",\"Purchase Order\",\"11/01/2026\",\"Q-1\",\"ACME\",\"A-1 (Guantes)\",5,1.25\r\n";
        var q = await import.ImportAsync("QUICKBOOKS", Bytes(qb), "q.csv", commit: true, "DRAFT", null, default);
        var qd = Assert.Single(q.Documents);
        Assert.Equal(PurchaseOrderImportRules.DocBlocked, qd.Status);
        Assert.Contains(qd.Issues, i => i.Code == PurchaseOrderImportRules.IssueCostConflict && i.Blocking);
        Assert.Equal(0, await f.Db.Set<PurchaseOrder>().CountAsync());
    }

    [Fact]
    public async Task Ir_y_venir_lo_que_salio_de_Teikem_a_QuickBooks_no_se_vuelve_a_crear()
    {
        await using var f = await CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var acme = await AddSupplierAsync(f, "ACME");
        var otro = await AddSupplierAsync(f, "OTRO");
        var a1 = await f.AddProductAsync("A-1", purchaseCost: 2m);
        var orders = f.Get<PurchaseOrderService>();
        PurchaseOrderCreateRequest Req(int supplierId) => new(supplierId, w.PublicId, Lines: new[] { new PurchaseOrderLineRequest(a1.PublicId, 3m, 2m) });

        var exported = await orders.CreateAsync(Req(acme.SupplierId), default);   // PO-00001: se exporta
        var marked = await orders.CreateAsync(Req(acme.SupplierId), default);     // PO-00002: se marca
        var notPassed = await orders.CreateAsync(Req(acme.SupplierId), default);  // PO-00003: nunca salió de Teikem
        var other = await orders.CreateAsync(Req(otro.SupplierId), default);      // PO-00004: exportada, pero de otro proveedor
        foreach (var (id, export, mark) in new[] { (exported.Id, true, false), (marked.Id, false, true), (other.Id, true, false) })
        {
            var po = await f.Db.Set<PurchaseOrder>().SingleAsync(p => p.PurchaseOrderId == id);
            if (export) { po.QuickBooksExportedAt = DateTime.UtcNow; po.QuickBooksExportedBy = 1; }
            if (mark) { po.QuickBooksAt = DateTime.UtcNow; po.QuickBooksBy = 1; }
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        // Num = número de Teikem (exportada); «P. O. #» = número de Teikem (marcada); los otros dos se crean
        var qb = ",\"Type\",\"Date\",\"Num\",\"P. O. #\",\"Name\",\"Item\",\"Qty\",\"Cost Price\"\r\n" +
                 $",\"Purchase Order\",\"11/01/2026\",\"{exported.Number}\",,\"ACME\",\"A-1\",3,2\r\n" +
                 $",\"Bill\",\"11/02/2026\",\"F-77\",\"{marked.Number}\",\"ACME\",\"A-1\",3,2\r\n" +
                 $",\"Purchase Order\",\"11/03/2026\",\"{notPassed.Number}\",,\"ACME\",\"A-1\",3,2\r\n" +
                 $",\"Purchase Order\",\"11/04/2026\",\"{exported.Number}\",,\"OTRO\",\"A-1\",3,2\r\n";
        var r = await f.Get<PurchaseOrderImportService>().ImportAsync("QUICKBOOKS", Bytes(qb), "q.csv", commit: true, "DRAFT", null, default);

        Assert.Equal((2, 2), (r.Duplicates, r.Created));
        var back1 = Assert.Single(r.Documents, d => d.Vendor == "ACME" && d.Number == exported.Number);
        Assert.Equal((PurchaseOrderImportRules.DocDuplicate, exported.Number), (back1.Status, back1.PurchaseOrderNumber));
        Assert.Equal($"La orden {exported.Number} nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.", Assert.Single(back1.Issues).Message);
        var back2 = Assert.Single(r.Documents, d => d.Number == "F-77");
        Assert.Equal((PurchaseOrderImportRules.DocDuplicate, marked.Number), (back2.Status, back2.PurchaseOrderNumber));
        Assert.Equal(PurchaseOrderImportRules.DocCreated, Assert.Single(r.Documents, d => d.Number == notPassed.Number).Status);
        Assert.Equal(PurchaseOrderImportRules.DocCreated, Assert.Single(r.Documents, d => d.Vendor == "OTRO").Status);
    }
}
