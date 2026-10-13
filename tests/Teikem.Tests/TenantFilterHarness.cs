using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Common;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 42 — arnés común de las pruebas de NO FILTRACIÓN entre compañías (InMemory, SQL Server real y forma del SQL).
/// Enumera por reflexión del modelo TODAS las entidades con filtro de tenant (ITenantScoped e IOptionallyTenantScoped), siembra
/// por cada una filas de la compañía A, de la B y (si es opcional) globales (TenantId NULL), con llaves foráneas que apuntan a
/// propósito a filas de la OTRA compañía, y corre sobre cada entidad las mismas formas de consulta: lista materializada,
/// proyección, Count, Any, Contains, Find por llave de una fila ajena, ordenar + paginar, e Include de cada navegación.
/// </summary>
internal static class TenantFilterHarness
{
    public const int TenantA = 7001;
    public const int TenantB = 7002;   // mayor que A a propósito: ordenar descendente pondría primero una fila ajena si se filtrara

    /// <summary>Entidades con filtro global de tenant, por reflexión del modelo (no una lista a mano).</summary>
    public static IEntityType[] TenantFilteredEntities(IModel model)
        => model.GetEntityTypes()
            .Where(e => !e.IsOwned() && (typeof(ITenantScoped).IsAssignableFrom(e.ClrType) || typeof(IOptionallyTenantScoped).IsAssignableFrom(e.ClrType)))
            .OrderBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .ToArray();

    public static bool IsOptional(IEntityType e) => typeof(IOptionallyTenantScoped).IsAssignableFrom(e.ClrType);

    /// <summary>Filas sembradas por entidad: 1-2 de A, 3-4 de B y 5 global (solo opcionales). Si la llave ES TenantId: una por compañía.</summary>
    public static IReadOnlyList<(int Row, int? Tenant)> RowsFor(IEntityType e)
    {
        if (KeyIsTenantId(e)) return new (int, int?)[] { (1, TenantA), (3, TenantB) };
        var rows = new List<(int, int?)> { (1, TenantA), (2, TenantA), (3, TenantB), (4, TenantB) };
        if (IsOptional(e)) rows.Add((5, null));
        return rows;
    }

    public static bool KeyIsTenantId(IEntityType e) => e.FindPrimaryKey()!.Properties is [{ Name: "TenantId" }];

    public static int ExpectedVisible(IEntityType e, int tenant) => RowsFor(e).Count(r => r.Tenant == tenant || r.Tenant is null);
    public static int Total(IEntityType e) => RowsFor(e).Count;

    // ------------------------------------------------------------------ siembra

