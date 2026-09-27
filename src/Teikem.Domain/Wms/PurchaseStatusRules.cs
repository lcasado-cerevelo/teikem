using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Lote 6 — Reglas puras del estatus de la orden de compra frente a la recepción y los faltantes.</summary>
public static class PurchaseStatusRules
{
    public const string NotReceivable = "La orden de compra debe estar enviada o recibida parcial para recibir contra ella.";
    public const string NothingPending = "La orden de compra no tiene cantidades pendientes de recibir.";

    /// <summary>Pendiente = pedido − recibido − resuelto, nunca negativo.</summary>
    public static decimal Pending(decimal ordered, decimal received, decimal resolved) => Math.Max(0m, ordered - received - resolved);

    /// <summary>Solo se recibe contra una PO SENT o PARTIAL.</summary>
    public static bool IsReceivable(string statusCode) => statusCode is PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial;

    /// <summary>
    /// Estatus después de aplicar una recepción: RECEIVED si ninguna línea queda con pendiente; PARTIAL si algo se recibió;
    /// si no, el actual.
    /// </summary>
    public static string StatusAfterReceipt(string current, IEnumerable<(decimal Ordered, decimal Received, decimal Resolved)> lines)
    {
        var list = (lines ?? Enumerable.Empty<(decimal, decimal, decimal)>()).ToList();
        if (list.Count > 0 && list.All(l => Pending(l.Ordered, l.Received, l.Resolved) == 0m)) return PurchaseOrderStatuses.Received;
        if (list.Any(l => l.Received > 0m)) return PurchaseOrderStatuses.Partial;
        return current;
    }

    /// <summary>Después de resolver un faltante: una PO PARTIAL sin pendientes pasa a RECEIVED; en otro caso no cambia.</summary>
    public static string StatusAfterResolution(string current, IEnumerable<(decimal Ordered, decimal Received, decimal Resolved)> lines)
    {
        var list = (lines ?? Enumerable.Empty<(decimal, decimal, decimal)>()).ToList();
        return current == PurchaseOrderStatuses.Partial && list.Count > 0 && list.All(l => Pending(l.Ordered, l.Received, l.Resolved) == 0m)
            ? PurchaseOrderStatuses.Received
            : current;
    }
}
