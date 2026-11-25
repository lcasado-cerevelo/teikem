using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Identity;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Scripts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — la sección de logistica-db-update.sql, leída SOLO hasta la siguiente (SqlSections.From): va después de la del Lote 243,
/// un solo DROP (el índice UX_SalesQuota_RepMonth, que vuelve filtrado con el mismo nombre) y un solo ALTER COLUMN, los dos con guarda; sin DELETE ni permisos.
/// Contra SQL Server real (si hay TEIKEM_TEST_SQLSERVER) corre DOS veces sobre una base desechable con la forma del 241 (SalesQuota.SalesRepId NOT NULL e índice sin
/// filtro, sin las columnas ni la tabla del lote), precedida cada vez por la guarda del 241 que crea UX_SalesQuota_RepMonth (como en db-update), y comprueba la
/// migración de representantes a personas de la ruta, los CHECK, los índices de titular y que la segunda corrida no cambia nada.
/// </summary>
public sealed class SalesRoutesSqlTests(SalesRoutesSqlFixture fx) : IClassFixture<SalesRoutesSqlFixture>
{
    private const string Marker = "2026-11-25 — Lote 244 (Lote A del plan de rutas): rutas de venta, titular y apoyos, vendedor preferido, cuota por ruta o persona";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    /// <summary>La sección: desde su comentario de encabezado hasta el de la siguiente (o el final del archivo).</summary>
    private static string Section()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > 0, "Falta la sección del Lote 244 en logistica-db-update.sql.");
        return SqlSections.From(sql, sql.LastIndexOf("/* ----", at, StringComparison.Ordinal));
    }

    /// <summary>Las sentencias sin los comentarios (lo que de verdad corre).</summary>
    private static string Statements(string section)
        => string.Join("\n", Regex.Replace(section, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline)
            .Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    private static List<string> Batches(string section)
        => Regex.Split(section, @"^\s*GO\s*$", RegexOptions.Multiline).Select(b => b.Trim()).Where(b => Statements(b).Trim().Length > 0).ToList();

    [Fact]
    public void The_section_goes_after_lote_243_names_its_decisions_and_says_it_only_adds_and_is_idempotent()
    {
        var sql = UpdateSql();
        var at = sql.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(at > sql.IndexOf("2026-11-24 — Lote 243 (Lote G del plan de Solutions)", StringComparison.Ordinal));
        var section = Section();
        Assert.Contains("(docs/lote244-decisiones.md)", section);
        Assert.Contains("Idempotente.", section);
        Assert.EndsWith("GO", section.TrimEnd());
    }

    [Fact]
    public void The_section_has_one_guarded_drop_one_guarded_alter_column_and_no_forbidden_words()
    {
        var section = Section();
        var code = Statements(section);
        // prohibido también en los comentarios
        foreach (var word in new[] { "DELETE", "Dispatcher", "PriceList", "UPDATE dbo.Tenant", "DROP TABLE", "DROP COLUMN", "INSERT INTO dbo.Permission", "dbo.RolePermission" })
            Assert.DoesNotContain(word, section, StringComparison.OrdinalIgnoreCase);
        // un solo DROP: el índice del 241, solo mientras no tenga filtro
        Assert.Single(Regex.Matches(code, @"\bDROP\b", RegexOptions.IgnoreCase));
        Assert.Contains("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SalesQuota_RepMonth' AND object_id = OBJECT_ID('dbo.SalesQuota') AND has_filter = 0)\n"
            + "    DROP INDEX UX_SalesQuota_RepMonth ON dbo.SalesQuota;", code);
        // un solo ALTER COLUMN: SalesRepId a NULL, solo si sigue NOT NULL
        Assert.Single(Regex.Matches(code, @"\bALTER\s+COLUMN\b", RegexOptions.IgnoreCase));
        Assert.Contains("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesQuota') AND name = 'SalesRepId' AND is_nullable = 0)\n"
            + "    ALTER TABLE dbo.SalesQuota ALTER COLUMN SalesRepId INT NULL;", code);
        // el índice vuelve con el MISMO nombre, filtrado (así la guarda del 241 lo salta)
        Assert.Contains("EXEC(N'CREATE UNIQUE INDEX UX_SalesQuota_RepMonth ON dbo.SalesQuota(TenantId, SalesRepId, [Month]) WHERE SalesRepId IS NOT NULL;');", code);
        Assert.True(code.IndexOf("DROP INDEX UX_SalesQuota_RepMonth", StringComparison.Ordinal) < code.IndexOf("ALTER COLUMN SalesRepId", StringComparison.Ordinal));
        Assert.True(code.IndexOf("ALTER COLUMN SalesRepId", StringComparison.Ordinal) < code.IndexOf("CREATE UNIQUE INDEX UX_SalesQuota_RepMonth", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_structure_change_has_its_guard()
    {
        var code = Statements(Section());
        foreach (var (table, column) in new[] { ("SalesQuota", "SalesRouteId"), ("Client", "SalesRouteId"), ("Client", "PreferredSalesUserId"), ("SalesOrder", "SalesRouteId"),
                     ("SalesOrder", "SellerUserId"), ("SalesOrder", "SalesCopyAtUtc"), ("SalesAnalysisSetting", "QuotaBy") })
            Assert.Contains($"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL\n    ALTER TABLE dbo.{table} ADD {column} ", code);
        foreach (var fk in new[] { "FK_SalesQuota_Route", "FK_Client_SalesRoute", "FK_SalesOrder_SalesRoute" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{fk}', N'F') IS NULL", code);
        foreach (var ck in new[] { "CK_SalesQuota_Target", "CK_SalesAnalysisSetting_QuotaBy" })
            Assert.Contains($"IF OBJECT_ID(N'dbo.{ck}', N'C') IS NULL", code);
        Assert.Contains("IF OBJECT_ID(N'dbo.SalesRouteAssignment', N'U') IS NULL CREATE TABLE dbo.SalesRouteAssignment (", code);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_SalesRoute_IdTenant' AND parent_object_id = OBJECT_ID('dbo.SalesRoute'))", code);
        // cada índice con su guarda
        var indexes = Regex.Matches(code, @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(11, indexes.Count);
        foreach (var name in indexes) Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}'", code);
        // las FK a la ruta son compuestas (una ruta de otra compañía no se puede colgar de nada)
        Assert.Equal(4, Regex.Matches(code, @"FOREIGN KEY \(SalesRouteId, TenantId\) REFERENCES dbo\.SalesRoute\(SalesRouteId, TenantId\)").Count);
    }

    [Fact]
    public void The_codes_of_the_sql_are_the_ones_of_the_domain()
    {
        var code = Statements(Section());
        Assert.Contains($"(N'{SalesRouteCodes.AssignmentEntityType}', N'{{\"es\":\"Personas de la ruta (titular y apoyo)\",\"en\":\"Route people (primary and support)\"}}', 197)", code);
        Assert.Contains($"CHECK (QuotaBy IN ('{SalesRouteCodes.QuotaByRep}', '{SalesRouteCodes.QuotaByRoute}'))", code);
        Assert.Equal(2, SalesRouteCodes.QuotaByValues.Length);
        Assert.Contains($"DEFAULT '{SalesRouteCodes.QuotaByRep}'", code);
        Assert.Contains($"s.Entity = N'{StatusDomains.MembershipStatus}' AND s.InternalCode = N'{MembershipStatuses.Active}'", code);
        // la migración corre una sola vez, con su marca
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L244_SALES_ROUTES_FROM_REPS')", code);
        Assert.Contains("INSERT INTO dbo.AppliedMigration (Code) VALUES ('L244_SALES_ROUTES_FROM_REPS');", code);
        Assert.Equal(PermissionCatalog.SalesAnalysisView, PermissionCatalog.DataSourceReadPermission(SalesRouteCodes.RouteDataSourceKey));
        Assert.Equal(PermissionCatalog.SalesAnalysisView, PermissionCatalog.DataSourceReadPermission(SalesRouteCodes.AssignmentDataSourceKey));
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

    /// <summary>La guarda del 241 que crea UX_SalesQuota_RepMonth si falta: en db-update corre ANTES de esta sección en cada corrida.</summary>
    private static string Lote241QuotaIndexBatch()
    {
        var sql = UpdateSql();
        var start = sql.IndexOf("2026-11-12 — Lote 241: «Análisis de ventas»", StringComparison.Ordinal);
        return Assert.Single(Batches(SqlSections.From(sql, start)), b => b.Contains("CREATE UNIQUE INDEX UX_SalesQuota_RepMonth", StringComparison.Ordinal));
    }

    /// <summary>Lo que la sección deja en la base: objetos, índices (con filtro), restricciones y datos del lote. Si la segunda corrida cambia algo, cambia esto.</summary>
    private const string FingerprintSql = """
        SELECT CONCAT(
          (SELECT STRING_AGG(CAST(CONCAT(o.name, ':', o.type) AS NVARCHAR(MAX)) COLLATE DATABASE_DEFAULT, '|') WITHIN GROUP (ORDER BY o.name) FROM sys.objects o WHERE o.is_ms_shipped = 0),
          '#', (SELECT STRING_AGG(CAST(CONCAT(OBJECT_NAME(i.object_id), '.', i.name, ':', i.is_unique, ':', i.filter_definition) AS NVARCHAR(MAX)) COLLATE DATABASE_DEFAULT, '|') WITHIN GROUP (ORDER BY OBJECT_NAME(i.object_id), i.name)
                FROM sys.indexes i WHERE i.name IS NOT NULL AND OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1),
          '#', (SELECT STRING_AGG(CAST(CONCAT(a.SalesRouteAssignmentId, ':', a.TenantId, ':', a.SalesRouteId, ':', a.UserId, ':', a.IsPrimary, ':', a.ValidFrom, ':', a.ValidTo, ':', a.IsActive) AS NVARCHAR(MAX)), '|')
                WITHIN GROUP (ORDER BY a.SalesRouteAssignmentId) FROM dbo.SalesRouteAssignment a),
          '#', (SELECT STRING_AGG(CAST(CONCAT(c.ClientId, ':', c.SalesRouteId, ':', c.PreferredSalesUserId) AS NVARCHAR(MAX)), '|') WITHIN GROUP (ORDER BY c.ClientId) FROM dbo.Client c),
          '#', (SELECT COUNT(*) FROM dbo.LookupCode), '#', (SELECT COUNT(*) FROM dbo.AppliedMigration),
          '#', (SELECT STRING_AGG(CAST(CONCAT(s.TenantId, ':', s.QuotaBy) AS NVARCHAR(MAX)), '|') FROM dbo.SalesAnalysisSetting s),
          '#', (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesQuota') AND name = 'SalesRepId'))
        """;

    [SqlServerFact]
    public async Task On_sql_server_the_section_runs_twice_migrates_the_reps_and_its_indexes_and_checks_hold()
    {
        var w = await fx.SeedAsync();
        await fx.ShapeLikeLote241Async();

        await using var c = new SqlConnection(fx.ConnectionString);
        var messages = new List<string>();
        c.InfoMessage += (_, e) => { foreach (SqlError err in e.Errors) messages.Add(err.Message); };
        await c.OpenAsync();

        var batches = new List<string> { Lote241QuotaIndexBatch() };
        batches.AddRange(Batches(Section()));
        foreach (var batch in batches) await ExecAsync(c, batch);
        var first = (string)(await ScalarAsync(c, FingerprintSql))!;
        var firstMessages = messages.ToList();
        messages.Clear();
        foreach (var batch in batches) await ExecAsync(c, batch);
        var second = (string)(await ScalarAsync(c, FingerprintSql))!;

        // la segunda corrida no cambia nada ni vuelve a migrar
        Assert.Equal(first, second);
        Assert.DoesNotContain(messages, m => m.StartsWith("Lote 244", StringComparison.Ordinal));
        Assert.Contains(firstMessages, m => m == "Lote 244: 1 clientes tomaron la ruta de su representante; 1 titulares y 2 apoyos.");
        Assert.Contains(firstMessages, m => m.Contains("representantes activos sin usuario", StringComparison.Ordinal) && m.Contains("Rep sin usuario", StringComparison.Ordinal));

        // SalesQuota: el índice del 241 con el MISMO nombre, ahora filtrado; SalesRepId admite NULL
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_SalesQuota_RepMonth' AND object_id = OBJECT_ID('dbo.SalesQuota') AND has_filter = 1 AND is_unique = 1"));
        Assert.Contains("SalesRepId", (string)(await ScalarAsync(c, "SELECT filter_definition FROM sys.indexes WHERE name = 'UX_SalesQuota_RepMonth' AND object_id = OBJECT_ID('dbo.SalesQuota')"))!);
        Assert.Equal(true, await ScalarAsync(c, "SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesQuota') AND name = 'SalesRepId'"));
        Assert.Equal(1, await ScalarAsync(c, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_SalesRep_User' AND object_id = OBJECT_ID('dbo.SalesRep')"));
        Assert.Equal(197, await ScalarAsync(c, $"SELECT SortOrder FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'{SalesRouteCodes.AssignmentEntityType}'"));
        Assert.Equal(SalesRouteCodes.QuotaByRep, await ScalarAsync(c, $"SELECT QuotaBy FROM dbo.SalesAnalysisSetting WHERE TenantId = {w.TenantA}"));

        // migración: el cliente con representante de ruta activa toma la ruta; el de ruta inactiva y el sin representante, no
        Assert.Equal(w.North, await ScalarAsync(c, $"SELECT SalesRouteId FROM dbo.Client WHERE ClientId = {w.ClientNorth}"));
        Assert.Null(await ScalarAsync(c, $"SELECT SalesRouteId FROM dbo.Client WHERE ClientId = {w.ClientInactiveRoute}"));
        Assert.Null(await ScalarAsync(c, $"SELECT SalesRouteId FROM dbo.Client WHERE ClientId = {w.ClientNoRep}"));
        // un solo candidato → titular desde el mes de la cuota más antigua; dos → los dos de apoyo; nada para suspendidos, inactivos, sin usuario ni otra compañía
        var rows = new List<(int Tenant, int Route, int User, bool Primary, DateTime From)>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom FROM dbo.SalesRouteAssignment WHERE IsActive = 1 AND ValidTo IS NULL ORDER BY SalesRouteId, UserId";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) rows.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetBoolean(3), r.GetDateTime(4)));
        }
        var from = new DateTime(2026, 1, 1);
        Assert.Equal(new[]
        {
            (w.TenantA, w.North, w.UserNorth, true, from),
            (w.TenantA, w.Metro, w.UserMetro1, false, from),
            (w.TenantA, w.Metro, w.UserMetro2, false, from),
        }.OrderBy(x => x.Item2).ThenBy(x => x.Item3), rows);

        // el CHECK uno u otro y el índice de la cuota por ruta
        var both = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesQuota (TenantId, SalesRepId, SalesRouteId, [Month], Amount, IsActive) VALUES ({w.TenantA}, {w.RepNorth}, {w.North}, '2026-03-01', 10, 1);"));
        Assert.Contains("CK_SalesQuota_Target", both.Message);
        var none = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesQuota (TenantId, [Month], Amount, IsActive) VALUES ({w.TenantA}, '2026-03-01', 10, 1);"));
        Assert.Contains("CK_SalesQuota_Target", none.Message);
        await ExecAsync(c, $"INSERT INTO dbo.SalesQuota (TenantId, SalesRouteId, [Month], Amount, IsActive) VALUES ({w.TenantA}, {w.North}, '2026-03-01', 10, 1);");
        await ExecAsync(c, $"INSERT INTO dbo.SalesQuota (TenantId, SalesRouteId, [Month], Amount, IsActive) VALUES ({w.TenantA}, {w.Metro}, '2026-03-01', 10, 1);");
        var twiceRoute = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesQuota (TenantId, SalesRouteId, [Month], Amount, IsActive) VALUES ({w.TenantA}, {w.North}, '2026-03-01', 20, 1);"));
        Assert.Contains("UX_SalesQuota_RouteMonth", twiceRoute.Message);
        var twiceRep = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesQuota (TenantId, SalesRepId, [Month], Amount, IsActive) VALUES ({w.TenantA}, {w.RepNorth}, '2026-01-01', 20, 1);"));
        Assert.Contains("UX_SalesQuota_RepMonth", twiceRep.Message);
        // la FK compuesta: la cuota de una ruta de otra compañía no entra
        var otherQuota = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesQuota (TenantId, SalesRouteId, [Month], Amount, IsActive) VALUES ({w.TenantB}, {w.North}, '2026-03-01', 10, 1);"));
        Assert.Contains("FK_SalesQuota_Route", otherQuota.Message);
        var otherClient = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"UPDATE dbo.Client SET SalesRouteId = {w.North} WHERE ClientId = {w.ClientOtherTenant};"));
        Assert.Contains("FK_Client_SalesRoute", otherClient.Message);
        var cks = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, $"UPDATE dbo.SalesAnalysisSetting SET QuotaBy = 'TEAM' WHERE TenantId = {w.TenantA};"));
        Assert.Contains("CK_SalesAnalysisSetting_QuotaBy", cks.Message);

        // un solo titular vigente por ruta; una sola fila vigente por persona y ruta; una anulada siempre lleva ValidTo
        var secondPrimary = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom) VALUES ({w.TenantA}, {w.North}, {w.UserMetro1}, 1, '2026-11-01');"));
        Assert.Contains("UX_SalesRouteAssignment_Primary", secondPrimary.Message);
        var samePerson = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom) VALUES ({w.TenantA}, {w.Metro}, {w.UserMetro1}, 0, '2026-11-01');"));
        Assert.Contains("UX_SalesRouteAssignment_Open", samePerson.Message);
        var voidOpen = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom, IsActive) VALUES ({w.TenantA}, {w.South}, {w.UserMetro1}, 0, '2026-11-01', 0);"));
        Assert.Contains("CK_SalesRouteAssignment_Void", voidOpen.Message);
        var backwards = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom, ValidTo) VALUES ({w.TenantA}, {w.South}, {w.UserMetro1}, 0, '2026-11-02', '2026-11-01');"));
        Assert.Contains("CK_SalesRouteAssignment_Dates", backwards.Message);
        var otherRoute = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c,
            $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom) VALUES ({w.TenantB}, {w.North}, {w.UserMetro1}, 0, '2026-11-01');"));
        Assert.Contains("FK_SalesRouteAssignment_Route", otherRoute.Message);
        // cerrado (con ValidTo) o anulado no ocupan los índices de vigentes: el titular de antes y el nuevo conviven
        await ExecAsync(c, $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom, ValidTo) VALUES ({w.TenantA}, {w.North}, {w.UserMetro1}, 1, '2025-06-01', '2025-12-31');");
        await ExecAsync(c, $"INSERT INTO dbo.SalesRouteAssignment (TenantId, SalesRouteId, UserId, IsPrimary, ValidFrom, ValidTo, IsActive) VALUES ({w.TenantA}, {w.North}, {w.UserMetro2}, 1, '2026-11-01', '2026-11-01', 0);");
        Assert.Equal(1, await ScalarAsync(c, $"SELECT COUNT(*) FROM dbo.SalesRouteAssignment WHERE SalesRouteId = {w.North} AND IsPrimary = 1 AND ValidTo IS NULL"));
    }
}

