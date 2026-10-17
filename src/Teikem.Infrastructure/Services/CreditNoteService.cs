using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Wms;

namespace Teikem.Infrastructure.Services;

/// <summary>Lo aplicado de notas de crédito a facturas, para que el saldo de una factura lo descuente igual que un pago.</summary>
public static class CreditQueries
{
    public static async Task<Dictionary<int, decimal>> AppliedByInvoiceAsync(this TeikemDbContext db, IEnumerable<int> invoiceIds, CancellationToken ct)
    {
        var list = invoiceIds.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, decimal>();
        var rows = await db.Set<CreditApplication>().AsNoTracking().Where(a => list.Contains(a.InvoiceId)).Select(a => new { a.InvoiceId, a.Amount }).ToListAsync(ct);
        return rows.GroupBy(r => r.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
    }
}

/// <summary>
/// Lote 79 (P6) — notas de crédito. DRAFT → APPROVED (AAL2 en el controlador) → APPLIED (todo su monto aplicado) o VOID.
/// - Con factura: cada línea acredita una línea de la factura (por omisión toda lo facturado a su monto) y no se acredita más de lo facturado de esa línea, descontando lo ya acreditado por notas
///   aprobadas; el impuesto es el de la línea original (copia) proporcional a lo acreditado y nunca pasa del que se cobró. Acreditar menos monto unitario es un ajuste de precio.
/// - Sin factura de Teikem (factura externa o ajuste): líneas libres con el impuesto del motor de impuestos.
/// - Aprobar vuelve a comprobar el tope (bajo bloqueo de la factura) y no mueve inventario. Aplicar descuenta del saldo de una factura abierta del mismo cliente (igual que un pago: la factura
///   pasa a PAGADA si queda en cero) y la nota pasa a APLICADA cuando se agota; lo que no se aplica queda como saldo a favor del cliente.
/// </summary>
public sealed class CreditNoteService(TeikemDbContext db, ITenantContext tenant, StatusService statuses, INumberSequenceService numbers, ILookupCache lookups, ITenantClock clock, TaxService taxes)
{
    public const int MaxPageSize = 200;
    private TenantContext T => (TenantContext)tenant;

    // ================================================================ consultas

    private sealed record StatusInfo(string Code, string Label, string Color);

    private async Task<Dictionary<int, StatusInfo>> StatusMapAsync(CancellationToken ct)
        => (await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CreditCodes.StatusDomain).ToListAsync(ct))
            .ToDictionary(s => s.StatusCodeId, s => new StatusInfo(s.InternalCode, MultilingualText.Resolve(s.LabelJson, T.Lang) ?? s.InternalCode, s.ColorHex ?? "#9CA3AF"));

    private async Task<(string Code, string Label)> LookupAsync(int id, CancellationToken ct)
    {
        var l = await lookups.GetAsync(id, ct);
        return (l?.InternalCode ?? string.Empty, MultilingualText.Resolve(l?.LabelJson, T.Lang) ?? l?.InternalCode ?? string.Empty);
    }

