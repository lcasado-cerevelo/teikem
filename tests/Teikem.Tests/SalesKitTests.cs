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

// Lote 69 (P4-T): órdenes de venta con kits — la foto de componentes por línea, reservar/liberar por componente en kits enteros, despachar y deshacer en kits.
public sealed class SalesKitTests
{
    private sealed class World(WmsFixture f, Warehouse w, Teikem.Domain.Clients.Client c, SalesOrderService svc, PickBatchService batches, KitService kits, WarehouseBin bin, Product kit, Product a, Product b)
    {
        public WmsFixture F { get; } = f;
        public Warehouse W { get; } = w;
        public Teikem.Domain.Clients.Client C { get; } = c;
        public SalesOrderService Svc { get; } = svc;
        public PickBatchService Batches { get; } = batches;
        public KitService Kits { get; } = kits;
        public WarehouseBin Bin { get; } = bin;
        public Product Kit { get; } = kit;
        public Product A { get; } = a;
        public Product B { get; } = b;
    }

    /// <summary>Kit = 2 × A + 1 × B, precio 50. Existencia: A y B en una posición de recolección.</summary>
    private static async Task<World> NewWorldAsync(decimal stockA = 10m, decimal stockB = 5m)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<KitService>();
            s.AddSingleton<SyncService>();
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
        var kit = await f.AddProductAsync("KIT-1");
        var a = await f.AddProductAsync("A", purchaseCost: 3m);
        var b = await f.AddProductAsync("B", purchaseCost: 4m);
        foreach (var p in new[] { kit, a, b })
        {
            var row = await f.Db.Set<Product>().AsTracking().SingleAsync(r => r.ProductId == p.ProductId);
            row.SalePrice = p == kit ? 50m : 10m;
        }
        foreach (var (p, qty) in new[] { (a, stockA), (b, stockB) })
            f.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = w.WarehouseId, WarehouseBinId = bin.WarehouseBinId, QtyOnHand = qty, UpdatedAtUtc = DateTime.UtcNow });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var kits = f.Get<KitService>();
        await kits.SetAsync(kit.PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(a.PublicId, 2m), new KitComponentRequest(b.PublicId, 1m) }), default);
        return new World(f, w, c, f.Get<SalesOrderService>(), f.Get<PickBatchService>(), kits, bin, kit, a, b);
    }

    private static Task<SalesOrderDto> DraftAsync(World x, decimal kits, IReadOnlyList<KitComponentRequest>? components = null)
        => x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId, Lines: new[] { new SalesOrderLineRequest(x.Kit.PublicId, kits, Components: components) }), default);

    private static async Task<SalesOrderDto> RequestedAsync(World x, decimal kits, bool allowBackorder = false)
    {
        var d = await DraftAsync(x, kits);
        return await x.Svc.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: allowBackorder), default);
    }

    private static async Task<(decimal OnHand, decimal Reserved)> BalanceAsync(World x, Product p)
    {
        var b = await x.F.BalanceAsync(p.ProductId, x.Bin.WarehouseBinId);
        return (b?.QtyOnHand ?? 0m, b?.QtyReserved ?? 0m);
    }

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task A_draft_keeps_a_snapshot_of_the_components_and_shows_them()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var d = await DraftAsync(x, 3m);
        var line = Assert.Single(d.Lines);
        Assert.True(line.IsKit);
        Assert.Equal(new[] { ("A", 2m, 6m), ("B", 1m, 3m) }, line.Components!.Select(c => (c.Sku, c.QtyPerKit, c.Needed)).ToArray());
        Assert.Equal(150m, line.LineSubtotal);   // el precio es el del kit

        // un cambio posterior del catálogo no altera la orden ya guardada
        await x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(x.A.PublicId, 5m) }), default);
        var again = await x.Svc.GetAsync(d.PublicId, default);
        Assert.Equal(new[] { ("A", 2m), ("B", 1m) }, again.Lines[0].Components!.Select(c => (c.Sku, c.QtyPerKit)).ToArray());
    }

    [Fact]
    public async Task Requesting_reserves_every_component_for_whole_kits_and_cancelling_frees_exactly_that()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 3m);
        Assert.Equal((3m, 0m), (o.Lines[0].QtyReserved, o.Lines[0].QtyBackordered));
        Assert.Equal((10m, 6m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 3m), await BalanceAsync(x, x.B));
        Assert.Null(await x.F.BalanceAsync(x.Kit.ProductId, x.Bin.WarehouseBinId));   // el kit no tiene saldo propio
        Assert.Equal(new[] { 6m, 3m }, o.Lines[0].Components!.Select(c => c.Reserved).ToArray());

        await x.Svc.CancelAsync(o.PublicId, null, default);
        Assert.Equal((10m, 0m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 0m), await BalanceAsync(x, x.B));
    }

    [Fact]
    public async Task A_shortage_is_counted_in_kits_and_backorder_reserves_only_whole_kits()
    {
        var x = await NewWorldAsync(stockA: 10m, stockB: 3m);   // se pueden armar 3 (B limita)
        await using var _ = x.F;
        var d = await DraftAsync(x, 5m);
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => x.Svc.RequestAsync(d.PublicId, null, default));
        Assert.Contains("KIT-1", ex.Message);
        var check = await x.Svc.CheckAsync(d.PublicId, default);
        Assert.Equal((5m, 3m, 2m), (check.Lines[0].Ordered, check.Lines[0].Available, check.Lines[0].Shortage));

        var o = await x.Svc.RequestAsync(d.PublicId, new SalesOrderRequestRequest(AllowBackorder: true), default);
        Assert.Equal((3m, 2m), (o.Lines[0].QtyReserved, o.Lines[0].QtyBackordered));
        Assert.Equal((10m, 6m), await BalanceAsync(x, x.A));   // 3 kits × 2
        Assert.Equal((3m, 3m), await BalanceAsync(x, x.B));
    }

    [Fact]
    public async Task Entering_stock_lets_the_backorder_be_fulfilled_in_whole_kits()
    {
        var x = await NewWorldAsync(stockA: 10m, stockB: 3m);
        await using var _ = x.F;
        var o = await RequestedAsync(x, 5m, allowBackorder: true);   // reserva 3, backorder 2
        var bal = await x.F.Db.StockBalances.AsTracking().SingleAsync(b => b.ProductId == x.B.ProductId);
        bal.QtyOnHand += 1m;   // entra 1 B: solo se puede armar 1 kit más
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var back = Assert.Single(await x.Svc.BackordersAsync(null, default));
        Assert.Equal((2m, 1m), (back.Pending, back.Fulfillable));
        var after = await x.Svc.FulfillLineAsync(o.PublicId, o.Lines[0].Id, default);
        Assert.Equal((4m, 1m), (after.Lines[0].QtyReserved, after.Lines[0].QtyBackordered));
        Assert.Equal((10m, 8m), await BalanceAsync(x, x.A));
        Assert.Equal((4m, 4m), await BalanceAsync(x, x.B));
    }

    [Fact]
    public async Task Changing_the_quantity_of_a_kit_line_reserves_or_frees_every_component()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 3m);
        var less = await x.Svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(Quantity: 1m), default);
        Assert.Equal(1m, less.Lines[0].QtyReserved);
        Assert.Equal((10m, 2m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 1m), await BalanceAsync(x, x.B));
        var more = await x.Svc.ModifyLineAsync(o.PublicId, o.Lines[0].Id, new SalesOrderLineModifyRequest(Quantity: 4m), default);
        Assert.Equal(4m, more.Lines[0].QtyReserved);
        Assert.Equal((10m, 8m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 4m), await BalanceAsync(x, x.B));
    }

    [Fact]
    public async Task Dispatching_kits_issues_the_components_leaves_the_rest_reserved_and_records_the_kits()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 3m);
        var d = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 2m) }), default);

        Assert.Equal("PARTIAL", d.Order.Status);
        Assert.Equal((2m, 1m), (d.Order.Lines[0].QtyShipped, d.Order.Lines[0].QtyReserved));
        Assert.Equal((6m, 2m), await BalanceAsync(x, x.A));   // salieron 4 y quedan reservadas 2 (1 kit)
        Assert.Equal((3m, 1m), await BalanceAsync(x, x.B));
        var lines = await x.F.Db.Set<PickBatchLine>().AsNoTracking().ToListAsync();
        Assert.Equal(new[] { x.A.ProductId, x.B.ProductId }.OrderBy(i => i), lines.Select(l => l.ProductId).OrderBy(i => i));
        Assert.All(lines, l => Assert.Equal(x.Kit.ProductId, l.KitProductId));
        Assert.Equal(2m, lines.Sum(l => l.KitQuantity ?? 0m));   // los kits se anotan una sola vez
        Assert.Equal(2m, Assert.Single(d.Order.Dispatches).Quantity);   // la cantidad del despacho es en kits

        var rest = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 1m) }), default);
        Assert.Equal("SHIPPED", rest.Order.Status);
        Assert.Equal((4m, 0m), await BalanceAsync(x, x.A));
        Assert.Equal((2m, 0m), await BalanceAsync(x, x.B));
    }

    [Fact]
    public async Task A_kit_is_dispatched_from_what_its_components_have_reserved_whole_and_without_choosing_a_bin()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 3m);
        var lineId = o.Lines[0].Id;
        Assert.Equal(SalesRules.KitNoBinChoice, await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, 1m, BinId: x.Bin.WarehouseBinId) }), default)));
        Assert.Equal(SalesRules.KitQuantityWhole("KIT-1"), await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, 1.5m) }), default)));
        Assert.Equal(SalesRules.DispatchMoreThanReserved("KIT-1", 3m), await MsgOf(() => x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(lineId, 4m) }), default)));
    }

    [Fact]
    public async Task Undoing_a_kit_dispatch_returns_the_components_and_reserves_the_kits_again()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var o = await RequestedAsync(x, 3m);
        var d = await x.Svc.DispatchAsync(o.PublicId, new SalesDispatchRequest(new[] { new SalesDispatchLineRequest(o.Lines[0].Id, 3m) }), default);
        Assert.Equal("SHIPPED", d.Order.Status);

        await x.Batches.DeleteAsync(d.PickBatchPublicId, new PickBatchDeleteRequest("Se devolvió"), default);
        var back = await x.Svc.GetAsync(o.PublicId, default);
        Assert.Equal(("REQUESTED", 0m, 3m), (back.Status, back.Lines[0].QtyShipped, back.Lines[0].QtyReserved));
        Assert.Equal((10m, 6m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 3m), await BalanceAsync(x, x.B));
        Assert.Equal(new[] { 6m, 3m }, back.Lines[0].Components!.Select(c => c.Reserved).ToArray());
    }

    [Fact]
    public async Task An_adjustable_kit_takes_the_pieces_of_the_order_and_a_fixed_one_refuses_them()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        var custom = new[] { new KitComponentRequest(x.A.PublicId, 1m), new KitComponentRequest(x.B.PublicId, 2m) };
        Assert.Equal(SalesRules.KitNotAdjustable("KIT-1"), await MsgOf(() => DraftAsync(x, 1m, custom)));

        await x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, true, new[] { new KitComponentRequest(x.A.PublicId, 2m), new KitComponentRequest(x.B.PublicId, 1m) }), default);
        var d = await DraftAsync(x, 2m, custom);
        Assert.Equal(new[] { ("A", 1m, 2m), ("B", 2m, 4m) }, d.Lines[0].Components!.Select(c => (c.Sku, c.QtyPerKit, c.Needed)).ToArray());
        var o = await x.Svc.RequestAsync(d.PublicId, null, default);
        Assert.Equal((10m, 2m), await BalanceAsync(x, x.A));
        Assert.Equal((5m, 4m), await BalanceAsync(x, x.B));
        Assert.Equal(2m, o.Lines[0].QtyReserved);

        // piezas inválidas
        Assert.Equal(KitRules.ComponentQuantity, await MsgOf(() => DraftAsync(x, 1m, new[] { new KitComponentRequest(x.A.PublicId, 0m) })));
        Assert.Equal(KitRules.DuplicateComponent("A"), await MsgOf(() => DraftAsync(x, 1m, new[] { new KitComponentRequest(x.A.PublicId, 1m), new KitComponentRequest(x.A.PublicId, 1m) })));
        Assert.Equal(KitRules.SelfComponent("KIT-1"), await MsgOf(() => DraftAsync(x, 1m, new[] { new KitComponentRequest(x.Kit.PublicId, 1m) })));
    }

    [Fact]
    public async Task Saving_the_draft_again_keeps_the_snapshot_of_the_pieces_even_if_the_catalog_changed()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, true, new[] { new KitComponentRequest(x.A.PublicId, 2m), new KitComponentRequest(x.B.PublicId, 1m) }), default);
        var d = await DraftAsync(x, 2m, new[] { new KitComponentRequest(x.A.PublicId, 1m), new KitComponentRequest(x.B.PublicId, 3m) });   // piezas ajustadas
        await x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(x.A.PublicId, 9m) }), default);

        var saved = await x.Svc.UpdateAsync(d.PublicId, new SalesOrderRequest(Lines: new[] { new SalesOrderLineRequest(x.Kit.PublicId, 4m) }), default);
        Assert.Equal(4m, saved.Lines[0].QtyOrdered);
        Assert.Equal(new[] { ("A", 1m, 4m), ("B", 3m, 12m) }, saved.Lines[0].Components!.Select(c => (c.Sku, c.QtyPerKit, c.Needed)).ToArray());
    }

    [Fact]
    public async Task Kits_are_sold_in_whole_pieces_and_components_only_go_on_kit_lines()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        Assert.Equal(SalesRules.KitQuantityWhole("KIT-1"), await MsgOf(() => DraftAsync(x, 1.5m)));
        var onNormal = await MsgOf(() => x.Svc.CreateAsync(new SalesOrderRequest(x.W.PublicId, x.C.PublicId,
            Lines: new[] { new SalesOrderLineRequest(x.A.PublicId, 1m, Components: new[] { new KitComponentRequest(x.B.PublicId, 1m) }) }), default));
        Assert.Equal(KitRules.NotAKit("A"), onNormal);
    }

    [Fact]
    public async Task The_device_sync_does_not_send_kit_lines_yet()
    {
        var x = await NewWorldAsync();
        await using var _ = x.F;
        await RequestedAsync(x, 2m);
        var sync = x.F.Get<SyncService>();
        var page = await sync.SalesOrdersAsync(new SyncQuery(WarehousePublicId: x.W.PublicId), default);
        Assert.All(page.Items, o => Assert.Empty(o.Lines));
        Assert.All(page.Items, o => Assert.False(o.IsActive));
    }
}
