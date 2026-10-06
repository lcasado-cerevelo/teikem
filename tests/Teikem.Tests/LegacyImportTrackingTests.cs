using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Migration;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Migration;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Rentas RM (D5-b, Lote 30): seguimiento de los productos al migrarlos. Las casillas Serial/Lot de la lista de ítems de
/// QuickBooks fijan el seguimiento al CREAR un producto solo si la configuración lo pide (products.trackingFromColumns, apagado
/// por defecto); un producto con saldo inicial se crea con NONE porque ninguna fuente trae series ni lotes (el ledger no exige
/// series en los asientos) y se informa como candidato a "Convertir a serie"; un producto que ya existe nunca cambia de
/// seguimiento (D25). Plan puro con filas en memoria y el servicio completo sobre EF InMemory (dry-run y carga real).
/// </summary>
public sealed class LegacyImportTrackingTests
{
    private static LegacyImportConfig Config(string json) => LegacyImportConfig.Parse(json, Path.GetTempPath(), checkFiles: false);

    private static QbItem Item(string sku, string? serial = null, string? lot = null, string? qty = null)
        => new("Active", "Inventory Part", sku, "Equipo", qty, null, null, null, null, null, null) { Serial = serial, Lot = lot };

    private static LegacyImportSources Qb(params QbItem[] items)
        => LegacyImportSources.FromQuickBooks(items, Array.Empty<QbCustomer>(), Array.Empty<QbVendor>());

    private static string Products(string extra) => $$"""
        { "company": { "name": "Prueba RM" }, "sources": { "products": "p.csv" },
          "products": { "defaultCategory": "EQUIPOS"{{extra}} } }
        """;

    private static PlannedProduct P(LegacyImportPlan plan, string sku) => plan.Products.Single(p => p.Sku == sku);

    // ================================================================ lector y reglas puras