    public async Task<PagedResult<CreditNoteListItemDto>> ListAsync(string? status, Guid? clientPublicId, string? q, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take <= 0 ? 50 : take, 1, MaxPageSize);
        var map = await StatusMapAsync(ct);
        var query = db.CreditNotes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var ids = map.Where(kv => kv.Value.Code.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
            query = query.Where(n => ids.Contains(n.StatusCodeId));
        }
        if (clientPublicId is Guid cp)
        {
            var cid = await db.Set<Client>().AsNoTracking().Where(c => c.PublicId == cp).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
            query = cid is null ? query.Where(n => false) : query.Where(n => n.ClientId == cid);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            var clientIds = await db.Set<Client>().AsNoTracking().Where(c => c.Name.Contains(s)).Select(c => c.ClientId).ToListAsync(ct);
            query = query.Where(n => n.CreditNumber.Contains(s) || clientIds.Contains(n.ClientId));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.CreditNoteId).Skip(skip).Take(take).ToListAsync(ct);
        var clients = await db.Set<Client>().AsNoTracking().Where(c => rows.Select(r => r.ClientId).Contains(c.ClientId)).ToDictionaryAsync(c => c.ClientId, ct);
        var invoiceIds = rows.Where(r => r.InvoiceId != null).Select(r => r.InvoiceId!.Value).ToList();
        var invoiceNumbers = await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);
        var noteIds = rows.Select(r => r.CreditNoteId).ToList();
        var applied = (await db.Set<CreditApplication>().AsNoTracking().Where(a => noteIds.Contains(a.CreditNoteId)).ToListAsync(ct)).GroupBy(a => a.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var items = new List<CreditNoteListItemDto>();
        foreach (var n in rows)
        {
            var st = map[n.StatusCodeId];
            var reason = await LookupAsync(n.ReasonLookupId, ct);
            var total2 = n.Subtotal + n.TaxAmount;
            var ap = applied.GetValueOrDefault(n.CreditNoteId);
            var c = clients[n.ClientId];
            items.Add(new CreditNoteListItemDto(n.PublicId, n.CreditNumber, st.Code, st.Label, st.Color, c.PublicId, c.Name, n.InvoiceId is int i && invoiceNumbers.TryGetValue(i, out var num) ? num : null,
                reason.Code, reason.Label, total2, ap, st.Code == CreditNoteStatuses.Approved ? CreditNoteRules.Balance(total2, ap) : 0m, n.CreatedAtUtc));
        }
        return new PagedResult<CreditNoteListItemDto>(items, total, skip, take);
    }

    public async Task<CreditNoteDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var n = await db.CreditNotes.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw new NotFoundException("Nota de crédito", feminine: true);
        var st = (await StatusMapAsync(ct))[n.StatusCodeId];
        var client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == n.ClientId, ct);
        var invoice = n.InvoiceId is int iid ? await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.InvoiceId == iid, ct) : null;
        var reason = await LookupAsync(n.ReasonLookupId, ct);
        var currency = (await lookups.GetAsync(n.CurrencyLookupId, ct))?.InternalCode ?? "USD";
        var apps = await db.Set<CreditApplication>().AsNoTracking().Where(a => a.CreditNoteId == n.CreditNoteId).OrderBy(a => a.CreditApplicationId).ToListAsync(ct);
        var appInvoiceIds = apps.Select(a => a.InvoiceId).ToList();
        var appInvoices = await db.Set<Invoice>().AsNoTracking().Where(i => appInvoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);
        var productIds = n.Lines.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).ToList();
        var skus = await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, p => p.Sku, ct);
        var lines = new List<CreditNoteLineDto>();
        foreach (var l in n.Lines.OrderBy(x => x.CreditNoteLineId))
            lines.Add(new CreditNoteLineDto(l.CreditNoteLineId, l.InvoiceLineId, l.Description, l.ChargeTypeLookupId is int ct2 ? (await LookupAsync(ct2, ct)).Code : null,
                l.ProductId is int p && skus.TryGetValue(p, out var sku) ? sku : null, l.Quantity, l.UnitAmount, CreditNoteRules.LineSubtotal(l.Quantity, l.UnitAmount), l.TaxRatePct, l.TaxAmount, l.IsTaxExempt));
        var total = n.Subtotal + n.TaxAmount;
        var applied = apps.Sum(a => a.Amount);
        return new CreditNoteDto(n.PublicId, n.CreditNumber, st.Code, st.Label, st.Color, client.PublicId, client.Name, invoice?.PublicId, invoice?.InvoiceNumber, n.ExternalInvoiceRef, reason.Code, reason.Label,
            currency, n.Subtotal, n.TaxAmount, total, applied, st.Code == CreditNoteStatuses.Approved ? CreditNoteRules.Balance(total, applied) : 0m, n.Note, n.CreatedAtUtc, n.ApprovedAtUtc, lines,
            apps.Select(a => new CreditApplicationDto(a.CreditApplicationId, appInvoices[a.InvoiceId].PublicId, appInvoices[a.InvoiceId].InvoiceNumber, a.Amount,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(a.AppliedAtUtc, DateTimeKind.Utc), clock.Zone)))).ToList());
    }

    // ================================================================ crear y cambiar (borrador)

    public async Task<CreditNoteDto> CreateAsync(CreditNoteRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        var reasonId = await RequireReasonAsync(req.Reason, ct);
        var note = NormalizeNote(req.Note);
        var external = string.IsNullOrWhiteSpace(req.ExternalInvoiceRef) ? null : req.ExternalInvoiceRef.Trim();
        if (external is { Length: > CreditNoteRules.MaxRefLength }) throw new ValidationException("externalInvoiceRef", CreditNoteRules.RefTooLong);

        Invoice? invoice = null;
        Client client;
        if (req.InvoicePublicId is Guid ip)
        {
            invoice = await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.PublicId == ip, ct) ?? throw new NotFoundException("Factura", feminine: true);
            var code = await InvoiceStatusCodeAsync(invoice.StatusCodeId, ct);
            if (code is not (InvoiceStatuses.Issued or InvoiceStatuses.Overdue or InvoiceStatuses.Paid)) throw new StatusRuleException(CreditNoteRules.InvoiceNotCreditable);
            client = await db.Set<Client>().AsNoTracking().FirstAsync(c => c.ClientId == invoice.ClientId, ct);
            if (req.ClientPublicId is Guid cpid && cpid != client.PublicId) throw new ValidationException("clientPublicId", CreditNoteRules.InvoiceClientMismatch);
            external = null;
        }
        else
        {
            if (req.ClientPublicId is not Guid cp) throw new ValidationException("clientPublicId", CreditNoteRules.ClientRequired);
            client = await db.Set<Client>().AsNoTracking().FirstOrDefaultAsync(c => c.PublicId == cp, ct) ?? throw new NotFoundException("Cliente");
        }
        var currency = invoice?.CurrencyLookupId ?? client.CurrencyLookupId ?? await lookups.GetIdAsync(LookupDomains.Currency, "USD", ct);

        var id = await db.RunInTransactionAsync(async ct2 =>
        {
            var built = await BuildLinesAsync(client.ClientId, invoice, req.Lines, null, ct2);
            await numbers.EnsureAsync(CreditCodes.NumberKind, null, ct2);
            var seq = await numbers.NextAsync(CreditCodes.NumberKind, null, ct2);
            var initial = await statuses.GetInitialAsync(CreditCodes.StatusDomain, ct2);
            var n = new CreditNote
            {
                PublicId = Guid.NewGuid(), TenantId = T.RequireTenantId(), CreditNumber = NumberFormat.Resolve(CreditCodes.NumberPattern, seq), ClientId = client.ClientId, InvoiceId = invoice?.InvoiceId,
                ExternalInvoiceRef = external, ReasonLookupId = reasonId, CurrencyLookupId = currency, StatusCodeId = initial.StatusCodeId, Note = note,
            };
            n.Lines.AddRange(built);
            Totals(n);
            db.CreditNotes.Add(n);
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
            await statuses.TransitionAsync(CreditCodes.StatusDomain, CreditCodes.EntityType, n.CreditNoteId, null, initial.InternalCode, null, ct2);
            return n.PublicId;
        }, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Cambia el motivo, la nota y las líneas (reemplaza todas) de una nota en borrador. El cliente y la factura no cambian.</summary>
    public async Task<CreditNoteDto> UpdateAsync(Guid publicId, CreditNoteRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        await db.RunInTransactionAsync(async ct2 =>
        {
            var n = await db.CreditNotes.AsTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Nota de crédito", feminine: true);
            if ((await StatusMapAsync(ct2))[n.StatusCodeId].Code != CreditNoteStatuses.Draft) throw new StatusRuleException(CreditNoteRules.NeedsDraft);
            if (req.Reason is not null) n.ReasonLookupId = await RequireReasonAsync(req.Reason, ct2);
            if (req.Note is not null) n.Note = NormalizeNote(req.Note);
            if (req.ExternalInvoiceRef is not null && n.InvoiceId is null)
            {
                var ext = req.ExternalInvoiceRef.Trim();
                if (ext.Length > CreditNoteRules.MaxRefLength) throw new ValidationException("externalInvoiceRef", CreditNoteRules.RefTooLong);
                n.ExternalInvoiceRef = ext.Length == 0 ? null : ext;
            }
            if (req.Lines is not null)
            {
                var invoice = n.InvoiceId is int iid ? await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.InvoiceId == iid, ct2) : null;
                var built = await BuildLinesAsync(n.ClientId, invoice, req.Lines, n.CreditNoteId, ct2);
                db.CreditNoteLines.RemoveRange(n.Lines);
                n.Lines.Clear();
                n.Lines.AddRange(built);
                Totals(n);
            }
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ aprobar, aplicar y anular

    /// <summary>Aprueba la nota: vuelve a comprobar el tope contra lo ya acreditado (bajo bloqueo de la factura) y la pasa a APROBADA. No mueve inventario. El controlador exige AAL2.</summary>
    public async Task<CreditNoteDto> ApproveAsync(Guid publicId, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var n = await db.CreditNotes.AsTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Nota de crédito", feminine: true);
            if ((await StatusMapAsync(ct2))[n.StatusCodeId].Code != CreditNoteStatuses.Draft) throw new StatusRuleException(CreditNoteRules.ApproveNeedsDraft);
            if (n.InvoiceId is int iid)
            {
                await db.LockInvoiceAsync(iid, ct2);
                var invoice = await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).FirstAsync(i => i.InvoiceId == iid, ct2);
                await CheckCapsAsync(invoice, n.Lines, n.CreditNoteId, ct2);
            }
            if (n.Subtotal + n.TaxAmount <= 0m) throw new StatusRuleException(CreditNoteRules.NothingToCredit);
            var to = await statuses.TransitionAsync(CreditCodes.StatusDomain, CreditCodes.EntityType, n.CreditNoteId, n.StatusCodeId, CreditNoteStatuses.Approved, null, ct2);
            (n.StatusCodeId, n.ApprovedBy, n.ApprovedAtUtc) = (to.StatusCodeId, tenant.UserId, clock.UtcNow);
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    public async Task<CreditNoteDto> VoidAsync(Guid publicId, string? reason, CancellationToken ct)
    {
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (why is null) throw new ValidationException("reason", CreditNoteRules.VoidReasonRequired);
        if (why.Length > 250) throw new ValidationException("reason", CreditNoteRules.VoidReasonTooLong);
        await db.RunInTransactionAsync(async ct2 =>
        {
            var n = await db.CreditNotes.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Nota de crédito", feminine: true);
            var code = (await StatusMapAsync(ct2))[n.StatusCodeId].Code;
            if (code is not (CreditNoteStatuses.Draft or CreditNoteStatuses.Approved)) throw new StatusRuleException(CreditNoteRules.VoidNeedsOpen);
            if (await db.Set<CreditApplication>().AsNoTracking().AnyAsync(a => a.CreditNoteId == n.CreditNoteId, ct2)) throw new StatusRuleException(CreditNoteRules.VoidHasApplications);
            var to = await statuses.TransitionAsync(CreditCodes.StatusDomain, CreditCodes.EntityType, n.CreditNoteId, n.StatusCodeId, CreditNoteStatuses.Void, why, ct2);
            n.StatusCodeId = to.StatusCodeId;
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    /// <summary>Aplica parte (o todo) del saldo de una nota aprobada a una factura abierta del mismo cliente. La factura pasa a PAGADA si queda en cero y la nota a APLICADA si se agota.</summary>
    public async Task<CreditNoteDto> ApplyAsync(Guid publicId, CreditApplyRequest? req, CancellationToken ct)
    {
        if (req is null) throw new ValidationException("body", "El cuerpo de la solicitud es obligatorio.");
        if (req.InvoicePublicId is not Guid ip) throw new ValidationException("invoicePublicId", CreditNoteRules.InvoiceNotFound);
        if (req.Amount is null) throw new ValidationException("amount", CreditNoteRules.ApplyAmountRequired);
        var amount = req.Amount.Value;
        if (!PaymentRules.IsValidAmount(amount)) throw new ValidationException("amount", CreditNoteRules.ApplyAmountInvalid);
        var invoiceId = await db.Set<Invoice>().AsNoTracking().Where(i => i.PublicId == ip).Select(i => (int?)i.InvoiceId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Factura", feminine: true);

        await db.RunInTransactionAsync(async ct2 =>
        {
            var inv = await db.LockInvoiceAsync(invoiceId, ct2);
            var n = await db.CreditNotes.AsTracking().FirstOrDefaultAsync(x => x.PublicId == publicId, ct2) ?? throw new NotFoundException("Nota de crédito", feminine: true);
            var noteStatus = (await StatusMapAsync(ct2))[n.StatusCodeId].Code;
            if (noteStatus != CreditNoteStatuses.Approved) throw new StatusRuleException(CreditNoteRules.ApplyNeedsApproved);
            if (n.ClientId != inv.ClientId) throw new StatusRuleException(CreditNoteRules.ApplyOtherClient);
            var invStatuses = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == BillingCodes.InvoiceStatusDomain).ToDictionaryAsync(s => s.StatusCodeId, s => s.InternalCode, ct2);
            if (invStatuses[inv.StatusCodeId] is not (InvoiceStatuses.Issued or InvoiceStatuses.Overdue)) throw new StatusRuleException(CreditNoteRules.ApplyInvoiceNotOpen);

            var noteTotal = n.Subtotal + n.TaxAmount;
            var applied = (await db.Set<CreditApplication>().AsNoTracking().Where(a => a.CreditNoteId == n.CreditNoteId).Select(a => a.Amount).ToListAsync(ct2)).Sum();
            var noteBalance = CreditNoteRules.Balance(noteTotal, applied);
            if (noteBalance <= 0m) throw new StatusRuleException(CreditNoteRules.ApplyNeedsApproved);
            var invTotal = inv.Subtotal + inv.TaxAmount;
            var paid = (await db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == inv.InvoiceId && p.VoidedAtUtc == null).Select(p => p.Amount).ToListAsync(ct2)).Sum();
            var credited = (await db.AppliedByInvoiceAsync(new[] { inv.InvoiceId }, ct2)).GetValueOrDefault(inv.InvoiceId);
            var invBalance = PaymentRules.Balance(invTotal, paid + credited);
            if (invBalance <= 0m) throw new StatusRuleException(CreditNoteRules.ApplyInvoiceNotOpen);
            if (amount > noteBalance) throw new StatusRuleException(CreditNoteRules.ExceedsNoteBalance(amount, noteBalance));
            if (amount > invBalance) throw new StatusRuleException(CreditNoteRules.ExceedsInvoiceBalance(amount, invBalance));

            db.Set<CreditApplication>().Add(new CreditApplication { TenantId = T.RequireTenantId(), CreditNoteId = n.CreditNoteId, InvoiceId = inv.InvoiceId, Amount = amount, AppliedAtUtc = clock.UtcNow, AppliedBy = tenant.UserId });
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
            if (PaymentRules.IsSettled(invTotal, paid + credited + amount))
            {
                var to = await statuses.TransitionAsync(BillingCodes.InvoiceStatusDomain, BillingCodes.InvoiceEntityType, inv.InvoiceId, inv.StatusCodeId, InvoiceStatuses.Paid, $"Pagada con la nota de crédito {n.CreditNumber}", ct2);
                inv.StatusCodeId = to.StatusCodeId;
            }
            if (CreditNoteRules.Balance(noteTotal, applied + amount) <= 0m)
            {
                var to = await statuses.TransitionAsync(CreditCodes.StatusDomain, CreditCodes.EntityType, n.CreditNoteId, n.StatusCodeId, CreditNoteStatuses.Applied, null, ct2);
                n.StatusCodeId = to.StatusCodeId;
            }
            await db.SaveGuardedAsync(BillingService.Concurrency, ct2);
        }, ct);
        return await GetAsync(publicId, ct);
    }

    // ================================================================ ayudas

    private static string? NormalizeNote(string? note)
    {
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > CreditNoteRules.MaxNoteLength }) throw new ValidationException("note", CreditNoteRules.NoteTooLong);
        return n;
    }

    private async Task<int> RequireReasonAsync(string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("reason", CreditNoteRules.ReasonRequired);
        var code = reason.Trim().ToUpperInvariant();
        if (!CreditReasons.All.Contains(code)) throw new ValidationException("reason", CreditNoteRules.ReasonInvalid);
        return await lookups.TryGetIdAsync(CreditCodes.ReasonDomain, code, ct) ?? throw new ValidationException("reason", CreditNoteRules.ReasonInvalid);
    }

    private async Task<string> InvoiceStatusCodeAsync(int statusCodeId, CancellationToken ct)
        => await db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId).Select(s => s.InternalCode).FirstAsync(ct);

    private static void Totals(CreditNote n)
    {
        n.Subtotal = n.Lines.Sum(l => CreditNoteRules.LineSubtotal(l.Quantity, l.UnitAmount));
        n.TaxAmount = n.Lines.Sum(l => l.TaxAmount);
    }

    /// <summary>Lo ya acreditado (valor e impuesto) por línea de factura en notas aprobadas o aplicadas, sin contar la nota `exceptNoteId`.</summary>
    private async Task<Dictionary<int, (decimal Value, decimal Tax)>> CreditedAsync(IReadOnlyCollection<int> invoiceLineIds, int? exceptNoteId, CancellationToken ct)
    {
        var counted = await db.StatusCodes.AsNoTracking().Where(s => s.Entity == CreditCodes.StatusDomain && (s.InternalCode == CreditNoteStatuses.Approved || s.InternalCode == CreditNoteStatuses.Applied))
            .Select(s => s.StatusCodeId).ToListAsync(ct);
        var rows = await (from l in db.CreditNoteLines.AsNoTracking()
                          join n in db.CreditNotes.AsNoTracking() on l.CreditNoteId equals n.CreditNoteId
                          where l.InvoiceLineId != null && invoiceLineIds.Contains(l.InvoiceLineId.Value) && counted.Contains(n.StatusCodeId) && n.CreditNoteId != exceptNoteId
                          select new { l.InvoiceLineId, l.Quantity, l.UnitAmount, l.TaxAmount }).ToListAsync(ct);
        return rows.GroupBy(r => r.InvoiceLineId!.Value).ToDictionary(g => g.Key, g => (g.Sum(r => CreditNoteRules.LineSubtotal(r.Quantity, r.UnitAmount)), g.Sum(r => r.TaxAmount)));
    }

    private async Task CheckCapsAsync(Invoice invoice, IEnumerable<CreditNoteLine> lines, int exceptNoteId, CancellationToken ct)
    {
        var byLine = lines.Where(l => l.InvoiceLineId != null).GroupBy(l => l.InvoiceLineId!.Value).ToList();
        var credited = await CreditedAsync(byLine.Select(g => g.Key).ToList(), exceptNoteId, ct);
        foreach (var g in byLine)
        {
            var il = invoice.Lines.First(x => x.InvoiceLineId == g.Key);
            var value = g.Sum(l => CreditNoteRules.LineSubtotal(l.Quantity, l.UnitAmount));
            var creditable = CreditNoteRules.Creditable(CreditNoteRules.LineSubtotal(il.Quantity, il.UnitAmount), credited.GetValueOrDefault(g.Key).Value);
            if (value > creditable) throw new StatusRuleException(CreditNoteRules.ExceedsCreditable(il.Description, creditable));
        }
    }

    private async Task<List<CreditNoteLine>> BuildLinesAsync(int clientId, Invoice? invoice, IReadOnlyList<CreditNoteLineRequest>? req, int? exceptNoteId, CancellationToken ct)
    {
        if (req is null || req.Count == 0) throw new ValidationException("lines", CreditNoteRules.LinesRequired);
        if (req.Count > CreditNoteRules.MaxLines) throw new ValidationException("lines", CreditNoteRules.TooManyLines);
        var result = new List<CreditNoteLine>();

        if (invoice is not null)
        {
            var ids = req.Where(l => l.InvoiceLineId != null).Select(l => l.InvoiceLineId!.Value).Distinct().ToList();
            var credited = await CreditedAsync(ids, exceptNoteId, ct);
            var accValue = new Dictionary<int, decimal>();
            var accTax = new Dictionary<int, decimal>();
            for (var i = 0; i < req.Count; i++)
            {
                var r = req[i];
                if (r.InvoiceLineId is not int lid) throw new ValidationException($"lines[{i}]", CreditNoteRules.InvoiceLineRequired);
                var il = invoice.Lines.FirstOrDefault(x => x.InvoiceLineId == lid) ?? throw new ValidationException($"lines[{i}]", CreditNoteRules.InvoiceLineNotFound);
                var qty = r.Quantity ?? il.Quantity;
                var unit = r.UnitAmount ?? il.UnitAmount;
                if (qty <= 0m || Math.Round(qty, 3) != qty) throw new ValidationException($"lines[{i}].quantity", CreditNoteRules.QuantityInvalid);
                if (unit <= 0m || Math.Round(unit, 4) != unit) throw new ValidationException($"lines[{i}].unitAmount", CreditNoteRules.AmountInvalid);
                var description = string.IsNullOrWhiteSpace(r.Description) ? il.Description : r.Description.Trim();
                if (description.Length > CreditNoteRules.MaxDescriptionLength) throw new ValidationException($"lines[{i}].description", CreditNoteRules.DescriptionTooLong);
                var value = CreditNoteRules.LineSubtotal(qty, unit);
                var soFar = credited.GetValueOrDefault(lid).Value + accValue.GetValueOrDefault(lid);
                var creditable = CreditNoteRules.Creditable(CreditNoteRules.LineSubtotal(il.Quantity, il.UnitAmount), soFar);
                if (value > creditable) throw new StatusRuleException(CreditNoteRules.ExceedsCreditable(il.Description, creditable));
                var tax = CreditNoteRules.TaxFor(value, il.TaxRatePct, il.IsTaxExempt, il.TaxAmount, credited.GetValueOrDefault(lid).Tax + accTax.GetValueOrDefault(lid));
                accValue[lid] = accValue.GetValueOrDefault(lid) + value;
                accTax[lid] = accTax.GetValueOrDefault(lid) + tax;
                result.Add(new CreditNoteLine
                {
                    InvoiceLineId = lid, ProductId = il.ProductId, ChargeTypeLookupId = il.ChargeTypeLookupId, Description = description, Quantity = qty, UnitAmount = unit,
                    TaxRateId = il.TaxRateId, TaxRatePct = il.TaxRatePct, TaxAmount = tax, IsTaxExempt = il.IsTaxExempt,
                });
            }
        }
        else
        {
            var taxLines = new List<TaxCalcLineRequest>();
            for (var i = 0; i < req.Count; i++)
            {
                var r = req[i];
                if (r.InvoiceLineId is not null) throw new ValidationException($"lines[{i}]", CreditNoteRules.InvoiceLineOnFree);
                var description = r.Description?.Trim();
                if (string.IsNullOrEmpty(description)) throw new ValidationException($"lines[{i}].description", CreditNoteRules.DescriptionRequired);
                if (description.Length > CreditNoteRules.MaxDescriptionLength) throw new ValidationException($"lines[{i}].description", CreditNoteRules.DescriptionTooLong);
                var qty = r.Quantity ?? 0m;
                if (qty <= 0m || Math.Round(qty, 3) != qty) throw new ValidationException($"lines[{i}].quantity", CreditNoteRules.QuantityInvalid);
                var unit = r.UnitAmount ?? 0m;
                if (unit <= 0m || Math.Round(unit, 4) != unit) throw new ValidationException($"lines[{i}].unitAmount", CreditNoteRules.AmountInvalid);
                int? productId = null;
                int? chargeId = null;
                string? chargeCode = null;
                if (r.ProductPublicId is Guid pp)
                {
                    productId = await db.Set<Product>().AsNoTracking().Where(p => p.PublicId == pp).Select(p => (int?)p.ProductId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Producto");
                    chargeCode = BillingCodes.ProductSale;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(r.ChargeType)) throw new ValidationException($"lines[{i}].chargeType", CreditNoteRules.ChargeTypeRequired);
                    chargeCode = r.ChargeType.Trim().ToUpperInvariant();
                }
                chargeId = await lookups.TryGetIdAsync(BillingCodes.ChargeTypeDomain, chargeCode, ct) ?? throw new ValidationException($"lines[{i}].chargeType", CreditNoteRules.ChargeTypeInvalid);
                result.Add(new CreditNoteLine { ProductId = productId, ChargeTypeLookupId = chargeId, Description = description, Quantity = qty, UnitAmount = unit });
                taxLines.Add(productId is int pid
                    ? new TaxCalcLineRequest(i.ToString(), pid, null, CreditNoteRules.LineSubtotal(qty, unit))
                    : new TaxCalcLineRequest(i.ToString(), null, chargeCode, CreditNoteRules.LineSubtotal(qty, unit)));
            }
            var calc = await taxes.CalculateAsync(new TaxCalcRequest(clientId, clock.Today, taxLines), ct);
            for (var i = 0; i < result.Count; i++)
            {
                var c = calc.Lines.First(x => x.Key == i.ToString());
                result[i].UnitAmount = Math.Round(c.Net / result[i].Quantity, 4, MidpointRounding.AwayFromZero);
                (result[i].TaxRateId, result[i].TaxRatePct, result[i].TaxAmount, result[i].IsTaxExempt) = (c.TaxRateId, c.TaxRatePct, c.TaxAmount, c.IsTaxExempt);
            }
        }
        return result;
    }
}
