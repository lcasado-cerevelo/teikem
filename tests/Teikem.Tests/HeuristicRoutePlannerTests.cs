using Xunit;
using HeuristicRoutePlanner = Teikem.Domain.Trips.HeuristicRoutePlanner;
using PlanStopInput = Teikem.Domain.Trips.PlanStopInput;
using RouteOptimizationRequest = Teikem.Domain.Trips.RouteOptimizationRequest;
using RouteOptimizationRules = Teikem.Domain.Trips.RouteOptimizationRules;
using UnassignedReasons = Teikem.Domain.Trips.UnassignedReasons;
using VehicleCapacity = Teikem.Domain.Trips.VehicleCapacity;

namespace Teikem.Tests;

/// <summary>Lote 5 / P3: motor HEURISTIC (puro y determinista): orden total, capacidad greedy y resultado válido.</summary>
public class HeuristicRoutePlannerTests
{
    private static readonly DateTime Day = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly VehicleCapacity NoLimits = new(MaxStops: null, MaxWeightKg: null, MaxVolumeM3: null);

    private static PlanStopInput Stop(int id, string? zone = null, DateTime? end = null, DateTime? start = null,
        string? postal = null, string? city = null, string? number = null, decimal? weight = null, decimal? volume = null)
        => new(
            OrderStopId: id,
            OrderNumber: number ?? $"ORD-{id:D4}",
            ZoneCode: zone,
            PostalCode: postal,
            City: city ?? string.Empty,
            WindowStartUtc: start,
            WindowEndUtc: end,
            ServiceMinutes: 10,
            WeightKg: weight,
            VolumeM3: volume,
            Lat: null,
            Lng: null);

    private static RouteOptimizationRequest Request(VehicleCapacity capacity, params PlanStopInput[] stops)
        => new(StartUtc: Day.AddHours(12), Capacity: capacity, Stops: stops);

    private static int[] Ids(IEnumerable<PlanStopInput> stops) => stops.Select(s => s.OrderStopId).ToArray();

    // ---------------------------------------------------------------- orden total

    [Fact]
    public void Orders_by_zone_first_with_null_zone_last()
    {
        var ordered = HeuristicRoutePlanner.Order(new[] { Stop(1, zone: null), Stop(2, zone: "Z2"), Stop(3, zone: "Z1") });
        Assert.Equal(new[] { 3, 2, 1 }, Ids(ordered));
    }

    [Fact]
    public void Within_a_zone_orders_by_window_end_then_window_start_with_nulls_last()
    {
        var ordered = HeuristicRoutePlanner.Order(new[]
        {
            Stop(1, "Z1", end: null, start: Day.AddHours(8)),
            Stop(2, "Z1", end: Day.AddHours(17), start: Day.AddHours(9)),
            Stop(3, "Z1", end: Day.AddHours(12), start: null),
            Stop(4, "Z1", end: Day.AddHours(17), start: Day.AddHours(8)),
        });
        Assert.Equal(new[] { 3, 4, 2, 1 }, Ids(ordered));
    }

    [Fact]
    public void Ties_break_by_postal_code_then_normalized_city_then_order_number_then_stop_id()
    {
        var ordered = HeuristicRoutePlanner.Order(new[]
        {
            Stop(1, "Z1", postal: null, city: "Aguadilla"),
            Stop(2, "Z1", postal: "00961", city: "Bayamón", number: "B"),
            Stop(3, "Z1", postal: "00961", city: "bayamon", number: "A"),
            Stop(4, "Z1", postal: "00949", city: "Toa Baja"),
            Stop(5, "Z1", postal: "00961", city: "Bayamón", number: "A"),
            Stop(6, "Z1", postal: "00961", city: "Aibonito"),
        });
        // 00949 < 00961; dentro de 00961: AIBONITO < BAYAMON; BAYAMON 'A' (ids 3 y 5 por OrderStopId) < 'B'; sin CP al final.
        Assert.Equal(new[] { 4, 6, 3, 5, 2, 1 }, Ids(ordered));
    }

    [Fact]
    public void Order_is_deterministic_regardless_of_input_order()
    {
        var stops = new[]
        {
            Stop(1, "Z2", end: Day.AddHours(10)), Stop(2, "Z1", end: Day.AddHours(15)), Stop(3, null, postal: "00901"),
            Stop(4, "Z1", end: Day.AddHours(9)), Stop(5, "Z1", end: Day.AddHours(9), postal: "00901"), Stop(6, "Z2"),
        };
        var expected = HeuristicRoutePlanner.Plan(Request(NoLimits, stops)).OrderedStopIds.ToArray();
        var rng = new Random(42);
        for (var i = 0; i < 20; i++)
        {
            var shuffled = stops.OrderBy(_ => rng.Next()).ToArray();
            Assert.Equal(expected, HeuristicRoutePlanner.Plan(Request(NoLimits, shuffled)).OrderedStopIds.ToArray());
        }
        Assert.Equal(new[] { 5, 4, 2, 1, 6, 3 }, expected); // 4 y 5 empatan en ventana: el CP gana al nulo
    }

