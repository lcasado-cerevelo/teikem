using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Clave de un saldo: (producto, almacén, posición, lote). Orden de bloqueo del lote: ProductId, WarehouseId, BinId, LotId.</summary>
public readonly record struct BalanceKey(int ProductId, int WarehouseId, int? BinId, int? LotId) : IComparable<BalanceKey>
{
    public int CompareTo(BalanceKey other)
    {
        var c = ProductId.CompareTo(other.ProductId);
        if (c != 0) return c;
        c = WarehouseId.CompareTo(other.WarehouseId);
        if (c != 0) return c;
        c = Nullable.Compare(BinId, other.BinId);
        return c != 0 ? c : Nullable.Compare(LotId, other.LotId);
    }
}

/// <summary>Fila del ledger para reconstruir saldos (InventoryRules.Rebuild / NetByProduct). Quantity CON SIGNO (D3).</summary>
public sealed record LedgerRow(string TxnType, int ProductId, int? LotId, int? FromWarehouseId, int? FromBinId, int? ToWarehouseId, int? ToBinId, decimal Quantity);

/// <summary>
/// Lote 6 — Reglas puras transversales del inventario (sin EF). Mensajes exactos como const o métodos estáticos (el manual y
/// la FAQ los citan), formateados en cultura invariante.
///
/// Convención del ledger (D3, maestro L331: 'cada despacho escribe movimiento negativo; cada recepción, positivo'):
/// - RECEIPT: + con To. ISSUE y CROSSDOCK: − con solo From. ADJUSTMENT: + con To o − con From (motivo obligatorio).
/// - TRANSFER: + con From y To distintos, en una sola fila.
/// Los llamadores pasan la MAGNITUD (&gt; 0) y el ledger fija el signo con StoredQuantity.
/// </summary>
public static class InventoryRules
{
    /// <summary>Tope de filas por página en las lecturas de inventario (D38).</summary>
    public const int MaxPageSize = 200;
    /// <summary>Máximo representable en DECIMAL(16,3): 13 dígitos enteros.</summary>
    public const decimal MaxQuantity = 9_999_999_999_999.999m;

    public const string QuantityMustBePositive = "La cantidad debe ser mayor que cero.";
    public const string QuantityMaxDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string QuantityTooLarge = "La cantidad excede el máximo permitido.";

    public const string LotRequired = "El producto se controla por lote: indique el lote.";
    public const string LotNotAllowed = "El producto no se controla por lote ni por serie: no indique lote.";
    public const string SerialInteger = "En productos con serie la cantidad debe ser entera.";
    public const string SerialCount = "La cantidad debe ser igual al número de series capturadas.";
    public const string SerialNotAllowed = "El producto no se controla por serie: no indique números de serie.";

    public const string SameBin = "El origen y el destino no pueden ser la misma posición.";
    public const string ReasonRequired = "Indique el motivo del ajuste.";
    public const string SerialQty = "Un movimiento con serie mueve exactamente una unidad.";
    public const string BinRequired = "Indique la posición del movimiento.";
    public const string ReleaseExceedsReserved = "La reserva a liberar excede lo reservado.";

    public static string UnknownTxnType(string? type) => $"Tipo de movimiento desconocido: '{type}'.";
    public static string DirectionInvalid(string type) => type switch
    {
        InventoryTxnTypes.Receipt => "Una recepción entra a una posición (solo destino).",
        InventoryTxnTypes.Issue => "Un despacho sale de una posición (solo origen).",
        InventoryTxnTypes.CrossDock => "Un cruce de muelle sale de una posición (solo origen).",
        InventoryTxnTypes.Adjustment => "Un ajuste entra a una posición o sale de ella, no ambas.",
        InventoryTxnTypes.Transfer => "Una transferencia necesita origen y destino.",
        _ => UnknownTxnType(type),
    };

    /// <summary>'Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}.' (409 insufficient_stock).</summary>
    public static string InsufficientStockMessage(string sku, string bin, decimal available, decimal requested)
        => $"Inventario insuficiente de {sku} en {bin}: disponible {FormatQty(available)}, solicitado {FormatQty(requested)}.";

    /// <summary>'El producto {sku} está inactivo; no admite entradas de inventario.' (422).</summary>
    public static string ProductInactiveMessage(string sku) => $"El producto {sku} está inactivo; no admite entradas de inventario.";

    /// <summary>'La posición {bin} está inactiva; no admite movimientos de inventario.' (422).</summary>
    public static string BinInactiveMessage(string bin) => $"La posición {bin} está inactiva; no admite movimientos de inventario.";

    /// <summary>'El almacén {code} está inactivo; no admite movimientos de inventario.' (422).</summary>
    public static string WarehouseInactiveMessage(string code) => $"El almacén {code} está inactivo; no admite movimientos de inventario.";

    /// <summary>Cantidad válida: &gt; 0, máximo 3 decimales, dentro de DECIMAL(16,3). Devuelve el mensaje de error o null.</summary>
    public static string? ValidateQuantity(decimal? quantity)
    {
        if (quantity is not decimal q || q <= 0m) return QuantityMustBePositive;
        if (decimal.Round(q, 3) != q) return QuantityMaxDecimals;
        if (q > MaxQuantity) return QuantityTooLarge;
        return null;
    }

