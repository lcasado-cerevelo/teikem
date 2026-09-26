using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P5): eficiencia de combustible (km/L, costo/km, resumen) y odómetro monótono por fecha.</summary>
public class FuelEfficiencyTests
{
    private static readonly DateTime D1 = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = new(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D3 = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private static FuelReading[] ThreeFills() => new[]
    {
        new FuelReading(1, D1, 5200m, 40m, 60m),
        new FuelReading(2, D2, 5600m, 40m, 60m),
        new FuelReading(3, D3, 6000m, 40m, 64m),
    };

    [Fact]
    public void Compute_three_fills_gives_km_per_liter_and_cost_per_km()
    {
        var result = FuelEfficiency.Compute(ThreeFills());

        var first = result.ById[1];
        Assert.Null(first.DistanceKm);
        Assert.Null(first.KmPerLiter);
        Assert.Null(first.CostPerKm);

        var second = result.ById[2];
        Assert.Equal(400m, second.DistanceKm);
        Assert.Equal(10m, second.KmPerLiter);
        Assert.Equal(0.15m, second.CostPerKm);

        var third = result.ById[3];
        Assert.Equal(400m, third.DistanceKm);
        Assert.Equal(10m, third.KmPerLiter);
        Assert.Equal(0.16m, third.CostPerKm);
    }

    [Fact]
    public void Compute_unordered_input_gives_same_result()
    {
        var ordered = FuelEfficiency.Compute(ThreeFills());
        var shuffled = FuelEfficiency.Compute(Enumerable.Reverse(ThreeFills()));

        Assert.Equal(new[] { 1, 2, 3 }, shuffled.Rows.Select(r => r.Id));
        foreach (var id in new[] { 1, 2, 3 }) Assert.Equal(ordered.ById[id], shuffled.ById[id]);
        Assert.Equal(ordered.Summary, shuffled.Summary);
    }

    [Fact]
    public void Compute_same_date_orders_by_id()
    {
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(8, D1, 5300m, 10m, 20m),
            new FuelReading(7, D1, 5200m, 10m, 20m),
        });
        Assert.Equal(new[] { 7, 8 }, result.Rows.Select(r => r.Id));
        Assert.Equal(100m, result.ById[8].DistanceKm);
    }

    [Fact]
    public void Compute_fill_without_odometer_is_null_and_next_uses_last_with_odometer_adding_its_fuel()
    {
        // Tramo 5200 → 5600 (400 km): consumo = 20 L/$30 de la carga sin odómetro + 40 L/$60 de la que cierra el tramo.
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D2, null, 20m, 30m),
            new FuelReading(3, D3, 5600m, 40m, 60m),
        });

        Assert.Null(result.ById[2].DistanceKm);
        Assert.Null(result.ById[2].KmPerLiter);
        Assert.Null(result.ById[2].CostPerKm);
        Assert.Equal(400m, result.ById[3].DistanceKm);
        Assert.Equal(60m, result.ById[3].ConsumedLiters);
        Assert.Equal(90m, result.ById[3].ConsumedCost);
        Assert.Equal(6.67m, result.ById[3].KmPerLiter);
        Assert.Equal(0.225m, result.ById[3].CostPerKm);

        Assert.Equal(3, result.Summary.Fills);
        Assert.Equal(100m, result.Summary.TotalLiters);
        Assert.Equal(150m, result.Summary.TotalCost);
        Assert.Equal(400m, result.Summary.DistanceKm);
        Assert.Equal(6.67m, result.Summary.KmPerLiter);
        Assert.Equal(0.225m, result.Summary.CostPerKm);
    }

    [Fact]
    public void Compute_fuel_before_first_odometer_is_not_attributed_and_invalid_leg_discards_pending()
    {
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, null, 25m, 35m),     // antes de la primera lectura: no se atribuye a ningún tramo
            new FuelReading(2, D2, 5200m, 40m, 60m),
            new FuelReading(3, D3, 5600m, 40m, 60m),
        });
        Assert.Equal(40m, result.ById[3].ConsumedLiters);
        Assert.Equal(10m, result.ById[3].KmPerLiter);

        var invalid = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D2, null, 20m, 30m),
            new FuelReading(3, D3, 5200m, 40m, 60m),    // distancia 0: tramo inválido, lo pendiente se descarta
            new FuelReading(4, D3.AddDays(1), 5600m, 40m, 60m),
        });
        Assert.Null(invalid.ById[3].DistanceKm);
        Assert.Equal(40m, invalid.ById[4].ConsumedLiters);
        Assert.Equal(10m, invalid.ById[4].KmPerLiter);
    }

    [Fact]
    public void Compute_distance_zero_or_negative_is_null()
    {
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D2, 5200m, 40m, 60m),
            new FuelReading(3, D3, 5100m, 40m, 60m),
        });

        Assert.Null(result.ById[2].DistanceKm);
        Assert.Null(result.ById[2].KmPerLiter);
        Assert.Null(result.ById[3].DistanceKm);
        Assert.Null(result.ById[3].CostPerKm);
        Assert.Null(result.Summary.KmPerLiter);
    }

    [Fact]
    public void Compute_rounds_to_2_and_4_decimals()
    {
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, 1000m, 30m, 50m),
            new FuelReading(2, D2, 1333.3m, 33.333m, 55.55m),
        });

        var row = result.ById[2];
        Assert.Equal(333.3m, row.DistanceKm);
        Assert.Equal(10.00m, row.KmPerLiter);          // 333.3 / 33.333 = 9.99910… → 10.00
        Assert.Equal(0.1667m, row.CostPerKm);          // 55.55 / 333.3 = 0.166666… → 0.1667
    }

    [Fact]
    public void Summary_is_sum_of_distance_over_sum_of_liters()
    {
        var result = FuelEfficiency.Compute(new[]
        {
            new FuelReading(1, D1, 5000m, 50m, 70m),
            new FuelReading(2, D2, 5400m, 40m, 60m),   // 400 km, 40 L
            new FuelReading(3, D3, 5700m, 20m, 40m),   // 300 km, 20 L
        });

        var s = result.Summary;
        Assert.Equal(3, s.Fills);
        Assert.Equal(110m, s.TotalLiters);
        Assert.Equal(170m, s.TotalCost);
        Assert.Equal(700m, s.DistanceKm);
        Assert.Equal(11.67m, s.KmPerLiter);             // 700 / 60
        Assert.Equal(0.1429m, s.CostPerKm);             // 100 / 700
    }

    [Fact]
    public void Summary_of_three_standard_fills_is_10_km_per_liter()
    {
        var s = FuelEfficiency.Compute(ThreeFills()).Summary;
        Assert.Equal(10m, s.KmPerLiter);
        Assert.Equal(800m, s.DistanceKm);
        Assert.Equal(0.155m, s.CostPerKm);              // 124 / 800
    }

    [Fact]
    public void Summary_without_distance_has_null_efficiency()
    {
        var s = FuelEfficiency.Summarize(Array.Empty<FuelEfficiencyRow>());
        Assert.Equal(0, s.Fills);
        Assert.Null(s.DistanceKm);
        Assert.Null(s.KmPerLiter);
        Assert.Null(s.CostPerKm);
    }

    [Fact]
    public void ValidateOdometer_lower_than_previous_fill_gives_exact_message()
    {
        var readings = new[] { new FuelReading(1, D3, 5200m, 40m, 60m) };
        var error = FuelEfficiency.ValidateOdometer(readings, D3.AddDays(1), 5100m, null);
        Assert.Equal("La lectura de odómetro (5,100 km) es menor que la de una carga anterior del mismo vehículo (5,200 km el 2026-09-20).", error);
    }

    [Fact]
    public void ValidateOdometer_higher_than_later_fill_gives_message()
    {
        var readings = new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D3, 5600m, 40m, 60m),
        };
        var error = FuelEfficiency.ValidateOdometer(readings, D2, 5700m, null);
        Assert.Equal("La lectura de odómetro (5,700 km) es mayor que la de una carga posterior del mismo vehículo (5,600 km el 2026-09-20).", error);
    }

    [Fact]
    public void ValidateOdometer_between_neighbors_equal_or_without_reading_is_valid()
    {
        var readings = new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D3, 5600m, 40m, 60m),
        };
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, 5400m, null));
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, 5200m, null));
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, 5600m, null));
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, null, null));
        Assert.Null(FuelEfficiency.ValidateOdometer(Array.Empty<FuelReading>(), D2, 1m, null));
    }

    [Fact]
    public void ValidateOdometer_on_edit_excludes_own_row()
    {
        var readings = new[]
        {
            new FuelReading(1, D1, 5200m, 40m, 60m),
            new FuelReading(2, D2, 5600m, 40m, 60m),
            new FuelReading(3, D3, 6000m, 40m, 64m),
        };
        // Bajar la carga 2 a 5300 es válido: su propia lectura (5600) no cuenta.
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, 5300m, 2));
        // Pero no por debajo de la anterior ni por encima de la posterior.
        Assert.NotNull(FuelEfficiency.ValidateOdometer(readings, D2, 5100m, 2));
        Assert.NotNull(FuelEfficiency.ValidateOdometer(readings, D2, 6100m, 2));
    }

    [Fact]
    public void ValidateOdometer_same_date_new_fill_goes_after_existing()
    {
        var readings = new[] { new FuelReading(5, D2, 5600m, 40m, 60m) };
        // Nueva carga (sin id) a la misma fecha: va después → no puede ser menor.
        Assert.NotNull(FuelEfficiency.ValidateOdometer(readings, D2, 5500m, null));
        Assert.Null(FuelEfficiency.ValidateOdometer(readings, D2, 5700m, null));
        // Al editar una carga de id menor a la misma fecha: va antes → no puede ser mayor.
        Assert.NotNull(FuelEfficiency.ValidateOdometer(readings, D2, 5700m, 4));
    }

    [Fact]
    public void FormatKm_uses_thousands_separator_and_trims_decimals()
    {
        Assert.Equal("5,100", FuelEfficiency.FormatKm(5100.0m));
        Assert.Equal("5,100.5", FuelEfficiency.FormatKm(5100.5m));
        Assert.Equal("0", FuelEfficiency.FormatKm(0m));
    }
}
