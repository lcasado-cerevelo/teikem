using Teikem.Domain.Constants;
using Xunit;
using DispatchZoneMatcher = Teikem.Domain.Trips.DispatchZoneMatcher;
using ZoneMember = Teikem.Domain.Trips.ZoneMember;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: normalización de miembros de zona, resolución CP &gt; rango &gt; municipio y solapamiento entre zonas.</summary>
public class DispatchZoneMatcherTests
{
    // ---------------------------------------------------------------- NormalizeMember

    [Theory]
    [InlineData("POSTAL_CODE", " 00949 ", "00949")]
    [InlineData("POSTAL_CODE", "00949-1234", "00949")]
    [InlineData("postal_code", "00901", "00901")]
    [InlineData("POSTAL_RANGE", "00900-00999", "00900-00999")]
    [InlineData("POSTAL_RANGE", " 00900 - 00999 ", "00900-00999")]
    [InlineData("POSTAL_RANGE", "00949-00949", "00949-00949")]
    [InlineData("MUNICIPALITY", "  Toa   Baja ", "Toa Baja")]
    [InlineData("MUNICIPALITY", "Bayamón", "Bayamón")]
    public void NormalizeMember_accepts_valid_values(string type, string raw, string expected)
    {
        var (value, error) = DispatchZoneMatcher.NormalizeMember(type, raw);
        Assert.Null(error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("POSTAL_CODE", "949", "El código postal debe tener 5 dígitos (ej. 00949).")]
    [InlineData("POSTAL_CODE", "ABCDE", "El código postal debe tener 5 dígitos (ej. 00949).")]
    [InlineData("POSTAL_CODE", "", "Indique el valor del criterio.")]
    [InlineData("POSTAL_RANGE", "00999-00900", "El rango postal debe tener la forma 00900-00999 (inicio menor o igual que el fin).")]
    [InlineData("POSTAL_RANGE", "00900", "El rango postal debe tener la forma 00900-00999 (inicio menor o igual que el fin).")]
    [InlineData("MUNICIPALITY", "   ", "Indique el municipio.")]
    [InlineData("POLYGON", "POLYGON((0 0, 1 1, 1 0, 0 0))", "Las zonas por polígono todavía no se soportan; use código postal, rango postal o municipio.")]
    [InlineData("FOO", "x", "Criterio de zona desconocido: 'FOO'.")]
    public void NormalizeMember_rejects_with_exact_messages(string type, string raw, string message)
    {
        var (value, error) = DispatchZoneMatcher.NormalizeMember(type, raw);
        Assert.Null(value);
        Assert.Equal(message, error);
    }

    [Fact]
    public void Municipality_longer_than_120_is_rejected()
        => Assert.Equal("El municipio admite como máximo 120 caracteres.", DispatchZoneMatcher.NormalizeMember(ZoneMatchTypes.Municipality, new string('a', 121)).Error);

    // ---------------------------------------------------------------- Resolve

    private static readonly ZoneMember[] Members =
    {
        new("Z1", ZoneMatchTypes.PostalCode, "00949"),
        new("Z2", ZoneMatchTypes.PostalRange, "00900-00999"),
        new("Z3", ZoneMatchTypes.Municipality, "Bayamón"),
        new("Z4", ZoneMatchTypes.Municipality, "Toa Baja"),
        new("Z5", ZoneMatchTypes.Municipality, "Toa Baja"),
        new("Z6", ZoneMatchTypes.PostalCode, "00725"),
        new("Z7", ZoneMatchTypes.PostalCode, "00725"),
    };

    [Fact]
    public void Exact_postal_code_wins_over_range_and_municipality()
    {
        var r = DispatchZoneMatcher.Resolve(Members, "00949", "Bayamón");
        Assert.Equal("Z1", r.ZoneCode);
        Assert.Equal(ZoneMatchTypes.PostalCode, r.MatchedBy);
        Assert.False(r.Ambiguous);
    }

    [Fact]
    public void Zip_plus_4_resolves_by_its_5_digits()
        => Assert.Equal("Z1", DispatchZoneMatcher.Resolve(Members, "00949-1234", null).ZoneCode);

    [Fact]
    public void Range_wins_over_municipality()
    {
        var r = DispatchZoneMatcher.Resolve(Members, "00961", "Bayamón");
        Assert.Equal("Z2", r.ZoneCode);
        Assert.Equal(ZoneMatchTypes.PostalRange, r.MatchedBy);
    }

    [Fact]
    public void Municipality_matches_without_accents_or_case()
    {
        var r = DispatchZoneMatcher.Resolve(Members, "01000", "  BAYAMON ");
        Assert.Equal("Z3", r.ZoneCode);
        Assert.Equal(ZoneMatchTypes.Municipality, r.MatchedBy);
        Assert.Equal("Z3", DispatchZoneMatcher.Resolve(Members, null, "bayamón").ZoneCode);
    }

    [Fact]
    public void Tie_in_the_same_level_is_ambiguous()
    {
        var byCity = DispatchZoneMatcher.Resolve(Members, null, "Toa Baja");
        Assert.True(byCity.Ambiguous);
        Assert.Null(byCity.ZoneCode);
        Assert.Equal(new[] { "Z4", "Z5" }, byCity.Candidates);

        var byZip = DispatchZoneMatcher.Resolve(Members, "00725", "Bayamón");
        Assert.True(byZip.Ambiguous);
        Assert.Equal(new[] { "Z6", "Z7" }, byZip.Candidates);
    }

    [Fact]
    public void No_match_is_empty()
    {
        var r = DispatchZoneMatcher.Resolve(Members, "99999", "Ponce");
        Assert.Null(r.ZoneCode);
        Assert.Null(r.MatchedBy);
        Assert.False(r.Ambiguous);
        Assert.Empty(r.Candidates);
        Assert.Null(DispatchZoneMatcher.Resolve(Array.Empty<ZoneMember>(), "00949", "Bayamón").ZoneCode);
    }

    // ---------------------------------------------------------------- FindConflict

    [Fact]
    public void FindConflict_detects_the_same_value_in_another_zone()
    {
        Assert.Equal("Z1", DispatchZoneMatcher.FindConflict(Members, "Z9", ZoneMatchTypes.PostalCode, "00949"));
        Assert.Equal("Z3", DispatchZoneMatcher.FindConflict(Members, "Z9", ZoneMatchTypes.Municipality, "bayamon"));
        Assert.Equal("Z2", DispatchZoneMatcher.FindConflict(Members, "Z9", ZoneMatchTypes.PostalRange, "00990-01010")); // se solapa
    }

    [Fact]
    public void FindConflict_ignores_the_own_zone_other_types_and_disjoint_ranges()
    {
        Assert.Null(DispatchZoneMatcher.FindConflict(Members, "Z1", ZoneMatchTypes.PostalCode, "00949"));
        Assert.Null(DispatchZoneMatcher.FindConflict(Members, "Z9", ZoneMatchTypes.PostalCode, "00950"));   // dentro del rango de Z2: no choca (precedencia)
        Assert.Null(DispatchZoneMatcher.FindConflict(Members, "Z9", ZoneMatchTypes.PostalRange, "01000-01099"));
    }
}
