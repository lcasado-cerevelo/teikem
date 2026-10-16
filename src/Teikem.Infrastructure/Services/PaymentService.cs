using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 75 (P5) — pagos de las facturas, estado de cuenta y cuentas por cobrar.
/// - Un pago se registra contra una factura emitida o vencida (nunca pasa del saldo, hasta 2 decimales, método del catálogo PaymentMethod); la factura se bloquea (UPDLOCK) para que dos pagos a la
///   vez no pasen del saldo. Cuando lo pagado cubre el total, la factura pasa a PAID por StatusService.
/// - Un pago no se borra: se anula con motivo (deja de contar en el saldo). No se anula un pago de una factura ya pagada por completo (PAID es terminal): se corrige con una nota de crédito (P6).
/// - El estado de cuenta del cliente lista las facturas emitidas (débito) y los pagos (crédito) del período con saldo corrido, y la antigüedad de lo abierto. Las cuentas por cobrar son lo mismo por cliente.
/// - MarkOverdueAsync (trabajo BILLING_OVERDUE_SCAN) pasa a OVERDUE lo emitido cuyo vencimiento ya pasó.
/// </summary>
public sealed class PaymentService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, ILookupCache lookups, ITenantClock clock, BillingService billing)
{
    private TenantContext T => (TenantContext)tenant;

