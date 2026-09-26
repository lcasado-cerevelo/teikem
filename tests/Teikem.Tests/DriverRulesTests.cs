using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P2): reglas puras del chofer (nombre, tope de paradas, licencia, certificación y zona).</summary>
public class DriverRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FullName_empty_is_required_error(string? raw)
    {
        var (value, error) = DriverRules.ValidateFullName(raw);
        Assert.Null(value);
        Assert.Equal("El nombre del chofer es obligatorio.", error);
    }

    [Fact]
    public void FullName_over_150_is_error_with_exact_message()
    {
        var (value, error) = DriverRules.ValidateFullName(new string('a', 151));
        Assert.Null(value);
        Assert.Equal("El nombre del chofer admite como máximo 150 caracteres.", error);
    }

    [Fact]
    public void FullName_150_is_valid_and_trimmed()
    {
        Assert.Equal((new string('a', 150), (string?)null), DriverRules.ValidateFullName(new string('a', 150)));
        Assert.Equal(("Juan del Pueblo", (string?)null), DriverRules.ValidateFullName("  Juan del Pueblo "));
    }

    [Fact]
    public void MaxStops_below_one_is_error_and_null_or_one_is_valid()
    {
        Assert.Equal("El tope de paradas debe ser mayor o igual a 1.", DriverRules.ValidateMaxStops(0));
        Assert.Equal("El tope de paradas debe ser mayor o igual a 1.", DriverRules.ValidateMaxStops(-3));
        Assert.Null(DriverRules.ValidateMaxStops(1));
        Assert.Null(DriverRules.ValidateMaxStops(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void LicenseNumber_empty_is_required_error(string? raw)
    {
        var (value, error) = DriverRules.ValidateLicenseNumber(raw);
        Assert.Null(value);
        Assert.Equal("El número de licencia es obligatorio.", error);
    }

    [Fact]
    public void LicenseNumber_over_60_is_error_and_60_is_valid()
    {
        Assert.Equal("El número de licencia admite como máximo 60 caracteres.", DriverRules.ValidateLicenseNumber(new string('9', 61)).Error);
        Assert.Equal((new string('9', 60), (string?)null), DriverRules.ValidateLicenseNumber(new string('9', 60)));
        Assert.Equal(("PR-123", (string?)null), DriverRules.ValidateLicenseNumber(" PR-123 "));
    }

    [Fact]
    public void CertNumber_is_optional_but_limited_to_60()
    {
        Assert.Equal(((string?)null, (string?)null), DriverRules.ValidateCertNumber("  "));
        Assert.Equal("El número de certificación admite como máximo 60 caracteres.", DriverRules.ValidateCertNumber(new string('x', 61)).Error);
        Assert.Equal(("HZ-1", (string?)null), DriverRules.ValidateCertNumber(" HZ-1 "));
    }

    [Fact]
    public void ZoneName_is_optional_but_limited_to_120()
    {
        Assert.Equal(((string?)null, (string?)null), DriverRules.ValidateZoneName(""));
        Assert.Equal("El nombre de la zona admite como máximo 120 caracteres.", DriverRules.ValidateZoneName(new string('z', 121)).Error);
        Assert.Equal(("Toa Baja · Bayamón", (string?)null), DriverRules.ValidateZoneName(" Toa Baja · Bayamón "));
    }
}
