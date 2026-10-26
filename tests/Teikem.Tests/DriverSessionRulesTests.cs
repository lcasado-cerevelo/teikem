using Teikem.Domain.Tenancy;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-23 — duración de la sesión del chofer: por compañía, la más permisiva, con el tope de la aplicación.</summary>
public class DriverSessionRulesTests
{
    private const int Cap = 30 * 1440;

    [Fact]
    public void Sin_valor_del_chofer_vale_la_sesion_general()
        => Assert.Equal(240, DriverSessionRules.CompanyMinutes(null, 240, Cap));

    [Fact]
    public void El_valor_del_chofer_solo_alarga_nunca_acorta_la_sesion_general()
    {
        Assert.Equal(20_000, DriverSessionRules.CompanyMinutes(20_000, 240, Cap));
        Assert.Equal(1440 * 60, DriverSessionRules.CompanyMinutes(60, 60 * 1440, Cap));   // general 60 días > chofer 60 min
    }

    [Fact]
    public void El_tope_de_la_aplicacion_limita_el_valor_del_chofer_pero_no_la_sesion_general()
    {
        Assert.Equal(Cap, DriverSessionRules.CompanyMinutes(1_000_000, 240, Cap));
        Assert.Equal(90 * 1440, DriverSessionRules.CompanyMinutes(1_000_000, 90 * 1440, Cap));   // la general de 90 días manda si es mayor que el tope
    }

    [Fact]
    public void Entre_varias_companias_manda_la_mas_permisiva()
    {
        var m = DriverSessionRules.Minutes(new (int?, int?)[] { (null, 240), (43_200, 240), (100, 240) }, Cap);
        Assert.Equal(43_200, m);
        Assert.Equal(1440, DriverSessionRules.Minutes(Array.Empty<(int?, int?)>(), Cap));
    }
}
