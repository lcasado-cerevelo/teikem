using Teikem.Domain.Security;
using Xunit;

namespace Teikem.Tests;

/// <summary>Ventana de reautenticación AAL2: solo 15, 30 o 60 minutos; un valor viejo ya guardado se conserva (2026-10-11).</summary>
public class Aal2WindowRulesTests
{
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void The_three_allowed_windows_are_valid(int minutes) => Assert.Null(Aal2WindowRules.Validate(minutes, 30));

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(45)]
    [InlineData(120)]
    [InlineData(240)]
    [InlineData(0)]
    [InlineData(-30)]
    public void Any_other_new_value_is_rejected_with_the_exact_message(int minutes)
        => Assert.Equal("La ventana de reautenticación debe ser de 15, 30 o 60 minutos.", Aal2WindowRules.Validate(minutes, 30));

    [Fact]
    public void A_value_already_saved_is_kept_even_if_it_is_not_one_of_the_three()
    {
        Assert.Null(Aal2WindowRules.Validate(45, 45));      // la compañía tenía 45: reenviarlo igual no es un cambio
        Assert.NotNull(Aal2WindowRules.Validate(50, 45));   // pero uno nuevo fuera de la lista no
    }
}
