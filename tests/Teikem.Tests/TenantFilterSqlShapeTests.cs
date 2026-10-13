using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Orders;
using Teikem.Domain.Trips;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;
using static Teikem.Tests.TenantFilterHarness;

namespace Teikem.Tests;

/// <summary>
/// Lote 42 — FORMA del SQL que genera EF para el filtro de tenant, SIN servidor (proveedor SQL Server con la conexión y la ejecución
/// suprimidas por un interceptor que solo captura el comando). Corre en todo `dotnet test`. Recorre el SQL de una muestra amplia
/// (todas las formas del arnés sobre TODAS las entidades con filtro, más consultas como las de la aplicación) y exige que cada tabla
/// con filtro lleve <c>[x].[TenantId] = @__ef_filter__QueryFilterTenantId_N</c> con el valor de la compañía, nunca la rama del bypass.
/// Si una actualización de EF cambia la forma del SQL, estas pruebas fallan (ver docs/lote42-decisiones.md, «Si EF cambia»).
/// </summary>
public sealed class TenantFilterSqlShapeTests
{
    private static (TContext Db, TenantContext Tenant, SqlCaptureInterceptor Capture) Create<TContext>(int? tenantId = TenantA) where TContext : TeikemDbContext
    {
        var tenant = new TenantContext { TenantId = tenantId, UserId = 1, IsAuthenticated = true };
        var capture = new SqlCaptureInterceptor(suppress: true);
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseSqlServer("Server=sql-shape.invalid;Database=TeikemShape;User Id=x;Password=x;TrustServerCertificate=True")
            .AddInterceptors(capture)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        return ((TContext)Activator.CreateInstance(typeof(TContext), options, tenant)!, tenant, capture);
    }

    private static string N(string sql) => sql.Replace("\r\n", "\n");

    [Fact]
    public void El_plugin_esta_registrado_en_todo_contexto()
    {
        var (db, _, _) = Create<TeikemDbContext>();
        using var _d = db;
        Assert.Contains(db.GetService<IEnumerable<IEvaluatableExpressionFilterPlugin>>(), p => p is TenantFilterEvaluatablePlugin);
        Assert.NotNull(db.GetService<IDbContextOptions>().FindExtension<TenantFilterOptionsExtension>());
    }