    [Fact]
    public void Reader_takes_the_serial_and_lot_columns_and_tolerates_files_without_them()
    {
        const string with = ",Active Status,Type,Item,Description,Quantity On Hand,Cost,Price,Serial,Lot\n"
                            + ",Active,Inventory Part,EQ-1,Cama,0,0,0,TRUE,FALSE\n"
                            + ",Active,Inventory Part,EQ-2,Bastón,0,0,0,,true\n";
        var items = QuickBooksCsvReader.ParseItems(with, "Depot Products.csv");
        Assert.Equal(("TRUE", "FALSE"), (items[0].Serial, items[0].Lot));
        Assert.Equal(((string?)null, "true"), (items[1].Serial, items[1].Lot));

        const string without = ",Active Status,Type,Item,Description,Quantity On Hand,Cost,Price\n,Active,Inventory Part,EQ-1,Cama,0,0,0\n";
        var plain = QuickBooksCsvReader.ParseItems(without, "Solutions Items.csv").Single();
        Assert.Equal(((string?)null, (string?)null), (plain.Serial, plain.Lot));

        // La muestra sintética tiene el formato exacto de QuickBooks: las dos columnas existen y vienen en FALSE.
        var sample = QuickBooksCsvReader.ReadItems(Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", "sample", "Sample Products.csv"));
        Assert.All(sample, i => Assert.Equal(("FALSE", "FALSE"), (i.Serial, i.Lot)));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("true", true)]
    [InlineData(" Yes ", true)]
    [InlineData("Y", true)]
    [InlineData("1", true)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("X", false)]
    public void Quickbooks_checkbox_is_true_only_for_true_values(string? raw, bool expected)
        => Assert.Equal(expected, LegacyImportRules.IsQuickBooksTrue(raw));

    [Theory]
    [InlineData("NONE", true, true, "TRUE", "TRUE", "SERIAL")]   // con las dos casillas gana SERIAL
    [InlineData("NONE", true, true, "FALSE", "TRUE", "LOT")]
    [InlineData("NONE", true, true, "FALSE", "FALSE", "NONE")]
    [InlineData("NONE", false, true, "TRUE", "FALSE", "NONE")]   // columna Serial no habilitada
    [InlineData("NONE", true, false, "FALSE", "TRUE", "NONE")]   // columna Lot no habilitada
    [InlineData("NONE", false, false, "TRUE", "TRUE", "NONE")]   // como antes del lote: la configuración manda
    [InlineData("LOT", true, false, "FALSE", "FALSE", "LOT")]    // casilla en FALSE = seguimiento de la configuración
    public void Tracking_comes_from_the_enabled_columns_and_falls_back_to_the_configuration(string configured, bool serialColumn,
        bool lotColumn, string? serial, string? lot, string expected)
        => Assert.Equal(expected, LegacyImportRules.TrackingFromColumns(configured, serialColumn, lotColumn, serial, lot));

    [Fact]
    public void Configuration_defaults_keep_the_columns_off_and_validate_the_tracking_type()
    {
        var cfg = Config(Products(""));
        Assert.Equal(TrackingTypes.None, cfg.Products.TrackingType);
        Assert.False(cfg.Products.TrackingFromColumns.Serial);
        Assert.False(cfg.Products.TrackingFromColumns.Lot);

        var on = Config(Products(""", "trackingType": " serial ", "trackingFromColumns": { "serial": true, "lot": true }"""));
        Assert.Equal(TrackingTypes.Serial, on.Products.TrackingType);
        Assert.True(on.Products.TrackingFromColumns.Serial && on.Products.TrackingFromColumns.Lot);

        var ex = Assert.Throws<ValidationException>(() => Config(Products(""", "trackingType": "SERIE" """)));
        Assert.Equal("products.trackingType debe ser NONE, LOT o SERIAL.", ex.Message);
        Assert.Equal(LegacyImportConfig.TrackingTypeInvalid, ex.Message);
    }

    [Fact]
    public void Real_configs_keep_tracking_none_and_the_columns_off()
    {
        // Decisión conservadora de RM: Depot y Solutions siguen migrando todo con NONE (decisión 4 de la migración); el dueño
        // enciende trackingFromColumns.serial en una copia del JSON si confirma que las casillas Serial son reales.
        foreach (var file in new[] { "import.depot.json", "import.solutions.json", Path.Combine("sample", "import.sample.json") })
        {
            var path = Path.Combine(TripCatalogTests.RepoRoot(), "docs", "migracion", file);
            var cfg = LegacyImportConfig.Parse(File.ReadAllText(path), Path.GetDirectoryName(path)!, checkFiles: false);
            Assert.Equal(TrackingTypes.None, cfg.Products.TrackingType);
            Assert.False(cfg.Products.TrackingFromColumns.Serial, file);
            Assert.False(cfg.Products.TrackingFromColumns.Lot, file);
        }
    }

    // ================================================================ plan

    [Fact]
    public void Without_the_option_marked_items_are_planned_with_none_as_before()
    {
        var plan = LegacyImportPlanner.Build(Config(Products("")), Qb(Item("EQ-1", "TRUE", "TRUE"), Item("EQ-2", "FALSE", "TRUE")),
            new LegacyImportReport("Prueba RM", dryRun: true));
        Assert.All(plan.Products, p => Assert.Equal((TrackingTypes.None, TrackingTypes.None), (p.RequestedTracking, p.TrackingType)));
    }

    [Fact]
    public void With_the_columns_on_new_products_without_stock_take_serial_or_lot()
    {
        var cfg = Config(Products(""", "trackingFromColumns": { "serial": true, "lot": true }"""));
        var plan = LegacyImportPlanner.Build(cfg, Qb(Item("EQ-1", "TRUE", "TRUE"), Item("EQ-2", "FALSE", "TRUE"), Item("EQ-3", "FALSE", "FALSE"),
            Item("EQ-4")), new LegacyImportReport("Prueba RM", dryRun: true));

        Assert.Equal(new[] { TrackingTypes.Serial, TrackingTypes.Lot, TrackingTypes.None, TrackingTypes.None }, plan.Products.Select(p => p.TrackingType));
        Assert.All(plan.Products, p => Assert.False(p.TrackingDowngraded));
        Assert.Empty(plan.Balances);
    }

    private const string MswmJson = """
        {
          "company": { "name": "Prueba RM" },
          "sources": { "products": "p.csv", "mswm": { "connectionStringName": "LegacyMswm", "warehouseId": "Main" } },
          "products": { "defaultCategory": "EQUIPOS", "createUnknownWmsSkusWithStock": true,
                        "trackingFromColumns": { "serial": true, "lot": true } },
          "warehouse": { "code": "ALM-RM", "zones": [ { "code": "PCK", "name": "Picking", "zoneType": "PICKING", "matchDescription": "Picking" } ] },
          "openingBalances": { "source": "mswm" }
        }
        """;

    [Fact]
    public void A_serial_or_lot_product_with_opening_balance_is_planned_with_none_and_keeps_its_balance()
    {
        var src = new LegacyImportSources(
            new[] { Item("EQ-1", "TRUE", "TRUE"), Item("EQ-2", "TRUE"), Item("EQ-3", lot: "TRUE"), Item("EQ-4") },
            Array.Empty<QbItem>(), Array.Empty<QbCustomer>(), Array.Empty<QbVendor>(), Array.Empty<WmsItem>(),
            new[] { new WmsLocation("01-A-01", 1, "Picking", false), new WmsLocation("01-A-02", 1, "Picking", false) },
            new[]
            {
                new WmsInventoryRow("01-A-01", "EQ-1", 2), new WmsInventoryRow("01-a-02", "eq-1", 1),
                new WmsInventoryRow("01-A-01", "EQ-3", 40), new WmsInventoryRow("01-A-02", "EQ-4", 5),
            },
            Array.Empty<WmsUpc>());
        var plan = LegacyImportPlanner.Build(Config(MswmJson), src, new LegacyImportReport("Prueba RM", dryRun: true));

        // EQ-1 (serie, 3 unidades en 2 posiciones) y EQ-3 (lote, 40) bajan a NONE; EQ-2 (serie sin existencia) queda SERIAL.
        var eq1 = P(plan, "EQ-1");
        Assert.Equal((TrackingTypes.Serial, TrackingTypes.None, true, 3m, 2), (eq1.RequestedTracking, eq1.TrackingType, eq1.TrackingDowngraded,
            eq1.DowngradedQuantity, eq1.DowngradedBins));
        Assert.Equal((TrackingTypes.Serial, false), (P(plan, "EQ-2").TrackingType, P(plan, "EQ-2").TrackingDowngraded));
        var eq3 = P(plan, "EQ-3");
        Assert.Equal((TrackingTypes.Lot, TrackingTypes.None, 40m, 1), (eq3.RequestedTracking, eq3.TrackingType, eq3.DowngradedQuantity, eq3.DowngradedBins));
        Assert.Equal(TrackingTypes.None, P(plan, "EQ-4").TrackingType);

        // El saldo inicial no se pierde: se carga sin serie como cualquier producto NONE.
        Assert.Equal(new[] { ("EQ-1", 2m), ("EQ-1", 1m), ("EQ-3", 40m), ("EQ-4", 5m) }, plan.Balances.Select(b => (b.Sku, b.Quantity)));
        // Ningún producto planeado con seguimiento queda con saldo: la invariante que protege la regla.
        var withStock = plan.Balances.Select(b => b.Key).ToHashSet();
        Assert.DoesNotContain(plan.Products, p => p.TrackingType != TrackingTypes.None && withStock.Contains(p.Key));
    }

    [Fact]
    public void A_configured_serial_tracking_also_downgrades_products_with_a_quickbooks_balance()
    {
        // Riesgo previo a RM: products.trackingType = SERIAL creaba todo SERIAL y el saldo inicial entraba sin series.
        const string json = """
            { "company": { "name": "Prueba RM" }, "sources": { "products": "p.csv" },
              "products": { "defaultCategory": "EQUIPOS", "trackingType": "SERIAL" },
              "warehouse": { "code": "ALM-RM", "singleBin": { "zone": { "code": "GEN", "name": "General", "zoneType": "RESERVE" }, "bin": "GENERAL" } },
              "openingBalances": { "source": "quickbooks" } }
            """;
        var plan = LegacyImportPlanner.Build(Config(json), Qb(Item("EQ-1", qty: "4"), Item("EQ-2", qty: "0"), Item("EQ-3", qty: "-2")),
            new LegacyImportReport("Prueba RM", dryRun: true));

        Assert.Equal((TrackingTypes.None, 4m, 1), (P(plan, "EQ-1").TrackingType, P(plan, "EQ-1").DowngradedQuantity, P(plan, "EQ-1").DowngradedBins));
        Assert.Equal(TrackingTypes.Serial, P(plan, "EQ-2").TrackingType);   // sin existencia
        Assert.Equal(TrackingTypes.Serial, P(plan, "EQ-3").TrackingType);   // existencia negativa: no hay saldo que cargar
    }

    [Fact]
    public void Downgrade_message_names_the_conversion_tool_for_serial_items()
    {
        Assert.Equal("El ítem EQ-1 está marcado como de serie pero tiene saldo inicial (3 unidades) y la fuente no trae sus números de serie; "
                     + "se crea con seguimiento NONE. Conviértalo después con \"Convertir a serie\".",
            LegacyImportRules.TrackingDowngraded("EQ-1", TrackingTypes.Serial, 3m));
        Assert.Equal("El ítem EQ-3 está marcado por lote pero tiene saldo inicial (40 unidades) y la fuente no trae sus lotes; se crea con seguimiento NONE.",
            LegacyImportRules.TrackingDowngraded("EQ-3", TrackingTypes.Lot, 40m));
        Assert.Equal("El producto EQ-1 ya existe con seguimiento NONE; la migración no cambia el seguimiento de un producto existente (en el origen "
                     + "está marcado SERIAL). Si es un equipo con número de serie, use \"Convertir a serie\".",
            LegacyImportRules.TrackingNotChanged("EQ-1", TrackingTypes.None, TrackingTypes.Serial));
    }

    // ================================================================ servicio (EF InMemory)

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
        var dbName = "legacy-tracking-" + Guid.NewGuid();
        // La carga real usa RunInTransactionAsync (PATCH del producto): InMemory no tiene transacciones, como en WmsFixture.
        services.AddDbContext<TeikemDbContext>(o => o.UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        return services.BuildServiceProvider();
    }

    /// <summary>Catálogo global mínimo: seguimiento, unidad UN, el motivo del saldo inicial y el origen de la conciliación.</summary>
    private static async Task SeedCatalogAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
        db.Set<LookupCode>().AddRange(
            new LookupCode { Entity = LookupDomains.TrackingType, InternalCode = TrackingTypes.None, LabelJson = "{}" },
            new LookupCode { Entity = LookupDomains.TrackingType, InternalCode = TrackingTypes.Lot, LabelJson = "{}" },
            new LookupCode { Entity = LookupDomains.TrackingType, InternalCode = TrackingTypes.Serial, LabelJson = "{}" },
            new LookupCode { Entity = LookupDomains.UnitOfMeasure, InternalCode = "UN", LabelJson = "{}" },
            new LookupCode { Entity = LookupDomains.AdjustmentReason, InternalCode = "OPENING_BALANCE", LabelJson = "{}" },
            // La carga real cierra con la conciliación (origen MIGRATION).
            new LookupCode { Entity = LookupDomains.ReconciliationTrigger, InternalCode = ReconciliationTriggers.Migration, LabelJson = "{}" });
        await db.SaveChangesAsync();
    }

