using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Billing;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 224 (P6 del pedido de vendedores): consignación — pago parcial y total, saldo (dejado − devuelto − pagado), el pago entra a la factura, anular, reporte por cliente,
// panel Ventas, permiso; y lo vendido (SALES_REVENUE) excluye lo consignado hasta su pago y lo cuenta en la fecha del pago.
public sealed class ConsignmentTests
{
    private static Task<SalesVendorWorld> WorldAsync() => SalesVendorWorld.CreateAsync(s =>
    {
        s.AddSingleton<PaymentService>();
        s.AddSingleton<ConsignmentService>();
    });

    private static ConsignmentService Svc(SalesVendorWorld x) => x.F.Get<ConsignmentService>();

    private static DateOnly Today => TenantClock.Default.Today;

    /// <summary>
    /// Una orden de venta despachada (consignment = en consignación) con una línea qty × price e IVU tax, y su factura (status) con esa línea; shipping = cargo de envío de la orden
    /// (línea por SalesOrderId, sin IVU). other = otra orden (no de consignación) en la MISMA factura.
    /// </summary>
    private static async Task<(Invoice Invoice, SalesOrder Order)> InvoiceAsync(SalesVendorWorld x, string number, Client c, Product p, decimal qty, decimal price, decimal tax,
        bool consignment = true, string status = InvoiceStatuses.Issued, decimal shipping = 0m, (Product P, decimal Qty, decimal Price)? other = null, DateOnly? issue = null)
    {
        var so = await OrderAsync(x, c, p, qty, consignment);
        var soLine = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == so.SalesOrderId);
        var productSale = x.Lookup(BillingCodes.ChargeTypeDomain, BillingCodes.ProductSale);
        var day = issue ?? Today.AddDays(-10);
        var invoice = new Invoice
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, InvoiceNumber = number, StatusCodeId = x.F.StatusId(BillingCodes.InvoiceStatusDomain, status),
            IssueDate = day, DueDate = day.AddDays(30), CurrencyLookupId = x.F.LookupId(LookupDomains.Currency, "USD"), IsActive = true,
        };
        invoice.Lines.Add(new InvoiceLine { ProductId = p.ProductId, ChargeTypeLookupId = productSale, Description = p.Sku, Quantity = qty, UnitAmount = price, TaxAmount = tax,
            TaxRatePct = tax > 0m ? 11.5m : 0m, SalesOrderLineId = soLine.SalesOrderLineId });
        if (shipping > 0m)
            invoice.Lines.Add(new InvoiceLine { ChargeTypeLookupId = x.Lookup(BillingCodes.ChargeTypeDomain, SalesShippingCodes.ChargeType), Description = "Cargo de envío",
                Quantity = 1m, UnitAmount = shipping, SalesOrderId = so.SalesOrderId });
        if (other is { } o)
        {
            var so2 = await OrderAsync(x, c, o.P, o.Qty, false);
            var l2 = await x.F.Db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.SalesOrderId == so2.SalesOrderId);
            invoice.Lines.Add(new InvoiceLine { ProductId = o.P.ProductId, ChargeTypeLookupId = productSale, Description = o.P.Sku, Quantity = o.Qty, UnitAmount = o.Price,
                SalesOrderLineId = l2.SalesOrderLineId });
        }
        invoice.Subtotal = invoice.Lines.Sum(l => Math.Round(l.Quantity * l.UnitAmount, 4));
        invoice.TaxAmount = invoice.Lines.Sum(l => l.TaxAmount);
        x.F.Db.Set<Invoice>().Add(invoice);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return (invoice, so);
    }

    /// <summary>Una orden despachada (borrador del servicio real, luego marcada en la base) con su total y su fecha de creación.</summary>
    private static async Task<SalesOrder> OrderAsync(SalesVendorWorld x, Client c, Product p, decimal qty, bool consignment, DateTime? createdAtUtc = null, string status = SalesOrderStatuses.Shipped)
    {
        var draft = await x.DraftAsync(c, p, qty);
        var row = await x.F.Db.Set<SalesOrder>().AsTracking().SingleAsync(o => o.PublicId == draft.PublicId);
        row.IsConsignment = consignment;
        row.StatusCodeId = x.F.StatusId(SalesCodes.StatusDomain, status);
        if (createdAtUtc is DateTime at) row.CreatedAtUtc = at;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.SalesOrderId == row.SalesOrderId);
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    private static Task<List<Payment>> InvoicePaymentsAsync(SalesVendorWorld x, int invoiceId)
        => x.F.Db.Set<Payment>().AsNoTracking().Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.PaymentId).ToListAsync();

    [Fact]
    public void Rules_line_total_consigned_balance_and_messages()
    {
        Assert.Equal(22.30m, ConsignmentRules.LineTotal(2m, 10m, 2.30m));
        Assert.Equal(3.3333m, ConsignmentRules.LineTotal(1m, 3.33333m, 0m));
        Assert.Equal(13.33m, ConsignmentRules.Consigned(new[] { 3.3333m, 10m }));
        Assert.Equal(12.30m, ConsignmentRules.Balance(22.30m, 0m, 10m));
        Assert.Equal(0m, ConsignmentRules.Balance(22.30m, 20m, 10m));   // nunca negativo
        Assert.Equal("El pago (13.00) es mayor que el saldo en consignación de la factura (12.30).", ConsignmentRules.ExceedsBalance(13m, 12.30m));
        Assert.Equal(new[] { "OPEN", "PAID", "ALL" }, ConsignmentRules.Filters);
    }

    [Fact]
    public async Task Partial_and_total_payments_lower_the_balance_enter_the_invoice_and_cannot_pass_it()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (inv, so) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 2.30m);

        var start = await Svc(x).GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((22.30m, 0m, 0m, 22.30m, 22.30m, 22.30m), (start.Invoice.Consigned, start.Invoice.Returned, start.Invoice.Paid, start.Invoice.Balance, start.Invoice.InvoiceTotal, start.Invoice.InvoiceBalance));
        Assert.Equal(new[] { so.Number }, start.Invoice.OrderNumbers);
        Assert.Equal(22.30m, Assert.Single(start.Lines).Amount);
        Assert.Empty(start.Payments);

        // parcial: queda el resto; el dinero entra también a la factura
        var partial = await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(10m, "check", Today.AddDays(-2), " CH-100 "), default);
        Assert.Equal((10m, 12.30m, 12.30m, InvoiceStatuses.Issued, 1), (partial.Invoice.Paid, partial.Invoice.Balance, partial.Invoice.InvoiceBalance, partial.Invoice.Status, partial.Invoice.PaymentCount));
        Assert.Equal(Today.AddDays(-2), partial.Invoice.LastPaidDate);
        var cp = Assert.Single(partial.Payments);
        Assert.Equal((10m, Today.AddDays(-2), "CHECK", "CH-100", true, false), (cp.Amount, cp.PaidDate, cp.Method, cp.Reference, cp.LinkedToInvoicePayment, cp.IsVoided));
        var invoicePayment = Assert.Single(await InvoicePaymentsAsync(x, inv.InvoiceId));
        Assert.Equal((10m, "CH-100"), (invoicePayment.Amount, invoicePayment.Reference));
        var row = await x.F.Db.Set<ConsignmentPayment>().AsNoTracking().SingleAsync();
        Assert.Equal((invoicePayment.PaymentId, 1), (row.PaymentId!.Value, row.RecordedBy!.Value));

        // no pasa del saldo en consignación
        var over = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(13m, "CASH"), default));
        Assert.Equal(ConsignmentRules.ExceedsBalance(13m, 12.30m), over.Message);

        // total: saldo en cero y la factura queda PAGADA
        var full = await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(12.30m, "ACH"), default);
        Assert.Equal((22.30m, 0m, 0m, InvoiceStatuses.Paid, 2), (full.Invoice.Paid, full.Invoice.Balance, full.Invoice.InvoiceBalance, full.Invoice.Status, full.Invoice.PaymentCount));
        Assert.Equal(Today, full.Invoice.LastPaidDate);   // sin fecha = hoy
        Assert.Equal(ConsignmentRules.AlreadySettled, await MsgOf(() => Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(1m, "CASH"), default)));
    }

    [Fact]
    public async Task Validations_have_their_messages()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (inv, _) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 0m);
        Task<ConsignmentInvoiceDto> Pay(ConsignmentPaymentRequest r) => Svc(x).RecordAsync(inv.PublicId, r, default);

        Assert.Equal(PaymentRules.AmountRequired, await MsgOf(() => Pay(new ConsignmentPaymentRequest(null, "CASH"))));
        Assert.Equal(PaymentRules.AmountInvalid, await MsgOf(() => Pay(new ConsignmentPaymentRequest(0m, "CASH"))));
        Assert.Equal(PaymentRules.AmountInvalid, await MsgOf(() => Pay(new ConsignmentPaymentRequest(1.005m, "CASH"))));
        Assert.Equal(PaymentRules.MethodRequired, await MsgOf(() => Pay(new ConsignmentPaymentRequest(5m, " "))));
        Assert.Equal(PaymentRules.MethodInvalid, await MsgOf(() => Pay(new ConsignmentPaymentRequest(5m, "BITCOIN"))));
        Assert.Equal(PaymentRules.DateFuture, await MsgOf(() => Pay(new ConsignmentPaymentRequest(5m, "CASH", Today.AddDays(1)))));
        Assert.Equal(PaymentRules.DateBeforeIssue, await MsgOf(() => Pay(new ConsignmentPaymentRequest(5m, "CASH", Today.AddDays(-30)))));
        Assert.Equal(PaymentRules.ReferenceTooLong, await MsgOf(() => Pay(new ConsignmentPaymentRequest(5m, "CASH", null, new string('x', 81)))));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(x).RecordAsync(Guid.NewGuid(), new ConsignmentPaymentRequest(5m, "CASH"), default));

        // una factura sin nada en consignación, un borrador y una anulada
        var (plain, _) = await InvoiceAsync(x, "FAC-2", c, p, 1m, 10m, 0m, consignment: false);
        Assert.Equal(ConsignmentRules.NotConsignment, await MsgOf(() => Svc(x).RecordAsync(plain.PublicId, new ConsignmentPaymentRequest(5m, "CASH"), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(x).GetInvoiceAsync(plain.PublicId, default));
        var (draft, _) = await InvoiceAsync(x, "FAC-3", c, p, 1m, 10m, 0m, status: InvoiceStatuses.Draft);
        Assert.Equal(ConsignmentRules.NotPayable, await MsgOf(() => Svc(x).RecordAsync(draft.PublicId, new ConsignmentPaymentRequest(5m, "CASH"), default)));
        var (voided, _) = await InvoiceAsync(x, "FAC-4", c, p, 1m, 10m, 0m, status: InvoiceStatuses.Void);
        Assert.Equal(ConsignmentRules.NotPayable, await MsgOf(() => Svc(x).RecordAsync(voided.PublicId, new ConsignmentPaymentRequest(5m, "CASH"), default)));
        Assert.Equal(ConsignmentRules.FilterInvalid, await MsgOf(() => Svc(x).ListAsync("XX", null, null, 0, 50, default)));
        Assert.Empty(await x.F.Db.Set<ConsignmentPayment>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Only_the_consignment_lines_count_shipping_included_and_returns_lower_the_balance()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var q = await x.ProductAsync("GUA", 5m);
        var c = await x.ClientAsync("C1");
        // 2 × 10 + IVU 2.30 + envío 7 en consignación; 4 × 5 de otra orden (no consignación) en la misma factura
        var (inv, _) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 2.30m, shipping: 7m, other: (q, 4m, 5m));
        var dto = await Svc(x).GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((29.30m, 49.30m, 29.30m), (dto.Invoice.Consigned, dto.Invoice.InvoiceTotal, dto.Invoice.Balance));
        Assert.Equal(2, dto.Lines.Count);

        // devolución de lo no vendido: nota de crédito APROBADA de 1 unidad de la línea en consignación (con su IVU)
        var line = await x.F.Db.Set<InvoiceLine>().AsNoTracking().SingleAsync(l => l.InvoiceId == inv.InvoiceId && l.ProductId == p.ProductId);
        var note = new CreditNote
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, CreditNumber = "NC-1", ClientId = c.ClientId, InvoiceId = inv.InvoiceId,
            ReasonLookupId = x.F.LookupId(LookupDomains.Currency, "USD"), CurrencyLookupId = x.F.LookupId(LookupDomains.Currency, "USD"),
            StatusCodeId = x.F.StatusId(CreditCodes.StatusDomain, CreditNoteStatuses.Draft), Subtotal = 10m, TaxAmount = 1.15m,
        };
        note.Lines.Add(new CreditNoteLine { InvoiceLineId = line.InvoiceLineId, Description = "MED", Quantity = 1m, UnitAmount = 10m, TaxAmount = 1.15m });
        x.F.Db.CreditNotes.Add(note);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(0m, (await Svc(x).GetInvoiceAsync(inv.PublicId, default)).Invoice.Returned);   // el borrador no cuenta
        var tracked = await x.F.Db.CreditNotes.AsTracking().SingleAsync();
        tracked.StatusCodeId = x.F.StatusId(CreditCodes.StatusDomain, CreditNoteStatuses.Approved);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var after = await Svc(x).GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((29.30m, 11.15m, 18.15m), (after.Invoice.Consigned, after.Invoice.Returned, after.Invoice.Balance));
        Assert.Equal(11.15m, after.Lines.Single(l => l.InvoiceLineId == line.InvoiceLineId).Returned);
        Assert.Equal(ConsignmentRules.ExceedsBalance(18.16m, 18.15m), await MsgOf(() => Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(18.16m, "CASH"), default)));
        var paid = await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(18.15m, "CASH"), default);
        Assert.Equal(0m, paid.Invoice.Balance);
        Assert.Equal(InvoiceStatuses.Issued, paid.Invoice.Status);   // la factura sigue con lo que no es de consignación por cobrar
        Assert.Equal(31.15m, paid.Invoice.InvoiceBalance);
    }

    [Fact]
    public async Task On_an_invoice_already_paid_by_payments_it_only_reports_what_was_sold()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (inv, _) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 0m);
        await x.F.Get<PaymentService>().RecordAsync(inv.PublicId, new PaymentRequest(20m, "CASH"), default);   // la cobró por Pagos
        var before = await Svc(x).GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((InvoiceStatuses.Paid, 20m, 0m), (before.Invoice.Status, before.Invoice.Balance, before.Invoice.InvoiceBalance));

        var dto = await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(8m, "CASH", Today), default);
        Assert.Equal((8m, 12m), (dto.Invoice.Paid, dto.Invoice.Balance));
        Assert.False(Assert.Single(dto.Payments).LinkedToInvoicePayment);
        Assert.Single(await InvoicePaymentsAsync(x, inv.InvoiceId));   // no se cobró dos veces
    }

    [Fact]
    public async Task Voiding_a_payment_voids_its_invoice_payment_and_it_stops_counting()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var (inv, _) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 0m);
        var cp = Assert.Single((await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(5m, "CASH"), default)).Payments);

        Assert.Equal(PaymentRules.VoidReasonRequired, await MsgOf(() => Svc(x).VoidAsync(cp.PublicId, " ", default)));
        Assert.Equal(PaymentRules.VoidReasonTooLong, await MsgOf(() => Svc(x).VoidAsync(cp.PublicId, new string('x', 251), default)));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(x).VoidAsync(Guid.NewGuid(), "error", default));

        var voided = await Svc(x).VoidAsync(cp.PublicId, " se registró dos veces ", default);
        Assert.Equal((0m, 20m, 20m, 0), (voided.Invoice.Paid, voided.Invoice.Balance, voided.Invoice.InvoiceBalance, voided.Invoice.PaymentCount));
        var shown = Assert.Single(voided.Payments);
        Assert.Equal((true, "se registró dos veces"), (shown.IsVoided, shown.VoidReason));
        Assert.NotNull(Assert.Single(await InvoicePaymentsAsync(x, inv.InvoiceId)).VoidedAtUtc);
        Assert.Equal(PaymentRules.AlreadyVoided, await MsgOf(() => Svc(x).VoidAsync(cp.PublicId, "otra vez", default)));

        // anular el pago de la factura por Pagos también lo saca de la consignación
        var cp2 = (await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(6m, "CASH"), default)).Payments.First(r => !r.IsVoided);
        var paymentId = (await InvoicePaymentsAsync(x, inv.InvoiceId)).Last().PaymentId;
        await x.F.Get<PaymentService>().VoidAsync(paymentId, "error", default);
        var dto = await Svc(x).GetInvoiceAsync(inv.PublicId, default);
        Assert.Equal((0m, 20m), (dto.Invoice.Paid, dto.Invoice.Balance));
        Assert.True(dto.Payments.Single(r => r.PublicId == cp2.PublicId).IsVoided);
    }

    [Fact]
    public async Task Sales_exclude_consignment_until_paid_and_count_it_on_the_payment_date()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var now = DateTime.UtcNow;
        var normal = await OrderAsync(x, c, p, 3m, consignment: false, createdAtUtc: now.AddDays(-1));   // venta normal: $30 ayer
        await OrderAsync(x, c, p, 9m, consignment: false, createdAtUtc: now.AddDays(-1), status: SalesOrderStatuses.Draft);   // borrador: no es venta
        var (inv, so) = await InvoiceAsync(x, "FAC-1", c, p, 2m, 10m, 0m);   // $20 en consignación
        var src = new SalesRevenueDataSource(x.F.Db, x.F.Tenant, TenantClock.Default);
        async Task<List<DataRow>> Load(DateOnly? from = null, DateOnly? toExclusive = null)
            => await src.LoadAsync(new DataQuery
            {
                FromDay = from, ToDayExclusive = toExclusive,
                FromUtc = from is DateOnly f ? TenantClock.Default.StartOfDayUtc(f) : null, ToUtc = toExclusive is DateOnly t ? TenantClock.Default.StartOfDayUtc(t) : null,
            }, default);

        // sin pagar: lo consignado no sale (solo la venta normal)
        var rows = await Load();
        var only = Assert.Single(rows);
        Assert.Equal((SalesRevenueDataSource.KindOrder, normal.Number, 30m, false), ((string)only["KindCode"]!, (string)only["OrderNumber"]!, (decimal)only["Amount"]!, (bool)only["IsConsignment"]!));
        Assert.Equal(so.SalesOrderId, (await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.IsConsignment)).SalesOrderId);

        // dos pagos en fechas distintas: cada uno cuenta en su fecha
        await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(5m, "CASH", Today.AddDays(-5)), default);
        await Svc(x).RecordAsync(inv.PublicId, new ConsignmentPaymentRequest(7m, "CHECK", Today), default);
        rows = await Load();
        Assert.Equal(new[] { 30m, 5m, 7m }.OrderBy(v => v), rows.Select(r => (decimal)r["Amount"]!).OrderBy(v => v));
        var pays = rows.Where(r => (string)r["KindCode"]! == SalesRevenueDataSource.KindConsignmentPayment).ToList();
        Assert.All(pays, r => Assert.Equal(("FAC-1", so.Number, true), ((string)r["InvoiceNumber"]!, (string)r["OrderNumber"]!, (bool)r["IsConsignment"]!)));
        var seven = pays.Single(r => (decimal)r["Amount"]! == 7m);
        Assert.Equal(Today, Teikem.Domain.Common.LocalDay.DayOf((DateTime)seven["SoldAtUtc"]!, TenantClock.Default.Zone));   // cae en el día del pago

        // rango de los últimos 2 días: el pago de hace 5 días queda fuera; la venta normal y el de hoy entran
        var recent = await Load(Today.AddDays(-1), Today.AddDays(1));
        Assert.Equal(new[] { 7m, 30m }, recent.Select(r => (decimal)r["Amount"]!).OrderBy(v => v));
        var old = await Load(Today.AddDays(-5), Today.AddDays(-4));
        Assert.Equal(5m, (decimal)Assert.Single(old)["Amount"]!);

        // un pago anulado deja de contar
        var five = (await Svc(x).GetInvoiceAsync(inv.PublicId, default)).Payments.Single(r => r.Amount == 5m);
        await Svc(x).VoidAsync(five.PublicId, "error", default);
        Assert.Equal(new[] { 7m, 30m }, (await Load()).Select(r => (decimal)r["Amount"]!).OrderBy(v => v));

        // la fuente de las órdenes ahora dice cuál es de consignación
        var orderRows = await new SalesOrderDataSource(x.F.Db, x.F.Lookups, x.F.Tenant).LoadAsync(new DataQuery(), default);
        Assert.True((bool)orderRows.Single(r => (int)r["Id"]! == so.SalesOrderId)["IsConsignment"]!);
        Assert.False((bool)orderRows.Single(r => (int)r["Id"]! == normal.SalesOrderId)["IsConsignment"]!);
    }

    [Fact]
    public async Task List_filters_report_by_client_and_the_sales_panel_count()
    {
        await using var x = await WorldAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c1 = await x.ClientAsync("C1");
        var c2 = await x.ClientAsync("C2");
        var (a, soA) = await InvoiceAsync(x, "FAC-A", c1, p, 2m, 10m, 0m, issue: Today.AddDays(-20));   // $20
        var (b, _) = await InvoiceAsync(x, "FAC-B", c1, p, 1m, 10m, 0m);   // $10
        var (d, _) = await InvoiceAsync(x, "FAC-D", c2, p, 3m, 10m, 0m);   // $30
        await InvoiceAsync(x, "FAC-N", c2, p, 1m, 10m, 0m, consignment: false);   // no es de consignación
        await Svc(x).RecordAsync(b.PublicId, new ConsignmentPaymentRequest(10m, "CASH"), default);   // pagada
        await Svc(x).RecordAsync(d.PublicId, new ConsignmentPaymentRequest(12m, "CASH"), default);   // parcial

        var open = await Svc(x).ListAsync(null, null, null, 0, 50, default);   // OPEN por omisión
        Assert.Equal(new[] { "FAC-A", "FAC-D" }, open.Items.Select(i => i.Number).OrderBy(n => n));
        Assert.Equal(2, open.Total);
        Assert.Equal(new[] { "FAC-B" }, (await Svc(x).ListAsync("paid", null, null, 0, 50, default)).Items.Select(i => i.Number));
        Assert.Equal(3, (await Svc(x).ListAsync("ALL", null, null, 0, 50, default)).Total);
        Assert.Equal(new[] { "FAC-D" }, (await Svc(x).ListAsync("ALL", c2.PublicId, null, 0, 50, default)).Items.Select(i => i.Number));
        Assert.Equal(new[] { "FAC-A" }, (await Svc(x).ListAsync("ALL", null, soA.Number, 0, 50, default)).Items.Select(i => i.Number));   // por número de orden
        var page = await Svc(x).ListAsync("ALL", null, null, 1, 1, default);
        Assert.Equal((3, 1), (page.Total, page.Items.Count));
        Assert.Empty((await Svc(x).ListAsync("ALL", Guid.NewGuid(), null, 0, 50, default)).Items);

        var report = await Svc(x).ByClientAsync(default);
        Assert.Equal(new[] { "C1", "C2" }, report.Items.Select(r => r.ClientCode));   // más saldo primero (20 y 18)
        var r1 = report.Items.Single(r => r.ClientCode == "C1");
        Assert.Equal((2, 1, 30m, 10m, 20m, (DateOnly?)Today.AddDays(-20)), (r1.InvoiceCount, r1.OpenInvoiceCount, r1.Consigned, r1.Paid, r1.Balance, r1.OldestOpenIssueDate));
        Assert.Equal((60m, 22m, 38m, 2), (report.Consigned, report.Paid, report.Balance, report.OpenInvoiceCount));

        // panel Ventas del Pulso: facturas con saldo en consignación, para quien ve Facturación
        Assert.Equal(2, await Svc(x).CountUnpaidAsync(default));
        var pulse = new SalesPulseService(x.F.Db, x.F.Get<ITenantClock>(), x.F.Get<PermissionService>(), null, Svc(x), x.F.Get<ModuleService>());
        var dto = await pulse.GetAsync(default);
        Assert.Equal(2, dto.ConsignmentUnpaid);
        Assert.Equal("/money/billing?tab=consignment", dto.Routes["consignmentUnpaid"]);
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.PulseSales, PermissionCatalog.BillingConsignmentPay);
        Assert.Equal(2, (await pulse.GetAsync(default)).ConsignmentUnpaid);
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.PulseSales);   // sin Facturación: no la ve
        Assert.Null((await pulse.GetAsync(default)).ConsignmentUnpaid);
        x.F.SetModules(ModuleKeys.SalesOrders);   // sin el módulo de facturación tampoco
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.BillingView);
        Assert.Null((await pulse.GetAsync(default)).ConsignmentUnpaid);
    }

    [Fact]
    public void The_permission_its_roles_its_screen_and_the_sales_source_permission()
    {
        var def = Assert.Single(PermissionCatalog.All, d => d.Code == PermissionCatalog.BillingConsignmentPay);
        Assert.Equal(("billing.consignment.pay", "BILLING"), (def.Code, def.Category));
        Assert.Contains(PermissionCatalog.BillingConsignmentPay, PermissionCatalog.RoleTemplates["SalesOffice"]);
        Assert.Contains(PermissionCatalog.BillingConsignmentPay, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        Assert.DoesNotContain(PermissionCatalog.BillingConsignmentPay, PermissionCatalog.RoleTemplates["Sales"]);
        Assert.Contains("billing.consignment.pay", PermissionDisplayOrder.Codes);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PermissionCatalog.BillingConsignmentPay };
        PermissionCatalog.ExpandImplied(set);
        Assert.Contains(PermissionCatalog.BillingView, set);   // entra a Facturación, donde vive la pestaña
        Assert.Equal(PermissionCatalog.SalesView, PermissionCatalog.DataSourceReadPermission(FinanceAnalyticsRules.SalesRevenueSource));
    }

    [Fact]
    public void The_update_script_creates_the_table_adds_the_permission_once_and_moves_the_sales_indicators()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Diseño"))) dir = dir.Parent;
        var sql = File.ReadAllText(Path.Combine(dir!.FullName, "Diseño", "logistica-db-update.sql"));
        var start = sql.IndexOf("2026-11-07 — Lote 224 (P6 del pedido de vendedores)", StringComparison.Ordinal);
        Assert.True(start > 0);
        var section = UpdateSqlSection.From(sql, start);
        Assert.Contains("IF OBJECT_ID(N'dbo.ConsignmentPayment', N'U') IS NULL", section);
        Assert.Contains("WHERE Code = 'billing.consignment.pay'", section);
        Assert.Contains("L224_CONSIGNMENT", section);
        Assert.Contains("r.Name IN ('TenantAdmin', 'SalesOffice')", section);
        Assert.Contains("SET DataSourceKey = N'SALES_REVENUE', FieldKey = N'Amount'", section);
        Assert.Contains("GroupByField = N'SoldAtUtc'", section);
        Assert.Contains(FinanceAnalyticsRules.SalesConfirmedFilter, section);   // solo si siguen como se sembraron
        Assert.DoesNotContain("DELETE", section);
    }
}
