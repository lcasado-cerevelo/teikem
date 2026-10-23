using Microsoft.Extensions.Configuration;
using Teikem.Domain.Constants;
using Teikem.Domain.Trips;
using Teikem.Infrastructure.Geocoding;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-22 — ubicación de las paradas de una ruta con Google: llave de la dirección (no se paga dos veces lo mismo), mes del contador, precisión y tope mensual (a nivel de aplicación).</summary>
public class StopGeocodeRulesTests
{
    [Fact]
    public void The_same_address_written_a_little_differently_gets_the_same_key()
    {
        var a = StopGeocodeRules.AddressKey("Calle Muñoz Rivera #12", null, "San Germán", "PR", "00683");
        var b = StopGeocodeRules.AddressKey("  CALLE MUNOZ RIVERA 12 ", "", "SAN GERMAN", "pr", "00683");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Different_addresses_get_different_keys_and_an_empty_one_has_none()
    {
        Assert.NotEqual(StopGeocodeRules.AddressKey("Calle 1", null, "Ponce", "PR", "00716"), StopGeocodeRules.AddressKey("Calle 2", null, "Ponce", "PR", "00716"));
        Assert.NotEqual(StopGeocodeRules.AddressKey("Calle 1", null, "Ponce", "PR", "00716"), StopGeocodeRules.AddressKey("Calle 1", null, "Ponce", "PR", "00717"));
        Assert.Equal(string.Empty, StopGeocodeRules.AddressKey(null, " ", "", null, null));
    }

    [Fact]
    public void The_usage_month_is_the_calendar_month_in_utc()
    {
        Assert.Equal("2026-10", StopGeocodeRules.UsageMonth(new DateTime(2026, 10, 22, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal("2027-01", StopGeocodeRules.UsageMonth(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("HIGH", GeocodeAccuracies.Exact)]
    [InlineData("MEDIUM", GeocodeAccuracies.ZipCentroid)]
    [InlineData("LOW", GeocodeAccuracies.ZipCentroid)]
    [InlineData(null, GeocodeAccuracies.ZipCentroid)]
    public void Only_a_rooftop_result_is_exact_the_rest_is_drawn_as_approximate(string? confidence, string accuracy)
        => Assert.Equal(accuracy, StopGeocodeRules.AccuracyOf(confidence));

    [Theory]
    [InlineData(9000, 0, true)]
    [InlineData(9000, 8999, true)]
    [InlineData(9000, 9000, false)]
    [InlineData(0, 0, false)]    // tope en cero: no se consulta nada
    [InlineData(-5, 0, false)]
    public void The_monthly_cap_blocks_when_reached_and_zero_turns_the_search_off(int cap, int calls, bool allowed)
        => Assert.Equal(allowed, StopGeocodeRules.CapAllows(cap, calls));

    [Fact]
    public void The_default_cap_is_below_the_free_tier_and_comes_from_the_app_settings()
    {
        Assert.Equal(9000, StopGeocodeRules.DefaultMonthlyCap);
        Assert.Equal(StopGeocodeRules.DefaultMonthlyCap, new GoogleMapsOptions().MonthlyCap);
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleMaps:MonthlyCap"] = "1234" }).Build()
            .GetSection(GoogleMapsOptions.Section).Get<GoogleMapsOptions>()!;
        Assert.Equal(1234, configured.MonthlyCap);
    }
}
