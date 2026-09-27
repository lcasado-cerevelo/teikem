using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 (P7, D12) — guarda de borrado en Órdenes: una orden nacida de una recolección (origen PICK_BATCH) solo la borra
/// 'Recolección y empaque' (OrderDeletionOptions con PICK_BATCH), que además restaura el inventario. Las demás órdenes no
/// cambian de regla.
/// </summary>
public class OrderPickBatchGuardTests
{
    [Fact]
    public void Order_from_pick_batch_cannot_be_deleted_from_orders_without_the_option()
    {
        Assert.False(OrderRules.CanDeleteFromOrders(EntityTypes.PickBatch, null));
        Assert.False(OrderRules.CanDeleteFromOrders("pick_batch", null));
        Assert.False(OrderRules.CanDeleteFromOrders(EntityTypes.PickBatch, "IMPORT_BATCH"));
    }

    [Fact]
    public void Pick_batch_service_option_allows_the_delete()
    {
        Assert.True(OrderRules.CanDeleteFromOrders(EntityTypes.PickBatch, EntityTypes.PickBatch));
        Assert.True(OrderRules.CanDeleteFromOrders(EntityTypes.PickBatch, "pick_batch"));
    }

    [Fact]
    public void Orders_with_other_or_no_origin_keep_the_usual_rule()
    {
        Assert.True(OrderRules.CanDeleteFromOrders(null, null));
        Assert.True(OrderRules.CanDeleteFromOrders("IMPORT_BATCH", null));
        Assert.True(OrderRules.CanDeleteFromOrders(null, EntityTypes.PickBatch));
    }

    [Fact]
    public void Default_deletion_options_allow_no_source()
    {
        var options = new OrderDeletionOptions();
        Assert.Null(options.AllowedSourceEntityType);
        Assert.False(OrderRules.CanDeleteFromOrders(EntityTypes.PickBatch, options.AllowedSourceEntityType));
        Assert.Equal(EntityTypes.PickBatch, new OrderDeletionOptions(EntityTypes.PickBatch).AllowedSourceEntityType);
    }

    [Fact]
    public void Delete_message_names_the_pick_batch_number()
    {
        Assert.Equal("Esta orden nació de la recolección EMP-00042; elimínela desde Recolección y empaque para restaurar el inventario.",
            OrderRules.PickBatchOrderDeleteMessage("EMP-00042"));
        Assert.Equal("PICK_BATCH", EntityTypes.PickBatch);
    }
}
