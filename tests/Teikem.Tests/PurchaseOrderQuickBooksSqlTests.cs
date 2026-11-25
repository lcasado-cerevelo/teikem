using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions, P1) — la base compartida del lote: el permiso purchasing.quickbooks (catálogo, plantillas, orden, «editar ⇒ ver», propagación),
/// el modelo EF de las columnas nuevas (PurchaseOrder, Supplier, Tenant) y los textos de su sección de logistica-db-update.sql, leída SOLO hasta la siguiente
/// (SqlSections.From). La corrida contra SQL Server real está en <see cref="PurchaseOrderQuickBooksSqlServerTests"/>.
/// </summary>
public sealed class PurchaseOrderQuickBooksSqlTests
{
    internal const string Marker = "2026-11-25 — Lote 246 (Lote F del plan de Solutions): Compras — plantilla de importación del usuario";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>La sección: desde su comentario de encabezado hasta el de la siguiente (o el final del archivo).</summary>
    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 246 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    /// <summary>Las sentencias sin los comentarios (lo que de verdad corre).</summary>
    internal static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    internal static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    // ---------------------------------------------------------------- permiso

    [Fact]
    public void Purchasing_quickbooks_is_a_billing_permission_of_tenant_admin_and_billing_only()
    {
        Assert.Equal("purchasing.quickbooks", PermissionCatalog.PurchasingQuickBooks);
        var def = Assert.Single(PermissionCatalog.All, p => p.Code == PermissionCatalog.PurchasingQuickBooks);
        Assert.Equal("BILLING", def.Category);
        Assert.Equal("Compras: pasar órdenes de compra a QuickBooks", def.LabelEs);
        Assert.Equal("Purchases: pass purchase orders to QuickBooks", def.LabelEn);
        Assert.Contains(PermissionCatalog.PurchasingQuickBooks, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        Assert.Contains(PermissionCatalog.PurchasingQuickBooks, PermissionCatalog.RoleTemplates["Billing"]);
        foreach (var role in new[] { "Sales", "SalesOffice", "WarehouseOperator", "Dispatcher" })
            if (PermissionCatalog.RoleTemplates.TryGetValue(role, out var codes)) Assert.DoesNotContain(PermissionCatalog.PurchasingQuickBooks, codes);
        Assert.True(PermissionCatalog.RoleTemplates.ContainsKey("Sales") && PermissionCatalog.RoleTemplates.ContainsKey("SalesOffice"));
    }

    [Fact]
    public void Purchasing_quickbooks_goes_after_purchasing_receive_implies_purchasing_view_and_propagates_to_billing()
    {
        Assert.Contains(PermissionCatalog.PurchasingQuickBooks, PermissionDisplayOrder.Codes);
        Assert.Equal(PermissionDisplayOrder.IndexOf(PermissionCatalog.PurchasingReceive) + 1, PermissionDisplayOrder.IndexOf(PermissionCatalog.PurchasingQuickBooks));
        Assert.Equal(new[] { PermissionCatalog.PurchasingView }, MenuPermissions.EditImplies[PermissionCatalog.PurchasingQuickBooks]);
        var implied = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.PurchasingQuickBooks };
        PermissionCatalog.ExpandImplied(implied);
        Assert.Contains(PermissionCatalog.PurchasingView, implied);

        var fresh = new HashSet<string> { PermissionCatalog.PurchasingQuickBooks };
        Assert.Equal(new[] { PermissionCatalog.PurchasingQuickBooks }, PermissionCatalog.CodesToPropagate("Billing", fresh, new HashSet<string>()));
        Assert.Equal(new[] { PermissionCatalog.PurchasingQuickBooks }, PermissionCatalog.CodesToPropagate("TenantAdmin", fresh, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Sales", fresh, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Billing", fresh, fresh));   // ya lo tiene: solo agrega
        // la fuente de datos PURCHASE_ORDER se lee con purchasing.view
        Assert.Equal(PermissionCatalog.PurchasingView, PermissionCatalog.DataSourceReadPermission(EntityTypes.PurchaseOrder));
    }

    // ---------------------------------------------------------------- modelo EF

