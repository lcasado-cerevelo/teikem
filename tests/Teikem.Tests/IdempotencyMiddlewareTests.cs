using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Teikem.Api.Middleware;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Security;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Teikem.Tests;

/// <summary>
/// Lote 8A (P0-5): "los 5xx no se guardan para permitir reintento". Si la operación lanza una excepción que no es de dominio
/// (el 500 real) o una de dominio con la respuesta ya iniciada, el middleware libera la clave (borra el registro en vuelo) y
/// relanza; si no la liberara, el reintento de la cola del aparato recibiría 409 'La operación con esta clave todavía se está
/// procesando.' durante <see cref="IdempotencyRules.InFlightTimeout"/> en lugar de volver a ejecutarse.
/// <para>InMemory no soporta ExecuteDeleteAsync: el borrado real lo cubre el smoke contra SQL Server. Aquí se comprueba que el
/// middleware INTENTÓ liberar la clave del registro insertado: o la fila ya no está, o quedó el error 'No se pudo liberar la
/// clave de idempotencia {id}.' que escribe DeleteAsync al fallar. Sin la llamada a DeleteAsync, ninguna de las dos ocurre.</para>
/// </summary>
public class IdempotencyMiddlewareTests
{
    private const int TenantId = 1;
    private const int UserId = 5;

    private sealed class CapturingLogger : ILogger<IdempotencyMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>Respuesta cuyo HasStarted se controla desde la prueba (DefaultHttpContext nunca la da por iniciada).</summary>
    private sealed class StartableResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        [Obsolete("Use IHttpResponseBodyFeature.")] public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; set; }
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    private static (TenantContext Tenant, TeikemDbContext Db, TripTestLookups Lookups) Context()
    {
        var tenant = new TenantContext { TenantId = TenantId, UserId = UserId, IsAuthenticated = true };
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseInMemoryDatabase("idem-" + Guid.NewGuid(), b => b.EnableNullChecks(false))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var lookups = new TripTestLookups();
        lookups.Load(new[] { new LookupCode { LookupCodeId = 77, Entity = LookupDomains.MessageDirection, InternalCode = MessageDirections.Inbound, LabelJson = "{\"es\":\"Entrada\"}", IsActive = true } });
        return (tenant, new TeikemDbContext(options, tenant), lookups);
    }

    private static DefaultHttpContext Request(string key)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/receipts";
        http.Request.ContentType = "application/json";
        http.Request.Headers[IdempotencyRules.HeaderName] = key;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"lines\":[]}"));
        http.Response.Body = new MemoryStream();
        return http;
    }

    /// <summary>La clave quedó liberada o se intentó liberar el registro insertado (InMemory no soporta ExecuteDelete).</summary>
    private static void AssertKeyReleased(TeikemDbContext db, CapturingLogger logger)
    {
        db.ChangeTracker.Clear();
        var rows = db.IntegrationMessageLogs.IgnoreQueryFilters().AsNoTracking().ToList();
        if (rows.Count == 0) return;
        var row = Assert.Single(rows);
        Assert.Null(row.ResponseCode);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error
            && e.Message == $"No se pudo liberar la clave de idempotencia {row.IntegrationMessageLogId}.");
    }

    [Fact]
    public async Task An_unhandled_exception_releases_the_key_and_is_rethrown()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        var logger = new CapturingLogger();
        var middleware = new IdempotencyMiddleware(_ => throw new InvalidOperationException("boom"), logger);
        var http = Request("k1");
        var originalBody = http.Response.Body;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));

        Assert.Equal("boom", ex.Message);
        Assert.Same(originalBody, http.Response.Body);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
        AssertKeyReleased(db, logger);
    }

    [Fact]
    public async Task A_domain_exception_after_the_response_started_releases_the_key_and_is_rethrown()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        var logger = new CapturingLogger();
        var http = Request("k2");
        var feature = new StartableResponseFeature();
        http.Features.Set<IHttpResponseFeature>(feature);
        var originalBody = http.Response.Body;
        var middleware = new IdempotencyMiddleware(_ =>
        {
            feature.HasStarted = true;
            throw new ConflictException("a mitad de la respuesta");
        }, logger);

        await Assert.ThrowsAsync<ConflictException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));

        Assert.Same(originalBody, http.Response.Body);
        AssertKeyReleased(db, logger);
    }

    /// <summary>Siembra una respuesta ya guardada para (TenantId, UserId, clave) con la huella de método, ruta y cuerpo.</summary>
    private static async Task SeedStoredAsync(TeikemDbContext db, string key, string method, string path, string body, int status, string responseJson)
    {
        db.IntegrationMessageLogs.Add(new IntegrationMessageLog
        {
            TenantId = TenantId, UserId = UserId, IdempotencyKey = key, Method = method, Endpoint = path, DirectionLookupId = 77,
            RequestHash = IdempotencyRules.ComputeHash(method, path, Encoding.UTF8.GetBytes(body)),
            ResponseCode = status, ResponseJson = responseJson, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static DefaultHttpContext Request(string key, string method, string path, string body)
    {
        var http = Request(key);
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return http;
    }

    /// <summary>
    /// DELETE repetido por la cola del aparato (p. ej. DELETE /receipts/{id} dos veces con la misma clave): la respuesta
    /// guardada es un 204 sin cuerpo (ResponseJson vacío). El reintento debe dar el mismo 204 con Idempotent-Replayed y cuerpo
    /// vacío, sin volver a ejecutar la operación (que ya daría 404).
    /// </summary>
    [Fact]
    public async Task A_stored_204_without_body_is_replayed_empty_without_calling_next()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts/0b0d3e8a-2f7e-4c55-9d7c-1f1e7a0c1a11";
        await SeedStoredAsync(db, "del-1", "DELETE", path, "", 204, "");
        var called = false;
        var middleware = new IdempotencyMiddleware(_ => { called = true; return Task.CompletedTask; }, new CapturingLogger());
        var http = Request("del-1", "DELETE", path, "");

        await middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions()));

        Assert.False(called);
        Assert.Equal(204, http.Response.StatusCode);
        Assert.Equal("true", http.Response.Headers[IdempotencyRules.ReplayedHeader].ToString());
        Assert.Equal(0, http.Response.Body.Length);
    }

    /// <summary>PATCH repetido con la misma clave y el mismo cuerpo: el mismo 200 y el mismo cuerpo guardados (no un 409 por rowVersion obsoleto).</summary>
    [Fact]
    public async Task A_stored_patch_response_is_replayed_with_the_same_status_and_body()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/products/0b0d3e8a-2f7e-4c55-9d7c-1f1e7a0c1a12";
        const string body = "{\"name\":\"X\",\"rowVersion\":\"AAAAAAAAB9E=\"}";
        const string stored = "{\"name\":\"X\",\"rowVersion\":\"AAAAAAAAB9I=\"}";
        await SeedStoredAsync(db, "patch-1", "PATCH", path, body, 200, stored);
        var called = false;
        var middleware = new IdempotencyMiddleware(_ => { called = true; return Task.CompletedTask; }, new CapturingLogger());
        var http = Request("patch-1", "PATCH", path, body);

        await middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions()));

        Assert.False(called);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.Equal("true", http.Response.Headers[IdempotencyRules.ReplayedHeader].ToString());
        http.Response.Body.Position = 0;
        Assert.Equal(stored, new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEnd());
    }
}
