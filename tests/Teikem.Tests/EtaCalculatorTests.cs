using Xunit;
using EtaCalculator = Teikem.Domain.Trips.EtaCalculator;
using EtaSettings = Teikem.Domain.Trips.EtaSettings;
using EtaStopInput = Teikem.Domain.Trips.EtaStopInput;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: ETA sin motor de ruteo (haversine × 1.3 a 35 km/h; 15 min por tramo sin coordenadas; ventanas y servicio).</summary>
public class EtaCalculatorTests
{
    private static readonly DateTime Start = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly (double Lat, double Lng) SanJuan = (18.4655, -66.1057);
    private static readonly (double Lat, double Lng) Bayamon = (18.3985, -66.1553);

    private static EtaStopInput Stop((double Lat, double Lng)? p = null, DateTime? from = null, DateTime? to = null, int service = 10)
        => new(p?.Lat, p?.Lng, from, to, service);

    [Fact]
    public void Empty_route_has_no_totals()
    {
        var r = EtaCalculator.Calculate(Start, Array.Empty<EtaStopInput>());
        Assert.Empty(r.Stops);
        Assert.Null(r.TotalDistanceKm);
        Assert.Null(r.TotalDurationMin);
        Assert.Null(r.EndUtc);
    }

    [Fact]
    public void First_leg_uses_the_default_and_has_no_distance()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(SanJuan) });
        var s = Assert.Single(r.Stops);
        Assert.Null(s.DistanceFromPrevKm);
        Assert.Equal(15, s.DurationFromPrevMin);
        Assert.Equal(Start.AddMinutes(15), s.ArrivalUtc);
        Assert.Equal(Start.AddMinutes(25), s.DepartureUtc);
        Assert.Null(r.TotalDistanceKm);
        Assert.Equal(25, r.TotalDurationMin);
        Assert.Equal(Start.AddMinutes(25), r.EndUtc);
    }

    [Fact]
    public void San_Juan_to_Bayamon_is_about_11_8_km_and_21_minutes()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(SanJuan), Stop(Bayamon) });
        var leg = r.Stops[1];
        Assert.NotNull(leg.DistanceFromPrevKm);
        Assert.InRange(leg.DistanceFromPrevKm!.Value, 11.6m, 12.0m);
        Assert.Equal(21, leg.DurationFromPrevMin);
        Assert.Equal(decimal.Round(leg.DistanceFromPrevKm.Value, 3), leg.DistanceFromPrevKm.Value); // 3 decimales
        Assert.Equal(leg.DistanceFromPrevKm, r.TotalDistanceKm);
        Assert.Equal(Start.AddMinutes(15 + 10 + 21), leg.ArrivalUtc);
    }

    [Fact]
    public void Legs_without_points_take_15_minutes_and_total_distance_is_null()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(), Stop(), Stop(SanJuan) });
        Assert.All(r.Stops, s => Assert.Equal(15, s.DurationFromPrevMin));
        Assert.All(r.Stops, s => Assert.Null(s.DistanceFromPrevKm));
        Assert.Null(r.TotalDistanceKm);
        Assert.Equal(3 * (15 + 10), r.TotalDurationMin);
    }

    [Fact]
    public void Without_start_there_are_no_times_but_legs_are_computed()
    {
        var r = EtaCalculator.Calculate(null, new[] { Stop(SanJuan), Stop(Bayamon) });
        Assert.All(r.Stops, s => Assert.Null(s.ArrivalUtc));
        Assert.All(r.Stops, s => Assert.Null(s.DepartureUtc));
        Assert.All(r.Stops, s => Assert.False(s.Late));
        Assert.Null(r.EndUtc);
        Assert.Equal(21, r.Stops[1].DurationFromPrevMin);
        Assert.NotNull(r.TotalDistanceKm);
    }

    [Fact]
    public void Early_arrival_waits_for_the_window_start()
    {
        var windowStart = Start.AddHours(2);
        var r = EtaCalculator.Calculate(Start, new[] { Stop(from: windowStart, to: Start.AddHours(4), service: 20) });
        var s = r.Stops[0];
        Assert.Equal(windowStart, s.ArrivalUtc);
        Assert.Equal(windowStart.AddMinutes(20), s.DepartureUtc);
        Assert.False(s.Late);
        Assert.Equal(120 + 20, r.TotalDurationMin); // 15 de tramo + 105 de espera + 20 de servicio
    }

    [Fact]
    public void Arrival_after_the_window_end_is_late()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(to: Start.AddMinutes(10)) });
        Assert.True(r.Stops[0].Late);
        var onTime = EtaCalculator.Calculate(Start, new[] { Stop(to: Start.AddMinutes(15)) });
        Assert.False(onTime.Stops[0].Late); // llegar justo al fin de la ventana no es tarde
    }

    [Fact]
    public void Custom_settings_are_respected()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(SanJuan), Stop(Bayamon) }, new EtaSettings(70, 1.0, 5));
        Assert.Equal(5, r.Stops[0].DurationFromPrevMin);
        Assert.InRange(r.Stops[1].DistanceFromPrevKm!.Value, 8.9m, 9.3m);
        Assert.Equal(8, r.Stops[1].DurationFromPrevMin); // ceil(9.1 / 70 × 60)
    }

    [Fact]
    public void Very_short_legs_take_at_least_one_minute()
    {
        var r = EtaCalculator.Calculate(Start, new[] { Stop(SanJuan), Stop((SanJuan.Lat + 0.00001, SanJuan.Lng)) });
        Assert.Equal(1, r.Stops[1].DurationFromPrevMin);
    }

    [Fact]
    public void Calculation_is_deterministic()
    {
        var stops = new[] { Stop(SanJuan), Stop(Bayamon, Start.AddHours(1)), Stop(), Stop(SanJuan, to: Start.AddMinutes(30)) };
        var a = EtaCalculator.Calculate(Start, stops);
        var b = EtaCalculator.Calculate(Start, stops);
        Assert.Equal(a.Stops, b.Stops);
        Assert.Equal(a.TotalDistanceKm, b.TotalDistanceKm);
        Assert.Equal(a.EndUtc, b.EndUtc);
    }
}
