using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

// Lote 62 (P4, S2): deshacer la recolección de una orden de venta (devuelve la existencia, baja lo despachado y vuelve a reservar), la lista de despachos de la
// orden y las validaciones de la entrega.
public sealed class SalesReversalTests
{
    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, PickBatchService batches, WarehouseBin bin, Product p)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public PickBatchService Batches { get; } = batches;
        public WarehouseBin Bin { get; } = bin;
        public Product P { get; } = p;
    }

    private static async Task<World> NewWorldAsync(decimal stock = 10m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<ISalesDispatchReversal>(sp => sp.GetRequiredService<SalesOrderService>());
            s.AddSingleton(sp => new PickBatchService(
                sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
                sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<Teikem.Infrastructure.Orders.INumberSequenceService>(),
                sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!, sp.GetRequiredService<ISalesDispatchReversal>()));
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("MED", purchaseCost: 4m);
        var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = stock, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, w, c, f.Get<SalesOrderService>(), f.Get<PickBatchService>(), bin, p);
    }

    private static async Task<SalesOrderDto> RequestedAsync(World x, decimal qty)
    {
        var draft = await x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.P.PublicId, qty) }), default);
        return await x.Svc.RequestAsync(draft.PublicId, null, default);
    }

    private static async Task<decimal> OnHandAsync(World x) => (await x.F.BalanceAsync(x.P.ProductId, x.Bin.WarehouseBinId))?.QtyOnHand ?? 0m;
    private static async Task<decimal> ReservedAsync(World x) => (await x.F.BalanceAsync(x.P.ProductId, x.Bin.WarehouseBinId))?.QtyReserved ?? 0m;

    [Fact]
    public async Task Undoing_the_only_dispatch_returns_the_stock_reopens_the_order_and_reserves_it_again()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 6m);
        var d = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 6m) }), default);
        Assert.Equal("SHIPPED", d.Order.Status);
        Assert.Equal((4m, 0m), (await OnHandAsync(x), await ReservedAsync(x)));

        await x.Batches.DeleteAsync(d.PickBatchPublicId, new PickBatchDeleteRequest("Se devolvió"), default);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal("REQUESTED", after.Status);   // vuelve a estar solicitada
        Assert.Equal((0m, 6m), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved));
        Assert.Equal((10m, 6m), (await OnHandAsync(x), await ReservedAsync(x)));   // la existencia volvió y quedó reservada otra vez
        Assert.Equal(6m, (await x.F.Db.Set<SalesOrderReservation>().ToListAsync()).Sum(r => r.Quantity));
        var info = Assert.Single(after.Dispatches!);
        Assert.Equal(("CANCELLED", d.PickBatchNumber), (info.Status, info.Number));
        // y se puede volver a despachar
        var again = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 6m) }), default);
        Assert.Equal("SHIPPED", again.Order.Status);
        Assert.Equal(2, again.Order.Dispatches!.Count);
    }

    [Fact]
    public async Task Undoing_one_of_two_dispatches_leaves_the_order_partial_with_only_that_part_reserved_again()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 6m);
        var lineId = o.Lines[0].Id;
        var first = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, 3m) }), default);
        var second = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, 3m) }), default);
        Assert.Equal("SHIPPED", second.Order.Status);

        await x.Batches.DeleteAsync(second.PickBatchPublicId, null, default);
        var after = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal("PARTIAL", after.Status);
        Assert.Equal((3m, 3m), (after.Lines[0].QtyShipped, after.Lines[0].QtyReserved));
        Assert.Equal((7m, 3m), (await OnHandAsync(x), await ReservedAsync(x)));
        Assert.Equal(new[] { "COLLECTED", "CANCELLED" }, after.Dispatches!.Select(d => d.Status).ToArray());
        Assert.Equal(new[] { first.PickBatchNumber, second.PickBatchNumber }, after.Dispatches!.Select(d => d.Number).ToArray());
        Assert.Equal(new[] { 3m, 3m }, after.Dispatches!.Select(d => d.Quantity).ToArray());
    }

    [Fact]
    public async Task Undoing_a_normal_pick_batch_is_not_affected_by_the_sales_hook()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var created = await x.Batches.CollectAsync(new PickBatchCreateRequest(x.W.PublicId, new[] { new PickBatchLineRequest(x.P.PublicId, 2m) }), default);
        Assert.Equal(8m, await OnHandAsync(x));
        await x.Batches.DeleteAsync(created.PublicId, null, default);
        Assert.Equal((10m, 0m), (await OnHandAsync(x), await ReservedAsync(x)));
    }

    [Fact]
    public async Task Delivery_validations_return_the_exact_message()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 4m);
        var d = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 4m) }), default);
        var delivery = new SalesDeliveryService(x.F.Db, x.Batches, x.Svc);
        var order = new OrderCreateRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

        Assert.Equal(SalesRules.DeliveryBatchRequired, await MsgOf(() => delivery.CreateAsync(o.PublicId, new SalesDeliveryRequest(null, order), default)));
        Assert.Equal(SalesRules.DeliveryOrderRequired, await MsgOf(() => delivery.CreateAsync(o.PublicId, new SalesDeliveryRequest(d.PickBatchPublicId, null), default)));
        Assert.Equal("Orden de venta no encontrada.", await MsgOf(() => delivery.CreateAsync(Guid.NewGuid(), new SalesDeliveryRequest(d.PickBatchPublicId, order), default)));
        Assert.Equal("Despacho de esta orden no encontrado.", await MsgOf(() => delivery.CreateAsync(o.PublicId, new SalesDeliveryRequest(Guid.NewGuid(), order), default)));

        // un despacho deshecho no se entrega
        await x.Batches.DeleteAsync(d.PickBatchPublicId, null, default);
        Assert.Equal(SalesRules.DeliveryNotCollected(d.PickBatchNumber), await MsgOf(() => delivery.CreateAsync(o.PublicId, new SalesDeliveryRequest(d.PickBatchPublicId, order), default)));
    }
}
