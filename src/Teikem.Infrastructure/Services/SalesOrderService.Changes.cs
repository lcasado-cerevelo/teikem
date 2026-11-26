using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

// ============================================================================================================================================================
// Lote 249 (Lote E) — el MOTOR de la «Modificación de la orden de venta». Vive en SalesOrderService (reservas, precios, impuesto, envío y estatus ya están aquí) y lo
// orquesta SalesOrderChangeService, que además hace lo del almacén (reabrir empaques y deshacer recolecciones con PickBatchService / PickGroupService: este
// servicio NO los inyecta, habría un ciclo con ISalesDispatchReversal). Nada de esto se expone por HTTP directamente.
// Orden de candados (igual en todo escritor de la orden): recolecciones activas (id ascendente) → cabecera SalesOrder → líneas (ascendente) → envío → saldos.
// ============================================================================================================================================================

/// <summary>Un producto de las líneas de la orden (para mensajes, fotos y la pantalla).</summary>
public sealed record SalesChangeProductInfo(int ProductId, Guid PublicId, string Sku, string Name, bool IsKit);

/// <summary>Una recolección activa de la orden con sus identificadores públicos (la etapa y las unidades viven en <see cref="ChangeBatchState"/>).</summary>
public sealed record SalesChangeBatchRef(int BatchId, Guid PublicId, string Number, string StatusCode, int? GroupId, Guid? GroupPublicId, string? GroupNumber,
    int? ShipmentId, Guid? ShipmentPublicId, string? ShipmentNumber);

/// <summary>
/// Una factura (viva) de la orden o de una de sus adiciones: número, estatus, estado frente a QuickBooks (SalesChangeQuickBooksStates), fecha, total y de qué orden es.
/// Una factura que cubre varias órdenes de la familia sale una vez por orden.
/// </summary>
public sealed record SalesChangeInvoiceRef(Guid PublicId, string Number, string Status, string QuickBooksState, DateOnly? IssueDate, decimal Total,
    Guid OrderPublicId, string OrderNumber, bool IsAddition);

/// <summary>
/// Lo que la modificación necesita de una orden, cargado de una vez: la orden RASTREADA (con líneas y componentes), su estatus, el estado para las reglas puras
/// (<see cref="ChangeOrderState"/>), los productos, las recolecciones activas, las facturas de la familia, los ajustes vigentes (el cliente manda) y la base de
/// facturación de la compañía.
/// </summary>
public sealed record SalesChangeOrderLoad(
    SalesOrder Order,
    string StatusCode,
    ChangeOrderState State,
    IReadOnlyDictionary<int, SalesChangeProductInfo> Products,
    IReadOnlyList<SalesChangeBatchRef> Batches,
    IReadOnlyList<SalesChangeInvoiceRef> Invoices,
    SalesChangeSettings Settings,
    string BillingTrigger)
{
    /// <summary>Ids de las recolecciones activas, ascendentes (al registrar se comparan con los de la segunda lectura: si cambiaron, 409).</summary>
    public IReadOnlyList<int> ActiveBatchIds => Batches.Select(b => b.BatchId).OrderBy(i => i).ToList();
    /// <summary>Total de la orden hoy (productos, IVU, envío y su IVU).</summary>
    public decimal Total => SalesOrderExtraRules.Total(Order.Subtotal, Order.TaxAmount, Order.ShippingCharge, Order.ShippingTaxAmount);
    /// <summary>Con la facturación ON_REQUEST lo facturado puede pasar de lo despachado: el verificador no exige despachado ≥ facturado + devuelto.</summary>
    public bool ShippedCoversInvoiced => BillingTrigger != Teikem.Domain.Billing.SalesBillingTriggers.OnRequest;
}

/// <summary>
/// Los términos que cambia una operación TERMS (null = no cambia): forma de pago y término (códigos; "" = quitarla), entrega (DELIVERY | PICKUP; "" = se deduce del
/// consignatario), consignatario (ClearConsignee lo quita) y consignación.
/// </summary>
public sealed record SalesChangeTermsInput(string? PaymentMethod = null, string? PaymentTerm = null, string? DeliveryMode = null, Guid? ConsigneePublicId = null,
    bool? ClearConsignee = null, bool? IsConsignment = null);

/// <summary>
/// Lo que aplica el motor: las operaciones (<see cref="ChangeOp"/>; ADD lleva ProductId), el número de la modificación (para el historial «Modificación n.º N»),
/// los términos (si hay TERMS), las notas (si hay NOTES) y si se permite dejar en backorder lo que falte al subir o agregar.
/// </summary>
public sealed record SalesChangeApplyInput(IReadOnlyList<ChangeOp> Ops, int ChangeNo, SalesChangeTermsInput? Terms = null, string? Notes = null, bool AllowBackorder = false);

/// <summary>La foto de antes, la proyectada de después (sin escribir nada) y su diferencia.</summary>
public sealed record SalesChangeProjection(SalesChangeSnapshot Before, SalesChangeSnapshot After, SalesChangeDiff Diff)
{
    public decimal TotalBefore => Before.Total;
    public decimal TotalAfter => After.Total;
    public decimal TotalDelta => After.Total - Before.Total;
}

/// <summary>
/// El crédito frente a una modificación: Checked = el total sube (si baja o queda igual no se revisa); Ok = no excede; Check y Credit = el detalle (para el 422
/// credit_exceeded y la pantalla).
/// </summary>
public sealed record SalesChangeCreditResult(bool Checked, bool Ok, CreditCheck Check, SalesCreditDto Credit);

public sealed partial class SalesOrderService
{
    // ---------------------------------------------------------------- mensajes del motor (públicos: el manual y la FAQ los citan)

    /// <summary>422: el IVU manual de una línea exige el módulo de impuestos encendido.</summary>
    public const string ChangeTaxModuleOff = "El IVU manual exige el módulo de impuestos encendido.";
    /// <summary>400: la operación TERMS no trae ningún término.</summary>
    public const string ChangeTermsRequired = "Indique los términos que cambian (forma de pago, entrega o consignatario).";
    /// <summary>422: cancelar una orden con adiciones abiertas.</summary>
    public static string CancelWithOpenAdditions(string numbers) => $"La orden tiene adiciones abiertas ({numbers}): cancélelas o despáchelas antes de cancelar esta.";
    /// <summary>Comentario del historial de estatus de la orden cuando una modificación la mueve.</summary>
    public static string ChangeHistoryComment(int changeNo) => $"Modificación n.º {changeNo}";

    // ---------------------------------------------------------------- carga y candados

