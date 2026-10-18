using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P7): recorrido etapa por etapa del pipeline de OrderStatus (entrega especial con chofer → IN_TRANSIT).</summary>
public class PipelinePathTests
{
    /// <summary>Pipeline de OrderStatus tal como queda con el seed + la sección (an) del Lote 106 en logistica-db-update.sql.</summary>
    private static List<PipelineStage> OrderPipeline(params string[] disabled)
    {
        var stages = new List<PipelineStage>
        {
            new(OrderStatuses.Submitted, 1, StageKinds.Pipeline, true),
            new(OrderStatuses.Pickup, 2, StageKinds.Pipeline, false),
            new(OrderStatuses.Unassigned, 3, StageKinds.Pipeline, false),
            new(OrderStatuses.Assigned, 4, StageKinds.Pipeline, false),
            new(OrderStatuses.InTransit, 5, StageKinds.Pipeline, false),
            new(OrderStatuses.Delivered, 8, StageKinds.Terminal, false),
            new(OrderStatuses.InWarehouse, 9, StageKinds.Terminal, false),
            new(OrderStatuses.Failed, 22, StageKinds.Lateral, false),
            new(OrderStatuses.Cancelled, 30, StageKinds.Terminal, false),
        };
        return stages.Select(s => disabled.Contains(s.Code) ? s with { IsEnabled = false } : s).ToList();
    }

    [Fact]
    public void From_submitted_walks_every_stage_to_in_transit()
    {
        Assert.Equal(
            new[] { OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(), OrderStatuses.Submitted, OrderStatuses.InTransit));
    }

    [Fact]
    public void From_pickup_walks_the_rest()
    {
        Assert.Equal(
            new[] { OrderStatuses.Unassigned, OrderStatuses.Assigned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(), OrderStatuses.Pickup, OrderStatuses.InTransit));
    }

    [Fact]
    public void Disabled_stages_are_skipped()
    {
        Assert.Equal(
            new[] { OrderStatuses.Assigned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(OrderStatuses.Pickup, OrderStatuses.Unassigned), OrderStatuses.Submitted, OrderStatuses.InTransit));
    }

    [Fact]
    public void Disabled_target_ends_at_last_enabled_pipeline_stage_before_it()
    {
        var steps = PipelinePath.StepsTo(OrderPipeline(OrderStatuses.InTransit), OrderStatuses.Pickup, OrderStatuses.InTransit);
        Assert.Equal(new[] { OrderStatuses.Unassigned, OrderStatuses.Assigned }, steps);
        Assert.Empty(PipelinePath.StepsTo(OrderPipeline(OrderStatuses.InTransit), OrderStatuses.Assigned, OrderStatuses.InTransit));
    }

    [Theory]
    [InlineData(OrderStatuses.InTransit)]
    public void Already_at_or_past_target_is_empty(string from)
    {
        Assert.Empty(PipelinePath.StepsTo(OrderPipeline(), from, OrderStatuses.InTransit));
    }

    [Theory]
    [InlineData(OrderStatuses.Failed)]
    [InlineData(OrderStatuses.Cancelled)]
    [InlineData(OrderStatuses.Delivered)]
    [InlineData(OrderStatuses.InWarehouse)]
    public void Lateral_or_terminal_origin_throws(string from)
    {
        Assert.Throws<InvalidOperationException>(() => PipelinePath.StepsTo(OrderPipeline(), from, OrderStatuses.InTransit));
    }

    [Fact]
    public void Unknown_origin_throws()
    {
        Assert.Throws<InvalidOperationException>(() => PipelinePath.StepsTo(OrderPipeline(), "FOO", OrderStatuses.InTransit));
    }

    [Fact]
    public void Codes_are_case_insensitive_and_sort_overrides_are_respected()
    {
        // El tenant movió ASSIGNED antes de PICKUP (SortOverride): el recorrido sigue el orden efectivo.
        var stages = OrderPipeline().Select(s => s.Code == OrderStatuses.Assigned ? s with { SortOrder = 2 } : s)
            .Select(s => s.Code == OrderStatuses.Pickup ? s with { SortOrder = 3 } : s)
            .Select(s => s.Code == OrderStatuses.Unassigned ? s with { SortOrder = 4 } : s)
            .ToList();
        Assert.Equal(
            new[] { OrderStatuses.Assigned, OrderStatuses.Pickup, OrderStatuses.Unassigned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(stages, "submitted", "in_transit"));
    }
}
