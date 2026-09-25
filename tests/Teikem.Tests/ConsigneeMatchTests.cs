using Teikem.Domain.Orders;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 3 (P2): coincidencia de consignatario escrito libre contra el directorio por nombre + línea 1 normalizados.</summary>
public class ConsigneeMatchTests
{
    [Fact]
    public void Key_ignores_case_spacing_and_trimming()
    {
        Assert.Equal(ConsigneeMatch.Key("  Farmacia   Ponce ", "Calle 5 #12"), ConsigneeMatch.Key("farmacia ponce", "CALLE 5 #12"));
        Assert.Equal("farmacia ponce|calle 5 #12", ConsigneeMatch.Key("  Farmacia   Ponce ", "Calle 5 #12"));
    }

    [Fact]
    public void Key_ignores_diacritics()
    {
        Assert.Equal(ConsigneeMatch.Key("Peña", "Calle 1"), ConsigneeMatch.Key("Pena", "Calle 1"));
        Assert.Equal(ConsigneeMatch.Key("Vagón", "Avenida Muñoz"), ConsigneeMatch.Key("VAGON", "avenida munoz"));
    }

    [Fact]
    public void Key_is_null_when_name_or_line1_is_empty()
    {
        Assert.Null(ConsigneeMatch.Key("", "Calle 1"));
        Assert.Null(ConsigneeMatch.Key("Farmacia", "   "));
        Assert.Null(ConsigneeMatch.Key(null, null));
    }

    [Fact]
    public void Key_does_not_cap_length_like_special_service_names()
    {
        var longLine = new string('a', 200);
        Assert.NotNull(ConsigneeMatch.Key("Farmacia", longLine));
    }

    [Fact]
    public void FindMatch_returns_first_location_with_same_name_and_line1()
    {
        var candidates = new[]
        {
            (LocationId: 1, Name: "Farmacia Ponce", Line1: "Calle 9"),
            (LocationId: 2, Name: "Farmacia Ponce", Line1: "Calle 5 #12"),
            (LocationId: 3, Name: "farmacia ponce", Line1: "calle 5 #12"),
        };
        Assert.Equal(2, ConsigneeMatch.FindMatch(candidates, "  FARMACIA  PONCE ", "calle 5 #12"));
    }

    [Fact]
    public void FindMatch_never_matches_by_name_alone_or_line1_alone()
    {
        var candidates = new[] { (LocationId: 1, Name: "Farmacia Ponce", Line1: "Calle 9") };
        Assert.Null(ConsigneeMatch.FindMatch(candidates, "Farmacia Ponce", "Calle 5 #12")); // solo nombre
        Assert.Null(ConsigneeMatch.FindMatch(candidates, "Otra Farmacia", "Calle 9"));       // solo línea 1
        Assert.Null(ConsigneeMatch.FindMatch(candidates, "", "Calle 9"));                    // sin nombre: sin clave
        Assert.Null(ConsigneeMatch.FindMatch(Array.Empty<(int, string, string)>(), "Farmacia Ponce", "Calle 9"));
    }
}
