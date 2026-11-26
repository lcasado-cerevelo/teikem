using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Domain.Tenancy;

namespace Teikem.Domain.Sales;

// ============================================================================================================================================================
// Lote 249 (Lote E) — reglas PURAS de la «Modificación de la orden de venta» (sin EF). SalesOrderService (motor) y SalesOrderChangeService (orquestación) cargan el
// estado (ChangeOrderState), piden el plan (SalesChangeRules.Plan) y lo traducen a 400/403/409/422. Los mensajes son públicos: el manual y la FAQ los citan tal cual.
// ============================================================================================================================================================

/// <summary>Estado de una orden para planear una modificación. DeliveryMode = la entrega EFECTIVA (ShippingRules.EffectiveMode: DELIVERY | PICKUP).</summary>
/// <param name="StatusCode">Código de SalesOrderStatus (DRAFT, REQUESTED, PARTIAL, SHIPPED, CANCELLED).</param>
/// <param name="DeliveryMode">Entrega efectiva: con PICKUP lo recolectado ya se lo llevó el cliente (cuenta como salido).</param>
/// <param name="ShippingInvoiced">El cargo de envío ya está en una factura (cuenta como «facturada»).</param>
/// <param name="Lines">Todas las líneas de la orden (también las cerradas y las quitadas).</param>
/// <param name="Batches">Las recolecciones ACTIVAS de la orden (las CANCELLED no cuentan y se ignoran si vienen).</param>
public sealed record ChangeOrderState(
    string StatusCode,
    string DeliveryMode,
    bool ShippingInvoiced,
    IReadOnlyList<ChangeLineState> Lines,
    IReadOnlyList<ChangeBatchState> Batches);

/// <summary>Una línea de la orden con sus cantidades. En un kit las cantidades van en KITS (como en SalesOrderLine).</summary>
public sealed record ChangeLineState(
    int LineId,
    int LineNo,
    int ProductId,
    string Sku,
    decimal QtyOrdered,
    decimal QtyReserved,
    decimal QtyBackordered,
    decimal QtyShipped,
    decimal QtyInvoiced,
    decimal QtyReturned,
    bool IsClosed,
    decimal UnitPrice,
    bool IsKit = false,
    string? TaxReason = null)
{
    /// <summary>Línea quitada por una modificación: pedido 0 y cerrada (nunca DELETE).</summary>
    public bool IsRemoved => QtyOrdered == 0m && IsClosed;
    /// <summary>Tiene IVU manual (TaxReason = MANUAL_OVERRIDE).</summary>
    public bool HasTaxOverride => TaxReason == TaxReasons.ManualOverride;
}

/// <summary>
/// Una recolección activa de la orden. Con maestra (GroupId), el envío y su estado son los DE LA MAESTRA y GroupPacked dice si la maestra está empacada.
/// UnitsByLine = unidades de cada línea de la orden que lleva la recolección (en kits para las líneas de kit).
/// </summary>
/// <param name="ShipmentInvoiced">Flete, cotejo o devolución del envío ya facturados, o con una InvoiceLine viva de ese envío.</param>
/// <param name="TenantLinked">La cuenta una compañía enlazada (Lote 227): cuenta como salida.</param>
/// <param name="IsManual">Despacho manual (DMA): no se deshace ni se reabre desde una modificación.</param>
public sealed record ChangeBatchState(
    int BatchId,
    string Number,
    string StatusCode,
    IReadOnlyDictionary<int, decimal> UnitsByLine,
    int? GroupId = null,
    string? GroupNumber = null,
    bool GroupPacked = false,
    int? ShipmentId = null,
    string? ShipmentNumber = null,
    bool ShipmentActive = false,
    bool ShipmentInitial = false,
    string? ShipmentStatusLabel = null,
    bool ShipmentInvoiced = false,
    bool TenantLinked = false,
    bool IsManual = false)
{
    /// <summary>El número que ve el almacén y el cliente: el de la maestra si la tiene (es el del envío, Lote 176); si no, el de la recolección.</summary>
    public string PackNumber => string.IsNullOrWhiteSpace(GroupNumber) ? Number : GroupNumber!;
    public decimal UnitsOf(int lineId) => UnitsByLine.TryGetValue(lineId, out var u) ? u : 0m;
}

/// <summary>Etapa de una recolección frente a una modificación.</summary>
public static class ChangeBatchStages
{
    /// <summary>Recolectada (sin empacar) de una entrega, sin compañía enlazada: se puede deshacer (el inventario vuelve a su posición y se vuelve a reservar).</summary>
    public const string Reversible = "REVERSIBLE";
    /// <summary>Empacada, con el envío activo y en su etapa inicial, flete sin facturar y sin producto facturado: se puede reabrir (warehouse.reopen-pack).</summary>
    public const string Reopenable = "REOPENABLE";
    /// <summary>Todo lo demás: ya salió (o cuenta como salida: recogido, compañía enlazada, envío avanzado o facturado).</summary>
    public const string Left = "LEFT";
}

/// <summary>Una recolección clasificada y, si no es REVERSIBLE, por qué (texto para la pantalla).</summary>
public sealed record ChangeBatchClassification(int BatchId, string Stage, string? Reason);

