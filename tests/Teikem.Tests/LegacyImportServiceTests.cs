using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Migration;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 10 / P0: construcción del plan (LegacyImportPlanner, pura) con la muestra sintética de docs/migracion/sample y con
/// filas del WMS armadas en memoria; el servicio completo en dry-run sobre EF InMemory (compañía nueva: solo lee); el
/// verbo CLI (sintaxis y códigos de salida) y las dos configuraciones reales (valores exactos del plan aprobado).
/// </summary>
public class LegacyImportServiceTests
{
    private static string SampleConfigPath => Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", "sample", "import.sample.json");

    private static (LegacyImportConfig Cfg, LegacyImportPlan Plan, LegacyImportReport Report) BuildSamplePlan()
    {
        var cfg = LegacyImportConfig.Load(SampleConfigPath);
        var src = LegacyImportSources.FromQuickBooks(
            QuickBooksCsvReader.ReadItems(cfg.Sources.Products!),
            QuickBooksCsvReader.ReadCustomers(cfg.Sources.Customers!),
            QuickBooksCsvReader.ReadVendors(cfg.Sources.Vendors!));
        var report = new LegacyImportReport(cfg.Company.Name!, dryRun: true);
        return (cfg, LegacyImportPlanner.Build(cfg, src, report), report);
    }

    private static List<string> Warnings(LegacyImportReport r) => r.Advertencias.Select(w => w.Message).ToList();

    // ================================================================ muestra sintética

    [Fact]
    public void Sample_products_accept_inventory_items_and_skip_the_service()
    {
        var (_, plan, report) = BuildSamplePlan();

        Assert.Equal(new[] { "SAM-001", "SAM002", "SAM-003", "CAJ-100", "SAM-006" }, plan.Products.Select(p => p.Sku));
        Assert.Equal(new[] { "MUESTRA", "CARTONES" }, plan.Categories);
        var t = report.Total(LegacyImportEntities.Products);
        Assert.Equal(6, t.Read);
        Assert.Equal(1, t.Skipped);
        Assert.Equal(0, t.Rejected);

        var warnings = Warnings(report);
        Assert.Contains("SKU 'SAM 002' normalizado a 'SAM002'.", warnings);
        Assert.Contains("El ítem SAM-003 no tiene descripción; se usa el SKU como nombre.", warnings);
        Assert.Contains("El ítem SRV-01 es de tipo 'Service' (no es de inventario); no se carga.", warnings);
        Assert.Contains(report.Mapeos, m => m.Kind == "SKU" && m.From == "SAM 002" && m.To == "SAM002");

        var sam3 = plan.Products.Single(p => p.Sku == "SAM-003");
        Assert.Equal("SAM-003", sam3.Name);
        Assert.Null(sam3.PurchaseCost);   // 0 en QuickBooks = sin costo
        Assert.Null(sam3.SalePrice);
        var sam1 = plan.Products.Single(p => p.Sku == "SAM-001");
        Assert.Equal(2.50m, sam1.PurchaseCost);
        Assert.Equal(4.00m, sam1.SalePrice);
        Assert.Equal("MUESTRA", sam1.Category);
        // Lote 12: la marca de QuickBooks (columna Brand) pasa al producto; vacía → null
        Assert.Equal("MARCA DEMO", sam1.Brand);
        Assert.Contains(plan.Products, p => p.Brand is null);
        Assert.Equal("CARTONES", plan.Products.Single(p => p.Sku == "CAJ-100").Category);
        Assert.Equal("Mascarilla quirúrgica, caja de 50 (muestra)", plan.Products.Single(p => p.Sku == "SAM002").Name);
        Assert.All(plan.Products, p => Assert.False(p.DeactivateAtEnd));
    }

    [Fact]
    public void Sample_opening_balance_goes_to_the_single_bin_and_reports_the_negative()
    {
        var (_, plan, report) = BuildSamplePlan();

        Assert.NotNull(plan.Warehouse);
        Assert.Equal("ALM-PRUEBA", plan.Warehouse!.Code);
        Assert.Equal("PR", plan.Warehouse.Country);
        Assert.Equal(new[] { ("GEN", "General", (string?)"RESERVE") }, plan.Zones.Select(z => (z.Code, z.Name, z.ZoneType)));
        Assert.Equal(new[] { "GENERAL" }, plan.Bins.Select(b => b.Code));

        Assert.Equal(
            new[] { ("SAM-001", 120m), ("SAM002", 1040m), ("SAM-003", 10m), ("CAJ-100", 500m) },
            plan.Balances.Select(b => (b.Sku, b.Quantity)));
        Assert.All(plan.Balances, b => Assert.Equal("GENERAL", b.BinCode));
        Assert.Contains("El ítem SAM-006 tiene existencia negativa (-5); no se carga saldo inicial.", Warnings(report));
        Assert.Equal(1, report.Total(LegacyImportEntities.OpeningBalance).Skipped);
        Assert.False(report.HasSevereRejections);
    }

