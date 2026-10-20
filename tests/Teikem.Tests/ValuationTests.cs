using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 148 — valuación del inventario: reglas puras (promedio, capas, costo de salida) y el servicio sobre recibos confirmados con precio.</summary>
public sealed class ValuationTests
{
    private static readonly CostLayer[] TwoLayers = { new(10m, 5m), new(10m, 8m) };   // la más vieja primero

    [Fact]
    public void Method_parses_only_the_two_values()
    {
        Assert.Equal("AVERAGE", ValuationRules.Parse(" average "));
        Assert.Equal("LAYERS", ValuationRules.Parse("layers"));
        Assert.Null(ValuationRules.Parse("FIFO"));
        Assert.Null(ValuationRules.Parse(null));
    }

    [Fact]
    public void Average_values_what_is_on_hand_at_the_weighted_average_of_every_layer()
    {
        var v = ValuationRules.Value("AVERAGE", 8m, TwoLayers, 12.5m);
        Assert.Equal((52m, 6.5m, 0m), (v.Value, v.UnitCost, v.UnvaluedQty));
    }

    [Fact]
    public void Layers_attribute_what_is_on_hand_to_the_newest_layers()
    {
        var v = ValuationRules.Value("LAYERS", 12m, TwoLayers, 12.5m);   // 10 de la nueva (8) + 2 de la vieja (5)
        Assert.Equal((90m, 7.5m, 0m), (v.Value, v.UnitCost, v.UnvaluedQty));
        // existencia más allá de todas las capas = inventario anterior: costo del producto
        var more = ValuationRules.Value("LAYERS", 25m, TwoLayers, 2m);
        Assert.Equal(5m * 2m + 10m * 5m + 10m * 8m, more.Value);
    }

    [Fact]
    public void A_layer_without_cost_uses_the_product_cost_and_without_any_it_is_unvalued()
    {
        var layers = new[] { new CostLayer(4m, null) };
        Assert.Equal(12m, ValuationRules.Value("LAYERS", 4m, layers, 3m).Value);
        var none = ValuationRules.Value("LAYERS", 4m, layers, null);
        Assert.Equal((0m, null, 4m), (none.Value, none.UnitCost, none.UnvaluedQty));
        var avg = ValuationRules.Value("AVERAGE", 4m, layers, null);
        Assert.Equal((0m, null, 4m), (avg.Value, avg.UnitCost, avg.UnvaluedQty));
        Assert.Equal((0m, null, 0m), (ValuationRules.Value("AVERAGE", 0m, layers, 3m) is var z ? (z.Value, z.UnitCost, z.UnvaluedQty) : default));
    }

    [Fact]
    public void Issue_cost_is_the_average_or_the_oldest_remaining_layer()
    {
        Assert.Equal(6.5m, ValuationRules.IssueUnitCost("AVERAGE", 20m, TwoLayers, 12.5m));
        Assert.Equal(5m, ValuationRules.IssueUnitCost("LAYERS", 20m, TwoLayers, 12.5m));   // todo sigue ahí: sale la vieja
        Assert.Equal(8m, ValuationRules.IssueUnitCost("LAYERS", 10m, TwoLayers, 12.5m));   // la vieja ya se fue
        Assert.Equal(8m, ValuationRules.IssueUnitCost("LAYERS", 4m, TwoLayers, 12.5m));
        Assert.Equal(2m, ValuationRules.IssueUnitCost("LAYERS", 25m, TwoLayers, 2m));      // lo más viejo es inventario sin capa
        Assert.Equal(2m, ValuationRules.IssueUnitCost("LAYERS", 5m, Array.Empty<CostLayer>(), 2m));
        Assert.Null(ValuationRules.IssueUnitCost("AVERAGE", 5m, Array.Empty<CostLayer>(), null));
    }

    // ---------------------------------------------------------------- servicio

    private static async Task<(ReceivingFixture F, int ProductId)> TwoReceiptsAsync()
    {
        var f = await ReceivingFixture.CreateAsync();
        var receipts = f.Get<ReceiptService>();
        foreach (var price in new[] { 5m, 8m })
        {
            var asn = await f.Get<AsnService>().CreateAsync(new AsnCreateRequest(null, f.ClientPublicId,
                Lines: new[] { new AsnLineRequest(f.ProductClientPublicId, 10m, UnitPrice: price) }), default);
            var created = await receipts.CreateAsync(new ReceiptCreateRequest(AsnId: asn.Id), default);
            await receipts.ConfirmAsync(created.Header.PublicId, null, default);
        }
        var productId = (await f.Db.Set<Product>().AsNoTracking().SingleAsync(p => p.PublicId == f.ProductClientPublicId)).ProductId;
        return (f, productId);
    }

    private static Task IssueAsync(ReceivingFixture f, int productId, decimal qty)
        => f.Get<InventoryLedger>().PostAsync(new[]
        {
            new InventoryPosting(InventoryTxnTypes.Issue, productId, qty, FromWarehouseId: f.WarehouseId, FromBinId: f.StagingBinId),
        }, default);

    private static async Task SetMethodAsync(ReceivingFixture f, string method)
    {
        var tenant = await f.Db.Tenants.SingleAsync(t => t.TenantId == ReceivingFixture.TenantId);
        tenant.ValuationMethod = method;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Report_values_the_stock_by_the_company_method_and_totals_the_whole_filter()
    {
        var (f, productId) = await TwoReceiptsAsync();
        await using var _ = f;
        await IssueAsync(f, productId, 12m);   // quedan 8
        var svc = f.Get<ValuationService>();

        var avg = await svc.ReportAsync(null, null, null, 0, 50, default);
        Assert.Equal("AVERAGE", avg.Method);
        var row = Assert.Single(avg.Items);
        Assert.Equal((8m, 6.5m, 52m), (row.OnHand, row.UnitCost, row.Value));
        Assert.Equal((52m, 8m, 0m), (avg.TotalValue, avg.TotalQty, avg.UnvaluedQty));

        await SetMethodAsync(f, "LAYERS");
        var layers = await svc.ReportAsync(null, null, null, 0, 50, default);
        Assert.Equal("LAYERS", layers.Method);
        Assert.Equal((64m, 8m), (Assert.Single(layers.Items).Value, Assert.Single(layers.Items).UnitCost));   // lo que queda es de la capa nueva (8)

        // filtros: buscador sin coincidencias, cliente ajeno 404
        Assert.Empty((await svc.ReportAsync(null, null, "zzz", 0, 50, default)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ReportAsync(null, Guid.NewGuid(), null, 0, 50, default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.ReportAsync(Guid.NewGuid(), null, null, 0, 50, default));
    }

    [Fact]
    public async Task Issue_costs_follow_the_method_and_advance_line_by_line()
    {
        var (f, productId) = await TwoReceiptsAsync();
        await using var _ = f;
        var svc = f.Get<ValuationService>();
        // se llama con la salida ya asentada: había 20 antes de sacar 10 + 5
        await IssueAsync(f, productId, 15m);
        Assert.Equal(new decimal?[] { 6.5m, 6.5m }, await svc.IssueCostsAsync(new[] { (productId, 10m), (productId, 5m) }, default));
        await SetMethodAsync(f, "LAYERS");
        var costs = await svc.IssueCostsAsync(new[] { (productId, 10m), (productId, 5m) }, default);
        Assert.Equal(new decimal?[] { 5m, 8m }, costs);   // las primeras 10 consumen la capa vieja entera; la siguiente sale de la nueva
    }
}