/// <summary>Operaciones de una modificación.</summary>
public static class ChangeOpKinds
{
    /// <summary>Agregar un producto (si estaba quitado, su línea se reactiva con el mismo número).</summary>
    public const string Add = "ADD";
    /// <summary>Cambiar la cantidad pedida de una línea.</summary>
    public const string Qty = "QTY";
    /// <summary>Quitar una línea: pedido 0 y cerrada, nunca DELETE.</summary>
    public const string Remove = "REMOVE";
    /// <summary>Cambiar el precio de una línea (distinto del de lista exige sales.price).</summary>
    public const string Price = "PRICE";
    /// <summary>Poner el IVU manual de una línea (porcentaje; exige sales.tax y motivo).</summary>
    public const string Tax = "TAX";
    /// <summary>Quitar el IVU manual de una línea (vuelve el cálculo normal, con la exención del cliente si aplica).</summary>
    public const string ClearTax = "CLEAR_TAX";
    /// <summary>Términos: forma de pago, entrega o recogido, consignatario.</summary>
    public const string Terms = "TERMS";
    /// <summary>Notas de la orden.</summary>
    public const string Notes = "NOTES";
    public static readonly string[] All = { Add, Qty, Remove, Price, Tax, ClearTax, Terms, Notes };
    /// <summary>Las operaciones que apuntan a una línea existente (LineId obligatorio).</summary>
    public static readonly string[] OnLine = { Qty, Remove, Price, Tax, ClearTax };
}

/// <summary>
/// Una operación de la modificación. ADD lleva ProductId y Quantity (Sku solo para los mensajes); QTY lleva LineId y Quantity; REMOVE y CLEAR_TAX, LineId; PRICE,
/// LineId y UnitPrice; TAX, LineId, TaxPct y Note (el motivo); TERMS y NOTES no apuntan a una línea (sus valores los aplica el motor).
/// </summary>
public sealed record ChangeOp(
    string Kind,
    int? LineId = null,
    int? ProductId = null,
    decimal? Quantity = null,
    decimal? UnitPrice = null,
    decimal? TaxPct = null,
    string? Note = null,
    string? Sku = null);

/// <summary>
/// El plan de una modificación (sin escribir nada). Blockers vacío = se puede aplicar.
/// </summary>
/// <param name="ReverseBatchIds">Recolecciones a deshacer COMPLETAS (reversa mínima), en el orden en que se escogieron.</param>
/// <param name="ReopenBatchIds">Empaques sueltos a reabrir (antes de deshacer o de cambiar).</param>
/// <param name="ReopenGroupIds">Maestras empacadas a reabrir (todas sus recolecciones vuelven a recolectadas).</param>
/// <param name="NeedsReopen">El cambio exige reabrir al menos un empaque (aunque no se haya pedido: entonces hay un bloqueo).</param>
/// <param name="NeedsTaxPermission">Hay TAX o CLEAR_TAX: se exige sales.tax.</param>
/// <param name="PriceChangedLineIds">Líneas cuyo precio cambia (el permiso sales.price se revisa contra el precio resuelto hoy).</param>
/// <param name="ReactivatedLineIds">Líneas quitadas que un ADD vuelve a pedir (mismo número de línea).</param>
public sealed record ChangePlan(
    IReadOnlyList<int> ReverseBatchIds,
    IReadOnlyList<int> ReopenBatchIds,
    IReadOnlyList<int> ReopenGroupIds,
    bool NeedsReopen,
    bool NeedsTaxPermission,
    IReadOnlyList<int> PriceChangedLineIds,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<int>? ReactivatedLineIds = null)
{
    public bool IsBlocked => Blockers.Count > 0;
    public IReadOnlyList<int> Reactivated => ReactivatedLineIds ?? Array.Empty<int>();
}

/// <summary>
/// Posición de una línea para la pantalla, antes de enviar. MinQuantity = lo mínimo a lo que se puede bajar SIN reabrir (lo salido + lo empacado);
/// MinQuantityWithReopen = reabriendo (solo lo salido). LockedReason = por qué la línea no admite NINGÚN cambio (null = se puede); PriceLockedReason = por qué no
/// se le puede cambiar el precio ni el IVU (null = se puede).
/// </summary>
public sealed record ChangeLinePosition(
    int LineId,
    int LineNo,
    string Sku,
    decimal QtyOrdered,
    decimal MinQuantity,
    decimal MinQuantityWithReopen,
    decimal InCollected,
    decimal InPacked,
    decimal Out,
    decimal Invoiced,
    bool IsRemoved,
    string? LockedReason,
    string? PriceLockedReason);

/// <summary>La elección de la reversa mínima para una línea.</summary>
/// <param name="BatchIds">Recolecciones NUEVAS que hay que deshacer para esta línea (las ya escogidas por otra línea no se repiten).</param>
/// <param name="Covered">Unidades de la línea que se regresan con todo lo escogido (incluidas las de recolecciones ya escogidas).</param>
/// <param name="Enough">Covered alcanza lo que hacía falta.</param>
/// <param name="CouldCoverWithReopen">Sin reabrir no alcanza, pero reabriendo sí.</param>
public sealed record PickReversalChoice(IReadOnlyList<int> BatchIds, decimal Covered, bool Enough, bool CouldCoverWithReopen);

/// <summary>Ajustes vigentes de las modificaciones para una orden: el cliente manda; NULL en el cliente = hereda de la compañía.</summary>
public sealed record SalesChangeSettings(string ApprovalMode, decimal? ApprovalAmount, bool AttachmentRequired, bool ApprovalFromClient, bool AttachmentFromClient);

/// <summary>Estado de una factura frente a QuickBooks, como lo ve la modificación.</summary>
public static class SalesChangeQuickBooksStates
{
    /// <summary>La factura no está emitida (borrador de una corrida abierta) o está anulada.</summary>
    public const string NotIssued = "NOT_ISSUED";
    /// <summary>Emitida y sin exportar.</summary>
    public const string PendingExport = "PENDING_EXPORT";
    /// <summary>Exportada en un archivo, sin la confirmación «Sí, ya están».</summary>
    public const string ExportedUnconfirmed = "EXPORTED_UNCONFIRMED";
    /// <summary>Confirmada «en QuickBooks».</summary>
    public const string InQuickBooks = "IN_QUICKBOOKS";
    public static readonly string[] All = { NotIssued, PendingExport, ExportedUnconfirmed, InQuickBooks };
}