    /// <summary>Disponible = en mano − reservado. La columna computada QtyAvailable NUNCA se usa en lógica (InMemory no la calcula).</summary>
    public static decimal Available(decimal onHand, decimal reserved) => onHand - reserved;

    /// <summary>
    /// Coherencia con el seguimiento del producto:
    /// - LOT: lote obligatorio; sin series.
    /// - SERIAL: cantidad entera igual al número de series (el lote es opcional).
    /// - NONE: ni lote ni series.
    /// Devuelve el mensaje de error o null.
    /// </summary>
    public static string? ValidateTracking(string trackingType, bool hasLot, decimal quantity, int serialCount)
    {
        switch (trackingType)
        {
            case TrackingTypes.Lot:
                if (!hasLot) return LotRequired;
                if (serialCount > 0) return SerialNotAllowed;
                return null;
            case TrackingTypes.Serial:
                if (decimal.Truncate(quantity) != quantity) return SerialInteger;
                if (serialCount != quantity) return SerialCount;
                return null;
            default:
                if (hasLot) return LotNotAllowed;
                if (serialCount > 0) return SerialNotAllowed;
                return null;
        }
    }

    /// <summary>
    /// Validación de un asiento del ledger sobre la MAGNITUD: cantidad, dirección por tipo, posición en cada lado presente
    /// (no hay saldos 'sin posición', D21), misma posición en TRANSFER, motivo en ADJUSTMENT y una unidad con serie.
    /// Devuelve el mensaje de error o null.
    /// </summary>
    public static string? ValidatePosting(string txnType, int? fromWarehouseId, int? fromBinId, int? toWarehouseId, int? toBinId,
        decimal magnitude, string? reasonCode, bool hasSerial)
    {
        var q = ValidateQuantity(magnitude);
        if (q is not null) return q;
        var hasFrom = fromWarehouseId.HasValue;
        var hasTo = toWarehouseId.HasValue;
        var directionOk = txnType switch
        {
            InventoryTxnTypes.Receipt => hasTo && !hasFrom,
            InventoryTxnTypes.Issue or InventoryTxnTypes.CrossDock => hasFrom && !hasTo,
            InventoryTxnTypes.Adjustment => hasFrom ^ hasTo,
            InventoryTxnTypes.Transfer => hasFrom && hasTo,
            _ => (bool?)null,
        };
        if (directionOk is null) return UnknownTxnType(txnType);
        if (directionOk == false) return DirectionInvalid(txnType);
        if ((hasFrom && fromBinId is null) || (hasTo && toBinId is null)) return BinRequired;
        if ((!hasFrom && fromBinId is not null) || (!hasTo && toBinId is not null)) return DirectionInvalid(txnType);
        if (txnType == InventoryTxnTypes.Transfer && fromWarehouseId == toWarehouseId && fromBinId == toBinId) return SameBin;
        if (txnType == InventoryTxnTypes.Adjustment && string.IsNullOrWhiteSpace(reasonCode)) return ReasonRequired;
        if (hasSerial && magnitude != 1m) return SerialQty;
        return null;
    }

    /// <summary>Cantidad que se guarda en el ledger (D3): +magnitud si hay destino; −magnitud si solo hay origen.</summary>
    public static decimal StoredQuantity(decimal magnitude, bool hasTo) => hasTo ? Math.Abs(magnitude) : -Math.Abs(magnitude);

    /// <summary>
    /// Reconstruye los saldos en mano desde el ledger, por clave: el destino suma |Q| y el origen resta |Q| (una TRANSFER
    /// resta en From y suma en To). Las claves que netean 0 se conservan (el llamador decide si compararlas).
    /// </summary>
    public static IReadOnlyDictionary<BalanceKey, decimal> Rebuild(IEnumerable<LedgerRow> rows)
    {
        var result = new Dictionary<BalanceKey, decimal>();
        foreach (var r in rows ?? Enumerable.Empty<LedgerRow>())
        {
            var abs = Math.Abs(r.Quantity);
            if (r.ToWarehouseId is int tw)
            {
                var k = new BalanceKey(r.ProductId, tw, r.ToBinId, r.LotId);
                result[k] = result.GetValueOrDefault(k) + abs;
            }
            if (r.FromWarehouseId is int fw)
            {
                var k = new BalanceKey(r.ProductId, fw, r.FromBinId, r.LotId);
                result[k] = result.GetValueOrDefault(k) - abs;
            }
        }
        return result;
    }

    /// <summary>Lectura literal de L331 por producto: Σ Quantity sin TRANSFER (que es neutra) = Σ QtyOnHand.</summary>
    public static IReadOnlyDictionary<int, decimal> NetByProduct(IEnumerable<LedgerRow> rows)
        => (rows ?? Enumerable.Empty<LedgerRow>())
            .Where(r => r.TxnType != InventoryTxnTypes.Transfer)
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

    /// <summary>Redondeo monetario a 4 decimales, AwayFromZero (DECIMAL(18,4)).</summary>
    public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Valor = Round4(cantidad × costo unitario); null sin costo.</summary>
    public static decimal? CostValue(decimal quantity, decimal? unitCost) => unitCost is decimal c ? Round4(quantity * c) : null;

    /// <summary>Cantidad legible en cultura invariante, sin ceros sobrantes: 5 → '5', 2.5 → '2.5', 0.125 → '0.125'.</summary>
    public static string FormatQty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
}
