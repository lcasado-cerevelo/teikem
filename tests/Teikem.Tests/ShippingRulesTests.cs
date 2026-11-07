using Teikem.Domain.Sales;
using Xunit;

namespace Teikem.Tests;

// Lote 222 (P2 del pedido de vendedores): la regla de envío — compañía o cliente (NULL = la de la compañía; el cliente gana), mínimo opcional medido después del IVU,
// solo en entrega, y el porqué.
public sealed class ShippingRulesTests
{
    private static ShippingResult Run(decimal total, string? delivery = SalesDeliveryModes.Delivery, bool consignee = true, string? clientMode = null, decimal? clientCharge = null,
        decimal? clientFreeFrom = null, decimal? companyCharge = 12m, decimal? companyFreeFrom = 120m)
        => ShippingRules.Compute(new ShippingInput(delivery, consignee, total, clientMode, clientCharge, clientFreeFrom, companyCharge, companyFreeFrom));

    [Fact]
    public void The_company_rule_charges_below_the_minimum_and_is_free_from_it()
    {
        Assert.Equal(new ShippingResult(12m, ShippingReasons.CompanyRule), Run(119.99m));
        Assert.Equal(new ShippingResult(0m, ShippingReasons.Amount), Run(120m));   // llega al mínimo: gratis
        Assert.Equal(new ShippingResult(0m, ShippingReasons.Amount), Run(500m));
    }

    [Fact]
    public void The_minimum_is_measured_after_tax_by_the_caller_total()
    {
        // $110 de productos + 11.5% de IVU = $122.65: ya llega a $120 (la regla recibe el total con IVU)
        var subtotal = 110m;
        var withTax = subtotal + decimal.Round(subtotal * 0.115m, 2);
        Assert.Equal(ShippingReasons.Amount, Run(withTax).Reason);
        Assert.Equal(ShippingReasons.CompanyRule, Run(subtotal).Reason);
    }

    [Fact]
    public void Without_a_minimum_the_charge_always_applies_and_without_a_charge_there_is_nothing()
    {
        Assert.Equal(new ShippingResult(12m, ShippingReasons.CompanyRule), Run(10_000m, companyFreeFrom: null));
        Assert.Equal(new ShippingResult(0m, null), Run(50m, companyCharge: null));   // la compañía no cobra envío
        Assert.Equal(new ShippingResult(0m, null), Run(50m, companyCharge: 0m));
    }

    [Fact]
    public void Only_deliveries_pay_shipping_and_the_mode_is_deduced_from_the_consignee_when_missing()
    {
        Assert.Equal(new ShippingResult(0m, ShippingReasons.Pickup), Run(50m, delivery: SalesDeliveryModes.Pickup));
        Assert.Equal(new ShippingResult(0m, ShippingReasons.Pickup), Run(50m, delivery: null, consignee: false));   // sin consignatario = recogido
        Assert.Equal(new ShippingResult(12m, ShippingReasons.CompanyRule), Run(50m, delivery: null, consignee: true));
        Assert.Equal(SalesDeliveryModes.Delivery, ShippingRules.EffectiveMode("delivery", false));   // la orden manda sobre el consignatario
        Assert.Equal(SalesDeliveryModes.Pickup, ShippingRules.EffectiveMode(SalesDeliveryModes.Pickup, true));
    }

    [Fact]
    public void The_client_wins_free_or_its_own_rule_and_null_means_the_company()
    {
        Assert.Equal(new ShippingResult(0m, ShippingReasons.ClientFree), Run(10m, clientMode: ClientShippingModes.Free));
        Assert.Equal(new ShippingResult(8m, ShippingReasons.ClientRule), Run(70m, clientMode: ClientShippingModes.Own, clientCharge: 8m, clientFreeFrom: 75m));
        Assert.Equal(new ShippingResult(0m, ShippingReasons.Amount), Run(75m, clientMode: ClientShippingModes.Own, clientCharge: 8m, clientFreeFrom: 75m));
        Assert.Equal(new ShippingResult(8m, ShippingReasons.ClientRule), Run(5_000m, clientMode: ClientShippingModes.Own, clientCharge: 8m));   // regla propia sin mínimo
        // NULL (o lo que no sea FREE/OWN) = la de la compañía, con sus valores de HOY (no hay copia en el cliente)
        Assert.Equal(new ShippingResult(15m, ShippingReasons.CompanyRule), Run(50m, clientMode: null, companyCharge: 15m));
        // el cliente gratis aunque la compañía cobre; y en recogido manda el recogido
        Assert.Equal(ShippingReasons.Pickup, Run(10m, delivery: SalesDeliveryModes.Pickup, clientMode: ClientShippingModes.Free).Reason);
    }

    [Fact]
    public void The_client_mode_and_the_amounts_are_validated_with_their_messages()
    {
        static (string?, string?) T((string? A, string? B) t) => t;
        Assert.Equal((null, null), T(ShippingRules.ParseClientMode("COMPANY")));
        Assert.Equal((null, null), T(ShippingRules.ParseClientMode("")));
        Assert.Equal((ClientShippingModes.Free, null), T(ShippingRules.ParseClientMode("free")));
        Assert.Equal((null, ShippingRules.ModeInvalid), T(ShippingRules.ParseClientMode("GRATIS")));
        Assert.Equal((null, ShippingRules.DeliveryModeInvalid), T(ShippingRules.ParseDeliveryMode("TRUCK")));
        Assert.Equal((SalesDeliveryModes.Pickup, null), T(ShippingRules.ParseDeliveryMode(" pickup ")));
        Assert.True(ShippingRules.IsValidCharge(12m));
        Assert.False(ShippingRules.IsValidCharge(0m));
        Assert.False(ShippingRules.IsValidCharge(12.345m));
        Assert.True(ShippingRules.IsValidFreeFrom(0m));
        Assert.False(ShippingRules.IsValidFreeFrom(-1m));
        Assert.Equal("El cargo de envío debe ser un monto mayor que cero con hasta 2 decimales.", ShippingRules.ChargeInvalid);
    }
}