/// <summary>
/// Lote 249 — reglas puras de las modificaciones de órdenes de venta: etapa de cada recolección, reversa mínima, el plan (qué se puede, qué se deshace, qué se
/// reabre), la posición por línea, la autorización del cliente, la segunda aprobación, el IVU manual y el estado de QuickBooks.
/// </summary>
public static class SalesChangeRules
{
    // ------------------------------------------------------------------------------------------------ límites
    public const int MaxAuthorizedByName = 150;
    public const int MaxAuthorizedByTitle = 100;
    public const int MaxReason = 500;
    public const int MaxTaxNote = 250;
    public const int MaxDecisionNote = 500;
    public const int MaxSummary = 500;
    /// <summary>Holgura para la fecha de la autorización «en el futuro» (relojes desfasados): hasta 5 minutos después de ahora vale (límite incluido).</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    // ------------------------------------------------------------------------------------------------ mensajes (públicos)
    /// <summary>422: la orden tiene algo facturado (una línea con QtyInvoiced &gt; 0 o el envío facturado). Solo admite la orden interna de adición.</summary>
    public const string Billed = "La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.";
    public const string DraftNotModifiable = "La orden está en borrador: se cambia desde su edición, no con una modificación.";
    public const string CancelledNotModifiable = "La orden está cancelada: no se puede modificar.";
    /// <summary>422: todo lo que se iba a despachar ya salió (y nada queda en el almacén): solo se le puede añadir con una orden interna de adición.</summary>
    public const string OrderLeftComplete = "La orden ya salió completa: solo se le puede añadir con una orden interna de adición.";
    public const string NothingToChange = "Indique al menos un cambio.";
    public const string CannotRemoveAll = "No se pueden quitar todas las líneas: para eso cancele la orden.";
    public const string TermsLeft = "Ya salió parte de la orden: el pago, la entrega y el consignatario ya no se cambian.";
    public const string OpKindInvalid = "Operación no válida: use ADD, QTY, REMOVE, PRICE, TAX, CLEAR_TAX, TERMS o NOTES.";
    public const string AddProductRequired = "Indique el producto que se agrega.";
    public const string LineRequired = "Indique la línea que se cambia.";
    public const string PriceRequired = "Indique el precio nuevo de la línea.";
    public const string TermsTwice = "Los términos vienen dos veces en la modificación.";
    public const string NotesTwice = "Las notas vienen dos veces en la modificación.";

    public static string LineNotInOrder(int lineId) => $"La línea {lineId} no es de esta orden.";
    public static string LineRemoved(int lineNo, string sku) => $"La línea {lineNo} ({sku}) está quitada: para volver a pedirla, agréguela de nuevo.";
    public static string LineClosed(int lineNo, string sku) => $"La línea {lineNo} ({sku}) está cerrada: su cantidad ya no se cambia.";
    public static string LineTwice(int lineNo, string sku) => $"La línea {lineNo} ({sku}) viene más de una vez en la modificación.";
    public static string ProductRepeated(string sku) => $"El producto {sku} ya está en la orden: cambie la cantidad de su línea.";
    /// <summary>422: la cantidad nueva queda por debajo de lo que ya salió.</summary>
    public static string BelowLeft(string sku, decimal newQty, decimal left) => $"No se puede bajar {sku} a {Qty(newQty)}: ya salieron {Qty(left)}.";
    public static string RemoveLeft(string sku, decimal left) => $"No se puede quitar {sku}: ya salieron {Qty(left)}.";
    public static string PriceLeft(string sku) => $"El precio de {sku} ya no se cambia: parte de la línea ya salió.";
    public static string TaxLeft(string sku) => $"El IVU de {sku} ya no se cambia: parte de la línea ya salió.";
    public static string NoTaxOverride(int lineNo, string sku) => $"La línea {lineNo} ({sku}) no tiene IVU manual.";
    /// <summary>422: la línea está en un empaque que se puede reabrir y no se marcó «Reabrir el empaque».</summary>
    public static string LineNeedsReopen(string sku, string packNumber)
        => $"{sku} está en el empaque {packNumber}: para cambiarla marque «Reabrir el empaque» (permiso warehouse.reopen-pack).";
    /// <summary>422: los términos cambian con un empaque que se puede reabrir y no se marcó «Reabrir el empaque».</summary>
    public static string TermsNeedReopen(string packNumber)
        => $"La orden está en el empaque {packNumber}: para cambiar el pago, la entrega o el consignatario marque «Reabrir el empaque» (permiso warehouse.reopen-pack).";
    /// <summary>422 (guarda global de la reversa): deshacer la recolección dejaría lo despachado por debajo de lo facturado más lo devuelto.</summary>
    public static string BatchInvoiced(string batchNumber, string invoiceNumbers) => $"La recolección {batchNumber} ya está facturada ({invoiceNumbers}): no se puede deshacer.";

    // por qué una recolección no es REVERSIBLE
    public const string ReasonPickup = "Es un recogido: lo recolectado ya se lo llevó el cliente.";
    public const string ReasonTenantLinked = "La cuenta una compañía enlazada.";
    public const string ReasonManual = "Es un despacho manual.";
    public const string ReasonNoShipment = "El empaque no tiene envío.";
    public const string ReasonProductInvoiced = "Tiene productos ya facturados.";
    public static string ReasonShipmentInactive(string? shipment) => $"El envío {shipment ?? "—"} está anulado.";
    public static string ReasonShipmentAdvanced(string? shipment, string? status)
        => string.IsNullOrWhiteSpace(status) ? $"El envío {shipment ?? "—"} ya pasó de su etapa inicial." : $"El envío {shipment ?? "—"} ya está «{status}».";
    public static string ReasonFreightInvoiced(string? shipment) => $"El flete del envío {shipment ?? "—"} ya está facturado.";
    public static string ReasonStatus(string status) => $"La recolección está «{status}».";

