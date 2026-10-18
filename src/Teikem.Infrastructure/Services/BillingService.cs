using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 72 (P5) — facturación. Una CORRIDA (generar → revisar → aprobar → exportar) junta lo que hay por facturar y arma una factura por cliente.
/// - Lote 74: también el FLETE de las órdenes de transporte (ver más abajo). La VENTA DE PRODUCTO de las órdenes de venta (ChargeType PRODUCT_SALE): lo despachado y no facturado (QtyShipped − QtyReturned − QtyInvoiced), con la copia del
///   impuesto de su línea. La base la elige la compañía (SalesBillingTrigger): al despachar o al entregar.
///   El FLETE de una orden de transporte se factura según el modelo de cobro de su contrato (por recogido, por entrega o mixto) con lo que se le cotizó al confirmarla, desglosado por concepto con la fila
///   de tarifa de cada uno; TransportOrder.FreightInvoiced la protege de facturarse dos veces (bloqueo de la orden al generar).
/// - Nunca se factura dos veces: al GENERAR se reclama lo facturado (SalesOrderLine.QtyInvoiced) bajo bloqueo de la línea; cancelar la corrida o anular la factura lo libera exactamente.
/// - El cálculo se congela al generar; el ajuste manual de un monto cambia la línea y deja el calculado y el motivo. Aprobar y anular exigen AAL2 (en el controlador).
/// - Estatus por StatusService: corrida GENERATED → REVIEWED → APPROVED → EXPORTED (o CANCELLED); factura DRAFT → ISSUED → PAID (o VOID).
/// </summary>
public sealed class BillingService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock,
    OrderQuoteService quotes, TaxService taxes)
{
    public const string Concurrency = "La facturación cambió mientras se guardaba. Revise y vuelva a intentar.";
    public const string CancelledCode = "CANCELLED";
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ ajustes

    public async Task<BillingSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        return new BillingSettingsDto(t.SalesBillingTrigger, t.InvoiceDueDays);
    }

    public async Task<BillingSettingsDto> UpdateSettingsAsync(BillingSettingsRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var t = await db.Tenants.AsTracking().FirstAsync(x => x.TenantId == T.RequireTenantId(), ct);
        if (req.SalesBillingTrigger is not null)
        {
            var trigger = req.SalesBillingTrigger.Trim().ToUpperInvariant();
            if (!SalesBillingTriggers.All.Contains(trigger)) throw new ValidationException("salesBillingTrigger", BillingRules.InvalidTrigger);
            t.SalesBillingTrigger = trigger;
        }
        if (req.InvoiceDueDays is int days)
        {
            if (days < 0 || days > BillingRules.MaxDueDays) throw new ValidationException("invoiceDueDays", BillingRules.DueDaysInvalid);
            t.InvoiceDueDays = days;
        }
        await db.SaveGuardedAsync(Concurrency, ct);
        return new BillingSettingsDto(t.SalesBillingTrigger, t.InvoiceDueDays);
    }

    // ================================================================ qué hay por facturar

    private sealed record Eligible(int LineId, int OrderId, string OrderNumber, int ClientId, int? CurrencyLookupId, int ProductId, string Sku, string Name, decimal Ordered, decimal Shipped,
        decimal Invoiced, decimal UnitPrice, decimal TaxAmount, decimal TaxRatePct, int? TaxRateId, bool Exempt, decimal Billable);

    /// <summary>
    /// Líneas de orden de venta con algo despachado por facturar. Lo despachado se mide con las recolecciones activas de la orden (hasta la fecha final, o dentro del período); con base
    /// «al entregar» solo cuentan las que no llevan entrega (retiro) o cuya orden de transporte ya se entregó.
    /// </summary>
    private async Task<List<Eligible>> EligibleAsync(int? clientId, DateOnly start, DateOnly end, bool onlyInPeriod, string trigger, CancellationToken ct)
    {
        var lines = await (from l in db.Set<SalesOrderLine>().AsNoTracking()
                           join o in db.Set<SalesOrder>().AsNoTracking() on l.SalesOrderId equals o.SalesOrderId
                           join p in db.Set<Product>().AsNoTracking() on l.ProductId equals p.ProductId
                           where l.QtyShipped - l.QtyReturned - l.QtyInvoiced > 0m && (clientId == null || o.ClientId == clientId)
                           select new { Line = l, Order = o, p.Sku, p.Name }).ToListAsync(ct);
        if (lines.Count == 0) return new List<Eligible>();

        var lineIds = lines.Select(x => x.Line.SalesOrderLineId).ToList();
        var (rangeFrom, toExclusive) = clock.UtcRange(onlyInPeriod ? start : null, end);
        var deliveredIds = trigger == SalesBillingTriggers.OnDelivery
            ? await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus && s.InternalCode == OrderStatuses.Delivered).Select(s => s.StatusCodeId).ToListAsync(ct)
            : new List<int>();
        var onDelivery = trigger == SalesBillingTriggers.OnDelivery;

        var dated = await (from bl in db.Set<PickBatchLine>().AsNoTracking()
                           join b in db.Set<PickBatch>().AsNoTracking() on bl.PickBatchId equals b.PickBatchId
                           where b.IsActive && bl.SalesOrderLineId != null && lineIds.Contains(bl.SalesOrderLineId.Value)
                                 && (rangeFrom == null || b.CollectedAtUtc >= rangeFrom) && (toExclusive == null || b.CollectedAtUtc < toExclusive)
                                 && (!onDelivery || b.TransportOrderId == null
                                     || db.Set<TransportOrder>().Any(t => t.TransportOrderId == b.TransportOrderId && deliveredIds.Contains(t.StatusCodeId)))
                           select new { LineId = bl.SalesOrderLineId!.Value, Units = bl.KitProductId == null ? bl.Quantity : (bl.KitQuantity ?? 0m) }).ToListAsync(ct);
        var shippedInRange = dated.GroupBy(d => d.LineId).ToDictionary(g => g.Key, g => g.Sum(d => d.Units));

        var result = new List<Eligible>();
        foreach (var x in lines.OrderBy(x => x.Order.ClientId).ThenBy(x => x.Order.SalesOrderId).ThenBy(x => x.Line.LineNo))
        {
            var l = x.Line;
            var unbilled = BillingRules.Billable(l.QtyShipped, l.QtyReturned, l.QtyInvoiced);
            var inRange = shippedInRange.GetValueOrDefault(l.SalesOrderLineId);
            var billable = onlyInPeriod ? Math.Min(unbilled, inRange) : Math.Min(unbilled, Math.Max(0m, inRange - l.QtyInvoiced));
            if (billable <= 0m) continue;
            result.Add(new Eligible(l.SalesOrderLineId, x.Order.SalesOrderId, x.Order.Number, x.Order.ClientId, x.Order.CurrencyLookupId, l.ProductId, x.Sku, x.Name, l.QtyOrdered, l.QtyShipped,
                l.QtyInvoiced, l.UnitPrice, l.TaxAmount, l.TaxRatePct, l.TaxRateId, l.IsTaxExempt, billable));
        }
        return result;
    }

    // ================================================================ generar

    public async Task<BillingRunDto> GenerateAsync(BillingRunRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.PeriodStart is not DateOnly start || req.PeriodEnd is not DateOnly end) throw new ValidationException("period", BillingRules.PeriodRequired);
        if (end < start) throw new ValidationException("period", BillingRules.PeriodInvalid);
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > BillingRules.MaxNoteLength }) throw new ValidationException("note", BillingRules.NoteTooLong);
        Client? client = null;
        if (req.ClientPublicId is Guid cid) client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == cid, ct) ?? throw new NotFoundException("Cliente");
        var settings = await GetSettingsAsync(ct);
        var onlyInPeriod = req.OnlyInPeriod == true;
        var includeProducts = req.IncludeProducts ?? true;
        var includeFreight = req.IncludeFreight ?? true;
        if (!includeProducts && !includeFreight) throw new ValidationException("include", FreightRules.NothingSelected);

        var snapshot = includeProducts ? await EligibleAsync(client?.ClientId, start, end, onlyInPeriod, settings.SalesBillingTrigger, ct) : new List<Eligible>();
        var freight = includeFreight ? await EligibleFreightAsync(client?.ClientId, start, end, onlyInPeriod, ct) : new List<FreightEligible>();
        if (snapshot.Count == 0 && freight.Count == 0) throw new StatusRuleException(BillingRules.NothingToBill);
        var freightCharges = await FreightChargesAsync(freight, ct);
        var freightTax = await FreightTaxAsync(freight, freightCharges, ct);
        await numbers.EnsureAsync(BillingCodes.NumberKind, null, ct);
        var usd = await lookups.GetIdAsync(LookupDomains.Currency, "USD", ct);
        var productSale = await lookups.GetIdAsync(BillingCodes.ChargeTypeDomain, BillingCodes.ProductSale, ct);
        var chargeIds = new Dictionary<string, int>();
        foreach (var code in freightCharges.Values.SelectMany(c => c).Select(c => c.ChargeCode).Distinct())
            chargeIds[code] = await lookups.GetIdAsync(BillingCodes.ChargeTypeDomain, code, ct);

        var runId = await db.RunInTransactionAsync(async ct2 =>
        {
            var tenantId = T.RequireTenantId();
            // 1) se bloquean las líneas (en orden) y se vuelve a calcular lo facturable con lo que hay AHORA: dos corridas a la vez no facturan lo mismo
            var ids = snapshot.Select(s => s.LineId).Distinct().OrderBy(i => i).ToList();
            var locked = await LockLinesAsync(ids, ct2);
            var priorTax = await db.Set<InvoiceLine>().AsNoTracking()
                .Where(il => il.SalesOrderLineId != null && ids.Contains(il.SalesOrderLineId.Value))
                .Join(db.Set<Invoice>().AsNoTracking(), il => il.InvoiceId, i => i.InvoiceId, (il, i) => new { il.SalesOrderLineId, il.TaxAmount, i.StatusCodeId })
                .ToListAsync(ct2);
            var voidId = (await statuses.GetByCodeAsync(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Void, ct2)).StatusCodeId;
            var taxBy = priorTax.Where(p => p.StatusCodeId != voidId).GroupBy(p => p.SalesOrderLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(p => p.TaxAmount));

            var work = new List<(Eligible E, decimal Qty, decimal Tax)>();
            foreach (var e in snapshot)
            {
                var line = locked[e.LineId];
                var qty = Math.Min(e.Billable, BillingRules.Billable(line.QtyShipped, line.QtyReturned, line.QtyInvoiced));
                if (qty <= 0m) continue;
                var tax = BillingRules.TaxPortion(line.TaxAmount, line.QtyOrdered, qty, Math.Max(0m, line.QtyOrdered - line.QtyInvoiced), taxBy.GetValueOrDefault(e.LineId));
                work.Add((e, qty, tax));
                line.QtyInvoiced += qty;
            }
            // 1b) el flete: se bloquea cada orden (en orden de id) y se reclama solo si nadie la facturó entre tanto
            var freightWork = new List<FreightEligible>();
            foreach (var f in freight.OrderBy(x => x.OrderId))
            {
                var order = await db.LockTransportOrderAsync(f.OrderId, ct2);
                if (order.FreightInvoiced) continue;
                order.FreightInvoiced = true;
                freightWork.Add(f);
            }
            if (work.Count == 0 && freightWork.Count == 0) throw new StatusRuleException(BillingRules.NothingToBill);

            // 2) la corrida y una factura por cliente y moneda
            var runInitial = await statuses.GetInitialAsync(BillingCodes.RunStatusDomain, ct2);
            var run = new BillingRun { TenantId = tenantId, PeriodStart = start, PeriodEnd = end, StatusCodeId = runInitial.StatusCodeId, ClientId = client?.ClientId, Note = note, GeneratedAtUtc = DateTime.UtcNow };
            db.Set<BillingRun>().Add(run);
            await db.SaveGuardedAsync(Concurrency, ct2);
            var invInitial = await statuses.GetInitialAsync(BillingCodes.InvoiceStatusDomain, ct2);
            var invoices = new List<Invoice>();
            var keys = work.Select(w => (w.E.ClientId, Currency: w.E.CurrencyLookupId ?? usd))
                .Concat(freightWork.Select(f => (f.ClientId, Currency: f.CurrencyLookupId ?? usd)))
                .Distinct().OrderBy(k => k.ClientId).ThenBy(k => k.Currency).ToList();
            foreach (var key in keys)
            {
                var seq = await numbers.NextAsync(BillingCodes.NumberKind, null, ct2);
                var invoice = new Invoice
                {
                    PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = key.ClientId, InvoiceNumber = NumberFormat.Resolve(BillingCodes.NumberPattern, seq),
                    StatusCodeId = invInitial.StatusCodeId, CurrencyLookupId = key.Currency, BillingRunId = run.BillingRunId,
                };
                foreach (var w in work.Where(w => w.E.ClientId == key.ClientId && (w.E.CurrencyLookupId ?? usd) == key.Currency))
                    invoice.Lines.Add(new InvoiceLine
                    {
                        ProductId = w.E.ProductId, ChargeTypeLookupId = productSale, Description = BillingRules.Describe(w.E.OrderNumber, w.E.Sku, w.E.Name), Quantity = w.Qty, UnitAmount = w.E.UnitPrice,
                        SalesOrderLineId = w.E.LineId, TaxRateId = w.E.TaxRateId, TaxRatePct = w.E.TaxRatePct, TaxAmount = w.Tax, IsTaxExempt = w.E.Exempt,
                    });
                foreach (var f in freightWork.Where(f => f.ClientId == key.ClientId && (f.CurrencyLookupId ?? usd) == key.Currency))
                {
                    var charges = freightCharges[f.OrderId];
                    for (var i = 0; i < charges.Count; i++)
                    {
                        var c = charges[i];
                        var tx = freightTax[FreightKey(f.OrderId, i)];
                        invoice.Lines.Add(new InvoiceLine
                        {
                            TransportOrderId = f.OrderId, ChargeTypeLookupId = chargeIds[c.ChargeCode], Description = c.Description, Quantity = 1m, UnitAmount = tx.Net, RateComponentId = c.RateComponentId,
                            TaxRateId = tx.TaxRateId, TaxRatePct = tx.TaxRatePct, TaxAmount = tx.TaxAmount, IsTaxExempt = tx.IsTaxExempt,
                        });
                    }
                }
                Recalculate(invoice);
                db.Set<Invoice>().Add(invoice);
                invoices.Add(invoice);
            }
            await db.SaveGuardedAsync(Concurrency, ct2);
            await statuses.TransitionAsync(BillingCodes.RunStatusDomain, BillingCodes.RunEntityType, run.BillingRunId, null, runInitial.InternalCode, null, ct2);
            foreach (var inv in invoices) await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, null, invInitial.InternalCode, null, ct2);
            run.InvoiceCount = invoices.Count;
            run.TotalAmount = invoices.Sum(i => i.Subtotal + i.TaxAmount);
            await db.SaveGuardedAsync(Concurrency, ct2);
            return run.BillingRunId;
        }, ct);
        return await GetRunAsync(runId, ct);
    }

    // ================================================================ flete de las órdenes de transporte (Lote 74)

    private sealed record FreightEligible(int OrderId, string OrderNumber, int ClientId, int? CurrencyLookupId, decimal Quoted, DateTime QuotedAtUtc);

    private sealed record FreightCharge(string Component, string ChargeCode, string Description, decimal Amount, int? RateComponentId);

    private static string FreightKey(int orderId, int index) => $"{orderId}:{index}";

    /// <summary>
    /// Órdenes de transporte con flete por facturar: activas, con monto cotizado (mayor que cero), sin facturar, en un estatus que su contrato ya admite (FreightRules.BillableStatuses según el modelo de
    /// cobro: por recogido desde Recogido; por entrega, mixto o sin modelo, solo Entregada) y cuyo hecho (recogida o entrega, de la bitácora de estatus) cae hasta la fecha final (o dentro del período).
    /// </summary>
    private async Task<List<FreightEligible>> EligibleFreightAsync(int? clientId, DateOnly start, DateOnly end, bool onlyInPeriod, CancellationToken ct)
    {
        var statusMap = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == StatusDomains.OrderStatus).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);
        var pickedIds = statusMap.Where(kv => FreightRules.PickedUpOrLater.Contains(kv.Value)).Select(kv => kv.Key).ToList();
        if (pickedIds.Count == 0) return new List<FreightEligible>();
        var orders = await db.Set<TransportOrder>().AsNoTracking()
            .Where(o => o.IsActive && !o.FreightInvoiced && o.QuotedAmount > 0m && pickedIds.Contains(o.StatusCodeId) && (clientId == null || o.ClientId == clientId))
            .OrderBy(o => o.TransportOrderId).ToListAsync(ct);
        if (orders.Count == 0) return new List<FreightEligible>();

        var contractIds = orders.Where(o => o.ContractId != null).Select(o => o.ContractId!.Value).Distinct().ToList();
        var modelIds = await db.Set<Contract>().AsNoTracking().Where(c => contractIds.Contains(c.ContractId)).Select(c => new { c.ContractId, c.BillingModelLookupId }).ToListAsync(ct);
        var models = new Dictionary<int, string?>();
        foreach (var m in modelIds) models[m.ContractId] = m.BillingModelLookupId is int lid ? (await lookups.GetAsync(lid, ct))?.InternalCode : null;
        string? ModelOf(TransportOrder o) => o.ContractId is int c ? models.GetValueOrDefault(c) : null;

        // Lote 114: un pick up facturable se cobra al llegar «En almacén» (su estatus final), sin importar el modelo de cobro del contrato
        var allowed = orders.Where(o => statusMap[o.StatusCodeId] == OrderStatuses.InWarehouse || FreightRules.BillableStatuses(ModelOf(o)).Contains(statusMap[o.StatusCodeId])).ToList();
        if (allowed.Count == 0) return new List<FreightEligible>();

        // el hecho: la primera vez que la orden llegó a un estatus facturable de su modelo
        var orderIds = allowed.Select(o => o.TransportOrderId).ToList();
        var entityTypeId = await lookups.TryGetIdAsync(LookupDomains.EntityType, EntityTypes.TransportOrder, ct);
        var history = new List<(int OrderId, string Code, DateTime At)>();
        if (entityTypeId is int etid)
        {
            var rows = await db.Set<EntityStatusHistory>().AsNoTracking()
                .Where(h => h.EntityTypeLookupId == etid && orderIds.Contains(h.EntityId) && pickedIds.Contains(h.ToStatusCodeId))
                .Select(h => new { h.EntityId, h.ToStatusCodeId, h.ChangedAtUtc }).ToListAsync(ct);
            history = rows.Select(r => (r.EntityId, statusMap[r.ToStatusCodeId], r.ChangedAtUtc)).ToList();
        }
        var (rangeFrom, toExclusive) = clock.UtcRange(onlyInPeriod ? start : null, end);
        var result = new List<FreightEligible>();
        foreach (var o in allowed)
        {
            var billable = FreightRules.BillableStatuses(ModelOf(o)).Append(OrderStatuses.InWarehouse).ToList();
            var fact = history.Where(h => h.OrderId == o.TransportOrderId && billable.Contains(h.Code)).Select(h => (DateTime?)h.At).Min()
                       ?? o.ConfirmedAtUtc ?? o.QuotedAtUtc ?? o.CreatedAtUtc;
            if ((rangeFrom != null && fact < rangeFrom) || (toExclusive != null && fact >= toExclusive)) continue;
            result.Add(new FreightEligible(o.TransportOrderId, o.OrderNumber, o.ClientId, o.CurrencyLookupId, o.QuotedAmount ?? 0m, o.QuotedAtUtc ?? fact));
        }
        return result;
    }

    /// <summary>
    /// Los cargos de cada orden: el desglose de su cotización (tarifa base, piezas extra, despacho, COD o servicio especial) con la fila de tarifa de cada uno, recalculado con las tarifas vigentes el día en
    /// que se cotizó. Si no se puede desglosar (cliente dado de baja, servicio especial cerrado, líneas sin tarifa) o la suma ya no coincide con lo cotizado, se factura UNA línea con el monto cotizado:
    /// lo que se le dijo al cliente manda.
    /// </summary>
    private async Task<Dictionary<int, List<FreightCharge>>> FreightChargesAsync(IReadOnlyList<FreightEligible> list, CancellationToken ct)
    {
        var result = new Dictionary<int, List<FreightCharge>>();
        if (list.Count == 0) return result;
        var ids = list.Select(f => f.OrderId).ToList();
        var orders = await db.Set<TransportOrder>().AsNoTracking().Include(o => o.CargoLines).Where(o => ids.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, ct);
        var clientIds = list.Select(f => f.ClientId).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        foreach (var f in list)
        {
            var asOf = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(f.QuotedAtUtc, DateTimeKind.Utc), clock.Zone));
            FreightBreakdown? breakdown = null;
            try { breakdown = await quotes.BreakdownAsync(orders[f.OrderId], clients[f.ClientId], asOf, ct); }
            catch (Exception ex) when (ex is ConflictException or StatusRuleException or ValidationException or NotFoundException) { }
            if (breakdown is { Pieces.Count: > 0 } && FreightRules.MatchesQuote(breakdown.Total, f.Quoted))
                result[f.OrderId] = breakdown.Pieces.Select(p => new FreightCharge(p.Component, FreightRules.ChargeTypeOf(p.Component),
                    FreightRules.Describe(f.OrderNumber, p.Component, p.ServiceType, p.PackageType, null), p.Amount, p.RateComponentId)).ToList();
            else
                result[f.OrderId] = new List<FreightCharge> { new(FreightComponents.Quoted, FreightRules.ChargeTypeOf(FreightComponents.Quoted), FreightRules.Describe(f.OrderNumber, FreightComponents.Quoted, null, null), f.Quoted, null) };
        }
        return result;
    }

    /// <summary>El impuesto de cada cargo del flete con el motor de impuestos (regla por tipo de cargo y exención vigente del cliente), calculado hoy: la copia queda en la línea.</summary>
    private async Task<Dictionary<string, TaxCalcLineDto>> FreightTaxAsync(IReadOnlyList<FreightEligible> list, Dictionary<int, List<FreightCharge>> charges, CancellationToken ct)
    {
        var result = new Dictionary<string, TaxCalcLineDto>();
        foreach (var byClient in list.GroupBy(f => f.ClientId))
        {
            var lines = byClient.SelectMany(f => charges[f.OrderId].Select((c, i) => new TaxCalcLineRequest(FreightKey(f.OrderId, i), null, c.ChargeCode, c.Amount))).ToList();
            foreach (var chunk in lines.Chunk(400))
            {
                var calc = await taxes.CalculateAsync(new TaxCalcRequest(byClient.Key, clock.Today, chunk), ct);
                foreach (var l in calc.Lines) result[l.Key] = l;
            }
        }
        return result;
    }

    /// <summary>Bloquea las líneas (en orden de id, siempre el mismo, para no interbloquear) y las devuelve rastreadas con lo que hay AHORA.</summary>
    private async Task<Dictionary<int, SalesOrderLine>> LockLinesAsync(IReadOnlyList<int> ids, CancellationToken ct)
    {
        var result = new Dictionary<int, SalesOrderLine>();
        foreach (var id in ids.OrderBy(i => i)) result[id] = await db.LockSalesOrderLineAsync(id, ct);
        return result;
    }

    private static void Recalculate(Invoice invoice)
    {
        invoice.Subtotal = invoice.Lines.Sum(l => Math.Round(l.Quantity * l.UnitAmount, 4, MidpointRounding.AwayFromZero));
        invoice.TaxAmount = invoice.Lines.Sum(l => l.TaxAmount);
    }

    // ================================================================ revisar, aprobar, cancelar

    public async Task<BillingRunDto> ReviewAsync(int runId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var run = await db.Set<BillingRun>().AsTracking().FirstOrDefaultAsync(r => r.BillingRunId == runId, ct2) ?? throw RunNotFound();
            if (await RunCodeAsync(run, ct2) != BillingRunStatuses.Generated) throw new StatusRuleException(BillingRules.ReviewNeedsGenerated);
            var to = await statuses.TransitionAsync(BillingCodes.RunStatusDomain, BillingCodes.RunEntityType, run.BillingRunId, run.StatusCodeId, BillingRunStatuses.Reviewed, null, ct2);
            (run.StatusCodeId, run.ReviewedBy, run.ReviewedAtUtc) = (to.StatusCodeId, tenant.UserId, DateTime.UtcNow);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetRunAsync(runId, ct);
    }

    /// <summary>Aprueba la corrida revisada: las facturas se EMITEN (fecha de emisión hoy y vencimiento según los días de la compañía). El controlador exige AAL2.</summary>
    public async Task<BillingRunDto> ApproveAsync(int runId, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var run = await db.Set<BillingRun>().AsTracking().FirstOrDefaultAsync(r => r.BillingRunId == runId, ct2) ?? throw RunNotFound();
            if (await RunCodeAsync(run, ct2) != BillingRunStatuses.Reviewed) throw new StatusRuleException(BillingRules.ApproveNeedsReviewed);
            var today = clock.Today;
            var invoices = await db.Set<Invoice>().AsTracking().Where(i => i.BillingRunId == runId).ToListAsync(ct2);
            foreach (var inv in invoices)
            {
                var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Issued, null, ct2);
                (inv.StatusCodeId, inv.IssueDate, inv.DueDate) = (to.StatusCodeId, today, BillingRules.DueDate(today, settings.InvoiceDueDays));
            }
            var approved = await statuses.TransitionAsync(BillingCodes.RunStatusDomain, BillingCodes.RunEntityType, run.BillingRunId, run.StatusCodeId, BillingRunStatuses.Approved, null, ct2);
            (run.StatusCodeId, run.ApprovedBy, run.ApprovedAtUtc) = (approved.StatusCodeId, tenant.UserId, DateTime.UtcNow);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetRunAsync(runId, ct);
    }

    /// <summary>Cancela una corrida generada o revisada: sus facturas se anulan y lo que reclamaron vuelve a quedar por facturar.</summary>
    public async Task<BillingRunDto> CancelAsync(int runId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var run = await db.Set<BillingRun>().AsTracking().FirstOrDefaultAsync(r => r.BillingRunId == runId, ct2) ?? throw RunNotFound();
            var code = await RunCodeAsync(run, ct2);
            if (code is not (BillingRunStatuses.Generated or BillingRunStatuses.Reviewed)) throw new StatusRuleException(BillingRules.CancelNeedsOpen);
            var invoices = await db.Set<Invoice>().AsTracking().Include(i => i.Lines).Where(i => i.BillingRunId == runId).ToListAsync(ct2);
            await ReleaseAsync(invoices, ct2);
            foreach (var inv in invoices)
            {
                var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Void, comment, ct2);
                inv.StatusCodeId = to.StatusCodeId;
            }
            var cancelled = await statuses.TransitionAsync(BillingCodes.RunStatusDomain, BillingCodes.RunEntityType, run.BillingRunId, run.StatusCodeId, CancelledCode, comment, ct2);
            run.StatusCodeId = cancelled.StatusCodeId;
            run.TotalAmount = 0m;
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetRunAsync(runId, ct);
    }

    /// <summary>Devuelve a «por facturar» lo que reclamaron las líneas de estas facturas (bajo bloqueo de las líneas de orden de venta y de las órdenes de transporte).</summary>
    private async Task ReleaseAsync(IReadOnlyList<Invoice> invoices, CancellationToken ct)
    {
        var claimed = invoices.SelectMany(i => i.Lines).Where(l => l.SalesOrderLineId != null).GroupBy(l => l.SalesOrderLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (claimed.Count > 0)
        {
            var locked = await LockLinesAsync(claimed.Keys.OrderBy(i => i).ToList(), ct);
            foreach (var (lineId, qty) in claimed)
                if (locked.TryGetValue(lineId, out var line)) line.QtyInvoiced = Math.Max(0m, line.QtyInvoiced - qty);
        }
        foreach (var orderId in invoices.SelectMany(i => i.Lines).Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value).Distinct().OrderBy(i => i))
            (await db.LockTransportOrderAsync(orderId, ct)).FreightInvoiced = false;
    }

    // ================================================================ ajustar una línea y anular una factura

    /// <summary>Ajuste manual del monto unitario de una línea (solo mientras la corrida está generada o revisada): conserva el calculado, exige el motivo y recalcula el impuesto de la línea y los totales.</summary>
    public async Task<InvoiceDto> AdjustLineAsync(Guid invoicePublicId, int lineId, InvoiceLineAdjustRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.UnitAmount is null) throw new ValidationException("unitAmount", BillingRules.AdjustNeedsPrice);
        var amount = req.UnitAmount.Value;
        if (amount < 0m || Math.Round(amount, 4) != amount) throw new ValidationException("unitAmount", BillingRules.AdjustPriceInvalid);
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is null) throw new ValidationException("note", BillingRules.AdjustNoteRequired);
        if (note.Length > 250) throw new ValidationException("note", BillingRules.AdjustNoteTooLong);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var inv = await db.Set<Invoice>().AsTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicId == invoicePublicId, ct2) ?? throw InvoiceNotFound();
            var run = inv.BillingRunId is int rid ? await db.Set<BillingRun>().AsTracking().FirstOrDefaultAsync(r => r.BillingRunId == rid, ct2) : null;
            if (run is null || await RunCodeAsync(run, ct2) is not (BillingRunStatuses.Generated or BillingRunStatuses.Reviewed)) throw new StatusRuleException(BillingRules.AdjustNeedsOpenRun);
            var line = inv.Lines.FirstOrDefault(l => l.InvoiceLineId == lineId) ?? throw new NotFoundException("Línea de factura", feminine: true);
            if (amount == line.UnitAmount) throw new StatusRuleException(BillingRules.AdjustSameAmount);
            line.OriginalUnitAmount ??= line.UnitAmount;
            line.UnitAmount = amount;
            line.AdjustmentNote = note;
            line.TaxAmount = line.IsTaxExempt || line.TaxRatePct is null ? 0m : BillingRules.Money(Math.Round(line.Quantity * amount, 4, MidpointRounding.AwayFromZero) * line.TaxRatePct.Value / 100m);
            Recalculate(inv);
            run.TotalAmount = await RunTotalAsync(run.BillingRunId, inv, ct2);
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetInvoiceAsync(invoicePublicId, ct);
    }

    private async Task<decimal> RunTotalAsync(int runId, Invoice changed, CancellationToken ct)
    {
        var voidId = (await statuses.GetByCodeAsync(BillingCodes.InvoiceStatusDomain, InvoiceStatuses.Void, ct)).StatusCodeId;
        var others = await db.Set<Invoice>().AsNoTracking().Where(i => i.BillingRunId == runId && i.InvoiceId != changed.InvoiceId && i.StatusCodeId != voidId)
            .Select(i => i.Subtotal + i.TaxAmount).ToListAsync(ct);
        return others.Sum() + (changed.StatusCodeId == voidId ? 0m : changed.Subtotal + changed.TaxAmount);
    }

    /// <summary>Anula una factura emitida (o vencida) sin pagos: libera lo que reclamó y deja el motivo en el historial. El controlador exige AAL2.</summary>
    public async Task<InvoiceDto> VoidAsync(Guid invoicePublicId, string? comment, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var inv = await db.Set<Invoice>().AsTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicId == invoicePublicId, ct2) ?? throw InvoiceNotFound();
            var code = (await StatusMapAsync(BillingCodes.InvoiceStatusDomain, ct2))[inv.StatusCodeId].Code;
            if (code is not (InvoiceStatuses.Issued or InvoiceStatuses.Overdue)) throw new StatusRuleException(BillingRules.VoidNeedsIssued);
            if (await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.InvoiceId == inv.InvoiceId && p.VoidedAtUtc == null, ct2)) throw new StatusRuleException(BillingRules.VoidHasPayments);
            if (await HasCreditsAsync(inv.InvoiceId, ct2)) throw new StatusRuleException(CreditNoteRules.InvoiceHasCredits);
            await ReleaseAsync(new[] { inv }, ct2);
            var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Void, comment, ct2);
            inv.StatusCodeId = to.StatusCodeId;
            if (inv.BillingRunId is int rid)
            {
                var run = await db.Set<BillingRun>().AsTracking().FirstOrDefaultAsync(r => r.BillingRunId == rid, ct2);
                if (run is not null) run.TotalAmount = await RunTotalAsync(rid, inv, ct2);
            }
            await db.SaveGuardedAsync(Concurrency, ct2);
        }, ct);
        return await GetInvoiceAsync(invoicePublicId, ct);
    }

    /// <summary>La factura tiene notas de crédito aprobadas o aplicadas (lo acreditado no se deshace anulando la factura).</summary>
    private async Task<bool> HasCreditsAsync(int invoiceId, CancellationToken ct)
        => await (from n in db.CreditNotes.AsNoTracking()
                  join s in db.StatusCodes.AsNoTracking() on n.StatusCodeId equals s.StatusCodeId
                  where n.InvoiceId == invoiceId && (s.InternalCode == CreditNoteStatuses.Approved || s.InternalCode == CreditNoteStatuses.Applied)
                  select n.CreditNoteId).AnyAsync(ct)
           || await db.Set<CreditApplication>().AsNoTracking().AnyAsync(a => a.InvoiceId == invoiceId, ct);

    // ================================================================ consultas

    private async Task<string> RunCodeAsync(BillingRun run, CancellationToken ct) => (await StatusMapAsync(BillingCodes.RunStatusDomain, ct))[run.StatusCodeId].Code;

    private static NotFoundException RunNotFound() => new("Corrida de facturación", feminine: true);
    private static NotFoundException InvoiceNotFound() => new("Factura", feminine: true);

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(string domain, CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == domain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    public async Task<IReadOnlyList<BillingRunListItemDto>> ListRunsAsync(string? status, CancellationToken ct)
    {
        var map = await StatusMapAsync(BillingCodes.RunStatusDomain, ct);
        var q = db.Set<BillingRun>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            q = q.Where(r => ids.Contains(r.StatusCodeId));
        }
        var runs = await q.OrderByDescending(r => r.BillingRunId).Take(500).ToListAsync(ct);
        var clientIds = runs.Where(r => r.ClientId != null).Select(r => r.ClientId!.Value).Distinct().ToList();
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientIds.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        return runs.Select(r =>
        {
            var s = map[r.StatusCodeId];
            return new BillingRunListItemDto(r.BillingRunId, r.PeriodStart, r.PeriodEnd, s.Code, s.Label, s.Color, r.ClientId is int c ? clients.GetValueOrDefault(c) : null, r.InvoiceCount, r.TotalAmount, r.GeneratedAtUtc);
        }).ToList();
    }

    public async Task<BillingRunDto> GetRunAsync(int runId, CancellationToken ct)
    {
        var run = await db.Set<BillingRun>().AsNoTracking().FirstOrDefaultAsync(r => r.BillingRunId == runId, ct) ?? throw RunNotFound();
        var s = (await StatusMapAsync(BillingCodes.RunStatusDomain, ct))[run.StatusCodeId];
        var client = run.ClientId is int c ? await db.Set<Client>().AsNoTracking().Where(x => x.ClientId == c).Select(x => new { x.PublicId, x.Name }).FirstOrDefaultAsync(ct) : null;
        var invoices = await ListInvoicesAsync(null, null, runId, null, 0, MaxPageSize, ct);
        return new BillingRunDto(run.BillingRunId, run.PeriodStart, run.PeriodEnd, s.Code, s.Label, s.Color, client?.PublicId, client?.Name, run.Note, run.InvoiceCount, run.TotalAmount,
            run.GeneratedAtUtc, run.ReviewedAtUtc, run.ApprovedAtUtc, run.ExportedAtUtc, invoices.Items);
    }

    public async Task<PagedResult<InvoiceListItemDto>> ListInvoicesAsync(string? status, Guid? clientPublicId, int? runId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(BillingCodes.InvoiceStatusDomain, ct);
        var query = db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(i => ids.Contains(i.StatusCodeId));
        }
        if (clientPublicId is Guid cp)
        {
            var cid = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            query = cid is null ? query.Where(i => false) : query.Where(i => i.ClientId == cid);
        }
        if (runId is int r) query = query.Where(i => i.BillingRunId == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var clientIds = await db.Set<Client>().AsNoTracking().Where(c => c.Name.Contains(s)).Select(c => c.ClientId).ToListAsync(ct);
            query = query.Where(i => i.InvoiceNumber.Contains(s) || clientIds.Contains(i.ClientId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.InvoiceId).Skip(skip).Take(take).Include(i => i.Lines).ToListAsync(ct);
        var clients = await ClientsAsync(rows.Select(i => i.ClientId), ct);
        var paid = await PaidAsync(rows.Select(i => i.InvoiceId), ct);
        var credits = await db.AppliedByInvoiceAsync(rows.Select(i => i.InvoiceId), ct);
        var items = rows.Select(i =>
        {
            var st = map[i.StatusCodeId];
            var c = clients[i.ClientId];
            var totalAmount = i.Subtotal + i.TaxAmount;
            var p = paid.GetValueOrDefault(i.InvoiceId);
            var cr = credits.GetValueOrDefault(i.InvoiceId);
            return new InvoiceListItemDto(i.PublicId, i.InvoiceNumber, st.Code, st.Label, st.Color, c.PublicId, c.Name, i.IssueDate, i.DueDate, i.Subtotal, i.TaxAmount, totalAmount, p, totalAmount - p - cr, i.Lines.Count, i.BillingRunId, cr);
        }).ToList();
        return new PagedResult<InvoiceListItemDto>(items, total, skip, take);
    }

    public async Task<InvoiceDto> GetInvoiceAsync(Guid publicId, CancellationToken ct)
    {
        var i = await db.Set<Invoice>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw InvoiceNotFound();
        var st = (await StatusMapAsync(BillingCodes.InvoiceStatusDomain, ct))[i.StatusCodeId];
        var c = (await ClientsAsync(new[] { i.ClientId }, ct))[i.ClientId];
        var paid = (await PaidAsync(new[] { i.InvoiceId }, ct)).GetValueOrDefault(i.InvoiceId);
        var credited = (await db.AppliedByInvoiceAsync(new[] { i.InvoiceId }, ct)).GetValueOrDefault(i.InvoiceId);
        var currency = (await lookups.GetAsync(i.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        var chargeTypes = i.Lines.Select(l => l.ChargeTypeLookupId).Distinct().ToList();
        var chargeCodes = new Dictionary<int, string>();
        foreach (var id in chargeTypes) chargeCodes[id] = (await lookups.GetAsync(id, ct))?.InternalCode ?? string.Empty;
        var productIds = i.Lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => (p.PublicId, p.Sku), ct);
        var lineIds = i.Lines.Where(l => l.SalesOrderLineId != null).Select(l => l.SalesOrderLineId!.Value).ToList();
        var transportIds = i.Lines.Where(l => l.TransportOrderId != null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var transportNumbers = transportIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Set<TransportOrder>().AsNoTracking().Where(o => transportIds.Contains(o.TransportOrderId)).ToDictionaryAsync(o => o.TransportOrderId, o => o.OrderNumber, ct);
        var orderNumbers = lineIds.Count == 0 ? new Dictionary<int, string>()
            : await (from sl in db.Set<SalesOrderLine>().AsNoTracking() join o in db.Set<SalesOrder>().AsNoTracking() on sl.SalesOrderId equals o.SalesOrderId where lineIds.Contains(sl.SalesOrderLineId) select new { sl.SalesOrderLineId, o.Number })
                .ToDictionaryAsync(x => x.SalesOrderLineId, x => x.Number, ct);
        var lines = i.Lines.OrderBy(l => l.InvoiceLineId).Select(l =>
        {
            var p = l.ProductId is int pid && products.TryGetValue(pid, out var pr) ? ((Guid, string)?)pr : null;
            return new InvoiceLineDto(l.InvoiceLineId, l.Description, chargeCodes[l.ChargeTypeLookupId], p?.Item1, p?.Item2, l.SalesOrderLineId is int sl ? orderNumbers.GetValueOrDefault(sl) : null, l.Quantity, l.UnitAmount,
                l.OriginalUnitAmount, l.OriginalUnitAmount != null, l.AdjustmentNote, Math.Round(l.Quantity * l.UnitAmount, 4, MidpointRounding.AwayFromZero), l.TaxRatePct, l.TaxAmount, l.IsTaxExempt,
                l.TransportOrderId is int tid ? transportNumbers.GetValueOrDefault(tid) : null, l.RateComponentId);
        }).ToList();
        var pays = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == i.InvoiceId).OrderBy(p => p.PaymentId).ToListAsync(ct);
        var payDtos = new List<PaymentDto>();
        foreach (var p in pays)
        {
            var m = await lookups.GetAsync(p.PaymentMethodLookupId, ct);
            payDtos.Add(new PaymentDto(p.PaymentId, p.Amount, m?.InternalCode ?? string.Empty, MultilingualText.Resolve(m?.LabelJson, T.Lang) ?? m?.InternalCode ?? string.Empty,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(p.PaidAtUtc, DateTimeKind.Utc), clock.Zone)), p.Reference, p.VoidedAtUtc, p.VoidReason));
        }
        var total = i.Subtotal + i.TaxAmount;
        return new InvoiceDto(i.PublicId, i.InvoiceNumber, st.Code, st.Label, st.Color, c.PublicId, c.Name, i.IssueDate, i.DueDate, currency, i.Subtotal, i.TaxAmount, total, paid, total - paid - credited, i.BillingRunId, i.Notes, lines, payDtos, credited);
    }

    private async Task<Dictionary<int, (Guid PublicId, string Name)>> ClientsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<Client>().AsNoTracking().Where(c => list.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => (c.PublicId, c.Name), ct);
    }

    private async Task<Dictionary<int, decimal>> PaidAsync(IEnumerable<int> invoiceIds, CancellationToken ct)
    {
        var list = invoiceIds.Distinct().ToList();
        return (await db.Set<Payment>().AsNoTracking().Where(p => list.Contains(p.InvoiceId) && p.VoidedAtUtc == null).ToListAsync(ct)).GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
    }
}
