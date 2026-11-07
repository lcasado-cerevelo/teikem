using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): la forma de pago de la orden — prellenada con la del cliente, la marca «cambió», el término solo con crédito,
// la consignación solo con esa forma; y el cargo de envío en la orden con la regla de la compañía o del cliente.
public sealed class SalesPaymentTests
{
    // ---------------------------------------------------------------- reglas puras

    [Theory]
    [InlineData("CHEQUE", SalesPaymentMethods.CodCheck, null)]
    [InlineData("CASH", SalesPaymentMethods.CodCash, null)]
    [InlineData("ACH", SalesPaymentMethods.CodAch, null)]
    [InlineData("NET30", SalesPaymentMethods.Credit, "NET30")]
    [InlineData("net45", SalesPaymentMethods.Credit, "NET45")]
    [InlineData("PK_BY_REP", SalesPaymentMethods.Credit, "PK_BY_REP")]
    [InlineData("CONSIGNMENT", SalesPaymentMethods.Consignment, null)]
    [InlineData("COD", null, null)]
    [InlineData(null, null, null)]
    public void The_client_payment_term_prefills_the_order_method(string? clientTerm, string? method, string? term)
        => Assert.Equal((method, term), SalesPaymentRules.Prefill(clientTerm));

    [Fact]
    public void Changed_means_different_from_the_client_and_cod_clients_may_pick_how()
    {
        Assert.False(SalesPaymentRules.IsChange("NET30", SalesPaymentMethods.Credit, "NET30"));
        Assert.True(SalesPaymentRules.IsChange("NET30", SalesPaymentMethods.Credit, "NET15"));
        Assert.True(SalesPaymentRules.IsChange("NET30", SalesPaymentMethods.CodCash, null));
        Assert.False(SalesPaymentRules.IsChange("COD", SalesPaymentMethods.CodAch, null));   // contra entrega: escoger cómo no es un cambio
        Assert.True(SalesPaymentRules.IsChange("COD", SalesPaymentMethods.Credit, "NET30"));
        Assert.True(SalesPaymentRules.IsChange(null, SalesPaymentMethods.CodCheck, null));   // el cliente no decía nada: la oficina lo ve
        Assert.False(SalesPaymentRules.IsChange("NET30", null, null));   // nada escogido
    }

    [Fact]
    public void Term_only_with_credit_and_consignment_only_with_its_method()
    {
        Assert.Null(SalesPaymentRules.ValidateMethodAndTerm(SalesPaymentMethods.Credit, "NET20"));
        Assert.Equal(SalesPaymentRules.CreditNeedsTerm, SalesPaymentRules.ValidateMethodAndTerm(SalesPaymentMethods.Credit, null));
        Assert.Equal(SalesPaymentRules.TermOnlyWithCredit, SalesPaymentRules.ValidateMethodAndTerm(SalesPaymentMethods.CodCash, "NET30"));
        Assert.Equal(SalesPaymentRules.TermNotCredit, SalesPaymentRules.ValidateMethodAndTerm(SalesPaymentMethods.Credit, "CASH"));
        Assert.Equal(SalesPaymentRules.MethodUnknown, SalesPaymentRules.ValidateMethodAndTerm("TARJETA", null));
        Assert.Null(SalesPaymentRules.ValidateConsignment(true, SalesPaymentMethods.Consignment));
        Assert.Equal("La venta en consignación exige la forma de pago Consignación.", SalesPaymentRules.ValidateConsignment(true, SalesPaymentMethods.Credit));
        Assert.Null(SalesPaymentRules.ValidateConsignment(false, SalesPaymentMethods.Credit));
    }

    // ---------------------------------------------------------------- en la orden

    [Fact]
    public async Task A_new_order_is_prefilled_with_the_client_payment_and_changing_it_marks_the_order()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1", r => r.PaymentTermLookupId = x.Lookup(LookupDomains.PaymentTerm, "NET30"));

        var d = await x.DraftAsync(c, p, 2m);
        Assert.Equal((SalesPaymentMethods.Credit, "NET30", false), (d.Terms!.PaymentMethod, d.Terms.PaymentTerm, d.Terms.PaymentChanged));

