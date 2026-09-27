using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>Línea capturada de una orden de compra, con los datos del producto ya resueltos (para validar sin BD).</summary>
public sealed record PurchaseOrderLineInput(int ProductId, string Sku, bool IsOwn, bool IsSerial, decimal? QtyOrdered, decimal? UnitCost, decimal? DefaultUnitCost);

/// <summary>Línea válida de una orden de compra: producto, cantidad ordenada y costo unitario congelado.</summary>
public sealed record PurchaseOrderLinePlan(int ProductId, decimal QtyOrdered, decimal UnitCost);

/// <summary>Línea existente de la orden de compra (para las guardas de edición con recepciones).</summary>
public sealed record PurchaseOrderExistingLine(int PurchaseOrderLineId, int ProductId, string Sku, decimal QtyReceived, decimal UnitCost, bool IsReferenced);

/// <summary>Resultado del reemplazo de líneas: qué se actualiza, qué se agrega y qué se elimina.</summary>
public sealed record PurchaseOrderLineChanges(
    IReadOnlyList<(int PurchaseOrderLineId, PurchaseOrderLinePlan Plan)> Updates,
    IReadOnlyList<PurchaseOrderLinePlan> Additions,
    IReadOnlyList<int> Removals);

/// <summary>
/// Lote 6 (P8) — reglas puras de la orden de compra (Compras 13B mínimas, maestro L461):
/// - Cancelar: desde DRAFT, SENT o PARTIAL (con bitácora en el historial); RECEIVED es terminal y no se cancela. Con un
///   recibo OPEN sobre la orden → 409 (confírmelo o elimínelo antes).
/// - Eliminar (baja lógica): solo DRAFT, SENT o CANCELLED, sin recepciones confirmadas y sin recibo abierto. Una cancelada
///   con recepciones se conserva con su bitácora.
/// - Editar NO es regla pura: la decide la capacidad EDIT_PURCHASE_ORDER (D46, negada fuera de DRAFT por defecto). Si el
///   tenant la habilita en SENT o PARTIAL, las líneas con recepciones siguen protegidas (ReceivedLineLocked).
/// - Líneas: producto propio, cantidad &gt; 0 con máximo 3 decimales (entera con serie), costo ≥ 0 con máximo 4 decimales
///   (por defecto Product.PurchaseCost), producto no repetido, máximo 200 líneas. Total con Round4.
/// Los mensajes son constantes o métodos estáticos porque el manual y la FAQ los citan.
/// </summary>
public static class PurchaseOrderRules
{
    public const int MaxLines = 200;
    public const int MaxPageSize = 200;
    public const int MaxNotesLength = 2000;
    public const string NumberPattern = "PO-#####";

    // ---------------------------------------------------------------- mensajes
    public const string NotFound = "Orden de compra";
    public const string CannotCancelReceived = "Una orden de compra recibida completa no se cancela.";
    public const string AlreadyCancelled = "La orden de compra ya está cancelada.";
    public const string HasOpenReceipt = "La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.";
    public const string HasOpenReceiptDelete = "La orden de compra tiene un recibo abierto; elimínelo antes de eliminar la orden de compra.";
    public const string HasOpenReceiptEdit = "La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cambiar sus líneas.";
    public const string DeleteWithReceipts = "Una orden de compra con recepciones no se elimina; cancélela.";
    public const string DeleteCancelledWithReceipts = "Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina.";
    public const string DeleteReceived = "Una orden de compra recibida completa se conserva con su bitácora; no se elimina.";
    public const string OnlyDraftSends = "Solo una orden de compra en borrador se envía.";
    public const string NotReceivable = "La orden de compra debe estar enviada o recibida parcial para recibir contra ella.";
    public const string LinesRequired = "La orden de compra debe tener al menos una línea.";
    public const string TooManyLines = "La orden de compra admite como máximo 200 líneas.";
    public const string ProductRequired = "Indique el producto.";
    public const string QtyRequired = "La cantidad ordenada debe ser mayor que cero.";
    public const string QtyDecimals = "La cantidad admite como máximo 3 decimales.";
    public const string CostNegative = "El costo unitario no puede ser negativo.";
    public const string CostDecimals = "El costo unitario admite como máximo 4 decimales.";
    public const string ExpectedBeforeOrder = "La fecha esperada no puede ser anterior a la fecha de la orden.";
    public const string NotesTooLong = "Las notas admiten como máximo 2000 caracteres.";
    public const string SupplierRequired = "Indique el proveedor.";
    public const string SupplierInactive = "El proveedor está dado de baja; no admite órdenes de compra nuevas.";
    public const string WarehouseInactive = "El almacén está dado de baja; no admite órdenes de compra nuevas.";
    public const string WarehouseRequired = "Indique el almacén: la compañía tiene más de uno.";
    public const string NoActiveWarehouse = "La compañía no tiene almacenes activos.";

