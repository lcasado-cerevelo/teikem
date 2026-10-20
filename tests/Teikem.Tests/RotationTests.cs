using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Rotación FEFO / FIFO configurable (2026-10-11, Lote 34): reglas puras. FEFO (por omisión) = vence primero, sale primero; FIFO = la fecha de
/// primera entrada más vieja sale primero, un saldo sin fecha va al final. El método del producto manda sobre el de la compañía, y sin ninguno es FEFO.
/// </summary>
public class RotationTests
{
    private static readonly DateTime Jan = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Feb = new(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    // Cuatro saldos del mismo producto en la misma zona. Orden FEFO esperado: B (vence antes), A, C, D (sin vencimiento, por posición).
    // Orden FIFO esperado: A (enero), C (febrero), B (marzo), D (sin fecha, al final).
    private static List<PickCandidate> Candidates() => new()
    {
        new(BinId: 1, "A01", ZoneTypes.Picking, true, LotId: 1, new DateOnly(2026, 12, 1), 10m, Jan),
        new(BinId: 2, "A02", ZoneTypes.Picking, true, LotId: 2, new DateOnly(2026, 6, 1), 10m, Mar),
        new(BinId: 3, "A03", ZoneTypes.Picking, true, LotId: null, null, 10m, Feb),
        new(BinId: 4, "A04", ZoneTypes.Picking, true, LotId: null, null, 10m, null),
    };

    [Theory]
    [InlineData("FEFO", "FEFO")]
    [InlineData("fifo", "FIFO")]
    [InlineData("  Fifo ", "FIFO")]
    [InlineData("LIFO", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parse_normalizes_or_rejects(string? input, string? expected) => Assert.Equal(expected, RotationRules.Parse(input));

    [Theory]
    [InlineData("FIFO", "FEFO", "FIFO")]     // la excepción del producto manda
    [InlineData("FEFO", "FIFO", "FEFO")]
    [InlineData(null, "FIFO", "FIFO")]       // sin excepción: la de la compañía
    [InlineData(null, null, "FEFO")]          // sin nada: FEFO (lo que había antes del lote)
    [InlineData("basura", "FIFO", "FIFO")]    // un valor inválido guardado se ignora
    public void Resolve_prefers_the_product_then_the_company_then_fefo(string? product, string? tenant, string expected)
        => Assert.Equal(expected, RotationRules.Resolve(product, tenant));

    [Fact]
    public void Pick_default_and_explicit_fefo_keep_the_old_order()
    {
        var expected = new[] { 2, 1, 3, 4 };
        Assert.Equal(expected, PickBatchRules.Eligible(Candidates()).Select(c => c.BinId).ToArray());
        Assert.Equal(expected, PickBatchRules.Eligible(Candidates(), method: RotationMethods.Fefo).Select(c => c.BinId).ToArray());
    }

    [Fact]
    public void Pick_fifo_orders_by_first_entry_with_unknown_dates_last()
        => Assert.Equal(new[] { 1, 3, 2, 4 }, PickBatchRules.Eligible(Candidates(), method: "fifo").Select(c => c.BinId).ToArray());

    [Fact]
    public void Pick_fifo_ties_on_the_entry_date_fall_back_to_expiry_then_bin()
    {
        var same = new List<PickCandidate>
        {
            new(1, "A01", ZoneTypes.Picking, true, 1, new DateOnly(2026, 12, 1), 5m, Jan),
            new(2, "A02", ZoneTypes.Picking, true, 2, new DateOnly(2026, 6, 1), 5m, Jan),
            new(3, "A03", ZoneTypes.Picking, true, null, null, 5m, Jan),
        };
        Assert.Equal(new[] { 2, 1, 3 }, PickBatchRules.Eligible(same, method: RotationMethods.Fifo).Select(c => c.BinId).ToArray());
    }

    [Fact]
    public void Pick_allocate_fifo_takes_the_oldest_first_and_fefo_the_soonest_to_expire()
    {
        var fifo = PickBatchRules.Allocate(12m, Candidates(), method: RotationMethods.Fifo);
        Assert.Equal(new[] { (1, 10m), (3, 2m) }, fifo.Allocations.Select(a => (a.BinId, a.Quantity)).ToArray());

        var fefo = PickBatchRules.Allocate(12m, Candidates(), method: RotationMethods.Fefo);
        Assert.Equal(new[] { (2, 10m), (1, 2m) }, fefo.Allocations.Select(a => (a.BinId, a.Quantity)).ToArray());
    }

    [Fact]
    public void Pick_fifo_still_skips_quarantine_and_empty_balances()
    {
        var list = new List<PickCandidate>
        {
            new(1, "Q01", ZoneTypes.Quarantine, true, null, null, 10m, Jan),   // la más vieja, pero en cuarentena
            new(2, "A02", ZoneTypes.Picking, true, null, null, 0m, Feb),       // sin disponible
            new(3, "A03", ZoneTypes.Picking, true, null, null, 4m, Mar),
        };
        Assert.Equal(new[] { 3 }, PickBatchRules.Eligible(list, method: RotationMethods.Fifo).Select(c => c.BinId).ToArray());
    }

    [Fact]
    public void Stock_allocator_follows_the_same_rotation()
    {
        var list = new List<StockCandidate>
        {
            new(1, "A01", ZoneTypes.Picking, 1, new DateOnly(2026, 12, 1), 10m, Jan),
            new(2, "A02", ZoneTypes.Picking, 2, new DateOnly(2026, 6, 1), 10m, Mar),
        };
        Assert.Equal(1, StockAllocator.Allocate(5m, null, null, list, "FIFO").Allocations[0].BinId);
        Assert.Equal(2, StockAllocator.Allocate(5m, null, null, list, "FEFO").Allocations[0].BinId);
        Assert.Equal(2, StockAllocator.Allocate(5m, null, null, list).Allocations[0].BinId);
    }

    [Fact]
    public void Replenishment_sources_and_plan_follow_the_product_method()
    {
        var sources = new List<ReplenishmentSource>
        {
            new(2, "R02", ZoneTypes.Reserve, true, 2, new DateOnly(2026, 6, 1), 10m, 0m, Mar),
            new(3, "R03", ZoneTypes.Reserve, true, 1, new DateOnly(2026, 12, 1), 10m, 0m, Jan),
        };
        Assert.Equal(new[] { 2, 3 }, ReplenishmentRules.EligibleSources(sources, 1).Select(s => s.BinId).ToArray());
        Assert.Equal(new[] { 3, 2 }, ReplenishmentRules.EligibleSources(sources, 1, "FIFO").Select(s => s.BinId).ToArray());

        var fefoPlan = ReplenishmentRules.Plan(new ReplenishmentProduct(10, "PN", 1, 5m, 8m, 0m, false), sources);
        var fifoPlan = ReplenishmentRules.Plan(new ReplenishmentProduct(10, "PN", 1, 5m, 8m, 0m, false, "FIFO"), sources);
        Assert.Equal(2, fefoPlan.Moves[0].FromBinId);
        Assert.Equal(3, fifoPlan.Moves[0].FromBinId);
    }

    [Theory]
    [InlineData("suggest", "SUGGEST")]
    [InlineData(" Enforce ", "ENFORCE")]
    [InlineData("otro", null)]
    [InlineData(null, null)]
    public void ParseEnforcement_normalizes_or_returns_null(string? input, string? expected)
        => Assert.Equal(expected, RotationRules.ParseEnforcement(input));

    [Theory]
    [InlineData("ENFORCE", true)]
    [InlineData("enforce", true)]
    [InlineData("SUGGEST", false)]
    [InlineData(null, false)]
    [InlineData("basura", false)]
    public void Only_enforce_is_enforced(string? input, bool expected) => Assert.Equal(expected, RotationRules.IsEnforced(input));

    [Fact]
    public void SameAllocation_ignores_order_and_detects_differences()
    {
        var a = new PickAllocationResult(new[] { new PickAllocation(1, 10, 5m), new PickAllocation(2, 11, 2m) }, 0m);
        var sameReordered = new PickAllocationResult(new[] { new PickAllocation(2, 11, 2m), new PickAllocation(1, 10, 5m) }, 0m);
        var otherLot = new PickAllocationResult(new[] { new PickAllocation(1, 12, 5m), new PickAllocation(2, 11, 2m) }, 0m);
        var otherQty = new PickAllocationResult(new[] { new PickAllocation(1, 10, 4m), new PickAllocation(2, 11, 2m) }, 0m);
        Assert.True(PickBatchRules.SameAllocation(a, sameReordered));
        Assert.False(PickBatchRules.SameAllocation(a, otherLot));
        Assert.False(PickBatchRules.SameAllocation(a, otherQty));
    }
}
