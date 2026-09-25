using Teikem.Domain.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 2 (P5): normalización y comparación de nombres de tipos de servicio especial (lógica pura).</summary>
public class SpecialServiceRulesTests
{
    [Fact]
    public void NormalizeName_trims_collapses_lowercases_and_strips_diacritics()
    {
        Assert.Equal("vagon del muelle", SpecialServiceRules.NormalizeName("  Vagón   del Muelle "));
        Assert.Equal("entrega en pinones", SpecialServiceRules.NormalizeName("Entrega en Piñones"));
        Assert.Equal("a b", SpecialServiceRules.NormalizeName("A\t\t B"));
    }

    [Fact]
    public void CleanName_keeps_case_and_accents_but_collapses_whitespace()
    {
        Assert.Equal("Vagón del Muelle", SpecialServiceRules.CleanName("  Vagón   del Muelle "));
    }

    [Fact]
    public void SameName_false_for_different_words()
    {
        Assert.False(SpecialServiceRules.SameName("Vagón del muelle", "Vagon Muelle"));
    }

    [Fact]
    public void SameName_true_ignoring_case_accents_and_spacing()
    {
        Assert.True(SpecialServiceRules.SameName("Vagón del muelle", "VAGON DEL MUELLE"));
        Assert.True(SpecialServiceRules.SameName("Vagón del muelle", "  vagón  del  muelle "));
    }

    [Fact]
    public void SameName_false_when_either_side_is_invalid()
    {
        Assert.False(SpecialServiceRules.SameName("", "Vagón del muelle"));
        Assert.False(SpecialServiceRules.SameName("Vagón del muelle", null));
        Assert.False(SpecialServiceRules.SameName(null, null));
    }

    [Fact]
    public void Empty_or_whitespace_name_throws()
    {
        // En Domain no existe ValidationException (vive en Infrastructure): el servicio traduce ArgumentException a 400.
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.NormalizeName(""));
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.NormalizeName("   "));
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.NormalizeName(null));
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.CleanName("\t \n"));
    }

    [Fact]
    public void Name_longer_than_120_throws_and_exactly_120_is_accepted()
    {
        var tooLong = new string('a', SpecialServiceRules.NameMaxLength + 1);
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.NormalizeName(tooLong));
        Assert.Throws<ArgumentException>(() => SpecialServiceRules.CleanName(tooLong));

        var exact = new string('a', SpecialServiceRules.NameMaxLength);
        Assert.Equal(exact, SpecialServiceRules.CleanName(exact));
        // Los espacios sobrantes no cuentan: se recortan antes de medir.
        Assert.Equal(exact, SpecialServiceRules.CleanName("   " + exact + "   "));
    }
}
