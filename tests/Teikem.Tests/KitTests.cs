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

// Lote 68 (P4-T): kits — definición (componentes, reglas), disponibilidad «se pueden armar N» y que el kit no tenga existencia propia.
public sealed class KitTests
{
    [Theory]
    [InlineData(10, 2, 7, 3, 2)]    // min(5, 2.33) -> 2
    [InlineData(10, 2, 0, 3, 0)]
    [InlineData(3, 4, 9, 1, 0)]
    [InlineData(8, 2, 4, 1, 4)]
    public void CanBuild_is_the_smallest_whole_quotient(double a1, double q1, double a2, double q2, int expected)
        => Assert.Equal(expected, KitRules.CanBuild(new[] { ((decimal)a1, (decimal)q1), ((decimal)a2, (decimal)q2) }));

    [Fact]
    public void CanBuild_is_zero_without_components_or_with_a_nonpositive_quantity()
    {
        Assert.Equal(0, KitRules.CanBuild(Array.Empty<(decimal, decimal)>()));
        Assert.Equal(0, KitRules.CanBuild(new[] { (5m, 0m) }));
        Assert.Equal(0, KitRules.CanBuild(new[] { (-3m, 1m) }));
    }

    private sealed record World(WmsFixture F, Warehouse W, WarehouseBin Bin, KitService Kits, Product Kit, Product A, Product B);

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<TaxService>();
            s.AddSingleton<FakeBalance>();
            s.AddSingleton<Teikem.Infrastructure.Orders.IClientBalanceProvider>(sp => sp.GetRequiredService<FakeBalance>());
            s.AddSingleton<SalesOrderService>();
            s.AddSingleton<KitService>();
        });
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PZ", ZoneTypes.Picking), "P-01");
        var kit = await f.AddProductAsync("KIT-1");
        var a = await f.AddProductAsync("A");
        var b = await f.AddProductAsync("B");
        return new World(f, w, bin, f.Get<KitService>(), kit, a, b);
    }

    private static async Task StockAsync(World x, Product p, decimal qty, decimal reserved = 0m)
    {
        x.F.Db.StockBalances.Add(new StockBalance { TenantId = WmsFixture.TenantId, ProductId = p.ProductId, WarehouseId = x.W.WarehouseId, WarehouseBinId = x.Bin.WarehouseBinId, QtyOnHand = qty, QtyReserved = reserved, UpdatedAtUtc = DateTime.UtcNow });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static KitSetRequest Req(params (Product P, decimal Q)[] c) => new(true, null, c.Select(x => new KitComponentRequest(x.P.PublicId, x.Q)).ToList());

    private static async Task<string> MsgOf(Func<Task> act) => (await Assert.ThrowsAnyAsync<TeikemException>(act)).Message;

    [Fact]
    public async Task A_product_becomes_a_kit_with_its_components_and_says_how_many_can_be_built_per_warehouse()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await StockAsync(x, x.A, 10m);
        await StockAsync(x, x.B, 7m, reserved: 1m);   // disponible 6

        var before = await x.Kits.GetAsync(x.Kit.PublicId, default);
        Assert.False(before.IsKit);

        var kit = await x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 2m), (x.B, 3m)) with { Adjustable = true }, default);
        Assert.True(kit.IsKit);
        Assert.True(kit.Adjustable);
        Assert.Equal(new[] { ("A", 2m), ("B", 3m) }, kit.Components.Select(c => (c.Sku, c.Quantity)).ToArray());
        var av = Assert.Single(kit.Availability);
        Assert.Equal(("W1", 2), (av.WarehouseCode, av.CanBuild));   // min(10/2=5, 6/3=2)
    }

    [Fact]
    public async Task Changing_components_replaces_them_and_the_kit_can_go_back_to_a_normal_product()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 2m), (x.B, 3m)), default);
        var again = await x.Kits.SetAsync(x.Kit.PublicId, Req((x.B, 5m)), default);
        Assert.Equal(new[] { ("B", 5m) }, again.Components.Select(c => (c.Sku, c.Quantity)).ToArray());
        Assert.Equal(1, await x.F.Db.Set<ProductKitComponent>().CountAsync());

        var normal = await x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(false), default);
        Assert.False(normal.IsKit);
        Assert.Empty(normal.Components);
        Assert.Equal(0, await x.F.Db.Set<ProductKitComponent>().CountAsync());
        Assert.Equal(KitRules.NotAKit("KIT-1"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(false), default)));
    }

    [Fact]
    public async Task Invalid_component_lists_are_rejected_with_the_exact_message()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        var other = await x.F.AddProductAsync("K2");
        await x.Kits.SetAsync(other.PublicId, Req((x.A, 1m)), default);   // K2 ya es kit
        var inactive = await x.F.AddProductAsync("OLD", isActive: false);
        var lotted = await x.F.AddProductAsync("LOT-1", TrackingTypes.Lot);
        var client = await x.F.AddClientAsync("C1");
        var owned = await x.F.AddProductAsync("OWNED", ownerClientId: client.ClientId);

        Assert.Equal(KitRules.NeedComponents, await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, null, null), default)));
        Assert.Equal(KitRules.NeedComponents, await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, null, Array.Empty<KitComponentRequest>()), default)));
        Assert.Equal(KitRules.ComponentQuantity, await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 0m)), default)));
        Assert.Equal(KitRules.DuplicateComponent("A"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 1m), (x.A, 2m)), default)));
        Assert.Equal(KitRules.SelfComponent("KIT-1"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((x.Kit, 1m)), default)));
        Assert.Equal(KitRules.ComponentIsKit("K2"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((other, 1m)), default)));
        Assert.Equal(KitRules.ComponentInactive("OLD"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((inactive, 1m)), default)));
        Assert.Equal(KitRules.ComponentOtherOwner("OWNED"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, Req((owned, 1m)), default)));
        Assert.Equal(KitRules.TrackingNotAllowed, await MsgOf(() => x.Kits.SetAsync(lotted.PublicId, Req((x.A, 1m)), default)));
        Assert.Equal("Componente no encontrado.", await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(true, null, new[] { new KitComponentRequest(Guid.NewGuid(), 1m) }), default)));
        Assert.False((await x.Kits.GetAsync(x.Kit.PublicId, default)).IsKit);   // nada quedó a medias
    }

    [Fact]
    public async Task A_product_with_stock_or_that_is_a_component_cannot_become_a_kit_and_a_used_kit_cannot_go_back()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await StockAsync(x, x.A, 5m);
        Assert.Equal(KitRules.HasStock("A"), await MsgOf(() => x.Kits.SetAsync(x.A.PublicId, Req((x.B, 1m)), default)));

        await x.Kits.SetAsync(x.Kit.PublicId, Req((x.B, 1m)), default);
        Assert.Equal(KitRules.IsComponent("B"), await MsgOf(() => x.Kits.SetAsync(x.B.PublicId, Req((x.A, 1m)), default)));

        // un pedido que ya usó el kit: no vuelve a producto normal
        var client = await x.F.AddClientAsync("C1");
        var order = new SalesOrder
        {
            TenantId = WmsFixture.TenantId, Number = "OV-1", WarehouseId = x.W.WarehouseId, ClientId = client.ClientId, RequestedDate = new DateOnly(2026, 10, 16),
            StatusCodeId = x.F.StatusId(SalesCodes.StatusDomain, SalesOrderStatuses.Draft), PublicId = Guid.NewGuid(),
        };
        order.Lines.Add(new SalesOrderLine { TenantId = WmsFixture.TenantId, LineNo = 1, ProductId = x.Kit.ProductId, QtyOrdered = 1m });
        x.F.Db.Set<SalesOrder>().Add(order);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        Assert.Equal(KitRules.UsedInSales("KIT-1"), await MsgOf(() => x.Kits.SetAsync(x.Kit.PublicId, new KitSetRequest(false), default)));
    }

    [Fact]
    public async Task A_kit_has_no_stock_of_its_own_and_the_ledger_refuses_to_receive_it()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 1m)), default);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, x.Kit.ProductId, 3m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId)));
        Assert.Equal(KitRules.KitHasNoStock("KIT-1"), ex.Message);
        // un producto normal sigue recibiendo
        await x.F.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, x.A.ProductId, 3m, ToWarehouseId: x.W.WarehouseId, ToBinId: x.Bin.WarehouseBinId));
    }

    [Fact]
    public async Task Kits_are_isolated_by_company()
    {
        var x = await WorldAsync();
        await using var _ = x.F;
        await x.Kits.SetAsync(x.Kit.PublicId, Req((x.A, 1m)), default);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            await Assert.ThrowsAsync<NotFoundException>(() => x.Kits.GetAsync(x.Kit.PublicId, default));
    }
}
