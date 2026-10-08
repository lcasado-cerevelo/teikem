using Teikem.Infrastructure.Abstractions;
using Xunit;

namespace Teikem.Tests;

/// <summary>2026-10-08: el remitente del correo se muestra con el nombre de la compañía (sin «vía Teikem»: el «via» lo pone Gmail); sin compañía, el nombre general.</summary>
public class EmailSenderNamesTests
{
    [Theory]
    [InlineData("Advance Logistics", "Advance Logistics")]
    [InlineData("  Advance Depot  ", "Advance Depot")]
    [InlineData("Advance \"Solutions\" <SA>", "Advance Solutions SA")]
    public void The_sender_is_just_the_company_name(string company, string expected)
        => Assert.Equal(expected, EmailSenderNames.Resolve(company, "Teikem"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_company_the_general_name_is_used(string? company)
        => Assert.Equal("Teikem", EmailSenderNames.Resolve(company, "Teikem"));

    [Fact]
    public void A_very_long_company_name_is_cut()
    {
        var name = EmailSenderNames.Resolve(new string('A', 200), "Teikem");
        Assert.Equal(new string('A', EmailSenderNames.MaxCompanyChars), name);
    }
}