    // autorización del cliente
    public const string AuthorizedByNameRequired = "Indique quién autorizó el cambio en el cliente.";
    public static readonly string AuthorizedByNameTooLong = $"El nombre de quien autorizó no puede pasar de {MaxAuthorizedByName} caracteres.";
    public static readonly string AuthorizedByTitleTooLong = $"El cargo de quien autorizó no puede pasar de {MaxAuthorizedByTitle} caracteres.";
    public const string ChannelRequired = "Indique cómo autorizó el cliente (por teléfono, por correo, en persona o por mensaje).";
    public const string ChannelInvalid = "El canal de autorización no es válido: use PHONE, EMAIL, IN_PERSON o MESSAGE.";
    public const string AuthorizedAtFuture = "La fecha de la autorización no puede ser futura.";
    public const string AuthorizedAtBeforeOrder = "La fecha de la autorización no puede ser anterior a la orden.";
    public const string ReasonRequired = "Indique el motivo de la modificación.";
    public static readonly string ReasonTooLong = $"El motivo de la modificación no puede pasar de {MaxReason} caracteres.";
    public const string AttachmentRequired = "Adjunte la evidencia de la autorización del cliente (captura del correo o del mensaje): es obligatoria para este cliente.";

    // IVU manual
    public const string TaxPctRequired = "Indique el porcentaje de IVU de la línea.";
    public const string TaxPctRange = "El IVU de la línea debe estar entre 0 y 100 %.";
    public const string TaxPctDecimals = "El IVU de la línea admite hasta 4 decimales.";
    public const string TaxNoteRequired = "Indique el motivo del IVU manual.";
    public static readonly string TaxNoteTooLong = $"El motivo del IVU manual no puede pasar de {MaxTaxNote} caracteres.";

    private static string Qty(decimal q) => q.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ facturada

    /// <summary>
    /// «Facturada» = alguna línea con QtyInvoiced &gt; 0 o el envío ya facturado (cuenta también el borrador de una corrida abierta y la orden solo RECOLECTADA ya
    /// facturada con ON_DISPATCH). Facturada = solo admite la orden interna de adición.
    /// </summary>
    public static bool IsBilled(ChangeOrderState s) => s.ShippingInvoiced || s.Lines.Any(l => l.QtyInvoiced > 0m);

    // ------------------------------------------------------------------------------------------------ etapa de las recolecciones

    /// <summary>
    /// Etapa de una recolección: REVERSIBLE (COLLECTED de una entrega, sin compañía enlazada ni despacho manual), REOPENABLE (PACKED con el envío activo y en su
    /// etapa inicial, flete sin facturar y sin producto facturado) o LEFT (todo lo demás, incluidos el recogido y la compañía enlazada).
    /// </summary>
    public static ChangeBatchClassification ClassifyBatch(ChangeOrderState state, ChangeBatchState b)
    {
        if (b.TenantLinked) return new(b.BatchId, ChangeBatchStages.Left, ReasonTenantLinked);
        if (b.IsManual) return new(b.BatchId, ChangeBatchStages.Left, ReasonManual);
        if (b.StatusCode == PickBatchStatuses.Collected)
        {
            if (b.GroupPacked) return ClassifyPacked(state, b);   // una maestra empacada manda sobre el estatus de su recolección
            return state.DeliveryMode == SalesDeliveryModes.Pickup
                ? new(b.BatchId, ChangeBatchStages.Left, ReasonPickup)
                : new(b.BatchId, ChangeBatchStages.Reversible, null);
        }
        if (b.StatusCode == PickBatchStatuses.Packed) return ClassifyPacked(state, b);
        return new(b.BatchId, ChangeBatchStages.Left, ReasonStatus(b.StatusCode));
    }

    private static ChangeBatchClassification ClassifyPacked(ChangeOrderState state, ChangeBatchState b)
    {
        if (b.ShipmentId is null && string.IsNullOrWhiteSpace(b.ShipmentNumber)) return new(b.BatchId, ChangeBatchStages.Left, ReasonNoShipment);
        if (!b.ShipmentActive) return new(b.BatchId, ChangeBatchStages.Left, ReasonShipmentInactive(b.ShipmentNumber));
        if (!b.ShipmentInitial) return new(b.BatchId, ChangeBatchStages.Left, ReasonShipmentAdvanced(b.ShipmentNumber, b.ShipmentStatusLabel));
        if (b.ShipmentInvoiced) return new(b.BatchId, ChangeBatchStages.Left, ReasonFreightInvoiced(b.ShipmentNumber));
        var invoiced = state.Lines.Any(l => l.QtyInvoiced > 0m && b.UnitsOf(l.LineId) > 0m);
        if (invoiced) return new(b.BatchId, ChangeBatchStages.Left, ReasonProductInvoiced);
        return new(b.BatchId, ChangeBatchStages.Reopenable, null);
    }

    /// <summary>Las recolecciones activas de la orden con su etapa (las CANCELLED se ignoran).</summary>
    public static IReadOnlyList<(ChangeBatchState Batch, ChangeBatchClassification Class)> ClassifyAll(ChangeOrderState state)
        => state.Batches.Where(b => b.StatusCode != PickBatchStatuses.Cancelled).Select(b => (b, ClassifyBatch(state, b))).ToList();

    // ------------------------------------------------------------------------------------------------ posición por línea