    /// <summary>
    /// Siembra todas las entidades con filtro (con el filtro apagado, como un seeder). Con SQL Server, las llaves explícitas van con
    /// IDENTITY_INSERT (la base de prueba no tiene restricciones de FK ni índices únicos: ver TenantIsolationSqlServerTests).
    /// </summary>
    public static async Task SeedAsync(TeikemDbContext db, TenantContext tenant)
    {
        var relational = db.Database.IsRelational();
        using var bypass = tenant.BypassTenantFilter();
        if (relational) await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var et in TenantFilteredEntities(db.Model))
            {
                var table = relational ? $"[{et.GetSchema() ?? "dbo"}].[{et.GetTableName()}]" : null;
                var identity = relational && await HasIdentityAsync(db, et);
                if (identity) await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {table} ON");
                foreach (var (row, rowTenant) in RowsFor(et))
                {
                    var entity = Activator.CreateInstance(et.ClrType, nonPublic: true)
                                 ?? throw new InvalidOperationException("Sin constructor: " + et.ClrType.Name);
                    var shadow = new List<(string Name, object? Value)>();
                    foreach (var p in et.GetProperties())
                    {
                        if (!TryValueFor(et, p, row, rowTenant, out var value)) continue;
                        if (p.PropertyInfo is { CanWrite: true } pi) pi.SetValue(entity, value);
                        else if (p.FieldInfo is { } fi) fi.SetValue(entity, value);
                        else shadow.Add((p.Name, value));
                    }
                    var entry = db.Add(entity);
                    foreach (var (name, value) in shadow) entry.Property(name).CurrentValue = value;
                }
                await db.SaveChangesAsync();
                if (identity) await db.Database.ExecuteSqlRawAsync($"SET IDENTITY_INSERT {table} OFF");
                db.ChangeTracker.Clear();
            }
        }
        finally
        {
            if (relational) await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> HasIdentityAsync(TeikemDbContext db, IEntityType et)
    {
        var name = $"{et.GetSchema() ?? "dbo"}.{et.GetTableName()}";
        var n = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.identity_columns WHERE object_id = OBJECT_ID({0})", name).ToListAsync();
        return n.Single() > 0;
    }

    /// <summary>Valor de una propiedad para la fila <paramref name="row"/>; false = se deja como está (calculada, versión, opcional).</summary>
    private static bool TryValueFor(IEntityType et, IProperty p, int row, int? rowTenant, out object? value)
    {
        value = null;
        if (p.Name == "TenantId") { value = rowTenant; return p.ClrType == typeof(int?) || rowTenant is not null; }
        if (p.GetComputedColumnSql() is not null || p.IsConcurrencyToken && p.ValueGenerated == ValueGenerated.OnAddOrUpdate) return false;
        if (p.IsPrimaryKey()) return TryScalar(p.ClrType, row, out value);
        if (p.IsForeignKey())
        {
            // FK: la fila 1 apunta a la fila 1 (de A) del principal; las demás, a la fila 3 (de B): una referencia CRUZADA a propósito.
            var target = row == 1 ? 1 : 3;
            if (p.GetContainingForeignKeys().Any(fk => fk.PrincipalEntityType.ClrType == typeof(Teikem.Domain.Tenancy.Tenant)))
                target = rowTenant ?? TenantA;
            return TryScalar(p.ClrType, target, out value);
        }
        if (p.IsNullable) return false;
        return TryScalar(p.ClrType, row, out value, p.GetMaxLength());
    }

    private static bool TryScalar(Type clr, int n, out object? value, int? maxLength = null)
    {
        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        value = t switch
        {
            _ when t == typeof(int) => n,
            _ when t == typeof(long) => (long)n,
            _ when t == typeof(short) => (short)n,
            _ when t == typeof(byte) => (byte)n,
            _ when t == typeof(bool) => false,
            _ when t == typeof(decimal) => 0m,
            _ when t == typeof(double) => 0d,
            _ when t == typeof(float) => 0f,
            _ when t == typeof(Guid) => Guid.NewGuid(),
            _ when t == typeof(DateTime) => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ when t == typeof(DateTimeOffset) => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ when t == typeof(DateOnly) => new DateOnly(2026, 1, 1),
            _ when t == typeof(TimeOnly) => new TimeOnly(8, 0),
            _ when t == typeof(TimeSpan) => TimeSpan.FromHours(8),
            _ when t == typeof(byte[]) => new byte[] { 1 },
            _ when t == typeof(string) => Trim("v" + n, maxLength),
            _ when t.IsEnum => Enum.GetValues(t).GetValue(0),
            _ => null,
        };
        return value is not null;
    }

    private static string Trim(string s, int? max) => max is { } m && s.Length > m ? s[..m] : s;

    // ------------------------------------------------------------------ formas de consulta

    /// <summary>Una observación: qué TenantId devolvió una forma de consulta (o un escalar).</summary>
    public sealed record Observation(string Entity, string Shape, IReadOnlyList<int?> Tenants, long? Scalar = null);

    /// <summary>
    /// Corre todas las formas sobre todas las entidades con filtro. Las excepciones de una forma se informan, no se tragan, salvo con
    /// <paramref name="sqlOnly"/> (generación de SQL sin servidor: el lector vacío hace fallar las consultas de un solo valor DESPUÉS
    /// de capturar su SQL).
    /// </summary>
    public static async Task<List<Observation>> RunAllShapesAsync(TeikemDbContext db, bool sqlOnly = false)
    {
        var all = new List<Observation>();
        foreach (var et in TenantFilteredEntities(db.Model))
        {
            var method = typeof(TenantFilterHarness).GetMethod(nameof(RunShapesAsync), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(et.ClrType);
            all.AddRange(await (Task<List<Observation>>)method.Invoke(null, new object[] { db, et, sqlOnly })!);
        }
        return all;
    }

    private static async Task<List<Observation>> RunShapesAsync<T>(TeikemDbContext db, IEntityType et, bool sqlOnly) where T : class
    {
        var name = et.ClrType.Name;
        var obs = new List<Observation>();
        var set = db.Set<T>();
        int? TenantOf(object e) => (int?)et.FindProperty("TenantId")!.PropertyInfo!.GetValue(e);
        async Task Run(Func<Task<Observation>> shape)
        {
            try { obs.Add(await shape()); }
            catch (InvalidOperationException) when (sqlOnly) { /* sin servidor: el SQL ya se capturó */ }
            finally { db.ChangeTracker.Clear(); }
        }

        await Run(async () => new(name, "lista", (await set.AsNoTracking().ToListAsync()).Select(TenantOf).ToList()));
        await Run(async () => new(name, "lista con seguimiento", (await set.ToListAsync()).Select(TenantOf).ToList()));
        await Run(async () => new(name, "proyección", await set.Select(e => EF.Property<int?>(e, "TenantId")).ToListAsync()));
        await Run(async () => new(name, "Count", Array.Empty<int?>(), await set.CountAsync()));
        await Run(async () => new(name, "LongCount con Where", Array.Empty<int?>(), await set.Where(e => EF.Property<int?>(e, "TenantId") != -5).LongCountAsync()));
        await Run(async () => new(name, "Any de B", Array.Empty<int?>(), await set.AnyAsync(e => EF.Property<int?>(e, "TenantId") == TenantB) ? 1 : 0));
        await Run(async () => new(name, "Contains de B", Array.Empty<int?>(), await set.Select(e => EF.Property<int?>(e, "TenantId")).ContainsAsync(TenantB) ? 1 : 0));
        await Run(async () => new(name, "Where TenantId = B explícito", await set.Where(e => EF.Property<int?>(e, "TenantId") == TenantB).Select(e => EF.Property<int?>(e, "TenantId")).ToListAsync()));
        await Run(async () => new(name, "ordenar desc + Take 1", await set.OrderByDescending(e => EF.Property<int?>(e, "TenantId")).Take(1).Select(e => EF.Property<int?>(e, "TenantId")).ToListAsync()));
        await Run(async () => new(name, "ordenar + Skip 1 Take 50", await set.OrderBy(e => EF.Property<int?>(e, "TenantId")).Skip(1).Take(50).Select(e => EF.Property<int?>(e, "TenantId")).ToListAsync()));
        await Run(async () => new(name, "Distinct", await set.Select(e => EF.Property<int?>(e, "TenantId")).Distinct().ToListAsync()));
        await Run(async () => new(name, "GroupBy Count", (await set.GroupBy(e => EF.Property<int?>(e, "TenantId")).Select(g => new { g.Key, N = g.Count() }).ToListAsync()).Select(x => x.Key).ToList()));
        await Run(async () => new(name, "Max", Array.Empty<int?>(), (long?)await set.MaxAsync(e => EF.Property<int?>(e, "TenantId"))));
        await Run(async () => new(name, "FirstOrDefault desc", (await set.AsNoTracking().OrderByDescending(e => EF.Property<int?>(e, "TenantId")).FirstOrDefaultAsync()) is { } f ? new[] { TenantOf(f) } : Array.Empty<int?>()));
        await Run(async () => new(name, "subconsulta Any sobre sí misma", await set.Where(e => set.Any(o => EF.Property<int?>(o, "TenantId") == TenantB)).Select(e => EF.Property<int?>(e, "TenantId")).ToListAsync()));

        // Find por la llave de una fila de B (la fila 3): debe ser «no encontrado».
        await Run(async () =>
        {
            var key = et.FindPrimaryKey()!.Properties.Select(p => { if (p.Name == "TenantId") return (object)TenantB; TryScalarKey(p.ClrType, 3, out var v); return v!; }).ToArray();
            var found = await db.FindAsync(et.ClrType, key);
            return new(name, "Find fila de B", found is null ? Array.Empty<int?>() : new[] { TenantOf(found) });
        });

        // Include de cada navegación: lo cargado de una entidad con filtro de tenant tampoco puede ser de otra compañía.
        foreach (var nav in et.GetNavigations().Where(n => !n.TargetEntityType.IsOwned()).Cast<INavigationBase>().Concat(et.GetSkipNavigations()))
        {
            var target = nav.TargetEntityType;
            var related = new List<int?>();
            await Run(async () =>
            {
                var rootRows = await set.AsNoTracking().Include(nav.Name).ToListAsync();
                if (TenantFilteredEntities(db.Model).Contains(target))
                {
                    var tp = target.FindProperty("TenantId")!.PropertyInfo!;
                    foreach (var r in rootRows)
                    {
                        var v = nav.PropertyInfo?.GetValue(r) ?? nav.FieldInfo?.GetValue(r);
                        if (v is null) continue;
                        if (v is IEnumerable many and not string) { foreach (var x in many) related.Add((int?)tp.GetValue(x)); }
                        else related.Add((int?)tp.GetValue(v));
                    }
                }
                return new(name, $"Include {nav.Name} (raíz)", rootRows.Select(TenantOf).ToList());
            });
            obs.Add(new(name, $"Include {nav.Name} (cargado: {target.ClrType.Name})", related));
            if (nav.IsCollection)
                await Run(async () => new(name, $"Include {nav.Name} AsSplitQuery (raíz)", (await set.AsNoTracking().Include(nav.Name).AsSplitQuery().ToListAsync()).Select(TenantOf).ToList()));
        }
        return obs;
    }

    private static void TryScalarKey(Type clr, int n, out object? value) => TryScalar(clr, n, out value);

    /// <summary>Count por entidad con filtro (opcionalmente con IgnoreQueryFilters).</summary>
    public static async Task<Dictionary<string, int>> CountAllAsync(TeikemDbContext db, bool ignoreFilters = false)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var et in TenantFilteredEntities(db.Model))
        {
            var method = typeof(TenantFilterHarness).GetMethod(nameof(CountAsync), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(et.ClrType);
            result[et.ClrType.Name] = await (Task<int>)method.Invoke(null, new object[] { db, ignoreFilters })!;
        }
        return result;
    }

    private static Task<int> CountAsync<T>(TeikemDbContext db, bool ignoreFilters) where T : class
        => ignoreFilters ? db.Set<T>().IgnoreQueryFilters().CountAsync() : db.Set<T>().CountAsync();

    /// <summary>Lista de (entidad, TenantId) ordenada, para comparar resultados entre dos contextos.</summary>
    public static async Task<List<string>> SnapshotAsync(TeikemDbContext db)
    {
        var all = await RunAllShapesAsync(db);
        return all.Select(o => $"{o.Entity}|{o.Shape}|{o.Scalar}|{string.Join(",", o.Tenants.Select(t => t?.ToString() ?? "N").OrderBy(s => s, StringComparer.Ordinal))}").ToList();
    }

    // ------------------------------------------------------------------ verificación

    /// <summary>
    /// Con la compañía <paramref name="tenant"/> activa: ninguna observación trae filas de otra compañía; las globales (NULL) solo en
    /// las entidades opcionales; y los conteos son exactamente los de la compañía (+ globales).
    /// </summary>
    public static void AssertOnlyTenant(IModel model, IEnumerable<Observation> observations, int tenant)
    {
        var byName = TenantFilteredEntities(model).ToDictionary(e => e.ClrType.Name);
        var failures = new List<string>();
        foreach (var o in observations)
        {
            var et = byName[o.Entity.Split(' ')[0]];
            var optional = IsOptional(et);
            foreach (var t in o.Tenants)
                if (!(t == tenant || (t is null && (optional || o.Shape.Contains("cargado", StringComparison.Ordinal)))))
                    failures.Add($"{o.Entity} / {o.Shape}: vio TenantId {t?.ToString() ?? "NULL"}");
            var expected = ExpectedVisible(et, tenant);
            switch (o.Shape)
            {
                case "lista" or "lista con seguimiento" or "proyección" when o.Tenants.Count != expected:
                    failures.Add($"{o.Entity} / {o.Shape}: {o.Tenants.Count} filas, se esperaban {expected}"); break;
                case "Count" or "LongCount con Where" when o.Scalar != expected:
                    failures.Add($"{o.Entity} / {o.Shape}: {o.Scalar}, se esperaba {expected}"); break;
                case "Any de B" or "Contains de B" when o.Scalar != (tenant == TenantB ? 1 : 0):
                    failures.Add($"{o.Entity} / {o.Shape}: {o.Scalar}"); break;
                case "Max" when o.Scalar != tenant:
                    failures.Add($"{o.Entity} / Max: {o.Scalar}, se esperaba {tenant}"); break;
                case "Find fila de B" when tenant == TenantA && o.Tenants.Count != 0:
                    failures.Add($"{o.Entity} / Find de una fila de B: la encontró"); break;
            }
        }
        Assert.True(failures.Count == 0, "Filtración entre compañías:\n" + string.Join("\n", failures.Take(60)));
    }

    // ------------------------------------------------------------------ SQL

    /// <summary>
    /// Revisa un comando SQL generado con la compañía activa: cada aparición de una tabla con filtro de tenant (<c>FROM|JOIN [T] AS [x]</c>)
    /// tiene su condición <c>[x].[TenantId] = @__ef_filter__QueryFilterTenantId_N</c>; no queda la rama del bypass (el OR con el parámetro
    /// bit); y el parámetro del filtro vale exactamente la compañía. Devuelve la lista de problemas (vacía = bien).
    /// </summary>
    public static List<string> CheckFilteredSql(IModel model, string sql, IReadOnlyDictionary<string, object?> parameters, int expectedTenant)
    {
        var problems = new List<string>();
        var tables = TenantFilteredEntities(model).Select(e => e.GetTableName()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occurrences = Regex.Matches(sql, @"(?:FROM|JOIN)\s+\[(?<t>\w+)\]\s+AS\s+\[(?<a>\w+)\]")
            .Where(m => tables.Contains(m.Groups["t"].Value))
            .GroupBy(m => m.Groups["a"].Value)
            .ToList();
        foreach (var g in occurrences)
        {
            var alias = Regex.Escape(g.Key);
            var predicates = Regex.Matches(sql, $@"\[{alias}\]\.\[TenantId\] = @__ef_filter__QueryFilterTenantId_\d+").Count;
            if (predicates < g.Count())
                problems.Add($"[{g.Key}] ({string.Join(",", g.Select(m => m.Groups["t"].Value).Distinct())}) aparece {g.Count()} vez/veces y solo tiene {predicates} condición(es) de TenantId");
        }
        // La rama del bypass de TENANT (el OR con un parámetro bit delante de la condición de TenantId).
        if (sql.Contains("TenantFilterDisabled", StringComparison.Ordinal) || Regex.IsMatch(sql, @"@__ef_filter__\w+ = CAST\(1 AS bit\) OR \[\w+\]\.\[TenantId\]"))
            problems.Add("quedó la rama del bypass (OR con el parámetro bit)");
        // Lote 42 también para el alcance de almacén/cliente: ningún «@bit = 0/1 OR …» del filtro global.
        if (Regex.IsMatch(sql, @"@__ef_filter__\w+ = CAST\([01] AS bit\) OR"))
            problems.Add("quedó una rama OR con un parámetro bit del filtro global (alcance)");
        foreach (var (k, v) in parameters.Where(p => p.Key.Contains("QueryFilterTenantId", StringComparison.Ordinal)))
            if (!Equals(v, expectedTenant)) problems.Add($"{k} = {v ?? "NULL"}, se esperaba {expectedTenant}");
        if (occurrences.Count > 0 && !parameters.Keys.Any(k => k.Contains("QueryFilterTenantId", StringComparison.Ordinal)))
            problems.Add("sin parámetro del filtro de tenant");
        return problems;
    }

    /// <summary>Tablas con filtro de tenant que aparecen en el SQL.</summary>
    public static int FilteredTableOccurrences(IModel model, string sql)
    {
        var tables = TenantFilteredEntities(model).Select(e => e.GetTableName()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Regex.Matches(sql, @"(?:FROM|JOIN)\s+\[(?<t>\w+)\]\s+AS\s+\[(?<a>\w+)\]").Count(m => tables.Contains(m.Groups["t"].Value));
    }
}

/// <summary>
/// Intercepta los comandos: guarda el texto y los parámetros; opcionalmente NO los ejecuta (ni abre la conexión) para generar el SQL
/// sin servidor (devuelve un lector vacío: las consultas de un solo valor fallan después de capturarse, y eso se ignora).
/// </summary>
internal sealed class SqlCaptureInterceptor(bool suppress) : DbCommandInterceptor, IDbConnectionInterceptor
{
    public List<(string Sql, Dictionary<string, object?> Parameters)> Commands { get; } = new();
    public bool Enabled { get; set; } = true;

    private void Capture(DbCommand c)
    {
        if (!Enabled) return;
        Commands.Add((c.CommandText, c.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName.TrimStart('@'), p => p.Value is DBNull ? null : p.Value)));
    }

    private static DbDataReader Empty() => new DataTable().CreateDataReader();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return suppress ? InterceptionResult<DbDataReader>.SuppressWithResult(Empty()) : result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(suppress ? InterceptionResult<DbDataReader>.SuppressWithResult(Empty()) : result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Capture(command);
        return suppress ? InterceptionResult<object>.SuppressWithResult(0) : result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(suppress ? InterceptionResult<object>.SuppressWithResult(0) : result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Capture(command);
        return suppress ? InterceptionResult<int>.SuppressWithResult(0) : result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(suppress ? InterceptionResult<int>.SuppressWithResult(0) : result);
    }

    InterceptionResult IDbConnectionInterceptor.ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => suppress ? InterceptionResult.Suppress() : result;

    ValueTask<InterceptionResult> IDbConnectionInterceptor.ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken)
        => ValueTask.FromResult(suppress ? InterceptionResult.Suppress() : result);
}

/// <summary>
/// El filtro global ANTERIOR al Lote 42, completo: tenant (<c>TenantFilterDisabled || e.TenantId == CurrentTenantId</c>) y alcance de datos
/// del usuario (<c>!Scoped || ids.Contains(e.Col)</c>), copiado del código de antes, para comparar resultados fila por fila con el nuevo.
/// </summary>
internal sealed class LegacyTenantFilterDbContext : TeikemDbContext
{
    private readonly ITenantContext _t;
    public LegacyTenantFilterDbContext(DbContextOptions<TeikemDbContext> options, ITenantContext tenant) : base(options, tenant) => _t = tenant;

    public bool LegacyWarehouseScoped => _t.ScopedWarehouseIds is not null && !_t.TenantFilterDisabled;
    public bool LegacyClientScoped => _t.ScopedClientIds is not null && !_t.TenantFilterDisabled;
    public int[] LegacyScopedWarehouseIdList => _t.ScopedWarehouseIds?.ToArray() ?? Array.Empty<int>();
    public int[] LegacyScopedClientIdList => _t.ScopedClientIds?.ToArray() ?? Array.Empty<int>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        foreach (var et in builder.Model.GetEntityTypes().Where(e => e.GetQueryFilter() is not null))
        {
            var clr = et.ClrType;
            var e = Expression.Parameter(clr, "e");
            var ctx = Expression.Constant(this);
            Expression? body = null;
            if (typeof(ITenantScoped).IsAssignableFrom(clr) || typeof(IOptionallyTenantScoped).IsAssignableFrom(clr))
            {
                var disabled = Expression.Property(ctx, nameof(TenantFilterDisabled));
                var current = Expression.Property(ctx, nameof(CurrentTenantId));
                var tenantProp = Expression.Property(e, "TenantId");
                body = typeof(ITenantScoped).IsAssignableFrom(clr)
                    ? Expression.OrElse(disabled, Expression.Equal(tenantProp, current))
                    : Expression.OrElse(disabled, Expression.OrElse(Expression.Equal(tenantProp, Expression.Constant(null, typeof(int?))), Expression.Equal(tenantProp, Expression.Convert(current, typeof(int?)))));
            }
            body = LegacyScope(clr, e, ctx, body, "WarehouseId", nameof(LegacyWarehouseScoped), nameof(LegacyScopedWarehouseIdList));
            body = LegacyScope(clr, e, ctx, body, "ClientId", nameof(LegacyClientScoped), nameof(LegacyScopedClientIdList));
            et.SetQueryFilter(body is null ? null : Expression.Lambda(body, e));
        }
    }

    private static Expression? LegacyScope(Type clr, ParameterExpression e, Expression ctx, Expression? existing, string column, string scopedFlag, string idList)
    {
        var prop = clr.GetProperty(column);
        if (prop is null || (prop.PropertyType != typeof(int) && prop.PropertyType != typeof(int?))) return existing;
        var notScoped = Expression.Not(Expression.Property(ctx, scopedFlag));
        var ids = Expression.Property(ctx, idList);
        var col = Expression.Property(e, prop);
        Expression inList = prop.PropertyType == typeof(int?)
            ? Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(int) }, ids, Expression.Property(col, "Value"))
            : Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(int) }, ids, col);
        if (prop.PropertyType == typeof(int?))
            inList = Expression.OrElse(Expression.Equal(col, Expression.Constant(null, typeof(int?))), inList);
        var scope = Expression.OrElse(notScoped, inList);
        return existing is null ? scope : Expression.AndAlso(existing, scope);
    }
}

/// <summary>Contexto SIN el plugin del Lote 42 (simula una versión de EF que dejara de consultarlo): debe fallar CERRADO.</summary>
internal sealed class NoSeekPluginDbContext(DbContextOptions<TeikemDbContext> options, ITenantContext tenant) : TeikemDbContext(options, tenant)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) { /* sin base: no se registra el plugin */ }
}
