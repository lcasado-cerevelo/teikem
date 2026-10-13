using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;
using static Teikem.Tests.TenantFilterHarness;

namespace Teikem.Tests;

/// <summary>Prueba que necesita SQL Server: se omite si no está definida la variable TEIKEM_TEST_SQLSERVER (cadena SIN base de datos).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TenantIsolationSqlServerFixture.EnvVar)))
            Skip = $"Sin SQL Server de prueba (defina {TenantIsolationSqlServerFixture.EnvVar}; el CI lo define).";
    }
}

/// <summary>
/// Base DESECHABLE propia de la prueba (Teikem_isotest_&lt;guid&gt;, nunca una base existente): se crea desde el modelo EF, se le quitan
/// las restricciones de FK/CHECK y los índices únicos (la siembra genérica cruza llaves entre compañías a propósito), se siembra
/// con el arnés y se BORRA al terminar.
/// </summary>
public sealed class TenantIsolationSqlServerFixture : IAsyncLifetime
{
    public const string EnvVar = "TEIKEM_TEST_SQLSERVER";
    public string? ConnectionString { get; private set; }
    private string? _database;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(server)) return;
        _database = "Teikem_isotest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        var (db, tenant, _) = Create<TeikemDbContext>(TenantA);
        try
        {
        await using (db)
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
                  FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ALL;' FROM sys.tables t;
                SELECT @sql += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N';'
                  FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.is_unique = 1 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0;
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(k.name) + N';'
                  FROM sys.key_constraints k JOIN sys.tables t ON t.object_id = k.parent_object_id WHERE k.type = 'UQ';
                EXEC sp_executesql @sql;
                """);
            await SeedAsync(db, tenant);
        }
        }
        catch
        {
            await DisposeAsync();   // la base desechable no queda huérfana si la preparación falla
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is null) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(EnvVar)) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_database}') IS NOT NULL BEGIN ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    internal (TContext Db, TenantContext Tenant, SqlCaptureInterceptor Capture) Create<TContext>(int? tenantId) where TContext : TeikemDbContext
    {
        var tenant = new TenantContext { TenantId = tenantId, UserId = 1, IsAuthenticated = true };
        var capture = new SqlCaptureInterceptor(suppress: false);
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(capture)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        var db = (TContext)Activator.CreateInstance(typeof(TContext), options, tenant)!;
        return (db, tenant, capture);
    }
}

/// <summary>
/// Lote 42 — NO FILTRACIÓN entre compañías contra SQL Server REAL, para TODAS las entidades con filtro de tenant (por reflexión):
/// mismas formas que la prueba InMemory (lista, proyección, Count, Any, Contains, Find, ordenar + paginar, Include y AsSplitQuery);
/// cada comando SQL ejecutado con la compañía activa lleva la condición de TenantId en cada tabla con filtro y ninguno conserva la
/// rama del bypass; los resultados son IDÉNTICOS a los del filtro anterior (contexto con el filtro viejo sobre la misma base) con A,
/// con B, con bypass y sin compañía; FromSql sigue compuesto con el filtro y SQL crudo sin cambios; y el filtro nuevo SÍ admite
/// FORCESEEK sobre un índice que empieza por TenantId, el viejo no («no hay plan», la causa medida en el Lote 41).
/// </summary>
public sealed class TenantIsolationSqlServerTests(TenantIsolationSqlServerFixture fx) : IClassFixture<TenantIsolationSqlServerFixture>
{
    [SqlServerFact]
    public async Task Ninguna_entidad_deja_ver_filas_de_otra_compania_y_todo_SQL_lleva_la_condicion_de_TenantId()
    {
        var (db, tenant, capture) = fx.Create<TeikemDbContext>(TenantA);
        await using var _ = db;
        foreach (var t in new[] { TenantA, TenantB })
        {
            capture.Commands.Clear();
            using (t == TenantB ? tenant.As(TenantB) : null)
                AssertOnlyTenant(db.Model, await RunAllShapesAsync(db), t);
            var withFilteredTables = capture.Commands.Where(c => FilteredTableOccurrences(db.Model, c.Sql) > 0).ToList();
            Assert.True(withFilteredTables.Count > 1000, $"Solo {withFilteredTables.Count} comandos revisados");
            var problems = withFilteredTables.SelectMany(c => CheckFilteredSql(db.Model, c.Sql, c.Parameters, t).Select(p => p + "\n" + c.Sql)).ToList();
            Assert.True(problems.Count == 0, $"{problems.Count} problemas; primero:\n{problems.FirstOrDefault()}");
        }
    }

    [SqlServerFact]
    public async Task Los_resultados_son_identicos_a_los_del_filtro_anterior()
    {
        var (nu, nuTenant, _) = fx.Create<TeikemDbContext>(TenantA);
        var (old, oldTenant, oldCapture) = fx.Create<LegacyTenantFilterDbContext>(TenantA);
        await using var d1 = nu;
        await using var d2 = old;
        async Task Same(string what)
        {
            var a = await SnapshotAsync(nu);
            var b = await SnapshotAsync(old);
            Assert.True(a.Count > 1000, what);
            var diff = a.Zip(b).Where(x => x.First != x.Second).Select(x => $"nuevo {x.First}\nviejo {x.Second}").ToList();
            Assert.True(a.Count == b.Count && diff.Count == 0, $"{what}: {diff.Count} diferencias\n{string.Join("\n", diff.Take(5))}");
        }
        await Same("compañía A");
        using (nuTenant.As(TenantB)) using (oldTenant.As(TenantB)) await Same("compañía B");
        using (nuTenant.BypassTenantFilter()) using (oldTenant.BypassTenantFilter()) await Same("bypass");
        // alcance de datos del usuario: almacén y/o cliente (la fila 1 de cada principal es de A; la 3, de B), y alcance vacío
        foreach (var (wh, cl) in new (int[]?, int[]?)[] { (new[] { 1 }, null), (null, new[] { 1 }), (new[] { 1 }, new[] { 1, 3 }), (Array.Empty<int>(), null), (null, Array.Empty<int>()) })
        {
            nuTenant.ScopedWarehouseIds = wh; oldTenant.ScopedWarehouseIds = wh;
            nuTenant.ScopedClientIds = cl; oldTenant.ScopedClientIds = cl;
            await Same($"alcance almacén [{string.Join(",", wh ?? Array.Empty<int>())}]{(wh is null ? " (sin)" : "")} cliente [{string.Join(",", cl ?? Array.Empty<int>())}]{(cl is null ? " (sin)" : "")}");
        }
        nuTenant.ScopedWarehouseIds = oldTenant.ScopedWarehouseIds = null;
        nuTenant.ScopedClientIds = oldTenant.ScopedClientIds = null;
        nuTenant.TenantId = null; oldTenant.TenantId = null;
        await Same("sin compañía");
        // el contexto viejo sí conserva el OR (control de que la comparación es contra el filtro anterior)
        Assert.Contains(oldCapture.Commands, c => c.Sql.Contains("= CAST(1 AS bit) OR", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task Bypass_solo_dentro_de_su_bloque_IgnoreQueryFilters_y_sin_compania_falla_cerrado()
    {
        var (db, tenant, capture) = fx.Create<TeikemDbContext>(TenantA);
        await using var _ = db;
        Dictionary<string, int> inside;
        using (tenant.BypassTenantFilter()) inside = await CountAllAsync(db);
        var after = await CountAllAsync(db);
        var ignored = await CountAllAsync(db, ignoreFilters: true);
        tenant.TenantId = null;
        capture.Commands.Clear();
        var none = await CountAllAsync(db);
        foreach (var e in TenantFilteredEntities(db.Model))
        {
            var n = e.ClrType.Name;
            Assert.True(inside[n] == Total(e), $"{n}: bypass {inside[n]}");
            Assert.True(after[n] == ExpectedVisible(e, TenantA), $"{n}: después del bypass {after[n]}");
            Assert.True(ignored[n] == Total(e), $"{n}: IgnoreQueryFilters {ignored[n]}");
            Assert.True(none[n] == (IsOptional(e) ? 1 : 0), $"{n}: sin compañía {none[n]}");
        }
        // sin compañía el parámetro vale -1 (no existe esa compañía)
        Assert.All(capture.Commands, c => Assert.Empty(CheckFilteredSql(db.Model, c.Sql, c.Parameters, -1)));
    }

    [SqlServerFact]
    public async Task FromSql_sigue_compuesto_con_el_filtro_y_el_SQL_crudo_no_cambia()
    {
        var (db, _, capture) = fx.Create<TeikemDbContext>(TenantA);
        await using var _d = db;
        // fila 3 = de la compañía B; fila 1 = de A (ver TenantFilterHarness.RowsFor)
        Assert.Empty(await db.Vehicles.FromSqlInterpolated($"SELECT * FROM dbo.Vehicle WITH (UPDLOCK, ROWLOCK) WHERE VehicleId = {3}").ToListAsync());
        Assert.Single(await db.Vehicles.FromSqlInterpolated($"SELECT * FROM dbo.Vehicle WITH (UPDLOCK, ROWLOCK) WHERE VehicleId = {1}").ToListAsync());
        Assert.Equal(2, capture.Commands.Count(c => System.Text.RegularExpressions.Regex.IsMatch(c.Sql, @"\) AS \[(?<a>\w+)\]\s+WHERE \[\k<a>\]\.\[TenantId\] = @__ef_filter__QueryFilterTenantId_\d+")));
        // SQL crudo (Database.SqlQuery): NUNCA lo filtra EF (ni antes ni ahora); por eso su uso está confinado (RawSqlConfinementTests)
        var raw = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM dbo.Vehicle").ToListAsync();
        Assert.Equal(4, raw.Single());
    }

    [SqlServerFact]
    public async Task El_filtro_nuevo_admite_FORCESEEK_por_TenantId_y_el_anterior_no()
    {
        var (db, _, capture) = fx.Create<TeikemDbContext>(TenantA);
        var (old, _, oldCapture) = fx.Create<LegacyTenantFilterDbContext>(TenantA);
        await using var d1 = db;
        await using var d2 = old;
        await db.Vehicles.CountAsync();
        await old.Vehicles.CountAsync();
        var nuevo = capture.Commands.Last();
        var viejo = oldCapture.Commands.Last();
        // índice que empieza por TenantId (UQ_Vehicle_Code se quitó en la siembra): uno propio de la prueba
        await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_test_Vehicle_Tenant') CREATE INDEX IX_test_Vehicle_Tenant ON dbo.Vehicle(TenantId)");
        await RunWithForceSeekAsync(nuevo.Sql, nuevo.Parameters);   // no lanza
        var ex = await Assert.ThrowsAsync<SqlException>(() => RunWithForceSeekAsync(viejo.Sql, viejo.Parameters));
        Assert.Equal(8622, ex.Number);   // «Query processor could not produce a query plan because of the hints defined in this query»
    }

    private async Task RunWithForceSeekAsync(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        await using var c = new SqlConnection(fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql + " OPTION (TABLE HINT([v], FORCESEEK))";
        foreach (var (k, v) in parameters) cmd.Parameters.AddWithValue("@" + k, v ?? DBNull.Value);
        await cmd.ExecuteScalarAsync();
    }
}
