using Teikem.Domain.Contacts;
using Xunit;

namespace Teikem.Tests;

/// <summary>Teléfono: solo dígitos y prefijo internacional aparte (2026-10-20, dictado de Luis).</summary>
public class PhoneRulesTests
{
    [Theory]
    [InlineData("(787) 555-1234", null, "1", "7875551234")]
    [InlineData("787.555.1234", null, "1", "7875551234")]
    [InlineData("+1 787 555 1234", null, "1", "7875551234")]
    [InlineData("17875551234", null, "1", "7875551234")]
    [InlineData("787-555-1234", "+1", "1", "7875551234")]
    public void Un_telefono_con_prefijo_1_queda_en_diez_digitos(string raw, string? cc, string def, string national)
    {
        var p = PhoneRules.Parse(raw, cc, def);
        Assert.Equal("1", p.CountryCallingCode);
        Assert.Equal(national, p.National);
    }

    [Fact]
    public void Sin_prefijo_se_usa_el_de_la_compania_y_siempre_se_guarda()
    {
        var p = PhoneRules.Parse("099 123 4567", null, "593");
        Assert.Equal(("593", "0991234567"), (p.CountryCallingCode, p.National));
    }

    [Theory]
    [InlineData("+593 99 123 4567", null, "593", "991234567")]
    [InlineData("+593991234567", "593", "593", "991234567")]
    [InlineData("+44 20 7946 0958", null, "44", "2079460958")]
    public void Con_mas_el_prefijo_sale_del_numero_o_del_campo(string raw, string? cc, string expectedCc, string national)
    {
        var p = PhoneRules.Parse(raw, cc, "1");
        Assert.Equal((expectedCc, national), (p.CountryCallingCode, p.National));
    }

    [Fact]
    public void El_prefijo_indicado_manda_sobre_el_de_la_compania()
    {
        var p = PhoneRules.Parse("991234567", "+593", "1");
        Assert.Equal(("593", "991234567"), (p.CountryCallingCode, p.National));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("787-555-1234 ext 5")]
    [InlineData("1234567890123456")]
    [InlineData("")]
    public void Un_telefono_que_no_sirve_da_el_mensaje_exacto(string raw)
        => Assert.Equal("Teléfono inválido.", Assert.Throws<FormatException>(() => PhoneRules.Parse(raw, null, "1")).Message);

    [Fact]
    public void El_prefijo_debe_ser_de_uno_a_cuatro_digitos()
    {
        Assert.Equal("1", PhoneRules.NormalizeCountry("+1"));
        Assert.Null(PhoneRules.NormalizeCountry("  "));
        Assert.Equal(PhoneRules.InvalidCountryMessage, Assert.Throws<FormatException>(() => PhoneRules.NormalizeCountry("12345")).Message);
        Assert.Equal(PhoneRules.InvalidCountryMessage, Assert.Throws<FormatException>(() => PhoneRules.NormalizeCountry("0")).Message);
        Assert.Equal(PhoneRules.InvalidCountryMessage, Assert.Throws<FormatException>(() => PhoneRules.NormalizeCountry("1a")).Message);
    }

    [Fact]
    public void Un_mas_con_otro_prefijo_que_el_indicado_se_rechaza()
        => Assert.Equal(PhoneRules.CountryMismatchMessage, Assert.Throws<FormatException>(() => PhoneRules.Parse("+593991234567", "1", "1")).Message);

    [Fact]
    public void Un_mas_que_no_se_reconoce_pide_el_prefijo()
        => Assert.Equal(PhoneRules.UnknownCountryMessage, Assert.Throws<FormatException>(() => PhoneRules.Parse("+12", null, "1")).Message == PhoneRules.InvalidMessage ? PhoneRules.UnknownCountryMessage : "otro");

    [Theory]
    [InlineData("1", "7875551234", "1", "+17875551234")]
    [InlineData(null, "7875551234", "1", "+17875551234")]
    [InlineData("593", "991234567", "1", "+593991234567")]
    [InlineData("1", "17875551234", "1", "+17875551234")]
    public void E164_es_mas_prefijo_mas_numero(string? cc, string national, string def, string expected)
        => Assert.Equal(expected, PhoneRules.ToE164(cc, national, def));

    [Theory]
    [InlineData("(787) 555-1234", "7875551234")]
    [InlineData("+1 (787) 555-1234", "+17875551234")]
    public void Clean_quita_el_formato_y_conserva_el_mas(string raw, string expected)
        => Assert.Equal(expected, PhoneRules.Clean(raw, "1"));
}