    [Fact]
    public void The_ef_model_maps_the_new_columns_indexes_and_the_origin_fk()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var po = db.Model.FindEntityType(typeof(PurchaseOrder))!;
        Assert.Equal(250, po.FindProperty(nameof(PurchaseOrder.ImportKey))!.GetMaxLength());
        foreach (var prop in new[] { nameof(PurchaseOrder.OriginLookupId), nameof(PurchaseOrder.ImportKey), nameof(PurchaseOrder.QuickBooksAt), nameof(PurchaseOrder.QuickBooksBy),
                     nameof(PurchaseOrder.QuickBooksExportedAt), nameof(PurchaseOrder.QuickBooksExportedBy) })
            Assert.True(po.FindProperty(prop)!.IsNullable, prop);

        var key = Assert.Single(po.GetIndexes(), i => i.GetDatabaseName() == "UX_PurchaseOrder_ImportKey");
        Assert.True(key.IsUnique);
        Assert.Equal(new[] { "TenantId", "ImportKey" }, key.Properties.Select(p => p.Name));
        Assert.Equal("[ImportKey] IS NOT NULL AND [IsActive] = 1", key.GetFilter());
        var pending = Assert.Single(po.GetIndexes(), i => i.GetDatabaseName() == "IX_PurchaseOrder_QuickBooksPending");
        Assert.False(pending.IsUnique);
        Assert.Equal(new[] { "TenantId", "StatusCodeId" }, pending.Properties.Select(p => p.Name));
        Assert.Equal("[QuickBooksAt] IS NULL AND [IsActive] = 1", pending.GetFilter());

        var fk = Assert.Single(po.GetForeignKeys(), f => f.Properties.Single().Name == nameof(PurchaseOrder.OriginLookupId));
        Assert.Equal(typeof(LookupCode), fk.PrincipalEntityType.ClrType);
        Assert.False(fk.IsRequired);

