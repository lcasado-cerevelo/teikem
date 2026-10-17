using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 79 (P6): notas de crédito — crear sobre una factura (tope de lo facturado y de lo ya acreditado, impuesto proporcional), aprobar, aplicar a facturas, anular, saldo a favor en el estado de cuenta.
public sealed class CreditNoteTests
{
    [Fact]
    public void Rules_creditable_proportional_tax_and_balances()
    {
        Assert.Equal(60m, CreditNoteRules.Creditable(100m, 40m));
        Assert.Equal(0m, CreditNoteRules.Creditable(100m, 120m));   // nunca negativo
        Assert.Equal(4m, CreditNoteRules.TaxFor(40m, 10m, false, 10m, 0m));   // 10 % de 40
        Assert.Equal(0m, CreditNoteRules.TaxFor(40m, 10m, true, 10m, 0m));   // exenta
        Assert.Equal(0m, CreditNoteRules.TaxFor(40m, null, false, 10m, 0m));   // sin tasa
        Assert.Equal(1m, CreditNoteRules.TaxFor(40m, 10m, false, 10m, 9m));   // nunca más del impuesto que queda (10 − 9)
        Assert.Equal(0m, CreditNoteRules.TaxFor(40m, 10m, false, 10m, 10m));
        Assert.Equal(1.15m, CreditNoteRules.TaxFor(10m, 11.5m, false, 11.5m, 0m));
        Assert.Equal(7.5m, CreditNoteRules.LineSubtotal(3m, 2.5m));
        Assert.Equal(30m, CreditNoteRules.Balance(100m, 70m));
        Assert.Equal(0m, CreditNoteRules.Balance(100m, 130m));
        Assert.Equal("No se puede acreditar más de lo facturado: de «Flete» quedan 60.00 por acreditar.", CreditNoteRules.ExceedsCreditable("Flete", 60m));
    }

