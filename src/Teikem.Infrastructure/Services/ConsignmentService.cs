using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Common;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 224 (P6 del pedido de vendedores) — consignación en Contabilidad → Facturación (módulo BILLING).
/// - Una factura está «en consignación» si lleva líneas de órdenes de venta marcadas IsConsignment (la venta del producto por SalesOrderLineId y el cargo de envío por SalesOrderId).
///   Cuenta si está emitida, vencida o pagada (activa). Lo dejado = esas líneas con su IVU; lo devuelto = notas de crédito aprobadas o aplicadas de esas líneas (la devolución de
///   venta de siempre las crea); lo pagado = pagos de consignación no anulados; saldo = dejado − devuelto − pagado.
/// - Registrar un pago (billing.consignment.pay): parcial o total, nunca más que el saldo en consignación; bajo el bloqueo de la factura. Si la factura tiene saldo por cobrar el
///   pago entra también como pago de la factura (PaymentService: mismas reglas, la factura pasa a PAGADA al saldarse); si ya estaba pagada, solo se informa lo vendido.
/// - Anular un pago de consignación (con motivo) anula también su pago de la factura (PaymentService.VoidAsync: no se anula si la factura ya quedó pagada). Un pago de la factura
///   anulado por Pagos deja de contar aquí también.
/// - Los reportes de ventas (fuente SALES_REVENUE) cuentan lo consignado solo por estos pagos, en su fecha.
/// </summary>
public sealed class ConsignmentService(TeikemDbContext db, ITenantContext tenant, ILookupCache lookups, ITenantClock clock, PaymentService payments)
{
    private TenantContext T => (TenantContext)tenant;
    private const int MaxPageSize = 200;

    private static readonly string[] CountedStatuses = { InvoiceStatuses.Issued, InvoiceStatuses.Overdue, InvoiceStatuses.Paid };

    private sealed record LineFact(int InvoiceId, int InvoiceLineId, string OrderNumber, string Description, decimal Quantity, decimal Amount);

    private sealed record PaymentFact(ConsignmentPayment Row, bool PaymentVoided)
    {
        public bool Counts => Row.VoidedAtUtc is null && !PaymentVoided;
    }

    private sealed record StatusInfo(string Code, string Label, string Color);

    private sealed record Figures(Invoice Invoice, IReadOnlyList<LineFact> Lines, IReadOnlyDictionary<int, decimal> ReturnedByLine, IReadOnlyList<PaymentFact> Payments,
        decimal Consigned, decimal Returned, decimal Paid, decimal Balance);

    // ================================================================ lectura

