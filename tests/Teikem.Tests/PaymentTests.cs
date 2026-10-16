using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Jobs;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 75 (P5): pagos de las facturas, estado de cuenta, cuentas por cobrar y vencimiento.
public sealed class PaymentTests
{
    [Fact]
    public void Rules_amount_balance_overdue_and_aging_buckets()
    {
        Assert.True(PaymentRules.IsValidAmount(10.25m));
        Assert.False(PaymentRules.IsValidAmount(0m));
        Assert.False(PaymentRules.IsValidAmount(-1m));
        Assert.False(PaymentRules.IsValidAmount(10.255m));
        Assert.Equal(15m, PaymentRules.Balance(40m, 25m));
        Assert.Equal(0m, PaymentRules.Balance(40m, 50m));
        Assert.True(PaymentRules.IsSettled(40m, 40m));
        Assert.False(PaymentRules.IsSettled(40m, 39.99m));
        var today = new DateOnly(2026, 10, 16);
        Assert.True(PaymentRules.IsOverdue(new DateOnly(2026, 10, 15), 5m, today));
        Assert.False(PaymentRules.IsOverdue(new DateOnly(2026, 10, 16), 5m, today));   // vence hoy: todavía no
        Assert.False(PaymentRules.IsOverdue(new DateOnly(2026, 10, 1), 0m, today));   // sin saldo
        Assert.Equal(0, PaymentRules.AgingBucket(null, today));   // sin vencimiento cuenta como por vencer
        Assert.Equal(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4 }, new[]
        {
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 15), new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 16),
            new DateOnly(2026, 7, 18), new DateOnly(2026, 7, 17),
        }.Select(d => PaymentRules.AgingBucket(d, today)));
        Assert.Equal("El pago (12.00) es mayor que el saldo de la factura (5.00).", PaymentRules.ExceedsBalance(12m, 5m));
    }

    private sealed class World(WmsFixture f, Client c, BillingService billing, PaymentService payments)
    {
        public WmsFixture F { get; } = f;
        public Client C { get; } = c;
        public BillingService Billing { get; } = billing;
        public PaymentService Payments { get; } = payments;
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
        });
        f.SetModules(ModuleKeys.Billing);
        var c = await f.AddClientAsync("C1");
        return new World(f, c, f.Get<BillingService>(), f.Get<PaymentService>());
    }

    private static BillingRunRequest Period(Client c)
        => new(c.PublicId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    /// <summary>Una factura emitida de `amount` (una orden de transporte entregada y cotizada, corrida revisada y aprobada).</summary>
    private static async Task<InvoiceListItemDto> IssuedAsync(World x, string number, decimal amount, Client? client = null)
    {
        var c = client ?? x.C;
        var o = await x.F.AddOrderAsync(c.ClientId, number, OrderStatuses.Delivered);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.QuotedAmount, t.QuotedAtUtc) = (amount, DateTime.UtcNow.AddDays(-1));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var run = await x.Billing.GenerateAsync(Period(c), default);
        await x.Billing.ReviewAsync(run.Id, default);
        return (await x.Billing.ApproveAsync(run.Id, default)).Invoices[0];
    }

    private static async Task SetDatesAsync(World x, Guid invoice, DateOnly issue, DateOnly due)
    {
        var inv = await x.F.Db.Set<Invoice>().AsTracking().SingleAsync(i => i.PublicId == invoice);
        (inv.IssueDate, inv.DueDate) = (issue, due);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Partial_and_full_payments_settle_the_invoice_and_cannot_pass_the_balance()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 40m);

        var partial = await x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(15m, "check", null, " CH-100 "), default);
        Assert.Equal(("ISSUED", 15m, 25m), (partial.Status, partial.Paid, partial.Balance));
        var p = Assert.Single(partial.Payments!);
        Assert.Equal((15m, "CHECK", "CH-100", null), (p.Amount, p.Method, p.Reference, p.VoidedAtUtc));

        Assert.Equal(PaymentRules.ExceedsBalance(30m, 25m), await MsgOf(() => x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(30m, "CASH"), default)));
        var full = await x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(25m, "ACH"), default);
        Assert.Equal(("PAID", 40m, 0m), (full.Status, full.Paid, full.Balance));
        Assert.Equal(PaymentRules.NotPayable, await MsgOf(() => x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(1m, "CASH"), default)));
    }

    [Fact]
    public async Task Payment_validations_have_their_messages()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 40m);
        Task<InvoiceDto> Pay(PaymentRequest r) => x.Payments.RecordAsync(inv.PublicId, r, default);

        Assert.Equal(PaymentRules.AmountRequired, await MsgOf(() => Pay(new PaymentRequest(null, "CASH"))));
        Assert.Equal(PaymentRules.AmountInvalid, await MsgOf(() => Pay(new PaymentRequest(0m, "CASH"))));
        Assert.Equal(PaymentRules.AmountInvalid, await MsgOf(() => Pay(new PaymentRequest(1.005m, "CASH"))));
        Assert.Equal(PaymentRules.MethodRequired, await MsgOf(() => Pay(new PaymentRequest(5m, " "))));
        Assert.Equal(PaymentRules.MethodInvalid, await MsgOf(() => Pay(new PaymentRequest(5m, "BITCOIN"))));
        Assert.Equal(PaymentRules.DateFuture, await MsgOf(() => Pay(new PaymentRequest(5m, "CASH", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3))))));
        Assert.Equal(PaymentRules.DateBeforeIssue, await MsgOf(() => Pay(new PaymentRequest(5m, "CASH", new DateOnly(2020, 1, 1)))));
        Assert.Equal(PaymentRules.ReferenceTooLong, await MsgOf(() => Pay(new PaymentRequest(5m, "CASH", null, new string('x', 81)))));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Payments.RecordAsync(Guid.NewGuid(), new PaymentRequest(5m, "CASH"), default));
    }

    [Fact]
    public async Task A_draft_invoice_is_not_payable()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await x.F.AddOrderAsync(x.C.ClientId, "T-1", OrderStatuses.Delivered);
        var t = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        (t.QuotedAmount, t.QuotedAtUtc) = (10m, DateTime.UtcNow.AddDays(-1));
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var run = await x.Billing.GenerateAsync(Period(x.C), default);
        Assert.Equal(PaymentRules.NotPayable, await MsgOf(() => x.Payments.RecordAsync(run.Invoices[0].PublicId, new PaymentRequest(5m, "CASH"), default)));
    }

    [Fact]
    public async Task Voiding_a_payment_restores_the_balance_but_not_on_a_paid_invoice_and_the_invoice_can_then_be_voided()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 40m);
        var a = await x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(10m, "CASH"), default);
        var id = a.Payments![0].Id;

        Assert.Equal(PaymentRules.VoidReasonRequired, await MsgOf(() => x.Payments.VoidAsync(id, " ", default)));
        Assert.Equal(PaymentRules.VoidReasonTooLong, await MsgOf(() => x.Payments.VoidAsync(id, new string('x', 251), default)));
        var voided = await x.Payments.VoidAsync(id, "Se anotó en la factura equivocada", default);
        Assert.Equal((0m, 40m), (voided.Paid, voided.Balance));
        var pay = Assert.Single(voided.Payments!);
        Assert.NotNull(pay.VoidedAtUtc);
        Assert.Equal("Se anotó en la factura equivocada", pay.VoidReason);
        Assert.Equal(PaymentRules.AlreadyVoided, await MsgOf(() => x.Payments.VoidAsync(id, "otra vez", default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Payments.VoidAsync(9999, "x", default));

        // con el pago anulado la factura ya no tiene pagos: se puede anular
        var gone = await x.Billing.VoidAsync(inv.PublicId, null, default);
        Assert.Equal("VOID", gone.Status);

        // un pago de una factura pagada por completo no se anula (el estatus Pagada es terminal)
        var inv2 = await IssuedAsync(x, "T-2", 20m);
        var paid = await x.Payments.RecordAsync(inv2.PublicId, new PaymentRequest(inv2.Total, "CARD"), default);   // (la T-1 anulada vuelve a entrar en esta factura)
        Assert.Equal(PaymentRules.VoidOnPaidInvoice, await MsgOf(() => x.Payments.VoidAsync(paid.Payments![0].Id, "error", default)));
    }

    [Fact]
    public async Task The_statement_has_running_balance_opening_balance_and_aging()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var a = await IssuedAsync(x, "T-1", 100m);
        await SetDatesAsync(x, a.PublicId, today.AddDays(-60), today.AddDays(-30));   // vencida hace 30 días
        await x.Payments.RecordAsync(a.PublicId, new PaymentRequest(40m, "CHECK", today.AddDays(-5), "CH-7"), default);
        await IssuedAsync(x, "T-2", 50m);   // emitida hoy, vence en 30 días: por vencer

        var st = await x.Payments.StatementAsync(x.C.PublicId, today.AddDays(-90), today, default);
        Assert.Equal((0m, 150m, 40m, 110m), (st.OpeningBalance, st.TotalInvoiced, st.TotalPaid, st.ClosingBalance));
        Assert.Equal(new[] { "INVOICE", "PAYMENT", "INVOICE" }, st.Lines.Select(l => l.Kind).ToArray());
        Assert.Equal(new[] { 100m, 60m, 110m }, st.Lines.Select(l => l.Balance).ToArray());
        Assert.Equal((60m, 50m, 110m), (st.Aging.Days1To30, st.Aging.Current, st.Aging.Total));

        // desde una fecha posterior, lo anterior es el saldo inicial
        var later = await x.Payments.StatementAsync(x.C.PublicId, today.AddDays(-2), today, default);
        Assert.Equal((60m, 50m), (later.OpeningBalance, later.TotalInvoiced));
        Assert.Equal(110m, later.ClosingBalance);
        Assert.Equal(PaymentRules.StatementPeriodInvalid, await MsgOf(() => x.Payments.StatementAsync(x.C.PublicId, today, today.AddDays(-1), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Payments.StatementAsync(Guid.NewGuid(), null, null, default));
    }

    [Fact]
    public async Task Receivables_group_open_balances_by_client_and_leave_out_what_is_paid()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var c2 = await x.F.AddClientAsync("C2");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var a = await IssuedAsync(x, "T-1", 100m);
        await SetDatesAsync(x, a.PublicId, today.AddDays(-100), today.AddDays(-95));   // más de 90
        await IssuedAsync(x, "T-2", 30m, c2);
        var paid = await IssuedAsync(x, "T-3", 10m, c2);
        await x.Payments.RecordAsync(paid.PublicId, new PaymentRequest(10m, "CASH"), default);

        var r = await x.Payments.ReceivablesAsync(null, default);
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(new[] { 100m, 30m }, r.Rows.Select(row => row.Balance).ToArray());
        Assert.Equal(100m, r.Rows[0].Aging.Over90);
        Assert.Equal((1, 30m), (r.Rows[1].OpenInvoices, r.Rows[1].Aging.Current));
        Assert.Equal((130m, 100m, 30m), (r.Total.Total, r.Total.Over90, r.Total.Current));
    }

    [Fact]
    public async Task The_scan_marks_overdue_invoices_and_an_overdue_invoice_can_still_be_paid()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var late = await IssuedAsync(x, "T-1", 40m);
        await SetDatesAsync(x, late.PublicId, today.AddDays(-40), today.AddDays(-10));
        var fine = await IssuedAsync(x, "T-2", 40m);

        Assert.Equal(1, await x.Payments.MarkOverdueAsync(default));
        Assert.Equal(0, await x.Payments.MarkOverdueAsync(default));   // ya está
        Assert.Equal("OVERDUE", (await x.Billing.GetInvoiceAsync(late.PublicId, default)).Status);
        Assert.Equal("ISSUED", (await x.Billing.GetInvoiceAsync(fine.PublicId, default)).Status);

        Assert.Equal("PAID", (await x.Payments.RecordAsync(late.PublicId, new PaymentRequest(40m, "ACH"), default)).Status);
    }

    [Fact]
    public async Task Payments_and_statements_are_isolated_by_company()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var inv = await IssuedAsync(x, "T-1", 40m);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => x.Payments.RecordAsync(inv.PublicId, new PaymentRequest(5m, "CASH"), default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Payments.StatementAsync(x.C.PublicId, null, null, default));
            Assert.Empty((await x.Payments.ReceivablesAsync(null, default)).Rows);
        }
    }

    [Fact]
    public void The_overdue_job_is_hourly_and_enabled()
    {
        var job = new BillingOverdueScanJob();
        Assert.Equal((JobKeys.BillingOverdueScan, JobScheduleKinds.Interval, 3600, true), (job.Key, job.DefaultKind, job.DefaultIntervalSeconds, job.DefaultEnabled));
    }
}
