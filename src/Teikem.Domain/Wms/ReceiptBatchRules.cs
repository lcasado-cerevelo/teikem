using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 174 (C2) — reglas puras del recibo por tandas. Una tanda asienta lo recibido y aún no asentado de cada línea
/// (delta = recibido − asentado); la confirmación final asienta el resto con el mismo plan del recibo de una sola vez.
///
/// Plan de una tanda (delta d, esperado pendiente Ep = max(esperado − asentado, 0)):
/// - recibo ciego/devolución (no espera cantidades): RECEIPT por d;
/// - línea extra (sin esperado): ADJUSTMENT RECEIPT_VARIANCE de entrada por d;
/// - con esperado: RECEIPT por min(d, Ep) y, si d &gt; Ep, ADJUSTMENT de entrada por el exceso. El faltante NO se asienta en una tanda
///   (se recibirá en otra o se cierra en la confirmación final).
/// Plan de cierre (resto r = recibido − asentado): el mismo <see cref="ReceiptPostingRules.Plan"/> con el esperado pendiente Ep;
/// sin tandas previas (asentado 0) es exactamente el plan de siempre.
/// Invariante (probada): Σ con signo de todas las tandas + el cierre = lo recibido.
/// </summary>
public static class ReceiptBatchRules
{
    public const string NoModeDirect = "Un recibo directo no admite tandas: cada línea ya entra a su posición destino al confirmar.";
    public const string NothingToConfirm = "No hay unidades nuevas por confirmar: capture lo recibido en al menos una línea antes de confirmar una tanda.";
    public const string ReceivedBelowPosted = "No se puede dejar lo recibido por debajo de lo ya confirmado en tandas.";
    public const string LineHasBatches = "La línea ya tiene unidades confirmadas en tandas: no se puede cambiar su producto ni su lote, ni quitarla del recibo.";
    public const string ReceiptHasBatches = "El recibo ya tiene tandas confirmadas: no se puede eliminar.";
    public const string DamageAfterBatches = "El daño no puede ser mayor que lo recibido y aún no confirmado en tandas.";
    public const string PartNeedsDocument = "Indique el aviso de llegada o la orden de compra (uno solo): una parte se recibe contra un documento.";
    public const string PartNoSerials = "Los productos con número de serie no se reciben por partes: se reciben en la confirmación final.";
    public const string PartNoDamage = "El daño no se declara en una parte: se declara en la confirmación final.";
    public static string LineNotBatchable(string sku, string reason) => $"La línea de {sku} no entra en una tanda: {reason}.";

    public const string ReasonSerial = "los productos con número de serie se confirman en la confirmación final";
    public const string ReasonDamage = "tiene daño declarado; se confirma en la confirmación final";
    public const string ReasonCrossDock = "tiene cruce de muelle asignado; se confirma en la confirmación final";

    /// <summary>Esperado que aún no se cubre con lo asentado (las tandas llenan primero lo esperado).</summary>
    public static decimal? PendingExpected(bool expectsQuantities, decimal? expected, decimal posted)
        => !expectsQuantities || expected is null ? expected : Math.Max(expected.Value - posted, 0m);

    public static IReadOnlyList<PlannedReceiptPosting> PlanBatch(bool expectsQuantities, decimal? expected, decimal posted, decimal delta)
    {
        if (delta < 0m) throw new ArgumentOutOfRangeException(nameof(delta), delta, ReceivedBelowPosted);
        var result = new List<PlannedReceiptPosting>();
        if (delta == 0m) return result;
        var reason = AdjustmentReasons.ReceiptVariance;
        if (!expectsQuantities)
        {
            result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Receipt, delta, true, null));
            return result;
        }
        var ep = PendingExpected(true, expected, posted);
        if (ep is null || (expected ?? 0m) <= 0m)
        {
            result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, delta, true, reason));
            return result;
        }
        var receipt = Math.Min(delta, ep.Value);
        if (receipt > 0m) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Receipt, receipt, true, null));
        if (delta > receipt) result.Add(new PlannedReceiptPosting(InventoryTxnTypes.Adjustment, delta - receipt, true, reason));
        return result;
    }

    /// <summary>Plan de la confirmación final de una línea que puede traer unidades ya asentadas por tandas.</summary>
    public static IReadOnlyList<PlannedReceiptPosting> PlanClose(bool expectsQuantities, decimal? expected, decimal posted, decimal received,
        string trackingCode, IReadOnlyList<string>? serials = null)
    {
        if (posted <= 0m) return ReceiptPostingRules.Plan(expectsQuantities, expected, received, trackingCode, serials);
        // con tandas previas no hay series (los productos con serie no entran a tandas): solo cantidades
        var pending = PendingExpected(expectsQuantities, expected, posted);
        return ReceiptPostingRules.Plan(expectsQuantities, pending, received - posted, trackingCode, serials);
    }

    /// <summary>Motivo por el que una línea no entra a la tanda (null = sí entra, o no tiene nada nuevo).</summary>
    public static string? NotBatchableReason(string trackingCode, decimal damagedQty, bool hasCrossDock)
        => trackingCode == TrackingTypes.Serial ? ReasonSerial
         : damagedQty > 0m ? ReasonDamage
         : hasCrossDock ? ReasonCrossDock
         : null;
}
