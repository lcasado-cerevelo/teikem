using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
using Teikem.Infrastructure.Services;
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
    private static async Task SeedStoredAsync(TeikemDbContext db, string key, string method, string path, string body, int status, string responseJson,
        string? replayChecksJson = null)
    {
        db.IntegrationMessageLogs.Add(new IntegrationMessageLog
        {
            TenantId = TenantId, UserId = UserId, IdempotencyKey = key, Method = method, Endpoint = path, DirectionLookupId = 77,
            RequestHash = IdempotencyRules.ComputeHash(method, path, Encoding.UTF8.GetBytes(body)),
            ResponseCode = status, ResponseJson = responseJson, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            ReplayChecksJson = replayChecksJson,
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

    /// <summary>La clave lógica es (TenantId, UserId, clave): el mismo usuario con la misma clave y el mismo cuerpo en otra compañía NO recibe la respuesta guardada en la primera.</summary>
    [Fact]
    public async Task The_same_user_and_key_in_another_tenant_is_not_replayed()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts";
        const string body = "{\"lines\":[]}";
        await SeedStoredAsync(db, "k-x", "POST", path, body, 200, "{\"number\":\"REC-A\"}");
        tenant.TenantId = 2; // misma persona, otra compañía
        var called = false;
        // 401 no se guarda: evita los reintentos de ExecuteUpdate (InMemory no lo soporta).
        var middleware = new IdempotencyMiddleware(h => { called = true; h.Response.StatusCode = 401; return Task.CompletedTask; }, new CapturingLogger());
        var http = Request("k-x", "POST", path, body);

        await middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions()));

        Assert.True(called);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
        http.Response.Body.Position = 0;
        Assert.DoesNotContain("REC-A", new StreamReader(http.Response.Body).ReadToEnd());
    }

    private sealed class CapturingSecurityWriter : ISecurityEventWriter
    {
        public List<string> Events { get; } = new();
        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add(eventType);
            return Task.CompletedTask;
        }
    }

    /// <summary>Servicios de la repetición con módulos y permisos efectivos precargados en la caché (sin BD de catálogos).</summary>
    private static (IServiceProvider Services, CapturingSecurityWriter Security) Services(TenantContext tenant, TeikemDbContext db, TripTestLookups lookups,
        string[] modules, string[] permissions)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"modules:{TenantId}", new HashSet<string>(modules, StringComparer.OrdinalIgnoreCase));
        cache.Set($"perms:{UserId}:{TenantId}", new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase));
        var security = new CapturingSecurityWriter();
        var services = new ServiceCollection()
            .AddSingleton<IMemoryCache>(cache)
            .AddSingleton(tenant).AddSingleton<ITenantContext>(tenant)
            .AddSingleton(db).AddSingleton<ILookupCache>(lookups).AddSingleton<ISecurityEventWriter>(security)
            .AddScoped<IdempotencyCheckRecorder>().AddScoped<ModuleService>().AddScoped<PermissionService>()
            .BuildServiceProvider();
        return (services, security);
    }

    private static string Checks(params IdempotencyCheck[] checks) => IdempotencyRules.SerializeChecks(checks)!;

    /// <summary>
    /// Recibo contra OC repetido después de apagar PURCHASING: la operación original comprobó el módulo en el servicio (true);
    /// ahora no se cumple → 409 'La operación con esta clave ya no puede repetirse con los permisos actuales.', sin repetir el
    /// cuerpo guardado (las comprobaciones de servicio que cambian dan 409; solo los atributos del endpoint dan su 403).
    /// </summary>
    [Fact]
    public async Task A_service_module_check_that_no_longer_holds_blocks_the_replay_with_conflict()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts";
        const string body = "{\"purchaseOrderPublicId\":\"x\",\"confirm\":true}";
        await SeedStoredAsync(db, "po-1", "POST", path, body, 200, "{\"number\":\"REC-1\"}",
            Checks(new IdempotencyCheck(IdempotencyRules.ModuleCheck, ModuleKeys.Purchasing, true),
                new IdempotencyCheck(IdempotencyRules.PermissionCheck, PermissionCatalog.PurchasingReceive, true)));
        var (services, _) = Services(tenant, db, lookups, modules: Array.Empty<string>(), permissions: new[] { PermissionCatalog.PurchasingReceive });
        var called = false;
        var middleware = new IdempotencyMiddleware(_ => { called = true; return Task.CompletedTask; }, new CapturingLogger());
        var http = Request("po-1", "POST", path, body);
        http.RequestServices = services;

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));

        Assert.Equal(IdempotencyRules.RecheckChangedMessage, ex.Message);
        Assert.False(called);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
    }

    /// <summary>Permiso comprobado en el servicio que ya no se tiene → 409 RecheckChangedMessage (no 403 ni PERMISSION_DENIED), sin repetir.</summary>
    [Fact]
    public async Task A_service_permission_check_that_no_longer_holds_blocks_the_replay_with_conflict()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts";
        const string body = "{\"purchaseOrderPublicId\":\"x\"}";
        await SeedStoredAsync(db, "po-2", "POST", path, body, 200, "{\"number\":\"REC-2\"}",
            Checks(new IdempotencyCheck(IdempotencyRules.PermissionCheck, PermissionCatalog.PurchasingReceive, true)));
        var (services, security) = Services(tenant, db, lookups, modules: new[] { ModuleKeys.Purchasing }, permissions: Array.Empty<string>());
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        var http = Request("po-2", "POST", path, body);
        http.RequestServices = services;

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));

        Assert.Equal(IdempotencyRules.RecheckChangedMessage, ex.Message);
        Assert.DoesNotContain(SecurityEventTypes.PermissionDenied, security.Events);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
    }

    /// <summary>
    /// Conteo a ciegas: la respuesta guardada se calculó con las cantidades visibles (warehouse.count = true). Sin ese permiso
    /// ahora solo queda la captura: repetir el cuerpo guardado se saltaría el modo a ciegas → 409 (no 403: la respuesta
    /// guardada no se puede servir y una llamada nueva daría otro resultado), sin repetir. Y al revés (se guardó a ciegas y
    /// ahora ve las cantidades) también 409: la respuesta sería otra.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_changed_blind_mode_permission_is_not_replayed(bool storedWithCount)
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/cycle-counts";
        const string body = "{\"warehousePublicId\":\"x\"}";
        await SeedStoredAsync(db, "cc-1", "POST", path, body, 200, "{\"lines\":[{\"systemQty\":5}]}",
            Checks(new IdempotencyCheck(IdempotencyRules.PermissionCheck, PermissionCatalog.WarehouseCount, storedWithCount)));
        var now = storedWithCount ? new[] { PermissionCatalog.WarehouseCountCapture } : new[] { PermissionCatalog.WarehouseCount };
        var (services, _) = Services(tenant, db, lookups, modules: Array.Empty<string>(), permissions: now);
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        var http = Request("cc-1", "POST", path, body);
        http.RequestServices = services;

        // En los dos sentidos (se tenía y ya no, o no se tenía y ahora sí): 409 con el mismo mensaje.
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));

        Assert.Equal(IdempotencyRules.RecheckChangedMessage, ex.Message);
        Assert.Equal(409, ex.StatusCode);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
    }

    /// <summary>
    /// Rechazo de negocio con clave (next lanza ValidationException): el middleware responde él mismo (para poder guardarlo)
    /// y el cuerpo escrito debe ser idéntico al de ExceptionHandlingMiddleware (ProblemBody con las mismas opciones JSON).
    /// </summary>
    [Fact]
    public async Task A_business_rejection_is_written_with_the_exception_middleware_problem_body()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        tenant.CorrelationId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        var thrown = new ValidationException("sku", "El SKU es obligatorio.");
        // 400 sí se guarda: InMemory no soporta ExecuteUpdate, StoreAsync reintenta y deja el error en el log (no afecta el cuerpo).
        var middleware = new IdempotencyMiddleware(_ => throw thrown, new CapturingLogger());
        var http = Request("val-1");

        await middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions()));

        Assert.Equal(400, http.Response.StatusCode);
        Assert.StartsWith("application/json", http.Response.ContentType);
        Assert.False(http.Response.Headers.ContainsKey(IdempotencyRules.ReplayedHeader));
        http.Response.Body.Position = 0;
        var written = new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEnd();
        var expected = System.Text.Json.JsonSerializer.Serialize(ExceptionHandlingMiddleware.ProblemBody(thrown, tenant.CorrelationId),
            new HttpJsonOptions().SerializerOptions);
        Assert.Equal(expected, written);

        // Y lo mismo que escribe ExceptionHandlingMiddleware para la misma excepción.
        var plain = new DefaultHttpContext();
        plain.Response.Body = new MemoryStream();
        await new ExceptionHandlingMiddleware(_ => throw thrown, Microsoft.Extensions.Logging.Abstractions.NullLogger<ExceptionHandlingMiddleware>.Instance)
            .InvokeAsync(plain, tenant);
        plain.Response.Body.Position = 0;
        Assert.Equal(400, plain.Response.StatusCode);
        Assert.Equal(new StreamReader(plain.Response.Body, Encoding.UTF8).ReadToEnd(), written);
    }

    /// <summary>Con las mismas comprobaciones cumplidas, la repetición devuelve el cuerpo guardado.</summary>
    [Fact]
    public async Task Unchanged_service_checks_are_replayed()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts";
        const string body = "{\"purchaseOrderPublicId\":\"x\"}";
        const string stored = "{\"number\":\"REC-3\"}";
        await SeedStoredAsync(db, "po-3", "POST", path, body, 200, stored,
            Checks(new IdempotencyCheck(IdempotencyRules.ModuleCheck, ModuleKeys.Purchasing, true),
                new IdempotencyCheck(IdempotencyRules.PermissionCheck, PermissionCatalog.WarehouseCount, false)));
        var (services, _) = Services(tenant, db, lookups, modules: new[] { ModuleKeys.Purchasing }, permissions: new[] { PermissionCatalog.WarehouseCountCapture });
        var middleware = new IdempotencyMiddleware(_ => throw new InvalidOperationException("no debe ejecutarse"), new CapturingLogger());
        var http = Request("po-3", "POST", path, body);
        http.RequestServices = services;

        await middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions()));

        Assert.Equal("true", http.Response.Headers[IdempotencyRules.ReplayedHeader].ToString());
        http.Response.Body.Position = 0;
        Assert.Equal(stored, new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEnd());
    }

    /// <summary>Un registro de comprobaciones ilegible no se arriesga: 409, sin repetir.</summary>
    [Fact]
    public async Task Unreadable_recorded_checks_are_not_replayed()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        const string path = "/api/v1/receipts";
        await SeedStoredAsync(db, "po-4", "POST", path, "{}", 200, "{}", "no-es-json");
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        var http = Request("po-4", "POST", path, "{}");

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => middleware.InvokeAsync(http, tenant, db, lookups, Options.Create(new HttpJsonOptions())));
        Assert.Equal(IdempotencyRules.RecheckChangedMessage, ex.Message);
    }

    /// <summary>
    /// La operación con clave anota las comprobaciones que hace el servicio (solo mientras corre la operación) y el registro
    /// las guarda; fuera de ese intervalo (la autorización, que se repite sola) no se anota nada.
    /// </summary>
    [Fact]
    public async Task The_recorder_only_captures_checks_made_inside_the_operation()
    {
        var (tenant, db, lookups) = Context();
        await using var _ = db;
        var (services, _) = Services(tenant, db, lookups, modules: new[] { ModuleKeys.Purchasing }, permissions: new[] { PermissionCatalog.WarehouseCountCapture });
        using var scope = services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<IdempotencyCheckRecorder>();
        var modules = scope.ServiceProvider.GetRequiredService<ModuleService>();
        var permissions = scope.ServiceProvider.GetRequiredService<PermissionService>();

        await permissions.HasPermissionAsync(PermissionCatalog.InventoryView, default);   // antes: no se anota
        recorder.Start();
        await modules.EnsureEnabledAsync(ModuleKeys.Purchasing, default);
        await permissions.HasPermissionAsync(PermissionCatalog.WarehouseCount, default);
        await permissions.HasPermissionAsync(PermissionCatalog.WarehouseCount, default);  // repetida: una sola vez
        var checks = recorder.Stop();
        await permissions.HasPermissionAsync(PermissionCatalog.OrdersCreate, default);   // después: no se anota

        Assert.Equal(new[]
        {
            new IdempotencyCheck(IdempotencyRules.ModuleCheck, ModuleKeys.Purchasing, true),
            new IdempotencyCheck(IdempotencyRules.PermissionCheck, PermissionCatalog.WarehouseCount, false),
        }, checks);
        Assert.Equal(checks, IdempotencyRules.ParseChecks(IdempotencyRules.SerializeChecks(checks)));
        Assert.Null(IdempotencyRules.SerializeChecks(Array.Empty<IdempotencyCheck>()));
    }
}
