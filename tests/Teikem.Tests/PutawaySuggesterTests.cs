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

    [Fact]
    public async Task Unit_capacity_counts_all_products_in_the_bin_and_the_claimed_quantity()
    {
        // Lote 16: R-01 con cupo 10 y 6 unidades de OTRO producto; R-02 sin cupo. 5 unidades no caben en R-01 (6 + 5 > 10).
        await using var f = await WmsFixture.CreateAsync();
        var w = await f.AddWarehouseAsync("W1");
        var rsv = await f.AddZoneAsync(w, "RSV", ZoneTypes.Reserve);
        var r1 = await f.AddBinAsync(rsv, "R-01", maxCapacityQty: 10);
        var r2 = await f.AddBinAsync(rsv, "R-02");
        var q = await f.AddBinAsync(await f.AddZoneAsync(w, "QUA", ZoneTypes.Quarantine), "Q-01");
        var other = await f.AddProductAsync("PO");
        var p = await f.AddProductAsync("PN");
        await f.PostAsync(new InventoryPosting(InventoryTxnTypes.Receipt, other.ProductId, 6m, ToWarehouseId: w.WarehouseId, ToBinId: r1.WarehouseBinId));
        var suggester = f.Get<PutawaySuggester>();

        var five = await suggester.SuggestAsync(w.WarehouseId, p.ProductId, null, 5m, null, 5, default);
        Assert.Equal(new[] { "R-02" }, five.Select(s => s.BinCode));
        var four = await suggester.SuggestAsync(w.WarehouseId, p.ProductId, null, 4m, null, 5, default);
        Assert.Equal(new[] { "R-02", "R-01" }, four.Select(s => s.BinCode));   // R-02 vacía antes que R-01 con espacio
        var r1Suggestion = four.Single(s => s.BinCode == "R-01");
        Assert.Equal((10, 6m, true), (r1Suggestion.MaxCapacityQty!.Value, r1Suggestion.BinQty, r1Suggestion.Fits));

        // Lo reservado por otras líneas cuenta como ocupado; las que no caben, a pedido, al final con Fits = false.
        var claimed = await suggester.SuggestAsync(new PutawaySuggestionOptions(w.WarehouseId, p.ProductId, null, 4m, null, 5,
            new Dictionary<int, decimal> { [r1.WarehouseBinId] = 1m }, IncludeOverCapacity: true), default);
        Assert.Equal(new[] { ("R-02", true), ("R-01", false) }, claimed.Select(s => (s.BinCode, s.Fits)));
        // Devolución: la cuarentena primero (D6).
        var ret = await suggester.SuggestAsync(new PutawaySuggestionOptions(w.WarehouseId, p.ProductId, null, 1m, null, 5, PreferQuarantine: true), default);
        Assert.Equal(q.WarehouseBinId, ret[0].BinId);
        Assert.Equal(PutawayRules.QuarantineReturn, ret[0].ReasonCode);
        _ = r2;
    }
}
