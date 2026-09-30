using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 14 (P2, D14) — revisión Kárdex ↔ saldo en segundo plano, segundos después de cada movimiento. Consume la cola en memoria
/// (InventoryReconciliationQueue) que llena InventoryChangeCommitInterceptor en el commit real de cada transacción con asientos.
/// No es el motor de trabajos programados: el barrido completo (SweepAsync con SCHEDULED) queda fuera de este lote.
///
/// Ciclo:
/// 1. Espera el primer aviso (o la hora del próximo producto en espera), espera DebounceMs y junta lo que siguió llegando:
///    agrupa por tenant sin repetir productos. Un producto revisado hace menos de MinRecheckSeconds espera hasta cumplirlos
///    (los avisos se juntan, no se pierden).
/// 2. Por tenant, en tandas de hasta MaxBatchProducts: alcance nuevo de DI fijado como el tenant SIN usuario
///    (TenantContext.AsAnonymous; la bitácora y el historial quedan como "Sistema") y
///    InventoryReconciliationService.CheckProductsAsync(productos, EVENT, mayor movimiento). Ese servicio hace la primera pasada
///    sin bloqueos y CONFIRMA bajo bloqueo (saldos del producto con UPDLOCK + HOLDLOCK) antes de abrir, actualizar o cerrar
///    descuadres: un movimiento en vuelo no produce un falso descuadre.
/// 3. Un error se registra en el log y en el estado del tenant (LastError) y el ciclo sigue con lo demás.
/// Los comandos de consola no llegan a app.Run(): este servicio no arranca y la cola no acepta avisos.
/// Drain y ProcessDueAsync son públicos para las pruebas (xunit los llama sin esperar al ciclo).
/// </summary>
public sealed class InventoryReconciliationWorker(
    InventoryReconciliationQueue queue,
    IServiceScopeFactory scopes,
    ILogger<InventoryReconciliationWorker> logger) : BackgroundService
{
    private sealed class TenantWait
    {
        /// <summary>Producto → hora desde la que se puede revisar.</summary>
        public Dictionary<int, DateTime> Products { get; } = new();
        public long? MaxTxnId { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, TenantWait> _waiting = new();
    private readonly Dictionary<(int TenantId, int ProductId), DateTime> _lastChecked = new();

    private InventoryReconciliationOptions Options => queue.Options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Enabled)
        {
            logger.LogInformation("Revisión Kárdex ↔ saldo en segundo plano apagada (Inventory:Reconciliation:Enabled = false).");
            return;
        }
        queue.StartConsuming();
        logger.LogInformation("Revisión Kárdex ↔ saldo en segundo plano iniciada (agrupación {DebounceMs} ms, tanda {Batch} productos).",
            Options.DebounceMs, Options.BatchSize);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await WaitForWorkAsync(stoppingToken))
                    {
                        Drain(DateTime.UtcNow);
                        if (Options.Debounce > TimeSpan.Zero) await Task.Delay(Options.Debounce, stoppingToken);
                        Drain(DateTime.UtcNow);
                    }
                    await ProcessDueAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Red de seguridad: un error fuera de la revisión de un tenant no detiene el ciclo.
                    logger.LogError(ex, "Error en el ciclo de la revisión Kárdex ↔ saldo en segundo plano; se continúa.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado normal de la aplicación.
        }
        finally
        {
            queue.StopConsuming();
        }
    }

    /// <summary>
    /// Espera hasta que llegue un aviso (true) o hasta la hora del próximo producto en espera (false). Sin nada en espera,
    /// espera solo avisos.
    /// </summary>
    private async Task<bool> WaitForWorkAsync(CancellationToken stoppingToken)
    {
        var next = NextDueUtc();
        TimeSpan? wait = next is DateTime due ? due - DateTime.UtcNow : null;
        if (wait is { } w && w <= TimeSpan.Zero) return queue.Reader.TryPeek(out _);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (wait is { } timeout) cts.CancelAfter(timeout);
        try
        {
            return await queue.Reader.WaitToReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;   // venció la espera: toca revisar productos que esperaban
        }
    }

    /// <summary>
    /// Pasa a la espera del worker todos los avisos que hay en la cola en este momento (por tenant, sin repetir productos). Un
    /// producto revisado hace menos de MinRecheckSeconds queda para cuando se cumplan. Devuelve cuántos avisos tomó.
    /// </summary>
    public int Drain(DateTime nowUtc)
    {
        var taken = 0;
        while (queue.Reader.TryRead(out var notice))
        {
            lock (_gate)
            {
                if (!_waiting.TryGetValue(notice.TenantId, out var wait)) _waiting[notice.TenantId] = wait = new TenantWait();
                foreach (var productId in notice.ProductIds)
                {
                    var due = _lastChecked.TryGetValue((notice.TenantId, productId), out var at) && at + Options.MinRecheck > nowUtc
                        ? at + Options.MinRecheck
                        : nowUtc;
                    if (!wait.Products.TryGetValue(productId, out var current) || due < current) wait.Products[productId] = due;
                }
                if (notice.MaxTxnId is long m && (wait.MaxTxnId is null || m > wait.MaxTxnId)) wait.MaxTxnId = m;
                queue.SetWaiting(notice.TenantId, wait.Products.Count);   // primero la espera, luego se descuenta de la cola
            }
            queue.OnDequeued(notice);
            taken++;
        }
        return taken;
    }

    /// <summary>
    /// Revisa los productos cuya hora llegó, por tenant (ascendente) y en tandas de MaxBatchProducts. Un error de una tanda se
    /// registra y no impide las demás. Devuelve cuántos productos se revisaron (con o sin error).
    /// </summary>
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<(int TenantId, List<int> Products, long? MaxTxnId)> work;
        lock (_gate)
        {
            work = _waiting.OrderBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value.Products.Where(p => p.Value <= nowUtc).Select(p => p.Key).OrderBy(p => p).ToList(), kv.Value.MaxTxnId))
                .Where(x => x.Item2.Count > 0).ToList();
        }

        var done = 0;
        foreach (var (tenantId, products, maxTxnId) in work)
        {
            foreach (var chunk in products.Chunk(Options.BatchSize))
            {
                ct.ThrowIfCancellationRequested();
                await CheckAsync(tenantId, chunk, maxTxnId, ct);
                done += chunk.Length;
                lock (_gate)
                {
                    var wait = _waiting[tenantId];
                    foreach (var p in chunk)
                    {
                        wait.Products.Remove(p);
                        _lastChecked[(tenantId, p)] = nowUtc;
                    }
                    if (wait.Products.Count == 0) _waiting.Remove(tenantId);
                    queue.SetWaiting(tenantId, wait.Products.Count);
                }
            }
        }

        lock (_gate)
        {
            // Solo se recuerdan las revisiones recientes (las que todavía piden espera).
            foreach (var key in _lastChecked.Where(kv => kv.Value + Options.MinRecheck <= nowUtc).Select(kv => kv.Key).ToList())
                _lastChecked.Remove(key);
        }
        return done;
    }

    /// <summary>Hora del próximo producto en espera; null si no hay ninguno.</summary>
    public DateTime? NextDueUtc()
    {
        lock (_gate)
            return _waiting.Values.SelectMany(w => w.Products.Values).Select(d => (DateTime?)d).Min();
    }

    /// <summary>Una tanda de un tenant, en su propio alcance de DI y como el tenant sin usuario ("Sistema").</summary>
    private async Task CheckAsync(int tenantId, IReadOnlyList<int> productIds, long? maxTxnId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TenantContext>();
            using var asTenant = context.AsAnonymous(tenantId);
            var reconciliation = scope.ServiceProvider.GetRequiredService<InventoryReconciliationService>();
            var run = await reconciliation.CheckProductsAsync(productIds.ToList(), ReconciliationTriggers.Event, maxTxnId, ct);
            queue.OnProcessed(tenantId, productIds.Count, DateTime.UtcNow);
            if (run.Opened > 0 || run.SelfCorrected > 0)
                logger.LogWarning("Revisión Kárdex ↔ saldo (tenant {TenantId}): {Opened} descuadres nuevos, {StillOpen} siguen abiertos, {SelfCorrected} se corrigieron solos.",
                    tenantId, run.Opened, run.StillOpen, run.SelfCorrected);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            queue.OnFailed(tenantId, productIds.Count, ex.Message);
            logger.LogError(ex, "La revisión Kárdex ↔ saldo en segundo plano falló (tenant {TenantId}, {Count} productos); se continúa con lo demás.",
                tenantId, productIds.Count);
        }
    }
}
