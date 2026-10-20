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

    // ---- 2026-10-20: dos métodos por compañía (con lote: FEFO | FIFO; sin lote ni series: FIFO | por posición) ----

    [Theory]
    [InlineData("position", "POSITION")]
    [InlineData(" Position ", "POSITION")]
    public void Parse_accepts_position(string input, string expected) => Assert.Equal(expected, RotationRules.Parse(input));

    [Theory]
    [InlineData("FEFO", true, true)]
    [InlineData("FIFO", true, true)]
    [InlineData("POSITION", true, false)]
    [InlineData("FEFO", false, false)]    // sin lote no hay vencimiento: FEFO no aplica
    [InlineData("FIFO", false, true)]
    [InlineData("POSITION", false, true)]
    [InlineData(null, false, false)]
    public void A_method_only_applies_to_the_product_types_that_can_use_it(string? method, bool lotTracked, bool applies)
        => Assert.Equal(applies, RotationRules.AppliesTo(method, lotTracked));

    [Fact]
    public void Resolve_uses_the_company_method_for_the_product_type_and_the_factory_default_when_nothing_applies()
    {
        var company = new CompanyRotation("FIFO", "POSITION");
        Assert.Equal("FIFO", RotationRules.Resolve(null, company, lotTracked: true));         // con lote → el de lote
        Assert.Equal("POSITION", RotationRules.Resolve(null, company, lotTracked: false));    // sin lote → el de sin lote
        Assert.Equal("FEFO", RotationRules.Resolve(null, new CompanyRotation(null, null), true));   // fábrica con lote
        Assert.Equal("FIFO", RotationRules.Resolve(null, new CompanyRotation(null, null), false));  // fábrica sin lote
        Assert.Equal("FIFO", RotationRules.Resolve(null, new CompanyRotation("FEFO", "FEFO"), false)); // un valor que no aplica al tipo se ignora
    }

    [Fact]
    public void The_product_exception_wins_only_when_it_applies_to_its_type()
    {
        var company = new CompanyRotation("FEFO", "FIFO");
        Assert.Equal("FIFO", RotationRules.Resolve("FIFO", company, true));
        Assert.Equal("POSITION", RotationRules.Resolve("POSITION", company, false));
        Assert.Equal("FEFO", RotationRules.Resolve("POSITION", company, true));   // por posición no aplica a un producto con lote: manda la compañía
        Assert.Equal("FIFO", RotationRules.Resolve("FEFO", company, false));      // FEFO no aplica a uno sin lote
    }

    [Fact]
    public void Position_ignores_dates_and_follows_zone_then_bin_code()
    {
        var picking = Candidates();   // A01..A04, todos en picking, con fechas y vencimientos distintos
        var byPosition = PickBatchRules.Eligible(picking, null, null, "POSITION").Select(c => c.BinCode).ToArray();
        Assert.Equal(new[] { "A01", "A02", "A03", "A04" }, byPosition);
        var mixed = new List<PickCandidate>
        {
            new(BinId: 9, "A01", ZoneTypes.Reserve, true, null, null, 5m, Jan),
            new(BinId: 8, "Z99", ZoneTypes.Picking, true, null, null, 5m, Mar),
        };
        Assert.Equal(new[] { "Z99", "A01" }, PickBatchRules.Eligible(mixed, null, null, "POSITION").Select(c => c.BinCode).ToArray());   // picking antes que reserva, aunque el código sea mayor
    }
}