    /// <summary>Lo que hay de una línea en recolecciones REVERSIBLE, REOPENABLE y lo que ya salió (lo despachado que no se puede deshacer ni reabrir).</summary>
    public static (decimal InCollected, decimal InPacked, decimal Out) Split(ChangeLineState line, IReadOnlyList<(ChangeBatchState Batch, ChangeBatchClassification Class)> classified)
    {
        var inCollected = classified.Where(x => x.Class.Stage == ChangeBatchStages.Reversible).Sum(x => x.Batch.UnitsOf(line.LineId));
        var inPacked = classified.Where(x => x.Class.Stage == ChangeBatchStages.Reopenable).Sum(x => x.Batch.UnitsOf(line.LineId));
        // lo despachado que no está en una recolección deshacible o reabrible cuenta como salido (también lo despachado sin recolección viva)
        var shipped = Math.Max(0m, line.QtyShipped);
        inCollected = Math.Min(inCollected, shipped);
        inPacked = Math.Min(inPacked, shipped - inCollected);
        return (inCollected, inPacked, shipped - inCollected - inPacked);
    }

    /// <summary>
    /// La orden «salió completa»: algo salió y no queda nada en el almacén (ni reservado, ni en backorder abierto, ni recolectado o empacado sin salir). Entonces solo
    /// se le puede añadir con una orden interna de adición.
    /// </summary>
    public static bool LeftComplete(ChangeOrderState state)
    {
        var classified = ClassifyAll(state);
        var anyOut = false;
        foreach (var l in state.Lines)
        {
            if (l.IsRemoved) continue;
            var (inCollected, inPacked, outQty) = Split(l, classified);
            var pending = l.QtyReserved + (l.IsClosed ? 0m : l.QtyBackordered);
            if (pending > 0m || inCollected > 0m || inPacked > 0m) return false;
            if (outQty > 0m) anyOut = true;
        }
        return anyOut;
    }

    /// <summary>El bloqueo de toda la orden (borrador, cancelada, facturada, salió completa); null = la orden admite modificaciones.</summary>
    public static string? OrderBlocker(ChangeOrderState state)
    {
        if (state.StatusCode == SalesOrderStatuses.Draft) return DraftNotModifiable;
        if (state.StatusCode == SalesOrderStatuses.Cancelled) return CancelledNotModifiable;
        if (IsBilled(state)) return Billed;
        if (LeftComplete(state)) return OrderLeftComplete;
        return null;
    }

