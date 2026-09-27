using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P7) — reglas puras de Recolección y empaque: un solo dueño, cliente de la orden, eliminación, números a la vista,
/// filtros antes de la búsqueda, tope de 100 líneas, asignación FEFO y mensajes exactos (los cita el manual 06 y la FAQ).
/// </summary>
public class PickBatchRulesTests
{
    // ---------------------------------------------------------------- dueño y orden

    [Fact]
    public void SingleOwner_accepts_one_owner_or_all_own_and_rejects_mixes()
    {
        Assert.True(PickBatchRules.IsSingleOwner(new int?[] { null, null }));
        Assert.True(PickBatchRules.IsSingleOwner(new int?[] { 7, 7 }));
        Assert.True(PickBatchRules.IsSingleOwner(Array.Empty<int?>()));
        Assert.False(PickBatchRules.IsSingleOwner(new int?[] { null, 7 }));
        Assert.False(PickBatchRules.IsSingleOwner(new int?[] { 7, 8 }));
        Assert.Equal("Una recolección solo puede tener productos de un mismo dueño.", PickBatchRules.SingleOwner);
    }

    [Fact]
    public void OrderClientMustBeOwner_only_binds_when_owner_is_a_3pl_client()
    {
        Assert.True(PickBatchRules.OrderClientAllowed(null, 5));
        Assert.True(PickBatchRules.OrderClientAllowed(7, 7));
        Assert.False(PickBatchRules.OrderClientAllowed(7, 5));
        Assert.False(PickBatchRules.OrderClientAllowed(7, null));
        Assert.Equal("La orden debe ser del cliente dueño del inventario (Farmacia Central).",
            PickBatchRules.OrderClientMustBeOwner("Farmacia Central"));
    }

    [Fact]
    public void Pack_rejects_special_delivery_and_driver()
    {
        Assert.True(PickBatchRules.PackRequestAllowed(false, false));
        Assert.False(PickBatchRules.PackRequestAllowed(true, false));
        Assert.False(PickBatchRules.PackRequestAllowed(false, true));
        Assert.Equal("Un empaque no puede ser una entrega especial ni llevar chofer.", PickBatchRules.PackSpecialNotAllowed);
    }

    [Fact]
    public void CanPack_only_active_collected()
    {
        Assert.True(PickBatchRules.CanPack("COLLECTED", true));
        Assert.False(PickBatchRules.CanPack("COLLECTED", false));
        Assert.False(PickBatchRules.CanPack("PACKED", true));
        Assert.False(PickBatchRules.CanPack("CANCELLED", true));
        Assert.Equal("La recolección EMP-00012 ya fue empacada.", PickBatchRules.NotCollected("EMP-00012"));
    }

    [Fact]
    public void CanDelete_collected_always_packed_only_with_active_initial_order()
    {
        Assert.True(PickBatchRules.CanDelete("COLLECTED", false, false));
        Assert.True(PickBatchRules.CanDelete("PACKED", true, true));
        Assert.False(PickBatchRules.CanDelete("PACKED", true, false));
        Assert.False(PickBatchRules.CanDelete("PACKED", false, true));
        Assert.False(PickBatchRules.CanDelete("CANCELLED", true, true));
        Assert.Equal("La orden de la recolección EMP-00012 ya avanzó a 'Confirmada'; la recolección ya no se puede eliminar.",
            PickBatchRules.DeleteBlocked("EMP-00012", "Confirmada"));
    }

    [Fact]
    public void DisplayNumbers_uses_dash_without_invoice()
    {
        Assert.Equal("Orden: 2026-000123 · Factura: FAC-9", PickBatchRules.DisplayNumbers("2026-000123", "FAC-9"));
        Assert.Equal("Orden: 2026-000123 · Factura: —", PickBatchRules.DisplayNumbers("2026-000123", null));
        Assert.Equal("Orden: 2026-000123 · Factura: —", PickBatchRules.DisplayNumbers("2026-000123", "  "));
    }

    // ---------------------------------------------------------------- captura

    [Fact]
    public void ValidateQuantity_messages()
    {
        Assert.Null(PickBatchRules.ValidateQuantity(2.125m));
        Assert.Equal("La cantidad debe ser mayor que cero.", PickBatchRules.ValidateQuantity(null));
        Assert.Equal("La cantidad debe ser mayor que cero.", PickBatchRules.ValidateQuantity(0m));
        Assert.Equal("La cantidad debe ser mayor que cero.", PickBatchRules.ValidateQuantity(-1m));
        Assert.Equal("La cantidad admite como máximo 3 decimales.", PickBatchRules.ValidateQuantity(1.2345m));
        Assert.Equal("La cantidad excede el máximo permitido.", PickBatchRules.ValidateQuantity(PickBatchRules.MaxQuantity + 1m));
    }

