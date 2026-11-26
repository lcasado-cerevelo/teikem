using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Teikem.Domain.Clients;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — la sección del lote de logistica-db-update.sql contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base
/// desechable de <see cref="NotificationSqlFixture"/> (creada desde el modelo EF y sin FK). PrepareAsync crea dos compañías, deja la base «como antes del lote»
/// (sin las tablas del período, sin las columnas nuevas de ClientDocument, ClientTaxExemption y Tenant, y con el CHECK viejo de ClientNotificationSetting), crea
/// lo que la sección da por hecho (dbo.AppliedMigration, UQ_Product_IdTenant, UQ_ProductCategory_IdTenant, el DEFAULT de LookupCode.CreatedAtUtc y el tipo
/// RESELLER_CERTIFICATE sin marcas), deja una exención vieja y corre DOS veces los lotes GO de la sección (acotada al siguiente encabezado). Después se prueban
/// las FK COMPUESTAS (547), los índices únicos filtrados (2601), los CHECK y los datos sembrados.
/// </summary>
public sealed class Lote248SqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    internal const string Marker = "2026-11-27 — Lote 248 (Lote D del plan";
    private const string TenantAName = "Compañía A documentos (sección 248)";
    private const string TenantBName = "Compañía B documentos (sección 248)";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>La sección del lote: desde su encabezado hasta el de la siguiente (o el final del archivo).</summary>
    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 248 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    /// <summary>Las sentencias sin los comentarios (lo que de verdad corre).</summary>
    internal static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    internal static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    private static async Task ExecAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    private async Task<int> TenantIdAsync(SqlConnection c, string name)
        => (int)(await ScalarAsync(c, $"SELECT TenantId FROM dbo.Tenant WHERE Name = N'{name}'"))!;

    /// <summary>Idempotente: la primera vez deja la base como antes del lote, siembra lo que la sección necesita y corre la sección dos veces.</summary>
    private async Task PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            await using var c = await OpenAsync();
            if ((int)(await ScalarAsync(c, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_ClientDocument_IdTenantClient'"))! > 0) return;

            // dos compañías creadas ANTES del lote (con el modelo EF de hoy): al agregar las columnas toman los valores por omisión
            await fx.NewTenantAsync(TenantAName);
            await fx.NewTenantAsync(TenantBName);

            // la base como antes del lote: sin las tablas del período ni las columnas nuevas (con sus índices) que creó EnsureCreated desde el modelo EF
            await ExecAsync(c, """
                IF OBJECT_ID(N'dbo.TaxExemptionPeriodProduct', N'U') IS NOT NULL DROP TABLE dbo.TaxExemptionPeriodProduct;
                IF OBJECT_ID(N'dbo.TaxExemptionPeriodCategory', N'U') IS NOT NULL DROP TABLE dbo.TaxExemptionPeriodCategory;
                IF OBJECT_ID(N'dbo.TaxExemptionPeriod', N'U') IS NOT NULL DROP TABLE dbo.TaxExemptionPeriod;
                DECLARE @cols TABLE (T SYSNAME, C SYSNAME);
                INSERT @cols VALUES (N'ClientDocument', N'DocumentNumber'), (N'ClientDocument', N'ValidFrom'), (N'ClientDocument', N'ValidTo'),
                    (N'ClientDocument', N'UpdatedAtUtc'), (N'ClientDocument', N'UpdatedBy'), (N'ClientDocument', N'RowVersion'),
                    (N'ClientTaxExemption', N'ClientDocumentId'),
                    (N'Tenant', N'DocExpiryThresholdDays'), (N'Tenant', N'DocExpiryDailyAfterExpiry'), (N'Tenant', N'DocExpiryNotifyClient');
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.' + QUOTENAME(x.T) + N';'
                  FROM (SELECT DISTINCT i.object_id, i.name, k.T FROM sys.indexes i
                        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                        JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                        JOIN @cols k ON OBJECT_ID(N'dbo.' + k.T) = i.object_id AND col.name = k.C
                        WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0) x
                  JOIN sys.indexes i ON i.object_id = x.object_id AND i.name = x.name;
                SELECT @sql += N'ALTER TABLE dbo.' + QUOTENAME(k.T) + N' DROP CONSTRAINT ' + QUOTENAME(d.name) + N';'
                  FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
                  JOIN @cols k ON OBJECT_ID(N'dbo.' + k.T) = d.parent_object_id AND col.name = k.C;
                SELECT @sql += N'ALTER TABLE dbo.' + QUOTENAME(k.T) + N' DROP COLUMN ' + QUOTENAME(k.C) + N';'
                  FROM @cols k WHERE COL_LENGTH(N'dbo.' + k.T, k.C) IS NOT NULL;
                EXEC sp_executesql @sql;
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_LookupCode_CreatedAtUtc_L248Test')
                    ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_L248Test DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Product_IdTenant')
                    ALTER TABLE dbo.Product ADD CONSTRAINT UQ_Product_IdTenant UNIQUE (ProductId, TenantId);
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_ProductCategory_IdTenant')
                    ALTER TABLE dbo.ProductCategory ADD CONSTRAINT UQ_ProductCategory_IdTenant UNIQUE (ProductCategoryId, TenantId);
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
                    VALUES (N'ClientDocumentType', N'RESELLER_CERTIFICATE', N'{"es":"Certificado de revendedor","en":"Reseller certificate"}', 2, 1, 1);
                IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'MERCHANT_REGISTRATION')
                    INSERT dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
                    VALUES (N'ClientDocumentType', N'MERCHANT_REGISTRATION', N'{"es":"Registro de comerciante (SURI)","en":"Merchant registration (SURI)"}', 1, 1, 1);
                """);

            // el CHECK viejo de los avisos al cliente (Lote 98: sin los de almacén) con una fila que ya existe
            var a = await TenantIdAsync(c, TenantAName);
            await ExecAsync(c, $"""
                INSERT dbo.ClientNotificationSetting (TenantId, ClientId, Kind, EmailEnabled, AppEnabled, SmsEnabled, UpdatedAtUtc) VALUES ({a}, 900001, 'DELIVERED', 1, 0, 0, SYSUTCDATETIME());
                IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ClientNotificationSetting_Kind')
                    ALTER TABLE dbo.ClientNotificationSetting ADD CONSTRAINT CK_ClientNotificationSetting_Kind CHECK (Kind IN ('PICKED_UP','OUT_FOR_DELIVERY','DELIVERED','COD_PROCESSED','INVOICE_ISSUED'));
                -- una exención vieja (sin documento): la sección no la toca
                INSERT dbo.ClientTaxExemption (PublicId, TenantId, ClientId, ExemptionType, CertificateNumber, ValidFrom, ValidTo, IsActive, Scope, CreatedAtUtc)
                VALUES (NEWID(), {a}, 900001, N'Revendedor', N'VIEJA-1', '2026-01-01', NULL, 1, N'ALL', SYSUTCDATETIME());
                """);

            // dos corridas: la segunda no hace nada ni falla
            foreach (var _ in new[] { 1, 2 })
                foreach (var batch in Batches(Section()))
                    await ExecAsync(c, batch);
        }
        finally { Gate.Release(); }
    }

    private static async Task<int> InsertDocumentAsync(SqlConnection c, int tenantId, int clientId, string? validTo = null)
        => Convert.ToInt32(await ScalarAsync(c, $"""
            INSERT dbo.ClientDocument (PublicId, TenantId, ClientId, StoredFileId, DocumentTypeLookupId, IsActive, CreatedAtUtc, ValidTo)
            VALUES (NEWID(), {tenantId}, {clientId}, 1, 1, 1, SYSUTCDATETIME(), {(validTo is null ? "NULL" : $"'{validTo}'")});
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """));

    private static Task InsertExemptionAsync(SqlConnection c, int tenantId, int clientId, int documentId, bool isActive = true)
        => ExecAsync(c, $"""
            INSERT dbo.ClientTaxExemption (PublicId, TenantId, ClientId, ExemptionType, CertificateNumber, ValidFrom, ValidTo, IsActive, Scope, CreatedAtUtc, ClientDocumentId)
            VALUES (NEWID(), {tenantId}, {clientId}, N'Certificado', N'C-1', '2026-01-01', '2027-01-01', {(isActive ? 1 : 0)}, N'ALL', SYSUTCDATETIME(), {documentId});
            """);

    // ---------------------------------------------------------------- la sección corre dos veces y deja todo

    [SqlServerFact]
    public async Task Section_runs_twice_and_leaves_the_structure()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        // una tercera corrida tampoco falla ni duplica nada
        foreach (var batch in Batches(Section())) await ExecAsync(c, batch);

        foreach (var col in new[] { "DocumentNumber", "ValidFrom", "ValidTo", "UpdatedAtUtc", "UpdatedBy", "RowVersion" })
            Assert.NotNull(await ScalarAsync(c, $"SELECT COL_LENGTH(N'dbo.ClientDocument', N'{col}')"));
        Assert.Equal((short)120, await ScalarAsync(c, "SELECT COL_LENGTH(N'dbo.ClientDocument', N'DocumentNumber')"));   // NVARCHAR(60)
        Assert.Equal("date", await ScalarAsync(c, "SELECT TYPE_NAME(system_type_id) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ClientDocument') AND name = 'ValidTo'"));
        Assert.Equal(3, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.key_constraints k ON k.parent_object_id = ic.object_id AND k.unique_index_id = ic.index_id WHERE k.name = 'UQ_ClientDocument_IdTenantClient'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_ClientDocument_ValidTo' AND object_id = OBJECT_ID('dbo.ClientDocument') AND has_filter = 1"));
        Assert.Equal(3, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = OBJECT_ID('dbo.FK_ClientTaxExemption_Document')"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_ClientTaxExemption_Document' AND is_unique = 1 AND has_filter = 1"));
        foreach (var t in new[] { "TaxExemptionPeriod", "TaxExemptionPeriodProduct", "TaxExemptionPeriodCategory" })
            Assert.NotNull(await ScalarAsync(c, $"SELECT OBJECT_ID(N'dbo.{t}', N'U')"));
        foreach (var fk in new[] { "FK_TaxExemptionPeriodProduct_Period", "FK_TaxExemptionPeriodProduct_Product", "FK_TaxExemptionPeriodCategory_Period", "FK_TaxExemptionPeriodCategory_Category" })
            Assert.Equal(2, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = OBJECT_ID('dbo.{fk}')"));
        foreach (var ux in new[] { "UX_TaxExemptionPeriodProduct", "UX_TaxExemptionPeriodCategory" })
            Assert.Equal("([IsActive]=(1))", await ScalarAsync(c, $"SELECT filter_definition FROM sys.indexes WHERE name = '{ux}' AND is_unique = 1"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_TaxExemptionPeriod_PublicId' AND is_unique = 1"));

        // la exención vieja sigue igual (sin documento, sin «hasta», activa) y la marca informativa se anotó UNA vez
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.ClientTaxExemption WHERE CertificateNumber = N'VIEJA-1' AND ClientDocumentId IS NULL AND ValidTo IS NULL AND IsActive = 1"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code = 'L248_EXEMPTIONS_WITHOUT_DOCUMENT'"));
    }

    // ---------------------------------------------------------------- datos de catálogo

    [SqlServerFact]
    public async Task Seeds_the_ivu_types_the_entity_type_and_never_overwrites_an_edited_reseller()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        foreach (var code in new[] { ClientDocumentCodes.IvuExemptionState, ClientDocumentCodes.IvuExemptionCity, ClientDocumentCodes.IvuExemptionTotal })
        {
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'{code}' AND IsSystem = 1 AND IsActive = 1"));
            Assert.Equal("{\"requiresValidTo\":true,\"isTax\":true}", await ScalarAsync(c, $"SELECT ExtraJson FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'{code}'"));
        }
        Assert.Equal("{\"requiresValidTo\":false,\"isTax\":true}", await ScalarAsync(c, "SELECT ExtraJson FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE'"));
        Assert.Null(await ScalarAsync(c, "SELECT ExtraJson FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'MERCHANT_REGISTRATION'"));
        Assert.Equal(200, await ScalarAsync(c, $"SELECT SortOrder FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'{TaxCodes.ExemptionPeriodEntityType}'"));

        // un valor editado de RESELLER_CERTIFICATE no se pisa al volver a correr
        await ExecAsync(c, "UPDATE dbo.LookupCode SET ExtraJson = N'{\"requiresValidTo\":true,\"isTax\":true}' WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE'");
        try
        {
            foreach (var batch in Batches(Section())) await ExecAsync(c, batch);
            Assert.Equal("{\"requiresValidTo\":true,\"isTax\":true}", await ScalarAsync(c, "SELECT ExtraJson FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE'"));
            Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE'"));
        }
        finally
        {
            await ExecAsync(c, "UPDATE dbo.LookupCode SET ExtraJson = N'{\"requiresValidTo\":false,\"isTax\":true}' WHERE Entity = N'ClientDocumentType' AND InternalCode = N'RESELLER_CERTIFICATE'");
        }
    }

    // ---------------------------------------------------------------- documento y exención

    [SqlServerFact]
    public async Task Document_dates_check_and_the_composite_fk_and_unique_index_of_the_exemption()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var (a, b) = (await TenantIdAsync(c, TenantAName), await TenantIdAsync(c, TenantBName));

        var bad = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"""
            INSERT dbo.ClientDocument (PublicId, TenantId, ClientId, StoredFileId, DocumentTypeLookupId, IsActive, CreatedAtUtc, ValidFrom, ValidTo)
            VALUES (NEWID(), {a}, 910001, 1, 1, 1, SYSUTCDATETIME(), '2026-12-31', '2026-01-01');
            """));
        Assert.Equal(547, bad.Number);
        Assert.Contains("CK_ClientDocument_Dates", bad.Message);

        var doc = await InsertDocumentAsync(c, a, 910001, "2027-01-01");
        // el documento de otro cliente de la misma compañía, o de otra compañía → 547
        var otherClient = await Assert.ThrowsAsync<SqlException>(() => InsertExemptionAsync(c, a, 910002, doc));
        Assert.Equal(547, otherClient.Number);
        Assert.Contains("FK_ClientTaxExemption_Document", otherClient.Message);
        var otherTenant = await Assert.ThrowsAsync<SqlException>(() => InsertExemptionAsync(c, b, 910001, doc));
        Assert.Equal(547, otherTenant.Number);

        // el suyo sí; un segundo ACTIVO con el mismo documento → 2601; uno inactivo sí entra
        await InsertExemptionAsync(c, a, 910001, doc);
        var dup = await Assert.ThrowsAsync<SqlException>(() => InsertExemptionAsync(c, a, 910001, doc));
        Assert.Equal(2601, dup.Number);
        Assert.Contains("UX_ClientTaxExemption_Document", dup.Message);
        await InsertExemptionAsync(c, a, 910001, doc, isActive: false);
        Assert.Equal(2, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.ClientTaxExemption WHERE ClientDocumentId = {doc}"));
    }

    // ---------------------------------------------------------------- período por temporada

    [SqlServerFact]
    public async Task Period_checks_composite_fks_and_filtered_unique_indexes()
    {
        await PrepareAsync();
        var a = 0; var b = 0;
        await using (var c0 = await OpenAsync()) { a = await TenantIdAsync(c0, TenantAName); b = await TenantIdAsync(c0, TenantBName); }

        int productA, productB, categoryA, categoryB;
        await using (var db = fx.NewDb(new TenantContext()))
        {
            var catA = new ProductCategory { TenantId = a, Name = "Escolar 248" };
            var catB = new ProductCategory { TenantId = b, Name = "Escolar 248" };
            db.AddRange(catA, catB);
            await db.SaveChangesAsync();
            var pA = new Product { TenantId = a, Sku = "L248" + Guid.NewGuid().ToString("N")[..8], Name = "Libreta", BaseUomLookupId = 1, TrackingTypeLookupId = 1 };
            var pB = new Product { TenantId = b, Sku = "L248" + Guid.NewGuid().ToString("N")[..8], Name = "Libreta", BaseUomLookupId = 1, TrackingTypeLookupId = 1 };
            db.AddRange(pA, pB);
            await db.SaveChangesAsync();
            (productA, productB, categoryA, categoryB) = (pA.ProductId, pB.ProductId, catA.ProductCategoryId, catB.ProductCategoryId);
        }

        await using var c = await OpenAsync();
        async Task<SqlException> Fails(string sql) => await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, sql));
        const string cols = "INSERT dbo.TaxExemptionPeriod (TenantId, Name, Reason, ValidFrom, ValidTo, Scope)";

        Assert.Contains("CK_TaxExemptionPeriod_Dates", (await Fails($"{cols} VALUES ({a}, N'Escolar', N'Orden', '2026-08-31', '2026-08-01', N'ALL');")).Message);
        Assert.Contains("CK_TaxExemptionPeriod_Scope", (await Fails($"{cols} VALUES ({a}, N'Escolar', N'Orden', '2026-08-01', '2026-08-31', N'FOO');")).Message);
        Assert.Contains("CK_TaxExemptionPeriod_Name", (await Fails($"{cols} VALUES ({a}, N'  ', N'Orden', '2026-08-01', '2026-08-31', N'ALL');")).Message);
        Assert.Contains("CK_TaxExemptionPeriod_Reason", (await Fails($"{cols} VALUES ({a}, N'Escolar', N'', '2026-08-01', '2026-08-31', N'ALL');")).Message);

        var period = Convert.ToInt32(await ScalarAsync(c, $"{cols} VALUES ({a}, N'Escolar', N'Orden administrativa', '2026-08-01', '2026-08-01', N'SELECTED'); SELECT CAST(SCOPE_IDENTITY() AS INT);"));
        Assert.NotNull(await ScalarAsync(c, $"SELECT PublicId FROM dbo.TaxExemptionPeriod WHERE TaxExemptionPeriodId = {period}"));   // NEWID() por omisión

        // producto y categoría de otra compañía, o el período bajo otra compañía → 547
        var crossProduct = await Fails($"INSERT dbo.TaxExemptionPeriodProduct (TenantId, TaxExemptionPeriodId, ProductId) VALUES ({a}, {period}, {productB});");
        Assert.Equal(547, crossProduct.Number);
        Assert.Contains("FK_TaxExemptionPeriodProduct_Product", crossProduct.Message);
        var crossPeriod = await Fails($"INSERT dbo.TaxExemptionPeriodProduct (TenantId, TaxExemptionPeriodId, ProductId) VALUES ({b}, {period}, {productB});");
        Assert.Contains("FK_TaxExemptionPeriodProduct_Period", crossPeriod.Message);
        var crossCategory = await Fails($"INSERT dbo.TaxExemptionPeriodCategory (TenantId, TaxExemptionPeriodId, ProductCategoryId) VALUES ({a}, {period}, {categoryB});");
        Assert.Contains("FK_TaxExemptionPeriodCategory_Category", crossCategory.Message);

        // los suyos sí; un segundo activo → 2601; quitar = IsActive 0 y volver a escogerlo entra
        await ExecAsync(c, $"INSERT dbo.TaxExemptionPeriodProduct (TenantId, TaxExemptionPeriodId, ProductId) VALUES ({a}, {period}, {productA});");
        await ExecAsync(c, $"INSERT dbo.TaxExemptionPeriodCategory (TenantId, TaxExemptionPeriodId, ProductCategoryId) VALUES ({a}, {period}, {categoryA});");
        var dupProduct = await Fails($"INSERT dbo.TaxExemptionPeriodProduct (TenantId, TaxExemptionPeriodId, ProductId) VALUES ({a}, {period}, {productA});");
        Assert.Equal(2601, dupProduct.Number);
        var dupCategory = await Fails($"INSERT dbo.TaxExemptionPeriodCategory (TenantId, TaxExemptionPeriodId, ProductCategoryId) VALUES ({a}, {period}, {categoryA});");
        Assert.Equal(2601, dupCategory.Number);
        await ExecAsync(c, $"UPDATE dbo.TaxExemptionPeriodProduct SET IsActive = 0 WHERE TaxExemptionPeriodId = {period} AND ProductId = {productA};");
        await ExecAsync(c, $"INSERT dbo.TaxExemptionPeriodProduct (TenantId, TaxExemptionPeriodId, ProductId) VALUES ({a}, {period}, {productA});");
        Assert.Equal(2, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.TaxExemptionPeriodProduct WHERE TaxExemptionPeriodId = {period}"));
    }

    // ---------------------------------------------------------------- ajustes de la compañía y avisos al cliente

    [SqlServerFact]
    public async Task Tenant_settings_defaults_and_check_and_the_widened_notification_kind_check()
    {
        await PrepareAsync();
        await using var c = await OpenAsync();
        var a = await TenantIdAsync(c, TenantAName);

        // las compañías que ya existían tomaron los valores por omisión
        Assert.Equal("30,15,5", await ScalarAsync(c, $"SELECT DocExpiryThresholdDays FROM dbo.Tenant WHERE TenantId = {a}"));
        Assert.Equal(false, await ScalarAsync(c, $"SELECT DocExpiryDailyAfterExpiry FROM dbo.Tenant WHERE TenantId = {a}"));
        Assert.Equal(false, await ScalarAsync(c, $"SELECT DocExpiryNotifyClient FROM dbo.Tenant WHERE TenantId = {a}"));
        Assert.Equal("30,15,5", DocumentExpiryCodes.DefaultThresholds);

        await ExecAsync(c, $"UPDATE dbo.Tenant SET DocExpiryThresholdDays = N'30,15,5,0,-1,-7' WHERE TenantId = {a};");
        var letters = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.Tenant SET DocExpiryThresholdDays = N'30,a' WHERE TenantId = {a};"));
        Assert.Equal(547, letters.Number);
        Assert.Contains("CK_Tenant_DocExpiryThresholdDays", letters.Message);
        await ExecAsync(c, $"UPDATE dbo.Tenant SET DocExpiryThresholdDays = N'30,15,5' WHERE TenantId = {a};");

        // el CHECK ampliado: los de almacén y el de documentos entran; un código inventado no
        foreach (var (kind, client) in new[] { (ClientNotificationKinds.WhReceived, 920001), (ClientNotificationKinds.WhDispatched, 920001), (ClientNotificationKinds.DocumentExpiring, 920001) })
            await ExecAsync(c, $"INSERT dbo.ClientNotificationSetting (TenantId, ClientId, Kind, EmailEnabled, AppEnabled, SmsEnabled, UpdatedAtUtc) VALUES ({a}, {client}, '{kind}', 1, 0, 0, SYSUTCDATETIME());");
        var foo = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"INSERT dbo.ClientNotificationSetting (TenantId, ClientId, Kind, EmailEnabled, AppEnabled, SmsEnabled, UpdatedAtUtc) VALUES ({a}, 920002, 'FOO', 1, 0, 0, SYSUTCDATETIME());"));
        Assert.Equal(547, foo.Number);
        Assert.Contains("CK_ClientNotificationSetting_Kind", foo.Message);
        // la fila que ya existía sigue ahí y hay un solo CHECK
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.ClientNotificationSetting WHERE TenantId = {a} AND ClientId = 900001 AND Kind = 'DELIVERED'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_ClientNotificationSetting_Kind'"));
        // todo lo que el dominio configura cabe en el CHECK
        var definition = (string)(await ScalarAsync(c, "SELECT definition FROM sys.check_constraints WHERE name = 'CK_ClientNotificationSetting_Kind'"))!;
        foreach (var kind in ClientNotificationKinds.Configurable) Assert.Contains($"'{kind}'", definition);
    }
}
