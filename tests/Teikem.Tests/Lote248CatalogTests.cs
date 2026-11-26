using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — la integración compartida del lote: los textos y guardas de su sección de logistica-db-update.sql (leída SOLO hasta la
/// siguiente, SqlSections.From), que no hay permisos nuevos, el permiso de lectura de la fuente CLIENT_DOCUMENT, la clave del trabajo y los registros de
/// DependencyInjection (servicios, fuentes de aviso, trabajo, eventos, contenido y fuente de datos).
/// </summary>
public sealed class Lote248CatalogTests
{
    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    private static string Section() => Lote248SqlTests.Section();
    private static string Code() => Lote248SqlTests.Statements(Section());

    // ---------------------------------------------------------------- sección SQL

    [Fact]
    public void The_section_goes_after_lote_247_names_its_decisions_and_says_it_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Lote248SqlTests.Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 247: Precios especiales y ofertas por cliente y por cadena", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("2026-11-25 — Lote 247", StringComparison.Ordinal) > 0);
        var section = Section();
        Assert.Contains("(docs/lote248-decisiones.md)", section);
        Assert.Contains("Solo AGREGA (y amplía un CHECK). Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
        // el corte de UpdateSqlSection conoce la sección (lleva DROP CONSTRAINT a propósito)
        Assert.Contains("2026-11-27 — Lote 248", section);
    }

    [Fact]
    public void Every_structure_change_has_its_guard()
    {
        var code = Code();
        // cada columna nueva con su COL_LENGTH
        foreach (Match m in Regex.Matches(code, @"ALTER TABLE dbo\.(\w+) ADD (?!CONSTRAINT\b)(\w+) "))
            Assert.Contains($"IF COL_LENGTH(N'dbo.{m.Groups[1].Value}', N'{m.Groups[2].Value}') IS NULL\n    ALTER TABLE dbo.{m.Groups[1].Value} ADD {m.Groups[2].Value} ", code);
        // cada tabla con su OBJECT_ID
        var tables = Regex.Matches(code, @"CREATE TABLE dbo\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "TaxExemptionPeriod", "TaxExemptionPeriodProduct", "TaxExemptionPeriodCategory" }, tables);
        foreach (var t in tables) Assert.Contains($"IF OBJECT_ID(N'dbo.{t}', N'U') IS NULL CREATE TABLE dbo.{t} (", code);
        // cada índice con su sys.indexes
        foreach (Match m in Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+) ON dbo\.(\w+)"))
            Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{m.Groups[1].Value}' AND object_id = OBJECT_ID('dbo.{m.Groups[2].Value}'))", code);
        // restricciones agregadas a tablas que ya existen, cada una con su guarda
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ClientDocument_Dates' AND parent_object_id = OBJECT_ID('dbo.ClientDocument'))", code);
        Assert.Contains("CHECK (ValidTo IS NULL OR ValidFrom IS NULL OR ValidTo >= ValidFrom)", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_ClientDocument_IdTenantClient' AND parent_object_id = OBJECT_ID('dbo.ClientDocument'))", code);
        Assert.Contains("UNIQUE (ClientDocumentId, TenantId, ClientId)", code);
        Assert.Contains("IF OBJECT_ID(N'dbo.FK_ClientTaxExemption_Document', N'F') IS NULL", code);
        Assert.Contains("FOREIGN KEY (ClientDocumentId, TenantId, ClientId) REFERENCES dbo.ClientDocument(ClientDocumentId, TenantId, ClientId)", code);
        Assert.Contains("IF OBJECT_ID(N'dbo.CK_Tenant_DocExpiryThresholdDays', N'C') IS NULL", code);
        // FK compuestas del período (nunca a la fila de otra compañía)
        Assert.Contains("FOREIGN KEY (TaxExemptionPeriodId, TenantId) REFERENCES dbo.TaxExemptionPeriod(TaxExemptionPeriodId, TenantId)", code);
        Assert.Contains("FOREIGN KEY (ProductId, TenantId) REFERENCES dbo.Product(ProductId, TenantId)", code);
        Assert.Contains("FOREIGN KEY (ProductCategoryId, TenantId) REFERENCES dbo.ProductCategory(ProductCategoryId, TenantId)", code);
        // lo que nombra columnas recién agregadas va en un lote GO aparte (cada sentencia en el suyo)
        Assert.Equal(28, Lote248SqlTests.Batches(Section()).Count);
    }

    [Fact]
    public void The_section_adds_no_permission_nor_rule_and_deletes_nothing()
    {
        var code = Code();
        foreach (var word in new[] { "dbo.Permission", "dbo.RolePermission", "dbo.NotificationRule", "dbo.ScheduledJob", "DELETE ", "DROP TABLE", "DROP COLUMN", "DROP INDEX", "ALTER COLUMN", "UPDATE dbo.ClientTaxExemption" })
            Assert.DoesNotContain(word, code, StringComparison.OrdinalIgnoreCase);
        // el único DROP: el CHECK de los avisos al cliente, que se vuelve a crear ampliado
        Assert.Single(Regex.Matches(code, @"\bDROP\b", RegexOptions.IgnoreCase));
        Assert.Contains("ALTER TABLE dbo.ClientNotificationSetting DROP CONSTRAINT CK_ClientNotificationSetting_Kind;", code);
        Assert.Contains("CHECK (Kind IN ('PICKED_UP','OUT_FOR_DELIVERY','DELIVERED','WH_RECEIVED','WH_DISPATCHED','COD_PROCESSED','INVOICE_ISSUED','DOCUMENT_EXPIRING'))", code);
        foreach (var kind in ClientNotificationKinds.Configurable) Assert.Contains($"'{kind}'", code);
        // los únicos UPDATE: la marca de RESELLER_CERTIFICATE, solo si estaba vacía
        Assert.Single(Regex.Matches(code, @"\bUPDATE\b", RegexOptions.IgnoreCase));
        Assert.Contains("WHEN MATCHED AND t.ExtraJson IS NULL AND t.InternalCode = N'RESELLER_CERTIFICATE' THEN", code);
        // la marca informativa se anota una sola vez
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L248_EXEMPTIONS_WITHOUT_DOCUMENT')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L248_EXEMPTIONS_WITHOUT_DOCUMENT');", code);
    }

    [Fact]
    public void The_section_seeds_the_ivu_document_types_with_their_marks_and_the_entity_type()
    {
        var code = Code();
        var marks = "N'{\"requiresValidTo\":true,\"isTax\":true}'";
        Assert.Matches($@"\(N'{ClientDocumentCodes.IvuExemptionState}',\s+N'\{{""es"":""Certificado de exención de IVU estatal"",""en"":""IVU exemption certificate \(state\)""\}}',\s+3, {Regex.Escape(marks)}\)", code);
        Assert.Matches($@"\(N'{ClientDocumentCodes.IvuExemptionCity}',\s+N'[^']+',\s+4, {Regex.Escape(marks)}\)", code);
        Assert.Matches($@"\(N'{ClientDocumentCodes.IvuExemptionTotal}',\s+N'[^']+',\s+5, {Regex.Escape(marks)}\)", code);
        Assert.Matches(@"\(N'RESELLER_CERTIFICATE',\s+N'[^']+',\s+2, N'\{""requiresValidTo"":false,""isTax"":true\}'\)", code);
        Assert.DoesNotContain("N'MERCHANT_REGISTRATION'", code);
        Assert.Contains("ON t.Entity = N'ClientDocumentType' AND t.InternalCode = s.Code", code);
        Assert.Contains(ClientDocumentCodes.IvuExemptionTotal, ClientDocumentCodes.Types);
        Assert.Equal("requiresValidTo", ClientDocumentTypeMarks.RequiresValidToKey);
        Assert.Equal("isTax", ClientDocumentTypeMarks.IsTaxKey);

        Assert.Contains($"(N'{TaxCodes.ExemptionPeriodEntityType}', N'{{\"es\":\"Período de exención de impuesto\",\"en\":\"Tax exemption period\"}}', 200)", code);
        Assert.Contains("ON t.Entity = N'EntityType' AND t.InternalCode = s.Code", code);
        // el 199 es del Lote 247: el 200 no lo usa otro EntityType de update.sql
        Assert.DoesNotMatch(@"\(N'(?!TAX_EXEMPTION_PERIOD)[A-Z_]+', N'\{[^}]*\}', 200\)", UpdateSql());
    }

    // ---------------------------------------------------------------- permisos, trabajo y fuente

    [Fact]
    public void No_new_permissions_and_the_client_document_source_reads_with_clients_read()
    {
        Assert.Equal(PermissionCatalog.ClientsRead, PermissionCatalog.DataSourceReadPermission(ClientDocumentCodes.EntityType));
        Assert.Equal(PermissionCatalog.ClientsRead, PermissionCatalog.DataSourceExtraReadPermission[ClientDocumentCodes.EntityType]);
        // NO es permiso de dueño: exigiría un IOwnedEntityResolver (OwnedEntityResolverCoverageTests)
        Assert.False(PermissionCatalog.OwnerReadPermission.ContainsKey(ClientDocumentCodes.EntityType));
        Assert.False(PermissionCatalog.OwnerWritePermission.ContainsKey(ClientDocumentCodes.EntityType));
        Assert.DoesNotContain(PermissionCatalog.All, p => p.Code.Contains("document", StringComparison.OrdinalIgnoreCase) && p.Code.StartsWith("clients.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_job_key_is_shared()
    {
        Assert.Equal("DOCUMENT_EXPIRY_SCAN", JobKeys.DocumentExpiryScan);
        Assert.Equal(JobKeys.DocumentExpiryScan, DocumentExpiryScanJob.JobKey);
        Assert.Equal(JobKeys.DocumentExpiryScan, new DocumentExpiryScanJob().Key);
    }

    // ---------------------------------------------------------------- DependencyInjection

    private static (IServiceCollection Services, ServiceProvider Provider) Container()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        return (services, services.BuildServiceProvider());
    }

    [Fact]
    public void Dependency_injection_registers_every_piece_of_the_lot()
    {
        var (services, provider) = Container();
        using var _ = provider;
        void Has(Type service, Type impl, ServiceLifetime lifetime)
            => Assert.Single(services, d => d.ServiceType == service && d.ImplementationType == impl && d.Lifetime == lifetime);

        foreach (var t in new[] { typeof(ClientDocumentExpiryService), typeof(ClientTaxExemptionCaptureService), typeof(TaxExemptionPeriodService), typeof(SalesTaxNoticeService), typeof(DocumentExpiryScanService) })
            Has(t, t, ServiceLifetime.Scoped);
        Has(typeof(IExpiryNoticeSource), typeof(ClientDocumentExpirySource), ServiceLifetime.Scoped);
        Has(typeof(IExpiryNoticeSource), typeof(SpecialPriceExpirySource), ServiceLifetime.Scoped);
        Has(typeof(IScheduledJob), typeof(DocumentExpiryScanJob), ServiceLifetime.Singleton);
        Has(typeof(INotificationEventProvider), typeof(ClientDocumentEventProvider), ServiceLifetime.Singleton);
        Has(typeof(INotificationEventProvider), typeof(SpecialPriceEventProvider), ServiceLifetime.Singleton);
        Has(typeof(INotificationContentProvider), typeof(ClientDocumentNoticeContent), ServiceLifetime.Scoped);
        Has(typeof(INotificationContentProvider), typeof(SpecialPriceNoticeContent), ServiceLifetime.Scoped);
        Has(typeof(IDataSource), typeof(ClientDocumentDataSource), ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_container_resolves_the_job_the_events_the_sources_and_the_services()
    {
        var (_, provider) = Container();
        using var __ = provider;
        Assert.Single(provider.GetServices<IScheduledJob>(), j => j.Key == JobKeys.DocumentExpiryScan);
        var catalog = provider.GetRequiredService<NotificationEventCatalog>();
        foreach (var code in ClientDocumentEventCodes.All.Concat(SpecialPriceEventCodes.All)) Assert.NotNull(catalog.Find(code));

        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var sources = sp.GetServices<IExpiryNoticeSource>().ToList();
        Assert.Equal(new[] { typeof(ClientDocumentExpirySource), typeof(SpecialPriceExpirySource) }, sources.Select(s => s.GetType()));
        Assert.Equal((ModuleKeys.Catalog, true), (sources[0].ModuleKey, sources[0].NotifyAfterExpiry));
        Assert.Equal((ModuleKeys.SalesOrders, false), (sources[1].ModuleKey, sources[1].NotifyAfterExpiry));
        foreach (var t in new[] { typeof(ClientDocumentExpiryService), typeof(ClientTaxExemptionCaptureService), typeof(TaxExemptionPeriodService), typeof(SalesTaxNoticeService), typeof(DocumentExpiryScanService) })
            Assert.NotNull(sp.GetRequiredService(t));
        var contents = sp.GetServices<INotificationContentProvider>().ToList();
        foreach (var code in ClientDocumentEventCodes.All) Assert.IsType<ClientDocumentNoticeContent>(Assert.Single(contents, c => c.EventCodes.Contains(code)));
        Assert.IsType<SpecialPriceNoticeContent>(Assert.Single(contents, c => c.EventCodes.Contains(SpecialPriceEventCodes.Expiring)));
        var source = Assert.Single(sp.GetServices<IDataSource>(), s => s.Key == ClientDocumentCodes.EntityType);
        Assert.IsType<ClientDocumentDataSource>(source);
    }
}
