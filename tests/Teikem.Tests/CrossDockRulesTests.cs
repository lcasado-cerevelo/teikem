using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P9) — reglas puras del cruce de muelle (R20-R22, maestro L316-L320, D29), incluido el reparto FIFO.</summary>
public class CrossDockRulesTests
{
    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("La cantidad excede lo disponible para cruce de muelle (6).", CrossDockRules.Exceeds(6m));
        Assert.Equal("La cantidad excede lo disponible para cruce de muelle (2.5).", CrossDockRules.Exceeds(2.5m));
        Assert.Equal("La recepción de la línea todavía no se confirma; la mercancía se mueve después de confirmar.", CrossDockRules.ReceiptNotConfirmed);
        Assert.Equal("La asignación no recibió mercancía al confirmar; cancélela.", CrossDockRules.NothingConfirmed);
        Assert.Equal("La tarea de cruce de muelle se completa por la cantidad confirmada (4).", CrossDockRules.ExactQty(4m));
        Assert.Equal("La mercancía debe estar en la zona de staging del plan.", CrossDockRules.StagingZone);
        Assert.Equal("La orden no admite asignaciones (cancelada, entregada o dada de baja).", CrossDockRules.OrderNotShippable);
        Assert.Equal("El plan ya fue completado.", CrossDockRules.PlanNotOpen);
        Assert.Equal("Mueva o cancele las asignaciones pendientes antes de completar el plan.", CrossDockRules.CannotComplete);
    }

    [Theory]
    [InlineData(10, 0, 10)]
    [InlineData(10, 4, 6)]
    [InlineData(10, 10, 0)]
    [InlineData(8, 10, 0)]
    public void Allocatable_on_open_receipt(decimal baseQty, decimal allocated, decimal expected)
        => Assert.Equal(expected, CrossDockRules.AllocatableOpen(baseQty, allocated));

    [Theory]
    [InlineData(10, 0, 10, 10)]
    [InlineData(10, 0, 7, 7)]    // limitado por el putaway pendiente
    [InlineData(10, 4, 10, 6)]   // limitado por lo no comprometido
    [InlineData(10, 4, 3, 3)]
    [InlineData(10, 10, 5, 0)]
    [InlineData(10, 2, 0, 0)]    // ya se guardó todo
    public void Allocatable_on_confirmed_receipt(decimal received, decimal allocated, decimal pendingPutaway, decimal expected)
        => Assert.Equal(expected, CrossDockRules.AllocatableConfirmed(received, allocated, pendingPutaway));

    [Fact]
    public void Split_received_10_with_4_and_8_gives_4_and_6_with_shortage_2()
    {
        var r = CrossDockRules.Split(10m, new[] { new CrossDockSplitInput(1, 4m), new CrossDockSplitInput(2, 8m) });
        Assert.Equal(new[] { 4m, 6m }, r.Shares.Select(s => s.ConfirmedQty));
        Assert.Equal(new[] { 0m, 2m }, r.Shares.Select(s => s.ShortQty));
        Assert.Equal(0m, r.Remainder);
        Assert.Equal(10m, r.TotalConfirmed);
        Assert.Equal(2m, r.TotalShort);
        Assert.Equal(new[] { 1, 2 }, r.Shares.Select(s => s.AllocationId));
    }

    [Fact]
    public void Split_received_15_covers_all_and_leaves_3_for_putaway()
    {
        var r = CrossDockRules.Split(15m, new[] { new CrossDockSplitInput(1, 4m), new CrossDockSplitInput(2, 8m) });
        Assert.Equal(new[] { 4m, 8m }, r.Shares.Select(s => s.ConfirmedQty));
        Assert.All(r.Shares, s => Assert.Equal(0m, s.ShortQty));
        Assert.Equal(3m, r.Remainder);
    }

    [Fact]
    public void Split_received_0_is_all_shortage()
    {
        var r = CrossDockRules.Split(0m, new[] { new CrossDockSplitInput(1, 4m), new CrossDockSplitInput(2, 8m) });
        Assert.Equal(new[] { 0m, 0m }, r.Shares.Select(s => s.ConfirmedQty));
        Assert.Equal(new[] { 4m, 8m }, r.Shares.Select(s => s.ShortQty));
        Assert.Equal(0m, r.Remainder);
    }

    [Fact]
    public void Split_is_fifo_in_the_given_order()
    {
        // El orden de entrada (CreatedAtUtc, Id) manda: la primera se cubre completa.
        var r = CrossDockRules.Split(5m, new[] { new CrossDockSplitInput(9, 3m), new CrossDockSplitInput(2, 3m), new CrossDockSplitInput(5, 3m) });
        Assert.Equal(new[] { 3m, 2m, 0m }, r.Shares.Select(s => s.ConfirmedQty));
        Assert.Equal(new[] { 9, 2, 5 }, r.Shares.Select(s => s.AllocationId));
        Assert.Empty(CrossDockRules.Split(5m, Array.Empty<CrossDockSplitInput>()).Shares);
        Assert.Equal(5m, CrossDockRules.Split(5m, Array.Empty<CrossDockSplitInput>()).Remainder);
    }

    [Fact]
    public void Short_qty_is_zero_until_the_receipt_is_confirmed()
    {
        Assert.Equal(0m, CrossDockRules.ShortQty(6m, null));
        Assert.Equal(2m, CrossDockRules.ShortQty(6m, 4m));
        Assert.Equal(0m, CrossDockRules.ShortQty(6m, 6m));
    }

    [Fact]
    public void Plan_completes_only_without_planned_allocations()
    {
        Assert.True(CrossDockRules.CanComplete(Array.Empty<string?>()));
        Assert.True(CrossDockRules.CanComplete(new string?[] { AllocationStatuses.Moved, AllocationStatuses.Cancelled }));
        Assert.False(CrossDockRules.CanComplete(new string?[] { AllocationStatuses.Moved, AllocationStatuses.Planned }));
        Assert.True(CrossDockRules.IsPlanOpen(CrossDockStatuses.Open));
        Assert.True(CrossDockRules.IsPlanOpen(CrossDockStatuses.Allocated));
        Assert.False(CrossDockRules.IsPlanOpen(CrossDockStatuses.Completed));
    }

    [Fact]
    public void Order_shippable_rules()
    {
        Assert.True(CrossDockRules.IsOrderShippable(true, OrderStatuses.Confirmed, StageKinds.Pipeline));
        Assert.False(CrossDockRules.IsOrderShippable(false, OrderStatuses.Confirmed, StageKinds.Pipeline));
        Assert.False(CrossDockRules.IsOrderShippable(true, OrderStatuses.Cancelled, StageKinds.Terminal));
        Assert.False(CrossDockRules.IsOrderShippable(true, OrderStatuses.Delivered, StageKinds.Terminal));
        Assert.False(CrossDockRules.IsOrderShippable(true, "OTHER_END", StageKinds.Terminal));
    }

    [Fact]
    public void Quantity_validation()
    {
        Assert.Null(CrossDockRules.ValidateQuantity(2.5m, false));
        Assert.Equal(CrossDockRules.QtyPositive, CrossDockRules.ValidateQuantity(null, false));
        Assert.Equal(CrossDockRules.QtyPositive, CrossDockRules.ValidateQuantity(0m, false));
        Assert.Equal(CrossDockRules.QtyDecimals, CrossDockRules.ValidateQuantity(1.2345m, false));
        Assert.Equal(CrossDockRules.SerialInteger, CrossDockRules.ValidateQuantity(1.5m, true));
        Assert.Null(CrossDockRules.ValidateQuantity(2m, true));
    }

    [Fact]
    public void Staging_zone_types_and_putaway_reduction()
    {
        Assert.True(CrossDockRules.IsStagingZoneType(ZoneTypes.CrossDock));
        Assert.True(CrossDockRules.IsStagingZoneType(ZoneTypes.Staging));
        Assert.False(CrossDockRules.IsStagingZoneType(ZoneTypes.Picking));
        Assert.False(CrossDockRules.IsStagingZoneType(null));

        Assert.Equal(new[] { (0, 5m), (1, 2m) }, CrossDockRules.ReducePlan(new[] { 5m, 4m }, 7m));
        Assert.Equal(new[] { (0, 3m) }, CrossDockRules.ReducePlan(new[] { 5m, 4m }, 3m));
        Assert.Equal(new[] { (1, 4m) }, CrossDockRules.ReducePlan(new[] { 0m, 4m }, 9m));
    }
}