/// <summary>Lo que siembra <see cref="SalesRoutesSqlFixture.SeedAsync"/> (ids reales de la base desechable).</summary>
public sealed record SalesRoutesSqlWorld(
    int TenantA, int TenantB, int North, int Metro, int South, int RepNorth,
    int UserNorth, int UserMetro1, int UserMetro2, int ClientNorth, int ClientInactiveRoute, int ClientNoRep, int ClientOtherTenant);

/// <summary>
/// Lote 244 — base DESECHABLE propia (Teikem_routetest_&lt;guid&gt;, se borra al terminar), creada desde el modelo EF y sin las llaves foráneas del modelo (se
/// siembran estatus, usuarios y compañías sueltos). <see cref="ShapeLikeLote241Async"/> la deja como estaba antes del lote. Se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class SalesRoutesSqlFixture : IAsyncLifetime
{
    public string? ConnectionString { get; private set; }
    private string? _database;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;
        _database = "Teikem_routetest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        try
        {
            await using var db = NewDb(new TenantContext());
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
                  FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
                EXEC sp_executesql @sql;
                """);
            // lo que el script de estructura trae y el modelo EF no: el DEFAULT de LookupCode.CreatedAtUtc y dbo.AppliedMigration
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE dbo.LookupCode ADD CONSTRAINT DF_LookupCode_CreatedAtUtc_Test DEFAULT SYSUTCDATETIME() FOR CreatedAtUtc;
                IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                """);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public TeikemDbContext NewDb(ITenantContext tenant) => new(new DbContextOptionsBuilder<TeikemDbContext>()
        .UseSqlServer(ConnectionString)
        .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)).Options, tenant);

    /// <summary>
    /// Compañía A: Norte (un representante con persona activa → titular), Metro (dos → apoyos), Sur (uno suspendido y uno sin usuario → nada), Oeste (usuario
    /// inactivo → nada) y Cadenas INACTIVA (→ nada); una cuota de enero de 2026 (desde ahí valen) y su ajuste del análisis. Compañía B: una ruta cuyo representante es
    /// un usuario que solo es miembro de A (→ nada). Todo antes de quitarle a la base lo del lote (el modelo EF ya trae las columnas nuevas).
    /// </summary>
    public async Task<SalesRoutesSqlWorld> SeedAsync()
    {
        await using var db = NewDb(new TenantContext());
        var a = new Tenant { Name = "Compañía A rutas", DefaultLangCode = "es" };
        var b = new Tenant { Name = "Compañía B rutas", DefaultLangCode = "es" };
        db.Tenants.AddRange(a, b);
        var active = new StatusCode { Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Active, LabelJson = "{\"es\":\"Activa\"}", StageKindLookupId = 1, SortOrder = 1, IsActive = true };
        var suspended = new StatusCode { Entity = StatusDomains.MembershipStatus, InternalCode = MembershipStatuses.Suspended, LabelJson = "{\"es\":\"Suspendida\"}", StageKindLookupId = 1, SortOrder = 2, IsActive = true };
        db.StatusCodes.AddRange(active, suspended);
        await db.SaveChangesAsync();

        ApplicationUser User(string name, bool isActive = true)
        {
            var mail = $"{name.Replace(' ', '.').ToLowerInvariant()}@x.test";
            var u = new ApplicationUser { UserName = mail, Email = mail, FullName = name, IsActive = isActive };
            db.Users.Add(u);
            return u;
        }
        var uNorth = User("Vendedor Norte");
        var uMetro1 = User("Vendedor Metro Uno");
        var uMetro2 = User("Vendedor Metro Dos");
        var uSuspended = User("Vendedor Suspendido");
        var uInactive = User("Vendedor Inactivo", isActive: false);
        var uChains = User("Vendedor Cadenas");
        await db.SaveChangesAsync();
        foreach (var (u, status) in new[] { (uNorth, active), (uMetro1, active), (uMetro2, active), (uSuspended, suspended), (uInactive, active), (uChains, active) })
            db.UserTenants.Add(new UserTenant { UserId = u.Id, TenantId = a.TenantId, StatusCodeId = status.StatusCodeId });
        await db.SaveChangesAsync();

        SalesRoute Route(int tenantId, string name, bool isActive = true)
        {
            var r = new SalesRoute { TenantId = tenantId, Name = name, IsActive = isActive };
            db.Set<SalesRoute>().Add(r);
            return r;
        }
        var north = Route(a.TenantId, "Norte");
        var metro = Route(a.TenantId, "Metro");
        var south = Route(a.TenantId, "Sur");
        var west = Route(a.TenantId, "Oeste");
        var chains = Route(a.TenantId, "Cadenas", isActive: false);
        var otherRoute = Route(b.TenantId, "Norte B");
        await db.SaveChangesAsync();

        SalesRep Rep(int tenantId, string name, int routeId, int? userId)
        {
            var r = new SalesRep { TenantId = tenantId, Name = name, SalesRouteId = routeId, UserId = userId, IsActive = true };
            db.Set<SalesRep>().Add(r);
            return r;
        }
        var repNorth = Rep(a.TenantId, "Rep Norte", north.SalesRouteId, uNorth.Id);
        Rep(a.TenantId, "Rep Metro Uno", metro.SalesRouteId, uMetro1.Id);
        Rep(a.TenantId, "Rep Metro Dos", metro.SalesRouteId, uMetro2.Id);
        Rep(a.TenantId, "Rep Suspendido", south.SalesRouteId, uSuspended.Id);
        Rep(a.TenantId, "Rep sin usuario", south.SalesRouteId, null);
        Rep(a.TenantId, "Rep Inactivo", west.SalesRouteId, uInactive.Id);
        var repChains = Rep(a.TenantId, "Rep Cadenas", chains.SalesRouteId, uChains.Id);
        Rep(b.TenantId, "Rep de otra compañía", otherRoute.SalesRouteId, uNorth.Id);
        await db.SaveChangesAsync();

        Client NewClient(int tenantId, string code, int? repId)
        {
            var cl = new Client
            {
                PublicId = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = "Cliente " + code, StatusCodeId = active.StatusCodeId, IsActive = true,
                CreatedAtUtc = DateTime.UtcNow, SalesRepId = repId,
            };
            db.Clients.Add(cl);
            return cl;
        }
        var cNorth = NewClient(a.TenantId, "RUTA-N", repNorth.SalesRepId);
        var cChains = NewClient(a.TenantId, "RUTA-C", repChains.SalesRepId);
        var cNoRep = NewClient(a.TenantId, "RUTA-0", null);
        var cOther = NewClient(b.TenantId, "RUTA-B", null);
        db.Set<SalesQuota>().Add(new SalesQuota { TenantId = a.TenantId, SalesRepId = repNorth.SalesRepId, Month = new DateOnly(2026, 1, 1), Amount = 1000m, IsActive = true });
        db.Set<SalesAnalysisSetting>().Add(new SalesAnalysisSetting { TenantId = a.TenantId });
        await db.SaveChangesAsync();

        return new SalesRoutesSqlWorld(a.TenantId, b.TenantId, north.SalesRouteId, metro.SalesRouteId, south.SalesRouteId, repNorth.SalesRepId,
            uNorth.Id, uMetro1.Id, uMetro2.Id, cNorth.ClientId, cChains.ClientId, cNoRep.ClientId, cOther.ClientId);
    }

    /// <summary>
    /// La base como estaba en producción antes del lote: sin dbo.SalesRouteAssignment ni las columnas nuevas (con sus índices), sin UQ_SalesRoute_IdTenant ni
    /// UX_SalesRep_User, y SalesQuota.SalesRepId NOT NULL con UX_SalesQuota_RepMonth SIN filtro (como lo crea el 241).
    /// </summary>
    public async Task ShapeLikeLote241Async()
    {
        await using var c = new SqlConnection(ConnectionString);
        await c.OpenAsync();
        async Task Exec(string sql)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        async Task DropIndexesOn(string table, string column) => await Exec($"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.{table};'
              FROM sys.indexes i
             WHERE i.object_id = OBJECT_ID('dbo.{table}') AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
               AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND col.name = '{column}');
            EXEC sp_executesql @sql;
            """);

        await Exec("IF OBJECT_ID(N'dbo.SalesRouteAssignment', N'U') IS NOT NULL DROP TABLE dbo.SalesRouteAssignment;");
        foreach (var (table, column) in new[] { ("Client", "SalesRouteId"), ("Client", "PreferredSalesUserId"), ("SalesOrder", "SalesRouteId"), ("SalesOrder", "SellerUserId"),
                     ("SalesOrder", "SalesCopyAtUtc"), ("SalesQuota", "SalesRouteId"), ("SalesAnalysisSetting", "QuotaBy") })
        {
            await DropIndexesOn(table, column);
            await Exec($"""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE dbo.{table} DROP CONSTRAINT ' + QUOTENAME(d.name) + N';'
                  FROM sys.default_constraints d JOIN sys.columns col ON col.object_id = d.parent_object_id AND col.column_id = d.parent_column_id
                 WHERE d.parent_object_id = OBJECT_ID('dbo.{table}') AND col.name = '{column}';
                EXEC sp_executesql @sql;
                IF COL_LENGTH('dbo.{table}', '{column}') IS NOT NULL ALTER TABLE dbo.{table} DROP COLUMN {column};
                """);
        }
        await DropIndexesOn("SalesQuota", "SalesRepId");
        await Exec("ALTER TABLE dbo.SalesQuota ALTER COLUMN SalesRepId INT NOT NULL;");
        await Exec("CREATE UNIQUE INDEX UX_SalesQuota_RepMonth ON dbo.SalesQuota(TenantId, SalesRepId, [Month]);");
        await Exec("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SalesRep_User' AND object_id = OBJECT_ID('dbo.SalesRep')) DROP INDEX UX_SalesRep_User ON dbo.SalesRep;");
        await Exec("IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_SalesRoute_IdTenant') ALTER TABLE dbo.SalesRoute DROP CONSTRAINT UQ_SalesRoute_IdTenant;");
    }

    public async Task DisposeAsync()
    {
        if (_database is null) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar)) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }
}