    [Theory]
    [InlineData("  Bayamón ", "BAYAMON")]
    [InlineData("Toa   Baja", "TOA BAJA")]
    [InlineData("MAYAGÜEZ", "MAYAGUEZ")]
    [InlineData(null, "")]
    public void NormalizeCity_removes_accents_case_and_extra_spaces(string? city, string expected)
        => Assert.Equal(expected, HeuristicRoutePlanner.NormalizeCity(city));

    // ---------------------------------------------------------------- capacidad

    [Fact]
    public void Without_limits_everything_is_assigned_and_polyline_is_null()
    {
        var result = HeuristicRoutePlanner.Plan(Request(NoLimits, Stop(1, "Z1", weight: 5000m), Stop(2, "Z1", volume: 90m), Stop(3)));
        Assert.Equal(3, result.OrderedStopIds.Count);
        Assert.Empty(result.Unassigned);
        Assert.Null(result.Polyline);
    }

    [Fact]
    public void Max_stops_leaves_the_rest_with_capacity_stops()
    {
        var result = HeuristicRoutePlanner.Plan(Request(new VehicleCapacity(MaxStops: 2, MaxWeightKg: null, MaxVolumeM3: null),
            Stop(1, "Z1"), Stop(2, "Z1"), Stop(3, "Z1")));
        Assert.Equal(new[] { 1, 2 }, result.OrderedStopIds.ToArray());
        var u = Assert.Single(result.Unassigned);
        Assert.Equal(3, u.OrderStopId);
        Assert.Equal(UnassignedReasons.CapacityStops, u.ReasonCode);
        Assert.Equal("Excede el máximo de paradas del vehículo.", UnassignedReasons.Message(u.ReasonCode));
    }

    [Fact]
    public void Weight_overflow_is_skipped_and_a_later_lighter_stop_still_fits()
    {
        var result = HeuristicRoutePlanner.Plan(Request(new VehicleCapacity(MaxStops: null, MaxWeightKg: 1000m, MaxVolumeM3: null),
            Stop(1, "Z1", weight: 600m), Stop(2, "Z1", weight: 500m), Stop(3, "Z1", weight: 400m), Stop(4, "Z1", weight: null)));
        Assert.Equal(new[] { 1, 3, 4 }, result.OrderedStopIds.ToArray()); // 600 + 400 = 1000 (límite inclusivo); NULL cuenta 0
        var u = Assert.Single(result.Unassigned);
        Assert.Equal(2, u.OrderStopId);
        Assert.Equal(UnassignedReasons.CapacityWeight, u.ReasonCode);
        Assert.Equal("Excede la capacidad de peso del vehículo.", UnassignedReasons.Message(u.ReasonCode));
    }

    [Fact]
    public void Volume_overflow_uses_capacity_volume()
    {
        var result = HeuristicRoutePlanner.Plan(Request(new VehicleCapacity(MaxStops: null, MaxWeightKg: null, MaxVolumeM3: 10m),
            Stop(1, "Z1", volume: 8m), Stop(2, "Z1", volume: 3m)));
        Assert.Equal(new[] { 1 }, result.OrderedStopIds.ToArray());
        var u = Assert.Single(result.Unassigned);
        Assert.Equal(UnassignedReasons.CapacityVolume, u.ReasonCode);
        Assert.Equal("Excede la capacidad de volumen del vehículo.", UnassignedReasons.Message(u.ReasonCode));
    }

    [Fact]
    public void The_first_failing_reason_wins_stops_then_weight_then_volume()
    {
        var full = new VehicleCapacity(MaxStops: 1, MaxWeightKg: 10m, MaxVolumeM3: 1m);
        var result = HeuristicRoutePlanner.Plan(Request(full, Stop(1, "Z1", weight: 1m, volume: 0.5m), Stop(2, "Z1", weight: 50m, volume: 5m)));
        Assert.Equal(UnassignedReasons.CapacityStops, Assert.Single(result.Unassigned).ReasonCode);

        var heavy = new VehicleCapacity(MaxStops: 5, MaxWeightKg: 10m, MaxVolumeM3: 1m);
        result = HeuristicRoutePlanner.Plan(Request(heavy, Stop(1, "Z1", weight: 50m, volume: 5m)));
        Assert.Equal(UnassignedReasons.CapacityWeight, Assert.Single(result.Unassigned).ReasonCode);
    }

    [Fact]
    public void Empty_request_gives_an_empty_plan()
    {
        var result = HeuristicRoutePlanner.Plan(Request(NoLimits));
        Assert.Empty(result.OrderedStopIds);
        Assert.Empty(result.Unassigned);
    }

    // ---------------------------------------------------------------- validez del resultado

    [Fact]
    public void Result_passes_ValidateResult()
    {
        var request = Request(new VehicleCapacity(MaxStops: 3, MaxWeightKg: 100m, MaxVolumeM3: 2m),
            Stop(1, "Z1", weight: 40m), Stop(2, "Z2", weight: 70m), Stop(3, null, volume: 3m), Stop(4, "Z1", weight: 10m),
            Stop(5, "Z1"), Stop(6, "Z2"));
        var result = HeuristicRoutePlanner.Plan(request);
        Assert.Null(RouteOptimizationRules.ValidateResult(request, result));
        Assert.Equal(6, result.OrderedStopIds.Count + result.Unassigned.Count);
    }
}
