using Teikem.Domain.Trips;
using Xunit;

namespace Teikem.Tests;

/// <summary>Reglas puras del seguimiento de ruta del chofer (2026-10-23).</summary>
public class DriverTrackingRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(18.4, -66.1, true)]
    [InlineData(0, 0, false)]            // un teléfono sin fix a veces manda ceros
    [InlineData(91, -66, false)]
    [InlineData(18, -181, false)]
    [InlineData(double.NaN, -66, false)]
    public void Coordinates_must_be_in_range_and_not_the_null_island(double lat, double lng, bool ok)
        => Assert.Equal(ok, DriverTrackingRules.ValidCoordinate(lat, lng));

    [Fact]
    public void A_point_is_acceptable_unless_it_is_in_the_future_or_older_than_the_retention()
    {
        Assert.True(DriverTrackingRules.Acceptable(18.4, -66.1, Now.AddMinutes(-3), Now, 10));
        Assert.True(DriverTrackingRules.Acceptable(18.4, -66.1, Now.AddMinutes(4), Now, 10));      // tolerancia del reloj del teléfono
        Assert.False(DriverTrackingRules.Acceptable(18.4, -66.1, Now.AddMinutes(10), Now, 10));
        Assert.False(DriverTrackingRules.Acceptable(18.4, -66.1, Now.AddDays(-11), Now, 10));
        Assert.True(DriverTrackingRules.Acceptable(18.4, -66.1, Now.AddDays(-9), Now, 10));
    }

    [Theory]
    [InlineData("STARTED", true)]
    [InlineData("stopped_by_driver", true)]
    [InlineData(" PERMISSION_LOST ", true)]
    [InlineData("RESUMED", true)]
    [InlineData("BATTERY", false)]
    [InlineData(null, false)]
    public void Event_types_are_a_closed_list(string? type, bool ok) => Assert.Equal(ok, DriverTrackingRules.ValidEventType(type));
}
