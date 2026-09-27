using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P0, D24; maestro L301) — PutawaySuggester sobre el ledger real (InMemory, WmsFixture). La rotación usa las salidas
/// ISSUE + CROSSDOCK de los últimos 30 días, que el ledger guarda NEGATIVAS (D3): si se perdiera el signo en
/// PutawaySuggester, todo saldría SLOW y fallarían los dos primeros casos. El tercero cubre la ventana de RotationWindowDays.
/// </summary>
public sealed class PutawaySuggesterTests
{
    private static async Task<(WmsFixture F, Warehouse W, WarehouseBin Staging, Product P)> SetupAsync(string outType)
    {
        var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var stg = await f.AddBinAsync(await f.AddZoneAsync(w, "STG", ZoneTypes.Staging), "STG-01");
        await f.AddBinAsync(await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking), "P-01");
        await f.AddBinAsync(await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve), "R-01");
        var p = await f.AddProductAsync("PN");
        // Entran 10 y salen 6 → existencia 4; salidas30d 6 ≥ 4 → FAST.
        await f.PostAsync(
            new InventoryPosting(InventoryTxnTypes.Receipt, p.ProductId, 10m, ToWarehouseId: w.WarehouseId, ToBinId: stg.WarehouseBinId),
            new InventoryPosting(outType, p.ProductId, 6m, FromWarehouseId: w.WarehouseId, FromBinId: stg.WarehouseBinId));
        return (f, w, stg, p);
    }

    [Theory]
    [InlineData(InventoryTxnTypes.Issue)]
    [InlineData(InventoryTxnTypes.CrossDock)]
    public async Task Recent_outflows_make_the_product_fast_and_suggest_picking_first(string outType)
    {
        var (f, w, stg, p) = await SetupAsync(outType);
        await using var _ = f;
        Assert.Equal(-6m, (await f.TransactionsAsync()).Last().Quantity);   // salida guardada con signo negativo

        var suggestions = await f.Get<PutawaySuggester>().SuggestAsync(w.WarehouseId, p.ProductId, null, 4m, stg.WarehouseBinId, 3, default);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.Equal(RotationClasses.Fast, s.RotationClass));
        Assert.Equal(PutawayRules.PickingFast, suggestions[0].ReasonCode);
        Assert.Equal("P-01", suggestions[0].BinCode);
    }

    [Fact]
    public async Task Outflows_older_than_the_window_do_not_count()
    {
        var (f, w, stg, p) = await SetupAsync(InventoryTxnTypes.Issue);
        await using var _ = f;
        var issue = await f.Db.InventoryTransactions.OrderByDescending(t => t.InventoryTransactionId).FirstAsync();
        issue.CreatedAtUtc = DateTime.UtcNow.AddDays(-(PutawaySuggester.RotationWindowDays + 1));
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var suggestions = await f.Get<PutawaySuggester>().SuggestAsync(w.WarehouseId, p.ProductId, null, 4m, stg.WarehouseBinId, 3, default);

        Assert.All(suggestions, s => Assert.Equal(RotationClasses.Slow, s.RotationClass));
        Assert.Equal(PutawayRules.ReserveEmpty, suggestions[0].ReasonCode);
        Assert.DoesNotContain(suggestions, s => s.ReasonCode == PutawayRules.PickingFast);
    }
}