    private sealed class World(WmsFixture f, Client c, BillingService billing, PaymentService payments, CreditNoteService credits)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public BillingService Billing { get; } = billing;
        public PaymentService Payments { get; } = payments;
        public CreditNoteService Credits { get; } = credits;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<RateService>();
            s.AddSingleton<OrderQuoteService>();
            s.AddSingleton<BillingService>();
            s.AddSingleton<PaymentService>();
            s.AddSingleton<CreditNoteService>();
        });
        f.SetModules(ModuleKeys.Billing);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<BillingService>(), f.Get<PaymentService>(), f.Get<CreditNoteService>());
    }

    /// <summary>Una factura emitida de una línea de `amount` con impuesto del 10 % copiado en la línea, de una orden de transporte entregada y cotizada.</summary>
    private static async Task<InvoiceDto> IssuedAsync(World x, string number, decimal amount, Client? client = null, bool withTax = true)
    {
        var c = client ?? x.C;
        var o = await x.F.AddOrderAsync(c.ClientId, number, OrderStatuses.Delivered);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.QuotedAmount, t.QuotedAtUtc) = (amount, DateTime.UtcNow.AddDays(-1));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var run = await x.Billing.GenerateAsync(new BillingRunRequest(c.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), default);
        if (withTax)
        {
            var inv = await x.F.Db.Set<Invoice>().AsTracking().Include(i => i.Lines).SingleAsync(i => i.PublicId == run.Invoices[0].PublicId);
            var line = inv.Lines.Single();
            (line.TaxRatePct, line.TaxAmount, line.IsTaxExempt) = (10m, amount / 10m, false);
            inv.TaxAmount = amount / 10m;
            await x.F.Db.SaveChangesAsync();
            x.F.Db.ChangeTracker.Clear();
        }
        await x.Billing.ReviewAsync(run.Id, default);
        var issued = (await x.Billing.ApproveAsync(run.Id, default)).Invoices[0];
        return await x.Billing.GetInvoiceAsync(issued.PublicId, default);
    }

    private static CreditNoteRequest OnInvoice(InvoiceDto inv, string reason = "PRICE_ADJUSTMENT", decimal? unit = null, decimal? qty = null)
        => new(InvoicePublicId: inv.PublicId, Reason: reason, Lines: new[] { new CreditNoteLineRequest(InvoiceLineId: inv.Lines[0].Id, UnitAmount: unit, Quantity: qty) });

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task A_note_on_an_invoice_defaults_to_the_whole_line_with_its_copied_tax_and_starts_as_a_draft()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);   // 100 + 10 de impuesto
        var n = await x.Credits.CreateAsync(OnInvoice(inv, "RETURN"), default);
        Assert.Equal(("NC-00001", "DRAFT", "RETURN", inv.Number), (n.Number, n.Status, n.Reason, n.InvoiceNumber));
        Assert.Equal((100m, 10m, 110m, 0m), (n.Subtotal, n.TaxAmount, n.Total, n.Balance));   // el saldo solo existe aprobada
        var line = Assert.Single(n.Lines);
        Assert.Equal((inv.Lines[0].Id, 100m, 10m, 1m), (line.InvoiceLineId, line.UnitAmount, line.TaxRatePct, line.Quantity));
        Assert.Equal(110m, (await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Balance);   // la factura no cambia hasta aplicar
        Assert.Equal("NC-00002", (await x.Credits.CreateAsync(OnInvoice(inv, unit: 10m), default)).Number);
    }

    [Fact]
    public async Task A_price_adjustment_credits_less_than_the_line_with_proportional_tax()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 40m), default);
        Assert.Equal((40m, 4m, 44m), (n.Subtotal, n.TaxAmount, n.Total));
        var changed = await x.Credits.UpdateAsync(n.PublicId, new CreditNoteRequest(Note: "Descuento pactado", Lines: new[] { new CreditNoteLineRequest(InvoiceLineId: inv.Lines[0].Id, UnitAmount: 25m, Description: "Rebaja") }), default);
        Assert.Equal((25m, 2.5m, "Descuento pactado", "Rebaja"), (changed.Subtotal, changed.TaxAmount, changed.Note, changed.Lines[0].Description));
        Assert.Single(changed.Lines);   // las líneas se reemplazan
    }

    [Fact]
    public async Task The_cap_counts_what_approved_notes_already_credited_and_is_checked_again_when_approving()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var a = await x.Credits.CreateAsync(OnInvoice(inv, unit: 40m), default);
        var b = await x.Credits.CreateAsync(OnInvoice(inv, unit: 70m), default);   // las dos caben mientras ninguna esté aprobada
        Assert.Equal(CreditNoteRules.ExceedsCreditable(inv.Lines[0].Description, 100m), await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv, unit: 100.01m), default)));
        await x.Credits.ApproveAsync(a.PublicId, default);
        Assert.Equal(CreditNoteRules.ExceedsCreditable(inv.Lines[0].Description, 60m), await MsgOf(() => x.Credits.ApproveAsync(b.PublicId, default)));   // quedan 60
        Assert.Equal(CreditNoteRules.ExceedsCreditable(inv.Lines[0].Description, 60m), await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv, unit: 61m), default)));
        var ok = await x.Credits.CreateAsync(OnInvoice(inv, unit: 60m), default);
        Assert.Equal("APPROVED", (await x.Credits.ApproveAsync(ok.PublicId, default)).Status);
        // el impuesto acreditado nunca pasa del cobrado: 4 + 6 = 10
        Assert.Equal(10m, (await x.Credits.GetAsync(a.PublicId, default)).TaxAmount + (await x.Credits.GetAsync(ok.PublicId, default)).TaxAmount);
    }

    [Fact]
    public async Task Creating_validates_reason_lines_invoice_and_free_lines()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var otherClient = await x.F.AddClientAsync("C2");
        var ok = new CreditNoteLineRequest(InvoiceLineId: inv.Lines[0].Id);

        Assert.Equal(CreditNoteRules.ReasonRequired, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Lines: new[] { ok }), default)));
        Assert.Equal(CreditNoteRules.ReasonInvalid, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Reason: "NADA", Lines: new[] { ok }), default)));
        Assert.Equal(CreditNoteRules.LinesRequired, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Reason: "RETURN", Lines: Array.Empty<CreditNoteLineRequest>()), default)));
        Assert.Equal(CreditNoteRules.NoteTooLong, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Reason: "RETURN", Note: new string('n', 501), Lines: new[] { ok }), default)));
        Assert.Equal(CreditNoteRules.InvoiceLineRequired, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Reason: "RETURN", Lines: new[] { new CreditNoteLineRequest(Description: "x") }), default)));
        Assert.Equal(CreditNoteRules.InvoiceLineNotFound, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: inv.PublicId, Reason: "RETURN", Lines: new[] { new CreditNoteLineRequest(InvoiceLineId: 99999) }), default)));
        Assert.Equal(CreditNoteRules.QuantityInvalid, await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv, qty: 0m), default)));
        Assert.Equal(CreditNoteRules.AmountInvalid, await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv, unit: 0m), default)));
        Assert.Equal(CreditNoteRules.AmountInvalid, await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv, unit: 1.00001m), default)));
        Assert.Equal(CreditNoteRules.InvoiceClientMismatch, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(ClientPublicId: otherClient.PublicId, InvoicePublicId: inv.PublicId, Reason: "RETURN", Lines: new[] { ok }), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.CreateAsync(new CreditNoteRequest(InvoicePublicId: Guid.NewGuid(), Reason: "RETURN", Lines: new[] { ok }), default));

        // sin factura
        Assert.Equal(CreditNoteRules.ClientRequired, await MsgOf(() => x.Credits.CreateAsync(new CreditNoteRequest(Reason: "RETURN", Lines: new[] { ok }), default)));
        var free = new CreditNoteRequest(ClientPublicId: x.C.PublicId, Reason: "SERVICE_FAILURE");
        Assert.Equal(CreditNoteRules.InvoiceLineOnFree, await MsgOf(() => x.Credits.CreateAsync(free with { Lines = new[] { ok } }, default)));
        Assert.Equal(CreditNoteRules.DescriptionRequired, await MsgOf(() => x.Credits.CreateAsync(free with { Lines = new[] { new CreditNoteLineRequest(Quantity: 1m, UnitAmount: 5m, ChargeType: "FREIGHT") } }, default)));
        Assert.Equal(CreditNoteRules.ChargeTypeRequired, await MsgOf(() => x.Credits.CreateAsync(free with { Lines = new[] { new CreditNoteLineRequest(Description: "Flete", Quantity: 1m, UnitAmount: 5m) } }, default)));
        Assert.Equal(CreditNoteRules.ChargeTypeInvalid, await MsgOf(() => x.Credits.CreateAsync(free with { Lines = new[] { new CreditNoteLineRequest(Description: "Flete", Quantity: 1m, UnitAmount: 5m, ChargeType: "NADA") } }, default)));
        Assert.Equal(CreditNoteRules.QuantityInvalid, await MsgOf(() => x.Credits.CreateAsync(free with { Lines = new[] { new CreditNoteLineRequest(Description: "Flete", UnitAmount: 5m, ChargeType: "FREIGHT") } }, default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.CreateAsync(free with { ClientPublicId = Guid.NewGuid(), Lines = new[] { new CreditNoteLineRequest(Description: "Flete", Quantity: 1m, UnitAmount: 5m, ChargeType: "FREIGHT") } }, default));
    }

    [Fact]
    public async Task A_free_note_for_an_external_invoice_takes_its_tax_from_the_tax_engine()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var n = await x.Credits.CreateAsync(new CreditNoteRequest(ClientPublicId: x.C.PublicId, ExternalInvoiceRef: " QB-1001 ", Reason: "SERVICE_FAILURE", Note: "Entrega tardía",
            Lines: new[] { new CreditNoteLineRequest(Description: "Flete de la orden 77", Quantity: 2m, UnitAmount: 12.5m, ChargeType: "freight") }), default);
        Assert.Equal(("QB-1001", null, 25m), (n.ExternalInvoiceRef, n.InvoiceNumber, n.Subtotal));
        Assert.Equal("FREIGHT", n.Lines[0].ChargeType);
        Assert.Equal(n.Subtotal + n.TaxAmount, n.Total);
    }

    [Fact]
    public async Task Draft_only_can_change_approving_asks_for_a_draft_and_voiding_needs_a_reason()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 50m), default);
        var approved = await x.Credits.ApproveAsync(n.PublicId, default);
        Assert.Equal(("APPROVED", 55m), (approved.Status, approved.Balance));
        Assert.NotNull(approved.ApprovedAtUtc);
        Assert.Equal(CreditNoteRules.NeedsDraft, await MsgOf(() => x.Credits.UpdateAsync(n.PublicId, new CreditNoteRequest(Note: "x"), default)));
        Assert.Equal(CreditNoteRules.ApproveNeedsDraft, await MsgOf(() => x.Credits.ApproveAsync(n.PublicId, default)));

        Assert.Equal(CreditNoteRules.VoidReasonRequired, await MsgOf(() => x.Credits.VoidAsync(n.PublicId, " ", default)));
        Assert.Equal(CreditNoteRules.VoidReasonTooLong, await MsgOf(() => x.Credits.VoidAsync(n.PublicId, new string('x', 251), default)));
        Assert.Equal("VOID", (await x.Credits.VoidAsync(n.PublicId, "Se pidió por error", default)).Status);
        Assert.Equal(CreditNoteRules.VoidNeedsOpen, await MsgOf(() => x.Credits.VoidAsync(n.PublicId, "otra vez", default)));
        // anulada: lo acreditado vuelve a estar disponible
        var again = await x.Credits.CreateAsync(OnInvoice(inv, unit: 100m), default);
        Assert.Equal("APPROVED", (await x.Credits.ApproveAsync(again.PublicId, default)).Status);
        await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.ApproveAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Applying_reduces_the_invoice_balance_settles_it_and_uses_up_the_note()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);   // total 110
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 50m), default);   // 55
        Assert.Equal(CreditNoteRules.ApplyNeedsApproved, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 10m), default)));   // borrador
        await x.Credits.ApproveAsync(n.PublicId, default);

        Assert.Equal(CreditNoteRules.ApplyAmountRequired, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, null), default)));
        Assert.Equal(CreditNoteRules.ApplyAmountInvalid, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 0.001m), default)));
        Assert.Equal(CreditNoteRules.ExceedsNoteBalance(60m, 55m), await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 60m), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(Guid.NewGuid(), 5m), default));

        var part = await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 20m), default);
        Assert.Equal(("APPROVED", 20m, 35m), (part.Status, part.Applied, part.Balance));
        var after = await x.Billing.GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((20m, 90m, "ISSUED"), (after.Credited, after.Balance, after.Status));

        var rest = await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 35m), default);
        Assert.Equal(("APPLIED", 0m, 2), (rest.Status, rest.Balance, rest.Applications.Count));
        Assert.Equal(CreditNoteRules.ApplyNeedsApproved, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 1m), default)));
        Assert.Equal(CreditNoteRules.VoidNeedsOpen, await MsgOf(() => x.Credits.VoidAsync(n.PublicId, "x", default)));
    }

    [Fact]
    public async Task Credits_and_payments_share_the_invoice_balance_and_a_fully_credited_invoice_becomes_paid()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);   // 110
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 100m), default);   // 110
        await x.Credits.ApproveAsync(n.PublicId, default);
        await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 30m), default);
        Assert.Equal(PaymentRules.ExceedsBalance(90m, 80m), await MsgOf(() => x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(90m, "CASH"), default)));   // quedan 80
        var paid = await x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(30m, "CHECK"), default);
        Assert.Equal((30m, 30m, 50m), (paid.Paid, paid.Credited, paid.Balance));
        var done = await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 50m), default);
        Assert.Equal("PAID", (await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Status);
        Assert.Equal(("APPROVED", 30m), (done.Status, done.Balance));   // sobran 30: saldo a favor
        Assert.Equal(CreditNoteRules.ApplyInvoiceNotOpen, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 5m), default)));
    }

    [Fact]
    public async Task Apply_checks_the_invoice_balance_and_the_client()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddClientAsync("C2");
        var inv = await IssuedAsync(x, "T-1", 100m, withTax: false);   // 100
        var otherInv = await IssuedAsync(x, "T-2", 50m, other, withTax: false);
        var n = await x.Credits.CreateAsync(new CreditNoteRequest(ClientPublicId: x.C.PublicId, Reason: "BILLING_ERROR", Lines: new[] { new CreditNoteLineRequest(Description: "Ajuste", Quantity: 1m, UnitAmount: 300m, ChargeType: "FREIGHT") }), default);
        await x.Credits.ApproveAsync(n.PublicId, default);
        Assert.Equal(CreditNoteRules.ApplyOtherClient, await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(otherInv.PublicId, 10m), default)));
        Assert.Equal(CreditNoteRules.ExceedsInvoiceBalance(150m, 100m), await MsgOf(() => x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 150m), default)));
        Assert.Single((await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 100m), default)).Applications);
        Assert.Equal("PAID", (await x.Billing.GetInvoiceAsync(inv.PublicId, default)).Status);
    }

    [Fact]
    public async Task An_invoice_with_approved_credits_cannot_be_voided_and_a_voided_note_frees_it()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 10m), default);
        await x.Credits.ApproveAsync(n.PublicId, default);
        Assert.Equal(CreditNoteRules.InvoiceHasCredits, await MsgOf(() => x.Billing.VoidAsync(inv.PublicId, null, default)));
        await x.Credits.VoidAsync(n.PublicId, "Error", default);
        Assert.Equal("VOID", (await x.Billing.VoidAsync(inv.PublicId, null, default)).Status);
        Assert.Equal(CreditNoteRules.InvoiceNotCreditable, await MsgOf(() => x.Credits.CreateAsync(OnInvoice(inv), default)));   // anulada: ya no se acredita
    }

    [Fact]
    public async Task The_statement_shows_the_credit_note_and_the_unapplied_credit_and_receivables_show_the_credit_balance()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m, withTax: false);   // 100
        var n = await x.Credits.CreateAsync(OnInvoice(inv, unit: 40m), default);
        await x.Credits.ApproveAsync(n.PublicId, default);
        var st = await x.Payments.StatementAsync(x.C.PublicId, null, null, default);
        Assert.Equal(new[] { "INVOICE", "CREDIT_NOTE" }, st.Lines.Select(l => l.Kind).ToArray());
        Assert.Equal((100m, 40m, 60m, 40m), (st.TotalInvoiced, st.TotalPaid, st.ClosingBalance, st.UnappliedCredit));
        var rec = await x.Payments.ReceivablesAsync(null, default);
        Assert.Equal((100m, 40m), (rec.Rows[0].Balance, rec.Rows[0].CreditBalance));

        await x.Credits.ApplyAsync(n.PublicId, new CreditApplyRequest(inv.PublicId, 40m), default);
        var after = await x.Payments.StatementAsync(x.C.PublicId, null, null, default);
        Assert.Equal((60m, 0m, 60m), (after.ClosingBalance, after.UnappliedCredit, after.Aging.Total));
        Assert.Equal(60m, (await x.Payments.ReceivablesAsync(null, default)).Rows[0].Balance);
    }

    [Fact]
    public async Task Lists_filter_by_status_client_and_text_and_notes_are_isolated_by_company()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 100m);
        var a = await x.Credits.CreateAsync(OnInvoice(inv, unit: 10m), default);
        var b = await x.Credits.CreateAsync(OnInvoice(inv, unit: 20m), default);
        await x.Credits.ApproveAsync(b.PublicId, default);
        Assert.Equal(2, (await x.Credits.ListAsync(null, null, null, 0, 50, default)).Total);
        Assert.Equal(b.Number, Assert.Single((await x.Credits.ListAsync("approved", null, null, 0, 50, default)).Items).Number);
        Assert.Equal(2, (await x.Credits.ListAsync(null, x.C.PublicId, "NC-", 0, 50, default)).Total);
        Assert.Equal(0, (await x.Credits.ListAsync(null, Guid.NewGuid(), null, 0, 50, default)).Total);
        Assert.Equal(a.Number, Assert.Single((await x.Credits.ListAsync(null, null, a.Number, 0, 50, default)).Items).Number);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.GetAsync(a.PublicId, default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Credits.ApproveAsync(a.PublicId, default));
            Assert.Equal(0, (await x.Credits.ListAsync(null, null, null, 0, 50, default)).Total);
        }
    }
}