    private DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.Zone));

    // ================================================================ registrar y anular pagos

    public async Task<InvoiceDto> RecordAsync(Guid invoicePublicId, PaymentRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.Amount is null) throw new ValidationException("amount", PaymentRules.AmountRequired);
        var amount = req.Amount.Value;
        if (!PaymentRules.IsValidAmount(amount)) throw new ValidationException("amount", PaymentRules.AmountInvalid);
        if (string.IsNullOrWhiteSpace(req.Method)) throw new ValidationException("method", PaymentRules.MethodRequired);
        var methodId = await lookups.TryGetIdAsync(BillingCodes.PaymentMethodDomain, req.Method.Trim().ToUpperInvariant(), ct)
                       ?? throw new ValidationException("method", PaymentRules.MethodInvalid);
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
            var map = await StatusCodesAsync(ct2);
            var code = map[inv.StatusCodeId];
            if (code is not (InvoiceStatuses.Issued or InvoiceStatuses.Overdue)) throw new StatusRuleException(PaymentRules.NotPayable);
            if (inv.IssueDate is DateOnly issued && paidDate < issued) throw new ValidationException("paidDate", PaymentRules.DateBeforeIssue);
            var total = inv.Subtotal + inv.TaxAmount;
            var paid = await ActivePaidAsync(inv.InvoiceId, ct2);
            var balance = PaymentRules.Balance(total, paid);
            if (amount > balance) throw new StatusRuleException(PaymentRules.ExceedsBalance(amount, balance));

            db.Set<Payment>().Add(new Payment
            {
                TenantId = T.RequireTenantId(), InvoiceId = inv.InvoiceId, PaymentMethodLookupId = methodId, Amount = amount, Reference = reference, RecordedBy = tenant.UserId,
                PaidAtUtc = paidDate == today ? clock.UtcNow : clock.StartOfDayUtc(paidDate).AddHours(12),
            });
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
            if (PaymentRules.IsSettled(total, paid + amount))
            {
                var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Paid, "Pagada por completo", ct2);
                inv.StatusCodeId = to.StatusCodeId;
                await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
            }
        }, ct);
        return await billing.GetInvoiceAsync(invoicePublicId, ct);
    }

    /// <summary>Anula un pago con motivo: deja de contar en el saldo de la factura. No aplica si la factura ya quedó pagada por completo (terminal).</summary>
    public async Task<InvoiceDto> VoidAsync(int paymentId, string? reason, CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note is null) throw new ValidationException("reason", PaymentRules.VoidReasonRequired);
        if (note.Length > PaymentRules.MaxVoidReasonLength) throw new ValidationException("reason", PaymentRules.VoidReasonTooLong);
        var found = await db.Set<Payment>().AsNoTracking().Where(p => p.PaymentId == paymentId).Select(p => new { p.InvoiceId }).FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Pago");
        var publicId = await db.Set<Invoice>().AsNoTracking().Where(i => i.InvoiceId == found.InvoiceId).Select(i => i.PublicId).FirstAsync(ct);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var inv = await db.LockInvoiceAsync(found.InvoiceId, ct2);
            var payment = await db.Set<Payment>().AsTracking().FirstAsync(p => p.PaymentId == paymentId, ct2);
            if (payment.VoidedAtUtc != null) throw new StatusRuleException(PaymentRules.AlreadyVoided);
            if ((await StatusCodesAsync(ct2))[inv.StatusCodeId] == InvoiceStatuses.Paid) throw new StatusRuleException(PaymentRules.VoidOnPaidInvoice);
            (payment.VoidedAtUtc, payment.VoidedBy, payment.VoidReason) = (clock.UtcNow, tenant.UserId, note);
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await billing.GetInvoiceAsync(publicId, ct);
    }

    // ================================================================ estado de cuenta y cuentas por cobrar

    private sealed record InvoiceFact(int InvoiceId, Guid PublicId, string Number, DateOnly? IssueDate, DateOnly? DueDate, decimal Total, string Status);

    public async Task<StatementDto> StatementAsync(Guid clientPublicId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = clock.Today;
        var end = to ?? today;
        var start = from ?? end.AddDays(-90);
        if (end < start) throw new ValidationException("to", PaymentRules.StatementPeriodInvalid);
        var client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == clientPublicId, ct) ?? throw new NotFoundException("Cliente");
        var invoices = await InvoicesOfAsync(new[] { client.ClientId }, ct);
        var ids = invoices.Select(i => i.InvoiceId).ToList();
        var payments = ids.Count == 0 ? new List<Payment>()
            : await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.VoidedAtUtc == null).OrderBy(p => p.PaidAtUtc).ThenBy(p => p.PaymentId).ToListAsync(ct);
        var byId = invoices.ToDictionary(i => i.InvoiceId);

        // movimientos de todo el historial, ordenados: la factura antes que sus pagos del mismo día
        var moves = new List<(DateOnly Date, int Order, string Kind, string Ref, string Desc, decimal Debit, decimal Credit, Guid Invoice)>();
        foreach (var i in invoices)
            moves.Add((i.IssueDate ?? LocalDate(DateTime.UtcNow), 0, "INVOICE", i.Number, "Factura " + i.Number, i.Total, 0m, i.PublicId));
        foreach (var p in payments)
        {
            var inv = byId[p.InvoiceId];
            var m = await lookups.GetAsync(p.PaymentMethodLookupId, ct);
            var method = MultilingualText.Resolve(m?.LabelJson, T.Lang) ?? m?.InternalCode ?? string.Empty;
            moves.Add((LocalDate(p.PaidAtUtc), 1, "PAYMENT", p.Reference ?? inv.Number, $"Pago {method} de la factura {inv.Number}", 0m, p.Amount, inv.PublicId));
        }
        moves = moves.OrderBy(x => x.Date).ThenBy(x => x.Order).ThenBy(x => x.Ref, StringComparer.Ordinal).ToList();

        var opening = moves.Where(x => x.Date < start).Sum(x => x.Debit - x.Credit);
        var lines = new List<StatementLineDto>();
        var running = opening;
        foreach (var x in moves.Where(x => x.Date >= start && x.Date <= end))
        {
            running += x.Debit - x.Credit;
            lines.Add(new StatementLineDto(x.Date, x.Kind, x.Ref, x.Desc, x.Debit, x.Credit, running, x.Invoice));
        }
        var aging = AgingOf(invoices, payments, today);
        return new StatementDto(client.PublicId, client.Name, start, end, opening, lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), running, lines, aging);
    }

    public async Task<ReceivablesDto> ReceivablesAsync(DateOnly? asOf, CancellationToken ct)
    {
        var day = asOf ?? clock.Today;
        var invoices = (await InvoicesOfAsync(null, ct)).Where(i => i.Status is InvoiceStatuses.Issued or InvoiceStatuses.Overdue).ToList();
        var ids = invoices.Select(i => i.InvoiceId).ToList();
        var payments = ids.Count == 0 ? new List<Payment>() : await db.Set<Payment>().AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.VoidedAtUtc == null).ToListAsync(ct);
        var clientOf = await db.Set<Invoice>().AsNoTracking().Where(i => ids.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.ClientId, ct);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => clientOf.Values.Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var rows = new List<ReceivableRowDto>();
        foreach (var g in invoices.GroupBy(i => clientOf[i.InvoiceId]))
        {
            var mine = g.ToList();
            var aging = AgingOf(mine, payments.Where(p => mine.Any(i => i.InvoiceId == p.InvoiceId)).ToList(), day);
            if (aging.Total <= 0m) continue;
            var open = mine.Count(i => PaymentRules.Balance(i.Total, payments.Where(p => p.InvoiceId == i.InvoiceId).Sum(p => p.Amount)) > 0m);
            var c = clients[g.Key];
            rows.Add(new ReceivableRowDto(c.PublicId, c.Name, open, aging.Total, aging, mine.Where(i => i.DueDate != null).Select(i => i.DueDate).Min()));
        }
        rows = rows.OrderByDescending(r => r.Balance).ThenBy(r => r.ClientName, StringComparer.OrdinalIgnoreCase).ToList();
        var total = new AgingDto(rows.Sum(r => r.Aging.Current), rows.Sum(r => r.Aging.Days1To30), rows.Sum(r => r.Aging.Days31To60), rows.Sum(r => r.Aging.Days61To90), rows.Sum(r => r.Aging.Over90), rows.Sum(r => r.Aging.Total));
        return new ReceivablesDto(day, rows, total);
    }

    private async Task<List<InvoiceFact>> InvoicesOfAsync(IReadOnlyCollection<int>? clientIds, CancellationToken ct)
    {
        var map = await StatusCodesAsync(ct);
        var counted = map.Where(kv => kv.Value is InvoiceStatuses.Issued or InvoiceStatuses.Overdue or InvoiceStatuses.Paid).Select(kv => kv.Key).ToList();
        var q = db.Set<Invoice>().AsNoTracking().Where(i => counted.Contains(i.StatusCodeId));
        if (clientIds is not null) q = q.Where(i => clientIds.Contains(i.ClientId));
        var rows = await q.ToListAsync(ct);
        return rows.Select(i => new InvoiceFact(i.InvoiceId, i.PublicId, i.InvoiceNumber, i.IssueDate, i.DueDate, i.Subtotal + i.TaxAmount, map[i.StatusCodeId])).ToList();
    }

    private static AgingDto AgingOf(IReadOnlyList<InvoiceFact> invoices, IReadOnlyList<Payment> payments, DateOnly asOf)
    {
        var buckets = new decimal[5];
        foreach (var i in invoices)
        {
            var balance = PaymentRules.Balance(i.Total, payments.Where(p => p.InvoiceId == i.InvoiceId).Sum(p => p.Amount));
            if (balance > 0m) buckets[PaymentRules.AgingBucket(i.DueDate, asOf)] += balance;
        }
        return new AgingDto(buckets[0], buckets[1], buckets[2], buckets[3], buckets[4], buckets.Sum());
    }

    // ================================================================ vencimiento

    /// <summary>Pasa a Vencida lo emitido cuyo vencimiento ya pasó (cada factura en su propia transacción). Devuelve cuántas.</summary>
    public async Task<int> MarkOverdueAsync(CancellationToken ct)
    {
        var map = await StatusCodesAsync(ct);
        var issuedId = map.First(kv => kv.Value == InvoiceStatuses.Issued).Key;
        var today = clock.Today;
        var ids = await db.Set<Invoice>().AsNoTracking().Where(i => i.StatusCodeId == issuedId && i.DueDate != null && i.DueDate < today).Select(i => i.InvoiceId).ToListAsync(ct);
        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                await db.RunInTransactionAsync(async ct2 =>
                {
                    var inv = await db.LockInvoiceAsync(id, ct2);
                    if (inv.StatusCodeId != issuedId) return;
                    var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Overdue, "Venció el plazo de pago", ct2);
                    inv.StatusCodeId = to.StatusCodeId;
                    await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
                    done++;
                }, ct);
            }
            catch (TeikemException) { /* otra operación la cambió entre tanto: se revisa en la próxima pasada */ }
        }
        return done;
    }

    // ================================================================ ayudas

    private async Task<Dictionary<int, string>> StatusCodesAsync(CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct);

    private async Task<decimal> ActivePaidAsync(int invoiceId, CancellationToken ct)
        => (await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoiceId && p.VoidedAtUtc == null).Select(p => p.Amount).ToListAsync(ct)).Sum();
}