    [Fact]
    public void Sample_clients_exclude_the_test_record_and_build_consignees_and_contacts()
    {
        var (_, plan, report) = BuildSamplePlan();

        Assert.Equal(new[] { "Cliente Muestra Uno", "Cliente Muestra Dos" }, plan.Clients.Select(c => c.Name));
        Assert.Contains("Registro de prueba descartado: 'prueba'.", Warnings(report));
        var t = report.Total(LegacyImportEntities.Clients);
        Assert.Equal(3, t.Read);
        Assert.Equal(1, t.Skipped);

        var uno = plan.Clients[0];
        Assert.Equal("Cliente Muestra Uno", uno.QbCode);
        Assert.Equal("Cliente Muestra Uno Inc", uno.LegalName);
        Assert.Equal("NET30", uno.PaymentTerm);
        Assert.Equal(1500m, uno.CreditLimit);
        Assert.Equal("RA", uno.Rep);
        var unoAddr = Assert.Single(uno.Addresses);   // Ship to = Bill to: solo DELIVERY
        Assert.Equal(LocationTypes.Delivery, unoAddr.LocationType);
        Assert.Equal(("Calle Ficticia 1", "Caguas", "PR", (string?)"00725"),
            (unoAddr.Address.Line1, unoAddr.Address.City, unoAddr.Address.State, unoAddr.Address.PostalCode));
        Assert.Equal(new[] { "compras@example.com" }, uno.Emails);
        Assert.Equal(new[] { "787-555-0101" }, uno.Phones);

        var dos = plan.Clients[1];
        Assert.Equal("CHEQUE", dos.PaymentTerm);
        Assert.Null(dos.CreditLimit);
        Assert.Equal(new[] { LocationTypes.Delivery, LocationTypes.Billing }, dos.Addresses.Select(a => a.LocationType));
        var ship = dos.Addresses[0].Address;
        Assert.Equal(("Carr. 2 Km 10.5", "Local 4", "Bayamón", (string?)"00961"), (ship.Line1, ship.Line2, ship.City, ship.PostalCode));
        var bill = dos.Addresses[1].Address;
        Assert.Equal(("Apartado 123", "787.555.0102"), (bill.Line1, bill.Phone));
        Assert.Equal(new[] { "pedidos@example.com", "pagos@example.com" }, dos.Emails);   // separados por ';', el primero principal
        Assert.Equal(new[] { "787.555.0102" }, dos.Phones);

        Assert.Equal(3, report.Total(LegacyImportEntities.Consignees).Read);
        Assert.Equal(5, report.Total(LegacyImportEntities.Contacts).Read);
        Assert.Equal(new[] { "RA", "KDF" }, plan.RepOptions);
        Assert.True(plan.QbCodeField);
        Assert.True(plan.RepField);
        Assert.Contains(report.Mapeos, m => m.Kind == "Término de pago" && m.From == "Net 30" && m.To == "NET30");
        Assert.Contains(report.Mapeos, m => m.Kind == "Término de pago" && m.From == "CHEQUE" && m.To == "CHEQUE");
    }

    [Fact]
    public void Sample_suppliers_take_contact_phone_and_bill_from_address()
    {
        var (_, plan, _) = BuildSamplePlan();

        Assert.Equal(new[] { "Proveedor Muestra A", "Proveedor Muestra B", "Proveedor Muestra C" }, plan.Suppliers.Select(s => s.Name));
        var a = plan.Suppliers[0];
        Assert.Equal(("Luis Demo", "787-555-0201", "Proveedor Muestra A Corp, Calle Industrial 5, Caguas, PR 00725"), (a.ContactName, a.Phone, a.Notes));
        var b = plan.Suppliers[1];
        Assert.Equal(((string?)null, (string?)null, (string?)null), (b.ContactName, b.Phone, b.Notes));
        var c = plan.Suppliers[2];
        Assert.Equal("Eva Prueba", c.ContactName);   // sin Primary Contact: 'First Last'
        Assert.Equal("787.555.0203", c.Phone);
    }

    // ================================================================ reglas del plan con filas en memoria

    private static LegacyImportConfig Config(string json) => LegacyImportConfig.Parse(json, Path.GetTempPath(), checkFiles: false);

    private static QbItem Item(string sku, string? category = null, string status = "Active", string type = "Inventory Part", string? desc = "Artículo",
        string? qty = null)
        => new(status, type, sku, desc, qty, null, null, category, null, null, null);

    private const string WmsConfigJson = """
        {
          "company": { "name": "Prueba WMS" },
          "sources": { "products": "p.csv", "mswm": { "connectionStringName": "LegacyMswm", "warehouseId": "Main" } },
          "products": {
            "defaultCategory": "AxisCare",
            "categoryByQuickBooksCategory": { "CARTONES": "CARTONES" },
            "excludeQuickBooksCategories": ["SOLUTIONS", "NATIONAL GUARD"],
            "createUnknownWmsSkusWithStock": true
          },
          "warehouse": {
            "code": "alm-depot", "name": "Almacén Depot",
            "zones": [
              { "code": "PCK", "name": "Picking", "zoneType": "PICKING", "matchDescription": "Picking Location" },
              { "code": "RSV", "name": "Reserva", "zoneType": "RESERVE", "matchDescription": "REGULAR" },
              { "code": "PISO", "name": "Piso", "zoneType": "RESERVE", "matchLocationIds": ["PISO", "FLOOR"] }
            ],
            "skipLocationIds": ["W1"]
          },
          "openingBalances": { "source": "mswm" }
        }
        """;

    private static (LegacyImportPlan Plan, LegacyImportReport Report) BuildWmsPlan()
    {
        var items = new[]
        {
            Item("171-AC-426-B"), Item("BOX-1", "CARTONES"), Item("SOL-1", "SOLUTIONS"), Item("NG-1", "NATIONAL GUARD"),
            Item("OLD-1", status: "Not-active"), Item("OLD-2", status: "Not-active"), Item("WEIRD-1", "OTRA"), Item("SRV", type: "Service"),
        };
        var src = new LegacyImportSources(items, Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(),
            new[] { new WmsItem("886-PS4", "Cama eléctrica UCI"), new WmsItem("prueba1", null), new WmsItem("SOL-1", "Producto Solutions") },
            new[]
            {
                new WmsLocation("01-A-24", 1, "Picking Location", false),
                new WmsLocation("01-a-24", 1, "Picking Location", false),
                new WmsLocation("02-b-03", 2, "REGULAR", false),
                new WmsLocation("PISO", 3, null, false),
                new WmsLocation("W1", 4, "Wave", false),
                new WmsLocation("XX 99", 1, "Otra cosa", false),
            },
            new[]
            {
                new WmsInventoryRow("01-a-24", "171-ac-426-b", 5), new WmsInventoryRow("01-A-24", "171-AC-426-B", 7),
                new WmsInventoryRow("PISO", "BOX-1", 100), new WmsInventoryRow("PISO", "886-PS4", 10), new WmsInventoryRow("XX 99", "BOX-1", 3),
                new WmsInventoryRow("02-b-03", "OLD-2", 4), new WmsInventoryRow("PISO", "SOL-1", 2), new WmsInventoryRow("PISO", "NEG-9", -3),
            },
            new[] { new WmsUpc("171-ac-426-b", "0001"), new WmsUpc("171-AC-426-B", "0002"), new WmsUpc("BOX-1", "7700"), new WmsUpc("OLD-2", "7700") });
        var report = new LegacyImportReport("Prueba WMS", dryRun: true);
        return (LegacyImportPlanner.Build(Config(WmsConfigJson), src, report), report);
    }