    /// <summary>
    /// La posición de cada línea (la pantalla la muestra antes de enviar): mínimo sin reabrir (lo salido + lo empacado), mínimo reabriendo (lo salido), lo recolectado,
    /// lo empacado, lo salido, lo facturado y el texto exacto del bloqueo.
    /// </summary>
    public static IReadOnlyList<ChangeLinePosition> LinePositions(ChangeOrderState state)
    {
        var classified = ClassifyAll(state);
        var orderBlocker = OrderBlocker(state);
        return state.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var (inCollected, inPacked, outQty) = Split(l, classified);
            var priceLocked = orderBlocker ?? (l.IsRemoved ? LineRemoved(l.LineNo, l.Sku) : outQty > 0m ? PriceLeft(l.Sku) : null);
            return new ChangeLinePosition(l.LineId, l.LineNo, l.Sku, l.QtyOrdered, outQty + inPacked, outQty, inCollected, inPacked, outQty, l.QtyInvoiced,
                l.IsRemoved, orderBlocker, priceLocked);
        }).ToList();
    }

    // ------------------------------------------------------------------------------------------------ el plan

    /// <summary>
    /// Planea una modificación SIN escribir: qué recolecciones se deshacen (reversa mínima), qué empaques se reabren, qué permisos extra hacen falta y los bloqueos
    /// (texto exacto). reopenRequested = el usuario marcó «Reabrir el empaque» (el permiso warehouse.reopen-pack lo revisa el servicio).
    /// </summary>
    public static ChangePlan Plan(ChangeOrderState state, IReadOnlyList<ChangeOp> ops, bool reopenRequested)
    {
        var blockers = new List<string>();
        var reverse = new List<int>();
        var reverseSet = new HashSet<int>();
        var reopenBatches = new List<int>();
        var reopenGroups = new List<int>();
        var priceChanged = new List<int>();
        var reactivated = new List<int>();
        var needsTax = false;
        var needsReopen = false;

        ChangePlan Result() => new(reverse, reopenBatches, reopenGroups, needsReopen, needsTax, priceChanged, blockers.Distinct().ToList(), reactivated);

        if (ops is null || ops.Count == 0) { blockers.Add(NothingToChange); return Result(); }
        if (OrderBlocker(state) is { } ob) { blockers.Add(ob); return Result(); }

        var classified = ClassifyAll(state);
        var lines = state.Lines.ToDictionary(l => l.LineId);
        var reopenable = classified.Where(x => x.Class.Stage == ChangeBatchStages.Reopenable).Select(x => x.Batch).ToList();

        void MarkReopen(ChangeBatchState b)
        {
            needsReopen = true;
            if (b.GroupId is int g) { if (!reopenGroups.Contains(g)) reopenGroups.Add(g); }
            else if (!reopenBatches.Contains(b.BatchId)) reopenBatches.Add(b.BatchId);
        }

        // la línea está en un empaque reabrible: cambiar su cantidad, precio o IVU exige reabrirlo (el COD y el envío quedarían viejos)
        void RequireReopenFor(ChangeLineState l)
        {
            var packs = reopenable.Where(b => b.UnitsOf(l.LineId) > 0m).ToList();
            if (packs.Count == 0) return;
            foreach (var b in packs) MarkReopen(b);
            if (!reopenRequested) blockers.Add(LineNeedsReopen(l.Sku, packs[0].PackNumber));
        }

        // bajar por debajo de lo despachado: la reversa mínima decide qué recolecciones se deshacen
        void Reduce(ChangeLineState l, decimal newQty, bool removing)
        {
            var (inCollected, inPacked, outQty) = Split(l, classified);
            if (newQty < outQty) { blockers.Add(removing ? RemoveLeft(l.Sku, outQty) : BelowLeft(l.Sku, newQty, outQty)); return; }
            var need = l.QtyShipped - newQty;
            if (need <= 0m) return;
            var choice = PickReversalPlan.Choose(l, need, classified, reopenRequested, reverseSet);
            foreach (var id in choice.BatchIds)
            {
                reverse.Add(id);
                reverseSet.Add(id);
                var b = classified.First(x => x.Batch.BatchId == id);
                if (b.Class.Stage == ChangeBatchStages.Reopenable) MarkReopen(b.Batch);
            }
            if (!choice.Enough)
            {
                if (choice.CouldCoverWithReopen)
                {
                    var pack = reopenable.First(b => b.UnitsOf(l.LineId) > 0m && !reverseSet.Contains(b.BatchId));
                    MarkReopen(pack);
                    blockers.Add(LineNeedsReopen(l.Sku, pack.PackNumber));
                }
                else blockers.Add(removing ? RemoveLeft(l.Sku, outQty) : BelowLeft(l.Sku, newQty, outQty));
            }
        }

        var seenKinds = new Dictionary<int, HashSet<string>>();
        var addedProducts = new HashSet<int>();
        var removedLines = new HashSet<int>();
        var addCount = 0;
        var termsSeen = false;
        var notesSeen = false;

        foreach (var op in ops)
        {
            var kind = op?.Kind?.Trim().ToUpperInvariant();
            if (op is null || kind is null || !ChangeOpKinds.All.Contains(kind)) { blockers.Add(OpKindInvalid); continue; }

            if (kind == ChangeOpKinds.Add)
            {
                if (op.ProductId is not int pid || pid <= 0) { blockers.Add(AddProductRequired); continue; }
                var sku = op.Sku ?? lines.Values.FirstOrDefault(l => l.ProductId == pid)?.Sku ?? pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (SalesRules.QuantityError(op.Quantity) is { } qe) { blockers.Add(qe); continue; }
                if (!addedProducts.Add(pid)) { blockers.Add(ProductRepeated(sku)); continue; }
                var active = lines.Values.FirstOrDefault(l => l.ProductId == pid && !l.IsRemoved);
                if (active is not null) { blockers.Add(ProductRepeated(active.Sku)); continue; }
                var removed = lines.Values.Where(l => l.ProductId == pid && l.IsRemoved).OrderBy(l => l.LineNo).FirstOrDefault();
                if (removed is not null)
                {
                    if (removed.IsKit && decimal.Truncate(op.Quantity!.Value) != op.Quantity.Value) { blockers.Add(SalesRules.KitQuantityWhole(removed.Sku)); continue; }
                    reactivated.Add(removed.LineId);
                }
                addCount++;
                continue;
            }

            if (kind == ChangeOpKinds.Terms)
            {
                if (termsSeen) { blockers.Add(TermsTwice); continue; }
                termsSeen = true;
                if (state.Lines.Any(l => !l.IsRemoved && Split(l, classified).Out > 0m)) { blockers.Add(TermsLeft); continue; }
                if (reopenable.Count > 0)
                {
                    foreach (var b in reopenable) MarkReopen(b);
                    if (!reopenRequested) blockers.Add(TermsNeedReopen(reopenable[0].PackNumber));
                }
                continue;
            }

            if (kind == ChangeOpKinds.Notes)
            {
                if (notesSeen) blockers.Add(NotesTwice);
                notesSeen = true;
                continue;
            }

            // operaciones sobre una línea
            if (op.LineId is not int lineId) { blockers.Add(LineRequired); continue; }
            if (!lines.TryGetValue(lineId, out var line)) { blockers.Add(LineNotInOrder(lineId)); continue; }
            if (!seenKinds.TryGetValue(lineId, out var kinds)) seenKinds[lineId] = kinds = new HashSet<string>();
            if (!kinds.Add(kind) || (kinds.Contains(ChangeOpKinds.Remove) && kinds.Count > 1)
                || (kinds.Contains(ChangeOpKinds.Tax) && kinds.Contains(ChangeOpKinds.ClearTax)))
            { blockers.Add(LineTwice(line.LineNo, line.Sku)); continue; }
            if (line.IsRemoved) { blockers.Add(LineRemoved(line.LineNo, line.Sku)); continue; }
            var split = Split(line, classified);

            switch (kind)
            {
                case ChangeOpKinds.Qty:
                {
                    if (SalesRules.QuantityError(op.Quantity) is { } qe) { blockers.Add(qe); break; }
                    var q = op.Quantity!.Value;
                    if (line.IsKit && decimal.Truncate(q) != q) { blockers.Add(SalesRules.KitQuantityWhole(line.Sku)); break; }
                    if (q == line.QtyOrdered) break;
                    if (line.IsClosed) { blockers.Add(LineClosed(line.LineNo, line.Sku)); break; }
                    RequireReopenFor(line);
                    Reduce(line, q, removing: false);
                    break;
                }
                case ChangeOpKinds.Remove:
                    removedLines.Add(lineId);
                    RequireReopenFor(line);
                    Reduce(line, 0m, removing: true);
                    break;
                case ChangeOpKinds.Price:
                {
                    if (op.UnitPrice is null) { blockers.Add(PriceRequired); break; }
                    if (SalesRules.PriceError(op.UnitPrice) is { } pe) { blockers.Add(pe); break; }
                    if (op.UnitPrice.Value == line.UnitPrice) break;
                    if (split.Out > 0m) { blockers.Add(PriceLeft(line.Sku)); break; }
                    RequireReopenFor(line);
                    priceChanged.Add(lineId);
                    break;
                }
                case ChangeOpKinds.Tax:
                    needsTax = true;
                    if (TaxPctError(op.TaxPct) is { } te) { blockers.Add(te); break; }
                    if (split.Out > 0m) { blockers.Add(TaxLeft(line.Sku)); break; }
                    RequireReopenFor(line);
                    break;
                case ChangeOpKinds.ClearTax:
                    needsTax = true;
                    if (!line.HasTaxOverride) { blockers.Add(NoTaxOverride(line.LineNo, line.Sku)); break; }
                    if (split.Out > 0m) { blockers.Add(TaxLeft(line.Sku)); break; }
                    RequireReopenFor(line);
                    break;
            }
        }

        var remaining = state.Lines.Count(l => !l.IsRemoved && !removedLines.Contains(l.LineId)) + addCount;
        if (remaining == 0) blockers.Add(CannotRemoveAll);
        return Result();
    }

    /// <summary>La línea quitada que un ADD de ese producto reactiva (null = el producto no tenía línea quitada: se agrega una línea nueva).</summary>
    public static ChangeLineState? ReactivationLine(ChangeOrderState state, int productId)
        => state.Lines.Where(l => l.ProductId == productId && l.IsRemoved).OrderBy(l => l.LineNo).FirstOrDefault();

    // ------------------------------------------------------------------------------------------------ autorización del cliente

    /// <summary>
    /// Valida quién autorizó en el cliente y cómo: nombre obligatorio (≤150), cargo opcional (≤100), canal (PHONE | EMAIL | IN_PERSON | MESSAGE), fecha ni futura
    /// (con 5 minutos de holgura, límite incluido) ni anterior a la orden (null = ahora, sin error) y motivo obligatorio (≤500). Null = sin error; si no, el campo y
    /// el mensaje (400).
    /// </summary>
    public static (string Field, string Message)? ValidateAuthorization(string? authorizedByName, string? authorizedByTitle, string? channel,
        DateTime? authorizedAtUtc, string? reason, DateTime nowUtc, DateTime orderCreatedAtUtc)
    {
        var name = authorizedByName?.Trim();
        if (string.IsNullOrEmpty(name)) return ("authorizedByName", AuthorizedByNameRequired);
        if (name.Length > MaxAuthorizedByName) return ("authorizedByName", AuthorizedByNameTooLong);
        var title = authorizedByTitle?.Trim();
        if (!string.IsNullOrEmpty(title) && title.Length > MaxAuthorizedByTitle) return ("authorizedByTitle", AuthorizedByTitleTooLong);
        var ch = channel?.Trim();
        if (string.IsNullOrEmpty(ch)) return ("authorizationChannel", ChannelRequired);
        if (NormalizeChannel(ch) is null) return ("authorizationChannel", ChannelInvalid);
        if (authorizedAtUtc is DateTime at)
        {
            if (at > nowUtc + FutureTolerance) return ("authorizedAtUtc", AuthorizedAtFuture);
            if (at < orderCreatedAtUtc) return ("authorizedAtUtc", AuthorizedAtBeforeOrder);
        }
        var r = reason?.Trim();
        if (string.IsNullOrEmpty(r)) return ("reason", ReasonRequired);
        if (r.Length > MaxReason) return ("reason", ReasonTooLong);
        return null;
    }

    /// <summary>El código del canal normalizado (mayúsculas, sin espacios a los lados); null = no es un canal válido.</summary>
    public static string? NormalizeChannel(string? channel)
    {
        var c = channel?.Trim().ToUpperInvariant();
        return c is not null && SalesChangeChannels.All.Contains(c) ? c : null;
    }

    // ------------------------------------------------------------------------------------------------ segunda aprobación y adjunto

    /// <summary>Ajustes vigentes: el cliente manda (su modo con su monto; su adjunto); lo que el cliente deja en NULL lo hereda de la compañía.</summary>
    public static SalesChangeSettings EffectiveSettings(string? tenantMode, decimal? tenantAmount, bool tenantAttachment,
        string? clientMode, decimal? clientAmount, bool? clientAttachment)
    {
        var fromClient = !string.IsNullOrWhiteSpace(clientMode);
        var mode = fromClient ? clientMode!.Trim().ToUpperInvariant() : (string.IsNullOrWhiteSpace(tenantMode) ? SalesChangeApprovalModes.None : tenantMode.Trim().ToUpperInvariant());
        var amount = fromClient ? clientAmount : tenantAmount;
        if (mode != SalesChangeApprovalModes.AboveAmount) amount = null;
        return new SalesChangeSettings(mode, amount, clientAttachment ?? tenantAttachment, fromClient, clientAttachment is not null);
    }

    /// <summary>Ajustes vigentes a partir de la compañía y (si se tiene) del cliente.</summary>
    public static SalesChangeSettings EffectiveSettings(Tenant tenant, Client? client)
        => EffectiveSettings(tenant.SalesChangeApprovalMode, tenant.SalesChangeApprovalAmount, tenant.SalesChangeAttachmentRequired,
            client?.SalesChangeApprovalMode, client?.SalesChangeApprovalAmount, client?.SalesChangeAttachmentRequired);

    /// <summary>
    /// Si la modificación pide segunda aprobación: NONE nunca; ALWAYS siempre; ABOVE_AMOUNT cuando |total nuevo − total anterior| (con IVU y envío) es ESTRICTAMENTE
    /// mayor que el monto.
    /// </summary>
    public static bool RequiresApproval(string? mode, decimal? amount, decimal totalDelta)
        => (mode?.Trim().ToUpperInvariant()) switch
        {
            SalesChangeApprovalModes.Always => true,
            SalesChangeApprovalModes.AboveAmount => Math.Abs(totalDelta) > (amount ?? 0m),
            _ => false,
        };

    /// <summary>La regla que pidió la aprobación (para SalesOrderChange.ApprovalRule); null = no la pide.</summary>
    public static string? ApprovalRule(SalesChangeSettings settings, decimal totalDelta)
        => RequiresApproval(settings.ApprovalMode, settings.ApprovalAmount, totalDelta) ? settings.ApprovalMode : null;

    // ------------------------------------------------------------------------------------------------ IVU manual

    /// <summary>
    /// Importe del IVU manual de una línea: Money(LineAmount(cantidad, precio) × % / 100), con redondeo de la mitad hacia arriba. Es la misma fórmula del ajuste de
    /// una línea de factura (BillingService.AdjustLineAsync).
    /// </summary>
    public static decimal ManualTax(decimal qty, decimal unitPrice, decimal pct)
        => BillingRules.Money(SalesRules.LineAmount(qty, unitPrice) * pct / 100m);

    /// <summary>Porcentaje del IVU manual: obligatorio, entre 0 y 100, hasta 4 decimales. Null = sin error.</summary>
    public static string? TaxPctError(decimal? pct)
    {
        if (pct is not decimal p) return TaxPctRequired;
        if (p < 0m || p > 100m) return TaxPctRange;
        if (decimal.Round(p, 4) != p) return TaxPctDecimals;
        return null;
    }

    /// <summary>Motivo del IVU manual: obligatorio, hasta 250 caracteres (va a SalesOrderLine.TaxOverrideNote). Null = sin error.</summary>
    public static string? TaxNoteError(string? note)
    {
        var n = note?.Trim();
        if (string.IsNullOrEmpty(n)) return TaxNoteRequired;
        return n.Length > MaxTaxNote ? TaxNoteTooLong : null;
    }

    // ------------------------------------------------------------------------------------------------ QuickBooks

    /// <summary>
    /// Estado de una factura frente a QuickBooks: NOT_ISSUED (sin estatus, borrador o anulada), IN_QUICKBOOKS (confirmada), EXPORTED_UNCONFIRMED (exportada sin
    /// confirmar) o PENDING_EXPORT (emitida y sin exportar).
    /// </summary>
    public static string QuickBooksState(string? invoiceStatus, DateTime? exportedAt, DateTime? quickBooksAt)
    {
        if (string.IsNullOrWhiteSpace(invoiceStatus) || invoiceStatus == InvoiceStatuses.Draft || invoiceStatus == InvoiceStatuses.Void)
            return SalesChangeQuickBooksStates.NotIssued;
        if (quickBooksAt is not null) return SalesChangeQuickBooksStates.InQuickBooks;
        if (exportedAt is not null) return SalesChangeQuickBooksStates.ExportedUnconfirmed;
        return SalesChangeQuickBooksStates.PendingExport;
    }
}