    /// <summary>
    /// Carga la orden RASTREADA (líneas y componentes) y todo lo que la modificación necesita: recolecciones activas con sus unidades por línea, la maestra, el envío
    /// (activo, etapa inicial, flete/cotejo/devolución facturados o con una línea de factura viva), la compañía enlazada, las facturas vivas de la orden y de sus
    /// adiciones con su estado de QuickBooks y los ajustes de la compañía y del cliente. 404 si la orden no es de la compañía.
    /// </summary>
    public async Task<SalesChangeOrderLoad> LoadChangeStateAsync(int orderId, CancellationToken ct)
    {
        var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).ThenInclude(l => l.Components)
            .FirstOrDefaultAsync(o => o.SalesOrderId == orderId, ct) ?? throw NotFound();
        var map = await StatusMapAsync(ct);
        var code = map[order.StatusCodeId].Code;

        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.PublicId, p.Sku, p.Name }).ToListAsync(ct))
            .ToDictionary(p => p.ProductId, p => new SalesChangeProductInfo(p.ProductId, p.PublicId, p.Sku, p.Name,
                order.Lines.Any(l => l.ProductId == p.ProductId && l.Components.Count > 0)));

        var (batchStates, batchRefs) = await BatchesForChangeAsync(order.SalesOrderId, ct);
        var lines = order.Lines.OrderBy(l => l.LineNo).Select(l => new ChangeLineState(l.SalesOrderLineId, l.LineNo, l.ProductId,
            products.TryGetValue(l.ProductId, out var p) ? p.Sku : string.Empty, l.QtyOrdered, l.QtyReserved, l.QtyBackordered, l.QtyShipped, l.QtyInvoiced,
            l.QtyReturned, l.IsClosed, l.UnitPrice, l.Components.Count > 0, l.TaxReason)).ToList();
        var state = new ChangeOrderState(code, ShippingRules.EffectiveMode(order.DeliveryMode, order.ConsigneeId is not null), order.ShippingInvoiced, lines, batchStates);

        var company = await db.Tenants.AsNoTracking().FirstAsync(t => t.TenantId == order.TenantId, ct);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == order.ClientId, ct);
        var settings = SalesChangeRules.EffectiveSettings(company, client);
        var invoices = await FamilyInvoicesAsync(order, ct);
        return new SalesChangeOrderLoad(order, code, state, products, batchRefs, invoices, settings,
            string.IsNullOrWhiteSpace(company.SalesBillingTrigger) ? Teikem.Domain.Billing.SalesBillingTriggers.OnDispatch : company.SalesBillingTrigger);
    }

    /// <summary>
    /// Los candados de la modificación, en el orden del lote: las recolecciones activas de la orden (id ascendente), la cabecera (UPDLOCK) y sus líneas (en una sola
    /// lectura con UPDLOCK: se bloquean en el orden del índice, el mismo para todos). Después vuelve a leer todo (<see cref="LoadChangeStateAsync"/>). Exige una transacción abierta (la de quien llama).
    /// </summary>
    public async Task<SalesChangeOrderLoad> LockForChangeAsync(int orderId, CancellationToken ct)
    {
        var batchIds = await db.Set<PickBatch>().AsNoTracking().Where(b => b.SalesOrderId == orderId && b.IsActive)
            .OrderBy(b => b.PickBatchId).Select(b => b.PickBatchId).ToListAsync(ct);
        foreach (var id in batchIds) await db.LockPickBatchAsync(id, ct);
        await db.LockSalesOrderAsync(orderId, ct);
        if (db.Database.IsRelational())
        {
            var tenantId = db.CurrentTenantId;
            await db.Database.SqlQuery<int>(
                    $"SELECT SalesOrderLineId AS Value FROM dbo.SalesOrderLine WITH (UPDLOCK, ROWLOCK) WHERE SalesOrderId = {orderId} AND TenantId = {tenantId}")
                .ToListAsync(ct);
        }
        return await LoadChangeStateAsync(orderId, ct);
    }

    /// <summary>Las recolecciones activas de la orden como las ven las reglas (etapa, unidades por línea, maestra, envío) y sus identificadores públicos.</summary>
    private async Task<(List<ChangeBatchState> States, List<SalesChangeBatchRef> Refs)> BatchesForChangeAsync(int orderId, CancellationToken ct)
    {
        var batches = await db.Set<PickBatch>().AsNoTracking().Where(b => b.SalesOrderId == orderId && b.IsActive).OrderBy(b => b.PickBatchId).ToListAsync(ct);
        if (batches.Count == 0) return (new List<ChangeBatchState>(), new List<SalesChangeBatchRef>());
        var ids = batches.Select(b => b.PickBatchId).ToList();
        // unidades por línea: en un kit cuenta la porción que lleva los kits (KitQuantity); lo ya revertido no cuenta
        var units = await db.Set<PickBatchLine>().AsNoTracking()
            .Where(l => ids.Contains(l.PickBatchId) && l.SalesOrderLineId != null && l.ReversalTxnId == null)
            .GroupBy(l => new { l.PickBatchId, LineId = l.SalesOrderLineId!.Value })
            .Select(g => new { g.Key.PickBatchId, g.Key.LineId, Units = g.Sum(l => l.KitProductId == null ? l.Quantity : (l.KitQuantity ?? 0m)) })
            .ToListAsync(ct);
        var statusIds = batches.Select(b => b.StatusCodeId).Distinct().ToList();
        var codes = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var groupIds = batches.Where(b => b.PickGroupId is not null).Select(b => b.PickGroupId!.Value).Distinct().ToList();
        var groups = groupIds.Count == 0 ? new Dictionary<int, PickGroup>()
            : await db.Set<PickGroup>().AsNoTracking().Where(g => groupIds.Contains(g.PickGroupId)).ToDictionaryAsync(g => g.PickGroupId, ct);
        int? ShipmentOf(PickBatch b) => b.PickGroupId is int g && groups.TryGetValue(g, out var grp) ? grp.TransportOrderId : b.TransportOrderId;
        var shipmentIds = batches.Select(ShipmentOf).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var shipments = shipmentIds.Count == 0 ? new Dictionary<int, ShipmentInfo>()
            : (await (from o in db.TransportOrders.AsNoTracking()
                      join s in db.StatusCodes.AsNoTracking() on o.StatusCodeId equals s.StatusCodeId
                      where shipmentIds.Contains(o.TransportOrderId)
                      select new { o.TransportOrderId, o.PublicId, o.OrderNumber, o.IsActive, s.IsInitial, s.LabelJson, o.FreightInvoiced, o.ReturnFeeInvoiced, o.InvoiceProcessedAtUtc })
                .ToListAsync(ct))
                .ToDictionary(o => o.TransportOrderId, o => new ShipmentInfo(o.PublicId, o.OrderNumber, o.IsActive, o.IsInitial, MultilingualText.Resolve(o.LabelJson, T.Lang),
                    o.FreightInvoiced || o.ReturnFeeInvoiced || o.InvoiceProcessedAtUtc is not null));
        if (shipmentIds.Count > 0)
        {
            var voidId = await VoidInvoiceStatusIdAsync(ct);
            var billed = await (from l in db.Set<Teikem.Domain.Billing.InvoiceLine>().AsNoTracking()
                                join i in db.Set<Teikem.Domain.Billing.Invoice>().AsNoTracking() on l.InvoiceId equals i.InvoiceId
                                where l.TransportOrderId != null && shipmentIds.Contains(l.TransportOrderId.Value) && i.IsActive && (voidId == null || i.StatusCodeId != voidId)
                                select l.TransportOrderId!.Value).Distinct().ToListAsync(ct);
            foreach (var id in billed)
                if (shipments.TryGetValue(id, out var sh)) shipments[id] = sh with { Invoiced = true };
        }
        var linked = (await db.Set<Teikem.Domain.Integrations.TenantLinkOrder>().AsNoTracking().Where(l => ids.Contains(l.PickBatchId))
            .Select(l => l.PickBatchId).ToListAsync(ct)).ToHashSet();

        var states = new List<ChangeBatchState>();
        var refs = new List<SalesChangeBatchRef>();
        foreach (var b in batches)
        {
            var grp = b.PickGroupId is int g ? groups.GetValueOrDefault(g) : null;
            var shipmentId = ShipmentOf(b);
            var sh = shipmentId is int sid ? shipments.GetValueOrDefault(sid) : null;
            var byLine = units.Where(u => u.PickBatchId == b.PickBatchId).ToDictionary(u => u.LineId, u => u.Units);
            var code = codes.GetValueOrDefault(b.StatusCodeId, string.Empty);
            states.Add(new ChangeBatchState(b.PickBatchId, b.Number, code, byLine, grp?.PickGroupId, grp?.Number, grp?.TransportOrderId is not null,
                shipmentId, sh?.Number, sh?.Active ?? false, sh?.Initial ?? false, sh?.StatusLabel, sh?.Invoiced ?? false, linked.Contains(b.PickBatchId),
                PickBatchRules.IsManual(b.ManualIssueReasonId)));
            refs.Add(new SalesChangeBatchRef(b.PickBatchId, b.PublicId, b.Number, code, grp?.PickGroupId, grp?.PublicId, grp?.Number, shipmentId, sh?.PublicId, sh?.Number));
        }
        return (states, refs);
    }

    private sealed record ShipmentInfo(Guid PublicId, string Number, bool Active, bool Initial, string? StatusLabel, bool Invoiced);

    private async Task<int?> VoidInvoiceStatusIdAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain && s.InternalCode == Teikem.Domain.Billing.InvoiceStatuses.Void)
            .Select(s => (int?)s.StatusCodeId).FirstOrDefaultAsync(ct);

    /// <summary>Las facturas vivas (IsActive) de la orden y de sus adiciones (por sus líneas o por el cargo de envío), con su estado frente a QuickBooks.</summary>
    private async Task<List<SalesChangeInvoiceRef>> FamilyInvoicesAsync(SalesOrder order, CancellationToken ct)
    {
        var family = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.SalesOrderId == order.SalesOrderId || o.ParentSalesOrderId == order.SalesOrderId)
            .Select(o => new { o.SalesOrderId, o.PublicId, o.Number, o.ParentSalesOrderId }).ToListAsync(ct);
        var familyIds = family.Select(o => o.SalesOrderId).ToList();
        var lineOrder = await db.Set<SalesOrderLine>().AsNoTracking().Where(l => familyIds.Contains(l.SalesOrderId))
            .Select(l => new { l.SalesOrderLineId, l.SalesOrderId }).ToDictionaryAsync(l => l.SalesOrderLineId, l => l.SalesOrderId, ct);
        var lineIds = lineOrder.Keys.ToList();
        var rows = await (from l in db.Set<Teikem.Domain.Billing.InvoiceLine>().AsNoTracking()
                          join i in db.Set<Teikem.Domain.Billing.Invoice>().AsNoTracking() on l.InvoiceId equals i.InvoiceId
                          where i.IsActive && ((l.SalesOrderLineId != null && lineIds.Contains(l.SalesOrderLineId.Value))
                                               || (l.SalesOrderId != null && familyIds.Contains(l.SalesOrderId.Value)))
                          select new
                          {
                              i.InvoiceId, i.PublicId, i.InvoiceNumber, i.StatusCodeId, i.IssueDate, i.Subtotal, i.TaxAmount, i.QuickBooksAt, i.QuickBooksExportedAt,
                              l.SalesOrderLineId, l.SalesOrderId,
                          }).ToListAsync(ct);
        if (rows.Count == 0) return new List<SalesChangeInvoiceRef>();
        var statusIds = rows.Select(r => r.StatusCodeId).Distinct().ToList();
        var codes = await db.StatusCodes.AsNoTracking().Where(s => statusIds.Contains(s.StatusCodeId)).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var result = new List<SalesChangeInvoiceRef>();
        var seen = new HashSet<(int InvoiceId, int OrderId)>();
        foreach (var r in rows.OrderBy(r => r.InvoiceId))
        {
            int? orderId = r.SalesOrderLineId is int lid && lineOrder.TryGetValue(lid, out var oid) ? oid : r.SalesOrderId;
            if (orderId is not int fid || !seen.Add((r.InvoiceId, fid))) continue;
            var o = family.First(x => x.SalesOrderId == fid);
            var status = codes.GetValueOrDefault(r.StatusCodeId, string.Empty);
            result.Add(new SalesChangeInvoiceRef(r.PublicId, r.InvoiceNumber, status, SalesChangeRules.QuickBooksState(status, r.QuickBooksExportedAt, r.QuickBooksAt),
                r.IssueDate, Teikem.Domain.Billing.BillingRules.Money(r.Subtotal + r.TaxAmount), o.PublicId, o.Number, o.ParentSalesOrderId is not null));
        }
        return result;
    }

    // ---------------------------------------------------------------- fotos

    /// <summary>La foto de la orden tal como está (líneas, términos y totales) para BeforeJson / AfterJson y la huella.</summary>
    public async Task<SalesChangeSnapshot> SnapshotAsync(SalesOrder order, CancellationToken ct)
    {
        var ids = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = (await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).Select(p => new { p.ProductId, p.Sku, p.Name }).ToListAsync(ct))
            .ToDictionary(p => p.ProductId, p => (p.Sku, p.Name));
        return SalesChangeSnapshot.FromOrder(order, products, await SnapshotTermsAsync(order, ct));
    }

    /// <summary>Los términos de la foto: forma de pago y término (códigos), entrega efectiva, consignatario (con nombre), consignación y notas.</summary>
    private async Task<SalesChangeSnapshotTerms> SnapshotTermsAsync(SalesOrder o, CancellationToken ct)
    {
        var method = o.PaymentMethodLookupId is int m ? (await lookups.GetAsync(m, ct))?.InternalCode : null;
        var term = o.PaymentTermLookupId is int t ? (await lookups.GetAsync(t, ct))?.InternalCode : null;
        var consignee = o.ConsigneeId is int cid
            ? await db.Set<Location>().AsNoTracking().Where(l => l.LocationId == cid).Select(l => l.Name).FirstOrDefaultAsync(ct)
            : null;
        return new SalesChangeSnapshotTerms(method, term, ShippingRules.EffectiveMode(o.DeliveryMode, o.ConsigneeId is not null), o.ConsigneeId, consignee, o.IsConsignment, o.Notes);
    }

    // ---------------------------------------------------------------- permisos de precio

    /// <summary>
    /// 403 «Cambiar el precio exige el permiso de precios (sales.price).» si alguna operación PRICE (o ADD con precio) pone un precio distinto del resuelto HOY para el
    /// cliente de la orden y quien actúa no tiene sales.price. Lo revisa el servicio antes de registrar; el motor lo vuelve a revisar al aplicar.
    /// </summary>
    public async Task EnsureChangePricePermissionAsync(SalesChangeOrderLoad load, IReadOnlyList<ChangeOp> ops, CancellationToken ct)
    {
        var priced = ops.Where(o => o is not null && o.UnitPrice is not null && (Norm(o.Kind) == ChangeOpKinds.Price || Norm(o.Kind) == ChangeOpKinds.Add)).ToList();
        if (priced.Count == 0) return;
        var order = load.Order;
        var productIds = priced.Select(o => Norm(o.Kind) == ChangeOpKinds.Add ? o.ProductId : order.Lines.FirstOrDefault(l => l.SalesOrderLineId == o.LineId)?.ProductId)
            .Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (productIds.Count == 0) return;
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToListAsync(ct);
        var chainId = await Teikem.Infrastructure.Clients.ClientChainQueries.ActiveChainOfClientAsync(db, order.ClientId, ct);
        var resolved = await PriceListQueries.ResolveAsync(db, products, order.ClientId, chainId, clock.Today, ct);
        foreach (var op in priced)
        {
            var pid = Norm(op.Kind) == ChangeOpKinds.Add ? op.ProductId : order.Lines.FirstOrDefault(l => l.SalesOrderLineId == op.LineId)?.ProductId;
            if (pid is not int id || !resolved.TryGetValue(id, out var r)) continue;
            if (Norm(op.Kind) == ChangeOpKinds.Price && order.Lines.FirstOrDefault(l => l.SalesOrderLineId == op.LineId)?.UnitPrice == op.UnitPrice) continue;   // no cambia
            if (PriceListRules.NeedsPricePermission(op.UnitPrice!.Value, r.Price, null) && !await permissions.HasPermissionAsync(PermissionCatalog.SalesPrice, ct))
                throw new ForbiddenException(SalesRules.PriceNeedsPermission);
        }
    }

    private static string? Norm(string? kind) => kind?.Trim().ToUpperInvariant();

    // ---------------------------------------------------------------- proyección (no escribe)

    private sealed class ProjLine
    {
        public int LineId; public int LineNo; public int ProductId; public decimal Qty; public decimal Price; public decimal? Resolved; public string? Source;
        public decimal? ManualPct; public bool Closed; public decimal Reserved; public decimal Back; public decimal Shipped; public decimal Invoiced; public decimal Returned;
        public int? LotId;
        public bool Removed => Qty == 0m && Closed;
    }

    /// <summary>
    /// Proyecta la orden como quedaría con las operaciones, SIN escribir nada (la orden rastreada no se toca): precios resueltos hoy para lo nuevo y lo que cambia de
    /// precio, el IVU calculado (TaxService) o manual (SalesChangeRules.ManualTax), los términos y el envío con la misma regla que al guardar. Devuelve la foto de
    /// antes, la de después (las líneas nuevas con id 0) y la diferencia.
    /// </summary>
    public async Task<SalesChangeProjection> ProjectAsync(SalesChangeOrderLoad load, SalesChangeApplyInput input, CancellationToken ct)
    {
        var order = load.Order;
        var before = await SnapshotAsync(order, ct);
        var rows = order.Lines.OrderBy(l => l.LineNo).Select(l => new ProjLine
        {
            LineId = l.SalesOrderLineId, LineNo = l.LineNo, ProductId = l.ProductId, Qty = l.QtyOrdered, Price = l.UnitPrice, Resolved = l.ResolvedUnitPrice,
            Source = l.PriceSource, ManualPct = l.TaxReason == Teikem.Domain.Tax.TaxReasons.ManualOverride ? l.TaxRatePct : null, Closed = l.IsClosed,
            Reserved = l.QtyReserved, Back = l.QtyBackordered, Shipped = l.QtyShipped, Invoiced = l.QtyInvoiced, Returned = l.QtyReturned, LotId = l.LotId,
        }).ToList();
        var ops = (input.Ops ?? Array.Empty<ChangeOp>()).Where(o => o is not null).ToList();

        // precios de hoy para lo que se agrega y lo que cambia de precio
        var addIds = ops.Where(o => Norm(o.Kind) == ChangeOpKinds.Add && o.ProductId is not null).Select(o => o.ProductId!.Value).ToList();
        var allIds = rows.Select(r => r.ProductId).Concat(addIds).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => allIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);
        var chainId = await Teikem.Infrastructure.Clients.ClientChainQueries.ActiveChainOfClientAsync(db, order.ClientId, ct);
        var resolved = await PriceListQueries.ResolveAsync(db, products.Values, order.ClientId, chainId, clock.Today, ct);
        var nextNo = rows.Count == 0 ? 0 : rows.Max(r => r.LineNo);

        foreach (var op in ops)
        {
            var kind = Norm(op.Kind);
            var row = op.LineId is int lid ? rows.FirstOrDefault(r => r.LineId == lid) : null;
            switch (kind)
            {
                case ChangeOpKinds.Qty when row is not null && op.Quantity is decimal q:
                    row.Qty = q;
                    break;
                case ChangeOpKinds.Remove when row is not null:
                    (row.Qty, row.Closed, row.ManualPct) = (0m, true, null);
                    break;
                case ChangeOpKinds.Price when row is not null && op.UnitPrice is decimal price:
                    if (price == row.Price) break;
                    row.Price = price;
                    if (resolved.TryGetValue(row.ProductId, out var rp)) (row.Resolved, row.Source) = (rp.Price, rp.Source);
                    break;
                case ChangeOpKinds.Tax when row is not null && op.TaxPct is decimal pct:
                    row.ManualPct = pct;
                    break;
                case ChangeOpKinds.ClearTax when row is not null:
                    row.ManualPct = null;
                    break;
                case ChangeOpKinds.Add when op.ProductId is int pid && op.Quantity is decimal aq:
                {
                    if (!products.TryGetValue(pid, out var product)) throw new NotFoundException("Producto");
                    var r = resolved.GetValueOrDefault(pid);
                    var price = op.UnitPrice ?? r?.Price ?? throw new ValidationException("ops", SalesRules.NoPrice(product.Sku));
                    var removed = rows.Where(x => x.ProductId == pid && x.Removed).OrderBy(x => x.LineNo).FirstOrDefault();
                    if (removed is not null)
                        (removed.Qty, removed.Closed, removed.Price, removed.Resolved, removed.Source, removed.ManualPct) = (aq, false, price, r?.Price, r?.Source, null);
                    else
                        rows.Add(new ProjLine { LineId = 0, LineNo = ++nextNo, ProductId = pid, Qty = aq, Price = price, Resolved = r?.Price, Source = r?.Source });
                    break;
                }
            }
        }

        // impuesto: calculado para todas (lo que el sistema cobraría), manual donde lo hay
        var priced = await PriceAsync(rows.Select(r => new PlannedLine(products[r.ProductId], r.Qty, r.Price, r.LotId)).ToList(), order.ClientId, order.RequestedDate, ct);
        var snapLines = new List<SalesChangeSnapshotLine>();
        decimal subtotal = 0m, taxTotal = 0m;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var p = priced[i];
            var manual = r.ManualPct is not null;
            var pct = manual ? r.ManualPct!.Value : p.TaxRatePct;
            var amount = manual ? SalesChangeRules.ManualTax(r.Qty, r.Price, pct) : p.TaxAmount;
            subtotal += p.Subtotal;
            taxTotal += amount;
            var prod = products[r.ProductId];
            snapLines.Add(new SalesChangeSnapshotLine(r.LineId, r.LineNo, r.ProductId, prod.Sku, prod.Name, r.Qty, r.Reserved, r.Back, r.Shipped, r.Invoiced, r.Returned,
                r.Price, r.Resolved, r.Source, pct, amount, manual ? Teikem.Domain.Tax.TaxReasons.ManualOverride : p.Reason,
                manual ? p.TaxRatePct : null, manual ? p.TaxAmount : null, r.Closed, r.Removed));
        }

        // términos y envío sobre una copia SIN rastrear de la orden (la regla de envío es la misma que al guardar)
        var draft = new SalesOrder
        {
            SalesOrderId = order.SalesOrderId, TenantId = order.TenantId, ClientId = order.ClientId, WarehouseId = order.WarehouseId, DeliveryMode = order.DeliveryMode,
            ConsigneeId = order.ConsigneeId, PaymentMethodLookupId = order.PaymentMethodLookupId, PaymentTermLookupId = order.PaymentTermLookupId,
            PaymentChanged = order.PaymentChanged, IsConsignment = order.IsConsignment, Notes = order.Notes, ShippingInvoiced = order.ShippingInvoiced,
            ShippingCharge = order.ShippingCharge, ShippingTaxAmount = order.ShippingTaxAmount, ShippingReasonLookupId = order.ShippingReasonLookupId,
            Subtotal = subtotal, TaxAmount = taxTotal,
        };
        if (ops.Any(o => Norm(o.Kind) == ChangeOpKinds.Terms)) await ApplyTermsInputAsync(draft, order, input.Terms, ct);
        if (ops.Any(o => Norm(o.Kind) == ChangeOpKinds.Notes)) draft.Notes = NormalizedNotes(input.Notes);
        await RecalculateShippingAsync(draft, ct);
        var after = SalesChangeSnapshot.Create(snapLines, await SnapshotTermsAsync(draft, ct), subtotal, taxTotal, draft.ShippingCharge, draft.ShippingTaxAmount);
        return new SalesChangeProjection(before, after, SalesChangeSnapshot.Diff(before, after));
    }

    private static string? NormalizedNotes(string? notes)
    {
        var (n, e) = SalesRules.NormalizeNotes(notes);
        if (e is not null) throw new ValidationException("notes", e);
        return n;
    }

    /// <summary>Valida y aplica los términos de una operación TERMS sobre <paramref name="target"/> (la orden rastreada o la copia de la proyección).</summary>
    private async Task ApplyTermsInputAsync(SalesOrder target, SalesOrder current, SalesChangeTermsInput? terms, CancellationToken ct)
    {
        if (terms is null || (terms.PaymentMethod is null && terms.PaymentTerm is null && terms.DeliveryMode is null && terms.ConsigneePublicId is null
                              && terms.ClearConsignee != true && terms.IsConsignment is null))
            throw new ValidationException("terms", ChangeTermsRequired);
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == current.ClientId, ct);
        var plan = await PlanTermsAsync(new SalesOrderRequest(PaymentMethod: terms.PaymentMethod, PaymentTerm: terms.PaymentTerm, DeliveryMode: terms.DeliveryMode,
            IsConsignment: terms.IsConsignment), client, current, false, ct);
        ApplyTerms(target, plan);
        if (terms.ClearConsignee == true) target.ConsigneeId = null;
        else if (terms.ConsigneePublicId is not null) target.ConsigneeId = await ResolveConsigneeAsync(terms.ConsigneePublicId, ct);
    }

    // ---------------------------------------------------------------- aplicar

    /// <summary>
    /// Aplica las operaciones a la orden, DESPUÉS de las reversas y reaperturas que hizo el servicio (en su misma transacción, con los candados ya tomados). Vuelve a
    /// leer la orden rastreada (las reversas limpian el contexto). ADD: valida y resuelve el precio de hoy (PlanLinesAsync: producto activo y propio, sin serie, kit
    /// entero con su foto, oferta vigente; sales.price si el precio es distinto), numera desde el máximo + 1 sin renumerar (un producto quitado reactiva su línea) y
    /// reserva. QTY: reserva o libera. REMOVE: libera todo y deja pedido 0 y cerrada (nunca DELETE). PRICE: el origen resuelto hoy. TAX / CLEAR_TAX: IVU manual
    /// (módulo TAX; sales.tax). TERMS y NOTES. Al final recalcula el impuesto, el envío y el estatus («Modificación n.º N») y corre el verificador de invariantes: si
    /// el cambio deja una línea incoherente, 422 y no se aplica nada (la transacción se revierte).
    /// </summary>
    public async Task<SalesOrder> ApplyOpsAsync(int orderId, SalesChangeApplyInput input, CancellationToken ct)
    {
        if (input is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var order = await db.Set<SalesOrder>().AsTracking().Include(o => o.Lines).ThenInclude(l => l.Components)
            .FirstOrDefaultAsync(o => o.SalesOrderId == orderId, ct) ?? throw NotFound();
        var code = (await StatusMapAsync(ct))[order.StatusCodeId].Code;
        if (code == SalesOrderStatuses.Draft) throw new StatusRuleException(SalesChangeRules.DraftNotModifiable);
        if (code == SalesOrderStatuses.Cancelled) throw new StatusRuleException(SalesChangeRules.CancelledNotModifiable);
        var trigger = await db.Tenants.AsNoTracking().Where(t => t.TenantId == order.TenantId).Select(t => t.SalesBillingTrigger).FirstOrDefaultAsync(ct);
        var before = InvariantOrder.From(order);
        var ops = (input.Ops ?? Array.Empty<ChangeOp>()).Where(o => o is not null).Select(o => o with { Kind = Norm(o.Kind) ?? string.Empty }).ToList();
        if (ops.Count == 0) throw new ValidationException("ops", SalesChangeRules.NothingToChange);
        if (ops.Any(o => !ChangeOpKinds.All.Contains(o.Kind))) throw new ValidationException("ops", SalesChangeRules.OpKindInvalid);

        if (ops.Any(o => o.Kind is ChangeOpKinds.Tax or ChangeOpKinds.ClearTax))
        {
            if (!await modules.IsEnabledAsync(ModuleKeys.Tax, ct)) throw new StatusRuleException(ChangeTaxModuleOff);
            await permissions.EnsureAsync(PermissionCatalog.SalesTax, ct);
        }
        var skus = await db.Set<Product>().AsNoTracking().Where(p => order.Lines.Select(l => l.ProductId).Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
        string Sku(SalesOrderLine l) => skus.GetValueOrDefault(l.ProductId, string.Empty);
        SalesOrderLine LineOf(ChangeOp op)
        {
            if (op.LineId is not int lid) throw new ValidationException("ops", SalesChangeRules.LineRequired);
            var line = order.Lines.FirstOrDefault(l => l.SalesOrderLineId == lid) ?? throw new ValidationException("ops", SalesChangeRules.LineNotInOrder(lid));
            if (line.QtyOrdered == 0m && line.IsClosed) throw new StatusRuleException(SalesChangeRules.LineRemoved(line.LineNo, Sku(line)));
            return line;
        }

        // 1) quitar y bajar primero (libera reservas que pueden servir para subir o agregar)
        foreach (var op in ops.Where(o => o.Kind == ChangeOpKinds.Remove))
        {
            var line = LineOf(op);
            if (line.QtyShipped > 0m) throw new StatusRuleException(SalesChangeRules.RemoveLeft(Sku(line), line.QtyShipped));
            var less = line.QtyReserved + line.QtyBackordered;
            if (less > 0m) await DecreaseAsync(order, line, less, ct);
            (line.QtyOrdered, line.IsClosed, line.QtyBackordered) = (0m, true, 0m);
            ClearManualTax(line);
        }
        var qtyOps = ops.Where(o => o.Kind == ChangeOpKinds.Qty).Select(o => (Op: o, Line: LineOf(o))).ToList();
        foreach (var (op, line) in qtyOps)
        {
            if (SalesRules.QuantityError(op.Quantity) is { } qe) throw new ValidationException("quantity", qe);
            var q = op.Quantity!.Value;
            if (line.Components.Count > 0 && decimal.Truncate(q) != q) throw new ValidationException("quantity", SalesRules.KitQuantityWhole(Sku(line)));
            if (q >= line.QtyOrdered) continue;
            if (line.IsClosed) throw new StatusRuleException(SalesChangeRules.LineClosed(line.LineNo, Sku(line)));
            if (q < line.QtyShipped) throw new StatusRuleException(SalesChangeRules.BelowLeft(Sku(line), q, line.QtyShipped));
            await DecreaseAsync(order, line, line.QtyOrdered - q, ct);
            line.QtyOrdered = q;
        }
        foreach (var (op, line) in qtyOps.Where(x => x.Op.Quantity > x.Line.QtyOrdered))
        {
            if (line.IsClosed) throw new StatusRuleException(SalesChangeRules.LineClosed(line.LineNo, Sku(line)));
            var q = op.Quantity!.Value;
            await IncreaseAsync(order, line, q - line.QtyOrdered, input.AllowBackorder, ct);
            line.QtyOrdered = q;
        }

        // 2) precio (el origen resuelto hoy; distinto del resuelto exige sales.price)
        var priceOps = ops.Where(o => o.Kind == ChangeOpKinds.Price).ToList();
        if (priceOps.Count > 0)
        {
            var chainId = await Teikem.Infrastructure.Clients.ClientChainQueries.ActiveChainOfClientAsync(db, order.ClientId, ct);
            bool? canPrice = null;
            foreach (var op in priceOps)
            {
                var line = LineOf(op);
                if (op.UnitPrice is null) throw new ValidationException("unitPrice", SalesChangeRules.PriceRequired);
                if (SalesRules.PriceError(op.UnitPrice) is { } pe) throw new ValidationException("unitPrice", pe);
                var price = op.UnitPrice.Value;
                if (price == line.UnitPrice) continue;
                var product = await db.Set<Product>().AsNoTracking().FirstAsync(p => p.ProductId == line.ProductId, ct);
                var today = (await PriceListQueries.ResolveAsync(db, new[] { product }, order.ClientId, chainId, clock.Today, ct))[product.ProductId];
                if (PriceListRules.NeedsPricePermission(price, today.Price, null))
                {
                    canPrice ??= await permissions.HasPermissionAsync(PermissionCatalog.SalesPrice, ct);
                    if (canPrice != true) throw new ForbiddenException(SalesRules.PriceNeedsPermission);
                }
                line.UnitPrice = price;
                SetPriceOrigin(line, PriceOriginRules.Copy(today, chainId));
            }
        }

        // 3) agregar (líneas nuevas desde el máximo + 1; un producto quitado reactiva su línea con el mismo número)
        var addOps = ops.Where(o => o.Kind == ChangeOpKinds.Add).ToList();
        if (addOps.Count > 0) await AddLinesAsync(order, addOps, input.AllowBackorder, ct);

        // 4) IVU manual de la línea (solo esa línea: la exención del cliente sigue para las demás)
        foreach (var op in ops.Where(o => o.Kind == ChangeOpKinds.Tax))
        {
            var line = LineOf(op);
            if (SalesChangeRules.TaxPctError(op.TaxPct) is { } te) throw new ValidationException("taxPct", te);
            if (SalesChangeRules.TaxNoteError(op.Note) is { } ne) throw new ValidationException("note", ne);
            if (line.TaxReason != Teikem.Domain.Tax.TaxReasons.ManualOverride)
                (line.TaxComputedRatePct, line.TaxComputedAmount) = (line.TaxRatePct, line.TaxAmount);   // provisional: el recálculo pone lo de hoy
            line.TaxReason = Teikem.Domain.Tax.TaxReasons.ManualOverride;
            line.TaxRatePct = op.TaxPct!.Value;
            line.TaxOverrideNote = op.Note!.Trim();
            line.TaxOverrideBy = tenant.UserId;
            line.TaxOverrideAtUtc = DateTime.UtcNow;
        }
        foreach (var op in ops.Where(o => o.Kind == ChangeOpKinds.ClearTax))
        {
            var line = LineOf(op);
            if (line.TaxReason != Teikem.Domain.Tax.TaxReasons.ManualOverride) throw new StatusRuleException(SalesChangeRules.NoTaxOverride(line.LineNo, Sku(line)));
            ClearManualTax(line);
        }

        // 5) términos y notas
        if (ops.Any(o => o.Kind == ChangeOpKinds.Terms)) await ApplyTermsInputAsync(order, order, input.Terms, ct);
        if (ops.Any(o => o.Kind == ChangeOpKinds.Notes)) order.Notes = NormalizedNotes(input.Notes);

        // 6) impuesto, envío y estatus; el verificador decide si se guarda
        await RecalculateTaxAsync(order, ct);
        await RecalculateShippingAsync(order, ct);
        await RefreshAfterChangeAsync(order, input.ChangeNo, ct);
        var violations = SalesLineInvariants.Check(before, InvariantOrder.From(order), trigger != Teikem.Domain.Billing.SalesBillingTriggers.OnRequest);
        if (violations.Count > 0)
        {
            var first = violations.FirstOrDefault(v => v.LineNo > 0) ?? violations[0];
            throw new StatusRuleException(SalesLineInvariants.IncoherentMessage(first.LineNo));
        }
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);
        return order;
    }

    /// <summary>Quita el IVU manual de la línea (el recálculo le vuelve a poner el normal, con la exención del cliente si aplica).</summary>
    private static void ClearManualTax(SalesOrderLine line)
    {
        if (line.TaxReason == Teikem.Domain.Tax.TaxReasons.ManualOverride) line.TaxReason = null;
        (line.TaxOverrideNote, line.TaxOverrideBy, line.TaxOverrideAtUtc, line.TaxComputedRatePct, line.TaxComputedAmount) = (null, null, null, null, null);
    }

    /// <summary>
    /// ADD: valida los productos con las reglas del alta (PlanLinesAsync) y el precio resuelto hoy; un producto que ya tiene línea activa → 400; uno con línea
    /// quitada la reactiva (mismo número, misma foto de kit). Las nuevas se numeran desde el máximo + 1 y se guardan ANTES de reservar (las reservas cuelgan del id).
    /// </summary>
    private async Task AddLinesAsync(SalesOrder order, IReadOnlyList<ChangeOp> addOps, bool allowBackorder, CancellationToken ct)
    {
        var ids = addOps.Select(o => o.ProductId ?? throw new ValidationException("ops", SalesChangeRules.AddProductRequired)).ToList();
        var publicIds = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.PublicId, ct);
        var requests = addOps.Select(o => new SalesOrderLineRequest(publicIds.TryGetValue(o.ProductId!.Value, out var pub) ? pub : throw new NotFoundException("Producto"),
            o.Quantity, o.UnitPrice)).ToList();
        var planned = await PlanLinesAsync(requests, order.ClientId, clock.Today, null, ct);
        var skus = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
        var nextNo = order.Lines.Count == 0 ? 0 : order.Lines.Max(l => l.LineNo);
        var toReserve = new List<(SalesOrderLine Line, decimal Qty)>();
        foreach (var p in planned)
        {
            var pid = p.Product.ProductId;
            if (order.Lines.Any(l => l.ProductId == pid && !(l.QtyOrdered == 0m && l.IsClosed)))
                throw new ValidationException("ops", SalesChangeRules.ProductRepeated(skus.GetValueOrDefault(pid, p.Product.Sku)));
            var removed = order.Lines.Where(l => l.ProductId == pid && l.QtyOrdered == 0m && l.IsClosed).OrderBy(l => l.LineNo).FirstOrDefault();
            if (removed is not null)
            {
                if (removed.Components.Count > 0 && decimal.Truncate(p.Quantity) != p.Quantity) throw new ValidationException("quantity", SalesRules.KitQuantityWhole(p.Product.Sku));
                (removed.QtyOrdered, removed.IsClosed, removed.UnitPrice, removed.QtyBackordered) = (p.Quantity, false, p.UnitPrice, 0m);
                removed.LotId = p.LotId ?? removed.LotId;
                if (p.Resolved is { } r) SetPriceOrigin(removed, PriceOriginRules.Copy(r, p.ChainId));
                ClearManualTax(removed);
                toReserve.Add((removed, p.Quantity));
                continue;
            }
            var line = new SalesOrderLine
            {
                TenantId = order.TenantId, SalesOrderId = order.SalesOrderId, LineNo = ++nextNo, ProductId = pid, LotId = p.LotId, QtyOrdered = p.Quantity, UnitPrice = p.UnitPrice,
            };
            if (p.Resolved is { } resolved) SetPriceOrigin(line, PriceOriginRules.Copy(resolved, p.ChainId));
            foreach (var c in p.Components ?? Array.Empty<(int ProductId, decimal Quantity)>())
                line.Components.Add(new SalesOrderLineComponent { TenantId = order.TenantId, ComponentProductId = c.ProductId, Quantity = c.Quantity,
                    SubstitutedProductId = p.Substituted is not null && p.Substituted.TryGetValue(c.ProductId, out var original) ? original : null });
            order.Lines.Add(line);
            toReserve.Add((line, p.Quantity));
        }
        await db.SaveGuardedAsync(SalesRules.Concurrency, ct);   // las reservas cuelgan del id de la línea
        foreach (var (line, qty) in toReserve)
        {
            line.QtyReserved = 0m;
            await IncreaseAsync(order, line, qty, allowBackorder, ct);
        }
    }

    // ---------------------------------------------------------------- estatus

    /// <summary>
    /// El estatus de la orden después de una modificación, siempre por StatusService con el comentario «Modificación n.º N»: una SHIPPED con algo pendiente vuelve a
    /// PARTIAL (o a REQUESTED si no quedó nada despachado); una PARTIAL sin nada despachado vuelve a REQUESTED (como la reversa del Lote 62); y una REQUESTED/PARTIAL
    /// con todo terminado pasa a SHIPPED (RefreshStatusAsync).
    /// </summary>
    public async Task RefreshAfterChangeAsync(SalesOrder order, int changeNo, CancellationToken ct)
    {
        var comment = ChangeHistoryComment(changeNo);
        var code = (await StatusMapAsync(ct))[order.StatusCodeId].Code;
        var anyShipped = order.Lines.Any(l => l.QtyShipped > 0m);
        var anyPending = order.Lines.Any(l => l.QtyReserved > 0m || (!l.IsClosed && l.QtyBackordered > 0m));
        string? target = null;
        if (code == SalesOrderStatuses.Shipped && anyPending) target = anyShipped ? SalesOrderStatuses.Partial : SalesOrderStatuses.Requested;
        else if (code == SalesOrderStatuses.Partial && !anyShipped && anyPending) target = SalesOrderStatuses.Requested;
        if (target is not null)
        {
            var to = await statuses.TransitionAsync(SalesCodes.StatusDomain, SalesCodes.EntityType, order.SalesOrderId, order.StatusCodeId, target, comment, ct);
            order.StatusCodeId = to.StatusCodeId;
        }
        await RefreshStatusAsync(order, comment, ct);
    }

    // ---------------------------------------------------------------- crédito

    /// <summary>
    /// El crédito frente a la modificación: solo se revisa si el total SUBE (con el total nuevo como «esta orden» y la exposición de las demás órdenes del cliente).
    /// Excedido y <paramref name="notifyIfExceeded"/> = se avisa a quien autoriza (CREDIT_OVERRIDE_NEEDED; el aviso se guarda solo, fuera de la transacción de
    /// negocio: llámese en la pasada sin transacción).
    /// </summary>
    public async Task<SalesChangeCreditResult> EvaluateCreditForChangeAsync(SalesOrder order, decimal totalBefore, decimal totalAfter, bool notifyIfExceeded, CancellationToken ct)
    {
        var probe = new SalesOrder
        {
            SalesOrderId = order.SalesOrderId, PublicId = order.PublicId, Number = order.Number, TenantId = order.TenantId, ClientId = order.ClientId,
            WarehouseId = order.WarehouseId, Subtotal = totalAfter,
        };
        var (check, dto) = await EvaluateCreditAsync(probe, ct);
        if (totalAfter <= totalBefore) return new SalesChangeCreditResult(false, true, check, dto);
        var ok = CreditRules.Evaluate(check).Ok;
        if (!ok && notifyIfExceeded) await NotifyCreditNeededAsync(probe, check, ct);
        return new SalesChangeCreditResult(true, ok, check, dto);
    }

    // ---------------------------------------------------------------- cancelar: la pendiente se retira

    /// <summary>Al cancelar la orden, su modificación pendiente de aprobar (si hay) pasa a WITHDRAWN con «La orden se canceló» (en la transacción de cancelar).</summary>
    public async Task WithdrawPendingOnCancelAsync(SalesOrder order, CancellationToken ct)
    {
        var pending = await db.Set<SalesOrderChange>().AsTracking().Where(c => c.SalesOrderId == order.SalesOrderId && c.IsPending).ToListAsync(ct);
        foreach (var change in pending)
        {
            const string note = "La orden se canceló";
            var to = await statuses.TransitionAsync(SalesChangeCodes.StatusDomain, SalesChangeCodes.EntityType, change.SalesOrderChangeId, change.StatusCodeId,
                SalesOrderChangeStatuses.Withdrawn, note, ct);
            change.StatusCodeId = to.StatusCodeId;
            change.IsPending = false;
            change.DecidedAtUtc = DateTime.UtcNow;
            change.DecidedBy = tenant.UserId;
            change.DecisionNote = note;
        }
    }

    // ---------------------------------------------------------------- aviso

    /// <summary>
    /// «Orden de venta modificada» (SALES_ORDER_CHANGED) en la transacción de quien aplica: una vez por modificación (clave CHG-{modificación}). Lleva el resumen, los
    /// totales, quién registró, quién autorizó y cómo, el motivo, quién aprobó (si pidió aprobación) y el impacto en el almacén (recolecciones deshechas y empaques
    /// reabiertos; <see cref="WarehouseImpactText"/>). Sin publicador no hace nada.
    /// </summary>
    public async Task NotifyChangedAsync(SalesOrder order, SalesOrderChange change, string? warehouseImpact, string? approvedByName, CancellationToken ct)
    {
        if (notifications is null) return;
        var values = await ChangeNoticeValuesAsync(change, ct);
        values["approvedBy"] = approvedByName ?? string.Empty;
        values["warehouseImpact"] = warehouseImpact ?? string.Empty;
        await NotifyOrderAsync(Teikem.Infrastructure.Notifications.SalesChangeEventCodes.OrderChanged, order, values, $"CHG-{change.PublicId:N}", ct);
    }

    /// <summary>Las variables comunes de los avisos de una modificación (número, quién registró, quién autorizó y cómo, motivo, resumen y totales).</summary>
    public async Task<Dictionary<string, string?>> ChangeNoticeValuesAsync(SalesOrderChange change, CancellationToken ct)
    {
        var registeredBy = (await UserNamesAsync(new[] { change.RegisteredBy }, ct)).GetValueOrDefault(change.RegisteredBy) ?? string.Empty;
        var channel = await lookups.GetAsync(change.AuthorizationChannelLookupId, ct);
        var authorized = string.IsNullOrWhiteSpace(change.AuthorizedByTitle) ? change.AuthorizedByName : $"{change.AuthorizedByName} ({change.AuthorizedByTitle})";
        return new Dictionary<string, string?>
        {
            ["changeNo"] = change.ChangeNo.ToString(CultureInfo.InvariantCulture),
            ["changePublicId"] = change.PublicId.ToString("N"),
            ["registeredBy"] = registeredBy,
            ["authorizedBy"] = authorized,
            ["channel"] = channel is null ? string.Empty : MultilingualText.Resolve(channel.LabelJson, T.Lang) ?? channel.InternalCode,
            ["reason"] = change.Reason,
            ["summary"] = change.Summary ?? string.Empty,
            ["totalBefore"] = Money(change.TotalBefore),
            ["totalAfter"] = Money(change.TotalAfter),
            ["totalDelta"] = (change.TotalDelta >= 0m ? "+" : "-") + Money(Math.Abs(change.TotalDelta)),
        };
    }

    private static string Money(decimal v) => "$" + v.ToString("#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>El impacto en el almacén para el aviso: qué recolecciones se deshicieron y qué empaques se reabrieron («Sin cambios en el almacén.» si nada).</summary>
    public static string WarehouseImpactText(IReadOnlyCollection<string> reversedBatchNumbers, IReadOnlyCollection<string> reopenedPackNumbers)
    {
        var parts = new List<string>();
        if (reopenedPackNumbers.Count > 0)
            parts.Add((reopenedPackNumbers.Count == 1 ? "Se reabrió el empaque " : "Se reabrieron los empaques ") + string.Join(", ", reopenedPackNumbers));
        if (reversedBatchNumbers.Count > 0)
            parts.Add((reversedBatchNumbers.Count == 1 ? "se deshizo la recolección " : "se deshicieron las recolecciones ") + string.Join(", ", reversedBatchNumbers)
                      + " (el almacén vuelve a despachar)");
        if (parts.Count == 0) return "Sin cambios en el almacén.";
        var text = string.Join("; ", parts) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
