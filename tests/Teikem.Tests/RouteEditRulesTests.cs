using Xunit;
using OptimizationSnapshot = Teikem.Domain.Trips.OptimizationSnapshot;
using RouteEditRules = Teikem.Domain.Trips.RouteEditRules;

namespace Teikem.Tests;

/// <summary>Lote 5 / P3: reglas puras de edición de la ruta (pin manual y optimización en tres fases).</summary>
public class RouteEditRulesTests
{
    // ---------------------------------------------------------------- ValidateLocation

    [Theory]
    [InlineData(null, null, "lat")]
    [InlineData(18.4, null, "lng")]
    [InlineData(null, -66.1, "lat")]
    public void ValidateLocation_missing_coordinate_asks_for_both(double? lat, double? lng, string field)
    {
        var error = RouteEditRules.ValidateLocation(lat, lng);
        Assert.NotNull(error);
        Assert.Equal(field, error!.Value.Field);
        Assert.Equal("Indique la latitud y la longitud.", error.Value.Message);
    }

    [Theory]
    [InlineData(91d)]
    [InlineData(-90.0001d)]
    [InlineData(double.NaN)]
    public void ValidateLocation_latitude_out_of_range(double lat)
    {
        var error = RouteEditRules.ValidateLocation(lat, 0d);
        Assert.NotNull(error);
        Assert.Equal(("lat", "La latitud debe estar entre -90 y 90."), error!.Value);
    }

    [Theory]
    [InlineData(-181d)]
    [InlineData(180.5d)]
    [InlineData(double.PositiveInfinity)]
    public void ValidateLocation_longitude_out_of_range(double lng)
    {
        var error = RouteEditRules.ValidateLocation(0d, lng);
        Assert.NotNull(error);
        Assert.Equal(("lng", "La longitud debe estar entre -180 y 180."), error!.Value);
    }

    [Theory]
    [InlineData(18.4655, -66.1057)]
    [InlineData(90d, 180d)]
    [InlineData(-90d, -180d)]
    [InlineData(0d, 0d)]
    public void ValidateLocation_valid_coordinates(double lat, double lng)
        => Assert.Null(RouteEditRules.ValidateLocation(lat, lng));

    // ---------------------------------------------------------------- IsStale

    private static readonly byte[] Rv1 = { 0, 0, 0, 0, 0, 0, 0x07, 0xD1 };
    private static readonly byte[] Rv2 = { 0, 0, 0, 0, 0, 0, 0x07, 0xD2 };

    [Fact]
    public void IsStale_false_when_route_and_rowversion_are_the_same()
        => Assert.False(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Rv1), 5, (byte[])Rv1.Clone(), true));

    [Fact]
    public void IsStale_true_when_rowversion_changed()
        => Assert.True(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Rv1), 5, Rv2, true));

    [Theory]
    [InlineData(6)]
    [InlineData(null)]
    public void IsStale_true_when_active_route_changed(int? currentRouteId)
        => Assert.True(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Rv1), currentRouteId, Rv1, true));

    [Fact]
    public void IsStale_true_when_a_route_appeared()
        => Assert.True(RouteEditRules.IsStale(new OptimizationSnapshot(10, null, Rv1), 7, Rv1, true));

    [Fact]
    public void IsStale_true_when_the_trip_is_no_longer_editable()
        => Assert.True(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Rv1), 5, Rv1, false));

    [Fact]
    public void IsStale_treats_missing_rowversion_as_empty()
    {
        Assert.False(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Array.Empty<byte>()), 5, null, true));
        Assert.True(RouteEditRules.IsStale(new OptimizationSnapshot(10, 5, Rv1), 5, null, true));
    }

    // ---------------------------------------------------------------- mensajes y constantes

    [Fact]
    public void Messages_are_the_documented_ones()
    {
        Assert.Equal("La ruta cambió mientras se optimizaba; vuelva a optimizar.", RouteEditRules.StaleMessage);
        Assert.Equal("El optimizador no pudo calcular la ruta; la corrida quedó registrada con error.", RouteEditRules.EngineErrorMessage);
        Assert.Equal("La ruta no tiene paradas que optimizar.", RouteEditRules.NoStopsMessage);
        Assert.Equal("Descartada: la ruta cambió durante el cálculo.", RouteEditRules.StaleRunErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(60), RouteEditRules.EngineTimeout);
        Assert.Equal("Optimización #12", RouteEditRules.OptimizationComment(12));
        Assert.Equal("Ruta optimizada (versión 3)", RouteEditRules.TripOptimizedComment(3));
    }

    [Fact]
    public void TrimRunError_caps_at_4000_and_never_returns_empty()
    {
        Assert.Equal(4000, RouteEditRules.TrimRunError(new string('x', 5000)).Length);
        Assert.Equal("boom", RouteEditRules.TrimRunError("  boom "));
        Assert.Equal(RouteEditRules.EngineErrorMessage, RouteEditRules.TrimRunError(null));
        Assert.Equal(RouteEditRules.EngineErrorMessage, RouteEditRules.TrimRunError("   "));
    }
}
