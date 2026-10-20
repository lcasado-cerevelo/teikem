using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 147 — capa de costo por línea de recibo: reglas puras (prioridad PO &gt; precio del aviso &gt; costo del producto, validación del precio) y el servicio sobre el fixture de recepción:
/// el costo se congela al confirmar con su origen y un cambio posterior del producto, del aviso o de la orden no lo toca.
/// </summary>
public sealed class CostLayerTests
{
    [Fact]
    public void Priority_is_purchase_order_then_asn_price_then_product_cost()
    {
        Assert.Equal((9m, "PO"), CostLayerRules.Resolve(9m, 7m, 5m));
        Assert.Equal((7m, "ASN"), CostLayerRules.Resolve(null, 7m, 5m));
        Assert.Equal((5m, "PRODUCT"), CostLayerRules.Resolve(null, null, 5m));
        Assert.Equal((null, null), CostLayerRules.Resolve(null, null, null));
        // un costo en cero es un costo (no «falta»)
        Assert.Equal((0m, "PO"), CostLayerRules.Resolve(0m, 7m, 5m));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(12.3456, true)]
    [InlineData(-0.01, false)]
    [InlineData(1.23456, false)]
    [InlineData(100000000, false)]
    public void Unit_price_is_zero_or_more_with_four_decimals_at_most(double price, bool ok)
        => Assert.Equal(ok, CostLayerRules.ValidateUnitPrice(price == 0 && !ok ? null : (decimal?)(decimal)price) is null);

    [Fact]
    public async Task Client_asn_price_is_frozen_on_the_receipt_line_with_source_ASN()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 6m, UnitPrice: 7.25m) }), default);
        Assert.Equal(7.25m, Assert.Single(asn.Lines).UnitPrice);

        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);

        var line = Assert.Single(confirmed.Lines);
        Assert.Equal((7.25m, "ASN"), (line.UnitCost, line.CostSource));
        var row = await f.Db.Set<ReceiptLine>().AsNoTracking().SingleAsync();
        Assert.Equal((7.25m, "ASN"), (row.UnitCost, row.CostSource));
    }

    [Fact]
    public async Task Without_price_the_product_cost_is_frozen_and_later_changes_do_not_touch_the_layer()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 4m) }), default);
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        var line = Assert.Single(confirmed.Lines);
        Assert.Equal(("PRODUCT", 12.5m), (line.CostSource, line.UnitCost));   // el producto del cliente del fixture cuesta 12.5

        var product = await f.Db.Set<Product>().SingleAsync(p => p.PublicId == f.ProductClientPublicId);
        product.PurchaseCost = 99m;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var again = await receipts.GetAsync(created.Header.PublicId, default);
        Assert.Equal(12.5m, Assert.Single(again.Lines).UnitCost);
    }

    [Fact]
    public async Task Purchase_order_cost_wins_over_the_product_cost()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var poLine = await f.Db.Set<PurchaseOrderLine>().SingleAsync(l => l.PurchaseOrderLineId == f.PoLineId);
        poLine.UnitCost = 9m;   // el producto cuesta 12.5
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var confirmed = await f.Get<ReceiptService>().CreateAsync(new ReceiptCreateRequest(PurchaseOrderPublicId: f.PoPublicId,
            Lines: new[] { new ReceiptLineRequest(f.ProductNonePublicId, 3m) }, Confirm: true), default);
        var line = Assert.Single(confirmed.Lines);
        Assert.Equal((9m, "PO"), (line.UnitCost, line.CostSource));
    }

    [Fact]
    public async Task A_line_that_received_nothing_gets_no_layer()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 4m, UnitPrice: 3m) }), default);
        var receipts = f.Get<ReceiptService>();
        var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
        var line = Assert.Single(created.Lines);
        await receipts.UpdateLineAsync(created.Header.PublicId, line.Id, new ReceiptLineUpdateRequest(ReceivedQty: 0m), default);
        var confirmed = await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        var after = Assert.Single(confirmed.Lines);
        Assert.Null(after.CostSource);
        Assert.Null(after.UnitCost);
    }

    [Fact]
    public async Task A_negative_or_five_decimal_price_is_a_400_with_the_line_key()
    {
        await using var f = await ReceivingFixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
            Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 4m, UnitPrice: -1m) }), default));
        Assert.Equal(CostLayerRules.UnitPriceInvalid, Assert.Single(ex.Errors!["lines[0].unitPrice"]));
    }
}
