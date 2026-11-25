using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 243 — desglose del IVU guardado entre los componentes de la tasa (estatal y municipal), con residuo mayor a nivel de orden.</summary>
public sealed class TaxBreakdownRulesTests
{
    private static readonly TaxRateInfo Ivu = new(1, "IVU", "IVU", 11.5m, new[]
    {
        new TaxComponentInfo("STATE", "IVU estatal", 10.5m),
        new TaxComponentInfo("CITY", "IVU municipal", 1m),
    });

    private static readonly TaxRateInfo Other = new(2, "IVU-B", "IVU B", 11.5m, new[]
    {
        new TaxComponentInfo("STATE", "IVU estatal", 10.5m),
        new TaxComponentInfo("CITY", "IVU municipal", 1m),
    });

    private static Dictionary<int, TaxRateInfo> Rates(params TaxRateInfo[] r) => r.ToDictionary(x => x.TaxRateId);

    private static TaxBreakdownLine Line(decimal tax, decimal pct = 11.5m, int? rate = 1, bool exempt = false) => new(rate, pct, tax, exempt);

    [Fact]
    public void Once_y_medio_por_ciento_se_reparte_en_estatal_y_municipal()
    {
        var rows = TaxBreakdownRules.Compute(new[] { Line(11.50m) }, 0m, Rates(Ivu), 2);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("IVU estatal", 10.5m, 10.50m), (rows[0].Name, rows[0].Percent!.Value, rows[0].Amount));
        Assert.Equal(("IVU municipal", 1m, 1.00m), (rows[1].Name, rows[1].Percent!.Value, rows[1].Amount));
        Assert.All(rows, r => Assert.Equal(TaxBreakdownKind.Component, r.Kind));
    }

    [Fact]
    public void Residuo_mayor_a_nivel_de_orden_suma_exacto_y_no_carga_todo_al_ultimo()
    {
        // tres líneas de 0.01 de IVU: por línea el municipal recibiría todo el redondeo; a nivel de orden 0.03 → 0.0274 estatal / 0.0026 municipal → 0.03 / 0.00
        var lines = new[] { Line(0.01m), Line(0.01m), Line(0.01m) };
        var rows = TaxBreakdownRules.Compute(lines, 0m, Rates(Ivu), 2);
        Assert.Equal(0.03m, rows.Sum(r => r.Amount));
        Assert.Equal(0.03m, rows.Single(r => r.Name == "IVU estatal").Amount);
        Assert.Equal(0.00m, rows.Single(r => r.Name == "IVU municipal").Amount);

        // varias líneas con centavos sueltos: la suma mostrada es la del documento más el envío, exacta
        var many = new[] { Line(4.31m), Line(2.01m), Line(2.88m), Line(0.07m), Line(1.15m) };
        var withShipping = TaxBreakdownRules.Compute(many, 0.58m, Rates(Ivu), 2);
        Assert.Equal(many.Sum(l => l.TaxAmount) + 0.58m, withShipping.Sum(r => r.Amount));
        Assert.All(withShipping, r => Assert.Equal(r.Amount, Math.Round(r.Amount, 2)));
    }

    [Fact]
    public void Exencion_parcial_deja_el_municipal_exento()
    {
        // la línea se cobró al 10.5 % con una tasa de 11.5 %: el municipal (1 %) quedó exento
        var rows = TaxBreakdownRules.Compute(new[] { Line(10.50m, pct: 10.5m) }, 0m, Rates(Ivu), 2);
        var state = rows.Single(r => r.Name == "IVU estatal");
        var city = rows.Single(r => r.Name == "IVU municipal");
        Assert.Equal((10.50m, false), (state.Amount, state.IsExempt));
        Assert.Equal((0m, true), (city.Amount, city.IsExempt));
    }

    [Fact]
    public void Un_componente_exento_en_un_grupo_pero_cobrado_en_otro_no_sale_exento()
    {
        var rows = TaxBreakdownRules.Compute(new[] { Line(10.50m, pct: 10.5m), Line(1.15m) }, 0m, Rates(Ivu), 2);
        var city = rows.Single(r => r.Name == "IVU municipal");
        Assert.False(city.IsExempt);
        Assert.Equal(0.10m, city.Amount);
        Assert.Equal(11.65m, rows.Sum(r => r.Amount));
    }

    [Fact]
    public void Exencion_ambigua_o_imposible_va_a_IVU_sin_desglose()
    {
        var twoOnes = new TaxRateInfo(3, "AMB", "Ambigua", 3m, new[]
        {
            new TaxComponentInfo("A", "Componente A", 1m), new TaxComponentInfo("B", "Componente B", 1m), new TaxComponentInfo("C", "Componente C", 1m),
        });
        var ambiguous = TaxBreakdownRules.Compute(new[] { Line(2.00m, pct: 2m, rate: 3) }, 0m, Rates(twoOnes), 2);
        var row = Assert.Single(ambiguous);
        Assert.Equal((TaxBreakdownKind.Unsplit, 2.00m), (row.Kind, row.Amount));
        Assert.Equal("IVU (sin desglose)", TaxBreakdownRules.Label(row, "es"));
        Assert.Equal("Sales tax (not itemized)", TaxBreakdownRules.Label(row, "en"));

        var impossible = TaxBreakdownRules.Compute(new[] { Line(7.00m, pct: 7m) }, 0m, Rates(Ivu), 2);   // 11.5 − 7 = 4.5: ningún subconjunto
        Assert.Equal(TaxBreakdownKind.Unsplit, Assert.Single(impossible).Kind);
    }

    [Fact]
    public void Una_tasa_sin_componentes_da_un_solo_renglon_IVU()
    {
        var plain = new TaxRateInfo(4, "SIMPLE", "Simple", 7m, Array.Empty<TaxComponentInfo>());
        var rows = TaxBreakdownRules.Compute(new[] { Line(0.70m, pct: 7m, rate: 4), Line(1.40m, pct: 7m, rate: 4) }, 0m, Rates(plain), 2);
        var row = Assert.Single(rows);
        Assert.Equal((TaxBreakdownKind.Plain, 2.10m, (decimal?)null), (row.Kind, row.Amount, row.Percent));
        Assert.Equal("IVU", TaxBreakdownRules.Label(row, "es"));

        // una tasa que no está en el diccionario o una línea sin tasa también
        Assert.Equal(TaxBreakdownKind.Plain, Assert.Single(TaxBreakdownRules.Compute(new[] { Line(1m, rate: 99) }, 0m, Rates(), 2)).Kind);
        Assert.Equal(TaxBreakdownKind.Plain, Assert.Single(TaxBreakdownRules.Compute(new[] { Line(1m, rate: null) }, 0m, Rates(), 2)).Kind);
    }

    [Fact]
    public void El_IVU_del_envio_se_reparte_con_una_sola_tasa_y_va_aparte_con_dos()
    {
        var single = TaxBreakdownRules.Compute(new[] { Line(11.50m) }, 1.15m, Rates(Ivu), 2);
        Assert.Equal(2, single.Count);
        Assert.Equal(11.55m, single.Single(r => r.Name == "IVU estatal").Amount);
        Assert.Equal(1.10m, single.Single(r => r.Name == "IVU municipal").Amount);

        var two = TaxBreakdownRules.Compute(new[] { Line(11.50m, rate: 1), Line(2.30m, rate: 2) }, 1.15m, Rates(Ivu, Other), 2);
        var shipping = two.Single(r => r.Kind == TaxBreakdownKind.Shipping);
        Assert.Equal(1.15m, shipping.Amount);
        Assert.Equal("IVU del envío", TaxBreakdownRules.Label(shipping, "es"));
        Assert.Equal(14.95m, two.Sum(r => r.Amount));
        // los componentes se suman por nombre entre tasas
        Assert.Equal(12.60m, two.Single(r => r.Name == "IVU estatal").Amount);
        Assert.Equal(1.20m, two.Single(r => r.Name == "IVU municipal").Amount);

        // sin líneas gravadas el IVU del envío va en su renglón
        Assert.Equal(TaxBreakdownKind.Shipping, Assert.Single(TaxBreakdownRules.Compute(Array.Empty<TaxBreakdownLine>(), 0.50m, Rates(Ivu), 2)).Kind);
    }

    [Fact]
    public void Una_linea_exenta_no_aporta_y_sin_impuesto_no_hay_renglones()
    {
        Assert.Empty(TaxBreakdownRules.Compute(new[] { Line(0m, pct: 0m, rate: null, exempt: true) }, 0m, Rates(Ivu), 2));
        var rows = TaxBreakdownRules.Compute(new[] { Line(0m, pct: 0m, rate: null, exempt: true), Line(11.50m) }, 0m, Rates(Ivu), 2);
        Assert.Equal(11.50m, rows.Sum(r => r.Amount));
    }

    [Fact]
    public void Subconjunto_unico_de_componentes_exentos()
    {
        Assert.Equal(new[] { "CITY" }, TaxBreakdownRules.UniqueExemptSubset(Ivu, 1m)!.ToArray());
        Assert.Equal(new[] { "STATE" }, TaxBreakdownRules.UniqueExemptSubset(Ivu, 10.5m)!.ToArray());
        Assert.Null(TaxBreakdownRules.UniqueExemptSubset(Ivu, 2m));
        Assert.Null(TaxBreakdownRules.UniqueExemptSubset(Ivu, 0m));
    }

    [Fact]
    public void Residuo_mayor_redondea_a_los_decimales_de_la_moneda()
    {
        Assert.Equal(new[] { 0.34m, 0.33m, 0.33m }, TaxBreakdownRules.LargestRemainder(new[] { 1m / 3m, 1m / 3m, 1m / 3m }, 1.00m, 2));
        Assert.Equal(new[] { 1m, 0m }, TaxBreakdownRules.LargestRemainder(new[] { 0.6m, 0.4m }, 1m, 0));
        Assert.Empty(TaxBreakdownRules.LargestRemainder(Array.Empty<decimal>(), 0m, 2));
    }
}
