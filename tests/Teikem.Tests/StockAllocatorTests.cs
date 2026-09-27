using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P0 (D14) — asignación FEFO pura sobre el disponible (en mano − reservado).</summary>
public class StockAllocatorTests
{
    private static StockCandidate C(int bin, string code, string zone, int? lot, DateOnly? expiry, decimal available)
        => new(bin, code, zone, lot, expiry, available);

    [Fact]
    public void Fefo_first_then_zone_order()
    {
        var candidates = new[]
        {
            C(1, "R-01", ZoneTypes.Reserve, 10, new DateOnly(2027, 1, 1), 5m),
            C(2, "P-01", ZoneTypes.Picking, 11, new DateOnly(2027, 6, 1), 5m),
            C(3, "P-02", ZoneTypes.Picking, null, null, 5m),
            C(4, "S-01", ZoneTypes.Staging, 10, new DateOnly(2027, 1, 1), 5m),
        };
        var r = StockAllocator.Allocate(7m, null, null, candidates);
        Assert.True(r.IsComplete);
        Assert.Equal(new[] { (1, 5m), (4, 2m) }, r.Allocations.Select(a => (a.BinId, a.Quantity)));
    }

    [Fact]
    public void Excludes_quarantine_crossdock_and_non_positive_available()
    {
        var candidates = new[]
        {
            C(1, "Q-01", ZoneTypes.Quarantine, null, null, 5m),
            C(2, "X-01", ZoneTypes.CrossDock, null, null, 5m),
            C(3, "P-01", ZoneTypes.Picking, null, null, 0m),
            C(4, "P-02", ZoneTypes.Picking, null, null, 2m),
        };
        var r = StockAllocator.Allocate(5m, null, null, candidates);
        Assert.False(r.IsComplete);
        Assert.Equal(3m, r.Shortfall);
        Assert.Equal(new[] { 4 }, r.Allocations.Select(a => a.BinId));
    }

    [Fact]
    public void Split_5_plus_2_and_restrictions()
    {
        var candidates = new[] { C(1, "P-01", ZoneTypes.Picking, null, null, 5m), C(2, "P-02", ZoneTypes.Picking, null, null, 5m) };
        var r = StockAllocator.Allocate(7m, null, null, candidates);
        Assert.Equal(new[] { 5m, 2m }, r.Allocations.Select(a => a.Quantity));
        Assert.Equal(new[] { 2 }, StockAllocator.Allocate(1m, null, 2, candidates).Allocations.Select(a => a.BinId));
        Assert.Empty(StockAllocator.Allocate(1m, 99, null, candidates).Allocations);
    }

    [Fact]
    public void Available_already_discounts_reserved_and_is_deterministic()
    {
        // El llamador pasa disponible = en mano − reservado: 5 en mano con 3 reservados → 2.
        var candidates = new[] { C(2, "P-02", ZoneTypes.Picking, null, null, InventoryRules.Available(5m, 3m)), C(1, "P-01", ZoneTypes.Picking, null, null, 1m) };
        var a = StockAllocator.Allocate(3m, null, null, candidates);
        var b = StockAllocator.Allocate(3m, null, null, Enumerable.Reverse(candidates));
        Assert.Equal(a.Allocations, b.Allocations);
        Assert.Equal(new[] { (1, 1m), (2, 2m) }, a.Allocations.Select(x => (x.BinId, x.Quantity)));
    }
}
