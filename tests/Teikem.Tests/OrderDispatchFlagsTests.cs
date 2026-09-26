using Xunit;
using OrderDispatchFlags = Teikem.Domain.Trips.OrderDispatchFlags;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: definiciones de 'en excepción' y 'pendiente de despacho' que usan los indicadores (DECISIÓN V5).</summary>
public class OrderDispatchFlagsTests
{
    [Theory]
    [InlineData("ON_HOLD", true)]
    [InlineData("PARTIAL", true)]
    [InlineData("FAILED", true)]
    [InlineData("on_hold", true)]
    [InlineData("CANCELLED", false)]
    [InlineData("CONFIRMED", false)]
    [InlineData("DELIVERED", false)]
    [InlineData(null, false)]
    public void IsException(string? status, bool expected) => Assert.Equal(expected, OrderDispatchFlags.IsException(status));

    [Fact]
    public void Pending_dispatch_statuses_are_confirmed_through_planned()
    {
        Assert.Equal(new[] { "CONFIRMED", "PICKUP", "INBOUND", "PLANNED" }, OrderDispatchFlags.PendingDispatchStatuses);
        Assert.True(OrderDispatchFlags.IsPendingDispatch("PLANNED"));
        Assert.False(OrderDispatchFlags.IsPendingDispatch("IN_TRANSIT"));
        Assert.False(OrderDispatchFlags.IsPendingDispatch("DRAFT"));
    }
}