    [Fact]
    public void Line_cap_is_100()
    {
        Assert.Equal(100, PickBatchRules.MaxLines);
        Assert.Equal("La recolección admite como máximo 100 líneas.", PickBatchRules.TooManyLines);
    }

    [Fact]
    public void NormalizeSerials_trims_skips_blanks_and_rejects_duplicates_and_long_values()
    {
        var (ok, error) = PickBatchRules.NormalizeSerials(new[] { " S1 ", "", null, "S2" });
        Assert.Null(error);
        Assert.Equal(new[] { "S1", "S2" }, ok);

        var (_, dup) = PickBatchRules.NormalizeSerials(new[] { "S1", "s1" });
        Assert.Equal("La serie s1 está repetida en la recolección.", dup);

        var (_, tooLong) = PickBatchRules.NormalizeSerials(new[] { new string('X', 81) });
        Assert.Equal("Cada número de serie admite como máximo 80 caracteres.", tooLong);

        var (_, tooMany) = PickBatchRules.NormalizeSerials(Enumerable.Range(1, 501).Select(i => "S" + i));
        Assert.Equal("Una línea admite como máximo 500 series.", tooMany);
    }

    [Fact]
    public void SerialQuantity_must_match_the_scanned_serials()
    {
        Assert.Equal((2m, (string?)null), PickBatchRules.SerialQuantity(null, 2));
        Assert.Equal((2m, (string?)null), PickBatchRules.SerialQuantity(2m, 2));
        Assert.Equal("En productos con serie la cantidad debe ser igual al número de series escaneadas.",
            PickBatchRules.SerialQuantity(3m, 2).Error);
        Assert.NotNull(PickBatchRules.SerialQuantity(1.5m, 2).Error);
    }

    // ---------------------------------------------------------------- FEFO (D14)

    private static PickCandidate C(int bin, string code, string? zone, int? lot, DateOnly? expiry, decimal available, bool active = true)
        => new(bin, code, zone, active, lot, expiry, available);

    [Fact]
    public void Allocate_fefo_first_then_zone_rank_and_code()
    {
        var candidates = new[]
        {
            C(1, "A-01", "PICKING", 10, new DateOnly(2027, 6, 1), 5m),
            C(2, "B-01", "RESERVE", 11, new DateOnly(2027, 1, 1), 2m),   // vence antes: primero aunque sea RESERVE
            C(3, "A-02", "RESERVE", 10, new DateOnly(2027, 6, 1), 9m),   // mismo vencimiento que bin 1: PICKING antes
            C(4, "A-00", "PICKING", null, null, 9m),                       // sin fecha: al final
        };
        var result = PickBatchRules.Allocate(7m, candidates);
        Assert.True(result.IsComplete);
        Assert.Equal(new[] { new PickAllocation(2, 11, 2m), new PickAllocation(1, 10, 5m) }, result.Allocations);
    }

    [Fact]
    public void Allocate_excludes_quarantine_crossdock_inactive_and_non_positive()
    {
        var candidates = new[]
        {
            C(1, "Q-01", "QUARANTINE", null, null, 5m),
            C(2, "XD-01", "CROSSDOCK", null, null, 5m),
            C(3, "A-01", "PICKING", null, null, 5m, active: false),
            C(4, "A-02", "PICKING", null, null, 0m),
            C(5, "A-03", "PICKING", null, null, -1m),
            C(6, "STG-01", "STAGING", null, null, 1m),
        };
        var result = PickBatchRules.Allocate(3m, candidates);
        Assert.False(result.IsComplete);
        Assert.Equal(2m, result.Shortfall);
        Assert.Equal(new[] { new PickAllocation(6, null, 1m) }, result.Allocations);
    }

    [Fact]
    public void Allocate_splits_5_plus_2_and_respects_explicit_bin_and_lot()
    {
        var candidates = new[]
        {
            C(1, "A-01", "PICKING", null, null, 5m),
            C(2, "B-01", "RESERVE", null, null, 4m),
        };
        var split = PickBatchRules.Allocate(7m, candidates);
        Assert.Equal(new[] { new PickAllocation(1, null, 5m), new PickAllocation(2, null, 2m) }, split.Allocations);

        var onlyBin2 = PickBatchRules.Allocate(7m, candidates, binId: 2);
        Assert.Equal(3m, onlyBin2.Shortfall);
        Assert.Equal(4m, onlyBin2.Allocated);

        var lots = new[] { C(1, "A-01", "PICKING", 10, null, 5m), C(1, "A-01", "PICKING", 11, null, 5m) };
        Assert.Equal(new[] { new PickAllocation(1, 11, 2m) }, PickBatchRules.Allocate(2m, lots, lotId: 11).Allocations);
    }

