using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Asiento planeado de una línea de recibo. Quantity es la MAGNITUD (&gt; 0): el ledger pone el signo según la dirección
/// (D3). Inbound = entra a la posición de recepción (To); si no, sale de ella (From). SerialNumber solo en SERIAL (1 por asiento).
/// </summary>
public sealed record PlannedReceiptPosting(string TxnType, decimal Quantity, bool Inbound, string? ReasonCode, string? SerialNumber = null)
{
    /// <summary>Cantidad con signo tal como la guardará el ledger.</summary>
    public decimal SignedQuantity => Inbound ? Quantity : -Quantity;
}

/// <summary>
/// Lote 6 (P4) — plan de asientos de una línea al confirmar el recibo (D4, R9). Puro; en MAGNITUD.
///
/// Matriz (NONE / LOT):
/// - Recibo ciego o de devolución (no espera cantidades): RECEIPT por lo recibido (nada si es 0).
/// - Línea con esperado E: RECEIPT por E y, por la diferencia d = recibido − E, ADJUSTMENT RECEIPT_VARIANCE de entrada
///   (d &gt; 0) o de salida (d &lt; 0). Recibido 0 ⇒ RECEIPT E y ADJUSTMENT de salida E.
/// - Línea extra de un recibo ASN (sin esperado): solo ADJUSTMENT RECEIPT_VARIANCE de entrada por lo recibido.
///
/// Matriz SERIAL (un asiento de 1 por serie; el ledger no admite una salida de serie sin número):
/// - Ciego/devolución: RECEIPT por cada serie.
/// - Extra: ADJUSTMENT de entrada por cada serie.
/// - Con esperado E y R series: RECEIPT por las primeras min(E, R); ADJUSTMENT de entrada por las R − E sobrantes; si
///   R &lt; E el faltante NO se asienta (no existe la serie que saldría): queda visible como varianza de la línea.
///
/// Invariante (propiedad probada): Σ cantidad CON SIGNO de los asientos = lo recibido.
/// </summary>
public static class ReceiptPostingRules
{
    public static IReadOnlyList<PlannedReceiptPosting> Plan(bool expectsQuantities, decimal? expected, decimal received,
        string trackingCode, IReadOnlyList<string>? serials = null)
    {
        if (received < 0m) throw new ArgumentOutOfRangeException(nameof(received), received, ReceiptRules.ReceivedQtyNegative);
        var isExtra = expectsQuantities && (expected is null || expected.Value <= 0m);
        var hasExpected = expectsQuantities && !isExtra;
        var reason = AdjustmentReasons.ReceiptVariance;
        var result = new List<PlannedReceiptPosting>();

        if (trackingCode == TrackingTypes.Serial)
        {
            var list = serials ?? Array.Empty<string>();
            if (list.Count != received)
                throw new ArgumentException(ReceiptRules.SerialCountMismatch("?", received, list.Count), nameof(serials));
            var k = hasExpected ? (int)Math.Min(decimal.Truncate(expected!.Value), list.Count) : (isExtra ? 0 : list.Count);
            for (var i = 0; i < list.Count; i++)
                result.Add(i < k
                    ? new PlannedReceiptPosting(InventoryTxnTypes.Receipt, 1m, true, null, list[i])
                    : new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, 1m, true, reason, list[i]));
            return result;
        }

        if (!expectsQuantities)
        {
            if (received > 0m) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Receipt, received, true, null));
            return result;
        }

        if (isExtra)
        {
            if (received > 0m) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, received, true, reason));
            return result;
        }

        var e = expected!.Value;
        result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Receipt, e, true, null));
        var diff = received - e;
        if (diff > 0m) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, diff, true, reason));
        else if (diff < 0m) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, -diff, false, reason));
        return result;
    }

    /// <summary>Neto con signo del plan (= lo recibido, por construcción).</summary>
    public static decimal Net(IEnumerable<PlannedReceiptPosting> postings) => postings.Sum(p => p.SignedQuantity);
}
