using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
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

// Lote 61 (P4, S2): despachar contra la orden de venta — consume la reserva, saca el inventario, crea la recolección enlazada y deja la orden en parcial o despachada.
public sealed class SalesDispatchTests
{
    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, WarehouseBin b1, WarehouseBin b2)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public WarehouseBin B1 { get; } = b1;
        public WarehouseBin B2 { get; } = b2;
    }

    private static async Task<World> NewWorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var zone = await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking);
        return new World(f, w, c, f.Get<SalesOrderService>(), await f.AddBinAsync(zone, "P-01"), await f.AddBinAsync(zone, "P-02"));
    }

    private static async Task<Product> ProductAsync(World x, string sku)
    {
        var p = await x.F.AddProductAsync(sku, purchaseCost: 4m);
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return p;
    }

    private static async Task StockAsync(World x, Product p, WarehouseBin bin, decimal qty)
    {
        x.F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = x.W.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = qty, UpdatedAtUtc = DateTime.UtcNow });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static SalesOrderLineRequest L(Product p, decimal qty) => new(p.PublicId, qty);

    /// <summary>Orden solicitada (con backorder si falta).</summary>
    private static async Task<SalesOrderDto> RequestedAsync(World x, params SalesOrderLineRequest[] lines)
    {
        var draft = await x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: lines), default);
        return await x.Svc.RequestAsync(draft.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
    }

    private static SalesDispatchRequest Dispatch(params SalesDispatchLineRequest[] lines) => new(lines);

    private static async Task<decimal> OnHandAsync(World x, Product p, WarehouseBin bin) => (await x.F.BalanceAsync(p.ProductId, bin.WarehouseBinId))?.QtyOnHand ?? 0m;

    private static async Task<decimal> ReservedAsync(World x, Product p, WarehouseBin bin) => (await x.F.BalanceAsync(p.ProductId, bin.WarehouseBinId))?.QtyReserved ?? 0m;

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Dispatching_everything_reserved_takes_the_stock_out_creates_the_linked_pick_batch_and_ships_the_order()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 10m);
        var o = await RequestedAsync(x, L(p, 6m));
        Assert.Equal(6m, await ReservedAsync(x, p, x.B1));

        var r = await x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(o.Lines[0].Id, 6m)), default);
        Assert.Equal("SHIPPED", r.Order.Status);
        Assert.Equal((6m, 0m, 0m), (r.Order.Lines[0].QtyShipped, r.Order.Lines[0].QtyReserved, r.Order.Lines[0].QtyBackordered));
        Assert.Equal(4m, await OnHandAsync(x, p, x.B1));   // salió del inventario SIEMPRE
        Assert.Equal(0m, await ReservedAsync(x, p, x.B1));   // y la reserva se consumió
        Assert.StartsWith("EMP-", r.PickBatchNumber);

        var batch = await x.F.Db.Set<PickBatch>().SingleAsync(b => b.PublicId == r.PickBatchPublicId);
        var orderId = (await x.F.Db.Set<SalesOrder>().SingleAsync(s => s.PublicId == o.PublicId)).SalesOrderId;
        Assert.Equal(orderId, batch.SalesOrderId);
        var line = await x.F.Db.Set<PickBatchLine>().SingleAsync(l => l.PickBatchId == batch.PickBatchId);
        Assert.Equal((6m, 4m, x.B1.WarehouseBinId), (line.Quantity, line.UnitCost, line.FromBinId));
        Assert.Equal(o.Lines[0].Id, line.SalesOrderLineId);
        var txn = await x.F.Db.InventoryTransactions.SingleAsync(t => t.InventoryTransactionId == line.IssueTxnId);
        Assert.Equal(-6m, txn.Quantity);
        Assert.Empty(await x.F.Db.Set<SalesOrderReservation>().ToListAsync());
        Assert.Equal(SalesRules.NotDispatchable, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(o.Lines[0].Id, 1m)), default)));
    }

    [Fact]
    public async Task Dispatching_by_parts_goes_partial_then_shipped_and_never_more_than_reserved()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 5m);
        await StockAsync(x, p, x.B2, 5m);
        var o = await RequestedAsync(x, L(p, 8m));   // reserva repartida entre dos posiciones
        var lineId = o.Lines[0].Id;

        Assert.Equal("No se puede despachar más de lo reservado: MED tiene 8 reservadas.", await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 9m)), default)));
        var first = await x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 3m)), default);
        Assert.Equal("PARTIAL", first.Order.Status);
        Assert.Equal((3m, 5m), (first.Order.Lines[0].QtyShipped, first.Order.Lines[0].QtyReserved));
        Assert.Equal(7m, (await OnHandAsync(x, p, x.B1)) + (await OnHandAsync(x, p, x.B2)));
        Assert.Equal(5m, (await ReservedAsync(x, p, x.B1)) + (await ReservedAsync(x, p, x.B2)));

        var second = await x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 5m)), default);
        Assert.Equal("SHIPPED", second.Order.Status);
        Assert.Equal((8m, 0m), (second.Order.Lines[0].QtyShipped, second.Order.Lines[0].QtyReserved));
        Assert.Equal(2m, (await OnHandAsync(x, p, x.B1)) + (await OnHandAsync(x, p, x.B2)));
        Assert.Equal(0m, (await ReservedAsync(x, p, x.B1)) + (await ReservedAsync(x, p, x.B2)));
        Assert.Equal(2, await x.F.Db.Set<PickBatch>().CountAsync());   // una recolección por despacho
        Assert.NotEqual(first.PickBatchNumber, second.PickBatchNumber);
    }

    [Fact]
    public async Task Taking_from_another_position_moves_the_reservation_there_when_it_has_stock()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 5m);
        await StockAsync(x, p, x.B2, 5m);
        var o = await RequestedAsync(x, L(p, 4m));   // se reserva en P-01
        Assert.Equal((4m, 0m), (await ReservedAsync(x, p, x.B1), await ReservedAsync(x, p, x.B2)));

        var r = await x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(o.Lines[0].Id, 3m, x.B2.WarehouseBinId)), default);
        Assert.Equal("PARTIAL", r.Order.Status);
        Assert.Equal((5m, 2m), (await OnHandAsync(x, p, x.B1), await OnHandAsync(x, p, x.B2)));   // salió de P-02
        Assert.Equal((1m, 0m), (await ReservedAsync(x, p, x.B1), await ReservedAsync(x, p, x.B2)));   // lo que falta por despachar sigue reservado en P-01
        Assert.Equal((3m, 1m), (r.Order.Lines[0].QtyShipped, r.Order.Lines[0].QtyReserved));
        var rows = await x.F.Db.Set<SalesOrderReservation>().ToListAsync();
        Assert.Equal(1m, rows.Sum(rr => rr.Quantity));
        var line = await x.F.Db.Set<PickBatchLine>().SingleAsync();
        Assert.Equal(x.B2.WarehouseBinId, line.FromBinId);
    }

    [Fact]
    public async Task Taking_from_a_position_without_stock_or_a_non_sellable_one_is_refused()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 5m);
        var quarantine = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "QZ", ZoneTypes.Quarantine), "Q-01");
        await StockAsync(x, p, quarantine, 5m);
        var o = await RequestedAsync(x, L(p, 4m));
        var lineId = o.Lines[0].Id;

        await Assert.ThrowsAsync<InsufficientStockException>(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 3m, x.B2.WarehouseBinId)), default));   // P-02 está vacía
        Assert.Equal(SalesRules.BinNotSellable("Q-01"), await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 3m, quarantine.WarehouseBinId)), default)));
        Assert.Equal("Posición no encontrada.", await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 3m, 99999)), default)));
    }

    [Fact]
    public async Task Dispatch_validations_return_the_exact_message()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 5m);
        var draft = await x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { L(p, 2m) }), default);
        Assert.Equal(SalesRules.NotDispatchable, await MsgOf(() => x.Svc.DispatchAsync(draft.PublicId, Dispatch(new SalesDispatchLineRequest(draft.Lines[0].Id, 1m)), default)));   // borrador
        var o = await x.Svc.RequestAsync(draft.PublicId, null, default);
        var lineId = o.Lines[0].Id;
        Assert.Equal(SalesRules.DispatchLinesRequired, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(), default)));
        Assert.Equal(SalesRules.DispatchLineRequired, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(null, 1m)), default)));
        Assert.Equal(SalesRules.DispatchLineRepeated, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 1m), new SalesDispatchLineRequest(lineId, 1m)), default)));
        Assert.Equal(SalesRules.QuantityRequired, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(lineId, 0m)), default)));
        Assert.Equal("Línea no encontrada.", await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(99999, 1m)), default)));
        Assert.Equal("Orden de venta no encontrada.", await MsgOf(() => x.Svc.DispatchAsync(Guid.NewGuid(), Dispatch(new SalesDispatchLineRequest(1, 1m)), default)));
        Assert.Empty(await x.F.Db.Set<PickBatch>().ToListAsync());
    }

    [Fact]
    public async Task With_a_backorder_pending_the_order_stays_partial_until_the_line_is_closed_with_shortage()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        var q = await ProductAsync(x, "TIRAS");
        await StockAsync(x, p, x.B1, 4m);
        await StockAsync(x, q, x.B1, 10m);
        var o = await RequestedAsync(x, L(p, 10m), L(q, 3m));   // MED: 4 reservadas y 6 en backorder; TIRAS completa
        var med = o.Lines.First(l => l.Sku == "MED");
        var tiras = o.Lines.First(l => l.Sku == "TIRAS");

        var r = await x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(med.Id, 4m), new SalesDispatchLineRequest(tiras.Id, 3m)), default);
        Assert.Equal("PARTIAL", r.Order.Status);   // lo reservado salió pero MED sigue con 6 pendientes
        Assert.True(r.Order.HasBackorder);
        var done = await x.Svc.CloseLineAsync(o.PublicId, med.Id, null, default);
        Assert.Equal("SHIPPED", done.Status);   // sin más reserva ni backorder: despachada
        Assert.False(done.HasBackorder);
    }

    [Fact]
    public async Task An_order_whose_lines_are_all_closed_with_shortage_and_nothing_shipped_cancels_itself_with_a_note()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        var o = await RequestedAsync(x, L(p, 5m));   // no hay inventario: todo en backorder
        Assert.Equal((0m, 5m), (o.Lines[0].QtyReserved, o.Lines[0].QtyBackordered));

        var closed = await x.Svc.CloseLineAsync(o.PublicId, o.Lines[0].Id, null, default);
        Assert.Equal("CANCELLED", closed.Status);
        var id = (await x.F.Db.Set<SalesOrder>().SingleAsync(s => s.PublicId == o.PublicId)).SalesOrderId;
        var history = await x.F.Db.Set<EntityStatusHistory>().Where(h => h.EntityId == id).ToListAsync();
        Assert.Contains(history, h => h.Comment == "Todas las líneas se cerraron con faltante y no se despachó nada.");
    }

    [Fact]
    public async Task Another_company_cannot_dispatch_the_order()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await StockAsync(x, p, x.B1, 5m);
        var o = await RequestedAsync(x, L(p, 2m));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            x.F.SetModules(ModuleKeys.SalesOrders);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.DispatchAsync(o.PublicId, Dispatch(new SalesDispatchLineRequest(o.Lines[0].Id, 1m)), default));
        }
        Assert.Equal(2m, await ReservedAsync(x, p, x.B1));
    }
}
