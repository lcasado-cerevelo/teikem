using Teikem.Domain.Billing;
using Xunit;
using R = Teikem.Domain.Sales.SalesChangeRules;

namespace Teikem.Tests;

/// <summary>Lote 249 (P2) — IVU manual por línea: el importe sale del porcentaje con la fórmula del ajuste de factura; validación del porcentaje y del motivo.</summary>
public sealed class SalesChangeTaxRulesTests
{
    [Theory]
    [InlineData(3.0, 8.00, 11.5, 2.76)]
    [InlineData(3.0, 8.00, 1.0, 0.24)]
    [InlineData(3.0, 8.00, 0.0, 0.00)]
    [InlineData(1.0, 1.25, 10.0, 0.13)]      // 0.125: la mitad hacia arriba
    [InlineData(2.5, 3.3333, 7.0, 0.58)]   // 8.33325 → 8.3333 × 7 % = 0.583331
    public void ManualTax_con_cifras_exactas(double qty, double price, double pct, double expected)
        => Assert.Equal((decimal)expected, R.ManualTax((decimal)qty, (decimal)price, (decimal)pct));

    [Fact]
    public void Al_0_por_ciento_la_linea_queda_exenta()
    {
        Assert.Equal(0m, R.ManualTax(3m, 8m, 0m));
        Assert.Null(R.TaxPctError(0m));
    }

    [Theory]
    [InlineData(1.0, 1.25, 10.0)]
    [InlineData(3.0, 8.0, 11.5)]
    [InlineData(7.125, 19.9999, 11.5)]
    [InlineData(1000.0, 0.0001, 4.5)]
    [InlineData(12.5, 3.47, 10.5)]
    public void La_formula_coincide_con_la_del_ajuste_de_factura(double qty, double price, double pct)
    {
        var (q, p, t) = ((decimal)qty, (decimal)price, (decimal)pct);
        // BillingService.AdjustLineAsync: Money(Round(cantidad × precio, 4) × % / 100)
        var billing = BillingRules.Money(Math.Round(q * p, 4, MidpointRounding.AwayFromZero) * t / 100m);
        Assert.Equal(billing, R.ManualTax(q, p, t));
    }

    [Fact]
    public void Errores_del_porcentaje()
    {
        Assert.Equal("Indique el porcentaje de IVU de la línea.", R.TaxPctError(null));
        Assert.Equal("El IVU de la línea debe estar entre 0 y 100 %.", R.TaxPctError(-0.01m));
        Assert.Equal("El IVU de la línea debe estar entre 0 y 100 %.", R.TaxPctError(100.0001m));
        Assert.Equal("El IVU de la línea admite hasta 4 decimales.", R.TaxPctError(11.12345m));
        Assert.Null(R.TaxPctError(100m));
        Assert.Null(R.TaxPctError(11.1234m));
    }

    [Fact]
    public void Motivo_del_IVU_manual()
    {
        Assert.Equal("Indique el motivo del IVU manual.", R.TaxNoteError(null));
        Assert.Equal("Indique el motivo del IVU manual.", R.TaxNoteError("   "));
        Assert.Equal("El motivo del IVU manual no puede pasar de 250 caracteres.", R.TaxNoteError(new string('x', 251)));
        Assert.Null(R.TaxNoteError(new string('x', 250)));
    }
}
