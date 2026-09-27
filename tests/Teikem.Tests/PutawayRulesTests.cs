using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 / P0 (D24) — putaway dirigido puro: rotación por salidas de 30 días y orden de razones.</summary>
public class PutawayRulesTests
{
    private static PutawayCandidate C(int id, string code, string zoneType, bool empty = true, decimal productQty = 0m, decimal lotQty = 0m,
        bool preferred = false, bool active = true, decimal? maxWeight = null, decimal currentWeight = 0m)
        => new(id, code, 1, zoneType[..3], zoneType, active, maxWeight, currentWeight, empty, productQty, lotQty, preferred);

    [Fact]
    public void Classify_by_30_day_issues()
    {
        Assert.Equal(RotationClasses.Slow, PutawayRules.Classify(0m, 0m));
        Assert.Equal(RotationClasses.Fast, PutawayRules.Classify(10m, 10m));
        Assert.Equal(RotationClasses.Fast, PutawayRules.Classify(12m, 5m));
        Assert.Equal(RotationClasses.Slow, PutawayRules.Classify(4m, 5m));
    }

    [Fact]
    public void Fast_puts_empty_picking_before_reserve()
    {
        var candidates = new[] { C(1, "R-01", ZoneTypes.Reserve), C(2, "P-01", ZoneTypes.Picking), C(3, "R-02", ZoneTypes.Reserve, empty: false) };
        var fast = PutawayRules.Rank(candidates, null, 1m, null, RotationClasses.Fast, null, 5);
        Assert.Equal(new[] { PutawayRules.PickingFast, PutawayRules.ReserveEmpty, PutawayRules.Reserve }, fast.Select(r => r.ReasonCode));
        Assert.Equal("Picking por alta rotación", fast[0].Reason);
        Assert.All(fast, r => Assert.Equal(RotationClasses.Fast, r.RotationClass));

        var slow = PutawayRules.Rank(candidates, null, 1m, null, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { PutawayRules.ReserveEmpty, PutawayRules.Reserve }, slow.Select(r => r.ReasonCode));
    }

    [Fact]
    public void Preferred_then_consolidate_lot_then_product()
    {
        var candidates = new[]
        {
            C(1, "R-01", ZoneTypes.Reserve),
            C(2, "R-02", ZoneTypes.Reserve, empty: false, productQty: 3m),
            C(3, "R-03", ZoneTypes.Reserve, empty: false, productQty: 2m, lotQty: 2m),
            C(4, "P-09", ZoneTypes.Picking, preferred: true, maxWeight: 1m, currentWeight: 0m),
        };
        var r = PutawayRules.Rank(candidates, 7, 1m, null, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { PutawayRules.Preferred, PutawayRules.ConsolidateLot, PutawayRules.Consolidate, PutawayRules.ReserveEmpty }, r.Select(x => x.ReasonCode));

        // La preferida sin capacidad para el peso se salta (sigue el resto del orden).
        var heavy = PutawayRules.Rank(candidates, 7, 1m, 5m, RotationClasses.Slow, null, 5);
        Assert.Equal(new[] { PutawayRules.ConsolidateLot, PutawayRules.Consolidate, PutawayRules.ReserveEmpty }, heavy.Select(x => x.ReasonCode));
    }

    [Fact]
    public void Exclusions_capacity_refrigerated_and_determinism()
    {
        var candidates = new[]
        {
            C(1, "Q-01", ZoneTypes.Quarantine), C(2, "S-01", ZoneTypes.Staging), C(3, "X-01", ZoneTypes.CrossDock),
            C(4, "R-01", ZoneTypes.Reserve, active: false), C(5, "R-02", ZoneTypes.Reserve), C(6, "F-01", ZoneTypes.Refrigerated),
            C(7, "F-02", ZoneTypes.Refrigerated, empty: false, productQty: 1m), C(8, "R-03", ZoneTypes.Reserve, maxWeight: 10m, currentWeight: 8m),
            C(9, "R-00", ZoneTypes.Reserve, maxWeight: null),
        };
        var r = PutawayRules.Rank(candidates, null, 1m, 5m, RotationClasses.Slow, excludeBinId: 5, take: 10);
        Assert.Equal(new[] { 7, 9 }, r.Select(x => x.BinId));   // F-02 consolida; R-03 sin capacidad; R-02 es el origen
        var again = PutawayRules.Rank(Enumerable.Reverse(candidates), null, 1m, 5m, RotationClasses.Slow, 5, 10);
        Assert.Equal(r, again);
        // Peso unitario NULL: la capacidad no limita.
        Assert.Contains(PutawayRules.Rank(candidates, null, 1m, null, RotationClasses.Slow, 5, 10), x => x.BinId == 8);
    }
}
