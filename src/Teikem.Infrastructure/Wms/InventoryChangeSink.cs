namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 14 (P2, D14) — aviso de que el inventario de un tenant cambió: productos tocados y el mayor InventoryTransactionId
/// asentado. Es lo que viaja de la bandeja de la petición a la cola de la revisión en segundo plano.
/// </summary>
public sealed record InventoryChangeNotice(int TenantId, IReadOnlyList<int> ProductIds, long? MaxTxnId);

/// <summary>
/// Lote 14 (P2, D14) — bandeja de cambios de inventario de la petición (alcance scoped). InventoryLedger.PostAsync anota al
/// final de cada asiento guardado; InventoryChangeCommitInterceptor la vacía al empezar, revertir o fallar una transacción y
/// la manda a la cola SOLO en el commit real. Nada anotado sale de la petición si la transacción no se confirma.
/// </summary>
public interface IInventoryChangeSink
{
    /// <summary>Anota productos tocados por un asiento del tenant y el mayor id de movimiento escrito.</summary>
    void Record(int tenantId, IEnumerable<int> productIds, long? maxTxnId);

    /// <summary>Entrega lo anotado (un aviso por tenant) y deja la bandeja vacía.</summary>
    IReadOnlyList<InventoryChangeNotice> Take();

    /// <summary>Descarta lo anotado (transacción que empieza de nuevo, se revierte o falla).</summary>
    void Clear();

    /// <summary>¿Hay algo anotado?</summary>
    bool HasPending { get; }
}

/// <summary>Implementación en memoria de la bandeja: por tenant, productos sin repetir y el mayor id de movimiento.</summary>
public sealed class InventoryChangeSink : IInventoryChangeSink
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (HashSet<int> Products, long? MaxTxnId)> _byTenant = new();

    public void Record(int tenantId, IEnumerable<int> productIds, long? maxTxnId)
    {
        lock (_gate)
        {
            if (!_byTenant.TryGetValue(tenantId, out var entry)) entry = (new HashSet<int>(), null);
            foreach (var id in productIds) entry.Products.Add(id);
            if (maxTxnId is long m && (entry.MaxTxnId is null || m > entry.MaxTxnId)) entry.MaxTxnId = m;
            _byTenant[tenantId] = entry;
        }
    }

    public IReadOnlyList<InventoryChangeNotice> Take()
    {
        lock (_gate)
        {
            if (_byTenant.Count == 0) return Array.Empty<InventoryChangeNotice>();
            var notices = _byTenant.Where(kv => kv.Value.Products.Count > 0).OrderBy(kv => kv.Key)
                .Select(kv => new InventoryChangeNotice(kv.Key, kv.Value.Products.OrderBy(p => p).ToList(), kv.Value.MaxTxnId))
                .ToList();
            _byTenant.Clear();
            return notices;
        }
    }

    public void Clear()
    {
        lock (_gate) _byTenant.Clear();
    }

    public bool HasPending
    {
        get { lock (_gate) return _byTenant.Count > 0; }
    }
}