    public static string OwnProductOnly(string sku) => $"La orden de compra solo admite productos propios; {sku} pertenece a un cliente.";
    public static string ProductInactive(string sku) => $"El producto {sku} está inactivo; no admite órdenes de compra.";
    public static string CostRequired(string sku) => $"Indique el costo unitario de {sku}.";
    public static string DuplicateProduct(string sku) => $"El producto {sku} está repetido en la orden de compra.";
    public static string SerialQtyInteger(string sku) => $"La cantidad de {sku} debe ser entera: el producto se controla por serie.";
    public static string ReceivedLineLocked(string sku, decimal received)
        => $"La línea de {sku} ya tiene recepciones: no se elimina, no baja de lo recibido ({FormatQty(received)}) y su costo no cambia.";
    public static string LineInUse(string sku) => $"La línea de {sku} ya se usó en un aviso de llegada; no se elimina.";
    public static string ImmutableField(string field) => $"El campo {field} de la orden de compra no se puede cambiar.";
    public static string UnknownCurrency(string code) => $"Moneda desconocida: '{code}'.";

    // ---------------------------------------------------------------- estatus

    /// <summary>¿Se puede cancelar? null = sí; si no, el mensaje (RECEIVED terminal o ya cancelada).</summary>
    public static string? CancelError(string statusCode) => Code(statusCode) switch
    {
        PurchaseOrderStatuses.Draft or PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial => null,
        PurchaseOrderStatuses.Received => CannotCancelReceived,
        PurchaseOrderStatuses.Cancelled => AlreadyCancelled,
        _ => CannotCancelReceived,
    };

    /// <summary>DRAFT, SENT o PARTIAL (L461).</summary>
    public static bool CanCancel(string statusCode) => CancelError(statusCode) is null;

    /// <summary>
    /// ¿Se puede eliminar (baja lógica)? null = sí. Solo DRAFT, SENT o CANCELLED, sin recepciones confirmadas y sin recibo
    /// abierto. Una cancelada con recepciones se conserva con su bitácora.
    /// </summary>
    public static string? DeleteError(string statusCode, bool hasReceipts, bool hasOpenReceipt)
    {
        var code = Code(statusCode);
        if (hasOpenReceipt) return HasOpenReceiptDelete;
        return code switch
        {
            PurchaseOrderStatuses.Received => DeleteReceived,
            PurchaseOrderStatuses.Cancelled when hasReceipts => DeleteCancelledWithReceipts,
            PurchaseOrderStatuses.Partial => DeleteWithReceipts,
            _ when hasReceipts => DeleteWithReceipts,
            PurchaseOrderStatuses.Draft or PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Cancelled => null,
            _ => DeleteWithReceipts,
        };
    }

    public static bool IsDeletable(string statusCode, bool hasReceipts, bool hasOpenReceipt) => DeleteError(statusCode, hasReceipts, hasOpenReceipt) is null;

    /// <summary>¿Se puede recibir contra la orden? SENT o PARTIAL.</summary>
    public static bool IsReceivable(string statusCode)
        => Code(statusCode) is PurchaseOrderStatuses.Sent or PurchaseOrderStatuses.Partial;

    /// <summary>
    /// Estatus que corresponde a la orden según sus líneas: RECEIVED si ninguna tiene pendiente (ordenado − recibido −
    /// resuelto ≤ 0); PARTIAL en otro caso. Lo usan la recepción y la resolución de faltantes.
    /// </summary>
    public static string TargetStatus(IEnumerable<(decimal Ordered, decimal Received, decimal Resolved)> lines)
        => lines.All(l => ShortageRules.Pending(l.Ordered, l.Received, l.Resolved) <= 0m)
            ? PurchaseOrderStatuses.Received
            : PurchaseOrderStatuses.Partial;

    /// <summary>
    /// Ruta escalonada de estatus desde el actual hasta el destino: SENT → RECEIVED pasa por PARTIAL (si está habilitado);
    /// igual estatus → ruta vacía.
    /// </summary>
    public static IReadOnlyList<string> Path(string currentCode, string targetCode, bool partialEnabled = true)
    {
        var current = Code(currentCode);
        var target = Code(targetCode);
        if (current == target) return Array.Empty<string>();
        if (current == PurchaseOrderStatuses.Sent && target == PurchaseOrderStatuses.Received && partialEnabled)
            return new[] { PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received };
        return new[] { target };
    }

    // ---------------------------------------------------------------- líneas