    [Fact]
    public void Category_is_the_manufacturer_when_configured_and_the_default_when_it_is_blank()
    {
        const string json = """
            { "company": { "name": "Prueba" }, "sources": { "products": "p.csv" },
              "products": { "categoryFromManufacturer": true, "defaultCategory": "PRODUCTOS" } }
            """;
        QbItem WithMaker(string sku, string? maker) => new("Active", "Inventory Part", sku, "Artículo", null, null, null, null, null, maker, null);
        var src = new LegacyImportSources(new[] { WithMaker("A-1", " GLOBAL "), WithMaker("A-2", null), WithMaker("A-3", "global"), WithMaker("A-4", "PRODIGY") },
            Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(), Array.Empty<WmsItem>(), Array.Empty<WmsLocation>(),
            Array.Empty<WmsInventoryRow>(), Array.Empty<WmsUpc>());
        var plan = LegacyImportPlanner.Build(Config(json), src, new LegacyImportReport("Prueba", dryRun: true));
        Assert.Equal(new[] { "GLOBAL", "PRODUCTOS", "global", "PRODIGY" }, plan.Products.Select(p => p.Category));
        Assert.Equal(new[] { "GLOBAL", "PRODIGY", "PRODUCTOS" }, plan.Categories.OrderBy(c => c, StringComparer.Ordinal));   // GLOBAL y global son la misma categoría

        // Sin la opción, todo va a la categoría de defecto (como antes).
        var off = LegacyImportPlanner.Build(Config(json.Replace("\"categoryFromManufacturer\": true, ", "")), src, new LegacyImportReport("Prueba", dryRun: true));
        Assert.All(off.Products, p => Assert.Equal("PRODUCTOS", p.Category));
        Assert.Null(LegacyImportPlanner.ManufacturerCategory("  "));
        Assert.Equal(150, LegacyImportPlanner.ManufacturerCategory(new string('x', 200))!.Length);
    }

    [Fact]
    public void Wms_products_follow_depot_categories_exclusions_and_unknown_skus_with_stock()
    {
        var (plan, report) = BuildWmsPlan();
        var warnings = Warnings(report);

        Assert.Equal(new[] { "171-AC-426-B", "BOX-1", "OLD-1", "OLD-2", "886-PS4" }, plan.Products.Select(p => p.Sku));
        Assert.Equal(new[] { "AxisCare", "CARTONES" }, plan.Categories);
        Assert.Contains("El ítem SOL-1 tiene la categoría de QuickBooks 'SOLUTIONS', excluida por la configuración; no se carga.", warnings);
        Assert.Contains("El ítem NG-1 tiene la categoría de QuickBooks 'NATIONAL GUARD', excluida por la configuración; no se carga.", warnings);
        Assert.Contains("Categoría de QuickBooks sin regla: 'OTRA'.", warnings);

        var unknown = plan.Products.Single(p => p.Sku == "886-PS4");
        Assert.Equal(("Cama eléctrica UCI", "AxisCare", PlannedProduct.OriginWms), (unknown.Name, unknown.Category, unknown.Origin));
        Assert.Contains("El SKU 886-PS4 tiene 10 unidades en el WMS y no está en QuickBooks; se crea en la categoría por defecto.", warnings);
        Assert.Contains("El SKU PRUEBA1 del WMS no está en QuickBooks y no tiene existencia; se ignora.", warnings);
        Assert.Contains("El SKU SOL-1 tiene 2 unidades en el WMS pero está excluido; no se carga.", warnings);

        // Inactivos: sin existencia → se crean y se dan de baja; con existencia → se dejan activos y se informa.
        Assert.True(plan.Products.Single(p => p.Sku == "OLD-1").DeactivateAtEnd);
        Assert.False(plan.Products.Single(p => p.Sku == "OLD-2").DeactivateAtEnd);
        Assert.Contains("El ítem OLD-2 está inactivo en QuickBooks pero tiene existencia; se deja activo.", warnings);

        // Códigos de barras: primer UPC; uno repetido entre productos no se asigna a ninguno.
        Assert.Equal("0001", plan.Products.Single(p => p.Sku == "171-AC-426-B").Barcode);
        Assert.Contains("El ítem 171-AC-426-B tiene 2 códigos de barras en el WMS; se usa el primero (0001).", warnings);
        Assert.Null(plan.Products.Single(p => p.Sku == "BOX-1").Barcode);
        Assert.Null(plan.Products.Single(p => p.Sku == "OLD-2").Barcode);
        Assert.Contains("El código de barras 7700 del WMS está en varios productos (BOX-1, OLD-2); no se asigna a ninguno.", warnings);
    }

