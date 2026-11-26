using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 247 (Lote C, P1) — la sección «Lote 247» de logistica-db-update.sql, leída SOLO hasta la siguiente (SqlSections.From): su texto (guardas, marca de datos,
/// EntityType SPECIAL_PRICE, sin DROP / DELETE / permisos, después del Lote 246), el modelo EF de lo nuevo y, contra SQL Server REAL (se omite sin
/// TEIKEM_TEST_SQLSERVER), sobre la base desechable de <see cref="NotificationSqlFixture"/> (creada desde el modelo EF y sin FK): la sección corre DOS veces, la
/// migración de las ofertas (AppliesToAll 0 → INCLUDE, 1 → ALL), los CHECK, las FK COMPUESTAS por compañía (547), los índices únicos filtrados (2601) y las líneas
/// viejas de la orden de venta en NULL.
/// </summary>
public sealed class ChainPricingSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    internal const string Marker = "2026-11-25 — Lote 247: Precios especiales y ofertas por cliente y por cadena";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Ids? _ids;

    private sealed record Ids(int TenantA, int TenantB, int ClientA, int ClientB, int ChainA, int ChainB, int ListA, int ListB, int OfferAllA, int OfferClientsA,
        int OfferB, int OrderA, int OldLineA);

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    internal static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 247 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    private static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    // ---------------------------------------------------------------- texto de la sección

    [Fact]
    public void Section_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 246 (Lote F del plan de Solutions)", StringComparison.Ordinal));
        Assert.True(at > sql.IndexOf("2026-11-25 — Lote 245: Cadenas de clientes", StringComparison.Ordinal));
        var section = Section();
        Assert.Contains("(docs/lote247-decisiones.md)", section);
        Assert.Contains("Solo AGREGA. Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());

        var code = Statements(section);
        foreach (var (table, uq) in new[] { ("PriceList", "UQ_PriceList_IdTenant"), ("Offer", "UQ_Offer_IdTenant") })
            Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = '{uq}' AND parent_object_id = OBJECT_ID('dbo.{table}'))", code);
        foreach (var t in new[] { "PriceListChain", "OfferChain" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{t}', N'U') IS NULL CREATE TABLE dbo.{t} (", code);
        foreach (var (table, column) in new[]
                 {
                     ("PriceList", "SpecialClientId"), ("PriceList", "SpecialChainId"), ("Offer", "AudienceMode"), ("SalesOrderLine", "PriceSource"),
                     ("SalesOrderLine", "PriceListId"), ("SalesOrderLine", "OfferId"), ("SalesOrderLine", "PriceChainId"), ("SalesOrderLine", "ResolvedUnitPrice"),
                 })
            Assert.Contains($"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL\n    ALTER TABLE dbo.{table} ADD {column} ", code);
        Assert.Contains("ADD AudienceMode NVARCHAR(10) NOT NULL CONSTRAINT DF_Offer_AudienceMode DEFAULT N'ALL';", code);
        Assert.Contains("ADD PriceSource NVARCHAR(20) NULL;", code);
        Assert.Contains("ADD ResolvedUnitPrice DECIMAL(18,4) NULL;", code);

        foreach (var fk in new[] { "FK_PriceList_SpecialClient", "FK_PriceList_SpecialChain", "FK_SalesOrderLine_PriceList", "FK_SalesOrderLine_Offer", "FK_SalesOrderLine_PriceChain" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{fk}', N'F') IS NULL\n    EXEC(N'ALTER TABLE", code);
        foreach (var ck in new[] { "CK_PriceList_SpecialOwner", "CK_Offer_AudienceMode" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{ck}', N'C') IS NULL\n    EXEC(N'ALTER TABLE", code);
        Assert.Contains("REFERENCES dbo.Client(ClientId, TenantId)", code);
        Assert.Contains("REFERENCES dbo.ClientChain(ClientChainId, TenantId)", code);
        Assert.Contains("REFERENCES dbo.PriceList(PriceListId, TenantId)", code);
        Assert.Contains("REFERENCES dbo.Offer(OfferId, TenantId)", code);

        var indexes = Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "UX_PriceListChain_Chain", "IX_PriceListChain_Chain", "UX_OfferChain_Chain", "IX_OfferChain_Chain", "UX_PriceList_SpecialClient", "UX_PriceList_SpecialChain" },
            indexes);
        foreach (var name in indexes) Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}'", code);

        // la migración de las ofertas, una sola vez, y el CHECK DESPUÉS de ella
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L247_OFFER_AUDIENCE')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L247_OFFER_AUDIENCE');", code);
        Assert.True(code.IndexOf("ADD CONSTRAINT CK_Offer_AudienceMode", StringComparison.Ordinal)
                    > code.IndexOf("VALUES ('L247_OFFER_AUDIENCE');", StringComparison.Ordinal));

        // EntityType SPECIAL_PRICE con orden 199
        Assert.Contains($"(N'{SpecialPriceCodes.EntityType}', N'{{\"es\":\"Precios especiales\",\"en\":\"Special prices\"}}', 199)", code);
        Assert.Contains("ON t.Entity = N'EntityType' AND t.InternalCode = s.Code", code);

        // solo agrega: sin DROP, DELETE ni permisos
        Assert.DoesNotContain("DROP", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dbo.Permission", code);
        Assert.DoesNotContain("dbo.RolePermission", code);
    }

    [Fact]
    public void The_ef_model_maps_the_new_columns_tables_and_indexes()
    {
        using var db = new TeikemDbContext(new DbContextOptionsBuilder<TeikemDbContext>()
            .UseSqlServer("Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True").Options, new TenantContext());
        var list = db.Model.FindEntityType(typeof(PriceList))!;
        Assert.Null(list.FindProperty(nameof(PriceList.IsSpecial)));
        Assert.Equal("[IsActive] = 1 AND [SpecialClientId] IS NOT NULL", list.GetIndexes().Single(i => i.GetDatabaseName() == "UX_PriceList_SpecialClient").GetFilter());
        Assert.True(list.GetIndexes().Single(i => i.GetDatabaseName() == "UX_PriceList_SpecialChain").IsUnique);
        Assert.Equal("PriceListChain", db.Model.FindEntityType(typeof(PriceListChain))!.GetTableName());
        Assert.Equal("OfferChain", db.Model.FindEntityType(typeof(OfferChain))!.GetTableName());
        var offer = db.Model.FindEntityType(typeof(Offer))!;
        Assert.Equal(10, offer.FindProperty(nameof(Offer.AudienceMode))!.GetMaxLength());
        Assert.False(offer.FindProperty(nameof(Offer.AudienceMode))!.IsNullable);
        var line = db.Model.FindEntityType(typeof(SalesOrderLine))!;
        Assert.Equal(20, line.FindProperty(nameof(SalesOrderLine.PriceSource))!.GetMaxLength());
        Assert.Equal("decimal(18,4)", line.FindProperty(nameof(SalesOrderLine.ResolvedUnitPrice))!.GetColumnType());
        Assert.Contains(db.Model.GetEntityTypes(), e => e.ClrType == typeof(PriceListChain) && e.GetQueryFilter() is not null);   // filtro de tenant
        Assert.Contains(db.Model.GetEntityTypes(), e => e.ClrType == typeof(OfferChain) && e.GetQueryFilter() is not null);
        // auditan con PRICE_LIST y OFFER
        Assert.Equal(PriceListCodes.EntityType, ((Teikem.Domain.Common.AuditEntityAttribute)Attribute.GetCustomAttribute(typeof(PriceListChain), typeof(Teikem.Domain.Common.AuditEntityAttribute))!).EntityTypeCode);
        Assert.Equal(OfferCodes.EntityType, ((Teikem.Domain.Common.AuditEntityAttribute)Attribute.GetCustomAttribute(typeof(OfferChain), typeof(Teikem.Domain.Common.AuditEntityAttribute))!).EntityTypeCode);
    }

    // ---------------------------------------------------------------- contra SQL Server real

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

    private static async Task<SqlException> RejectedAsync(SqlConnection c, string sql, int number, string constraint)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, sql));
        Assert.Equal(number, ex.Number);
        Assert.Contains(constraint, ex.Message);
        return ex;
    }

    /// <summary>
    /// Idempotente: la primera vez siembra con EF (dos compañías, clientes, cadenas, listas, ofertas de todos y de clientes, una orden con una línea), deja la base
    /// «como antes del lote» (quita lo que creó EnsureCreated desde el modelo EF y pone los UQ que la estructura y el Lote 245 ya traen) y corre la sección DOS veces.
    /// </summary>
    private async Task<Ids> PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_ids is not null) return _ids;
            await using var c = new SqlConnection(fx.ConnectionString);
            await c.OpenAsync();

            Ids ids;
            await using (var db = fx.NewDb(new TenantContext()))
            {
                var a = new Tenant { Name = "Compañía A precios especiales (sección)", DefaultLangCode = "es" };
                var b = new Tenant { Name = "Compañía B precios especiales (sección)", DefaultLangCode = "es" };
                db.Tenants.AddRange(a, b);
                await db.SaveChangesAsync();
                ClientChain Chain(int t) { var x = new ClientChain { PublicId = Guid.NewGuid(), TenantId = t, Name = "Walgreens", CreatedAtUtc = DateTime.UtcNow }; db.Set<ClientChain>().Add(x); return x; }
                Client Cl(int t) { var x = new Client { PublicId = Guid.NewGuid(), TenantId = t, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Farmacia", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow }; db.Clients.Add(x); return x; }
                PriceList Pl(int t) { var x = new PriceList { PublicId = Guid.NewGuid(), TenantId = t, Name = "General", ValidFrom = new DateOnly(2026, 12, 1), IsPublished = true, AppliesToAll = true, CreatedAtUtc = DateTime.UtcNow }; db.Set<PriceList>().Add(x); return x; }
                Offer Of(int t, bool all) { var x = new Offer { PublicId = Guid.NewGuid(), TenantId = t, Name = all ? "Para todos" : "De clientes", ValidFrom = new DateOnly(2026, 12, 1), AppliesToAll = all, CreatedAtUtc = DateTime.UtcNow }; db.Set<Offer>().Add(x); return x; }
                var (chainA, chainB, clientA, clientB, listA, listB) = (Chain(a.TenantId), Chain(b.TenantId), Cl(a.TenantId), Cl(b.TenantId), Pl(a.TenantId), Pl(b.TenantId));
                var (offerAll, offerClients, offerB) = (Of(a.TenantId, true), Of(a.TenantId, false), Of(b.TenantId, false));
                var order = new SalesOrder { PublicId = Guid.NewGuid(), TenantId = a.TenantId, Number = "OV-00001", WarehouseId = 1, ClientId = 0, RequestedDate = new DateOnly(2026, 12, 1), StatusCodeId = 1, CreatedAtUtc = DateTime.UtcNow };
                db.Set<SalesOrder>().Add(order);
                await db.SaveChangesAsync();
                order.ClientId = clientA.ClientId;
                var line = new SalesOrderLine { TenantId = a.TenantId, SalesOrderId = order.SalesOrderId, LineNo = 1, ProductId = 1, QtyOrdered = 1, UnitPrice = 5m };
                db.Set<SalesOrderLine>().Add(line);
                await db.SaveChangesAsync();
                ids = new Ids(a.TenantId, b.TenantId, clientA.ClientId, clientB.ClientId, chainA.ClientChainId, chainB.ClientChainId, listA.PriceListId, listB.PriceListId,
                    offerAll.OfferId, offerClients.OfferId, offerB.OfferId, order.SalesOrderId, line.SalesOrderLineId);
            }

            // la base como antes del lote: sin las tablas, columnas, índices y DEFAULT que creó EnsureCreated; con los UQ de la estructura y del Lote 245
            await ExecAsync(c, """
                IF OBJECT_ID(N'dbo.PriceListChain', N'U') IS NOT NULL DROP TABLE dbo.PriceListChain;
                IF OBJECT_ID(N'dbo.OfferChain', N'U') IS NOT NULL DROP TABLE dbo.OfferChain;
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.' + QUOTENAME(t.name) + N';'
                  FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
                 WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.index_id > 0
                   AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                                 AND ((t.name = 'PriceList' AND col.name IN ('SpecialClientId', 'SpecialChainId'))
                                   OR (t.name = 'SalesOrderLine' AND col.name IN ('PriceListId', 'OfferId', 'PriceChainId'))));
                SELECT @sql += N'ALTER TABLE dbo.Offer DROP CONSTRAINT ' + QUOTENAME(d.name) + N';'
                  FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
                 WHERE d.parent_object_id = OBJECT_ID('dbo.Offer') AND col.name = 'AudienceMode';
                EXEC sp_executesql @sql;
                ALTER TABLE dbo.PriceList DROP COLUMN SpecialClientId, SpecialChainId;
                ALTER TABLE dbo.Offer DROP COLUMN AudienceMode;
                ALTER TABLE dbo.SalesOrderLine DROP COLUMN PriceSource, PriceListId, OfferId, PriceChainId, ResolvedUnitPrice;
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Client_IdTenant') ALTER TABLE dbo.Client ADD CONSTRAINT UQ_Client_IdTenant UNIQUE (ClientId, TenantId);
                IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_ClientChain_IdTenant') ALTER TABLE dbo.ClientChain ADD CONSTRAINT UQ_ClientChain_IdTenant UNIQUE (ClientChainId, TenantId);
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_LookupCode_CreatedAtUtc_L247Test')
                    ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_L247Test DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
                """);

            // dos corridas: la segunda no hace nada ni falla
            foreach (var _ in new[] { 1, 2 })
                foreach (var batch in Batches(Section()))
                    await ExecAsync(c, batch);
            return _ids = ids;
        }
        finally { Gate.Release(); }
    }

    [SqlServerFact]
    public async Task Section_runs_twice_and_migrates_the_offers()
    {
        var ids = await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        foreach (var batch in Batches(Section())) await ExecAsync(c, batch);   // una tercera corrida tampoco falla

        foreach (var uq in new[] { ("UQ_PriceList_IdTenant", "PriceList"), ("UQ_Offer_IdTenant", "Offer") })
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.key_constraints WHERE name = '{uq.Item1}' AND parent_object_id = OBJECT_ID('dbo.{uq.Item2}')"));
        foreach (var fk in new[] { "FK_PriceListChain_List", "FK_PriceListChain_Chain", "FK_OfferChain_Offer", "FK_OfferChain_Chain", "FK_PriceList_SpecialClient",
                     "FK_PriceList_SpecialChain", "FK_SalesOrderLine_PriceList", "FK_SalesOrderLine_Offer", "FK_SalesOrderLine_PriceChain" })
            Assert.Equal(2, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id = OBJECT_ID('dbo.{fk}')"));   // compuestas
        foreach (var ux in new[] { "UX_PriceListChain_Chain", "UX_OfferChain_Chain", "UX_PriceList_SpecialClient", "UX_PriceList_SpecialChain" })
            Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{ux}' AND is_unique = 1 AND has_filter = 1"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM dbo.AppliedMigration WHERE Code = 'L247_OFFER_AUDIENCE'"));
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'{SpecialPriceCodes.EntityType}' AND SortOrder = 199"));

        // la migración: AppliesToAll = 0 → INCLUDE; 1 → ALL
        Assert.Equal("ALL", await ScalarAsync(c, $"SELECT AudienceMode FROM dbo.Offer WHERE OfferId = {ids.OfferAllA}"));
        Assert.Equal("INCLUDE", await ScalarAsync(c, $"SELECT AudienceMode FROM dbo.Offer WHERE OfferId = {ids.OfferClientsA}"));
        Assert.Equal("INCLUDE", await ScalarAsync(c, $"SELECT AudienceMode FROM dbo.Offer WHERE OfferId = {ids.OfferB}"));

        // la línea vieja queda en NULL
        Assert.Equal(0, await ScalarAsync(c, $"""
            SELECT COUNT(*) FROM dbo.SalesOrderLine WHERE SalesOrderLineId = {ids.OldLineA}
               AND (PriceSource IS NOT NULL OR PriceListId IS NOT NULL OR OfferId IS NOT NULL OR PriceChainId IS NOT NULL OR ResolvedUnitPrice IS NOT NULL)
            """));
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.SalesOrderLine WHERE SalesOrderLineId = {ids.OldLineA}"));

        // y EF lee y escribe las columnas nuevas
        await using var db = fx.NewDb(new TenantContext { TenantId = ids.TenantA, UserId = 1, IsAuthenticated = true });
        Assert.Equal(OfferAudienceModes.Include, (await db.Set<Offer>().AsNoTracking().SingleAsync(o => o.OfferId == ids.OfferClientsA)).AudienceMode);
        var tracked = await db.Set<Offer>().AsTracking().SingleAsync(o => o.OfferId == ids.OfferClientsA);
        tracked.SetAudienceMode(OfferAudienceModes.Exclude);
        await db.SaveChangesAsync();
        Assert.Equal("EXCLUDE", await ScalarAsync(c, $"SELECT AudienceMode FROM dbo.Offer WHERE OfferId = {ids.OfferClientsA} AND AppliesToAll = 0"));
    }

    [SqlServerFact]
    public async Task Check_constraints_keep_audience_and_special_owner_consistent()
    {
        var ids = await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        await RejectedAsync(c, $"UPDATE dbo.Offer SET AppliesToAll = 0 WHERE OfferId = {ids.OfferAllA};", 547, "CK_Offer_AudienceMode");          // ALL con 0
        await RejectedAsync(c, $"UPDATE dbo.Offer SET AppliesToAll = 1 WHERE OfferId = {ids.OfferB};", 547, "CK_Offer_AudienceMode");              // INCLUDE con 1
        await RejectedAsync(c, $"UPDATE dbo.Offer SET AudienceMode = N'OTHER' WHERE OfferId = {ids.OfferB};", 547, "CK_Offer_AudienceMode");
        await ExecAsync(c, $"UPDATE dbo.Offer SET AudienceMode = N'EXCLUDE' WHERE OfferId = {ids.OfferB};");
        await ExecAsync(c, $"UPDATE dbo.Offer SET AudienceMode = N'INCLUDE' WHERE OfferId = {ids.OfferB};");

        var insert = $"INSERT INTO dbo.PriceList (PublicId, TenantId, Name, ValidFrom, IsPublished, AppliesToAll, IsActive, CreatedAtUtc, SpecialClientId, SpecialChainId) VALUES (NEWID(), {ids.TenantA}, N'Especial', '2026-12-01', 1, ";
        await RejectedAsync(c, insert + $"1, 1, SYSUTCDATETIME(), {ids.ClientA}, NULL);", 547, "CK_PriceList_SpecialOwner");                  // dedicada general
        await RejectedAsync(c, insert + $"0, 1, SYSUTCDATETIME(), {ids.ClientA}, {ids.ChainA});", 547, "CK_PriceList_SpecialOwner");          // dos dueños
        await ExecAsync(c, insert + $"0, 1, SYSUTCDATETIME(), {ids.ClientA}, NULL);");
        await ExecAsync(c, insert + $"0, 0, SYSUTCDATETIME(), NULL, NULL);");   // una lista normal de clientes sigue entrando
    }

    [SqlServerFact]
    public async Task Composite_fks_reject_rows_of_another_tenant()
    {
        var ids = await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        var (a, b) = (ids.TenantA, ids.TenantB);

        await RejectedAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId) VALUES ({a}, {ids.ListA}, {ids.ChainB});", 547, "FK_PriceListChain_Chain");
        await RejectedAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId) VALUES ({a}, {ids.ListB}, {ids.ChainA});", 547, "FK_PriceListChain_List");
        await RejectedAsync(c, $"INSERT INTO dbo.OfferChain (TenantId, OfferId, ChainId) VALUES ({a}, {ids.OfferClientsA}, {ids.ChainB});", 547, "FK_OfferChain_Chain");
        await RejectedAsync(c, $"INSERT INTO dbo.OfferChain (TenantId, OfferId, ChainId) VALUES ({a}, {ids.OfferB}, {ids.ChainA});", 547, "FK_OfferChain_Offer");

        var special = $"INSERT INTO dbo.PriceList (PublicId, TenantId, Name, ValidFrom, IsPublished, AppliesToAll, IsActive, CreatedAtUtc, SpecialClientId, SpecialChainId) VALUES (NEWID(), {a}, N'Especial ajeno', '2026-11-01', 1, 0, 0, SYSUTCDATETIME(), ";
        await RejectedAsync(c, special + $"{ids.ClientB}, NULL);", 547, "FK_PriceList_SpecialClient");
        await RejectedAsync(c, special + $"NULL, {ids.ChainB});", 547, "FK_PriceList_SpecialChain");

        await RejectedAsync(c, $"UPDATE dbo.SalesOrderLine SET PriceListId = {ids.ListB} WHERE SalesOrderLineId = {ids.OldLineA};", 547, "FK_SalesOrderLine_PriceList");
        await RejectedAsync(c, $"UPDATE dbo.SalesOrderLine SET OfferId = {ids.OfferB} WHERE SalesOrderLineId = {ids.OldLineA};", 547, "FK_SalesOrderLine_Offer");
        await RejectedAsync(c, $"UPDATE dbo.SalesOrderLine SET PriceChainId = {ids.ChainB} WHERE SalesOrderLineId = {ids.OldLineA};", 547, "FK_SalesOrderLine_PriceChain");

        // lo de la misma compañía sí entra
        await ExecAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId, IsActive) VALUES ({a}, {ids.ListA}, {ids.ChainA}, 0);");
        await ExecAsync(c, $"INSERT INTO dbo.OfferChain (TenantId, OfferId, ChainId, IsActive) VALUES ({b}, {ids.OfferB}, {ids.ChainB}, 0);");
        await ExecAsync(c, special.Replace("Especial ajeno", "Especial propio") + $"NULL, {ids.ChainA});");
        await ExecAsync(c, $"UPDATE dbo.SalesOrderLine SET PriceSource = N'CHAIN_LIST', PriceListId = {ids.ListA}, PriceChainId = {ids.ChainA}, ResolvedUnitPrice = 5 WHERE SalesOrderLineId = {ids.OldLineA};");
        await ExecAsync(c, $"UPDATE dbo.SalesOrderLine SET PriceSource = NULL, PriceListId = NULL, OfferId = {ids.OfferAllA}, PriceChainId = NULL, ResolvedUnitPrice = NULL WHERE SalesOrderLineId = {ids.OldLineA};");
        await ExecAsync(c, $"UPDATE dbo.SalesOrderLine SET OfferId = NULL WHERE SalesOrderLineId = {ids.OldLineA};");
    }

    [SqlServerFact]
    public async Task Filtered_unique_indexes_reject_active_duplicates()
    {
        var ids = await PrepareAsync();
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        var a = ids.TenantA;
        // una lista nueva y una oferta nueva para no chocar con las otras pruebas
        var listId = (int)(await ScalarAsync(c, $"INSERT INTO dbo.PriceList (PublicId, TenantId, Name, ValidFrom, IsPublished, AppliesToAll, IsActive, CreatedAtUtc) OUTPUT INSERTED.PriceListId VALUES (NEWID(), {a}, N'Con cadenas', '2026-12-01', 0, 0, 1, SYSUTCDATETIME());"))!;
        var offerId = (int)(await ScalarAsync(c, $"INSERT INTO dbo.Offer (PublicId, TenantId, Name, ValidFrom, IsPublished, AppliesToAll, AudienceMode, IsActive, CreatedAtUtc) OUTPUT INSERTED.OfferId VALUES (NEWID(), {a}, N'Con cadenas', '2026-12-01', 0, 0, N'EXCLUDE', 1, SYSUTCDATETIME());"))!;

        await ExecAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId) VALUES ({a}, {listId}, {ids.ChainA});");
        await RejectedAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId) VALUES ({a}, {listId}, {ids.ChainA});", 2601, "UX_PriceListChain_Chain");
        await ExecAsync(c, $"INSERT INTO dbo.PriceListChain (TenantId, PriceListId, ChainId, IsActive) VALUES ({a}, {listId}, {ids.ChainA}, 0);");   // quitada: entra
        await ExecAsync(c, $"INSERT INTO dbo.OfferChain (TenantId, OfferId, ChainId) VALUES ({a}, {offerId}, {ids.ChainA});");
        await RejectedAsync(c, $"INSERT INTO dbo.OfferChain (TenantId, OfferId, ChainId) VALUES ({a}, {offerId}, {ids.ChainA});", 2601, "UX_OfferChain_Chain");
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.PriceListChain WHERE PriceListId = {listId} AND IsActive = 1"));

        // una sola dedicada activa por dueño y vigencia (ValidTo NULL cuenta como valor)
        string Dedicated(string owner, string to, int active)
            => $"INSERT INTO dbo.PriceList (PublicId, TenantId, Name, ValidFrom, ValidTo, IsPublished, AppliesToAll, IsActive, CreatedAtUtc, {owner}) VALUES (NEWID(), {a}, N'Dedicada', '2027-01-01', {to}, 1, 0, {active}, SYSUTCDATETIME(), ";
        await ExecAsync(c, Dedicated("SpecialChainId", "NULL", 1) + $"{ids.ChainA});");
        await RejectedAsync(c, Dedicated("SpecialChainId", "NULL", 1) + $"{ids.ChainA});", 2601, "UX_PriceList_SpecialChain");
        await ExecAsync(c, Dedicated("SpecialChainId", "NULL", 0) + $"{ids.ChainA});");             // inactiva: entra
        await ExecAsync(c, Dedicated("SpecialChainId", "'2027-01-31'", 1) + $"{ids.ChainA});");     // otra vigencia: entra
        await ExecAsync(c, Dedicated("SpecialClientId", "NULL", 1) + $"{ids.ClientA});");
        await RejectedAsync(c, Dedicated("SpecialClientId", "NULL", 1) + $"{ids.ClientA});", 2601, "UX_PriceList_SpecialClient");
    }
}