        var changed = await x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(PaymentMethod: SalesPaymentMethods.CodCheck), default);
        Assert.Equal((SalesPaymentMethods.CodCheck, (string?)null, true), (changed.Terms!.PaymentMethod, changed.Terms.PaymentTerm, changed.Terms.PaymentChanged));

        var back = await x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(PaymentMethod: SalesPaymentMethods.Credit, PaymentTerm: "NET30"), default);
        Assert.False(back.Terms!.PaymentChanged);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(PaymentMethod: SalesPaymentMethods.Credit, PaymentTerm: ""), default));
        Assert.Equal(SalesPaymentRules.CreditNeedsTerm, ex.Message);
    }

    [Fact]
    public async Task Consignment_is_only_allowed_with_the_consignment_method_and_follows_it()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1", r => r.PaymentTermLookupId = x.Lookup(LookupDomains.PaymentTerm, "CONSIGNMENT"));

        var d = await x.DraftAsync(c, p, 1m);
        Assert.Equal((SalesPaymentMethods.Consignment, true), (d.Terms!.PaymentMethod, d.Terms.IsConsignment));   // el cliente de consignación la trae marcada

        var off = await x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(PaymentMethod: SalesPaymentMethods.CodCash), default);
        Assert.False(off.Terms!.IsConsignment);   // cambiar de forma la quita

        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(IsConsignment: true), default));
        Assert.Equal(SalesPaymentRules.ConsignmentNeedsMethod, ex.Message);
        Assert.True(ex.Errors!.ContainsKey("isConsignment"));

        var other = await x.ClientAsync("C2");
        await Assert.ThrowsAsync<ValidationException>(() => x.DraftAsync(other, p, 1m, new SalesOrderRequest(PaymentMethod: SalesPaymentMethods.Credit, PaymentTerm: "NET30", IsConsignment: true)));
    }

    [Fact]
    public async Task The_order_carries_urgent_delivery_mode_and_who_authorizes()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var d = await x.DraftAsync(c, p, 1m, new SalesOrderRequest(IsUrgent: true, DeliveryMode: "pickup", AuthorizedByName: "  Luis Pérez  ", Notes: "Antes del mediodía"));
        Assert.True(d.Terms!.IsUrgent);
        Assert.Equal((SalesDeliveryModes.Pickup, true, "Luis Pérez"), (d.Terms.DeliveryMode, d.Terms.DeliveryModeSet, d.Terms.AuthorizedByName));
        var list = await x.Sales.ListAsync(null, null, null, false, null, null, 0, 50, default);
        Assert.True(list.Items.Single().IsUrgent);
        await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(DeliveryMode: "CAMION"), default));
    }

    [Fact]
    public async Task The_shipping_charge_follows_the_company_rule_and_the_client_wins()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        await x.SetCompanyShippingAsync(12m, 120m);
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var cons = await x.ConsigneeAsync(c);

        var small = await x.DraftAsync(c, p, 5m, consignee: cons.PublicId);   // $50 de entrega: lleva envío
        Assert.Equal((12m, ShippingReasons.CompanyRule), (small.Terms!.ShippingCharge, small.Terms.ShippingReason));
        Assert.Equal(62m, small.Total);

        var big = await x.DraftAsync(c, p, 12m, consignee: cons.PublicId);   // $120: gratis por monto
        Assert.Equal((0m, ShippingReasons.Amount), (big.Terms!.ShippingCharge, big.Terms.ShippingReason));

        var pickup = await x.DraftAsync(c, p, 1m);   // sin consignatario: recogido
        Assert.Equal((0m, ShippingReasons.Pickup), (pickup.Terms!.ShippingCharge, pickup.Terms.ShippingReason));

        // el cliente con regla propia gana; «no se le cobra» también; volver a la de la compañía la toma de HOY
        var row = await x.F.Db.Clients.AsTracking().SingleAsync(r => r.ClientId == c.ClientId);
        (row.ShippingMode, row.ShippingCharge, row.ShippingFreeFrom) = (ClientShippingModes.Own, 5m, 40m);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var own = await x.Sales.UpdateAsync(small.PublicId, new SalesOrderRequest(Notes: "otra vez"), default);
        Assert.Equal((0m, ShippingReasons.Amount), (own.Terms!.ShippingCharge, own.Terms.ShippingReason));   // $50 ≥ su mínimo de $40
        await x.SetCompanyShippingAsync(15m, null);
        var p2 = await x.ProductAsync("VIT", 1m);
        var c2 = await x.ClientAsync("C2");
        var cons2 = await x.ConsigneeAsync(c2);
        var noMin = await x.DraftAsync(c2, p2, 500m, consignee: cons2.PublicId);
        Assert.Equal((15m, ShippingReasons.CompanyRule), (noMin.Terms!.ShippingCharge, noMin.Terms.ShippingReason));   // sin mínimo: siempre se cobra
    }

    [Fact]
    public async Task The_shipping_settings_are_validated_and_zero_clears_them()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var s = await x.Sales.UpdateShippingSettingsAsync(new SalesShippingSettingsRequest(12m, 120m, true), default);
        Assert.Equal(new SalesShippingSettingsDto(12m, 120m, true), s);
        var cleared = await x.Sales.UpdateShippingSettingsAsync(new SalesShippingSettingsRequest(Charge: 0m, FreeFrom: 0m), default);
        Assert.Equal((null, null, true), (cleared.Charge, cleared.FreeFrom, cleared.Taxable));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateShippingSettingsAsync(new SalesShippingSettingsRequest(Charge: 12.345m), default));
        Assert.Equal(ShippingRules.ChargeInvalid, ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => x.Sales.UpdateShippingSettingsAsync(new SalesShippingSettingsRequest(FreeFrom: -5m), default));
    }
}

