using Xunit;
using OrderDispatchFlags = Teikem.Domain.Trips.OrderDispatchFlags;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: definiciones de 'en excepción' y 'pendiente de despacho' que usan los indicadores (DECISIÓN V5; flujo del Lote 106).</summary>
public class OrderDispatchFlagsTests
{
    [Theory]
    [InlineData("FAILED", true)]
    [InlineData("failed", true)]
    [InlineData("CANCELLED", false)]
    [InlineData("SUBMITTED", false)]
    [InlineData("DELIVERED", false)]
    [InlineData("IN_WAREHOUSE", false)]
    [InlineData(null, false)]
    public void IsException(string? status, bool expected) => Assert.Equal(expected, OrderDispatchFlags.IsException(status));

    [Fact]
    public void Pending_dispatch_statuses_are_submitted_through_assigned()
    {
        Assert.Equal(new[] { "SUBMITTED", "PICKUP", "UNASSIGNED", "ASSIGNED" }, OrderDispatchFlags.PendingDispatchStatuses);
        Assert.True(OrderDispatchFlags.IsPendingDispatch("ASSIGNED"));
        Assert.True(OrderDispatchFlags.IsPendingDispatch("SUBMITTED"));
        Assert.False(OrderDispatchFlags.IsPendingDispatch("IN_TRANSIT"));
        Assert.False(OrderDispatchFlags.IsPendingDispatch("FAILED"));
    }
}
