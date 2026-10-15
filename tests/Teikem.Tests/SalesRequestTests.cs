using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
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

// Lote 58 (P4, S1): solicitar = validar y reservar en la misma transacción; faltante y backorder; cancelar libera lo reservado; modificar una línea.
public sealed class SalesRequestTests
{
    private sealed class World(WmsFixture f, Warehouse w, Client c, SalesOrderService svc)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public WarehouseBin Pick1 { get; set; } = null!;
        public WarehouseBin Pick2 { get; set; } = null!;
    }

    private static async Task<World> NewWorldAsync(Action<IServiceCollection>? more = null)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            more?.Invoke(s);
        });
        f.SetModules(ModuleKeys.SalesOrders);
        var w = await f.AddWarehouseAsync("W1");
        var c = await f.AddClientAsync("C1");
        var pickZone = await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking);
        var world = new World(f, w, c, f.Get<SalesOrderService>()) { Pick1 = await f.AddBinAsync(pickZone, "P-01"), Pick2 = await f.AddBinAsync(pickZone, "P-02") };
        return world;
    }

    private static async Task<Product> ProductAsync(World x, string sku, decimal price = 10m)
    {
        var p = await x.F.AddProductAsync(sku);
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = price;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return p;
    }

    private static async Task StockAsync(World x, Product p, WarehouseBin bin, decimal qty, InventoryLot? lot = null, Warehouse? w = null)
    {
        x.F.Db.StockBalances.Add(new StockBalance
        {
            TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = (w ?? x.W).WarehouseId, WarehouseBinId = bin.WarehouseBinId, LotId = lot?.LotId,
            QtyOnHand = qty, QtyReserved = 0m, UpdatedAtUtc = DateTime.UtcNow,
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static SalesOrderLineRequest L(Product p, decimal qty, decimal? price = null, int? lotId = null) => new(p.PublicId, qty, price, lotId);

    private static Task<SalesOrderDto> DraftAsync(World x, params SalesOrderLineRequest[] lines) => x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: lines), default);

    private static async Task<decimal> ReservedAsync(World x, Product p, WarehouseBin bin, InventoryLot? lot = null) => (await x.F.BalanceAsync(p.ProductId, bin.WarehouseBinId, lot?.LotId))?.QtyReserved ?? 0m;

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task Requesting_reserves_in_the_same_transaction_and_keeps_exactly_what_each_line_reserved()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        var q = await ProductAsync(x, "P2");
        await StockAsync(x, p, x.Pick1, 4m);
        await StockAsync(x, p, x.Pick2, 10m);
        await StockAsync(x, q, x.Pick1, 5m);
        var o = await DraftAsync(x, L(p, 6m), L(q, 2m));

        var r = await x.Svc.RequestAsync(o.PublicId, null, default);
        Assert.Equal("REQUESTED", r.Status);
        Assert.NotNull(r.RequestedAtUtc);
        Assert.All(r.Lines, l => Assert.Equal(0m, l.QtyBackordered));
        Assert.Equal(new[] { 6m, 2m }, r.Lines.Select(l => l.QtyReserved).ToArray());
        Assert.False(r.HasBackorder);
        // el ledger reservó lo mismo: 6 de P1 (4 + 2 por rotación/código de posición) y 2 de P2
        Assert.Equal(6m, await ReservedAsync(x, p, x.Pick1) + await ReservedAsync(x, p, x.Pick2));
        Assert.Equal(2m, await ReservedAsync(x, q, x.Pick1));
        var lineIds = r.Lines.Select(l => l.Id).ToList();
        var rows = await x.F.Db.Set<SalesOrderReservation>().Where(rr => lineIds.Contains(rr.SalesOrderLineId)).ToListAsync();
        Assert.Equal(8m, rows.Sum(rr => rr.Quantity));
        Assert.Equal("Solo se puede solicitar una orden en borrador.", await MsgOf(() => x.Svc.RequestAsync(o.PublicId, null, default)));
    }

    [Fact]
    public async Task A_shortage_answers_ordered_available_shortage_and_reserves_nothing_unless_backorder_is_allowed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "MED");
        var q = await ProductAsync(x, "TIRAS");
        await StockAsync(x, p, x.Pick1, 6m);
        await StockAsync(x, q, x.Pick1, 100m);
        var o = await DraftAsync(x, L(p, 10m), L(q, 4m));

        var check = await x.Svc.CheckAsync(o.PublicId, default);
        Assert.True(check.HasShortage);
        Assert.Equal((10m, 6m, 4m), (check.Lines[0].Ordered, check.Lines[0].Available, check.Lines[0].Shortage));
        Assert.Equal((4m, 100m, 0m), (check.Lines[1].Ordered, check.Lines[1].Available, check.Lines[1].Shortage));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => x.Svc.RequestAsync(o.PublicId, null, default));
        Assert.Equal("No alcanza el inventario de MED: pedido 10, disponible 6, faltante 4.", ex.Message);
        Assert.Equal(0m, await ReservedAsync(x, p, x.Pick1));   // nada se reservó, ni siquiera lo de las líneas que sí alcanzaban
        Assert.Equal(0m, await ReservedAsync(x, q, x.Pick1));
        Assert.Equal("DRAFT", (await x.Svc.GetAsync(o.PublicId, default)).Status);

        var r = await x.Svc.RequestAsync(o.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        Assert.Equal("REQUESTED", r.Status);
        Assert.Equal((6m, 4m), (r.Lines[0].QtyReserved, r.Lines[0].QtyBackordered));
        Assert.Equal((4m, 0m), (r.Lines[1].QtyReserved, r.Lines[1].QtyBackordered));
        Assert.True(r.HasBackorder);
        Assert.Equal(6m, await ReservedAsync(x, p, x.Pick1));
        var back = await x.Svc.ListAsync(null, null, null, true, null, null, 0, 50, default);
        Assert.Single(back.Items);
        Assert.True(back.Items[0].HasBackorder);
    }

    [Fact]
    public async Task Available_to_sell_excludes_quarantine_crossdock_rental_inactive_bins_other_warehouses_and_what_is_already_reserved()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        var quarantine = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "QZ", ZoneTypes.Quarantine), "Q-01");
        var crossdock = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "XZ", ZoneTypes.CrossDock), "X-01");
        var rental = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "RZ", ZoneTypes.Rental), "R-01");
        var inactive = await x.F.AddBinAsync(await x.F.AddZoneAsync(x.W, "IZ", ZoneTypes.Picking, isActive: false), "I-01");
        var otherWh = await x.F.AddWarehouseAsync("W2");
        var otherBin = await x.F.AddBinAsync(await x.F.AddZoneAsync(otherWh, "OZ", ZoneTypes.Picking), "O-01");
        foreach (var b in new[] { quarantine, crossdock, rental, inactive }) await StockAsync(x, p, b, 50m);
        await StockAsync(x, p, otherBin, 50m, w: otherWh);
        await StockAsync(x, p, x.Pick1, 10m);
        // ya hay 3 reservadas por otro documento
        var bal = await x.F.Db.StockBalances.AsTracking().SingleAsync(b => b.ProductId == p.ProductId && b.WarehouseBinId == x.Pick1.WarehouseBinId);
        bal.QtyReserved = 3m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var o = await DraftAsync(x, L(p, 100m));
        var check = await x.Svc.CheckAsync(o.PublicId, default);
        Assert.Equal(7m, check.Lines[0].Available);   // solo lo vendible del almacén de la orden: 10 − 3
        Assert.Equal(93m, check.Lines[0].Shortage);
    }

    [Fact]
    public async Task A_line_with_a_lot_only_takes_that_lot()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await x.F.AddProductAsync("LOTP", TrackingTypes.Lot);
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
        row.SalePrice = 5m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var lotA = await x.F.AddLotAsync(p, "A", new DateOnly(2027, 1, 1));
        var lotB = await x.F.AddLotAsync(p, "B", new DateOnly(2026, 12, 1));
        await StockAsync(x, p, x.Pick1, 5m, lotA);
        await StockAsync(x, p, x.Pick1, 5m, lotB);

        var o = await DraftAsync(x, L(p, 4m, null, lotA.LotId));
        var r = await x.Svc.RequestAsync(o.PublicId, null, default);
        Assert.Equal(4m, await ReservedAsync(x, p, x.Pick1, lotA));
        Assert.Equal(0m, await ReservedAsync(x, p, x.Pick1, lotB));
        Assert.Equal(lotA.LotId, r.Lines[0].LotId);
        var o2 = await DraftAsync(x, L(p, 3m, null, lotA.LotId));   // del lote A solo queda 1
        Assert.Equal(1m, (await x.Svc.CheckAsync(o2.PublicId, default)).Lines[0].Available);
    }

    [Fact]
    public async Task Two_sellers_cannot_sell_the_same_unit_twice()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        await StockAsync(x, p, x.Pick1, 10m);
        var a = await DraftAsync(x, L(p, 8m));
        var b = await DraftAsync(x, L(p, 5m));

        await x.Svc.RequestAsync(a.PublicId, null, default);
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => x.Svc.RequestAsync(b.PublicId, null, default));
        Assert.Equal("No alcanza el inventario de P1: pedido 5, disponible 2, faltante 3.", ex.Message);
        Assert.Equal(8m, await ReservedAsync(x, p, x.Pick1));   // nunca se reserva más de lo que hay
        var withBack = await x.Svc.RequestAsync(b.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        Assert.Equal((2m, 3m), (withBack.Lines[0].QtyReserved, withBack.Lines[0].QtyBackordered));
        Assert.Equal(10m, await ReservedAsync(x, p, x.Pick1));
    }

    [Fact]
    public async Task Cancelling_a_requested_order_releases_exactly_what_it_reserved_and_clears_the_backorder()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        await StockAsync(x, p, x.Pick1, 4m);
        await StockAsync(x, p, x.Pick2, 3m);
        var other = await DraftAsync(x, L(p, 1m));
        await x.Svc.RequestAsync(other.PublicId, null, default);   // otra orden con 1 reservada: no se toca al cancelar la nuestra
        var o = await DraftAsync(x, L(p, 9m));
        var r = await x.Svc.RequestAsync(o.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        Assert.Equal((6m, 3m), (r.Lines[0].QtyReserved, r.Lines[0].QtyBackordered));
        Assert.Equal(7m, await ReservedAsync(x, p, x.Pick1) + await ReservedAsync(x, p, x.Pick2));

        var cancelled = await x.Svc.CancelAsync(o.PublicId, "Se cayó la venta", default);
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Equal((0m, 0m), (cancelled.Lines[0].QtyReserved, cancelled.Lines[0].QtyBackordered));
        Assert.False(cancelled.HasBackorder);
        Assert.Equal(1m, await ReservedAsync(x, p, x.Pick1) + await ReservedAsync(x, p, x.Pick2));   // solo queda la reserva de la otra orden
        var lineId = r.Lines[0].Id;
        Assert.Empty(await x.F.Db.Set<SalesOrderReservation>().Where(rr => rr.SalesOrderLineId == lineId).ToListAsync());
        Assert.Equal(SalesRules.AlreadyCancelled, await MsgOf(() => x.Svc.CancelAsync(o.PublicId, null, default)));
    }

    [Fact]
    public async Task An_order_with_something_already_shipped_cannot_be_cancelled()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        await StockAsync(x, p, x.Pick1, 10m);
        var o = await DraftAsync(x, L(p, 5m));
        await x.Svc.RequestAsync(o.PublicId, null, default);
        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync();
        line.QtyShipped = 2m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRules.OnlyDraftOrRequestedCancellable, await MsgOf(() => x.Svc.CancelAsync(o.PublicId, null, default)));
        Assert.Equal(5m, await ReservedAsync(x, p, x.Pick1));
    }

    [Fact]
    public async Task Modifying_a_line_adjusts_the_reservation_up_and_down_and_recalculates_the_totals()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1", 10m);
        await StockAsync(x, p, x.Pick1, 10m);
        var o = await DraftAsync(x, L(p, 4m));
        var r = await x.Svc.RequestAsync(o.PublicId, null, default);
        var lineId = r.Lines[0].Id;
        Assert.Equal(4m, await ReservedAsync(x, p, x.Pick1));

        var up = await x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 9m), default);
        Assert.Equal((9m, 9m, 0m), (up.Lines[0].QtyOrdered, up.Lines[0].QtyReserved, up.Lines[0].QtyBackordered));
        Assert.Equal(9m, await ReservedAsync(x, p, x.Pick1));
        Assert.Equal(90m, up.Subtotal);

        // subir más de lo que hay: sin backorder es 409 y no cambia nada; con backorder reserva lo que queda
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 15m), default));
        Assert.Equal("No alcanza el inventario de P1 para subir la cantidad: se agregan 6, disponible 1, faltante 5.", ex.Message);
        Assert.Equal(9m, await ReservedAsync(x, p, x.Pick1));
        var back = await x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 15m, AllowBackorder: true), default);
        Assert.Equal((15m, 10m, 5m), (back.Lines[0].QtyOrdered, back.Lines[0].QtyReserved, back.Lines[0].QtyBackordered));

        // bajar: primero se quita del backorder y luego de la reserva
        var down1 = await x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 12m), default);
        Assert.Equal((12m, 10m, 2m), (down1.Lines[0].QtyOrdered, down1.Lines[0].QtyReserved, down1.Lines[0].QtyBackordered));
        var down2 = await x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 3m), default);
        Assert.Equal((3m, 3m, 0m), (down2.Lines[0].QtyOrdered, down2.Lines[0].QtyReserved, down2.Lines[0].QtyBackordered));
        Assert.Equal(3m, await ReservedAsync(x, p, x.Pick1));
        Assert.Equal(30m, down2.Subtotal);
        var rows = await x.F.Db.Set<SalesOrderReservation>().Where(rr => rr.SalesOrderLineId == lineId).ToListAsync();
        Assert.Equal(3m, rows.Sum(rr => rr.Quantity));

        Assert.Equal(SalesRules.QuantityRequired, await MsgOf(() => x.Svc.ModifyLineAsync(o.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 0m), default)));
        Assert.Equal("Línea no encontrada.", await MsgOf(() => x.Svc.ModifyLineAsync(o.PublicId, 99999, new SalesOrderLineModifyRequest(Quantity: 1m), default)));
    }

    [Fact]
    public async Task Modifying_is_only_for_requested_orders_open_lines_and_nothing_shipped_and_a_new_price_needs_the_permission()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1", 10m);
        await StockAsync(x, p, x.Pick1, 10m);
        var draft = await DraftAsync(x, L(p, 2m));
        Assert.Equal(SalesRules.OnlyRequestedModifiable, await MsgOf(() => x.Svc.ModifyLineAsync(draft.PublicId, draft.Lines[0].Id, new SalesOrderLineModifyRequest(Quantity: 3m), default)));
        var r = await x.Svc.RequestAsync(draft.PublicId, null, default);
        var lineId = r.Lines[0].Id;

        x.F.SetPermissions(PermissionCatalog.SalesCreate);
        Assert.Equal(SalesRules.PriceNeedsPermission, await MsgOf(() => x.Svc.ModifyLineAsync(draft.PublicId, lineId, new SalesOrderLineModifyRequest(UnitPrice: 8m), default)));
        x.F.SetPermissions(PermissionCatalog.SalesCreate, PermissionCatalog.SalesPrice);
        var cheaper = await x.Svc.ModifyLineAsync(draft.PublicId, lineId, new SalesOrderLineModifyRequest(UnitPrice: 8m), default);
        Assert.Equal(16m, cheaper.Subtotal);

        var line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync();
        line.QtyShipped = 1m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRules.LineAlreadyShipped, await MsgOf(() => x.Svc.ModifyLineAsync(draft.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 5m), default)));
        line = await x.F.Db.Set<SalesOrderLine>().AsTracking().SingleAsync();
        (line.QtyShipped, line.IsClosed) = (0m, true);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRules.LineClosed, await MsgOf(() => x.Svc.ModifyLineAsync(draft.PublicId, lineId, new SalesOrderLineModifyRequest(Quantity: 5m), default)));
    }

    [Fact]
    public async Task Credit_over_the_limit_warns_with_422_and_is_authorized_with_the_permission_leaving_a_trace()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var client = await x.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == x.C.ClientId);
        client.CreditLimit = 100m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        x.F.Get<FakeBalance>().Pending = 60m;
        var p = await ProductAsync(x, "P1", 10m);
        await StockAsync(x, p, x.Pick1, 100m);
        var o = await DraftAsync(x, L(p, 5m));   // 50 + 60 en curso > 100

        var check = await x.Svc.CheckAsync(o.PublicId, default);
        Assert.True(check.Credit.Exceeded);
        Assert.Equal((100m, 60m, 50m), (check.Credit.Limit, check.Credit.Exposure, check.Credit.NewAmount));
        var ex = await Assert.ThrowsAsync<CreditExceededException>(() => x.Svc.RequestAsync(o.PublicId, null, default));
        Assert.Equal("El cliente excede su límite de crédito: límite 100.00, en curso 60.00, esta orden 50.00.", ex.Message);
        Assert.Equal(0m, await ReservedAsync(x, p, x.Pick1));

        x.F.SetPermissions(PermissionCatalog.SalesCreate);
        await Assert.ThrowsAsync<ForbiddenException>(() => x.Svc.RequestAsync(o.PublicId, new SalesOrderRequestRequest(OverrideCredit: true), default));
        x.F.SetPermissions(PermissionCatalog.SalesCreate, PermissionCatalog.OrdersCreditOverride);
        var r = await x.Svc.RequestAsync(o.PublicId, new SalesOrderRequestRequest(OverrideCredit: true, Comment: "Cliente de confianza"), default);
        Assert.Equal("REQUESTED", r.Status);
        var id = (await x.F.Db.Set<SalesOrder>().SingleAsync(s => s.PublicId == o.PublicId)).SalesOrderId;
        var history = await x.F.Db.Set<EntityStatusHistory>().Where(h => h.EntityId == id).ToListAsync();
        Assert.Contains(history, h => h.Comment != null && h.Comment.StartsWith("Crédito excedido autorizado por ") && h.Comment.Contains("Cliente de confianza"));
    }

    [Fact]
    public async Task Other_open_sales_orders_of_the_client_count_as_credit_in_course()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var client = await x.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == x.C.ClientId);
        client.CreditLimit = 100m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var p = await ProductAsync(x, "P1", 10m);
        await StockAsync(x, p, x.Pick1, 100m);
        var first = await DraftAsync(x, L(p, 7m));
        await x.Svc.RequestAsync(first.PublicId, null, default);
        var second = await DraftAsync(x, L(p, 5m));   // 70 solicitados + 50 > 100
        Assert.Equal(70m, (await x.Svc.CheckAsync(second.PublicId, default)).Credit.Exposure);
        await Assert.ThrowsAsync<CreditExceededException>(() => x.Svc.RequestAsync(second.PublicId, null, default));
    }

    [Fact]
    public async Task Serial_tracked_products_are_not_sold_with_a_sales_order_yet()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var serial = await x.F.AddProductAsync("SER", TrackingTypes.Serial);
        var row = await x.F.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == serial.ProductId);
        row.SalePrice = 9m;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(SalesRules.SerialNotSupported, await MsgOf(() => DraftAsync(x, L(serial, 1m))));
    }

    [Fact]
    public async Task Requesting_changes_nothing_for_another_company_and_the_stock_of_another_company_is_not_sellable()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var p = await ProductAsync(x, "P1");
        await StockAsync(x, p, x.Pick1, 5m);
        var o = await DraftAsync(x, L(p, 5m));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
        {
            x.F.SetModules(ModuleKeys.SalesOrders);
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.RequestAsync(o.PublicId, null, default));
            await Assert.ThrowsAsync<NotFoundException>(() => x.Svc.CheckAsync(o.PublicId, default));
        }
        Assert.Equal("DRAFT", (await x.Svc.GetAsync(o.PublicId, default)).Status);
    }
}