    /// <summary>Facturas en consignación (las más recientes primero). balance = OPEN (con saldo, por omisión), PAID o ALL; q = número de factura, de orden o cliente.</summary>
    public async Task<PagedResult<ConsignmentInvoiceListItemDto>> ListAsync(string? balance, Guid? clientPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        var filter = string.IsNullOrWhiteSpace(balance) ? ConsignmentRules.FilterOpen : balance.Trim().ToUpperInvariant();
        if (!ConsignmentRules.Filters.Contains(filter)) throw new ValidationException("balance", ConsignmentRules.FilterInvalid);
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        int? clientId = null;
        if (clientPublicId is Guid cp)
        {
            clientId = await db.Clients.AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            if (clientId is null) return new PagedResult<ConsignmentInvoiceListItemDto>(Array.Empty<ConsignmentInvoiceListItemDto>(), 0, skip, take);
        }
        var figures = await FiguresAsync(null, clientId, ct);
        var items = await ItemsAsync(figures, ct);
        IEnumerable<ConsignmentInvoiceListItemDto> rows = items;
        if (filter == ConsignmentRules.FilterOpen) rows = rows.Where(r => r.Balance > 0m);
        else if (filter == ConsignmentRules.FilterPaid) rows = rows.Where(r => r.Balance <= 0m);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            rows = rows.Where(r => r.Number.Contains(s, StringComparison.OrdinalIgnoreCase) || r.ClientName.Contains(s, StringComparison.OrdinalIgnoreCase)
                                   || r.ClientCode.Contains(s, StringComparison.OrdinalIgnoreCase) || r.OrderNumbers.Any(n => n.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        var list = rows.ToList();
        return new PagedResult<ConsignmentInvoiceListItemDto>(list.Skip(skip).Take(take).ToList(), list.Count, skip, take);
    }

    /// <summary>Detalle: la factura con sus líneas en consignación y sus pagos de consignación (también los anulados). 404 si no existe o no tiene nada en consignación.</summary>
    public async Task<ConsignmentInvoiceDto> GetInvoiceAsync(Guid invoicePublicId, CancellationToken ct)
    {
        var invoiceId = await db.Set<Invoice>().AsNoTracking().Where(i => i.PublicId == invoicePublicId).Select(i => (int?)i.InvoiceId).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Factura", feminine: true);
        var f = (await FiguresAsync(new[] { invoiceId }, null, ct)).SingleOrDefault() ?? throw new NotFoundException("Factura en consignación", feminine: true);
        var item = (await ItemsAsync(new[] { f }, ct)).Single();
        var lines = f.Lines.OrderBy(l => l.InvoiceLineId)
            .Select(l => new ConsignmentLineDto(l.InvoiceLineId, l.OrderNumber, l.Description, l.Quantity, Math.Round(l.Amount, 2, MidpointRounding.AwayFromZero),
                Math.Round(f.ReturnedByLine.GetValueOrDefault(l.InvoiceLineId), 2, MidpointRounding.AwayFromZero))).ToList();
        var users = await AssigneeCheck.NamesAsync(db, f.Payments.Where(p => p.Row.RecordedBy != null).Select(p => p.Row.RecordedBy!.Value), ct);
        var pays = new List<ConsignmentPaymentDto>();
        foreach (var p in f.Payments.OrderByDescending(p => p.Row.PaidDate).ThenByDescending(p => p.Row.ConsignmentPaymentId))
        {
            var method = await lookups.GetAsync(p.Row.PaymentMethodLookupId, ct);
            pays.Add(new ConsignmentPaymentDto(p.Row.PublicId, p.Row.Amount, p.Row.PaidDate, method?.InternalCode ?? string.Empty,
                method is null ? string.Empty : MultilingualText.Resolve(method.LabelJson, T.Lang) ?? method.InternalCode, p.Row.Reference,
                p.Row.RecordedBy is int u ? users.GetValueOrDefault(u) : null, p.Row.RecordedAtUtc, p.Row.PaymentId != null, !p.Counts, p.Row.VoidedAtUtc, p.Row.VoidReason));
        }
        return new ConsignmentInvoiceDto(item, lines, pays);
    }

    /// <summary>Reporte «Consignado por cliente»: lo dejado, devuelto, pagado y el saldo de cada cliente con facturas en consignación (los de más saldo primero).</summary>
    public async Task<ConsignmentByClientDto> ByClientAsync(CancellationToken ct)
    {
        var figures = await FiguresAsync(null, null, ct);
        var clients = await ClientsAsync(figures.Select(f => f.Invoice.ClientId), ct);
        var rows = figures.GroupBy(f => f.Invoice.ClientId).Select(g =>
        {
            var c = clients[g.Key];
            var open = g.Where(f => f.Balance > 0m).ToList();
            return new ConsignmentClientRowDto(c.PublicId, c.Code, c.Name, g.Count(), open.Count, g.Sum(f => f.Consigned), g.Sum(f => f.Returned), g.Sum(f => f.Paid),
                g.Sum(f => f.Balance), open.Select(f => f.Invoice.IssueDate).Where(d => d != null).Min());
        }).OrderByDescending(r => r.Balance).ThenBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase).ToList();
        return new ConsignmentByClientDto(rows, rows.Sum(r => r.Consigned), rows.Sum(r => r.Returned), rows.Sum(r => r.Paid), rows.Sum(r => r.Balance), rows.Sum(r => r.OpenInvoiceCount));
    }

    /// <summary>Facturas con saldo en consignación (cifra «Consignación sin pagar» del panel Ventas del Pulso).</summary>
    public async Task<int> CountUnpaidAsync(CancellationToken ct) => (await FiguresAsync(null, null, ct)).Count(f => f.Balance > 0m);

    // ================================================================ registrar y anular pagos

    public async Task<ConsignmentInvoiceDto> RecordAsync(Guid invoicePublicId, ConsignmentPaymentRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Amount is null) throw new ValidationException("amount", PaymentRules.AmountRequired);
        var amount = req.Amount.Value;
        if (!PaymentRules.IsValidAmount(amount)) throw new ValidationException("amount", PaymentRules.AmountInvalid);
        if (string.IsNullOrWhiteSpace(req.Method)) throw new ValidationException("method", PaymentRules.MethodRequired);
        var methodCode = req.Method.Trim().ToUpperInvariant();
        var methodId = await lookups.TryGetIdAsync(BillingCodes.PaymentMethodDomain, methodCode, ct) ?? throw new ValidationException("method", PaymentRules.MethodInvalid);
        var reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference.Trim();
        if (reference is { Length: > PaymentRules.MaxReferenceLength }) throw new ValidationException("reference", PaymentRules.ReferenceTooLong);
        var today = clock.Today;
        var paidDate = req.PaidDate ?? today;
        if (paidDate > today) throw new ValidationException("paidDate", PaymentRules.DateFuture);

        var invoiceId = await db.Set<Invoice>().AsNoTracking().Where(i => i.PublicId == invoicePublicId).Select(i => (int?)i.InvoiceId).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("Factura", feminine: true);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var inv = await db.LockInvoiceAsync(invoiceId, ct2);
            var status = (await StatusMapAsync(ct2)).GetValueOrDefault(inv.StatusCodeId)?.Code;
            if (!inv.IsActive || status is null || !CountedStatuses.Contains(status)) throw new StatusRuleException(ConsignmentRules.NotPayable);
            if (inv.IssueDate is DateOnly issued && paidDate < issued) throw new ValidationException("paidDate", PaymentRules.DateBeforeIssue);
            var f = (await FiguresAsync(new[] { invoiceId }, null, ct2)).SingleOrDefault() ?? throw new StatusRuleException(ConsignmentRules.NotConsignment);
            if (f.Balance <= 0m) throw new StatusRuleException(ConsignmentRules.AlreadySettled);
            if (amount > f.Balance) throw new StatusRuleException(ConsignmentRules.ExceedsBalance(amount, f.Balance));

            int? paymentId = null;
            if (status is InvoiceStatuses.Issued or InvoiceStatuses.Overdue)
            {
                // el dinero entra también a la factura: mismas reglas que Pagos (no pasa del saldo de la factura; la salda y la pasa a PAGADA)
                await payments.RecordAsync(inv.PublicId, new PaymentRequest(amount, methodCode, paidDate, reference), ct2);
                paymentId = await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoiceId).OrderByDescending(p => p.PaymentId)
                    .Select(p => (int?)p.PaymentId).FirstOrDefaultAsync(ct2);
            }
            db.Set<ConsignmentPayment>().Add(new ConsignmentPayment
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), InvoiceId = invoiceId, PaymentId = paymentId, Amount = amount, PaidDate = paidDate,
                PaymentMethodLookupId = methodId, Reference = reference, RecordedBy = tenant.UserId, RecordedAtUtc = clock.UtcNow,
            });
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetInvoiceAsync(invoicePublicId, ct);
    }

    /// <summary>Anula un pago de consignación con motivo (deja de contar como vendido) y, si entró a la factura, también ese pago.</summary>
    public async Task<ConsignmentInvoiceDto> VoidAsync(Guid paymentPublicId, string? reason, CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note is null) throw new ValidationException("reason", PaymentRules.VoidReasonRequired);
        if (note.Length > PaymentRules.MaxVoidReasonLength) throw new ValidationException("reason", PaymentRules.VoidReasonTooLong);
        var found = await db.Set<ConsignmentPayment>().AsNoTracking().Where(p => p.PublicId == paymentPublicId)
                        .Select(p => new { p.ConsignmentPaymentId, p.InvoiceId }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Pago de consignación");
        var invoicePublicId = await db.Set<Invoice>().AsNoTracking().Where(i => i.InvoiceId == found.InvoiceId).Select(i => i.PublicId).FirstAsync(ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            await db.LockInvoiceAsync(found.InvoiceId, ct2);
            var current = await db.Set<ConsignmentPayment>().AsNoTracking().FirstAsync(p => p.ConsignmentPaymentId == found.ConsignmentPaymentId, ct2);
            if (current.VoidedAtUtc != null) throw new StatusRuleException(PaymentRules.AlreadyVoided);
            if (current.PaymentId is int pid && await db.Set<Payment>().AsNoTracking().AnyAsync(p => p.PaymentId == pid && p.VoidedAtUtc == null, ct2))
                await payments.VoidAsync(pid, note, ct2);   // mismas reglas que Pagos (no se anula si la factura ya quedó pagada)
            var row = await db.Set<ConsignmentPayment>().AsTracking().FirstAsync(p => p.ConsignmentPaymentId == found.ConsignmentPaymentId, ct2);
            (row.VoidedAtUtc, row.VoidedBy, row.VoidReason) = (clock.UtcNow, tenant.UserId, note);
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetInvoiceAsync(invoicePublicId, ct);
    }

    // ================================================================ cifras

    private async Task<List<int>> CountedStatusIdsAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain && CountedStatuses.Contains(s.InternalCode)).Select(s => s.StatusCodeId).ToListAsync(ct);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    /// <summary>Las cifras de consignación de las facturas emitidas, vencidas o pagadas que llevan algo en consignación (todas, o solo las dadas / las de un cliente).</summary>
    private async Task<List<Figures>> FiguresAsync(IReadOnlyCollection<int>? invoiceIds, int? clientId, CancellationToken ct)
    {
        var statusIds = await CountedStatusIdsAsync(ct);
        var invoices = db.Set<Invoice>().AsNoTracking().Where(i => i.IsActive && statusIds.Contains(i.StatusCodeId));
        if (invoiceIds is not null)
        {
            var wanted = invoiceIds.ToList();
            invoices = invoices.Where(i => wanted.Contains(i.InvoiceId));
        }
        if (clientId is int cid) invoices = invoices.Where(i => i.ClientId == cid);

        // venta del producto (por la línea de la orden) y cargo de envío (por la orden) de las órdenes en consignación
        var byLine = await (from il in db.Set<InvoiceLine>().AsNoTracking()
                            join i in invoices on il.InvoiceId equals i.InvoiceId
                            join sol in db.Set<SalesOrderLine>().AsNoTracking() on il.SalesOrderLineId equals (int?)sol.SalesOrderLineId
                            join so in db.Set<SalesOrder>().AsNoTracking() on sol.SalesOrderId equals so.SalesOrderId
                            where so.IsConsignment
                            select new { il.InvoiceId, il.InvoiceLineId, so.Number, il.Description, il.Quantity, il.UnitAmount, il.TaxAmount }).ToListAsync(ct);
        var byOrder = await (from il in db.Set<InvoiceLine>().AsNoTracking()
                             join i in invoices on il.InvoiceId equals i.InvoiceId
                             join so in db.Set<SalesOrder>().AsNoTracking() on il.SalesOrderId equals (int?)so.SalesOrderId
                             where so.IsConsignment && il.SalesOrderLineId == null
                             select new { il.InvoiceId, il.InvoiceLineId, so.Number, il.Description, il.Quantity, il.UnitAmount, il.TaxAmount }).ToListAsync(ct);
        var lines = byLine.Concat(byOrder)
            .Select(x => new LineFact(x.InvoiceId, x.InvoiceLineId, x.Number, x.Description, x.Quantity, ConsignmentRules.LineTotal(x.Quantity, x.UnitAmount, x.TaxAmount)))
            .ToList();
        if (lines.Count == 0) return new List<Figures>();

        var ids = lines.Select(l => l.InvoiceId).Distinct().ToList();
        var lineIds = lines.Select(l => l.InvoiceLineId).Distinct().ToList();
        var rows = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.InvoiceId)).ToListAsync(ct);

        // lo devuelto: notas de crédito aprobadas o aplicadas de esas líneas
        var creditOk = await db.StatusCodes.AsNoTracking()
            .Where(s => s.Entity == CreditCodes.StatusDomain && (s.InternalCode == CreditNoteStatuses.Approved || s.InternalCode == CreditNoteStatuses.Applied))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var credited = await (from cl in db.Set<CreditNoteLine>().AsNoTracking()
                              join cn in db.CreditNotes.AsNoTracking() on cl.CreditNoteId equals cn.CreditNoteId
                              where cl.InvoiceLineId != null && lineIds.Contains(cl.InvoiceLineId.Value) && creditOk.Contains(cn.StatusCodeId)
                              select new { Line = cl.InvoiceLineId!.Value, cl.Quantity, cl.UnitAmount, cl.TaxAmount }).ToListAsync(ct);
        var returnedByLine = credited.GroupBy(c => c.Line).ToDictionary(g => g.Key, g => g.Sum(c => ConsignmentRules.LineTotal(c.Quantity, c.UnitAmount, c.TaxAmount)));

        // lo pagado: pagos de consignación no anulados (ni anulado su pago de la factura)
        var paid = await (from cp in db.Set<ConsignmentPayment>().AsNoTracking()
                          where ids.Contains(cp.InvoiceId)
                          join p in db.Set<Payment>().AsNoTracking() on cp.PaymentId equals (int?)p.PaymentId into pj
                          from p in pj.DefaultIfEmpty()
                          select new { Row = cp, PaymentVoided = p != null && p.VoidedAtUtc != null }).ToListAsync(ct);
        var paidBy = paid.Select(x => new PaymentFact(x.Row, x.PaymentVoided)).ToLookup(x => x.Row.InvoiceId);
        var linesBy = lines.ToLookup(l => l.InvoiceId);

        return rows.Select(inv =>
        {
            var ls = linesBy[inv.InvoiceId].ToList();
            var ps = paidBy[inv.InvoiceId].ToList();
            var consigned = ConsignmentRules.Consigned(ls.Select(l => l.Amount));
            var returned = Math.Round(ls.Sum(l => returnedByLine.GetValueOrDefault(l.InvoiceLineId)), 2, MidpointRounding.AwayFromZero);
            var paidAmount = ps.Where(p => p.Counts).Sum(p => p.Row.Amount);
            return new Figures(inv, ls, returnedByLine, ps, consigned, returned, paidAmount, ConsignmentRules.Balance(consigned, returned, paidAmount));
        }).ToList();
    }

    private sealed record ClientInfo(Guid PublicId, string Code, string Name);

    private async Task<Dictionary<int, ClientInfo>> ClientsAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Clients.AsNoTracking().Where(c => list.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, c => new ClientInfo(c.PublicId, c.Code, c.Name), ct);
    }

    private async Task<List<ConsignmentInvoiceListItemDto>> ItemsAsync(IReadOnlyCollection<Figures> figures, CancellationToken ct)
    {
        if (figures.Count == 0) return new List<ConsignmentInvoiceListItemDto>();
        var map = await StatusMapAsync(ct);
        var clients = await ClientsAsync(figures.Select(f => f.Invoice.ClientId), ct);
        var ids = figures.Select(f => f.Invoice.InvoiceId).ToList();
        var invoicePaid = (await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.VoidedAtUtc == null).Select(p => new { p.InvoiceId, p.Amount }).ToListAsync(ct))
            .GroupBy(p => p.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var credits = await db.AppliedByInvoiceAsync(ids, ct);
        return figures.OrderByDescending(f => f.Invoice.IssueDate).ThenByDescending(f => f.Invoice.InvoiceId).Select(f =>
        {
            var inv = f.Invoice;
            var st = map.GetValueOrDefault(inv.StatusCodeId) ?? new StatusInfo(string.Empty, string.Empty, "#9CA3AF");
            var c = clients[inv.ClientId];
            var total = inv.Subtotal + inv.TaxAmount;
            var counted = f.Payments.Where(p => p.Counts).ToList();
            return new ConsignmentInvoiceListItemDto(inv.PublicId, inv.InvoiceNumber, st.Code, st.Label, st.Color, c.PublicId, c.Code, c.Name, inv.IssueDate, inv.DueDate,
                f.Lines.Select(l => l.OrderNumber).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(), f.Consigned, f.Returned, f.Paid, f.Balance, total,
                PaymentRules.Balance(total, invoicePaid.GetValueOrDefault(inv.InvoiceId) + credits.GetValueOrDefault(inv.InvoiceId)), counted.Count,
                counted.Count == 0 ? null : counted.Max(p => p.Row.PaidDate));
        }).ToList();
    }
}
