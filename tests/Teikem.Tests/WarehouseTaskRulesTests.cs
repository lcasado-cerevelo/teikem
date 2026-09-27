using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P5) — reglas puras de la cola de tareas de almacén (R17, D41), con los mensajes exactos del manual.</summary>
public class WarehouseTaskRulesTests
{
    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("Las tareas de tipo PICK no se completan desde la cola.", WarehouseTaskRules.NoHandler("PICK"));
        Assert.Equal("La tarea ya fue completada o cancelada.", WarehouseTaskRules.TaskNotOpen);
        Assert.Equal("El usuario no es miembro activo de la compañía.", WarehouseTaskRules.AssigneeNotMember);
        Assert.Equal("La cantidad excede la de la tarea.", WarehouseTaskRules.QtyExceeds);
        Assert.Equal("Indique la posición de destino.", WarehouseTaskRules.DestinationRequired);
        Assert.Equal("Las tareas de tipo COUNT se cancelan desde su pantalla, no desde la cola.", WarehouseTaskRules.CancelNotFromQueue("COUNT"));
        Assert.Equal("Indique exactamente 3 número(s) de serie.", WarehouseTaskRules.SerialCountMismatch(3m));
    }

    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(10, 4, 6)]
    [InlineData(5.5, 2.25, 3.25)]
    [InlineData(3, 5, 0)]
    public void Split_returns_the_remainder(decimal taskQty, decimal completed, decimal remainder)
        => Assert.Equal(remainder, WarehouseTaskRules.Split(taskQty, completed));

    [Fact]
    public void Completion_quantity_defaults_to_task_quantity_and_validates()
    {
        Assert.Equal((8m, (string?)null), WarehouseTaskRules.CompletionQuantity(8m, null));
        Assert.Equal((3.5m, (string?)null), WarehouseTaskRules.CompletionQuantity(8m, 3.5m));
        Assert.Equal(WarehouseTaskRules.QtyExceeds, WarehouseTaskRules.CompletionQuantity(8m, 9m).Error);
        Assert.Equal(WarehouseTaskRules.QtyPositive, WarehouseTaskRules.CompletionQuantity(8m, 0m).Error);
        Assert.Equal(WarehouseTaskRules.QtyPositive, WarehouseTaskRules.CompletionQuantity(8m, -1m).Error);
        Assert.Equal(WarehouseTaskRules.QtyDecimals, WarehouseTaskRules.CompletionQuantity(8m, 1.2345m).Error);
        Assert.Equal(WarehouseTaskRules.QuantityMissing, WarehouseTaskRules.CompletionQuantity(null, 1m).Error);
    }

    [Fact]
    public void Destination_is_required_and_distinct_from_origin()
    {
        Assert.Equal(((int?)null, WarehouseTaskRules.DestinationRequired), WarehouseTaskRules.Destination(null, null, 5));
        Assert.Equal(((int?)7, (string?)null), WarehouseTaskRules.Destination(null, 7, 5));
        Assert.Equal(((int?)9, (string?)null), WarehouseTaskRules.Destination(9, 7, 5));   // el operador cambia el sugerido
        Assert.Equal(WarehouseTaskRules.SameBin, WarehouseTaskRules.Destination(5, 7, 5).Error);
    }

    [Fact]
    public void Serials_for_completion_match_quantity_and_task_serial()
    {
        Assert.Equal(new[] { "S1", "S2" }, WarehouseTaskRules.SerialsForCompletion(2m, null, new[] { " S1 ", "S2" }).Serials);
        Assert.Equal(WarehouseTaskRules.SerialCountMismatch(2m), WarehouseTaskRules.SerialsForCompletion(2m, null, new[] { "S1" }).Error);
        Assert.Equal(WarehouseTaskRules.SerialDuplicated, WarehouseTaskRules.SerialsForCompletion(2m, null, new[] { "S1", "s1" }).Error);
        Assert.Equal(WarehouseTaskRules.SerialQtyInteger, WarehouseTaskRules.SerialsForCompletion(1.5m, null, new[] { "S1" }).Error);
        Assert.Equal(WarehouseTaskRules.SerialTooLong, WarehouseTaskRules.SerialsForCompletion(1m, null, new[] { new string('X', 81) }).Error);
        // Tarea de una serie específica: se toma aunque no se indique; otra serie → 400.
        Assert.Equal(new[] { "S9" }, WarehouseTaskRules.SerialsForCompletion(1m, "S9", null).Serials);
        Assert.Equal(WarehouseTaskRules.SerialNotOfTask, WarehouseTaskRules.SerialsForCompletion(1m, "S9", new[] { "S1" }).Error);
    }

    [Fact]
    public void Open_statuses_and_cancelable_types()
    {
        Assert.True(WarehouseTaskRules.IsOpen(WarehouseTaskStatuses.Pending));
        Assert.True(WarehouseTaskRules.IsOpen("in_progress"));
        Assert.False(WarehouseTaskRules.IsOpen(WarehouseTaskStatuses.Done));
        Assert.False(WarehouseTaskRules.IsOpen(WarehouseTaskStatuses.Cancelled));
        Assert.False(WarehouseTaskRules.IsOpen(null));

        Assert.True(WarehouseTaskRules.IsCancelableFromQueue(WarehouseTaskTypes.Putaway));
        Assert.True(WarehouseTaskRules.IsCancelableFromQueue(WarehouseTaskTypes.Replenish));
        Assert.False(WarehouseTaskRules.IsCancelableFromQueue(WarehouseTaskTypes.Count));
        Assert.False(WarehouseTaskRules.IsCancelableFromQueue(WarehouseTaskTypes.CrossDock));
    }

    [Fact]
    public void Queue_order_is_priority_then_oldest_then_id()
    {
        var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var tasks = new[]
        {
            (Id: 1, Priority: 100, Created: t0.AddHours(1)),
            (Id: 2, Priority: 50, Created: t0.AddHours(3)),
            (Id: 3, Priority: 100, Created: t0),
            (Id: 4, Priority: 100, Created: t0),
        };
        var ordered = WarehouseTaskRules.QueueOrder(tasks, t => t.Priority, t => t.Created, t => t.Id).Select(t => t.Id);
        Assert.Equal(new[] { 2, 3, 4, 1 }, ordered);
    }

    [Fact]
    public void Age_hours_until_close_or_now()
    {
        var created = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(2.5m, WarehouseTaskRules.AgeHours(created, created.AddMinutes(150), created.AddDays(3)));
        Assert.Equal(24m, WarehouseTaskRules.AgeHours(created, null, created.AddDays(1)));
        Assert.Equal(0m, WarehouseTaskRules.AgeHours(created, null, created.AddMinutes(-5)));
    }

    [Fact]
    public void Page_is_bounded()
    {
        Assert.Equal((0, 100), WarehouseTaskRules.Page(-3, 0));
        Assert.Equal((20, 200), WarehouseTaskRules.Page(20, 5000));
        Assert.Equal((0, 25), WarehouseTaskRules.Page(0, 25));
    }
}
