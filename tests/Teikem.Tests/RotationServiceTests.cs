using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Rotación FEFO / FIFO configurable (Lote 34), por los servicios: la recolección y el orden de salida usan el método de la compañía
/// (Tenant.RotationMethod) y, si el producto tiene excepción (Product.RotationMethod), la del producto.
/// </summary>
public class RotationServiceTests
{
    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton(sp => new PickBatchService(
        sp.GetRequiredService<Teikem.Infrastructure.Persistence.TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(),
        sp.GetRequiredService<ILookupCache>(), sp.GetRequiredService<StatusService>(), sp.GetRequiredService<INumberSequenceService>(),
        sp.GetRequiredService<PermissionService>(), sp.GetRequiredService<InventoryLedger>(), null!)));

    private sealed record Scene(WmsFixture F, Warehouse W, Product P, InventoryLot Late, InventoryLot Early);

    /// <summary>Dos lotes del mismo producto: «TARDE» venció más lejos pero ENTRÓ primero (hace 30 días); «TEMPRANO» vence antes y entró ayer.</summary>
    private static async Task<Scene> SeedAsync()
    {
        var f = await FixtureAsync();
        var w = await f.AddWarehouseAsync("W1");
        var bin = await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        var p = await f.AddProductAsync("PL", tracking: TrackingTypes.Lot);
        var today = TenantClock.Default.Today;
        var late = await f.AddLotAsync(p, "L-TARDE", today.AddDays(60));
        var early = await f.AddLotAsync(p, "L-TEMPRANO", today.AddDays(10));
        InventoryPosting In(InventoryLot lot) => new(InventoryTxnTypes.Receipt, p.ProductId, 5m, LotId: lot.LotId, ToWarehouseId: w.WarehouseId, ToBinId: bin.WarehouseBinId);
        await f.PostAsync(In(late), In(early));
        foreach (var b in await f.Db.StockBalances.Where(b => b.ProductId == p.ProductId).ToListAsync())
            b.FirstReceivedAtUtc = b.LotId == late.LotId ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        return new Scene(f, w, p, late, early);
    }

    private static async Task SetMethodsAsync(Scene s, string? tenant, string? product)
    {
        if (tenant is not null) (await s.F.Db.Tenants.SingleAsync(t => t.TenantId == WmsFixture.TenantId)).RotationMethod = tenant;
        (await s.F.Db.Products.SingleAsync(x => x.ProductId == s.P.ProductId)).RotationMethod = product;
        await s.F.Db.SaveChangesAsync();
    }

    private static async Task<int?> CollectedLotAsync(Scene s)
    {
        var dto = await s.F.Get<PickBatchService>().CollectAsync(new PickBatchCreateRequest(s.W.PublicId, new[] { new PickBatchLineRequest(s.P.PublicId, 1m) }), default);
        return dto.Lines.Single().LotId;
    }

    [Fact]
    public async Task By_default_a_company_collects_fefo_the_lot_that_expires_first()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        Assert.Equal(s.Early.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task A_fifo_company_collects_the_lot_that_entered_first_even_if_it_expires_later()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetMethodsAsync(s, tenant: "FIFO", product: null);
        Assert.Equal(s.Late.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task A_product_exception_beats_the_company_method_in_both_directions()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        await SetMethodsAsync(s, tenant: "FIFO", product: "FEFO");      // la compañía es FIFO pero este producto es FEFO
        Assert.Equal(s.Early.LotId, await CollectedLotAsync(s));

        await SetMethodsAsync(s, tenant: "FEFO", product: "FIFO");      // y al revés
        Assert.Equal(s.Late.LotId, await CollectedLotAsync(s));
    }

    [Fact]
    public async Task Exit_options_rank_follows_the_same_method()
    {
        var s = await SeedAsync();
        await using var _ = s.F;
        var service = new StockExitService(s.F.Db, s.F.Get<ILookupCache>());

        var fefo = await service.ListAsync(s.W.PublicId, new[] { s.P.PublicId }, 0, 50, default);
        Assert.Equal(new int?[] { s.Early.LotId, s.Late.LotId }, fefo.Items.OrderBy(i => i.Rank).Select(i => i.LotId).ToArray());

        await SetMethodsAsync(s, tenant: "FIFO", product: null);
        var fifo = await service.ListAsync(s.W.PublicId, new[] { s.P.PublicId }, 0, 50, default);
        Assert.Equal(new int?[] { s.Late.LotId, s.Early.LotId }, fifo.Items.OrderBy(i => i.Rank).Select(i => i.LotId).ToArray());
    }
}