    /// <summary>
    /// Valida las líneas capturadas (sin BD): al menos una y máximo 200; producto propio y no repetido; cantidad &gt; 0 con
    /// máximo 3 decimales (entera con serie); costo ≥ 0 con máximo 4 decimales o, si se omite, el costo de compra del
    /// producto (sin él → 'Indique el costo unitario de {sku}.'). Errores por 'lines[i]'.
    /// </summary>
    public static (IReadOnlyList<PurchaseOrderLinePlan> Lines, IDictionary<string, string[]> Errors) ValidateLines(IReadOnlyList<PurchaseOrderLineInput>? inputs)
    {
        var errors = new Dictionary<string, string[]>();
        var plans = new List<PurchaseOrderLinePlan>();
        if (inputs is null || inputs.Count == 0)
        {
            errors["lines"] = new[] { LinesRequired };
            return (plans, errors);
        }
        if (inputs.Count > MaxLines)
        {
            errors["lines"] = new[] { TooManyLines };
            return (plans, errors);
        }

        var seen = new HashSet<int>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var l = inputs[i];
            var key = $"lines[{i}]";
            var lineErrors = new List<string>();
            if (!l.IsOwn) lineErrors.Add(OwnProductOnly(l.Sku));
            if (!seen.Add(l.ProductId)) lineErrors.Add(DuplicateProduct(l.Sku));

            if (ValidateQty(l.QtyOrdered, l.IsSerial, l.Sku) is { } qtyError) lineErrors.Add(qtyError);
            var cost = l.UnitCost ?? l.DefaultUnitCost;
            if (cost is null) lineErrors.Add(CostRequired(l.Sku));
            else if (ValidateCost(cost.Value) is { } costError) lineErrors.Add(costError);

            if (lineErrors.Count > 0) errors[key] = lineErrors.ToArray();
            else plans.Add(new PurchaseOrderLinePlan(l.ProductId, l.QtyOrdered!.Value, cost!.Value));
        }
        return (plans, errors);
    }

    /// <summary>Cantidad ordenada: &gt; 0, máximo 3 decimales, entera si el producto se controla por serie.</summary>
    public static string? ValidateQty(decimal? qty, bool isSerial, string sku)
    {
        if (qty is null || qty.Value <= 0m) return QtyRequired;
        if (decimal.Round(qty.Value, 3) != qty.Value) return QtyDecimals;
        if (isSerial && decimal.Truncate(qty.Value) != qty.Value) return SerialQtyInteger(sku);
        return null;
    }

    /// <summary>Costo unitario: ≥ 0 con máximo 4 decimales (DECIMAL(18,4)).</summary>
    public static string? ValidateCost(decimal cost)
    {
        if (cost < 0m) return CostNegative;
        if (decimal.Round(cost, 4) != cost) return CostDecimals;
        return null;
    }

    /// <summary>
    /// Reemplazo completo de líneas (PATCH con lines), emparejadas por producto (no se repiten en la orden). Una línea con
    /// recepciones no se elimina, no baja de lo recibido y no cambia de costo (ReceivedLineLocked); una línea sin recepciones
    /// pero ya usada en un aviso de llegada no se elimina (LineInUse). Errores por 'lines' (líneas que desaparecen) o
    /// 'lines[i]'.
    /// </summary>
    public static (PurchaseOrderLineChanges Changes, IDictionary<string, string[]> Errors) PlanReplacement(
        IReadOnlyList<PurchaseOrderExistingLine> existing, IReadOnlyList<PurchaseOrderLinePlan> requested)
    {
        var errors = new Dictionary<string, string[]>();
        var updates = new List<(int, PurchaseOrderLinePlan)>();
        var additions = new List<PurchaseOrderLinePlan>();
        var removals = new List<int>();
        var byProduct = existing.GroupBy(e => e.ProductId).ToDictionary(g => g.Key, g => g.First());
        var requestedProducts = new HashSet<int>(requested.Select(r => r.ProductId));

        var removalErrors = new List<string>();
        foreach (var e in existing)
        {
            if (requestedProducts.Contains(e.ProductId)) continue;
            if (e.QtyReceived > 0m) removalErrors.Add(ReceivedLineLocked(e.Sku, e.QtyReceived));
            else if (e.IsReferenced) removalErrors.Add(LineInUse(e.Sku));
            else removals.Add(e.PurchaseOrderLineId);
        }
        if (removalErrors.Count > 0) errors["lines"] = removalErrors.ToArray();

        for (var i = 0; i < requested.Count; i++)
        {
            var r = requested[i];
            if (!byProduct.TryGetValue(r.ProductId, out var e))
            {
                additions.Add(r);
                continue;
            }
            if (e.QtyReceived > 0m && (r.QtyOrdered < e.QtyReceived || r.UnitCost != e.UnitCost))
            {
                errors[$"lines[{i}]"] = new[] { ReceivedLineLocked(e.Sku, e.QtyReceived) };
                continue;
            }
            updates.Add((e.PurchaseOrderLineId, r));
        }
        return (new PurchaseOrderLineChanges(updates, additions, removals), errors);
    }

    // ---------------------------------------------------------------- números

    /// <summary>Redondeo monetario a 4 decimales, alejándose de cero (DECIMAL(18,4)).</summary>
    public static decimal Round4(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Importe de la línea (el mismo cálculo que la columna computada LineTotal, que la lógica no lee).</summary>
    public static decimal LineAmount(decimal qtyOrdered, decimal unitCost) => Round4(qtyOrdered * unitCost);

    /// <summary>Total de la orden = Σ importes de línea, con Round4.</summary>
    public static decimal Total(IEnumerable<(decimal QtyOrdered, decimal UnitCost)> lines)
        => Round4(lines.Sum(l => LineAmount(l.QtyOrdered, l.UnitCost)));

    /// <summary>Cantidad en cultura invariante sin ceros de relleno ('2', '1.5').</summary>
    public static string FormatQty(decimal qty) => qty.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Code(string? statusCode) => (statusCode ?? string.Empty).Trim().ToUpperInvariant();
}