    [Fact]
    public async Task Todas_las_formas_de_todas_las_entidades_llevan_la_condicion_de_TenantId_sin_la_rama_del_bypass()
    {
        var (db, tenant, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        foreach (var t in new[] { TenantA, TenantB })
        {
            capture.Commands.Clear();
            using (t == TenantB ? tenant.As(TenantB) : null)
                await RunAllShapesAsync(db, sqlOnly: true);
            var checkedCommands = capture.Commands.Where(c => FilteredTableOccurrences(db.Model, c.Sql) > 0).ToList();
            Assert.True(checkedCommands.Count > 1000, $"Solo {checkedCommands.Count} comandos");
            Assert.Equal(capture.Commands.Count, checkedCommands.Count);   // cada comando toca al menos una tabla con filtro
            var problems = checkedCommands.SelectMany(c => CheckFilteredSql(db.Model, c.Sql, c.Parameters, t).Select(p => p + "\n" + c.Sql)).ToList();
            Assert.True(problems.Count == 0, $"{problems.Count} problemas; primero:\n{problems.FirstOrDefault()}");
        }
    }

    [Fact]
    public async Task Consultas_como_las_de_la_aplicacion_llevan_la_condicion_en_cada_tabla_con_filtro()
    {
        var (db, tenant, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        var since = DateTime.UtcNow.AddDays(-30);
        var ids = new[] { 1, 2, 3 };
        async Task Q(Func<Task> query) { try { await query(); } catch (InvalidOperationException) { } }

        // lista de órdenes con filtros, conteo, búsqueda y página profunda
        await Q(() => db.TransportOrders.AsNoTracking().Where(o => o.IsActive && o.CreatedAtUtc >= since && ids.Contains(o.StatusCodeId))
            .OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.TransportOrderId).Skip(5000).Take(200)
            .Select(o => new { o.PublicId, o.OrderNumber, Client = o.Client!.Name, Stops = o.Stops.Count, Pieces = o.CargoLines.Count() }).ToListAsync());
        await Q(() => db.TransportOrders.Where(o => o.OrderNumber.Contains("123") || o.Stops.Any(s => s.SnapName!.Contains("x"))).CountAsync());
        await Q(() => db.TransportOrders.Include(o => o.Stops).Include(o => o.CargoLines).Include(o => o.Client).ThenInclude(c => c!.Status).AsSplitQuery().Where(o => o.PublicId == Guid.Empty).ToListAsync());
        // saldo de crédito
        await Q(() => db.TransportOrders.Where(o => o.ClientId == 5 && o.IsActive && o.QuotedAmount != null).SumAsync(o => o.QuotedAmount));
        // rutas: lista del día con sus órdenes, «sin asignar», otras rutas del chofer
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Q(() => db.Trips.AsNoTracking().Where(t => t.PlanDate == today).Select(t => new { t.Code, Orders = t.Orders.Count(o => o.IsCurrent), Driver = t.Driver!.FullName, Zone = t.DispatchZone!.Name }).ToListAsync());
        await Q(() => db.TransportOrders.Where(o => !db.TripOrders.Any(x => x.TransportOrderId == o.TransportOrderId && x.IsCurrent)).Take(500).ToListAsync());
        await Q(() => (from t in db.Trips join x in db.TripOrders on t.TripId equals x.TripId join o in db.TransportOrders on x.TransportOrderId equals o.TransportOrderId
                       where t.PlanDate == today select new { t.Code, o.OrderNumber }).ToListAsync());
        // inventario: productos con saldos, Kárdex, recibos
        await Q(() => db.Products.Where(p => db.StockBalances.Any(b => b.ProductId == p.ProductId && b.QtyOnHand > 0)).OrderBy(p => p.Sku).Take(100).ToListAsync());
        await Q(() => db.InventoryTransactions.Where(t => t.ProductId == 1).OrderByDescending(t => t.InventoryTransactionId).Take(50).ToListAsync());
        await Q(() => db.ReceiptHeaders.Include(r => r.Lines).Where(r => r.IsActive).Take(20).ToListAsync());
        await Q(() => db.StockBalances.GroupBy(b => b.WarehouseId).Select(g => new { g.Key, Qty = g.Sum(b => b.QtyOnHand) }).ToListAsync());
        // catálogos opcionales (TenantId NULL = global)
        await Q(() => db.LookupCodes.Where(l => l.Entity == "ServiceType").ToListAsync());
        // con alcance de almacén y cliente a la vez: la condición de TenantId sigue ahí
        tenant.ScopedWarehouseIds = new[] { 1 };
        tenant.ScopedClientIds = new[] { 1 };
        await Q(() => db.Asns.Where(a => a.IsActive).ToListAsync());
        await Q(() => db.Products.CountAsync());
        tenant.ScopedWarehouseIds = null;
        tenant.ScopedClientIds = null;
        // FromSql (con hint de bloqueo): EF compone el filtro afuera
        await Q(() => db.Trips.FromSqlInterpolated($"SELECT * FROM dbo.Trip WITH (UPDLOCK, ROWLOCK) WHERE TripId = {1}").ToListAsync());
        // ExecuteUpdate también pasa por el filtro
        await Q(() => db.TransportOrders.Where(o => o.TransportOrderId == 1).ExecuteUpdateAsync(s => s.SetProperty(o => o.Notes, "x")));

        Assert.True(capture.Commands.Count >= 15, $"{capture.Commands.Count} comandos");
        foreach (var c in capture.Commands)
        {
            var problems = CheckFilteredSql(db.Model, c.Sql, c.Parameters, TenantA);
            Assert.True(problems.Count == 0, string.Join("; ", problems) + "\n" + c.Sql);
            Assert.True(FilteredTableOccurrences(db.Model, c.Sql) > 0 || c.Sql.Contains(") AS [", StringComparison.Ordinal), c.Sql);
            Assert.Matches(@"\[\w+\]\.\[TenantId\] = @__ef_filter__QueryFilterTenantId_\d+", c.Sql);
        }
    }

    [Fact]
    public async Task Forma_exacta_del_SQL_con_compania_bypass_y_opcional()
    {
        var (db, tenant, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        async Task<string> Sql(Func<Task> q) { capture.Commands.Clear(); try { await q(); } catch (InvalidOperationException) { } return N(capture.Commands.Single().Sql); }

        Assert.Equal("SELECT COUNT(*)\nFROM [Trip] AS [t]\nWHERE [t].[TenantId] = @__ef_filter__QueryFilterTenantId_0",
            await Sql(() => db.Trips.CountAsync()));
        Assert.Equal(TenantA, capture.Commands.Single().Parameters["__ef_filter__QueryFilterTenantId_0"]);
        Assert.Equal("SELECT COUNT(*)\nFROM [LookupCode] AS [l]\nWHERE [l].[TenantId] IS NULL OR [l].[TenantId] = @__ef_filter__QueryFilterTenantId_0",
            await Sql(() => db.LookupCodes.CountAsync()));
        using (tenant.BypassTenantFilter())
        {
            Assert.Equal("SELECT COUNT(*)\nFROM [Trip] AS [t]", await Sql(() => db.Trips.CountAsync()));
            Assert.Empty(capture.Commands.Single().Parameters);
            Assert.Equal("SELECT COUNT(*)\nFROM [LookupCode] AS [l]", await Sql(() => db.LookupCodes.CountAsync()));
        }
        // misma consulta otra vez con la compañía: la caché de EF separa por nulidad del parámetro
        Assert.Equal("SELECT COUNT(*)\nFROM [Trip] AS [t]\nWHERE [t].[TenantId] = @__ef_filter__QueryFilterTenantId_0", await Sql(() => db.Trips.CountAsync()));
        using (tenant.As(TenantB))
        {
            await Sql(() => db.Trips.CountAsync());
            Assert.Equal(TenantB, capture.Commands.Single().Parameters["__ef_filter__QueryFilterTenantId_0"]);
        }
        tenant.TenantId = null;
        await Sql(() => db.Trips.CountAsync());
        Assert.Equal(-1, capture.Commands.Single().Parameters["__ef_filter__QueryFilterTenantId_0"]);   // sin compañía: no ve nada
    }

    [Fact]
    public async Task Alternar_compania_y_bypass_en_el_mismo_contexto_nunca_reusa_el_SQL_equivocado()
    {
        var (db, tenant, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        for (var i = 0; i < 6; i++)
        {
            capture.Commands.Clear();
            var bypass = i % 2 == 1;
            using (bypass ? tenant.BypassTenantFilter() : null)
                await db.TransportOrders.Where(o => o.IsActive).Take(5).ToListAsync();
            var sql = capture.Commands.Single().Sql;
            Assert.Equal(!bypass, sql.Contains("[t].[TenantId] = @__ef_filter__QueryFilterTenantId_", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_no_lleva_condicion_de_tenant()
    {
        var (db, _, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        await db.TransportOrders.IgnoreQueryFilters().Where(o => o.IsActive).ToListAsync();
        var (sql, parameters) = capture.Commands.Single();
        Assert.DoesNotContain("QueryFilterTenantId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("[t].[TenantId] =", sql, StringComparison.Ordinal);
        Assert.Empty(parameters);
    }

    [Fact]
    public async Task Sin_el_plugin_falla_cerrado_el_SQL_queda_como_antes_con_el_filtro_completo()
    {
        // Simula una versión de EF que dejara de consultar el plugin: la comparación vuelve a ir como parámetro bit (el OR de antes),
        // la condición de TenantId SIGUE ahí y su parámetro vale la compañía; con el bypass apagado el bit vale false.
        var (db, _, capture) = Create<NoSeekPluginDbContext>();
        await using var _d = db;
        try { await db.Trips.CountAsync(); } catch (InvalidOperationException) { }
        var (sql, p) = capture.Commands.Single();
        Assert.Equal("SELECT COUNT(*)\nFROM [Trip] AS [t]\nWHERE @__ef_filter__p_0 = CAST(1 AS bit) OR [t].[TenantId] = @__ef_filter__QueryFilterTenantId_1", N(sql));
        Assert.Equal(false, p["__ef_filter__p_0"]);
        Assert.Equal(TenantA, p["__ef_filter__QueryFilterTenantId_1"]);
    }

    [Fact]
    public void El_plugin_solo_reconoce_la_comparacion_exacta()
    {
        var plugin = new TenantFilterEvaluatablePlugin();
        var (db, _, _) = Create<TeikemDbContext>();
        using var _d = db;
        var ctx = Expression.Constant(db);
        var prop = Expression.Property(ctx, nameof(TeikemDbContext.QueryFilterTenantId));
        var nul = Expression.Constant(null, typeof(int?));
        Assert.False(plugin.IsEvaluatableExpression(Expression.Equal(prop, nul)));
        Assert.False(plugin.IsEvaluatableExpression(Expression.Equal(nul, prop)));
        // todo lo demás lo decide EF como siempre
        Assert.True(plugin.IsEvaluatableExpression(Expression.NotEqual(prop, nul)));
        Assert.True(plugin.IsEvaluatableExpression(Expression.Equal(prop, Expression.Constant(5, typeof(int?)))));
        Assert.True(plugin.IsEvaluatableExpression(prop));
        Assert.True(plugin.IsEvaluatableExpression(Expression.Equal(Expression.Convert(Expression.Property(ctx, nameof(TeikemDbContext.CurrentTenantId)), typeof(int?)), nul)));
        Assert.True(plugin.IsEvaluatableExpression(Expression.Property(ctx, nameof(TeikemDbContext.TenantFilterDisabled))));
        // alcance de datos del usuario (almacén / cliente): misma forma exacta
        var arrNull = Expression.Constant(null, typeof(int[]));
        Assert.False(plugin.IsEvaluatableExpression(Expression.Equal(Expression.Property(ctx, nameof(TeikemDbContext.QueryFilterClientIds)), arrNull)));
        Assert.False(plugin.IsEvaluatableExpression(Expression.Equal(Expression.Property(ctx, nameof(TeikemDbContext.QueryFilterWarehouseIds)), arrNull)));
        Assert.True(plugin.IsEvaluatableExpression(Expression.NotEqual(Expression.Property(ctx, nameof(TeikemDbContext.QueryFilterClientIds)), arrNull)));
    }

    [Fact]
    public async Task Alcance_de_almacen_y_cliente_sin_OR_y_sin_termino_cuando_no_hay_alcance()
    {
        var (db, tenant, capture) = Create<TeikemDbContext>();
        await using var _d = db;
        async Task<string> Sql(Func<Task> q) { capture.Commands.Clear(); try { await q(); } catch (InvalidOperationException) { } return N(capture.Commands.Single().Sql); }
        // sin alcance: solo la compañía
        Assert.Equal("SELECT COUNT(*)\nFROM [Asn] AS [a]\nWHERE [a].[TenantId] = @__ef_filter__QueryFilterTenantId_0", await Sql(() => db.Asns.CountAsync()));
        // con alcance de almacén y de cliente (ClientId opcional en Asn): IN sobre la lista, sin OR con bit
        tenant.ScopedWarehouseIds = new[] { 10 };
        tenant.ScopedClientIds = new[] { 1, 2 };
        var sql = await Sql(() => db.Asns.CountAsync());
        Assert.Equal("""
            SELECT COUNT(*)
            FROM [Asn] AS [a]
            WHERE [a].[TenantId] = @__ef_filter__QueryFilterTenantId_0 AND [a].[WarehouseId] IN (
                SELECT [e].[value]
                FROM OPENJSON(@__ef_filter__QueryFilterWarehouseIds_1) WITH ([value] int '$') AS [e]
            ) AND ([a].[ClientId] IS NULL OR [a].[ClientId] IN (
                SELECT [e0].[value]
                FROM OPENJSON(@__ef_filter__QueryFilterClientIds_2) WITH ([value] int '$') AS [e0]
            ))
            """.Replace("\r\n", "\n"), sql);
        Assert.Equal("[10]", capture.Commands.Single().Parameters["__ef_filter__QueryFilterWarehouseIds_1"]);
        Assert.Equal("[1,2]", capture.Commands.Single().Parameters["__ef_filter__QueryFilterClientIds_2"]);
        // con el bypass, ni tenant ni alcance
        using (tenant.BypassTenantFilter())
            Assert.Equal("SELECT COUNT(*)\nFROM [Asn] AS [a]", await Sql(() => db.Asns.CountAsync()));
        // alcance vacío: no ve ninguno de ese tipo (IN sobre lista vacía), igual que antes
        tenant.ScopedWarehouseIds = Array.Empty<int>();
        await Sql(() => db.Asns.CountAsync());
        Assert.Equal("[]", capture.Commands.Single().Parameters["__ef_filter__QueryFilterWarehouseIds_1"]);
    }

    [Fact]
    public void El_filtro_de_cada_entidad_usa_QueryFilterTenantId_y_ya_no_TenantFilterDisabled()
    {
        var (db, _, _) = Create<TeikemDbContext>();
        using var _d = db;
        foreach (var e in TenantFilteredEntities(db.Model))
        {
            var text = e.GetQueryFilter()!.ToString();
            Assert.Contains("QueryFilterTenantId == null", text);
            Assert.DoesNotContain("TenantFilterDisabled", text);
            Assert.DoesNotContain("Scoped", text);   // ni WarehouseScoped/ClientScoped (el alcance usa QueryFilterWarehouseIds/ClientIds == null)
        }
    }
}
