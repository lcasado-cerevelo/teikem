using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Filtro de ubicación del Kárdex (perspectiva de SignedQuantity). Conjuntos vacíos o NULL = sin filtro en esa dimensión.
/// Con ambos conjuntos, una ubicación coincide si su almacén Y su posición están en el filtro.
/// </summary>
public sealed record KardexLocationFilter(IReadOnlySet<int>? WarehouseIds, IReadOnlySet<int>? BinIds)
{
    public static readonly KardexLocationFilter None = new(null, null);

    public bool IsEmpty => (WarehouseIds is null || WarehouseIds.Count == 0) && (BinIds is null || BinIds.Count == 0);

    /// <summary>¿La ubicación (almacén, posición) cae dentro del filtro? Un lado sin almacén (NULL) nunca coincide.</summary>
    public bool Matches(int? warehouseId, int? binId)
    {
        if (warehouseId is null) return false;
        if (WarehouseIds is { Count: > 0 } && !WarehouseIds.Contains(warehouseId.Value)) return false;
        if (BinIds is { Count: > 0 } && (binId is null || !BinIds.Contains(binId.Value))) return false;
        return true;
    }
}

/// <summary>
/// Lote 6 (P3) — reglas puras de lectura del ledger (Kárdex, genealogía, conciliación). Sin BD.
///
/// Convención del ledger (D3, maestro L331): InventoryTransaction.Quantity se guarda CON SIGNO.
/// - RECEIPT: + con To. ISSUE y CROSSDOCK: − con solo From. ADJUSTMENT: + con To o − con From.
/// - TRANSFER: + con From y To distintos, en una sola fila.
/// El Kárdex muestra la cantidad del ledger tal cual (Quantity) y, aparte, SignedQuantity según la perspectiva del filtro
/// de ubicación: sin filtro, la del ledger salvo TRANSFER = 0 (neutra para el total); con filtro de almacenes o posiciones,
/// lo que sale del filtro es −|Q|, lo que entra es +|Q| y lo interno (ambos lados dentro) es 0.
/// </summary>
public static class KardexRules
{
    /// <summary>Texto de la columna Posición cuando el movimiento no tiene ubicación.</summary>
    public const string NoPosition = "—";
    /// <summary>Separador entre origen y destino en la columna Posición.</summary>
    public const string Arrow = " → ";
    /// <summary>Etiqueta del dueño cuando el producto es propio del tenant (Product.ClientId NULL).</summary>
    public const string OwnLabel = "Propio";

    public static string UnknownTypeMessage(string code) => $"Tipo de movimiento desconocido: '{code}'.";
    public static string UnknownRefEntityMessage(string code) => $"Tipo de documento de referencia desconocido: '{code}'.";
    public const string RangeInverted = "La fecha 'desde' no puede ser posterior a la fecha 'hasta'.";

    /// <summary>
    /// Cantidad con la perspectiva del filtro. Sin filtro: la del ledger (RECEIPT +, ISSUE −, CROSSDOCK −, ADJUSTMENT ±),
    /// salvo TRANSFER = 0. Con filtro: salida del filtro −|Q|, entrada +|Q|, interna (o ajena) 0.
    /// </summary>
    public static decimal SignedQuantity(decimal storedQuantity, string typeCode, int? fromWarehouseId, int? fromBinId,
        int? toWarehouseId, int? toBinId, KardexLocationFilter? filter = null)
    {
        if (filter is null || filter.IsEmpty)
            return string.Equals(typeCode, InventoryTxnTypes.Transfer, StringComparison.OrdinalIgnoreCase) ? 0m : storedQuantity;

        var magnitude = Math.Abs(storedQuantity);
        var inFrom = filter.Matches(fromWarehouseId, fromBinId);
        var inTo = filter.Matches(toWarehouseId, toBinId);
        if (inFrom && inTo) return 0m;
        if (inFrom) return -magnitude;
        if (inTo) return magnitude;
        return 0m;
    }

