using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P7): recorrido etapa por etapa del pipeline de OrderStatus (entrega especial con chofer → IN_TRANSIT).</summary>
public class PipelinePathTests
{
    /// <summary>Pipeline de OrderStatus tal como lo siembra logistica-db-seed.sql.</summary>
    private static List<PipelineStage> OrderPipeline(params string[] disabled)
    {
        var stages = new List<PipelineStage>
        {
            new(OrderStatuses.Draft, 1, StageKinds.Pipeline, true),
            new(OrderStatuses.Confirmed, 2, StageKinds.Pipeline, false),
            new(OrderStatuses.Pickup, 3, StageKinds.Pipeline, false),
            new(OrderStatuses.Inbound, 4, StageKinds.Pipeline, false),
            new(OrderStatuses.Planned, 5, StageKinds.Pipeline, false),
            new(OrderStatuses.InTransit, 6, StageKinds.Pipeline, false),
            new(OrderStatuses.Arrived, 7, StageKinds.Pipeline, false),
            new(OrderStatuses.Delivered, 8, StageKinds.Terminal, false),
            new(OrderStatuses.OnHold, 20, StageKinds.Lateral, false),
            new(OrderStatuses.Partial, 21, StageKinds.Lateral, false),
            new(OrderStatuses.Failed, 22, StageKinds.Lateral, false),
            new(OrderStatuses.Cancelled, 30, StageKinds.Terminal, false),
        };
        return stages.Select(s => disabled.Contains(s.Code) ? s with { IsEnabled = false } : s).ToList();
    }

    [Fact]
    public void From_draft_walks_every_stage_to_in_transit()
    {
        Assert.Equal(
            new[] { OrderStatuses.Confirmed, OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(), OrderStatuses.Draft, OrderStatuses.InTransit));
    }

    [Fact]
    public void From_confirmed_walks_the_rest()
    {
        Assert.Equal(
            new[] { OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(), OrderStatuses.Confirmed, OrderStatuses.InTransit));
    }

    [Fact]
    public void Disabled_stages_are_skipped()
    {
        Assert.Equal(
            new[] { OrderStatuses.Confirmed, OrderStatuses.Planned, OrderStatuses.InTransit },
            PipelinePath.StepsTo(OrderPipeline(OrderStatuses.Pickup, OrderStatuses.Inbound), OrderStatuses.Draft, OrderStatuses.InTransit));
    }

    [Fact]
    public void Disabled_target_ends_at_last_enabled_pipeline_stage_before_it()
    {
        var steps = PipelinePath.StepsTo(OrderPipeline(OrderStatuses.InTransit), OrderStatuses.Confirmed, OrderStatuses.InTransit);
        Assert.Equal(new[] { OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.Planned }, steps);
        Assert.Empty(PipelinePath.StepsTo(OrderPipeline(OrderStatuses.InTransit), OrderStatuses.Planned, OrderStatuses.InTransit));
    }

    [Theory]
    [InlineData(OrderStatuses.InTransit)]
    [InlineData(OrderStatuses.Arrived)]
    public void Already_at_or_past_target_is_empty(string from)
    {
        Assert.Empty(PipelinePath.StepsTo(OrderPipeline(), from, OrderStatuses.InTransit));
    }

    [Theory]
    [InlineData(OrderStatuses.OnHold)]
    [InlineData(OrderStatuses.Cancelled)]
    [InlineData(OrderStatuses.Delivered)]
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
        // El tenant movió PLANNED antes de PICKUP (SortOverride): el recorrido sigue el orden efectivo.
        var stages = OrderPipeline().Select(s => s.Code == OrderStatuses.Planned ? s with { SortOrder = 2 } : s)
            .Select(s => s.Code == OrderStatuses.Confirmed ? s with { SortOrder = 1 } : s)
            .Select(s => s.Code == OrderStatuses.Draft ? s with { SortOrder = 0 } : s)
            .ToList();
        Assert.Equal(
            new[] { OrderStatuses.Confirmed, OrderStatuses.Planned, OrderStatuses.Pickup, OrderStatuses.Inbound, OrderStatuses.InTransit },
            PipelinePath.StepsTo(stages, "draft", "in_transit"));
    }
}
