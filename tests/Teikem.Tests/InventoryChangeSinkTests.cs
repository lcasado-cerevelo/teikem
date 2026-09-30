using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Persistence.Interceptors;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 (P2, D14) — bandeja de cambios de inventario, interceptor de transacción y cola en memoria. InMemory no dispara los
/// eventos de transacción de EF, así que la secuencia que produce SQL Server (empezar → anotar → confirmar / revertir /
/// reintentar) se reproduce llamando al interceptor en el mismo orden; los asientos son del ledger real (WmsFixture).
/// Cubre: PostAsync anota productos y el mayor movimiento; el commit envía; revertir o fallar no envía; un reintento solo envía
/// lo del intento que se confirmó; una transacción anidada (RunInTransactionAsync se une a la de afuera: un solo empezar y un
/// solo confirmar) envía una vez; sin consumidor (comandos de consola) no se encola nada; cola llena descarta y cuenta;
/// contadores aislados por tenant; y el contenedor real registra bandeja, interceptor, cola y worker.
/// </summary>
public class InventoryChangeSinkTests
{
    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin B, Product P, Product Q);

    private static async Task<World> SeedAsync()
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("ALM-01");
        var b = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "A-01");
        var p = await f.AddProductAsync("SKU-P");
        var q = await f.AddProductAsync("SKU-Q");
        return new World(f, w, b, p, q);
    }

    private static InventoryPosting Receive(World w, Product p, decimal qty)
        => new(InventoryTxnTypes.Receipt, p.ProductId, qty, ToWarehouseId: w.W.WarehouseId, ToBinId: w.B.WarehouseBinId);

    private static InventoryReconciliationQueue Consuming(int capacity = 100)
    {
        var queue = new InventoryReconciliationQueue(new InventoryReconciliationOptions { Capacity = capacity });
        queue.StartConsuming();
        return queue;
    }

    private static List<InventoryChangeNotice> Queued(InventoryReconciliationQueue queue)
    {
        var list = new List<InventoryChangeNotice>();
        while (queue.Reader.TryRead(out var n)) list.Add(n);
        return list;
    }

    [Fact]
    public async Task PostAsync_records_the_products_and_the_highest_transaction_id()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        Assert.False(w.F.Changes.HasPending);
        var first = await w.F.PostAsync(Receive(w, w.P, 5m), Receive(w, w.Q, 2m));
        var second = await w.F.PostAsync(Receive(w, w.P, 1m));

        var notice = Assert.Single(w.F.Changes.Take());
        Assert.Equal(WmsFixture.TenantId, notice.TenantId);
        Assert.Equal(new[] { w.P.ProductId, w.Q.ProductId }.OrderBy(x => x), notice.ProductIds);
        Assert.Equal(first.Concat(second).Max(), notice.MaxTxnId);
        Assert.False(w.F.Changes.HasPending);   // Take deja la bandeja vacía
        Assert.Empty(w.F.Changes.Take());
    }

    [Fact]
    public async Task A_rejected_posting_records_nothing()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        // Salida sin existencia → 409 antes de guardar: nada que revisar.
        await Assert.ThrowsAnyAsync<Exception>(() => w.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Issue, w.P.ProductId, 1m,
            FromWarehouseId: w.W.WarehouseId, FromBinId: w.B.WarehouseBinId)));
        Assert.False(w.F.Changes.HasPending);
    }

    [Fact]
    public async Task The_commit_sends_what_was_recorded_and_rollback_or_failure_discard_it()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var queue = Consuming();
        var interceptor = new InventoryChangeCommitInterceptor(w.F.Changes, queue);

        interceptor.OnTransactionStarted();
        await w.F.PostAsync(Receive(w, w.P, 3m));
        Assert.Equal(1, interceptor.OnTransactionCommitted());
        var sent = Assert.Single(Queued(queue));
        Assert.Equal(new[] { w.P.ProductId }, sent.ProductIds);
        Assert.False(w.F.Changes.HasPending);

        // Revertida: lo anotado no sale.
        interceptor.OnTransactionStarted();
        await w.F.PostAsync(Receive(w, w.Q, 3m));
        interceptor.OnTransactionAbandoned();
        Assert.Equal(0, interceptor.OnTransactionCommitted());   // un commit posterior sin anotar nada no envía lo revertido
        Assert.Empty(Queued(queue));
    }

    [Fact]
    public async Task A_retried_transaction_only_sends_the_attempt_that_committed()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var queue = Consuming();
        var interceptor = new InventoryChangeCommitInterceptor(w.F.Changes, queue);

        // Intento 1: empieza, anota P y falla sin evento de rollback (dispose); la estrategia de reintentos vuelve a empezar.
        interceptor.OnTransactionStarted();
        await w.F.PostAsync(Receive(w, w.P, 3m));
        // Intento 2: empieza (vacía lo del intento 1), anota Q y confirma.
        interceptor.OnTransactionStarted();
        var ids = await w.F.PostAsync(Receive(w, w.Q, 2m));
        interceptor.OnTransactionCommitted();

        var sent = Assert.Single(Queued(queue));
        Assert.Equal(new[] { w.Q.ProductId }, sent.ProductIds);
        Assert.Equal(ids.Max(), sent.MaxTxnId);
    }

    [Fact]
    public async Task A_nested_transaction_is_sent_once_with_the_outer_commit()
    {
        var w = await SeedAsync();
        await using var _ = w.F;
        var queue = Consuming();
        var interceptor = new InventoryChangeCommitInterceptor(w.F.Changes, queue);

        // RunInTransactionAsync anidado se une a la de afuera: un solo "empieza" y un solo "confirma" para los dos asientos.
        interceptor.OnTransactionStarted();
        var a = await w.F.PostAsync(Receive(w, w.P, 3m));   // llamada anidada 1
        var b = await w.F.PostAsync(Receive(w, w.Q, 1m));   // llamada anidada 2
        Assert.True(w.F.Changes.HasPending);
        interceptor.OnTransactionCommitted();

        var sent = Assert.Single(Queued(queue));
        Assert.Equal(new[] { w.P.ProductId, w.Q.ProductId }.OrderBy(x => x), sent.ProductIds);
        Assert.Equal(a.Concat(b).Max(), sent.MaxTxnId);
        Assert.Equal(2, queue.Status(WmsFixture.TenantId).Pending);
    }

    [Fact]
    public void Without_a_consumer_nothing_is_queued_and_nothing_stays_pending()
    {
        // Comandos de consola (db-init, import-legacy): el worker no arranca → la cola no acepta y lo anotado se descarta.
        var queue = new InventoryReconciliationQueue(new InventoryReconciliationOptions());
        var sink = new InventoryChangeSink();
        var interceptor = new InventoryChangeCommitInterceptor(sink, queue);
        interceptor.OnTransactionStarted();
        sink.Record(1, new[] { 10, 11 }, 99);
        Assert.Equal(0, interceptor.OnTransactionCommitted());
        Assert.False(sink.HasPending);
        Assert.False(queue.Reader.TryRead(out var _));
        var status = queue.Status(1);
        Assert.Equal((true, false, 0, 0L, 0L), (status.Enabled, status.Consuming, status.Pending, status.Processed, status.Dropped));

        // Apagada por configuración: tampoco, aunque haya consumidor.
        var off = new InventoryReconciliationQueue(new InventoryReconciliationOptions { Enabled = false });
        off.StartConsuming();
        Assert.False(off.TryEnqueue(new InventoryChangeNotice(1, new[] { 1 }, null)));
        Assert.False(off.Status(1).Enabled);
    }

    [Fact]
    public void A_full_queue_drops_and_counts_and_counters_are_per_tenant()
    {
        var queue = Consuming(capacity: 2);
        Assert.True(queue.TryEnqueue(new InventoryChangeNotice(1, new[] { 1, 2 }, 5)));
        Assert.True(queue.TryEnqueue(new InventoryChangeNotice(2, new[] { 3 }, 6)));
        Assert.False(queue.TryEnqueue(new InventoryChangeNotice(1, new[] { 4 }, 7)));   // llena
        Assert.False(queue.TryEnqueue(new InventoryChangeNotice(1, Array.Empty<int>(), 8)));   // vacío: no cuenta

        var t1 = queue.Status(1);
        Assert.Equal((2, 1L), (t1.Pending, t1.Dropped));
        var t2 = queue.Status(2);
        Assert.Equal((1, 0L), (t2.Pending, t2.Dropped));
        var none = queue.Status(3);
        Assert.Equal((0, 0L, 0L), (none.Pending, none.Processed, none.Dropped));
        Assert.Equal(0, queue.Status(null).Pending);

        queue.OnFailed(1, 2, new string('x', 400));
        Assert.Equal(InventoryReconciliationQueue.LastErrorMaxLength, queue.Status(1).LastError!.Length);
        Assert.Null(queue.Status(2).LastError);   // el error de una compañía no se ve en otra
        queue.StopConsuming();
        Assert.False(queue.TryEnqueue(new InventoryChangeNotice(2, new[] { 9 }, 1)));
    }

    [Fact]
    public void The_sink_groups_by_tenant_without_repeating_products()
    {
        var sink = new InventoryChangeSink();
        sink.Record(2, new[] { 5, 3 }, 10);
        sink.Record(1, new[] { 7 }, null);
        sink.Record(2, new[] { 3, 4 }, 8);
        var notices = sink.Take();
        Assert.Equal(new[] { 1, 2 }, notices.Select(n => n.TenantId));
        Assert.Equal(new[] { 7 }, notices[0].ProductIds);
        Assert.Null(notices[0].MaxTxnId);
        Assert.Equal(new[] { 3, 4, 5 }, notices[1].ProductIds);
        Assert.Equal(10L, notices[1].MaxTxnId);
        sink.Record(1, new[] { 1 }, 1);
        sink.Clear();
        Assert.Empty(sink.Take());
    }

    [Fact]
    public void The_real_container_wires_sink_interceptor_queue_and_worker()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Teikem"] = "Server=model-only.invalid;Database=Teikem;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 64),
            ["Inventory:Reconciliation:DebounceMs"] = "250",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddTeikemInfrastructure(config);
        Assert.Contains(services, d => d.ServiceType == typeof(IInventoryChangeSink) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(InventoryChangeCommitInterceptor) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(InventoryReconciliationQueue) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(InventoryReconciliationWorker));

        using var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<InventoryReconciliationQueue>();
        Assert.Equal(250, queue.Options.DebounceMs);
        Assert.False(queue.IsConsuming);   // sin host (consola) nadie consume

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TeikemDbContext>();
        var interceptors = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!;
        var interceptor = Assert.Single(interceptors.OfType<InventoryChangeCommitInterceptor>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<InventoryChangeCommitInterceptor>(), interceptor);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InventoryLedger>());   // el ledger resuelve con su bandeja

        // Misma bandeja en el alcance: lo que anota el ledger lo envía el interceptor de ESE alcance.
        queue.StartConsuming();
        scope.ServiceProvider.GetRequiredService<IInventoryChangeSink>().Record(1, new[] { 42 }, 7);
        Assert.Equal(1, interceptor.OnTransactionCommitted());
        Assert.Equal(1, queue.Status(1).Pending);
    }

    [Fact]
    public void Console_commands_end_before_the_host_runs()
    {
        // db-init, db-reset e import-legacy regresan antes de app.Run(): los servicios en segundo plano nunca arrancan.
        var program = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "src", "Teikem.Api", "Program.cs"));
        var run = program.IndexOf("app.Run();", StringComparison.Ordinal);
        Assert.True(run > 0);
        foreach (var verb in new[] { "\"db-init\"", "DbResetRules.Verb", "LegacyImportRunner.Verb" })
        {
            var at = program.IndexOf(verb, StringComparison.Ordinal);
            Assert.True(at > 0 && at < run, $"{verb} debe resolverse antes de app.Run()");
        }
    }
}
