using Teikem.Domain.Tenancy;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 243 — texto de dinero, fechas, horas y teléfonos con el formato de la compañía (TenantFormatText, lógica pura).</summary>
public sealed class TenantFormatTextTests
{
    private static readonly TenantFormat Pr = TenantFormatRules.Defaults(TenantFormatRules.PuertoRico);

    [Theory]
    [InlineData(1234.5, "$1,234.50")]
    [InlineData(0, "$0.00")]
    [InlineData(1234567.891, "$1,234,567.89")]
    [InlineData(0.005, "$0.01")]          // mitad hacia afuera
    [InlineData(-5, "-$5.00")]
    [InlineData(999.999, "$1,000.00")]
    public void Dinero_con_el_simbolo_antes_y_dos_decimales(double amount, string expected)
        => Assert.Equal(expected, TenantFormatText.Money((decimal)amount, Pr));

    [Fact]
    public void Dinero_con_el_simbolo_despues_y_separadores_europeos()
    {
        var eu = Pr with { CurrencySymbol = "€", CurrencySymbolPosition = TenantFormatRules.SymbolPositions.After, ThousandsSeparator = ".", DecimalSeparator = "," };
        Assert.Equal("1.234,50 €", TenantFormatText.Money(1234.5m, eu));
        Assert.Equal("-12,00 €", TenantFormatText.Money(-12m, eu));
    }

    [Fact]
    public void Dinero_con_cero_y_tres_decimales_y_espacio_de_miles()
    {
        Assert.Equal("$1,235", TenantFormatText.Money(1234.5m, Pr with { CurrencyDecimals = 0 }));
        Assert.Equal("$1 234.500", TenantFormatText.Money(1234.5m, Pr with { CurrencyDecimals = 3, ThousandsSeparator = " " }));
        Assert.Equal("$12", TenantFormatText.Money(12m, Pr with { CurrencyDecimals = 0 }));
    }

    [Fact]
    public void Cantidades_sin_ceros_de_mas()
    {
        Assert.Equal("4", TenantFormatText.Quantity(4.000m, Pr));
        Assert.Equal("2.5", TenantFormatText.Quantity(2.50m, Pr));
        Assert.Equal("1,250.125", TenantFormatText.Quantity(1250.125m, Pr));
        Assert.Equal("2,5", TenantFormatText.Quantity(2.5m, Pr with { ThousandsSeparator = ".", DecimalSeparator = "," }));
    }

    [Theory]
    [InlineData("MDY", "/", "11/24/2026")]
    [InlineData("DMY", "/", "24/11/2026")]
    [InlineData("YMD", "-", "2026-11-24")]
    [InlineData("DMY", ".", "24.11.2026")]
    public void Fechas_en_el_orden_y_con_el_separador_de_la_compania(string order, string separator, string expected)
        => Assert.Equal(expected, TenantFormatText.Date(new DateOnly(2026, 11, 24), Pr with { DateOrder = order, DateSeparator = separator }));

    [Fact]
    public void Una_fecha_de_un_digito_lleva_ceros()
        => Assert.Equal("03/05/2026", TenantFormatText.Date(new DateOnly(2026, 3, 5), Pr));

    [Theory]
    [InlineData(15, 5, 12, "3:05 PM")]
    [InlineData(0, 0, 12, "12:00 AM")]
    [InlineData(12, 30, 12, "12:30 PM")]
    [InlineData(9, 7, 12, "9:07 AM")]
    [InlineData(15, 5, 24, "15:05")]
    [InlineData(0, 0, 24, "00:00")]
    public void Hora_en_12_y_24_horas(int h, int m, byte format, string expected)
        => Assert.Equal(expected, TenantFormatText.Time(new TimeOnly(h, m), Pr with { TimeFormat = format }));

    [Fact]
    public void Fecha_y_hora_desde_UTC_en_la_zona_de_la_compania()
    {
        // 2026-11-25 02:30 UTC es el 24/11 a las 10:30 PM en Puerto Rico (UTC−4)
        Assert.Equal("11/24/2026 10:30 PM", TenantFormatText.DateTimeFromUtc(new DateTime(2026, 11, 25, 2, 30, 0, DateTimeKind.Utc), Pr));
        Assert.Equal("24/11/2026 22:30", TenantFormatText.DateTimeFromUtc(new DateTime(2026, 11, 25, 2, 30, 0, DateTimeKind.Utc), Pr with { DateOrder = "DMY", TimeFormat = 24 }));
    }

    [Fact]
    public void Telefono_con_la_mascara_y_si_no_cuadra_tal_cual()
    {
        Assert.Equal("(787) 555-1234", TenantFormatText.Phone("7875551234", Pr));
        Assert.Equal("555-1234", TenantFormatText.Phone("555-1234", Pr));      // no son solo dígitos
        Assert.Equal("78755512", TenantFormatText.Phone("78755512", Pr));      // otra cantidad de dígitos
        Assert.Equal(string.Empty, TenantFormatText.Phone(null, Pr));
    }
}
