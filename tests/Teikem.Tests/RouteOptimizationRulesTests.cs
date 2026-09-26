using Xunit;
using PlanStopInput = Teikem.Domain.Trips.PlanStopInput;
using RouteOptimizationRequest = Teikem.Domain.Trips.RouteOptimizationRequest;
using RouteOptimizationResult = Teikem.Domain.Trips.RouteOptimizationResult;
using RouteOptimizationRules = Teikem.Domain.Trips.RouteOptimizationRules;
using UnassignedPlanStop = Teikem.Domain.Trips.UnassignedPlanStop;
using UnassignedReasons = Teikem.Domain.Trips.UnassignedReasons;
using VehicleCapacity = Teikem.Domain.Trips.VehicleCapacity;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: la respuesta de cualquier motor se valida antes de escribirla (ids de la entrada, sin repetidos, completa).</summary>
public class RouteOptimizationRulesTests
{
    private static PlanStopInput Stop(int id) => new(id, $"ORD-{id}", "Z1", "00949", "San Juan", null, null, 10, null, null, null, null);

    private static readonly RouteOptimizationRequest Request = new(null, VehicleCapacity.Unlimited, new[] { Stop(1), Stop(2), Stop(3) });

    private static RouteOptimizationResult Result(int[] ordered, params (int Id, string Reason)[] unassigned)
        => new(ordered, unassigned.Select(u => new UnassignedPlanStop(u.Id, u.Reason)).ToList(), null);

    [Fact]
    public void Valid_result_passes()
    {
        Assert.Null(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 3, 1, 2 })));
        Assert.Null(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 3, 1 }, (2, UnassignedReasons.CapacityStops))));
    }

    [Fact]
    public void Foreign_id_is_rejected() => Assert.NotNull(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 1, 2, 3, 99 })));

    [Fact]
    public void Repeated_id_is_rejected() => Assert.NotNull(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 1, 2, 2, 3 })));

    [Fact]
    public void Missing_stop_is_rejected() => Assert.Contains("3", RouteOptimizationRules.ValidateResult(Request, Result(new[] { 1, 2 })));

    [Fact]
    public void Stop_in_both_groups_is_rejected()
        => Assert.NotNull(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 1, 2, 3 }, (2, UnassignedReasons.CapacityWeight))));

    [Fact]
    public void Unassigned_without_reason_and_null_result_are_rejected()
    {
        Assert.NotNull(RouteOptimizationRules.ValidateResult(Request, Result(new[] { 1, 2 }, (3, " "))));
        Assert.NotNull(RouteOptimizationRules.ValidateResult(Request, null));
    }

    [Theory]
    [InlineData("CAPACITY_STOPS", "Excede el máximo de paradas del vehículo.")]
    [InlineData("CAPACITY_WEIGHT", "Excede la capacidad de peso del vehículo.")]
    [InlineData("CAPACITY_VOLUME", "Excede la capacidad de volumen del vehículo.")]
    [InlineData("TIME_WINDOW", "TIME_WINDOW")]
    public void Unassigned_reason_labels(string code, string label) => Assert.Equal(label, UnassignedReasons.Message(code));
}