    [Fact]
    public void Wms_locations_map_to_zones_normalize_case_and_skip_what_has_no_zone()
    {
        var (plan, report) = BuildWmsPlan();
        var warnings = Warnings(report);

        Assert.Equal("ALM-DEPOT", plan.Warehouse!.Code);
        Assert.Equal(new[] { "PCK", "RSV", "PISO" }, plan.Zones.Select(z => z.Code));
        Assert.Equal(
            new[] { ("01-A-24", "PCK", (string?)"01", (string?)"A", (string?)"24"), ("02-B-03", "RSV", "02", "B", "03"), ("PISO", "PISO", null, null, null) },
            plan.Bins.Select(b => (b.Code, b.ZoneCode, b.Aisle, b.Level, b.Position)));
        Assert.Contains("La posición 01-a-24 del WMS coincide con 01-A-24 al pasarla a mayúsculas; se conserva la primera.", warnings);
        Assert.Contains("La posición W1 del WMS está en warehouse.skipLocationIds; se omite.", warnings);
        Assert.Contains("La posición XX 99 del WMS no tiene zona destino; se omite.", warnings);
        Assert.Contains(report.Mapeos, m => m.Kind == "Posición" && m.From == "02-b-03" && m.To == "02-B-03");
        Assert.Contains(report.Mapeos, m => m.Kind == "Zona de posición" && m.From == "02-b-03 (REGULAR)" && m.To == "02-B-03 → RSV");
        Assert.Contains(report.Mapeos, m => m.Kind == "Zona de posición" && m.From == "PISO" && m.To == "PISO → PISO");
        Assert.Contains(report.Info, i => i.Key == "Posiciones en zona PCK" && i.Value == "1");
        var t = report.Total(LegacyImportEntities.Bins);
        Assert.Equal((6, 3), (t.Read, t.Skipped));
    }

    [Fact]
    public void Wms_opening_balance_groups_by_sku_and_bin_and_rejects_what_cannot_land()
    {
        var (plan, report) = BuildWmsPlan();

        Assert.Equal(
            new[] { ("171-AC-426-B", "01-A-24", 12m), ("886-PS4", "PISO", 10m), ("BOX-1", "PISO", 100m), ("OLD-2", "02-B-03", 4m) },
            plan.Balances.Select(b => (b.Sku, b.BinCode, b.Quantity)));
        Assert.Contains(report.Rechazos, r => r.Message == "La posición XX-99 no se creó en Teikem; no se carga el saldo de BOX-1.");
        Assert.Contains(report.Rechazos, r => r.Message == "SKU SOL-1 con existencia en el WMS no existe en Teikem.");
        Assert.Contains("El ítem NEG-9 tiene existencia negativa (-3); no se carga saldo inicial.", Warnings(report));
        Assert.False(report.HasSevereRejections);
    }

    [Fact]
    public void Unknown_wms_skus_are_not_created_unless_configured()
    {
        var json = WmsConfigJson.Replace("\"createUnknownWmsSkusWithStock\": true", "\"createUnknownWmsSkusWithStock\": false");
        var src = new LegacyImportSources(new[] { Item("A-1") }, Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(),
            Array.Empty<WmsItem>(), new[] { new WmsLocation("PISO", 3, null, false) },
            new[] { new WmsInventoryRow("PISO", "886-PS4", 10) }, Array.Empty<WmsUpc>());
        var report = new LegacyImportReport("x", true);
        var plan = LegacyImportPlanner.Build(Config(json), src, report);

        Assert.Equal(new[] { "A-1" }, plan.Products.Select(p => p.Sku));
        Assert.Empty(plan.Balances);
        Assert.Contains(report.Rechazos, r => r.Message == "SKU 886-PS4 con existencia en el WMS no existe en Teikem.");
    }

    [Fact]
    public void Extra_products_add_only_missing_skus_of_the_given_category_to_the_default_category()
    {
        var json = """
            {
              "company": { "name": "Advance Solutions" },
              "sources": { "products": "s.csv", "extraProducts": { "path": "d.csv", "whereCategory": "SOLUTIONS" } },
              "products": { "types": ["Inventory Part"], "defaultCategory": "PRODUCTOS", "excludeSkus": ["101010"] }
            }
            """;
        var items = new[] { Item("NECH 1001"), Item("101010"), Item("24-081028", qty: "-208"), Item("GRP", type: "Group") };
        var extra = new[] { Item("NECH1001", "SOLUTIONS"), Item("00050-7", "SOLUTIONS", qty: "50"), Item("101010", "SOLUTIONS"), Item("X-9") };
        var report = new LegacyImportReport("Advance Solutions", true);
        var plan = LegacyImportPlanner.Build(Config(json), LegacyImportSources.FromQuickBooks(items, Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(), extra), report);

        Assert.Equal(new[] { "NECH1001", "24-081028", "00050-7" }, plan.Products.Select(p => p.Sku));
        Assert.All(plan.Products, p => Assert.Equal("PRODUCTOS", p.Category));
        Assert.Equal(PlannedProduct.OriginExtra, plan.Products.Single(p => p.Sku == "00050-7").Origin);
        Assert.Contains("El SKU 101010 está excluido por la configuración (products.excludeSkus); no se carga.", Warnings(report));
        Assert.Contains(report.Mapeos, m => m.Kind == "Producto adicional" && m.From == "00050-7");
        Assert.Equal(39, report.Total(LegacyImportEntities.Products).Expected);   // totales esperados de Advance Solutions
    }

    [Fact]
    public void Quickbooks_balance_without_single_bin_is_a_severe_configuration_error()
    {
        var json = """
            { "company": { "name": "X" }, "sources": { "products": "p.csv" }, "warehouse": { "code": "ALM" },
              "openingBalances": { "source": "quickbooks" } }
            """;
        var report = new LegacyImportReport("X", true);
        LegacyImportPlanner.Build(Config(json), LegacyImportSources.FromQuickBooks(new[] { Item("A", qty: "5") }, Array.Empty<QbCustomer>(), Array.Empty<QbVendor>()), report);
        var r = Assert.Single(report.Rechazos);
        Assert.True(r.Severe);
        Assert.Equal("Con openingBalances.source=quickbooks se requiere warehouse.singleBin.", r.Message);
    }

    [Fact]
    public void Opening_balance_without_warehouse_is_a_severe_configuration_error()
    {
        var json = """{ "company": { "name": "X" }, "sources": { "products": "p.csv" }, "openingBalances": { "source": "quickbooks" } }""";
        var report = new LegacyImportReport("X", true);
        var plan = LegacyImportPlanner.Build(Config(json), LegacyImportSources.FromQuickBooks(new[] { Item("A", qty: "5") }, Array.Empty<QbCustomer>(), Array.Empty<QbVendor>()), report);
        Assert.Empty(plan.Balances);
        Assert.Contains(report.Rechazos, r => r.Severe && r.Message == "Hay saldo inicial que cargar pero la configuración no define warehouse.code.");
    }

