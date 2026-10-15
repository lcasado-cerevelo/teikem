using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 59 (P4, S1): backorders — la vista (antigüedad), surtir (lo que haya, por orden de antigüedad) y cerrar con faltante.
public sealed class SalesBackorderTests
{
    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, WarehouseBin bin)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public WarehouseBin Bin { get; } = bin;
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
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        return new World(f, w, c, f.Get<SalesOrderService>(), bin);
    }

    private static async Task<Product> ProductAsync(World x, string sku)
    {
        var p = await x.F.AddProductAsync(sku);
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 10m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return p;
    }

    /// <summary>Entra inventario (como lo haría un recibo): suma al saldo de la posición.</summary>
    private static async Task ReceiveAsync(World x, Product p, decimal qty)
    {
        var bal = await x.F.Db.StockBalances.AsTracking().FirstOrDefaultAsync(b => b.ProductId == p.ProductId && b.WarehouseBinId == x.Bin.WarehouseBinId);
        if (bal is null)
            x.F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = x.W.WarehouseId, WarehouseBinId = x.Bin.WarehouseBinId, QtyOnHand = qty, UpdatedAtUtc = DateTime.UtcNow });
        else bal.QtyOnHand += qty;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static SalesOrderLineRequest L(Product p, decimal qty) => new(p.PublicId, qty);

    private static Task<SalesOrderDto> DraftAsync(World x, params SalesOrderLineRequest[] lines) => x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: lines), default);

    private static Task<SalesOrderDto> RequestWithBackorderAsync(World x, SalesOrderDto draft) => x.Svc.RequestAsync(draft.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);

    private static async Task<decimal> ReservedAsync(World x, Product p) => (await x.F.BalanceAsync(p.ProductId, x.Bin.WarehouseBinId))?.QtyReserved ?? 0m;

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    /// <summary>Dos órdenes con backorder del mismo producto: A (la primera, pide 10) y B (la segunda, pide 5); hay 4 en existencia.</summary>
    private static async Task<(World X, Product P, SalesOrderDto A, SalesOrderDto B)> TwoWaitingAsync()
    {
        var x = await NewWorldAsync();
        var p = await ProductAsync(x, "MED");
        await ReceiveAsync(x, p, 4m);
        var a = await RequestWithBackorderAsync(x, await DraftAsync(x, L(p, 10m)));   // reserva 4, quedan 6 en backorder
        var b = await RequestWithBackorderAsync(x, await DraftAsync(x, L(p, 5m)));    // nada disponible: 5 en backorder
        return (x, p, a, b);
    }

    [Fact]
    public async Task The_backorder_view_lists_oldest_first_with_available_and_what_could_be_fulfilled_now()
    {
        var (x, p, a, b) = await TwoWaitingAsync();
        await using var _ = x.F;
        Assert.Equal((4m, 6m), (a.Lines[0].QtyReserved, a.Lines[0].QtyBackordered));
        Assert.Equal((0m, 5m), (b.Lines[0].QtyReserved, b.Lines[0].QtyBackordered));

        var none = await x.Svc.BackordersAsync(null, default);
        Assert.Equal(new[] { a.Number, b.Number }, none.Select(r => r.OrderNumber).ToArray());   // el primero que pidió, primero
        Assert.All(none, r => Assert.Equal(0m, r.Fulfillable));   // todavía no entró inventario
        Assert.Equal((6m, 0m), (none[0].Pending, none[0].Available));

        await ReceiveAsync(x, p, 8m);
        var now = await x.Svc.BackordersAsync(null, default);
        Assert.Equal((6m, 8m, 6m), (now[0].Pending, now[0].Available, now[0].Fulfillable));
        Assert.Equal((5m, 8m, 5m), (now[1].Pending, now[1].Available, now[1].Fulfillable));   // cada línea ve lo disponible; al surtir todo el primero toma antes
        Assert.Equal(2, (await x.Svc.BackordersAsync("MED", default)).Count);
        Assert.Equal(new[] { b.Number }, (await x.Svc.BackordersAsync(b.Number, default)).Select(r => r.OrderNumber).ToArray());
        Assert.Empty(await x.Svc.BackordersAsync("no-existe", default));
    }

    [Fact]
    public async Task Fulfilling_a_line_reserves_what_there_is_all_or_part_and_nothing_when_there_is_nothing()
    {
        var (x, p, a, _) = await TwoWaitingAsync();
        await using var _ = x.F;
        var lineId = a.Lines[0].Id;
        Assert.Equal(SalesRules.NothingToFulfill, await MsgOf(() => x.Svc.FulfillLineAsync(a.PublicId, lineId, default)));   // no hay nada nuevo

        await ReceiveAsync(x, p, 2m);
        var part = await x.Svc.FulfillLineAsync(a.PublicId, lineId, default);
        Assert.Equal((6m, 4m, true), (part.Lines[0].QtyReserved, part.Lines[0].QtyBackordered, part.HasBackorder));   // 4 + 2 reservadas, 4 pendientes
        Assert.Equal(6m, await ReservedAsync(x, p));

        await ReceiveAsync(x, p, 20m);
        var whole = await x.Svc.FulfillLineAsync(a.PublicId, lineId, default);
        Assert.Equal((10m, 0m), (whole.Lines[0].QtyReserved, whole.Lines[0].QtyBackordered));
        Assert.Equal(10m, await ReservedAsync(x, p));
        Assert.Equal(SalesRules.NoBackorder, await MsgOf(() => x.Svc.FulfillLineAsync(a.PublicId, lineId, default)));
        var rows = await x.F.Db.Set<SalesOrderReservation>().Where(r => r.SalesOrderLineId == lineId).ToListAsync();
        Assert.Equal(10m, rows.Sum(r => r.Quantity));
    }

    [Fact]
    public async Task Fulfill_all_serves_the_oldest_first_and_what_one_takes_the_next_does_not_see()
    {
        var (x, p, a, b) = await TwoWaitingAsync();
        await using var _ = x.F;
        await ReceiveAsync(x, p, 8m);   // 8 disponibles: A necesita 6 y B 5 → A se surte completa y B recibe solo 2

        var result = await x.Svc.FulfillAllAsync(default);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(new[] { a.Number, b.Number }, result.Lines.Select(l => l.OrderNumber).ToArray());
        Assert.Equal((6m, 0m), (result.Lines[0].Reserved, result.Lines[0].StillPending));
        Assert.Equal((2m, 3m), (result.Lines[1].Reserved, result.Lines[1].StillPending));
        Assert.Equal(8m, result.TotalReserved);
        Assert.Equal(1, result.LinesCompleted);
        Assert.Equal(12m, await ReservedAsync(x, p));   // 4 + 8 reservadas en total, nunca más de las 12 en mano
        Assert.Equal(12m, (await x.F.BalanceAsync(p.ProductId, x.Bin.WarehouseBinId))!.QtyOnHand);
        // lo que queda pendiente sigue en la vista; lo completo ya no
        var left = await x.Svc.BackordersAsync(null, default);
        Assert.Single(left);
        Assert.Equal((b.Number, 3m), (left[0].OrderNumber, left[0].Pending));
        // sin inventario nuevo, surtir todo no hace nada
        Assert.Empty((await x.Svc.FulfillAllAsync(default)).Lines);
    }

    [Fact]
    public async Task Closing_a_line_with_shortage_stops_the_wait_keeps_the_reservation_and_the_ordered_quantity()
    {
        var (x, p, a, _) = await TwoWaitingAsync();
        await using var _ = x.F;
        var lineId = a.Lines[0].Id;
        var closed = await x.Svc.CloseLineAsync(a.PublicId, lineId, "Ya no esperamos más", default);
        Assert.Equal((10m, 4m, 0m, true), (closed.Lines[0].QtyOrdered, closed.Lines[0].QtyReserved, closed.Lines[0].QtyBackordered, closed.Lines[0].IsClosed));
        Assert.False(closed.HasBackorder);
        Assert.Equal(4m, await ReservedAsync(x, p));   // lo reservado sigue reservado para despacharse
        Assert.DoesNotContain(await x.Svc.BackordersAsync(null, default), r => r.OrderNumber == a.Number);
        Assert.Equal(SalesRules.LineClosed, await MsgOf(() => x.Svc.CloseLineAsync(a.PublicId, lineId, null, default)));
        Assert.Equal(SalesRules.LineClosed, await MsgOf(() => x.Svc.FulfillLineAsync(a.PublicId, lineId, default)));
        Assert.Equal(SalesRules.LineClosed, await MsgOf(() => x.Svc.ModifyLineAsync(a.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 3m), default)));
    }

    [Fact]
    public async Task Cancelling_an_order_with_backorder_removes_it_from_the_view_and_lets_the_next_one_be_fulfilled()
    {
        var (x, p, a, b) = await TwoWaitingAsync();
        await using var _ = x.F;
        await x.Svc.CancelAsync(a.PublicId, "Cancelada", default);   // libera sus 4 reservadas y borra su backorder
        var view = await x.Svc.BackordersAsync(null, default);
        Assert.Equal(new[] { b.Number }, view.Select(r => r.OrderNumber).ToArray());
        Assert.Equal((5m, 4m, 4m), (view[0].Pending, view[0].Available, view[0].Fulfillable));
        var fulfilled = await x.Svc.FulfillLineAsync(b.PublicId, b.Lines[0].Id, default);
        Assert.Equal((4m, 1m), (fulfilled.Lines[0].QtyReserved, fulfilled.Lines[0].QtyBackordered));
    }

    [Fact]
    public async Task Backorder_actions_need_a_requested_order_and_a_line_with_backorder()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        await ReceiveAsync(x, p, 10m);
        var draft = await DraftAsync(x, L(p, 3m));
        Assert.Equal(SalesRules.OnlyRequestedModifiable, await MsgOf(() => x.Svc.FulfillLineAsync(draft.PublicId, draft.Lines[0].Id, default)));
        Assert.Equal(SalesRules.OnlyRequestedModifiable, await MsgOf(() => x.Svc.CloseLineAsync(draft.PublicId, draft.Lines[0].Id, null, default)));
        var requested = await x.Svc.RequestAsync(draft.PublicId, null, default);   // alcanza: sin backorder
        Assert.Equal(SalesRules.NoBackorder, await MsgOf(() => x.Svc.FulfillLineAsync(draft.PublicId, requested.Lines[0].Id, default)));
        Assert.Equal(SalesRules.NoBackorder, await MsgOf(() => x.Svc.CloseLineAsync(draft.PublicId, requested.Lines[0].Id, null, default)));
        Assert.Equal("Línea no encontrada.", await MsgOf(() => x.Svc.FulfillLineAsync(draft.PublicId, 99999, default)));
        Assert.Equal("Orden de venta no encontrada.", await MsgOf(() => x.Svc.FulfillLineAsync(Guid.NewGuid(), 1, default)));
        Assert.Empty(await x.Svc.BackordersAsync(null, default));
    }

    [Fact]
    public async Task Another_company_sees_no_backorders_and_cannot_fulfill_them()
    {
        var (x, _, a, _) = await TwoWaitingAsync();
        await using var _f = x.F;
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            x.F.SetModules(ModuleKeys.SalesOrders);
            Assert.Empty(await x.Svc.BackordersAsync(null, default));
            Assert.Empty((await x.Svc.FulfillAllAsync(default)).Lines);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.FulfillLineAsync(a.PublicId, a.Lines[0].Id, default));
        }
    }
}
