using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Resultado de validar una resolución de faltante: cantidad a resolver o el error (IsConflict = 409; si no, 400).</summary>
public sealed record ShortageValidation(decimal Quantity, string? Error = null, bool IsConflict = false)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Lote 6 (P8) — reglas puras de los faltantes de compra (pantalla 'Ajustes de inventario', bitácora L762; D8):
/// - Pendiente de una línea = ordenado − recibido − resuelto, nunca negativo.
/// - Acciones (catálogo ShortageAction): CLOSE y REORDER resuelven el faltante COMPLETO de la línea; MANUAL_ADJUSTMENT
///   admite una cantidad parcial (≤ pendiente), con motivo PO_SHORTAGE (por defecto) o FOUND y una posición de entrada.
/// - Sin pendiente → 409 (la segunda de dos resoluciones simultáneas lo recibe bajo el bloqueo de la PO).
/// - Una orden cancelada ya no resuelve faltantes (422); una sin recepciones confirmadas todavía no tiene faltante (422).
/// </summary>
public static class ShortageRules
{
    public const string NoPendingShortage = "La línea ya no tiene faltante pendiente.";
    public const string UnknownAction = "Acción desconocida: use CLOSE, REORDER o MANUAL_ADJUSTMENT.";
    public const string PoNotReceivedYet = "La orden de compra todavía no tiene recepciones confirmadas.";
    public const string PoCancelled = "La orden de compra está cancelada; su faltante ya no se resuelve.";
    public const string QuantityRequired = "Indique la cantidad del ajuste.";
    public const string QuantityPositive = "La cantidad del ajuste debe ser mayor que cero.";
    public const string QuantityDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string BinRequired = "Indique la posición donde entra la mercancía.";
    public const string ReasonNotAllowed = "El motivo del ajuste por faltante debe ser PO_SHORTAGE o FOUND.";
    public const string ManualOnlyFields = "La posición, el lote, las series y el motivo solo aplican al ajuste manual.";
    public const string LineNotFound = "Línea de la orden de compra";

    public static string ManualExceeds(decimal pending) => $"La cantidad del ajuste excede el faltante pendiente ({PurchaseOrderRules.FormatQty(pending)}).";
    public static string FullOnly(decimal pending) => $"Cerrar y Reordenar resuelven el faltante completo ({PurchaseOrderRules.FormatQty(pending)}); para una parte use el ajuste manual.";

    /// <summary>Motivos admitidos en el ajuste manual de un faltante (el primero es el de por defecto).</summary>
    public static readonly IReadOnlyList<string> ManualReasons = new[] { AdjustmentReasons.PoShortage, AdjustmentReasons.Found };

    /// <summary>Pendiente = ordenado − recibido − resuelto; nunca negativo (la sobre-recepción no genera faltante negativo).</summary>
    public static decimal Pending(decimal ordered, decimal received, decimal resolved)
        => Math.Max(0m, ordered - received - resolved);

    /// <summary>Costo del pendiente = pendiente × costo unitario congelado de la línea (Round4).</summary>
    public static decimal PendingCost(decimal pending, decimal unitCost) => PurchaseOrderRules.Round4(pending * unitCost);

    /// <summary>Código canónico de la acción (CLOSE, REORDER, MANUAL_ADJUSTMENT) o null si es desconocida.</summary>
    public static string? NormalizeAction(string? action)
    {
        var code = (action ?? string.Empty).Trim().ToUpperInvariant();
        return code switch
        {
            ShortageActions.Close or ShortageActions.Reorder or ShortageActions.ManualAdjustment => code,
            _ => null,
        };
    }

    /// <summary>Motivo del ajuste manual: vacío → PO_SHORTAGE; PO_SHORTAGE o FOUND → ese; otro → null (400 ReasonNotAllowed).</summary>
    public static string? NormalizeManualReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return AdjustmentReasons.PoShortage;
        var code = reason.Trim().ToUpperInvariant();
        return ManualReasons.Contains(code) ? code : null;
    }

    /// <summary>
    /// ¿La orden admite resolver faltantes? null = sí. CANCELLED → PoCancelled; sin recepciones confirmadas →
    /// PoNotReceivedYet (ambos 422).
    /// </summary>
    public static string? ResolvableError(string statusCode, bool hasConfirmedReceipt)
    {
        if (string.Equals(statusCode, PurchaseOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)) return PoCancelled;
        if (!hasConfirmedReceipt) return PoNotReceivedYet;
        return null;
    }

    /// <summary>
    /// Valida la resolución contra el pendiente recalculado bajo el bloqueo de la PO:
    /// - acción desconocida → 400 UnknownAction;
    /// - pendiente 0 → 409 NoPendingShortage;
    /// - CLOSE/REORDER: resuelven el pendiente completo (una cantidad distinta → 400 FullOnly);
    /// - MANUAL_ADJUSTMENT: cantidad &gt; 0, máximo 3 decimales y ≤ pendiente (si no, 400 ManualExceeds).
    /// </summary>
    public static ShortageValidation Validate(string? action, decimal? quantity, decimal pending)
    {
        var code = NormalizeAction(action);
        if (code is null) return new ShortageValidation(0m, UnknownAction);
        if (pending <= 0m) return new ShortageValidation(0m, NoPendingShortage, IsConflict: true);

        if (code is ShortageActions.Close or ShortageActions.Reorder)
        {
            if (quantity is decimal q && q != pending) return new ShortageValidation(0m, FullOnly(pending));
            return new ShortageValidation(pending);
        }

        if (quantity is null) return new ShortageValidation(0m, QuantityRequired);
        if (quantity.Value <= 0m) return new ShortageValidation(0m, QuantityPositive);
        if (decimal.Round(quantity.Value, 3) != quantity.Value) return new ShortageValidation(0m, QuantityDecimals);
        if (quantity.Value > pending) return new ShortageValidation(0m, ManualExceeds(pending));
        return new ShortageValidation(quantity.Value);
    }
}
