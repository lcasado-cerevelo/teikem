using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Orden de salida (GET /inventory/exit-options): lo disponible por producto en el orden FEFO de la recolección (vence primero, luego zona y
/// código), sin cuarentena ni posiciones inactivas ni disponible cero; filtro por producto, paginación y 404. InMemory con el ledger real.
/// </summary>
public sealed class StockExitTests
{
    private sealed record World(WmsFixture F, Warehouse W, Product Lot, Product Plain) : IAsyncDisposable
    {
        public StockExitService Service => F.Get<StockExitService>();
        public ValueTask DisposeAsync() => F.DisposeAsync();
    }

    private static async Task<World> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<StockExitService>();
            s.AddSingleton<ITenantClock>(TenantClock.Default);
        });
        var w = await f.AddWarehouseAsync("W1");
        var lot = await f.AddProductAsync("SKU-L", TrackingTypes.Lot);
        var plain = await f.AddProductAsync("SKU-P");
        return new World(f, w, lot, plain);
    }

    private static InventoryPosting In(World x, Product p, WarehouseBin b, decimal q, int? lotId = null)
        => new(InventoryTxnTypes.Receipt, p.ProductId, q, ToWarehouseId: x.W.WarehouseId, ToBinId: b.WarehouseBinId, LotId: lotId);

    [Fact]
    public async Task Options_are_ranked_fefo_then_zone_then_bin_and_skip_quarantine_inactive_bins_and_zero_stock()
    {
        await using var x = await WorldAsync();
        var pck = await x.F.AddZoneAsync(x.W, "PCK", ZoneTypes.Picking);
        var res = await x.F.AddZoneAsync(x.W, "RES", ZoneTypes.Reserve);
        var qua = await x.F.AddZoneAsync(x.W, "QUA", ZoneTypes.Quarantine);
        var a = await x.F.AddBinAsync(pck, "A-01");
        var b = await x.F.AddBinAsync(res, "B-01");
        var c = await x.F.AddBinAsync(res, "C-01");
        var q = await x.F.AddBinAsync(qua, "Q-01");
        var dead = await x.F.AddBinAsync(res, "D-01");
        var late = await x.F.AddLotAsync(x.Lot, "L-LATE", new DateOnly(2027, 6, 1));
        var soon = await x.F.AddLotAsync(x.Lot, "L-SOON", new DateOnly(2026, 12, 31));
        var none = await x.F.AddLotAsync(x.Lot, "L-NONE");
        await x.F.PostAsync(In(x, x.Lot, a, 5m, late.LotId), In(x, x.Lot, b, 7m, soon.LotId), In(x, x.Lot, c, 3m, soon.LotId),
            In(x, x.Lot, q, 9m, soon.LotId), In(x, x.Lot, dead, 9m, soon.LotId), In(x, x.Lot, c, 4m, none.LotId));
        dead.IsActive = false;   // se dio de baja con existencia: no se sale de ahí
        x.F.Db.Update(dead);
        await x.F.Db.SaveChangesAsync();

        var page = await x.Service.ListAsync(x.W.PublicId, new[] { x.Lot.PublicId }, 0, 100, default);
        // vence primero (L-SOON: B-01 y C-01 por código), luego L-LATE, y los sin vencimiento al final; sin Q-01 ni D-01
        Assert.Equal(new[] { ("B-01", "L-SOON", 1), ("C-01", "L-SOON", 2), ("A-01", "L-LATE", 3), ("C-01", "L-NONE", 4) },
            page.Items.Select(i => (i.BinCode, i.LotNumber!, i.Rank)).ToArray());
        Assert.Equal(new decimal[] { 7m, 3m, 5m, 4m }, page.Items.Select(i => i.Available).ToArray());
        Assert.Equal(("RES", ZoneTypes.Reserve, new DateOnly(2026, 12, 31)), (page.Items[0].ZoneCode, page.Items[0].ZoneTypeCode, page.Items[0].ExpiryDate!.Value));
        Assert.Equal(4, page.Total);
    }

    [Fact]
    public async Task Zone_order_breaks_the_tie_between_the_same_expiry_and_rank_restarts_for_each_product()
    {
        await using var x = await WorldAsync();
        var pck = await x.F.AddZoneAsync(x.W, "PCK", ZoneTypes.Picking);
        var res = await x.F.AddZoneAsync(x.W, "RES", ZoneTypes.Reserve);
        var r = await x.F.AddBinAsync(res, "A-01");
        var p = await x.F.AddBinAsync(pck, "Z-09");
        await x.F.PostAsync(In(x, x.Plain, r, 8m), In(x, x.Plain, p, 2m), In(x, x.Lot, r, 1m, (await x.F.AddLotAsync(x.Lot, "L1")).LotId));

        var page = await x.Service.ListAsync(x.W.PublicId, null, 0, 100, default);
        var plain = page.Items.Where(i => i.ProductPublicId == x.Plain.PublicId).ToList();
        Assert.Equal(new[] { ("Z-09", 1), ("A-01", 2) }, plain.Select(i => (i.BinCode, i.Rank)).ToArray());   // picking antes que reserva
        Assert.Equal(1, page.Items.Single(i => i.ProductPublicId == x.Lot.PublicId).Rank);
    }

    [Fact]
    public async Task Paging_and_unknown_or_foreign_warehouse()
    {
        await using var x = await WorldAsync();
        var pck = await x.F.AddZoneAsync(x.W, "PCK", ZoneTypes.Picking);
        await x.F.PostAsync(In(x, x.Plain, await x.F.AddBinAsync(pck, "A-01"), 1m), In(x, x.Plain, await x.F.AddBinAsync(pck, "A-02"), 1m),
            In(x, x.Plain, await x.F.AddBinAsync(pck, "A-03"), 1m));
        var second = await x.Service.ListAsync(x.W.PublicId, null, 1, 1, default);
        Assert.Equal((3, 1, 1), (second.Total, second.Skip, second.Take));
        Assert.Equal("A-02", Assert.Single(second.Items).BinCode);

        await Assert.ThrowsAsync<ValidationException>(() => x.Service.ListAsync(null, null, 0, 10, default));
        await Assert.ThrowsAsync<NotFoundException>(() => x.Service.ListAsync(Guid.NewGuid(), null, 0, 10, default));
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            await Assert.ThrowsAsync<NotFoundException>(() => x.Service.ListAsync(x.W.PublicId, null, 0, 10, default));
    }
}
