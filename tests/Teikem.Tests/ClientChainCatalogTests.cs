using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 245 (Lote B del plan: cadenas de clientes) — la base compartida del lote: el permiso sales.chain (catálogo, plantillas, orden, «editar ⇒ ver», propagación
/// a los roles ya clonados y permiso de lectura de la fuente CLIENT_CHAIN), el modelo EF de ClientChain (tabla, filtro de tenant, índices, FK de Client.ChainId,
/// bitácora) y los textos de su sección de logistica-db-update.sql, leída SOLO hasta la siguiente (SqlSections.From).
/// </summary>
public sealed class ClientChainCatalogTests
{
    internal const string Marker = "2026-11-25 — Lote 245: Cadenas de clientes (Lote B del plan";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>La sección: desde su comentario de encabezado hasta el de la siguiente (o el final del archivo).</summary>
    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección de cadenas de clientes en logistica-db-update.sql.");
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
    public void Sales_chain_is_a_billing_permission_of_tenant_admin_and_sales_supervisor_only()
    {
        Assert.Equal("sales.chain", PermissionCatalog.SalesChain);
        var def = Assert.Single(PermissionCatalog.All, p => p.Code == PermissionCatalog.SalesChain);
        Assert.Equal("BILLING", def.Category);
        Assert.Equal("Cadenas de clientes: crear, editar, asignar clientes y alta masiva", def.LabelEs);
        Assert.Equal("Client chains: create, edit, assign clients and bulk assign", def.LabelEn);
        Assert.Contains(PermissionCatalog.SalesChain, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        Assert.Contains(PermissionCatalog.SalesChain, PermissionCatalog.RoleTemplates["SalesSupervisor"]);
        foreach (var role in new[] { "Sales", "SalesOffice", "Dispatcher", "ReadOnly" })
            Assert.DoesNotContain(PermissionCatalog.SalesChain, PermissionCatalog.RoleTemplates[role]);
    }

    [Fact]
    public void Sales_chain_goes_after_sales_pricelist_implies_clients_read_and_the_data_source_reads_with_clients_read()
    {
        Assert.Equal(PermissionDisplayOrder.IndexOf(PermissionCatalog.SalesPricelist) + 1, PermissionDisplayOrder.IndexOf(PermissionCatalog.SalesChain));
        Assert.Equal(new[] { PermissionCatalog.ClientsRead }, MenuPermissions.EditImplies[PermissionCatalog.SalesChain]);
        Assert.Equal(PermissionCatalog.ClientsRead, PermissionCatalog.DataSourceReadPermission(ClientChainCodes.EntityType));
        Assert.Equal(ClientChainCodes.EntityType, ClientChainCodes.DataSourceKey);
    }

    [Fact]
    public void The_seeder_propagates_sales_chain_to_cloned_supervisors_and_not_to_sellers()
    {
        var fresh = new HashSet<string> { PermissionCatalog.SalesChain };
        Assert.Equal(new[] { PermissionCatalog.SalesChain }, PermissionCatalog.CodesToPropagate("SalesSupervisor", fresh, new HashSet<string>()));
        Assert.Equal(new[] { PermissionCatalog.SalesChain }, PermissionCatalog.CodesToPropagate("TenantAdmin", fresh, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("Sales", fresh, new HashSet<string>()));
        Assert.Empty(PermissionCatalog.CodesToPropagate("SalesSupervisor", fresh, fresh));   // ya lo tiene: solo agrega
    }

    // ---------------------------------------------------------------- modelo EF

    [Fact]
    public void The_ef_model_maps_the_chain_with_its_tenant_filter_indexes_and_the_client_fk()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(typeof(ClientChain))!;
        Assert.Equal("ClientChain", entity.GetTableName());
        Assert.NotNull(entity.GetQueryFilter());   // filtro global de tenant (ITenantScoped)
        Assert.True(entity.FindProperty(nameof(ClientChain.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(100, entity.FindProperty(nameof(ClientChain.Name))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(ClientChain.Name))!.IsNullable);
        Assert.Equal(500, entity.FindProperty(nameof(ClientChain.Notes))!.GetMaxLength());
        Assert.Equal("NEWID()", entity.FindProperty(nameof(ClientChain.PublicId))!.GetDefaultValueSql());

        var name = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "UX_ClientChain_Name");
        Assert.True(name.IsUnique);
        Assert.Equal(new[] { "TenantId", "Name" }, name.Properties.Select(p => p.Name));
        Assert.Equal("[IsActive] = 1", name.GetFilter());
        var pub = Assert.Single(entity.GetIndexes(), i => i.GetDatabaseName() == "UX_ClientChain_PublicId");
        Assert.True(pub.IsUnique);

        var client = db.Model.FindEntityType(typeof(Client))!;
        var fk = Assert.Single(client.GetForeignKeys(), f => f.Properties.Single().Name == nameof(Client.ChainId));
        Assert.Equal(typeof(ClientChain), fk.PrincipalEntityType.ClrType);
        Assert.False(fk.IsRequired);
        Assert.True(client.FindProperty(nameof(Client.ChainId))!.IsNullable);
    }

    [Fact]
    public void The_chain_is_audited_as_client_chain_and_the_concurrency_touch_is_not()
    {
        Assert.Equal(ClientChainCodes.EntityType, typeof(ClientChain).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var prop in new[] { "PublicId", "UpdatedAtUtc", "UpdatedBy", "RowVersion" })
            Assert.NotNull(typeof(ClientChain).GetProperty(prop)!.GetCustomAttribute<NotAuditedAttribute>());
        foreach (var prop in new[] { "Name", "Notes", "IsActive" })
            Assert.Null(typeof(ClientChain).GetProperty(prop)!.GetCustomAttribute<NotAuditedAttribute>());
        // la cadena del cliente sí deja bitácora en el cliente
        Assert.Null(typeof(Client).GetProperty(nameof(Client.ChainId))!.GetCustomAttribute<NotAuditedAttribute>());
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(typeof(ClientChain)));
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(ClientChain)));
        Assert.Equal(new[] { "ASSIGN", "MOVE", "ALREADY", "SKIP_OTHER_CHAIN", "EXCLUDED" }, ClientChainCodes.Actions);
    }

    // ---------------------------------------------------------------- sección SQL

    [Fact]
    public void The_section_goes_after_lote_244_names_its_decisions_and_says_it_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-24 — Lote 243 (Lote G del plan de Solutions)", StringComparison.Ordinal));
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 244 (Lote A del plan de rutas)", StringComparison.Ordinal));
        var section = Section();
        Assert.Contains("(docs/lote245-decisiones.md)", section);
        Assert.Contains("Solo AGREGA. Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
    }

    [Fact]
    public void Every_structure_change_has_its_guard_and_the_fk_is_composite()
    {
        var code = Statements(Section());
        Assert.Contains("IF OBJECT_ID(N'dbo.ClientChain', N'U') IS NULL CREATE TABLE dbo.ClientChain (", code);
        Assert.Contains("CONSTRAINT UQ_ClientChain_IdTenant UNIQUE (ClientChainId, TenantId)", code);
        Assert.Contains("IF COL_LENGTH(N'dbo.Client', N'ChainId') IS NULL\n    ALTER TABLE dbo.Client ADD ChainId INT NULL;", code);
        Assert.Contains("IF OBJECT_ID(N'dbo.FK_Client_Chain', N'F') IS NULL", code);
        Assert.Contains("FOREIGN KEY (ChainId, TenantId) REFERENCES dbo.ClientChain(ClientChainId, TenantId)", code);
        Assert.Contains("CREATE UNIQUE INDEX UX_ClientChain_Name ON dbo.ClientChain(TenantId, Name) WHERE IsActive = 1;", code);
        Assert.Contains("CREATE INDEX IX_Client_Chain ON dbo.Client(TenantId, ChainId) WHERE ChainId IS NOT NULL;", code);
        var indexes = Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "UX_ClientChain_Name", "UX_ClientChain_PublicId", "IX_Client_Chain" }, indexes);
        foreach (var name in indexes) Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}'", code);
        // cada sentencia en su lote, con GO al final
        Assert.Equal(9, Batches(Section()).Count);
    }

    [Fact]
    public void The_section_seeds_the_entity_type_the_permission_and_its_propagation_once()
    {
        var code = Statements(Section());
        Assert.Contains($"(N'{ClientChainCodes.EntityType}', N'{{\"es\":\"Cadena de clientes\",\"en\":\"Client chain\"}}', 198)", code);
        Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = '{PermissionCatalog.SalesChain}')", code);
        Assert.Contains("InternalCode = 'BILLING'", code);
        Assert.Contains("N'{\"es\":\"Cadenas de clientes: crear, editar, asignar clientes y alta masiva\",\"en\":\"Client chains: create, edit, assign clients and bulk assign\"}'", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'LB_CLIENT_CHAIN_PERMS')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('LB_CLIENT_CHAIN_PERMS');", code);
        Assert.Contains("r.Name IN ('TenantAdmin', 'SalesSupervisor')", code);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = pm.PermissionId)", code);
    }

    [Fact]
    public void The_section_does_not_touch_routes_clients_data_nor_deletes_anything()
    {
        var code = Statements(Section());
        foreach (var word in new[] { "SalesRoute", "UPDATE dbo.Client", "INSERT INTO dbo.ClientChain", "DROP ", "DELETE ", "ALTER COLUMN" })
            Assert.DoesNotContain(word, code, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Lote 245 — la sección de cadenas contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER): sobre una base desechable (NotificationSqlFixture, creada desde
/// el modelo EF y sin llaves foráneas) que se deja «como antes del lote» (sin dbo.ClientChain ni dbo.Client.ChainId), la sección corre DOS veces sin error y la
/// segunda no cambia nada; queda la FK COMPUESTA (un cliente no apunta a la cadena de otra compañía: error 547), el índice único filtrado y sales.chain solo en
/// los TenantAdmin y SalesSupervisor clonados y activos.
/// </summary>
public sealed class ClientChainSectionSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
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

    /// <summary>Lo que la sección deja en la base: si la segunda corrida cambia algo, cambia esto.</summary>
    private const string FingerprintSql = """
        SELECT CONCAT(
          (SELECT STRING_AGG(CAST(CONCAT(o.name, ':', o.type) AS NVARCHAR(MAX)) COLLATE DATABASE_DEFAULT, '|') WITHIN GROUP (ORDER BY o.name) FROM sys.objects o WHERE o.is_ms_shipped = 0),
          '#', (SELECT STRING_AGG(CAST(CONCAT(OBJECT_NAME(i.object_id), '.', i.name, ':', i.is_unique, ':', i.filter_definition) AS NVARCHAR(MAX)) COLLATE DATABASE_DEFAULT, '|') WITHIN GROUP (ORDER BY OBJECT_NAME(i.object_id), i.name)
                FROM sys.indexes i WHERE i.name IS NOT NULL AND OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1),
          '#', (SELECT COUNT(*) FROM dbo.LookupCode), '#', (SELECT COUNT(*) FROM dbo.Permission), '#', (SELECT COUNT(*) FROM dbo.RolePermission),
          '#', (SELECT COUNT(*) FROM dbo.AppliedMigration), '#', (SELECT COUNT(*) FROM dbo.ClientChain))
        """;

    [SqlServerFact]
    public async Task On_sql_server_the_section_runs_twice_and_leaves_the_composite_fk_the_index_and_the_permission()
    {
        // 1) datos de antes del lote (con EF, mientras el modelo y la base coinciden): dos compañías, sus roles, un cliente en cada una y la categoría BILLING
        int tenantA, tenantB, clientA, clientB;
        var roles = new Dictionary<string, int>();
        await using (var db = fx.NewDb(new TenantContext { TenantFilterDisabled = true }))
        {
            var a = new Tenant { Name = "Compañía A cadenas", DefaultLangCode = "es" };
            var b = new Tenant { Name = "Compañía B cadenas", DefaultLangCode = "es" };
            db.Tenants.AddRange(a, b);
            await db.SaveChangesAsync();
            tenantA = a.TenantId;
            tenantB = b.TenantId;
            if (!await db.LookupCodes.IgnoreQueryFilters().AnyAsync(l => l.Entity == "PermissionCategory" && l.InternalCode == "BILLING"))
                db.LookupCodes.Add(new LookupCode { Entity = "PermissionCategory", InternalCode = "BILLING", LabelJson = "{\"es\":\"Facturación\"}", IsSystem = true });
            Role R(int? tenantId, string name, bool isActive = true)
            {
                var r = new Role { TenantId = tenantId, Name = name, IsSystem = tenantId is null, IsActive = isActive };
                db.AppRoles.Add(r);
                return r;
            }
            var created = new Dictionary<string, Role>
            {
                ["A.TenantAdmin"] = R(tenantA, "TenantAdmin"),
                ["A.SalesSupervisor"] = R(tenantA, "SalesSupervisor"),
                ["A.Sales"] = R(tenantA, "Sales"),
                ["B.TenantAdmin"] = R(tenantB, "TenantAdmin"),
                ["B.SalesOffice"] = R(tenantB, "SalesOffice"),
                ["B.InactiveSupervisor"] = R(tenantB, "SalesSupervisor", isActive: false),
                ["Template.SalesSupervisor"] = R(null, "SalesSupervisor"),
            };
            var ca = new Client { PublicId = Guid.NewGuid(), TenantId = tenantA, Code = "CAD-A", Name = "Walgreens 1", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            var cb = new Client { PublicId = Guid.NewGuid(), TenantId = tenantB, Code = "CAD-B", Name = "Walgreens B", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            db.Clients.AddRange(ca, cb);
            await db.SaveChangesAsync();
            foreach (var (k, r) in created) roles[k] = r.RoleId;
            clientA = ca.ClientId;
            clientB = cb.ClientId;
        }

        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();

        // 2) la base como estaba en producción antes del lote: sin dbo.ClientChain ni dbo.Client.ChainId (con sus índices EF); y lo que el script de estructura
        //    trae y el modelo EF no: dbo.AppliedMigration y el DEFAULT de LookupCode.CreatedAtUtc
        await ExecAsync(c, """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Client;'
              FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
             WHERE i.object_id = OBJECT_ID('dbo.Client') AND col.name = 'ChainId' AND i.is_primary_key = 0
             GROUP BY i.name;
            EXEC sp_executesql @sql;
            IF COL_LENGTH('dbo.Client', 'ChainId') IS NOT NULL ALTER TABLE dbo.Client DROP COLUMN ChainId;
            IF OBJECT_ID(N'dbo.ClientChain', N'U') IS NOT NULL DROP TABLE dbo.ClientChain;
            IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
            IF NOT EXISTS (SELECT 1 FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
                           WHERE d.parent_object_id = OBJECT_ID('dbo.LookupCode') AND col.name = 'CreatedAtUtc')
                ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_Test DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
            """);
        Assert.Null(await ScalarAsync(c, "SELECT OBJECT_ID(N'dbo.ClientChain', N'U')"));

        // 3) dos corridas: la segunda no cambia nada ni falla
        var batches = ClientChainCatalogTests.Batches(ClientChainCatalogTests.Section());
        foreach (var batch in batches) await ExecAsync(c, batch);
        var first = (string)(await ScalarAsync(c, FingerprintSql))!;
        foreach (var batch in batches) await ExecAsync(c, batch);
        var second = (string)(await ScalarAsync(c, FingerprintSql))!;
        Assert.Equal(first, second);

        // estructura
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_ClientChain_IdTenant' AND parent_object_id = OBJECT_ID('dbo.ClientChain') AND type = 'UQ'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_ClientChain_Name' AND object_id = OBJECT_ID('dbo.ClientChain') AND is_unique = 1 AND has_filter = 1"));
        Assert.Contains("IsActive", (string)(await ScalarAsync(c, "SELECT filter_definition FROM sys.indexes WHERE name = 'UX_ClientChain_Name' AND object_id = OBJECT_ID('dbo.ClientChain')"))!);
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_Client_Chain' AND object_id = OBJECT_ID('dbo.Client') AND has_filter = 1"));
        Assert.Equal(2, await ScalarAsync(c, """
            SELECT COUNT(*) FROM sys.foreign_key_columns fc JOIN sys.foreign_keys f ON f.object_id = fc.constraint_object_id
            WHERE f.name = 'FK_Client_Chain' AND f.parent_object_id = OBJECT_ID('dbo.Client') AND f.referenced_object_id = OBJECT_ID('dbo.ClientChain')
            """));
        // datos: ninguna cadena creada sola, los clientes sin cadena, el EntityType y el permiso una sola vez
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.ClientChain"));
        Assert.Equal(0, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.Client WHERE ChainId IS NOT NULL"));
        Assert.Equal(198, await ScalarAsync(c, $"SELECT SortOrder FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'{ClientChainCodes.EntityType}'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.Permission WHERE Code = 'sales.chain'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code = 'LB_CLIENT_CHAIN_PERMS'"));
        Assert.Equal(1, await ScalarAsync(c, """
            SELECT COUNT(*) FROM dbo.Permission p JOIN dbo.LookupCode l ON l.LookupCodeId = p.CategoryLookupId
            WHERE p.Code = 'sales.chain' AND l.Entity = 'PermissionCategory' AND l.InternalCode = 'BILLING'
            """));
        async Task<int> Grants(string role) => (int)(await ScalarAsync(c, $"""
            SELECT COUNT(*) FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
            WHERE p.Code = 'sales.chain' AND rp.RoleId = {roles[role]}
            """))!;
        foreach (var role in new[] { "A.TenantAdmin", "A.SalesSupervisor", "B.TenantAdmin" })
            Assert.Equal(1, await Grants(role));
        foreach (var role in new[] { "A.Sales", "B.SalesOffice", "B.InactiveSupervisor", "Template.SalesSupervisor" })
            Assert.Equal(0, await Grants(role));

        // la FK compuesta: un cliente no puede apuntar a la cadena de otra compañía; a la de la suya sí
        await ExecAsync(c, $"INSERT INTO dbo.ClientChain (TenantId, Name) VALUES ({tenantA}, N'Walgreens');");
        var chainA = (int)(await ScalarAsync(c, $"SELECT ClientChainId FROM dbo.ClientChain WHERE TenantId = {tenantA} AND Name = N'Walgreens'"))!;
        var cross = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.Client SET ChainId = {chainA} WHERE ClientId = {clientB};"));
        Assert.Equal(547, cross.Number);
        Assert.Contains("FK_Client_Chain", cross.Message);
        await ExecAsync(c, $"UPDATE dbo.Client SET ChainId = {chainA} WHERE ClientId = {clientA};");
        Assert.Equal(chainA, await ScalarAsync(c, $"SELECT ChainId FROM dbo.Client WHERE ClientId = {clientA}"));

        // nombre único entre las ACTIVAS de la compañía (otra compañía o una inactiva pueden repetirlo); nombre en blanco no entra
        var twice = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"INSERT INTO dbo.ClientChain (TenantId, Name) VALUES ({tenantA}, N'Walgreens');"));
        Assert.Contains("UX_ClientChain_Name", twice.Message);
        await ExecAsync(c, $"INSERT INTO dbo.ClientChain (TenantId, Name, IsActive) VALUES ({tenantA}, N'Walgreens', 0);");
        await ExecAsync(c, $"INSERT INTO dbo.ClientChain (TenantId, Name) VALUES ({tenantB}, N'Walgreens');");
        var blank = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"INSERT INTO dbo.ClientChain (TenantId, Name) VALUES ({tenantA}, N'   ');"));
        Assert.Contains("CK_ClientChain_Name", blank.Message);

        // el modelo EF lee la tabla que dejó el SQL (columnas 1:1) y el filtro de tenant aísla las cadenas
        await using (var dbA = fx.NewDb(tenantA))
        {
            var mine = await dbA.Set<ClientChain>().AsNoTracking().OrderBy(x => x.ClientChainId).ToListAsync();
            Assert.Equal(2, mine.Count);
            Assert.All(mine, x => Assert.Equal(tenantA, x.TenantId));
            Assert.All(mine, x => Assert.NotEqual(Guid.Empty, x.PublicId));
            Assert.NotNull(mine[0].RowVersion);
            Assert.Equal(chainA, (await dbA.Clients.AsNoTracking().SingleAsync(x => x.ClientId == clientA)).ChainId);
        }
    }
}