        Assert.True(db.Model.FindEntityType(typeof(Supplier))!.FindProperty(nameof(Supplier.QuickBooksSentAt))!.IsNullable);
        Assert.Equal(159, db.Model.FindEntityType(typeof(Tenant))!.FindProperty(nameof(Tenant.QuickBooksPurchaseOrderAccount))!.GetMaxLength());
    }

    [Fact]
    public void The_quickbooks_columns_are_audited_like_the_invoice()
    {
        foreach (var prop in new[] { nameof(PurchaseOrder.OriginLookupId), nameof(PurchaseOrder.ImportKey), nameof(PurchaseOrder.QuickBooksAt), nameof(PurchaseOrder.QuickBooksBy),
                     nameof(PurchaseOrder.QuickBooksExportedAt), nameof(PurchaseOrder.QuickBooksExportedBy) })
            Assert.Null(typeof(PurchaseOrder).GetProperty(prop)!.GetCustomAttribute<NotAuditedAttribute>());
        Assert.Null(typeof(Supplier).GetProperty(nameof(Supplier.QuickBooksSentAt))!.GetCustomAttribute<NotAuditedAttribute>());
    }

    // ---------------------------------------------------------------- sección SQL

    [Fact]
    public void The_section_goes_after_lote_245_names_its_decisions_and_says_it_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 245: Cadenas de clientes", StringComparison.Ordinal));
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 244 (Lote A del plan de rutas)", StringComparison.Ordinal));
        var section = Section();
        Assert.Contains("(docs/lote246-decisiones.md)", section);
        Assert.Contains("Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
    }

    [Fact]
    public void Every_structure_change_has_its_guard()
    {
        var code = Statements(Section());
        foreach (var (table, column) in new[]
                 {
                     ("PurchaseOrder", "OriginLookupId"), ("PurchaseOrder", "ImportKey"), ("PurchaseOrder", "QuickBooksAt"), ("PurchaseOrder", "QuickBooksBy"),
                     ("PurchaseOrder", "QuickBooksExportedAt"), ("PurchaseOrder", "QuickBooksExportedBy"), ("Supplier", "QuickBooksSentAt"), ("Tenant", "QuickBooksPurchaseOrderAccount"),
                 })
            Assert.Contains($"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL\n    ALTER TABLE dbo.{table} ADD {column} ", code);
        Assert.Contains("ADD ImportKey NVARCHAR(250) NULL;", code);
        Assert.Contains("ADD QuickBooksPurchaseOrderAccount NVARCHAR(159) NULL;", code);
        Assert.Contains("CONSTRAINT FK_PurchaseOrder_Origin REFERENCES dbo.LookupCode(LookupCodeId)", code);

        foreach (var ck in new[] { "CK_PurchaseOrder_QbBy", "CK_PurchaseOrder_QbExportedBy" })
            Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = '{ck}' AND parent_object_id = OBJECT_ID('dbo.PurchaseOrder'))", code);
        Assert.Contains("CHECK (QuickBooksBy IS NULL OR QuickBooksAt IS NOT NULL)", code);
        Assert.Contains("CHECK (QuickBooksExportedBy IS NULL OR QuickBooksExportedAt IS NOT NULL)", code);
        Assert.DoesNotContain("CK_PurchaseOrder_QbOrigin", code);   // la regla «origen QUICKBOOKS ⇒ sin marca» la guarda el servicio

        var indexes = Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "IX_PurchaseOrder_QuickBooksPending", "UX_PurchaseOrder_ImportKey" }, indexes);
        foreach (var name in indexes) Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}'", code);
        Assert.Contains("WHERE ImportKey IS NOT NULL AND IsActive = 1;", code);
        Assert.Contains("WHERE QuickBooksAt IS NULL AND IsActive = 1;", code);
        // el índice único va DESPUÉS de cargar las claves de las órdenes existentes
        Assert.True(code.IndexOf("CREATE UNIQUE INDEX UX_PurchaseOrder_ImportKey", StringComparison.Ordinal)
                    > code.IndexOf("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L246_PO_ORIGIN');", StringComparison.Ordinal));
    }

    [Fact]
    public void The_section_seeds_the_origins_and_loads_the_existing_orders_once()
    {
        var code = Statements(Section());
        Assert.Contains("ON t.Entity = N'PurchaseOrderOrigin' AND t.InternalCode = s.Code", code);
        foreach (var origin in PurchaseOrderOrigins.All) Assert.Contains($"(N'{origin}',", code);
        Assert.Equal(PurchaseOrderOrigins.Domain, "PurchaseOrderOrigin");

        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L246_PO_ORIGIN')", code);
        Assert.Contains(@"Notes LIKE N'%\[QB:%' ESCAPE N'\'", code);
        Assert.Contains(@"Notes LIKE N'%\[IMP:%' ESCAPE N'\'", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L246_SUPPLIER_QB_SENT')", code);
        Assert.Contains("FROM dbo.PurchaseHistoryLine h WHERE h.TenantId = s.TenantId", code);
    }

    [Fact]
    public void The_section_adds_the_permission_and_propagates_it_once_without_deleting_anything()
    {
        var code = Statements(Section());
        Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = '{PermissionCatalog.PurchasingQuickBooks}')", code);
        Assert.Contains("InternalCode = 'BILLING'", code);
        Assert.Contains("N'{\"es\":\"Compras: pasar órdenes de compra a QuickBooks\",\"en\":\"Purchases: pass purchase orders to QuickBooks\"}'", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L246_PURCHASING_QUICKBOOKS')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L246_PURCHASING_QUICKBOOKS');", code);
        Assert.Contains("r.Name IN ('TenantAdmin', 'Billing')", code);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = pm.PermissionId)", code);
        Assert.DoesNotContain("DELETE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", code, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Lote 246 (P1) — la sección del lote contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de <see cref="NotificationSqlFixture"/>
/// (creada desde el modelo EF y sin FK). PrepareAsync siembra con EF las compañías y los roles, deja la base «como antes del lote» (quita las columnas e índices que creó
/// EnsureCreated), inserta con SQL proveedores, órdenes con las marcas del Lote 239 y el historial de compras de QuickBooks, corre la sección, mete una orden nueva y la
/// corre OTRA vez (la segunda corrida no la toca: las cargas son de una sola vez). Después se prueban las cargas, los CHECK, el índice único y la propagación del permiso.
/// </summary>
public sealed class PurchaseOrderQuickBooksSqlServerTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private const string TenantAName = "Compañía A compras QB (sección)";
    private const string TenantBName = "Compañía B compras QB (sección)";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _tenantA, _tenantB;

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

    private async Task PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            await using var c = new SqlConnection(fx.ConnectionString);
            await c.OpenAsync();
            if ((int)(await ScalarAsync(c, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_PurchaseOrder_QbBy'"))! > 0) return;

            // con EF, antes de quitar columnas: la categoría BILLING, dos compañías y sus roles (y la plantilla global, que no se toca)
            await using (var db = fx.NewDb(new TenantContext()))
            {
                if (!await db.LookupCodes.IgnoreQueryFilters().AnyAsync(l => l.Entity == LookupDomains.PermissionCategory && l.InternalCode == "BILLING"))
                    db.LookupCodes.Add(new LookupCode { Entity = LookupDomains.PermissionCategory, InternalCode = "BILLING", LabelJson = "{\"es\":\"Facturación\"}", IsSystem = true });
                var a = new Tenant { Name = TenantAName, DefaultLangCode = "es" };
                var b = new Tenant { Name = TenantBName, DefaultLangCode = "es" };
                db.Tenants.AddRange(a, b);
                await db.SaveChangesAsync();
                foreach (var t in new[] { a, b })
                    foreach (var name in new[] { "TenantAdmin", "Billing", "Sales" })
                        db.AppRoles.Add(new Role { TenantId = t.TenantId, Name = name, IsActive = true });
                var inactive = new Tenant { Name = "Compañía C compras QB (sección)", DefaultLangCode = "es" };
                db.Tenants.Add(inactive);
                await db.SaveChangesAsync();
                db.AppRoles.Add(new Role { TenantId = inactive.TenantId, Name = "Billing", IsActive = false });   // rol inactivo: no recibe nada
                db.AppRoles.Add(new Role { TenantId = null, Name = "TenantAdmin", IsActive = true, IsSystem = true });
                await db.SaveChangesAsync();
                (_tenantA, _tenantB) = (a.TenantId, b.TenantId);
            }

            // la base como antes del lote: sin las columnas ni sus índices (los creó EnsureCreated desde el modelo EF)
            await ExecAsync(c, """
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PurchaseOrder_ImportKey' AND object_id = OBJECT_ID('dbo.PurchaseOrder')) DROP INDEX UX_PurchaseOrder_ImportKey ON dbo.PurchaseOrder;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseOrder_QuickBooksPending' AND object_id = OBJECT_ID('dbo.PurchaseOrder')) DROP INDEX IX_PurchaseOrder_QuickBooksPending ON dbo.PurchaseOrder;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PurchaseOrder_OriginLookupId' AND object_id = OBJECT_ID('dbo.PurchaseOrder')) DROP INDEX IX_PurchaseOrder_OriginLookupId ON dbo.PurchaseOrder;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'OriginLookupId') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN OriginLookupId;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'ImportKey') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN ImportKey;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'QuickBooksAt') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN QuickBooksAt;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'QuickBooksBy') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN QuickBooksBy;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'QuickBooksExportedAt') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN QuickBooksExportedAt;
                IF COL_LENGTH(N'dbo.PurchaseOrder', N'QuickBooksExportedBy') IS NOT NULL ALTER TABLE dbo.PurchaseOrder DROP COLUMN QuickBooksExportedBy;
                IF COL_LENGTH(N'dbo.Supplier', N'QuickBooksSentAt') IS NOT NULL ALTER TABLE dbo.Supplier DROP COLUMN QuickBooksSentAt;
                IF COL_LENGTH(N'dbo.Tenant', N'QuickBooksPurchaseOrderAccount') IS NOT NULL ALTER TABLE dbo.Tenant DROP COLUMN QuickBooksPurchaseOrderAccount;
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_LookupCode_CreatedAtUtc_PoQbTest')
                    ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_PoQbTest DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
                """);

            // proveedores, órdenes con las marcas del Lote 239 y el historial de compras de QuickBooks (Lote 241)
            await ExecAsync(c, $"""
                INSERT INTO dbo.Supplier (TenantId, Name, IsActive) VALUES
                    ({_tenantA}, N'Acme', 1), ({_tenantA}, N'Beta Corp', 1), ({_tenantA}, N'Gamma', 1), ({_tenantA}, N'Delta', 1), ({_tenantB}, N'Acme', 1);
                DECLARE @acme INT = (SELECT SupplierId FROM dbo.Supplier WHERE TenantId = {_tenantA} AND Name = N'Acme');
                DECLARE @delta INT = (SELECT SupplierId FROM dbo.Supplier WHERE TenantId = {_tenantA} AND Name = N'Delta');
                DECLARE @acmeB INT = (SELECT SupplierId FROM dbo.Supplier WHERE TenantId = {_tenantB} AND Name = N'Acme');
                INSERT INTO dbo.PurchaseOrder (TenantId, SupplierId, WarehouseId, Number, OrderDate, StatusCodeId, Notes, IsActive, CreatedAtUtc) VALUES
                    ({_tenantA}, @acme,  1, N'PO-00001', '2026-11-01', 1, N'Importada de QuickBooks 1001. [QB:ACME|1001]', 1, SYSUTCDATETIME()),
                    ({_tenantA}, @acme,  1, N'PO-00002', '2026-11-01', 1, N'Importada de archivo X-1. [IMP:ACME|X-1]', 1, SYSUTCDATETIME()),
                    ({_tenantA}, @acme,  1, N'PO-00003', '2026-11-01', 1, N'Otra vez. [QB:ACME|1001]', 1, SYSUTCDATETIME()),
                    ({_tenantA}, @acme,  1, N'PO-00004', '2026-11-01', 1, N'Dada de baja. [IMP:ACME|X-2]', 0, SYSUTCDATETIME()),
                    ({_tenantA}, @delta, 1, N'PO-00005', '2026-11-01', 1, NULL, 1, SYSUTCDATETIME()),
                    ({_tenantA}, @acme,  1, N'PO-00006', '2026-11-01', 1, N'Marca rota [QB:sin cierre', 1, SYSUTCDATETIME()),
                    ({_tenantB}, @acmeB, 1, N'PO-00001', '2026-11-01', 1, N'Importada de QuickBooks 1001. [QB:ACME|1001]', 1, SYSUTCDATETIME());
                INSERT INTO dbo.PurchaseHistoryLine (TenantId, SalesHistoryBatchId, SourceLine, TxnDate, TxnType, SupplierNameRaw, ItemRaw, ItemSku, Quantity, Amount) VALUES
                    ({_tenantA}, 1, 1, '2026-01-10', N'Bill', N'  beta corp ', N'X', N'X', 1, 1),
                    ({_tenantB}, 1, 1, '2026-01-10', N'Bill', N'Gamma', N'X', N'X', 1, 1);
                """);

            foreach (var batch in PurchaseOrderQuickBooksSqlTests.Batches(PurchaseOrderQuickBooksSqlTests.Section())) await ExecAsync(c, batch);
            // una orden que llega DESPUÉS de la primera corrida: la segunda no la toca (las cargas son de una sola vez)
            await ExecAsync(c, $"""
                INSERT INTO dbo.PurchaseOrder (TenantId, SupplierId, WarehouseId, Number, OrderDate, StatusCodeId, Notes, IsActive, CreatedAtUtc)
                VALUES ({_tenantA}, (SELECT SupplierId FROM dbo.Supplier WHERE TenantId = {_tenantA} AND Name = N'Delta'), 1, N'PO-00009', '2026-11-02', 1, N'Nueva. [IMP:NEW|1]', 1, SYSUTCDATETIME());
                """);
            foreach (var batch in PurchaseOrderQuickBooksSqlTests.Batches(PurchaseOrderQuickBooksSqlTests.Section())) await ExecAsync(c, batch);
        }
        finally { Gate.Release(); }
    }

    private async Task<(string? Origin, string? Key)> OrderAsync(SqlConnection c, int tenantId, string number)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT l.InternalCode, p.ImportKey FROM dbo.PurchaseOrder p LEFT JOIN dbo.LookupCode l ON l.LookupCodeId = p.OriginLookupId
            WHERE p.TenantId = {tenantId} AND p.Number = N'{number}'
            """;
        await using var rd = await cmd.ExecuteReaderAsync();
        Assert.True(await rd.ReadAsync());
        return (rd.IsDBNull(0) ? null : rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1));
    }

    [SqlServerFact]
    public async Task The_section_runs_twice_and_leaves_the_structure()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        // una tercera corrida tampoco falla
        foreach (var batch in PurchaseOrderQuickBooksSqlTests.Batches(PurchaseOrderQuickBooksSqlTests.Section())) await ExecAsync(c, batch);

        foreach (var (table, column) in new[] { ("PurchaseOrder", "OriginLookupId"), ("PurchaseOrder", "ImportKey"), ("PurchaseOrder", "QuickBooksAt"), ("PurchaseOrder", "QuickBooksBy"),
                     ("PurchaseOrder", "QuickBooksExportedAt"), ("PurchaseOrder", "QuickBooksExportedBy"), ("Supplier", "QuickBooksSentAt"), ("Tenant", "QuickBooksPurchaseOrderAccount") })
            Assert.NotNull(await ScalarAsync(c, $"SELECT COL_LENGTH(N'dbo.{table}', N'{column}')"));
        Assert.Equal((short)500, await ScalarAsync(c, "SELECT COL_LENGTH(N'dbo.PurchaseOrder', N'ImportKey')"));   // NVARCHAR(250)
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_PurchaseOrder_ImportKey' AND is_unique = 1 AND has_filter = 1"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_PurchaseOrder_QuickBooksPending' AND has_filter = 1"));
        Assert.Equal(2, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('CK_PurchaseOrder_QbBy', 'CK_PurchaseOrder_QbExportedBy')"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_PurchaseOrder_Origin'"));
        Assert.Equal(3, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'PurchaseOrderOrigin'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.Permission WHERE Code = 'purchasing.quickbooks'"));
        Assert.Equal(3, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code LIKE 'L246[_]%'"));
    }

    [SqlServerFact]
    public async Task The_existing_orders_get_their_origin_and_import_key_once()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        Assert.Equal(("QUICKBOOKS", "QB:ACME|1001"), await OrderAsync(c, _tenantA, "PO-00001"));
        Assert.Equal(("EXCEL", "IMP:ACME|X-1"), await OrderAsync(c, _tenantA, "PO-00002"));
        Assert.Equal(("QUICKBOOKS", (string?)null), await OrderAsync(c, _tenantA, "PO-00003"));   // la misma marca: solo la primera activa lleva la clave
        Assert.Equal(("EXCEL", (string?)null), await OrderAsync(c, _tenantA, "PO-00004"));        // inactiva: origen sí, clave no
        Assert.Equal(("TEIKEM", (string?)null), await OrderAsync(c, _tenantA, "PO-00005"));
        Assert.Equal(("QUICKBOOKS", (string?)null), await OrderAsync(c, _tenantA, "PO-00006"));   // marca sin cierre: sin clave
        Assert.Equal(("QUICKBOOKS", "QB:ACME|1001"), await OrderAsync(c, _tenantB, "PO-00001"));  // la misma clave en otra compañía
        Assert.Equal(((string?)null, (string?)null), await OrderAsync(c, _tenantA, "PO-00009"));  // llegó después: la segunda corrida no la tocó
    }

    [SqlServerFact]
    public async Task Suppliers_already_in_quickbooks_are_marked_as_sent_once()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        async Task<bool> Sent(int tenantId, string name)
            => await ScalarAsync(c, $"SELECT QuickBooksSentAt FROM dbo.Supplier WHERE TenantId = {tenantId} AND Name = N'{name}'") is not null;
        Assert.True(await Sent(_tenantA, "Acme"));        // tiene órdenes de QuickBooks
        Assert.True(await Sent(_tenantA, "Beta Corp"));   // sale en el historial de SU compañía (recortado, sin distinguir mayúsculas)
        Assert.False(await Sent(_tenantA, "Gamma"));      // solo sale en el historial de OTRA compañía
        Assert.False(await Sent(_tenantA, "Delta"));      // solo órdenes de Teikem
        Assert.True(await Sent(_tenantB, "Acme"));
    }

    [SqlServerFact]
    public async Task The_checks_and_the_unique_import_key_guard_the_data()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        // órdenes propias de esta prueba (las de PrepareAsync las leen las otras)
        await ExecAsync(c, $"""
            DECLARE @delta INT = (SELECT SupplierId FROM dbo.Supplier WHERE TenantId = {_tenantA} AND Name = N'Delta');
            INSERT INTO dbo.PurchaseOrder (TenantId, SupplierId, WarehouseId, Number, OrderDate, StatusCodeId, IsActive, CreatedAtUtc) VALUES
                ({_tenantA}, @delta, 1, N'PO-00010', '2026-11-03', 1, 1, SYSUTCDATETIME()),
                ({_tenantA}, @delta, 1, N'PO-00011', '2026-11-03', 1, 1, SYSUTCDATETIME()),
                ({_tenantA}, @delta, 1, N'PO-00012', '2026-11-03', 1, 1, SYSUTCDATETIME()),
                ({_tenantA}, @delta, 1, N'PO-00013', '2026-11-03', 1, 0, SYSUTCDATETIME());
            UPDATE dbo.PurchaseOrder SET ImportKey = N'IMP:CHECK|1' WHERE TenantId = {_tenantA} AND Number = N'PO-00011';
            """);
        var by = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.PurchaseOrder SET QuickBooksBy = 1 WHERE TenantId = {_tenantA} AND Number = N'PO-00010';"));
        Assert.Equal(547, by.Number);
        Assert.Contains("CK_PurchaseOrder_QbBy", by.Message);
        var exportedBy = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.PurchaseOrder SET QuickBooksExportedBy = 1 WHERE TenantId = {_tenantA} AND Number = N'PO-00010';"));
        Assert.Equal(547, exportedBy.Number);
        Assert.Contains("CK_PurchaseOrder_QbExportedBy", exportedBy.Message);
        await ExecAsync(c, $"UPDATE dbo.PurchaseOrder SET QuickBooksAt = SYSUTCDATETIME(), QuickBooksBy = 1, QuickBooksExportedAt = SYSUTCDATETIME(), QuickBooksExportedBy = 1 WHERE TenantId = {_tenantA} AND Number = N'PO-00010';");

        // dos órdenes ACTIVAS con la misma clave en la misma compañía → 2601; la inactiva sí entra
        var dup = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.PurchaseOrder SET ImportKey = N'IMP:CHECK|1' WHERE TenantId = {_tenantA} AND Number = N'PO-00012';"));
        Assert.True(dup.Number is 2601 or 2627, dup.Message);
        await ExecAsync(c, $"UPDATE dbo.PurchaseOrder SET ImportKey = N'IMP:CHECK|1' WHERE TenantId = {_tenantA} AND Number = N'PO-00013';");
    }

    [SqlServerFact]
    public async Task The_permission_goes_to_the_active_cloned_tenant_admin_and_billing_roles_only()
    {
        await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        var withPerm = new List<(string Tenant, string Role)>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                SELECT t.Name, r.Name FROM dbo.RolePermission rp JOIN dbo.Role r ON r.RoleId = rp.RoleId JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
                LEFT JOIN dbo.Tenant t ON t.TenantId = r.TenantId
                WHERE p.Code = 'purchasing.quickbooks'
                """;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) withPerm.Add((rd.IsDBNull(0) ? "(global)" : rd.GetString(0), rd.GetString(1)));
        }
        Assert.Equal(new[] { (TenantAName, "Billing"), (TenantAName, "TenantAdmin"), (TenantBName, "Billing"), (TenantBName, "TenantAdmin") },
            withPerm.OrderBy(x => x.Tenant).ThenBy(x => x.Role).ToArray());
    }
}
