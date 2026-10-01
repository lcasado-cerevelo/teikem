using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Compañía con la que entra un usuario de varias compañías (pedido de Luis, 2026-09-30): el login ya no pregunta; entra directo y
/// la compañía se cambia desde el selector de la cabecera.
/// </summary>
public class LoginTenantRulesTests
{
    private static readonly TenantOptionDto Depot = new(7, "Advance Depot", false);
    private static readonly TenantOptionDto Logistics = new(1, "Advance Logistics", false);
    private static readonly TenantOptionDto Solutions = new(8, "Advance Solutions", false);

    [Fact]
    public void El_predeterminado_del_usuario_manda_si_sigue_siendo_miembro()
        => Assert.Equal(8, LoginTenantRules.Pick(8, [Depot, Logistics, Solutions with { IsDefault = false }]));

    [Fact]
    public void Si_el_predeterminado_ya_no_es_miembro_usa_la_membresia_marcada()
        => Assert.Equal(7, LoginTenantRules.Pick(99, [Logistics, Depot with { IsDefault = true }, Solutions]));

    [Fact]
    public void Sin_predeterminado_entra_a_la_primera_por_nombre()
        => Assert.Equal(7, LoginTenantRules.Pick(null, [Solutions, Logistics, Depot]));

    [Fact]
    public void Con_una_sola_compania_entra_a_esa()
        => Assert.Equal(8, LoginTenantRules.Pick(null, [Solutions]));

    [Fact]
    public void Sin_membresias_no_hay_a_donde_entrar()
        => Assert.Throws<ArgumentException>(() => LoginTenantRules.Pick(1, []));
}
