using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Teikem.Api.Middleware;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 41 — interbloqueos de SQL Server al escribir rutas con varios despachadores a la vez.
/// <list type="bullet">
/// <item>Red de seguridad: un interbloqueo (1205) que sobrevive a los reintentos de EF ya no es 500 'Error interno.' sino 409
/// 'concurrent_edit' con un mensaje que invita a reintentar; el resto de los errores no cambia.</item>
/// <item>Orden de bloqueo: toda escritura de rutas toma la fila Tenant ANTES que el Trip (LockTripAsync) y el alta antes del
/// número; las lecturas posteriores a escribir buscan solo sus filas (WITH (FORCESEEK)).</item>
/// </list>
/// </summary>
public class DeadlockRulesTests
{
    // ------------------------------------------------------------ SqlException real (sin constructor público)

    /// <summary>Arma una SqlException real con el número de error indicado (reflexión sobre los miembros internos de SqlClient).</summary>
    private static SqlException SqlError(int number)
    {
        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(int))
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var args = errorCtor.GetParameters().Select((p, i) => i == 0 ? number : Default(p.ParameterType, p.Name)).ToArray();
        var error = (SqlError)errorCtor.Invoke(args);

        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(collection, new object[] { error });

        var create = typeof(SqlException).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "CreateException" && m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(SqlErrorCollection)
                        && m.GetParameters()[1].ParameterType == typeof(string))
            .OrderBy(m => m.GetParameters().Length)
            .First();
        var createArgs = create.GetParameters().Select((p, i) => i == 0 ? collection : i == 1 ? "16.0" : Default(p.ParameterType, p.Name)).ToArray();
        return (SqlException)create.Invoke(null, createArgs)!;
    }

    private static object? Default(Type t, string? name)
    {
        if (t == typeof(string)) return name switch { "errorMessage" => "Transaction was deadlocked.", _ => "teikem" };
        if (t == typeof(byte)) return (byte)13;
        if (t == typeof(int)) return 0;
        if (t == typeof(uint)) return 0u;
        if (t == typeof(Guid)) return Guid.Empty;
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }

    private static RetryLimitExceededException RetryLimit(Exception inner)
        => new("The maximum number of retries (3) was exceeded while executing database operations with 'SqlServerRetryingExecutionStrategy'.", inner);

    // ------------------------------------------------------------ detección

    [Fact]
    public void A_real_sql_exception_1205_is_a_deadlock_and_other_numbers_are_not()
    {
        Assert.Equal(1205, SqlError(1205).Number);
        Assert.True(DeadlockRules.IsDeadlock(SqlError(1205)));
        Assert.False(DeadlockRules.IsDeadlock(SqlError(2627)));   // índice único: lo traduce SaveGuardedAsync, no esta regla
        Assert.False(DeadlockRules.IsDeadlock(SqlError(1222)));   // tiempo de espera de bloqueo: no es víctima de interbloqueo
        Assert.False(DeadlockRules.IsDeadlock(null));
        Assert.False(DeadlockRules.IsDeadlock(new InvalidOperationException("otra cosa")));
    }

    [Fact]
    public void The_deadlock_is_found_inside_the_retry_and_update_wrappers()
    {
        // Lo que de verdad llegaba al middleware (log de la regresión): RetryLimitExceeded → SqlException 1205.
        Assert.True(DeadlockRules.IsDeadlock(RetryLimit(SqlError(1205))));
        // SaveChanges dentro de una transacción anidada (sin reintentos): DbUpdateException → SqlException 1205.
        Assert.True(DeadlockRules.IsDeadlock(new DbUpdateException("save", SqlError(1205))));
        Assert.True(DeadlockRules.IsDeadlock(RetryLimit(new DbUpdateException("save", SqlError(1205)))));
        Assert.True(DeadlockRules.IsDeadlock(new AggregateException(new InvalidOperationException("a"), SqlError(1205))));
        // Reintentos agotados por OTRA causa transitoria: sigue siendo 500.
        Assert.False(DeadlockRules.IsDeadlock(RetryLimit(SqlError(-2))));
    }

    [Fact]
    public void Injected_error_numbers_are_honoured_and_the_walk_is_bounded()
    {
        static IEnumerable<int> Numbers(Exception e) => e is FakeSqlError f ? new[] { f.Number } : Array.Empty<int>();
        Assert.True(DeadlockRules.IsDeadlock(new Exception("x", new Exception("y", new FakeSqlError(1205))), Numbers));
        Assert.False(DeadlockRules.IsDeadlock(new Exception("x", new FakeSqlError(547)), Numbers));

        Exception deep = new FakeSqlError(1205);
        for (var i = 0; i < 40; i++) deep = new Exception("capa " + i, deep);
        Assert.False(DeadlockRules.IsDeadlock(deep, Numbers));    // más de 16 capas: no se sigue (nunca hay tantas)
    }

    private sealed class FakeSqlError(int number) : Exception("sql") { public int Number { get; } = number; }

    [Theory]
    [InlineData("/api/v1/trips/0b7c4b52-60f5-4a52-9f40-0b0b1f6b3d11/stops/12/location", DeadlockRules.RouteMessage)]
    [InlineData("/api/v1/trips/plan-day", DeadlockRules.RouteMessage)]
    [InlineData("/API/V1/TRIPS", DeadlockRules.RouteMessage)]
    [InlineData("/api/v1/orders/0b7c4b52-60f5-4a52-9f40-0b0b1f6b3d11/cancel", DeadlockRules.GenericMessage)]
    [InlineData(null, DeadlockRules.GenericMessage)]
    public void The_message_depends_on_the_request_path(string? path, string expected)
        => Assert.Equal(expected, DeadlockRules.MessageFor(path));

    [Fact]
    public void Messages_are_the_exact_texts_of_the_manual()
    {
        Assert.Equal("Otra persona está modificando esta ruta al mismo tiempo; vuelva a intentarlo.", DeadlockRules.RouteMessage);
        Assert.Equal("Otra persona está modificando estos datos al mismo tiempo; vuelva a intentarlo.", DeadlockRules.GenericMessage);
        Assert.Equal("concurrent_edit", DeadlockRules.Code);
    }

    // ------------------------------------------------------------ middleware

    private static async Task<(int Status, JsonElement Body)> RunMiddlewareAsync(Exception thrown, string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        var tenant = new TenantContext { CorrelationId = Guid.Parse("60cb7b2e-8336-4057-a19b-1127da12bf8b") };
        await new ExceptionHandlingMiddleware(_ => throw thrown, NullLogger<ExceptionHandlingMiddleware>.Instance).InvokeAsync(http, tenant);
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (http.Response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task A_deadlock_after_the_retries_is_a_409_concurrent_edit_not_a_500()
    {
        var (status, body) = await RunMiddlewareAsync(RetryLimit(SqlError(1205)), "/api/v1/trips/0b7c4b52-60f5-4a52-9f40-0b0b1f6b3d11/stops/7/location");

        Assert.Equal(409, status);
        Assert.Equal("concurrent_edit", body.GetProperty("code").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("https://teikem.app/errors/concurrent_edit", body.GetProperty("type").GetString());
        Assert.Equal(DeadlockRules.RouteMessage, body.GetProperty("title").GetString());
        Assert.Equal("60cb7b2e-8336-4057-a19b-1127da12bf8b", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A_deadlock_outside_the_routes_uses_the_generic_message()
    {
        var (status, body) = await RunMiddlewareAsync(new DbUpdateException("save", SqlError(1205)), "/api/v1/orders");
        Assert.Equal(409, status);
        Assert.Equal(DeadlockRules.GenericMessage, body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Other_errors_keep_their_behaviour()
    {
        // Reintentos agotados por otra causa → sigue siendo 500 'Error interno.'.
        var (status, body) = await RunMiddlewareAsync(RetryLimit(SqlError(-2)), "/api/v1/trips");
        Assert.Equal(500, status);
        Assert.Equal("internal", body.GetProperty("code").GetString());
        Assert.Equal("Error interno.", body.GetProperty("title").GetString());

        // Una excepción de dominio sigue saliendo con su propio código (409 conflict del RowVersion, por ejemplo).
        (status, body) = await RunMiddlewareAsync(new ConflictException(DbExtensions.ConcurrencyMessage), "/api/v1/trips");
        Assert.Equal(409, status);
        Assert.Equal("conflict", body.GetProperty("code").GetString());
        Assert.Equal(DbExtensions.ConcurrencyMessage, body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_cancelled_request_is_not_turned_into_a_409()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new OperationCanceledException(), NullLogger<ExceptionHandlingMiddleware>.Instance);
        await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(http, new TenantContext()));
    }

    // ------------------------------------------------------------ orden de bloqueo (guardas sobre el código fuente)

    private static string Source(params string[] relative) => File.ReadAllText(Path.Combine(new[] { TripCatalogTests.RepoRoot(), "src" }.Concat(relative).ToArray()));

    /// <summary>Cuerpo del método (desde su firma hasta el siguiente miembro público o privado de la clase).</summary>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "no se encontró " + signature);
        var next = Regex.Match(source[(start + signature.Length)..], @"\n    (public|private|internal) ");
        return next.Success ? source.Substring(start, signature.Length + next.Index) : source[start..];
    }

    [Fact]
    public void Every_route_write_takes_the_tenant_row_before_the_trip()
    {
        var queries = Source("Teikem.Infrastructure", "Trips", "TripQueries.cs");
        var lockTrip = Body(queries, "public static async Task<Trip> LockTripAsync(this TeikemDbContext db, int tripId, CancellationToken ct)");
        var tenantAt = lockTrip.IndexOf("await db.LockTenantPlanningAsync(ct);", StringComparison.Ordinal);
        var tripAt = lockTrip.IndexOf("FROM dbo.Trip WITH (UPDLOCK, ROWLOCK)", StringComparison.Ordinal);
        Assert.True(tenantAt >= 0 && tripAt > tenantAt, "LockTripAsync debe tomar la fila Tenant (paso 1) antes que el Trip (paso 2).");

        // Las otras sobrecargas pasan por LockTripAsync(int): heredan el paso 1.
        Assert.Contains("return await db.LockTripAsync(tripId, ct);", Body(queries, "public static async Task<Trip> LockTripAsync(this TeikemDbContext db, Guid publicId, CancellationToken ct)"));
        Assert.Contains("db.LockTripAsync(id, ct)", Body(queries, "public static async Task<IReadOnlyList<Trip>> LockTripsAsync("));

        // El alta de rutas toma la fila Tenant antes del número y del INSERT.
        var create = Body(Source("Teikem.Infrastructure", "Services", "TripService.cs"), "public async Task<Trip> CreateTrackedAsync(");
        var createTenantAt = create.IndexOf("await db.LockTenantPlanningAsync(ct);", StringComparison.Ordinal);
        Assert.True(createTenantAt >= 0 && createTenantAt < create.IndexOf("numbers.NextAsync", StringComparison.Ordinal)
                    && createTenantAt < create.IndexOf("db.Trips.Add(trip)", StringComparison.Ordinal),
            "CreateTrackedAsync debe tomar la fila Tenant antes del número y del INSERT.");
    }

    [Fact]
    public void Reads_after_writing_a_route_only_seek_their_own_rows()
    {
        var writer = Source("Teikem.Infrastructure", "Trips", "RouteWriter.cs");
        // Paradas de la versión y ventanas por TripQueries (FORCESEEK), no por LINQ que el optimizador resuelve con un recorrido.
        Assert.Contains("await db.LoadRouteStopsAsync(routeId, ct);", writer);
        Assert.Contains("await db.StopTimingsAsync(orderStopIds, ct);", writer);
        Assert.DoesNotContain("db.RouteStops.AsTracking().Where(s => s.RouteId == routeId).LoadAsync", writer);

        var queries = Source("Teikem.Infrastructure", "Trips", "TripQueries.cs");
        Assert.Contains("FROM dbo.RouteStop rs WITH (FORCESEEK) JOIN dbo.Route r WITH (FORCESEEK) ON r.RouteId = rs.RouteId JOIN dbo.Trip t WITH (FORCESEEK)", queries);
        Assert.Equal(4, Regex.Matches(queries, Regex.Escape("FROM dbo.OrderStop os WITH (FORCESEEK) JOIN dbo.TransportOrder o WITH (FORCESEEK)")).Count
                        + Regex.Matches(queries, Regex.Escape("FROM dbo.RouteStop rs WITH (FORCESEEK)")).Count);
    }

    // ---- Petición cancelada por el cliente (2026-10-12): no es un error del servidor ----

    [Fact]
    public async Task A_failure_after_the_client_aborted_the_request_is_not_an_unhandled_error_and_does_not_answer_500()
    {
        using var cts = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        var log = new CapturingLogger();
        // SQL Server aborta la consulta cuando el cliente se va: llega como una excepción cualquiera (SqlException «severe error»)
        var middleware = new ExceptionHandlingMiddleware(_ => { cts.Cancel(); throw new InvalidOperationException("A severe error occurred on the current command. Operation cancelled by user."); }, log);

        await middleware.InvokeAsync(http, new TenantContext());

        Assert.Equal(499, http.Response.StatusCode);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task The_same_failure_with_the_client_still_connected_is_still_a_500_and_an_error_in_the_log()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var log = new CapturingLogger();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("fallo real"), log);

        await middleware.InvokeAsync(http, new TenantContext());

        Assert.Equal(500, http.Response.StatusCode);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_domain_exception_keeps_its_status_even_if_the_client_aborted()
    {
        using var cts = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        http.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => { cts.Cancel(); throw new Teikem.Infrastructure.Exceptions.NotFoundException("Cliente", "x"); }, NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(http, new TenantContext());

        Assert.Equal(404, http.Response.StatusCode);
    }

    private sealed class CapturingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