    /// <summary>Configuración y lista de ítems en una carpeta temporal (sin clientes ni proveedores).</summary>
    private static (LegacyImportConfig Cfg, string Dir) WriteSources(string productsJson, string csvRows)
    {
        var dir = Path.Combine(Path.GetTempPath(), "teikem-rm-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Items.csv"), ",Active Status,Type,Item,Description,Quantity On Hand,Cost,Price,Serial,Lot\n" + csvRows);
        var json = $$"""
            { "company": { "name": "Prueba RM" }, "sources": { "products": "Items.csv" }, {{productsJson}},
              "report": { "outputDir": "out", "prefix": "rm" } }
            """;
        File.WriteAllText(Path.Combine(dir, "import.json"), json);
        return (LegacyImportConfig.Load(Path.Combine(dir, "import.json")), dir);
    }

    [Fact]
    public async Task Dry_run_of_a_new_company_reports_the_tracking_and_the_conversion_candidates_without_writing()
    {
        await using var provider = BuildProvider();
        await SeedCatalogAsync(provider);
        var (cfg, dir) = WriteSources("""
            "products": { "defaultCategory": "EQUIPOS", "trackingFromColumns": { "serial": true } },
            "warehouse": { "code": "ALM-RM", "singleBin": { "zone": { "code": "GEN", "name": "General", "zoneType": "RESERVE" }, "bin": "GENERAL" } },
            "openingBalances": { "source": "quickbooks" }
            """,
            ",Active,Inventory Part,EQ-1,Cama,3,0,0,TRUE,TRUE\n,Active,Inventory Part,EQ-2,Monitor,0,0,0,TRUE,FALSE\n,Active,Inventory Part,EQ-3,Bastón,7,0,0,FALSE,TRUE\n");
        try
        {
            LegacyImportReport report;
            using (var scope = provider.CreateScope())
                report = await scope.ServiceProvider.GetRequiredService<LegacyImportService>().RunAsync(cfg, dryRun: true, default);

            Assert.False(report.HasSevereRejections);
            Assert.Equal(3, report.Total(LegacyImportEntities.Products).Created);
            Assert.Contains(report.Mapeos, m => m.Kind == LegacyImportService.TrackingMapKind && m.From == "EQ-2" && m.To == TrackingTypes.Serial);
            Assert.Contains(report.Mapeos, m => m.Kind == LegacyImportService.SerialCandidateMapKind && m.From == "EQ-1"
                                                && m.To == "3 unidades en 1 posición(es); se crea con NONE");
            Assert.Contains(report.Advertencias, w => w.Key == "EQ-1" && w.Message == LegacyImportRules.TrackingDowngraded("EQ-1", TrackingTypes.Serial, 3m));
            // EQ-3 tiene Lot = TRUE pero la columna Lot no está habilitada: NONE sin advertencia.
            Assert.DoesNotContain(report.Mapeos, m => m.From == "EQ-3");
            Assert.DoesNotContain(report.Advertencias, w => w.Key == "EQ-3" && w.Message.Contains("seguimiento", StringComparison.Ordinal));
            Assert.Equal(10m, report.SaldoInicial.Sum(b => b.Quantity));   // el saldo de EQ-1 se carga igual (sin series)

            using var check = provider.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<TeikemDbContext>();
            using var _ = check.ServiceProvider.GetRequiredService<TenantContext>().BypassTenantFilter();
            Assert.Equal(0, await db.Tenants.CountAsync());
            Assert.Equal(0, await db.Products.CountAsync());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_real_update_run_creates_new_products_with_their_tracking_and_never_changes_an_existing_one()
    {
        await using var provider = BuildProvider();
        await SeedCatalogAsync(provider);
        int tenantId, noneId, serialId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
            var tc = scope.ServiceProvider.GetRequiredService<TenantContext>();
            noneId = (await db.LookupCodes.SingleAsync(l => l.Entity == LookupDomains.TrackingType && l.InternalCode == TrackingTypes.None)).LookupCodeId;
            serialId = (await db.LookupCodes.SingleAsync(l => l.Entity == LookupDomains.TrackingType && l.InternalCode == TrackingTypes.Serial)).LookupCodeId;
            var uomId = (await db.LookupCodes.SingleAsync(l => l.Entity == LookupDomains.UnitOfMeasure)).LookupCodeId;
            var tenant = new Tenant { Name = "Prueba RM" };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantId = tenant.TenantId;
            using (tc.As(tenantId))
            {
                // Producto de una carga anterior: NONE (como todo Depot hoy).
                db.Products.Add(new Product { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = "EQ-1", Name = "Cama", BaseUomLookupId = uomId, TrackingTypeLookupId = noneId });
                await db.SaveChangesAsync();
            }
        }
        var (cfg, dir) = WriteSources("""
            "products": { "defaultCategory": null, "trackingFromColumns": { "serial": true, "lot": true } }
            """,
            ",Active,Inventory Part,EQ-1,Cama eléctrica,0,0,0,TRUE,FALSE\n,Active,Inventory Part,EQ-2,Monitor,0,0,0,TRUE,FALSE\n"
            + ",Active,Inventory Part,EQ-3,Bastón,0,0,0,FALSE,FALSE\n,Active,Inventory Part,EQ-4,Gasas,0,0,0,FALSE,TRUE\n");
        try
        {
            LegacyImportReport report;
            using (var scope = provider.CreateScope())
                report = await scope.ServiceProvider.GetRequiredService<LegacyImportService>().RunAsync(cfg, dryRun: false, update: true, default);

            Assert.Empty(report.Rechazos);
            Assert.Equal(3, report.Total(LegacyImportEntities.Products).Created);
            Assert.Contains(report.Advertencias, w => w.Key == "EQ-1"
                && w.Message == LegacyImportRules.TrackingNotChanged("EQ-1", TrackingTypes.None, TrackingTypes.Serial));
            Assert.Contains(report.Mapeos, m => m.Kind == LegacyImportService.SerialCandidateMapKind && m.From == "EQ-1"
                                                && m.To == LegacyImportService.SerialCandidateExisting);
            // --update sí actualizó el nombre, pero no el seguimiento.
            Assert.Contains(report.Actualizaciones, u => u.Key == "EQ-1" && u.Field == "Nombre");
            Assert.DoesNotContain(report.Actualizaciones, u => u.Field.Contains("eguimiento", StringComparison.Ordinal));

            using var check = provider.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<TeikemDbContext>();
            using var _ = check.ServiceProvider.GetRequiredService<TenantContext>().As(tenantId);
            var byCode = await db.LookupCodes.Where(l => l.Entity == LookupDomains.TrackingType).ToDictionaryAsync(l => l.LookupCodeId, l => l.InternalCode);
            var tracking = await db.Products.ToDictionaryAsync(p => p.Sku, p => byCode[p.TrackingTypeLookupId]);
            Assert.Equal(TrackingTypes.None, tracking["EQ-1"]);     // existente: no cambia (D25)
            Assert.Equal(TrackingTypes.Serial, tracking["EQ-2"]);
            Assert.Equal(TrackingTypes.None, tracking["EQ-3"]);
            Assert.Equal(TrackingTypes.Lot, tracking["EQ-4"]);
            Assert.Equal("Cama eléctrica", (await db.Products.SingleAsync(p => p.Sku == "EQ-1")).Name);
            Assert.NotEqual(serialId, noneId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
