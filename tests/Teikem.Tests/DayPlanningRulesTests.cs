using Xunit;
using DayPlanningAllocation = Teikem.Domain.Trips.DayPlanningAllocation;
using DayPlanningCandidate = Teikem.Domain.Trips.DayPlanningCandidate;
using DayPlanningRules = Teikem.Domain.Trips.DayPlanningRules;
using DayPlanningZone = Teikem.Domain.Trips.DayPlanningZone;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 (P9): reglas puras de 'Planificar el día': validación de la solicitud y reparto por zona (ruta abierta o nueva,
/// orden de asignación, omitidas por elegibilidad o por el tope de 300 paradas, órdenes sin zona, determinismo).
/// (Convención del lote: las pruebas no importan Teikem.Domain.Trips; se usan alias.)
/// </summary>
public class DayPlanningRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);
    private static readonly Dictionary<int, int> NoOpenTrips = new();

    private static DayPlanningCandidate C(int id, int? zone, string? number = null, DateTime? requested = null,
        string? ineligible = null, bool ambiguous = false)
        => new(id, number ?? $"ORD-{id:0000}", zone, ambiguous, requested, ineligible);

    private static DayPlanningZone Zone(DayPlanningAllocation a, int zoneId) => Assert.Single(a.Zones, z => z.ZoneId == zoneId);

    // ---------------------------------------------------------------- ValidateRequest

    [Fact]
    public void ValidateRequest_requires_the_plan_date()
    {
        var r = DayPlanningRules.ValidateRequest(null, null, Today);
        Assert.False(r.IsValid);
        Assert.Equal("planDate", r.Field);
        Assert.Equal("Indique la fecha de la ruta.", r.Error);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(61)]
    public void ValidateRequest_rejects_dates_out_of_range(int days)
    {
        var r = DayPlanningRules.ValidateRequest(Today.AddDays(days), null, Today);
        Assert.Equal("La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.", r.Error);
    }

    [Fact]
    public void ValidateRequest_without_zones_means_all_active_zones()
    {
        var none = DayPlanningRules.ValidateRequest(Today, null, Today);
        var empty = DayPlanningRules.ValidateRequest(Today, Array.Empty<int>(), Today);
        Assert.True(none.IsValid);
        Assert.Null(none.ZoneIds);
        Assert.True(empty.IsValid);
        Assert.Null(empty.ZoneIds);
        Assert.Equal(Today, none.PlanDate);
    }

    [Fact]
    public void ValidateRequest_rejects_51_zones()
    {
        var r = DayPlanningRules.ValidateRequest(Today, Enumerable.Range(1, 51).ToArray(), Today);
        Assert.Equal("dispatchZoneIds", r.Field);
        Assert.Equal("Máximo 50 zonas por planificación.", r.Error);
        Assert.Equal(DayPlanningRules.MaxZonesMessage, r.Error);
    }

    [Fact]
    public void ValidateRequest_collapses_duplicates_before_counting()
    {
        var ids = Enumerable.Range(1, 50).Concat(Enumerable.Range(1, 50)).ToArray();   // 100 ids, 50 distintos
        var r = DayPlanningRules.ValidateRequest(Today, ids, Today);
        Assert.True(r.IsValid);
        Assert.Equal(50, r.ZoneIds!.Count);

        var dup = DayPlanningRules.ValidateRequest(Today, new[] { 7, 3, 7 }, Today);
        Assert.Equal(new[] { 7, 3 }, dup.ZoneIds);
    }

    // ---------------------------------------------------------------- Allocate

    [Fact]
    public void Allocate_groups_by_zone_and_creates_a_trip_when_there_is_no_open_one()
    {
        var a = DayPlanningRules.Allocate(
            new[] { C(1, 10), C(2, 20), C(3, 10) }, NoOpenTrips, new[] { 10, 20 }, createEmptyTrips: false);

        Assert.Equal(new[] { 10, 20 }, a.Zones.Select(z => z.ZoneId));
        Assert.Equal(new[] { 1, 3 }, Zone(a, 10).Assigned);
        Assert.Equal(new[] { 2 }, Zone(a, 20).Assigned);
        Assert.All(a.Zones, z => Assert.True(z.CreateTrip));
        Assert.All(a.Zones, z => Assert.False(z.HasOpenTrip));
        Assert.Equal(3, a.OrdersAssigned);
        Assert.Equal(2, a.TripsToCreate);
    }

    [Fact]
    public void Allocate_reuses_the_open_trip_of_the_zone()
    {
        var a = DayPlanningRules.Allocate(new[] { C(1, 10) }, new Dictionary<int, int> { [10] = 4 }, new[] { 10 }, false);
        var z = Zone(a, 10);
        Assert.True(z.HasOpenTrip);
        Assert.False(z.CreateTrip);
        Assert.Equal(new[] { 1 }, z.Assigned);
    }

    [Fact]
    public void Allocate_creates_only_with_orders_unless_create_empty_trips()
    {
        var without = DayPlanningRules.Allocate(Array.Empty<DayPlanningCandidate>(), NoOpenTrips, new[] { 10 }, false);
        Assert.False(Zone(without, 10).CreateTrip);
        Assert.Equal(0, without.TripsToCreate);

        var empty = DayPlanningRules.Allocate(Array.Empty<DayPlanningCandidate>(), NoOpenTrips, new[] { 10 }, true);
        Assert.True(Zone(empty, 10).CreateTrip);
        Assert.Empty(Zone(empty, 10).Assigned);

        // Con ruta abierta, CreateEmptyTrips no crea otra.
        var open = DayPlanningRules.Allocate(Array.Empty<DayPlanningCandidate>(), new Dictionary<int, int> { [10] = 0 }, new[] { 10 }, true);
        Assert.False(Zone(open, 10).CreateTrip);
    }

    [Fact]
    public void Allocate_skips_ineligible_orders_with_their_reason_and_does_not_create_for_them()
    {
        const string reason = "El estatus actual no permite la acción 'ASSIGN_TRIP'.";
        var a = DayPlanningRules.Allocate(new[] { C(1, 10, ineligible: reason) }, NoOpenTrips, new[] { 10 }, false);
        var z = Zone(a, 10);
        Assert.Empty(z.Assigned);
        Assert.False(z.CreateTrip);
        var skip = Assert.Single(z.Skipped);
        Assert.Equal(1, skip.TransportOrderId);
        Assert.Equal("NOT_ELIGIBLE", skip.ReasonCode);
        Assert.Equal(reason, skip.Reason);
        Assert.Equal(1, a.OrdersSkipped);
    }

    [Fact]
    public void Allocate_respects_the_300_stop_hard_cap_counting_existing_stops()
    {
        var candidates = Enumerable.Range(1, 8).Select(i => C(i, 10)).ToList();
        var a = DayPlanningRules.Allocate(candidates, new Dictionary<int, int> { [10] = 295 }, new[] { 10 }, false);
        var z = Zone(a, 10);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, z.Assigned);
        Assert.Equal(3, z.Skipped.Count);
        Assert.All(z.Skipped, s =>
        {
            Assert.Equal("CAPACITY_HARD_CAP", s.ReasonCode);
            Assert.Equal("La ruta llegó al máximo de 300 paradas; la orden queda sin asignar.", s.Reason);
        });
    }

    [Fact]
    public void Allocate_caps_a_new_trip_at_300_stops()
    {
        var candidates = Enumerable.Range(1, 302).Select(i => C(i, 10)).ToList();
        var a = DayPlanningRules.Allocate(candidates, NoOpenTrips, new[] { 10 }, false);
        Assert.Equal(300, Zone(a, 10).Assigned.Count);
        Assert.Equal(2, Zone(a, 10).Skipped.Count);
    }

    [Fact]
    public void Allocate_ineligible_orders_do_not_consume_capacity()
    {
        var a = DayPlanningRules.Allocate(
            new[] { C(1, 10, ineligible: "x"), C(2, 10) }, new Dictionary<int, int> { [10] = 299 }, new[] { 10 }, false);
        Assert.Equal(new[] { 2 }, Zone(a, 10).Assigned);
        Assert.Equal("NOT_ELIGIBLE", Assert.Single(Zone(a, 10).Skipped).ReasonCode);
    }

    [Fact]
    public void Allocate_orders_by_requested_date_then_number_with_undated_last()
    {
        var a = DayPlanningRules.Allocate(new[]
        {
            C(1, 10, "B-2", null),
            C(2, 10, "A-9", new DateTime(2026, 9, 26)),
            C(3, 10, "A-1", new DateTime(2026, 9, 26)),
            C(4, 10, "Z-1", new DateTime(2026, 9, 20)),
            C(5, 10, "A-0", null),
        }, NoOpenTrips, new[] { 10 }, false);
        Assert.Equal(new[] { 4, 3, 2, 5, 1 }, Zone(a, 10).Assigned);
    }

    [Fact]
    public void Allocate_ignores_zones_outside_the_list_and_counts_orders_without_zone()
    {
        var a = DayPlanningRules.Allocate(new[]
        {
            C(1, 10),
            C(2, 99),                    // zona fuera de la lista: no cuenta en ningún total
            C(3, null),                  // sin zona
            C(4, null, ambiguous: true), // ambigua
        }, NoOpenTrips, new[] { 10 }, false);

        var z = Assert.Single(a.Zones);
        Assert.Equal(10, z.ZoneId);
        Assert.Equal(new[] { 1 }, z.Assigned);
        Assert.Equal(2, a.OrdersWithoutZone);
        Assert.Equal(1, a.OrdersAssigned);
        Assert.Equal(0, a.OrdersSkipped);
    }

    [Fact]
    public void Allocate_lists_every_target_zone_in_ascending_order_even_without_orders()
    {
        var a = DayPlanningRules.Allocate(new[] { C(1, 30) }, NoOpenTrips, new[] { 30, 10, 20 }, false);
        Assert.Equal(new[] { 10, 20, 30 }, a.Zones.Select(z => z.ZoneId));
        Assert.False(Zone(a, 10).CreateTrip);
        Assert.True(Zone(a, 30).CreateTrip);
    }

    [Fact]
    public void Allocate_counts_a_repeated_order_once()
    {
        var a = DayPlanningRules.Allocate(new[] { C(1, 10), C(1, 10) }, NoOpenTrips, new[] { 10 }, false);
        Assert.Equal(new[] { 1 }, Zone(a, 10).Assigned);
    }

    [Fact]
    public void Allocate_is_deterministic()
    {
        var candidates = new[]
        {
            C(5, 10, "C", new DateTime(2026, 9, 25)), C(2, 20, "A"), C(9, 10, "B", new DateTime(2026, 9, 25)),
            C(1, 10, "B", new DateTime(2026, 9, 25)), C(7, null), C(3, 20, "A", ineligible: "no"),
        };
        var open = new Dictionary<int, int> { [20] = 1 };
        var first = DayPlanningRules.Allocate(candidates, open, new[] { 10, 20 }, false);
        var second = DayPlanningRules.Allocate(Enumerable.Reverse(candidates).ToArray(), open, new[] { 20, 10 }, false);

        Assert.Equal(Describe(first), Describe(second));
        Assert.Equal(new[] { 1, 9, 5 }, Zone(first, 10).Assigned);             // misma fecha: número, luego id
    }

    private static string Describe(DayPlanningAllocation a)
        => string.Join("|", a.Zones.Select(z =>
               $"{z.ZoneId}:{z.HasOpenTrip}:{z.CreateTrip}:[{string.Join(",", z.Assigned)}]:[{string.Join(",", z.Skipped.Select(s => $"{s.TransportOrderId}/{s.ReasonCode}"))}]"))
           + $"|nz={a.OrdersWithoutZone}";

    // ---------------------------------------------------------------- RemoveTaken

    [Fact]
    public void RemoveTaken_drops_orders_assigned_elsewhere_and_cancels_an_unneeded_new_trip()
    {
        var a = DayPlanningRules.Allocate(new[] { C(1, 10), C(2, 20), C(3, 20) }, NoOpenTrips, new[] { 10, 20 }, false);
        var r = DayPlanningRules.RemoveTaken(a, new[] { 1, 3 }, createEmptyTrips: false);

        Assert.Empty(Zone(r, 10).Assigned);
        Assert.False(Zone(r, 10).CreateTrip);
        Assert.Equal(new[] { 2 }, Zone(r, 20).Assigned);
        Assert.True(Zone(r, 20).CreateTrip);

        var keep = DayPlanningRules.RemoveTaken(a, new[] { 1 }, createEmptyTrips: true);
        Assert.True(Zone(keep, 10).CreateTrip);
        Assert.Same(a, DayPlanningRules.RemoveTaken(a, Array.Empty<int>(), false));
    }

    [Fact]
    public void Skipped_issue_message_names_the_order()
        => Assert.Equal("Orden 2026-0007: La ruta llegó al máximo de 300 paradas; la orden queda sin asignar.",
            DayPlanningRules.SkippedIssueMessage("2026-0007", DayPlanningRules.CapacityHardCapMessage));
}
