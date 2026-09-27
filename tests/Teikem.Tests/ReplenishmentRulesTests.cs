using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P5) — reabasto de la posición de picking (R15, D23): objetivo, límite de reserva, FEFO desde RESERVE y omisiones.</summary>
public class ReplenishmentRulesTests
{
    private const int PickBin = 1;

    private static ReplenishmentProduct Product(decimal onHand, decimal? min = 5m, decimal? max = 8m, bool openTask = false)
        => new(10, "PN", PickBin, min, max, onHand, openTask);

    private static ReplenishmentSource Reserve(int bin, string code, decimal onHand, decimal reserved = 0m, int? lot = null,
        DateOnly? expiry = null, string zone = ZoneTypes.Reserve, bool active = true)
        => new(bin, code, zone, active, lot, expiry, onHand, reserved);

    [Fact]
    public void Target_is_max_or_twice_min()
    {
        Assert.Equal(8m, ReplenishmentRules.Target(5m, 8m));
        Assert.Equal(10m, ReplenishmentRules.Target(5m, null));
        Assert.True(ReplenishmentRules.Applies(1m));
        Assert.False(ReplenishmentRules.Applies(null));
        Assert.False(ReplenishmentRules.Applies(0m));
    }

    [Fact]
    public void Below_min_asks_for_target_minus_on_hand_in_one_task()
    {
        // Smoke: MinPickQty 5 / MaxPickQty 8; picking con 2 y reserva con 6 → una REPLENISH por 6.
        var plan = ReplenishmentRules.Plan(Product(2m), new[] { Reserve(20, "RSV-01", 6m) });
        Assert.Null(plan.SkipReason);
        Assert.Equal(8m, plan.Target);
        Assert.Equal(6m, plan.Needed);
        var move = Assert.Single(plan.Moves);
        Assert.Equal(new ReplenishmentMove(20, null, 6m), move);
    }

    [Fact]
    public void Quantity_is_limited_by_reserve_available_excluding_reserved()
    {
        var plan = ReplenishmentRules.Plan(Product(0m), new[] { Reserve(20, "RSV-01", 5m, reserved: 2m) });
        Assert.Equal(3m, Assert.Single(plan.Moves).Quantity);
    }

    [Fact]
    public void Fefo_from_reserve_only_one_task_per_allocation()
    {
        var sources = new[]
        {
            Reserve(21, "RSV-02", 4m, lot: 2, expiry: new DateOnly(2027, 5, 1)),
            Reserve(22, "RSV-03", 10m, lot: 3),                                   // sin vencimiento: al final
            Reserve(20, "RSV-01", 3m, lot: 1, expiry: new DateOnly(2027, 1, 1)),
            Reserve(30, "PCK-02", 50m, lot: 1, expiry: new DateOnly(2026, 1, 1), zone: ZoneTypes.Picking),
            Reserve(31, "QUA-01", 50m, zone: ZoneTypes.Quarantine),
            Reserve(32, "STG-01", 50m, zone: ZoneTypes.Staging),
            Reserve(23, "RSV-04", 50m, active: false),
        };
        var plan = ReplenishmentRules.Plan(Product(0m, min: 5m, max: 9m), sources);
        Assert.Equal(new[]
        {
            new ReplenishmentMove(20, 1, 3m),
            new ReplenishmentMove(21, 2, 4m),
            new ReplenishmentMove(22, 3, 2m),
        }, plan.Moves);
    }

    [Fact]
    public void Pick_bin_itself_is_never_a_source()
    {
        var plan = ReplenishmentRules.Plan(Product(1m), new[] { Reserve(PickBin, "PCK-01", 20m) });
        Assert.Equal(ReplenishmentRules.SkipNoReserve, plan.SkipReason);
    }

    [Fact]
    public void Skip_reasons()
    {
        Assert.Equal(ReplenishmentRules.SkipNotBelowMin, ReplenishmentRules.Plan(Product(5m), new[] { Reserve(20, "RSV-01", 6m) }).SkipReason);
        Assert.Equal(ReplenishmentRules.SkipNotBelowMin, ReplenishmentRules.Plan(Product(0m, min: null), new[] { Reserve(20, "RSV-01", 6m) }).SkipReason);
        Assert.Equal(ReplenishmentRules.SkipOpenTask, ReplenishmentRules.Plan(Product(2m, openTask: true), new[] { Reserve(20, "RSV-01", 6m) }).SkipReason);
        Assert.Equal(ReplenishmentRules.SkipNoReserve, ReplenishmentRules.Plan(Product(2m), Array.Empty<ReplenishmentSource>()).SkipReason);
        Assert.Equal(ReplenishmentRules.SkipNoReserve, ReplenishmentRules.Plan(Product(2m), new[] { Reserve(20, "RSV-01", 4m, reserved: 4m) }).SkipReason);
        Assert.Equal("OPEN_TASK", ReplenishmentRules.SkipOpenTask);
        Assert.Equal("NO_RESERVE", ReplenishmentRules.SkipNoReserve);
        Assert.Equal("NOT_BELOW_MIN", ReplenishmentRules.SkipNotBelowMin);
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var sources = new[] { Reserve(21, "RSV-B", 2m), Reserve(20, "RSV-A", 2m), Reserve(22, "RSV-A", 2m, lot: 7) };
        var a = ReplenishmentRules.Plan(Product(0m), sources).Moves;
        var b = ReplenishmentRules.Plan(Product(0m), Enumerable.Reverse(sources)).Moves;
        Assert.Equal(a, b);
        Assert.Equal(new[] { 20, 22, 21 }, a.Select(m => m.FromBinId));
    }
    [Fact]
    public void Picking_is_measured_by_available_not_on_hand()
    {
        // Maestro L306: en mano 6 y reservado 3 (disponible 3) con MinPickQty 5 / MaxPickQty 8 → REPLENISH por 5.
        var available = ReplenishmentRules.Available(6m, 3m);
        Assert.Equal(3m, available);
        Assert.True(ReplenishmentRules.IsBelowMin(available, 5m));
        var plan = ReplenishmentRules.Plan(Product(available), new[] { Reserve(20, "RSV-01", 10m) });
        Assert.Null(plan.SkipReason);
        Assert.Equal(5m, plan.Needed);
        Assert.Equal(new ReplenishmentMove(20, null, 5m), Assert.Single(plan.Moves));
        // Con la existencia en mano (6) no habría disparado.
        Assert.Equal(ReplenishmentRules.SkipNotBelowMin, ReplenishmentRules.Plan(Product(6m), new[] { Reserve(20, "RSV-01", 10m) }).SkipReason);
    }
}