/// <summary>
/// Lote 249 — REVERSA MÍNIMA: para bajar lo despachado de una línea se deshacen recolecciones COMPLETAS, pero solo las necesarias. Primero las REVERSIBLE (de la
/// más reciente a la más vieja: id descendente) y, solo si se pidió reabrir, las REOPENABLE en el mismo orden. Una recolección escogida para una línea también
/// regresa las unidades de las demás líneas que lleva (cuenta para ellas). No hay reversa parcial.
/// </summary>
public static class PickReversalPlan
{
    /// <param name="line">La línea que baja.</param>
    /// <param name="need">Unidades que hay que regresar (QtyShipped − cantidad nueva).</param>
    /// <param name="batches">Las recolecciones clasificadas (SalesChangeRules.ClassifyAll).</param>
    /// <param name="reopenAllowed">Se pidió reabrir: las REOPENABLE también son candidatas.</param>
    /// <param name="alreadyChosen">Recolecciones ya escogidas para otras líneas (cuentan para esta y no se repiten).</param>
    public static PickReversalChoice Choose(ChangeLineState line, decimal need, IReadOnlyList<(ChangeBatchState Batch, ChangeBatchClassification Class)> batches,
        bool reopenAllowed, IReadOnlySet<int>? alreadyChosen = null)
    {
        var chosen = new List<int>();
        var covered = batches.Where(x => alreadyChosen?.Contains(x.Batch.BatchId) == true).Sum(x => x.Batch.UnitsOf(line.LineId));
        if (need <= 0m || covered >= need) return new PickReversalChoice(chosen, covered, true, false);

        IEnumerable<ChangeBatchState> Candidates(string stage) => batches
            .Where(x => x.Class.Stage == stage && x.Batch.UnitsOf(line.LineId) > 0m && alreadyChosen?.Contains(x.Batch.BatchId) != true)
            .Select(x => x.Batch)
            .OrderByDescending(b => b.BatchId);

        foreach (var b in Candidates(ChangeBatchStages.Reversible))
        {
            if (covered >= need) break;
            chosen.Add(b.BatchId);
            covered += b.UnitsOf(line.LineId);
        }
        if (covered < need && reopenAllowed)
            foreach (var b in Candidates(ChangeBatchStages.Reopenable))
            {
                if (covered >= need) break;
                chosen.Add(b.BatchId);
                covered += b.UnitsOf(line.LineId);
            }
        var enough = covered >= need;
        var couldWithReopen = !enough && !reopenAllowed && covered + Candidates(ChangeBatchStages.Reopenable).Sum(b => b.UnitsOf(line.LineId)) >= need;
        return new PickReversalChoice(chosen, covered, enough, couldWithReopen);
    }
}