    [Fact]
    public void Allocate_is_deterministic_regardless_of_input_order()
    {
        var a = C(1, "A-02", "PICKING", null, null, 1m);
        var b = C(2, "A-01", "PICKING", null, null, 1m);
        var r1 = PickBatchRules.Allocate(1m, new[] { a, b });
        var r2 = PickBatchRules.Allocate(1m, new[] { b, a });
        Assert.Equal(r1.Allocations, r2.Allocations);
        Assert.Equal(2, r1.Allocations[0].BinId); // código A-01 antes que A-02
    }

    [Fact]
    public void Zone_rank_orders_picking_reserve_refrigerated_staging()
    {
        Assert.True(PickBatchRules.ZoneRank("PICKING") < PickBatchRules.ZoneRank("RESERVE"));
        Assert.True(PickBatchRules.ZoneRank("RESERVE") < PickBatchRules.ZoneRank("REFRIGERATED"));
        Assert.True(PickBatchRules.ZoneRank("REFRIGERATED") < PickBatchRules.ZoneRank("STAGING"));
        Assert.True(PickBatchRules.ZoneRank("STAGING") < PickBatchRules.ZoneRank(null));
        Assert.False(PickBatchRules.IsPickableZone("QUARANTINE"));
        Assert.False(PickBatchRules.IsPickableZone("crossdock"));
        Assert.True(PickBatchRules.IsPickableZone(null));
    }

    [Fact]
    public void InsufficientStock_message_uses_invariant_quantities()
    {
        Assert.Equal("Inventario insuficiente de PN en A01-R01-N1-P01: disponible 2.5, solicitado 9999.",
            PickBatchRules.InsufficientStock("PN", "A01-R01-N1-P01", 2.5m, 9999m));
    }

    // ---------------------------------------------------------------- listado: filtros y después búsqueda

    private static readonly PickBatchListRow[] Rows =
    {
        new(1, "EMP-00001", "2026-000001", "EMP-00001", "FAC-1", "Farmacia Central", new[] { "GLU-100" }),
        new(2, "EMP-00002", "2026-000002", "EMP-00002", "FAC-2", "Farmacia Norte", new[] { "TIR-50" }),
        new(3, "EMP-00003", null, null, null, null, new[] { "GLU-100" }),
    };

    [Fact]
    public void Filters_apply_before_search()
    {
        // La búsqueda 'GLU' coincide con 1 y 3, pero el filtro de orden ya dejó solo la 2: resultado vacío.
        Assert.Empty(PickBatchRules.FilterThenSearch(Rows, "000002", null, "GLU"));
        Assert.Equal(new[] { 1, 3 }, PickBatchRules.FilterThenSearch(Rows, null, null, "glu").Select(r => r.PickBatchId));
        Assert.Equal(new[] { 2 }, PickBatchRules.FilterThenSearch(Rows, null, "fac-2", null).Select(r => r.PickBatchId));
        Assert.Equal(new[] { 1, 2 }, PickBatchRules.FilterThenSearch(Rows, "2026", null, "farmacia").Select(r => r.PickBatchId));
    }

    [Fact]
    public void Order_filter_excludes_batches_without_order_and_empty_filters_do_not_filter()
    {
        Assert.False(PickBatchRules.MatchesFilters(Rows[2], "2026", null));
        Assert.True(PickBatchRules.MatchesFilters(Rows[2], null, " "));
        Assert.True(PickBatchRules.MatchesSearch(Rows[2], null));
        Assert.True(PickBatchRules.MatchesSearch(Rows[2], "emp-00003"));
    }

    [Fact]
    public void Paging_defaults_and_caps()
    {
        Assert.Equal((0, 100), PickBatchRules.NormalizePaging(-3, 0));
        Assert.Equal((10, 200), PickBatchRules.NormalizePaging(10, 5000));
    }

    // ---------------------------------------------------------------- dinero

    [Fact]
    public void TotalCost_rounds4_and_is_null_without_costs()
    {
        Assert.Equal(24.6912m, PickBatchRules.TotalCost(new (decimal, decimal?)[] { (2m, 12.3456m) }));
        Assert.Equal(0.0002m, PickBatchRules.TotalCost(new (decimal, decimal?)[] { (0.001m, 0.15m), (1m, null) })); // 0.00015 → 0.0002
        Assert.Null(PickBatchRules.TotalCost(new (decimal, decimal?)[] { (1m, null) }));
        Assert.Null(PickBatchRules.TotalCost(Array.Empty<(decimal, decimal?)>()));
    }
}
