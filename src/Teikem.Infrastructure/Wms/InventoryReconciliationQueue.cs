using System.Collections.Concurrent;
using System.Threading.Channels;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 14 (P2, D14) — configuración de la revisión en segundo plano (sección <c>Inventory:Reconciliation</c>):
/// Enabled (apagada: no se encola ni arranca el consumo), DebounceMs (espera para agrupar avisos por tenant), MaxBatchProducts
/// (productos por revisión), Capacity (tope de avisos en cola; lleno = se descarta y se cuenta) y MinRecheckSeconds (un
/// producto recién revisado espera ese tiempo antes de revisarse otra vez: los avisos se juntan, no se pierden).
/// </summary>
public sealed class InventoryReconciliationOptions
{
    public const string Section = "Inventory:Reconciliation";

    public bool Enabled { get; set; } = true;
    public int DebounceMs { get; set; } = 1500;
    public int MaxBatchProducts { get; set; } = 500;
    public int Capacity { get; set; } = 10_000;
    public int MinRecheckSeconds { get; set; } = 10;

    public TimeSpan Debounce => TimeSpan.FromMilliseconds(Math.Max(0, DebounceMs));
    public TimeSpan MinRecheck => TimeSpan.FromSeconds(Math.Max(0, MinRecheckSeconds));
    public int BatchSize => Math.Max(1, MaxBatchProducts);
}

/// <summary>
/// Lote 14 (P2, D14) — cola EN MEMORIA (singleton) entre el commit de un movimiento de inventario y la revisión Kárdex ↔ saldo
/// que hace InventoryReconciliationWorker. Acotada (Capacity): si está llena el aviso se descarta y se cuenta (Dropped); el
/// siguiente movimiento del producto, "Ejecutar conciliación" o el barrido programado lo cubren (riesgo aceptado en D14).
/// Solo acepta avisos mientras el worker consume (IsConsuming): los comandos de consola (db-init, db-reset, import-legacy)
/// terminan antes de app.Run(), el worker nunca arranca y lo anotado se descarta sin encolar.
/// Contadores POR TENANT (una compañía no ve la actividad ni los errores de otra): Pending = productos en avisos en cola +
/// productos esperando en el worker (agrupación o espera entre revisiones); Processed/Failed = productos revisados o con error.
/// </summary>
public sealed class InventoryReconciliationQueue
{
    /// <summary>Largo máximo del último error que se expone en el estado.</summary>
    public const int LastErrorMaxLength = 300;

    private readonly Channel<InventoryChangeNotice> _channel;
    private readonly ConcurrentDictionary<int, TenantCounters> _counters = new();
    private int _consuming;

    public InventoryReconciliationQueue(InventoryReconciliationOptions options)
    {
        Options = options;
        _channel = Channel.CreateBounded<InventoryChangeNotice>(new BoundedChannelOptions(Math.Max(1, options.Capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,   // TryWrite devuelve false si está llena: se descarta y se cuenta
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public InventoryReconciliationOptions Options { get; }

    /// <summary>¿Hay un consumidor activo? Sin él (consola, worker apagado o detenido) no se encola nada.</summary>
    public bool IsConsuming => Volatile.Read(ref _consuming) == 1;

    /// <summary>Lector de la cola (solo lo usa el worker).</summary>
    public ChannelReader<InventoryChangeNotice> Reader => _channel.Reader;

    /// <summary>
    /// Encola el aviso de un commit. false (y no queda nada pendiente) si la revisión está apagada, no hay consumidor o el aviso
    /// viene vacío; false y Dropped + 1 si la cola está llena.
    /// </summary>
    public bool TryEnqueue(InventoryChangeNotice notice)
    {
        if (!Options.Enabled || !IsConsuming || notice.ProductIds.Count == 0) return false;
        var c = For(notice.TenantId);
        Interlocked.Add(ref c.Queued, notice.ProductIds.Count);   // antes de escribir: el worker puede tomarlo al instante
        if (_channel.Writer.TryWrite(notice)) return true;
        Interlocked.Add(ref c.Queued, -notice.ProductIds.Count);
        Interlocked.Increment(ref c.Dropped);
        return false;
    }

    /// <summary>El worker empieza a consumir (desde aquí se aceptan avisos).</summary>
    public void StartConsuming() => Volatile.Write(ref _consuming, 1);

    /// <summary>El worker deja de consumir (al detenerse la aplicación): los avisos siguientes se descartan.</summary>
    public void StopConsuming() => Volatile.Write(ref _consuming, 0);

    /// <summary>El worker tomó el aviso de la cola (sus productos ya cuentan en su espera, ver SetWaiting).</summary>
    public void OnDequeued(InventoryChangeNotice notice) => Interlocked.Add(ref For(notice.TenantId).Queued, -notice.ProductIds.Count);

    /// <summary>Productos del tenant que esperan en el worker (agrupación o espera entre revisiones).</summary>
    public void SetWaiting(int tenantId, int products) => Volatile.Write(ref For(tenantId).Waiting, Math.Max(0, products));

    /// <summary>Revisión terminada: suma los productos revisados y sella la hora.</summary>
    public void OnProcessed(int tenantId, int products, DateTime atUtc)
    {
        var c = For(tenantId);
        Interlocked.Add(ref c.Processed, products);
        Interlocked.Exchange(ref c.LastProcessedTicks, atUtc.Ticks);
    }

    /// <summary>Revisión con error: suma los productos y guarda el mensaje (recortado) para soporte.</summary>
    public void OnFailed(int tenantId, int products, string message)
    {
        var c = For(tenantId);
        Interlocked.Add(ref c.Failed, products);
        var text = string.IsNullOrWhiteSpace(message) ? "Error sin mensaje." : message.Trim();
        c.LastError = text.Length > LastErrorMaxLength ? text[..LastErrorMaxLength] : text;
    }

    /// <summary>Estado de la revisión automática para el tenant (GET /inventory/reconciliation/status).</summary>
    public ReconciliationStatusDto Status(int? tenantId)
    {
        var c = tenantId is int t && _counters.TryGetValue(t, out var found) ? found : null;
        if (c is null) return new ReconciliationStatusDto(Options.Enabled, IsConsuming, 0, 0, 0, null, null);
        var pending = Math.Max(0, Volatile.Read(ref c.Queued)) + Math.Max(0, Volatile.Read(ref c.Waiting));
        var lastTicks = Interlocked.Read(ref c.LastProcessedTicks);
        return new ReconciliationStatusDto(Options.Enabled, IsConsuming, pending, Interlocked.Read(ref c.Processed),
            Interlocked.Read(ref c.Dropped), lastTicks == 0 ? null : new DateTime(lastTicks, DateTimeKind.Utc), c.LastError,
            Interlocked.Read(ref c.Failed));
    }

    private TenantCounters For(int tenantId) => _counters.GetOrAdd(tenantId, _ => new TenantCounters());

    private sealed class TenantCounters
    {
        public int Queued;
        public int Waiting;
        public long Processed;
        public long Failed;
        public long Dropped;
        public long LastProcessedTicks;
        public volatile string? LastError;
    }
}