// Lote 222: el cargo de envío llega a la factura como una línea de servicio, una sola vez por orden, y vuelve a «por facturar» si se cancela la corrida.
public sealed class SalesShippingInvoiceTests
{
    [Fact]
    public async Task The_shipping_charge_is_invoiced_once_as_a_service_line_and_cancelling_the_run_releases_it()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        await x.SetCompanyShippingAsync(12m, 120m);
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("C1");
        var cons = await x.ConsigneeAsync(c);
        var o = await x.Sales.RequestAsync((await x.DraftAsync(c, p, 4m, consignee: cons.PublicId)).PublicId, null, default);
        Assert.Equal((12m, 52m), (o.Terms!.ShippingCharge, o.Total));
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default);

        var billing = x.F.Get<Teikem.Infrastructure.Services.BillingService>();
        var period = new BillingRunRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
        var run = await billing.GenerateAsync(period, default);
        var invoice = await billing.GetInvoiceAsync(run.Invoices.Single().PublicId, default);
        var shipping = Assert.Single(invoice.Lines, l => l.ChargeType == SalesShippingCodes.ChargeType);
        Assert.Equal((1m, 12m, (Guid?)null), (shipping.Quantity, shipping.UnitAmount, shipping.ProductPublicId));
        Assert.Equal(32m, invoice.Subtotal);   // 2 × $10 + el envío

        // el resto se despacha y se factura: el envío no vuelve a salir
        await x.Sales.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default);
        var run2 = await billing.GenerateAsync(period, default);
        var invoice2 = await billing.GetInvoiceAsync(run2.Invoices.Single().PublicId, default);
        Assert.DoesNotContain(invoice2.Lines, l => l.ChargeType == SalesShippingCodes.ChargeType);

        // cancelar la primera corrida devuelve el envío a «por facturar»: sale en la siguiente
        await billing.CancelAsync(run.Id, "prueba", default);
        Assert.False((await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(s => s.PublicId == o.PublicId)).ShippingInvoiced);
        var run3 = await billing.GenerateAsync(period, default);
        var invoice3 = await billing.GetInvoiceAsync(run3.Invoices.Single().PublicId, default);
        Assert.Single(invoice3.Lines, l => l.ChargeType == SalesShippingCodes.ChargeType);
    }
}
