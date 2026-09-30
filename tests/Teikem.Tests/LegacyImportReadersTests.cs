using System.Text;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 10 (P2): lectores de la migración — parser CSV de QuickBooks (comillas, comas y saltos internos, columna
/// inicial vacía), proyecciones tipadas con los encabezados reales, detección de Windows-1252, configuración con sus
/// mensajes exactos, consultas del lector MSWM y render del reporte.
/// </summary>
public class LegacyImportReadersTests
{
    private const string ItemsHeader =
        ",Active Status,Type,Item,Description,Sales Tax Code,Account,COGS Account,Asset Account,Accumulated Depreciation," +
        "Purchase Description,Quantity On Hand,U/M,Cost,Preferred Vendor,Tax Agency,Price,Reorder Pt (Min),MPN,Serial,Lot,Category,Brand,Model";

    private const string SolutionsItemsHeader =
        ",\"Active Status\",\"Type\",\"Item\",\"Description\",\"Sales Tax Code\",\"Account\",\"COGS Account\",\"Asset Account\"," +
        "\"Accumulated Depreciation\",\"Purchase Description\",\"Quantity On Hand\",\"Cost\",\"Preferred Vendor\",\"Tax Agency\",\"Price\"," +
        "\"Reorder Pt (Min)\",\"MPN\",\"MANUFACTERS\"";

    private const string CustomersHeader =
        ",Active Status,Customer,Balance,Balance Total,Company,Mr./Ms./...,First Name,M.I.,Last Name,Primary Contact,Main Phone,Fax," +
        "Alt. Phone,Secondary Contact,Job Title,Main Email,Bill to 1,Bill to 2,Bill to 3,Bill to 4,Bill to 5,Ship to 1,Ship to 2," +
        "Ship to 3,Ship to 4,Ship to 5,Customer Type,Terms,Rep,Sales Tax Code,Tax item,Resale Num,Account No.,Credit Limit,Job Status," +
        "Job Type,Job Description,Start Date,Projected End,End Date";

    private const string VendorsHeader =
        ",Active Status,Vendor,Balance,Balance Total,Company,Mr./Ms./...,First Name,M.I.,Last Name,Bill from 1,Bill from 2,Bill from 3," +
        "Bill from 4,Bill from 5,Ship from 1,Ship from 2,Ship from 3,Ship from 4,Ship from 5,Primary Contact,Job Title,Main Phone,Fax," +
        "Alt. Phone,Secondary Contact";

    // ================================================================ parser CSV

    [Fact]
    public void Parser_handles_quotes_commas_and_newlines_inside_quotes()
    {
        var csv = ",A,B,C\r\n,\"uno, dos\",\"dice \"\"hola\"\"\",\"línea 1\r\nlínea 2\"\r\n,x,y,z\r\n";
        var t = QuickBooksCsvReader.ParseTable(csv, "prueba.csv");

        Assert.Equal(new[] { "A", "B", "C" }, t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("uno, dos", t.Rows[0].Get("A"));
        Assert.Equal("dice \"hola\"", t.Rows[0].Get("B"));
        Assert.Equal("línea 1\nlínea 2", t.Rows[0].Get("C"));
        Assert.Equal(2, t.Rows[0].Line);
        Assert.Equal("z", t.Rows[1].Get("c"));      // sin distinguir mayúsculas
        Assert.Equal(4, t.Rows[1].Line);            // el salto dentro de comillas consume una línea física
    }

    [Fact]
    public void Parser_drops_leading_empty_column_and_empty_rows()
    {
        var csv = ",Item,Description\n,,\n,ABC,Caja\n\n,  ,  \n,DEF,Cinta";
        var t = QuickBooksCsvReader.ParseTable(csv, "prueba.csv");

        Assert.Equal(new[] { "Item", "Description" }, t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(new[] { "ABC", "Caja" }, t.Rows[0].Cells);
        Assert.Equal("DEF", t.Rows[1].Get("Item"));  // último registro sin salto de línea final
    }

    [Fact]
    public void Header_without_leading_comma_keeps_first_column()
    {
        var t = QuickBooksCsvReader.ParseTable("Item,Description\nABC,Caja\n", "x.csv");
        Assert.Equal(new[] { "Item", "Description" }, t.Headers);
        Assert.Equal("ABC", t.Rows[0].Get("Item"));
    }

    [Fact]
    public void Get_trims_returns_empty_for_short_rows_and_null_for_unknown_column()
    {
        var t = QuickBooksCsvReader.ParseTable(",Item,Description,Price\n,  ABC  ,\" Caja \"\n", "x.csv");
        var row = t.Rows.Single();
        Assert.Equal("ABC", row.Get("Item"));
        Assert.Equal("Caja", row.Get("Description"));
        Assert.Equal(string.Empty, row.Get("Price"));   // la columna existe, la fila no trae la celda
        Assert.Null(row.GetOrNull("Price"));
        Assert.Null(row.Get("Brand"));                  // la columna no existe
        Assert.False(row.Has("Brand"));
    }

    [Fact]
    public void Parser_has_no_row_limit()
    {
        var sb = new StringBuilder(",Item\n");
        for (var i = 0; i < 6000; i++) sb.Append(",SKU").Append(i).Append('\n');
        var t = QuickBooksCsvReader.ParseTable(sb.ToString(), "grande.csv");
        Assert.Equal(6000, t.Rows.Count);
        Assert.Equal("SKU5999", t.Rows[^1].Get("Item"));
    }

    [Fact]
    public void Empty_content_gives_no_headers_and_no_rows()
    {
        var t = QuickBooksCsvReader.ParseTable("", "vacio.csv");
        Assert.Empty(t.Headers);
        Assert.Empty(t.Rows);
    }

    // ================================================================ proyecciones

    [Fact]
    public void ReadItems_projects_depot_row_with_real_headers()
    {
        var csv = ItemsHeader + "\r\n" +
                  ",Active,Inventory Part,1040 P,\"CAJA 10X4, DOBLE\",Tax,Inventory,Cost of Goods Sold,Inventory Asset,,,\"1,045\",,0.74,,,2.5,,MPN-9,,,CARTONES,Uline,M1\r\n";
        var item = Assert.Single(QuickBooksCsvReader.ParseItems(csv, "Depot Products.csv"));

        Assert.Equal("Active", item.ActiveStatus);
        Assert.True(item.IsActive);
        Assert.Equal("Inventory Part", item.Type);
        Assert.Equal("1040 P", item.Item);                  // el lector no normaliza: eso es de LegacyImportRules
        Assert.Equal("CAJA 10X4, DOBLE", item.Description);
        Assert.Equal("1,045", item.QuantityOnHand);
        Assert.Equal("0.74", item.Cost);
        Assert.Equal("2.5", item.Price);
        Assert.Equal("CARTONES", item.Category);
        Assert.Equal("Uline", item.Brand);
        Assert.Null(item.Manufacturer);
        Assert.Equal("MPN-9", item.Mpn);
        Assert.Equal(2, item.Line);
    }

    [Fact]
    public void ReadItems_projects_solutions_row_with_quoted_headers_and_manufacturer_typo()
    {
        var csv = SolutionsItemsHeader + "\n" +
                  ",\"Inactive\",\"Inventory Part\",\"NECH 1001\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"-208\",\"3.10\",\"\",\"\",\"5\",\"\",\"\",\"ACME\"\n";
        var item = Assert.Single(QuickBooksCsvReader.ParseItems(csv, "Solutions Items.csv"));

        Assert.False(item.IsActive);
        Assert.Equal("NECH 1001", item.Item);
        Assert.Null(item.Description);                     // vacío = null
        Assert.Equal("-208", item.QuantityOnHand);
        Assert.Null(item.Category);                        // Solutions no trae Category
        Assert.Equal("ACME", item.Manufacturer);
    }

    [Fact]
    public void ReadCustomers_projects_addresses_terms_and_emails()
    {
        var cells = new Dictionary<string, string>
        {
            ["Active Status"] = "Active", ["Customer"] = "FARMACIA EJEMPLO", ["Company"] = "Farmacia Ejemplo Inc",
            ["First Name"] = "Ana", ["Last Name"] = "Pérez", ["Main Phone"] = "787.686.6464", ["Alt. Phone"] = "787-555-0000",
            ["Main Email"] = "a@x.com; b@x.com", ["Bill to 1"] = "Farmacia Ejemplo", ["Bill to 2"] = "Calle 1 #5",
            ["Bill to 3"] = "Añasco PR, 00610", ["Ship to 1"] = "Farmacia Ejemplo", ["Ship to 2"] = "Carr. 2 Km 5",
            ["Ship to 3"] = "Bayamón, PR 00961", ["Ship to 4"] = "787.787.7733/787.743.1273",
            ["Terms"] = "Net 30", ["Rep"] = "JR", ["Credit Limit"] = "1,500.00",
        };
        var headers = CustomersHeader.Split(',').Skip(1).ToList();
        var row = "," + string.Join(",", headers.Select(h => cells.TryGetValue(h, out var v) ? Quote(v) : ""));
        var c = Assert.Single(QuickBooksCsvReader.ParseCustomers(CustomersHeader + "\n" + row + "\n", "Solutions Customers.csv"));

        Assert.True(c.IsActive);
        Assert.Equal("FARMACIA EJEMPLO", c.Customer);
        Assert.Equal("Farmacia Ejemplo Inc", c.Company);
        Assert.Equal("Ana", c.FirstName);
        Assert.Equal("Pérez", c.LastName);
        Assert.Equal("787.686.6464", c.MainPhone);
        Assert.Equal("787-555-0000", c.AltPhone);
        Assert.Equal("a@x.com; b@x.com", c.MainEmail);
        Assert.Equal(new string?[] { "Farmacia Ejemplo", "Calle 1 #5", "Añasco PR, 00610", null, null }, c.BillTo);
        Assert.Equal(new string?[] { "Farmacia Ejemplo", "Carr. 2 Km 5", "Bayamón, PR 00961", "787.787.7733/787.743.1273", null }, c.ShipTo);
        Assert.Equal("Net 30", c.Terms);
        Assert.Equal("JR", c.Rep);
        Assert.Equal("1,500.00", c.CreditLimit);
    }

    [Fact]
    public void ReadVendors_projects_bill_from_and_contact()
    {
        var row = ",Active,SHIELD LINE,0,0,Shield Line Corp,,Luis,,Rivera,Shield Line Corp,PO Box 1,\"Caguas, PR 00725\",,,,,,,,Luis Rivera,,787-000-1111,,,";
        var v = Assert.Single(QuickBooksCsvReader.ParseVendors(VendorsHeader + "\n" + row + "\n", "Depot Vendor.csv"));

        Assert.Equal("SHIELD LINE", v.Vendor);
        Assert.Equal("Shield Line Corp", v.Company);
        Assert.Equal(new string?[] { "Shield Line Corp", "PO Box 1", "Caguas, PR 00725", null, null }, v.BillFrom);
        Assert.Equal("Luis Rivera", v.PrimaryContact);
        Assert.Equal("787-000-1111", v.MainPhone);
        Assert.Equal("Luis", v.FirstName);
        Assert.Equal("Rivera", v.LastName);
    }

    [Fact]
    public void Missing_required_column_gives_exact_message()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            QuickBooksCsvReader.ParseItems(",Active Status,Type,Item,Description,Cost,Price\n,Active,Service,X,Y,1,2\n", "Depot Products.csv"));
        Assert.Equal("El archivo Depot Products.csv no tiene la columna 'Quantity On Hand'.", ex.Message);

        var ex2 = Assert.Throws<ValidationException>(() => QuickBooksCsvReader.ParseVendors(",Active Status\n", "Depot Vendor.csv"));
        Assert.Equal("El archivo Depot Vendor.csv no tiene la columna 'Vendor'.", ex2.Message);

        var ex3 = Assert.Throws<ValidationException>(() => QuickBooksCsvReader.ParseCustomers("", "Depot Customers.csv"));
        Assert.Equal("El archivo Depot Customers.csv no tiene la columna 'Active Status'.", ex3.Message);
    }

    [Fact]
    public void Reading_a_missing_file_gives_exact_message()
    {
        var path = Path.Combine(Path.GetTempPath(), "teikem-no-existe-" + Guid.NewGuid() + ".csv");
        var ex = Assert.Throws<ValidationException>(() => QuickBooksCsvReader.ReadItems(path));
        Assert.Equal($"El archivo no existe: {path}.", ex.Message);
    }

    // ================================================================ codificación

    [Fact]
    public void Decode_detects_windows_1252()
    {
        var text = ",Customer,Bill to 3\r\n,CLIENTE,Añasco PR 00610\r\n,OTRO,\"Bayamón, PR 00961\"\r\n";
        var bytes = QuickBooksCsvReader.Windows1252().GetBytes(text);
        Assert.Contains((byte)0xF1, bytes);   // 'ñ' en 1252 (inválido como UTF-8 suelto)

        var decoded = QuickBooksCsvReader.Decode(bytes);
        Assert.Equal(text, decoded);

        var t = QuickBooksCsvReader.ParseTable(decoded, "x.csv");
        Assert.Equal("Añasco PR 00610", t.Rows[0].Get("Bill to 3"));
        Assert.Equal("Bayamón, PR 00961", t.Rows[1].Get("Bill to 3"));
    }

    [Fact]
    public void Decode_reads_utf8_with_and_without_bom()
    {
        const string text = ",Item\n,Añasco";
        Assert.Equal(text, QuickBooksCsvReader.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
        Assert.Equal(text, QuickBooksCsvReader.Decode(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void ReadItems_from_a_1252_file_on_disk()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "Items 1252.csv");
        var content = ",Active Status,Type,Item,Description,Quantity On Hand,Cost,Price\r\n,Active,Inventory Part,B-1,Caja Bayamón,10,1,2\r\n";
        File.WriteAllBytes(path, QuickBooksCsvReader.Windows1252().GetBytes(content));

        var item = Assert.Single(QuickBooksCsvReader.ReadItems(path));
        Assert.Equal("Caja Bayamón", item.Description);
    }

    // ================================================================ configuración

    private const string ValidConfig = """
        {
          // comentario permitido
          "company": { "name": "Advance Depot", "legalName": "Advance Depot Solutions", "modules": ["CATALOG"], },
          "sources": {
            "products": "Depot Products.csv",
            "mswm": { "connectionStringName": "LegacyMswm", "warehouseId": "Main" }
          },
          "products": { "defaultCategory": "AxisCare", "categoryByQuickBooksCategory": { "CARTONES": "CARTONES" } },
          "warehouse": {
            "code": "ALM-DEPOT",
            "zones": [ { "code": "PISO", "name": "Piso", "zoneType": "RESERVE", "matchLocationIds": ["PISO", "FLOOR"] } ]
          },
          "openingBalances": { "source": "MSWM", "notes": "Migración" },
        }
        """;

    [Fact]
    public void Config_parses_camel_case_with_comments_trailing_commas_and_defaults()
    {
        var dir = TempDir();
        var cfg = LegacyImportConfig.Parse(ValidConfig, dir, checkFiles: false);

        Assert.Equal("Advance Depot", cfg.Company.Name);
        Assert.Equal("Advance Depot Solutions", cfg.Company.LegalName);
        Assert.Equal("es", cfg.Company.Lang);
        Assert.Equal(new[] { "CATALOG" }, cfg.Company.Modules);
        Assert.Equal(Path.Combine(dir, "Depot Products.csv"), cfg.Sources.Products);   // relativa al JSON
        Assert.Equal("Main", cfg.Sources.Mswm!.WarehouseId);
        Assert.Equal("AxisCare", cfg.Products.DefaultCategory);
        Assert.Equal("CARTONES", cfg.Products.CategoryByQuickBooksCategory["cartones"]); // sin distinguir mayúsculas
        Assert.Equal(new[] { "Inventory Part", "Inventory Assembly" }, cfg.Products.Types);
        Assert.Equal("UN", cfg.Products.BaseUom);
        Assert.Equal("NONE", cfg.Products.TrackingType);
        Assert.True(cfg.Products.NameFallbackToSku);
        Assert.False(cfg.Products.CreateUnknownWmsSkusWithStock);
        Assert.Null(cfg.Suppliers.IncludeNames);
        Assert.Equal("USD", cfg.Clients.DefaultCurrency);
        Assert.True(cfg.Clients.QbCodeCustomField);
        Assert.True(cfg.Clients.RepCustomField);
        Assert.Equal("PR", cfg.Warehouse.Country);
        Assert.Equal(new[] { "PISO", "FLOOR" }, cfg.Warehouse.Zones.Single().MatchLocationIds);
        Assert.Equal("mswm", cfg.OpeningBalances.Source);                                 // normalizado
        Assert.Equal("OPENING_BALANCE", cfg.OpeningBalances.Reason);
        Assert.Equal(dir, cfg.ResolvedOutputDir);                                         // carpeta del primer archivo fuente
        Assert.Equal("migracion", cfg.ResolvedPrefix);
    }

    [Fact]
    public void Config_single_bin_and_absolute_paths()
    {
        var abs = Path.Combine(TempDir(), "Solutions Items.csv");
        var json = $$"""
            { "company": { "name": "Advance Solutions" },
              "sources": { "products": {{System.Text.Json.JsonSerializer.Serialize(abs)}} },
              "warehouse": { "code": "ALM-SOL", "singleBin": { "zone": { "code": "GEN", "name": "General", "zoneType": "RESERVE" }, "bin": "GENERAL" } },
              "openingBalances": { "source": "quickbooks" },
              "report": { "outputDir": "reportes", "prefix": "solutions" } }
            """;
        var baseDir = TempDir();
        var cfg = LegacyImportConfig.Parse(json, baseDir, checkFiles: false);

        Assert.Equal(abs, cfg.Sources.Products);
        Assert.Equal("GEN", cfg.Warehouse.SingleBin!.Zone!.Code);
        Assert.Equal("GENERAL", cfg.Warehouse.SingleBin.Bin);
        Assert.Equal(Path.Combine(baseDir, "reportes"), cfg.ResolvedOutputDir);
        Assert.Equal("solutions", cfg.ResolvedPrefix);
    }

    [Theory]
    [InlineData("""{ "sources": { "products": "a.csv" } }""", "company.name es obligatorio.")]
    [InlineData("""{ "company": { "name": "  " }, "sources": { "products": "a.csv" } }""", "company.name es obligatorio.")]
    [InlineData("""{ "company": { "name": "X" } }""", "sources.products es obligatorio.")]
    [InlineData("""{ "company": { "name": "X" }, "sources": { "products": "a.csv" }, "openingBalances": { "source": "excel" } }""",
        "openingBalances.source debe ser mswm, quickbooks o none.")]
    [InlineData("""{ "company": { "name": "X" }, "sources": { "products": "a.csv" }, "openingBalances": { "source": "mswm" } }""",
        "Con openingBalances.source=mswm se requiere sources.mswm.connectionStringName y sources.mswm.warehouseId.")]
    [InlineData("""{ "company": { "name": "X" }, "sources": { "products": "a.csv", "mswm": { "connectionStringName": "LegacyMswm" } }, "openingBalances": { "source": "mswm" } }""",
        "Con openingBalances.source=mswm se requiere sources.mswm.connectionStringName y sources.mswm.warehouseId.")]
    public void Config_validation_messages_are_exact(string json, string message)
    {
        var ex = Assert.Throws<ValidationException>(() => LegacyImportConfig.Parse(json, TempDir(), checkFiles: false));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void Config_without_opening_balances_defaults_to_none()
    {
        var cfg = LegacyImportConfig.Parse("""{ "company": { "name": "X" }, "sources": { "products": "a.csv" } }""", TempDir(), checkFiles: false);
        Assert.Equal("none", cfg.OpeningBalances.Source);
    }

    [Fact]
    public void Config_load_reports_missing_source_file_with_resolved_path()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "Products.csv"), ",Item\n");
        var cfgPath = Path.Combine(dir, "import.json");
        File.WriteAllText(cfgPath, """{ "company": { "name": "X" }, "sources": { "products": "Products.csv", "customers": "Customers.csv" } }""");

        var ex = Assert.Throws<ValidationException>(() => LegacyImportConfig.Load(cfgPath));
        Assert.Equal($"El archivo no existe: {Path.Combine(dir, "Customers.csv")}.", ex.Message);
    }

    [Fact]
    public void Config_load_ok_when_files_exist()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "Products.csv"), ",Item\n");
        var cfgPath = Path.Combine(dir, "import.json");
        File.WriteAllText(cfgPath, """{ "company": { "name": "X" }, "sources": { "products": "Products.csv" } }""");

        var cfg = LegacyImportConfig.Load(cfgPath);
        Assert.Equal(cfgPath, cfg.ConfigPath);
        Assert.Equal(Path.Combine(dir, "Products.csv"), cfg.Sources.Products);
    }

    [Fact]
    public void Config_load_missing_config_file_and_invalid_json()
    {
        var missing = Path.Combine(TempDir(), "no-existe.json");
        var ex = Assert.Throws<ValidationException>(() => LegacyImportConfig.Load(missing));
        Assert.Equal($"El archivo no existe: {missing}.", ex.Message);

        var ex2 = Assert.Throws<ValidationException>(() => LegacyImportConfig.Parse("{ no es json", TempDir(), checkFiles: false));
        Assert.StartsWith("El archivo de configuración no es un JSON válido:", ex2.Message);
    }

    // ================================================================ MSWM

    [Fact]
    public void Mswm_queries_are_select_only_and_filtered_by_warehouse()
    {
        Assert.Equal(9, MswmReader.AllQueries.Count);   // Lote 11: + 5 del historial por posición (cupos)
        foreach (var sql in MswmReader.AllQueries)
        {
            Assert.StartsWith("SELECT ", sql);
            Assert.Contains("WHERE WarehouseId = @warehouseId", sql);
            foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "DROP", "ALTER", ";" })
                Assert.DoesNotContain(verb, sql, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("FROM dbo.Item ", MswmReader.ItemsSql);
        Assert.Contains("FROM dbo.Location ", MswmReader.LocationsSql);
        Assert.Contains("FROM dbo.Inventory ", MswmReader.InventorySql);
        Assert.Contains("OnHandQuantity <> 0", MswmReader.InventorySql);
        Assert.Contains("FROM dbo.ItemUPC ", MswmReader.UpcsSql);
        Assert.Contains("FROM dbo.Inventory_Old ", MswmReader.InventoryOldPhotoSql);
        Assert.Contains("FROM dbo.CycleCountInventory ", MswmReader.CycleCountPhotoSql);
        Assert.Contains("GROUP BY Request, Iteration, LocationId", MswmReader.CycleCountPhotoSql);
        Assert.Contains("FROM dbo.CycleCountHistory ", MswmReader.CycleCountHistoryPhotoSql);
        Assert.Contains("OnHandQuantity + AdjustmentQuantity", MswmReader.CycleCountHistoryPhotoSql);
        Assert.Contains("FROM dbo.PutAwayHistory ", MswmReader.PutAwayPhotoSql);
        Assert.Contains("CAST(TransDate AS date)", MswmReader.PutAwayPhotoSql);
        Assert.All(MswmReader.BinHistoryQueries, q => Assert.Contains("HAVING SUM(", q.Sql));
        Assert.Equal(120, MswmReader.CommandTimeoutSeconds);
    }

    [Fact]
    public void Mswm_clean_turns_section_sign_and_blanks_into_null()
    {
        Assert.Null(MswmReader.Clean("§"));
        Assert.Null(MswmReader.Clean(" § "));
        Assert.Null(MswmReader.Clean(""));
        Assert.Null(MswmReader.Clean(null));
        Assert.Null(MswmReader.Clean(DBNull.Value));
        Assert.Equal("01-a-24", MswmReader.Clean(" 01-a-24 "));
        Assert.Equal("§A", MswmReader.Clean("§A"));
    }

    // ================================================================ reporte

    private static LegacyImportReport SampleReport(bool dryRun)
    {
        var r = new LegacyImportReport("Advance Depot", dryRun) { GeneratedAt = new DateTime(2026, 9, 28, 14, 5, 0) };
        r.AddInfo("Configuración", "import.depot.json");
        r.CountRead("Productos", 3);
        r.CountCreated("Productos", 2);
        r.SetExpected("Productos", 552);
        r.Reject("Productos", "12525556", "Categoría de QuickBooks sin regla: 'NATIONAL GUARD'.");
        r.Warn("Productos", "1040P", "SKU '1040 P' normalizado a '1040P'.");
        r.Map("SKU", "1040 P", "1040P");
        r.AddOpeningBalance("1040P", "01-A-24", 12m);
        r.AddOpeningBalance("TAPE|X", "PISO", 3.5m);
        return r;
    }

    [Fact]
    public void Report_markdown_has_title_summary_and_rejection_row()
    {
        var md = SampleReport(dryRun: true).RenderMarkdown();

        Assert.Contains("# Migración de datos — Advance Depot — SIMULACIÓN (dry-run)", md);
        Assert.Contains("| Entidad | Leídos | Creados | Ya existían | Omitidos | Rechazados | Esperados |", md);
        Assert.Contains("| Productos | 3 | 2 | 0 | 0 | 1 | 552 |", md);
        Assert.Contains("## Rechazos (1)", md);
        Assert.Contains("| Productos | 12525556 | Categoría de QuickBooks sin regla: 'NATIONAL GUARD'. | No |", md);
        Assert.Contains("| Productos | 1040P | SKU '1040 P' normalizado a '1040P'. |", md);
        Assert.Contains("| SKU | 1040 P | 1040P |", md);
        Assert.Contains("## Saldo inicial (2 asientos, 15.5 unidades)", md);
        Assert.Contains("| TAPE\\|X | PISO | 3.5 |", md);   // '|' escapado dentro de la tabla
        Assert.Contains("| Configuración | import.depot.json |", md);
        Assert.DoesNotContain("CARGA REAL", md);
    }

    [Fact]
    public void Report_real_run_mark_and_severe_rejections()
    {
        var r = SampleReport(dryRun: false);
        Assert.Contains("CARGA REAL", r.RenderMarkdown());
        Assert.False(r.HasSevereRejections);

        r.Reject("Conciliación", "ALM-DEPOT", "Diferencia entre el kardex y las existencias.", severe: true);
        Assert.True(r.HasSevereRejections);
        Assert.Equal(1, r.Total("conciliación").Rejected);
        Assert.Equal(2, r.Resumen.Count);
    }

    [Fact]
    public void Empty_report_renders_empty_sections()
    {
        var md = new LegacyImportReport("X", true).RenderMarkdown();
        Assert.Contains("Sin rechazos.", md);
        Assert.Contains("Sin advertencias.", md);
        Assert.Contains("Sin mapeos.", md);
        Assert.Contains("Sin saldo inicial.", md);
    }

    [Fact]
    public async Task Report_write_creates_md_and_one_csv_per_section()
    {
        var dir = Path.Combine(TempDir(), "salida");
        var paths = await SampleReport(dryRun: true).WriteAsync(dir, "depot");

        Assert.Equal(7, paths.Count);
        Assert.Equal(Path.Combine(dir, "depot-20260928-1405.md"), paths[0]);
        foreach (var suffix in new[] { "resumen", "rechazos", "advertencias", "mapeos", "saldo-inicial", "actualizaciones" })
            Assert.Contains(Path.Combine(dir, $"depot-20260928-1405-{suffix}.csv"), paths);
        Assert.All(paths, p => Assert.True(File.Exists(p)));

        Assert.Contains("SIMULACIÓN (dry-run)", await File.ReadAllTextAsync(paths[0]));

        // el CSV de rechazos se relee con el mismo parser y conserva el mensaje con comillas simples
        var rechazos = QuickBooksCsvReader.ParseTable(
            QuickBooksCsvReader.Decode(await File.ReadAllBytesAsync(Path.Combine(dir, "depot-20260928-1405-rechazos.csv"))), "r.csv");
        Assert.Equal("Categoría de QuickBooks sin regla: 'NATIONAL GUARD'.", rechazos.Rows.Single().Get("Motivo"));

        var saldo = QuickBooksCsvReader.ParseTable(await File.ReadAllTextAsync(Path.Combine(dir, "depot-20260928-1405-saldo-inicial.csv")), "s.csv");
        Assert.Equal("3.5", saldo.Rows[1].Get("Cantidad"));
    }

    // ================================================================ utilidades

    private static string Quote(string v) => v.Contains(',') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "teikem-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