    /// <summary>
    /// Columna Posición: 'ALM-01/A01-R01-N1-P01' (un lado), 'ALM-01/A → ALM-02/B' (transferencia) o '—' (sin ubicación).
    /// Sin posición se muestra solo el código del almacén.
    /// </summary>
    public static string Position(string? fromWarehouseCode, string? fromBinCode, string? toWarehouseCode, string? toBinCode)
    {
        var from = Location(fromWarehouseCode, fromBinCode);
        var to = Location(toWarehouseCode, toBinCode);
        if (from is not null && to is not null) return from + Arrow + to;
        return from ?? to ?? NoPosition;
    }

    private static string? Location(string? warehouseCode, string? binCode)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode)) return string.IsNullOrWhiteSpace(binCode) ? null : binCode;
        return string.IsNullOrWhiteSpace(binCode) ? warehouseCode : warehouseCode + "/" + binCode;
    }

    /// <summary>
    /// Origen legible del movimiento por EntityType. Con número de documento: 'Recibo REC-00001', 'Recolección EMP-00001',
    /// 'Conteo CC-00001', 'Orden de compra PO-00001', 'Orden 2026-000123', 'Cruce de muelle XD-00001'; la tarea de almacén
    /// se muestra como 'Tarea #12'. Sin número conocido: fallback 'TIPO·id'. Sin referencia: NULL.
    /// </summary>
    public static string? RefLabel(string? refEntityCode, int? refId, string? documentNumber = null)
    {
        if (string.IsNullOrWhiteSpace(refEntityCode)) return null;
        var id = refId?.ToString(CultureInfo.InvariantCulture) ?? "?";
        if (string.Equals(refEntityCode, EntityTypes.WarehouseTask, StringComparison.OrdinalIgnoreCase))
            return refId is null ? Fallback(refEntityCode, id) : "Tarea #" + id;
        if (string.IsNullOrWhiteSpace(documentNumber)) return Fallback(refEntityCode, id);

        var prefix = RefPrefix(refEntityCode);
        return prefix is null ? documentNumber : prefix + " " + documentNumber;
    }

    /// <summary>Prefijo del origen legible por EntityType (NULL = solo el número).</summary>
    public static string? RefPrefix(string refEntityCode) => refEntityCode.ToUpperInvariant() switch
    {
        EntityTypes.Receipt => "Recibo",
        EntityTypes.PickBatch => "Recolección",
        EntityTypes.CycleCount => "Conteo",
        EntityTypes.PurchaseOrder => "Orden de compra",
        EntityTypes.TransportOrder => "Orden",
        EntityTypes.CrossDockAllocation => "Cruce de muelle",
        EntityTypes.CrossDockPlan => "Cruce de muelle",
        _ => null,
    };

    private static string Fallback(string code, string id) => code.ToUpperInvariant() + "·" + id;

    /// <summary>Etiqueta corta (chip) del tipo de movimiento.</summary>
    public static string TypeChip(string typeCode) => (typeCode ?? string.Empty).ToUpperInvariant() switch
    {
        InventoryTxnTypes.Receipt => "Recepción",
        InventoryTxnTypes.Issue => "Despacho",
        InventoryTxnTypes.Transfer => "Transferencia",
        InventoryTxnTypes.Adjustment => "Ajuste",
        InventoryTxnTypes.CrossDock => "Cruce de muelle",
        _ => typeCode ?? string.Empty,
    };

    /// <summary>¿'desde' es posterior a 'hasta'? (400 RangeInverted).</summary>
    public static bool IsRangeInverted(DateOnly? from, DateOnly? to) => from is DateOnly f && to is DateOnly t && f > t;

    /// <summary>Rango UTC del Kárdex a partir de fechas: desde inclusivo (00:00 UTC) y hasta EXCLUSIVO (+1 día).</summary>
    public static (DateTime? FromUtc, DateTime? ToUtcExclusive) UtcRange(DateOnly? from, DateOnly? to)
        => (from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    /// <summary>Página acotada: skip ≥ 0; take en 1..max (take ≤ 0 → max).</summary>
    public static (int Skip, int Take) Page(int skip, int take, int max)
        => (Math.Max(0, skip), take <= 0 ? max : Math.Min(take, max));

    /// <summary>Categorías pedidas más todas sus descendientes (el árbol se recorre con protección contra ciclos).</summary>
    public static IReadOnlySet<int> WithDescendants(IEnumerable<int> categoryIds, IReadOnlyDictionary<int, int?> parentOf)
    {
        var result = new HashSet<int>(categoryIds);
        var children = parentOf.Where(kv => kv.Value.HasValue).GroupBy(kv => kv.Value!.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());
        var pending = new Queue<int>(result);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!children.TryGetValue(id, out var kids)) continue;
            foreach (var k in kids)
                if (result.Add(k)) pending.Enqueue(k);
        }
        return result;
    }

    /// <summary>Días al vencimiento (negativo = vencido); NULL sin fecha.</summary>
    public static int? DaysToExpiry(DateOnly? expiry, DateOnly today)
        => expiry is DateOnly e ? e.DayNumber - today.DayNumber : null;

    /// <summary>Dueño legible: nombre del cliente 3PL o 'Propio'.</summary>
    public static string OwnerLabel(string? clientName) => string.IsNullOrWhiteSpace(clientName) ? OwnLabel : clientName;

    // ---------------------------------------------------------------- genealogía

    /// <summary>
    /// Entradas y salidas de un lote a partir de las cantidades del ledger CON signo, sin TRANSFER (no cambia la existencia
    /// del lote): QtyIn = Σ positivas; QtyOut = Σ |negativas|.
    /// </summary>
    public static (decimal QtyIn, decimal QtyOut) InOut(IEnumerable<(string TypeCode, decimal Quantity)> rows)
    {
        decimal qin = 0m, qout = 0m;
        foreach (var (type, q) in rows)
        {
            if (string.Equals(type, InventoryTxnTypes.Transfer, StringComparison.OrdinalIgnoreCase)) continue;
            if (q > 0) qin += q; else qout += -q;
        }
        return (qin, qout);
    }

    /// <summary>
    /// Cantidad neta enviada a un destino (documento de referencia) = −Σ cantidades con signo de sus movimientos sin
    /// TRANSFER. Una reversa (ajuste de entrada con la misma referencia) la neutraliza: un destino con neto ≤ 0 ya no cuenta.
    /// </summary>
    public static decimal DestinationQty(IEnumerable<(string TypeCode, decimal Quantity)> rows)
        => -rows.Where(r => !string.Equals(r.TypeCode, InventoryTxnTypes.Transfer, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Quantity);

    /// <summary>¿El tipo de movimiento es una salida hacia un destino (despacho o cruce de muelle)?</summary>
    public static bool IsOutbound(string typeCode)
        => string.Equals(typeCode, InventoryTxnTypes.Issue, StringComparison.OrdinalIgnoreCase)
           || string.Equals(typeCode, InventoryTxnTypes.CrossDock, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- conciliación

    /// <summary>
    /// Saldo reconstruido por clave (producto, almacén, posición, lote) desde el ledger: el lado To suma |Q| y el lado From
    /// resta |Q| (una TRANSFER mueve |Q| de From a To). Entrada: sumas ya agrupadas por lado.
    /// </summary>
    public static Dictionary<LedgerKey, decimal> Rebuild(IEnumerable<(LedgerKey Key, decimal Magnitude)> toSides,
        IEnumerable<(LedgerKey Key, decimal Magnitude)> fromSides)
    {
        var map = new Dictionary<LedgerKey, decimal>();
        foreach (var (k, m) in toSides) map[k] = map.GetValueOrDefault(k) + Math.Abs(m);
        foreach (var (k, m) in fromSides) map[k] = map.GetValueOrDefault(k) - Math.Abs(m);
        return map;
    }
}

/// <summary>Clave de saldo reconstruido desde el ledger (misma clave que UQ_StockBalance).</summary>
public readonly record struct LedgerKey(int ProductId, int WarehouseId, int? BinId, int? LotId);