    [Fact]
    public void Suppliers_are_filtered_by_include_names_and_missing_names_are_reported()
    {
        var json = """
            { "company": { "name": "X" }, "sources": { "products": "p.csv" },
              "suppliers": { "includeNames": ["SHIELD LINE", "PADIMONT HOLDINGS CORP"], "excludeNames": [] } }
            """;
        QbVendor V(string name) => new("Active", name, null, new string?[5], null, null, null, null);
        var report = new LegacyImportReport("X", true);
        var plan = LegacyImportPlanner.Build(Config(json),
            LegacyImportSources.FromQuickBooks(Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), new[] { V("shield line"), V("HACIENDA"), V("IRS") }), report);
        Assert.Equal(new[] { "shield line" }, plan.Suppliers.Select(s => s.Name));
        Assert.Equal(2, report.Total(LegacyImportEntities.Suppliers).Skipped);
        Assert.Contains("El proveedor 'PADIMONT HOLDINGS CORP' de suppliers.includeNames no está en el archivo.", Warnings(report));
    }

    [Fact]
    public void Clients_report_unknown_terms_inactive_rows_and_missing_addresses()
    {
        QbCustomer C(string name, string status = "Active", string? terms = null, string? email = null, string?[]? bill = null) =>
            new(status, name, null, null, null, null, null, email, bill ?? new string?[5], new string?[5], terms, null, null);
        var report = new LegacyImportReport("X", true);
        var json = """{ "company": { "name": "X" }, "sources": { "products": "p.csv" } }""";
        var plan = LegacyImportPlanner.Build(Config(json), LegacyImportSources.FromQuickBooks(Array.Empty<QbItem>(), new[]
        {
            C("FARMACIA A", terms: "Net 90", email: "malo; bueno@example.com", bill: new string?[] { "FARMACIA A", "Calle 1", null, null, null }),
            C("FARMACIA B", status: "Not-active"),
            C("FARMACIA A"),
        }, Array.Empty<QbVendor>()), report);

        var a = Assert.Single(plan.Clients);
        Assert.Null(a.PaymentTerm);
        Assert.Equal(new[] { "bueno@example.com" }, a.Emails);   // 'malo' no tiene '@'
        Assert.Equal(LegacyImportRules.NoCity, a.Addresses.Single().Address.City);
        var warnings = Warnings(report);
        Assert.Contains("Término de pago de QuickBooks sin equivalente: 'Net 90'.", warnings);
        Assert.Contains("Cliente 'FARMACIA B' inactivo en QuickBooks; no se carga.", warnings);
        Assert.Contains("Dirección de 'FARMACIA A' sin línea de ciudad; se usa 'SIN CIUDAD'.", warnings);
        Assert.Contains(report.Rechazos, r => r.Message == "'FARMACIA A' está repetido en el archivo; se conserva la primera fila.");
        Assert.False(plan.RepField);   // sin representantes no se crea el campo
    }

    [Theory]
    [InlineData("(787) 249-8344", "787-249-8344")]
    [InlineData("787.686.6464", "787.686.6464")]
    [InlineData("787-249-8344", "787-249-8344")]
    [InlineData("1 (787) 555-0100", "1 (787) 555-0100")]   // ya es válido tal cual
    [InlineData("(1) 787 555 0100", "787-555-0100")]      // 11 dígitos con 1 inicial
    public void Contact_phone_is_accepted_by_the_contact_service_rules(string raw, string expected)
    {
        var (value, error) = LegacyImportPlanner.ContactPhone(raw);
        Assert.Null(error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null, "AxisCare")]
    [InlineData("", "AxisCare")]
    [InlineData("CARTONES", "CARTONES")]
    [InlineData("cartones", "CARTONES")]
    public void Category_resolution_matches_the_depot_rule(string? qbCategory, string expected)
    {
        var cfg = Config("""{ "company": { "name": "X" }, "sources": { "products": "p.csv" }, "products": { "defaultCategory": "AxisCare", "categoryByQuickBooksCategory": { "CARTONES": "CARTONES" } } }""");
        Assert.True(LegacyImportPlanner.ResolveCategory(cfg, qbCategory, out var category));
        Assert.Equal(expected, category);
        Assert.Equal(LegacyImportRules.DepotCategory(qbCategory), category);
        Assert.False(LegacyImportPlanner.ResolveCategory(cfg, "NATIONAL GUARD", out var none));
        Assert.Null(none);
    }

    [Fact]
    public void Expected_totals_follow_the_approved_plan()
    {
        var depot = LegacyImportPlanner.ExpectedTotals("Advance Depot");
        Assert.Equal((2, 552, 10, 1, 1, 6, 3887, 1310), (depot[LegacyImportEntities.Categories], depot[LegacyImportEntities.Products],
            depot[LegacyImportEntities.Suppliers], depot[LegacyImportEntities.Clients], depot[LegacyImportEntities.Consignees],
            depot[LegacyImportEntities.Zones], depot[LegacyImportEntities.Bins], depot[LegacyImportEntities.OpeningBalance]));
        var sol = LegacyImportPlanner.ExpectedTotals("Advance Solutions");
        Assert.Equal((1, 39, 6, 693, 743, 1, 1, 32), (sol[LegacyImportEntities.Categories], sol[LegacyImportEntities.Products],
            sol[LegacyImportEntities.Suppliers], sol[LegacyImportEntities.Clients], sol[LegacyImportEntities.Consignees],
            sol[LegacyImportEntities.Zones], sol[LegacyImportEntities.Bins], sol[LegacyImportEntities.OpeningBalance]));
        Assert.Empty(LegacyImportPlanner.ExpectedTotals("Compañía de prueba (migración)"));
    }

    [Fact]
    public void Describe_uses_the_exact_service_message()
    {
        Assert.Equal("Ya existe un proveedor con ese nombre.", LegacyImportService.Describe(new ConflictException("Ya existe un proveedor con ese nombre.")));
        Assert.Equal("El SKU es obligatorio. El nombre del producto es obligatorio.", LegacyImportService.Describe(new ValidationException(
            new Dictionary<string, string[]> { ["sku"] = new[] { "El SKU es obligatorio." }, ["name"] = new[] { "El nombre del producto es obligatorio." } })));
        Assert.Equal("Correo inválido.", LegacyImportService.Describe(new ValidationException("value", "Correo inválido.")));
    }

    // ================================================================ configuraciones reales

    private static LegacyImportConfig RealConfig(string file)
    {
        var path = Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", file);
        return LegacyImportConfig.Parse(File.ReadAllText(path), Path.GetDirectoryName(path)!, checkFiles: false);
    }

    [Fact]
    public void Depot_config_has_the_approved_values()
    {
        var cfg = RealConfig("import.depot.json");
        Assert.Equal(("Advance Depot", "Advance Depot Solutions", (string?)null), (cfg.Company.Name, cfg.Company.LegalName, cfg.Company.TaxId));
        Assert.Equal(new[] { "WMS_LOTSERIAL", "PURCHASING", "CATALOG", "ANALYTICS", "SYSTEM", "CUSTOM_FIELDS" }, cfg.Company.Modules);
        Assert.Equal(("teikem+admin@cerevelo.com", "Administrador Advance", "es"), (cfg.Company.AdminEmail, cfg.Company.AdminFullName, cfg.Company.Lang));
        Assert.Equal("AxisCare", cfg.Products.DefaultCategory);   // no 'ASSIST CARE'
        Assert.Equal("CARTONES", cfg.Products.CategoryByQuickBooksCategory["CARTONES"]);
        Assert.Equal(new[] { "SOLUTIONS", "NATIONAL GUARD" }, cfg.Products.ExcludeQuickBooksCategories);
        Assert.Equal(new[] { "Inventory Part", "Inventory Assembly" }, cfg.Products.Types);
        Assert.True(cfg.Products.CreateUnknownWmsSkusWithStock);
        Assert.Equal(("UN", "NONE"), (cfg.Products.BaseUom, cfg.Products.TrackingType));
        Assert.Equal(("LegacyMswm", "Main"), (cfg.Sources.Mswm!.ConnectionStringName, cfg.Sources.Mswm.WarehouseId));
        Assert.EndsWith("Depot Products.csv", cfg.Sources.Products);
        Assert.Equal(new[] { "prueba2" }, cfg.Clients.ExcludeNames);
        Assert.Equal(("ALM-DEPOT", "Almacén Depot", "Av. Shuford, PR-784", "Caguas", "PR"),
            (cfg.Warehouse.Code, cfg.Warehouse.Name, cfg.Warehouse.Line1, cfg.Warehouse.City, cfg.Warehouse.State));
        Assert.Equal(new[] { "PCK", "RSV", "ALM", "PISO", "STG", "SHP" }, cfg.Warehouse.Zones.Select(z => z.Code));
        Assert.Equal(new[] { "PICKING", "RESERVE", "RESERVE", "RESERVE", "STAGING", "STAGING" }, cfg.Warehouse.Zones.Select(z => z.ZoneType));
        Assert.Equal(new[] { "Picking Location", "REGULAR", "ADVANCE LOGISTICS M" }, cfg.Warehouse.Zones.Take(3).Select(z => z.MatchDescription));
        Assert.Equal(new[] { "PISO", "FLOOR", "CARTONES", "MATTRESS PISO DEPOT", "15006 MATTRESS PISO" }, cfg.Warehouse.Zones[3].MatchLocationIds);
        Assert.Equal(new[] { "R1" }, cfg.Warehouse.Zones[4].MatchLocationIds);
        Assert.Equal(new[] { "S1" }, cfg.Warehouse.Zones[5].MatchLocationIds);
        Assert.Equal(new[] { "W1", "Z1", "01", "R-1" }, cfg.Warehouse.SkipLocationIds);
        Assert.Null(cfg.Warehouse.SingleBin);
        // Lote 16 (D8, D12): Depot recibe con acomodo (sin modo = PUTAWAY) y su posición de recepción por defecto es R1 (zona STG).
        Assert.Null(cfg.Warehouse.ReceivingMode);
        Assert.Equal("R1", cfg.Warehouse.DefaultReceivingBin);
        Assert.Equal((LegacyImportConfig.SourceMswm, "OPENING_BALANCE", "Migración WMS MSWM 2026-09-30"),
            (cfg.OpeningBalances.Source, cfg.OpeningBalances.Reason, cfg.OpeningBalances.Notes));
    }

    [Fact]
    public void Solutions_config_has_the_approved_values()
    {
        var cfg = RealConfig("import.solutions.json");
        Assert.Equal(("Advance Solutions", "Advance Solutions Group"), (cfg.Company.Name, cfg.Company.LegalName));
        Assert.Equal(new[] { "WMS_LOTSERIAL", "PURCHASING", "CATALOG", "ANALYTICS", "SYSTEM", "CUSTOM_FIELDS" }, cfg.Company.Modules);
        Assert.Equal("teikem+admin@cerevelo.com", cfg.Company.AdminEmail);
        Assert.EndsWith("Solutions Items.csv", cfg.Sources.Products);
        Assert.EndsWith("Depot Products.csv", cfg.Sources.ExtraProducts!.Path);
        Assert.Equal("SOLUTIONS", cfg.Sources.ExtraProducts.WhereCategory);
        Assert.Null(cfg.Sources.Mswm);
        Assert.Equal(new[] { "Inventory Part" }, cfg.Products.Types);
        Assert.Equal("PRODUCTOS", cfg.Products.DefaultCategory);
        Assert.True(cfg.Products.CategoryFromManufacturer);   // la categoría es el fabricante
        Assert.Equal(new[] { "101010", "121212", "979" }, cfg.Products.ExcludeSkus);
        Assert.Equal(new[] { "ADVANCE LOGISTICS LLC", "GLOBAL IMPORT SALES LLC", "PADIMONT HOLDINGS CORP", "SHIELD LINE", "ADVANCE DEPOT SOLUTIONS", "FARMACIA CENTRAL DRUG" },
            cfg.Suppliers.IncludeNames);
        Assert.Equal(new[] { "1", "Alec" }, cfg.Clients.ExcludeNames);
        Assert.Equal(("ALM-SOL", "Almacén Solutions", "Caguas"), (cfg.Warehouse.Code, cfg.Warehouse.Name, cfg.Warehouse.City));
        Assert.Equal(("GEN", "General", "RESERVE", "GENERAL"),
            (cfg.Warehouse.SingleBin!.Zone!.Code, cfg.Warehouse.SingleBin.Zone.Name, cfg.Warehouse.SingleBin.Zone.ZoneType, cfg.Warehouse.SingleBin.Bin));
        // Lote 16 (D8): Solutions no tiene zona de recepción: directo a posición.
        Assert.Equal("DIRECT", cfg.Warehouse.ReceivingMode);
        Assert.Null(cfg.Warehouse.DefaultReceivingBin);
        Assert.Equal((LegacyImportConfig.SourceQuickBooks, "Saldo inicial QuickBooks 2026-09-30"), (cfg.OpeningBalances.Source, cfg.OpeningBalances.Notes));
    }

    [Theory]
    [InlineData("import.depot.json")]
    [InlineData("import.solutions.json")]
    [InlineData("sample/import.sample.json")]
    public void Configs_never_use_the_wrong_category_name(string file)
    {
        var text = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", file));
        Assert.DoesNotContain("ASSIST", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_data_has_no_real_contact_data()
    {
        var dir = Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", "sample");
        foreach (var file in Directory.EnumerateFiles(dir, "*.csv"))
        {
            var text = File.ReadAllText(file);
            // Solo dominios de documentación y números 555 (ficticios).
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"@([A-Za-z0-9.-]+)"))
                Assert.Equal("example.com", m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"787[-.]\d{3}[-.]\d{4}"))
                Assert.Contains("555", m.Value);
        }
    }

    // ================================================================ servicio completo en dry-run (EF InMemory)

    private static ServiceProvider BuildProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=unused;Database=unused",
            ["Jwt:SigningKey"] = "test-signing-key-test-signing-key-1234567890",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        services.RemoveAll<DbContextOptions<TeikemDbContext>>();
        services.RemoveAll<TeikemDbContext>();
        var dbName = "legacy-import-" + Guid.NewGuid();
        services.AddDbContext<TeikemDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Service_and_runner_are_registered()
    {
        var services = new ServiceCollection();
        services.AddTeikemInfrastructure(new ConfigurationBuilder().Build());
        Assert.Contains(services, d => d.ServiceType == typeof(LegacyImportService) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(LegacyImportRunner) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task Dry_run_of_the_sample_for_a_new_company_counts_everything_as_to_create_and_writes_nothing()
    {
        await using var provider = BuildProvider();
        // El catálogo es global (db-init ya lo siembra en producción): el dry-run de una compañía nueva también lo verifica
        // (D: anticipar los rechazos de la carga real), así que la base InMemory de esta prueba necesita los mismos códigos
        // que usa la muestra sintética (dos términos de pago y el motivo de ajuste del saldo inicial).
        using (var seedScope = provider.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            seedDb.Set<LookupCode>().AddRange(
                new LookupCode { Entity = "PaymentTerm", InternalCode = "NET30", LabelJson = "{}" },
                new LookupCode { Entity = "PaymentTerm", InternalCode = "CHEQUE", LabelJson = "{}" },
                new LookupCode { Entity = "AdjustmentReason", InternalCode = "OPENING_BALANCE", LabelJson = "{}" });
            await seedDb.SaveChangesAsync();
        }
        var cfg = LegacyImportConfig.Load(SampleConfigPath);
        LegacyImportReport report;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LegacyImportService>();
            report = await service.RunAsync(cfg, dryRun: true, default);
            Assert.Null(service.TemporaryAdminPassword);
        }

        Assert.True(report.DryRun);
        Assert.Contains(LegacyImportReport.DryRunMark, report.Title);
        Assert.False(report.HasSevereRejections);
        (int Created, int Existing) C(string e) => (report.Total(e).Created, report.Total(e).Existing);
        Assert.Equal((1, 0), C(LegacyImportEntities.Company));
        Assert.Equal((2, 0), C(LegacyImportEntities.Categories));
        Assert.Equal((1, 0), C(LegacyImportEntities.Warehouse));
        Assert.Equal((1, 0), C(LegacyImportEntities.Zones));
        Assert.Equal((1, 0), C(LegacyImportEntities.Bins));
        Assert.Equal((3, 0), C(LegacyImportEntities.Suppliers));
        Assert.Equal((5, 0), C(LegacyImportEntities.Products));
        Assert.Equal((2, 0), C(LegacyImportEntities.CustomFields));
        Assert.Equal((2, 0), C(LegacyImportEntities.Clients));
        Assert.Equal((3, 0), C(LegacyImportEntities.Consignees));
        Assert.Equal((5, 0), C(LegacyImportEntities.Contacts));
        Assert.Equal((4, 0), C(LegacyImportEntities.OpeningBalance));
        Assert.Equal(1670m, report.SaldoInicial.Sum(b => b.Quantity));

        // Nada se escribió: ni la compañía ni sus datos.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var tc = scope.ServiceProvider.GetRequiredService<Teikem.Infrastructure.Abstractions.TenantContext>();
            using var _ = tc.BypassTenantFilter();
            Assert.Equal(0, await db.Tenants.CountAsync());
            Assert.Equal(0, await db.Products.CountAsync());
            Assert.Equal(0, await db.Clients.CountAsync());
        }

        // El reporte se escribe (.md con la marca de simulación y un CSV por sección).
        var dir = Path.Combine(Path.GetTempPath(), "teikem-legacy-" + Guid.NewGuid());
        try
        {
            var paths = await report.WriteAsync(dir, cfg.ResolvedPrefix);
            Assert.EndsWith(".md", paths[0]);
            Assert.StartsWith("reporte-muestra-", Path.GetFileName(paths[0]));
            var md = await File.ReadAllTextAsync(paths[0]);
            Assert.Contains("SIMULACIÓN (dry-run)", md);
            Assert.Contains("| Productos | 6 | 5 | 0 | 1 | 0 |", md);
            Assert.Contains("El ítem SAM-006 tiene existencia negativa (-5); no se carga saldo inicial.", md);
            Assert.Equal(7, paths.Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// D51 (corregido 2026-09-29 tras una carga real: Advance Depot quedó con 0 unidades de inventario porque --update
    /// omitía el saldo inicial también en la primera carga, la que crea la compañía). --update SÍ carga el saldo inicial
    /// cuando la compañía se acaba de crear en esta misma corrida (RunState.CompanyAlreadyExisted = false); solo lo omite
    /// cuando la compañía YA existía antes de esta corrida. La rama "ya existía" no tiene aquí una prueba de extremo a
    /// extremo (exige sembrar el catálogo completo — EntityType, StatusCode de cliente, país, UserKind — para una
    /// segunda carga real; ver docs/lote10-decisiones.md); se revisó a mano leyendo ImportOpeningBalancesAsync.
    /// </summary>
    [Fact]
    public async Task Update_mode_still_loads_the_opening_balance_when_the_company_is_created_in_this_same_run()
    {
        await using var provider = BuildProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            seedDb.Set<LookupCode>().AddRange(
                new LookupCode { Entity = "PaymentTerm", InternalCode = "NET30", LabelJson = "{}" },
                new LookupCode { Entity = "PaymentTerm", InternalCode = "CHEQUE", LabelJson = "{}" },
                new LookupCode { Entity = "AdjustmentReason", InternalCode = "OPENING_BALANCE", LabelJson = "{}" });
            await seedDb.SaveChangesAsync();
        }
        var cfg = LegacyImportConfig.Load(SampleConfigPath);

        LegacyImportReport report;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LegacyImportService>();
            report = await service.RunAsync(cfg, dryRun: true, update: true, default);
        }
        Assert.False(report.HasSevereRejections);
        Assert.True(report.UpdateMode);
        Assert.Equal(4, report.Total(LegacyImportEntities.OpeningBalance).Created);
        Assert.Equal(1, report.Total(LegacyImportEntities.OpeningBalance).Skipped); // SAM-006 (existencia negativa) se omite al armar el plan, no por --update
        Assert.Equal(1670m, report.SaldoInicial.Sum(b => b.Quantity));
        Assert.DoesNotContain(report.Info, kv => kv.Key == "Saldo inicial" && kv.Value == LegacyImportPlanner.OpeningBalanceNotInUpdate);
    }

    // ================================================================ verbo CLI

    [Theory]
    [InlineData(new[] { "import-legacy", "a.json" }, "a.json", false)]
    [InlineData(new[] { "import-legacy", "a.json", "--dry-run" }, "a.json", true)]
    [InlineData(new[] { "import-legacy", "--dry-run", "a.json" }, "a.json", true)]
    [InlineData(new[] { "IMPORT-LEGACY", "a.json", "--DRY-RUN" }, "a.json", true)]
    public void Parse_args_accepts_the_documented_syntax(string[] args, string path, bool dryRun)
    {
        var parsed = LegacyImportRunner.ParseArgs(args);
        Assert.Null(parsed.Error);
        Assert.Equal((path, dryRun), (parsed.ConfigPath, parsed.DryRun));
    }

    [Theory]
    [InlineData("import-legacy")]
    [InlineData("import-legacy --dry-run")]
    [InlineData("import-legacy a.json b.json")]
    [InlineData("import-legacy a.json --force")]
    [InlineData("db-init")]
    public void Parse_args_rejects_anything_else_with_the_usage_message(string commandLine)
        => Assert.Equal("Uso: dotnet run --project src/Teikem.Api -- import-legacy <config.json> [--dry-run] [--update]",
            LegacyImportRunner.ParseArgs(commandLine.Split(' ')).Error);

    [Fact]
    public async Task Runner_exit_codes_for_bad_syntax_and_bad_configuration()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var runner = new LegacyImportRunner(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<LegacyImportRunner>.Instance);
        Assert.Equal(LegacyImportRunner.ExitUsage, await runner.RunAsync(new[] { "import-legacy" }));
        Assert.Equal(2, LegacyImportRunner.ExitUsage);
        Assert.Equal(LegacyImportRunner.ExitSevere, await runner.RunAsync(new[] { "import-legacy", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json") }));
        Assert.Equal(1, LegacyImportRunner.ExitSevere);
    }

    [Fact]
    public void Runner_resolves_repo_relative_config_paths()
    {
        var resolved = LegacyImportRunner.ResolveConfigPath(Path.Combine("docs", "migracion", "sample", "import.sample.json"));
        Assert.True(File.Exists(resolved), resolved);
    }

    [Fact]
    public void Summary_lists_every_entity_with_expected_totals()
    {
        var report = new LegacyImportReport("Advance Depot", true);
        LegacyImportPlanner.Build(Config(WmsConfigJson.Replace("Prueba WMS", "Advance Depot")), LegacyImportSources.FromQuickBooks(
            Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>()), report);
        var text = LegacyImportRunner.Summary(report);
        Assert.Contains("Entidad", text);
        Assert.Contains("Posiciones", text);
        Assert.Contains("3887", text);
        Assert.Contains("Rechazos: 0", text);
    }
}
