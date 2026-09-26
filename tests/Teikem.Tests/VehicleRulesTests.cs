using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P1): reglas puras del vehículo (VIN, capacidades, odómetro, año del modelo y corrección manual del odómetro).</summary>
public class VehicleRulesTests
{
    [Fact]
    public void NormalizeVin_removes_spaces_and_uppercases()
    {
        var (vin, error) = VehicleRules.NormalizeVin("  1hgcm 82633a 004352 ");
        Assert.Null(error);
        Assert.Equal("1HGCM82633A004352", vin);
    }

    [Fact]
    public void NormalizeVin_empty_is_null_without_error()
    {
        var blank = VehicleRules.NormalizeVin("   ");
        Assert.Null(blank.Vin);
        Assert.Null(blank.Error);
        var none = VehicleRules.NormalizeVin(null);
        Assert.Null(none.Vin);
        Assert.Null(none.Error);
    }

    [Fact]
    public void NormalizeVin_rejects_more_than_40_characters()
    {
        var (vin, error) = VehicleRules.NormalizeVin(new string('A', 41));
        Assert.Null(vin);
        Assert.Equal("El VIN admite como máximo 40 caracteres.", error);

        Assert.Null(VehicleRules.NormalizeVin(new string('A', 40)).Error);
        // Los espacios no cuentan para el largo
        Assert.Null(VehicleRules.NormalizeVin(new string('A', 20) + "  " + new string('B', 20)).Error);
    }

    [Fact]
    public void ValidateCapacities_rejects_negative_weight_and_volume()
    {
        var errors = VehicleRules.ValidateCapacities(-1m, -0.5m, null);
        Assert.Contains(("maxWeightKg", "La capacidad no puede ser negativa."), errors);
        Assert.Contains(("maxVolumeM3", "La capacidad no puede ser negativa."), errors);
        Assert.Empty(VehicleRules.ValidateCapacities(0m, 0m, null));
    }

    [Fact]
    public void ValidateCapacities_max_stops_must_be_at_least_one()
    {
        var errors = VehicleRules.ValidateCapacities(null, null, 0);
        Assert.Equal(new[] { ("maxStops", "El tope de paradas debe ser mayor o igual a 1.") }, errors);
        Assert.Empty(VehicleRules.ValidateCapacities(null, null, 1));
        Assert.Empty(VehicleRules.ValidateCapacities(null, null, null));
    }

    [Fact]
    public void ValidateOdometer_rejects_negative()
    {
        Assert.Equal("El odómetro no puede ser negativo.", VehicleRules.ValidateOdometer(-0.1m));
        Assert.Null(VehicleRules.ValidateOdometer(0m));
        Assert.Null(VehicleRules.ValidateOdometer(null));
    }

    [Fact]
    public void ValidateModelYear_between_1900_and_next_year()
    {
        const int year = 2026;
        Assert.Equal("El año del modelo debe estar entre 1900 y 2027.", VehicleRules.ValidateModelYear(1899, year));
        Assert.Equal("El año del modelo debe estar entre 1900 y 2027.", VehicleRules.ValidateModelYear(year + 2, year));
        Assert.Null(VehicleRules.ValidateModelYear(year + 1, year));
        Assert.Null(VehicleRules.ValidateModelYear(1900, year));
        Assert.Null(VehicleRules.ValidateModelYear(null, year));
    }

    [Fact]
    public void ValidateManualOdometer_below_last_reading_has_exact_message()
    {
        var error = VehicleRules.ValidateManualOdometer(5000m, (6000.0m, new DateOnly(2026, 9, 20)));
        Assert.Equal("El odómetro no puede ser menor que la última lectura registrada (6000 km el 2026-09-20).", error);

        var withDecimal = VehicleRules.ValidateManualOdometer(100m, (6000.5m, new DateOnly(2026, 1, 2)));
        Assert.Equal("El odómetro no puede ser menor que la última lectura registrada (6000.5 km el 2026-01-02).", withDecimal);
    }

    [Fact]
    public void ValidateManualOdometer_equal_or_above_or_without_readings_is_valid()
    {
        Assert.Null(VehicleRules.ValidateManualOdometer(6000m, (6000m, new DateOnly(2026, 9, 20))));
        Assert.Null(VehicleRules.ValidateManualOdometer(6500m, (6000m, new DateOnly(2026, 9, 20))));
        Assert.Null(VehicleRules.ValidateManualOdometer(10m, null));
    }
}
