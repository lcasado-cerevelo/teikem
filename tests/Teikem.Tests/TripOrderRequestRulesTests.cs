using Xunit;
using TripOrderRequestRules = Teikem.Domain.Trips.TripOrderRequestRules;

namespace Teikem.Tests;

/// <summary>Lote 5 / P2: reglas puras de 'Órdenes en la ruta' (alta de órdenes y consulta 'Sin asignar').</summary>
public class TripOrderRequestRulesTests
{
    // ---------------------------------------------------------------- ValidateAdd

    [Fact]
    public void ValidateAdd_null_or_empty_asks_for_an_order()
    {
        Assert.Equal("Indique al menos una orden.", TripOrderRequestRules.ValidateAdd(null).Error);
        Assert.Equal("Indique al menos una orden.", TripOrderRequestRules.ValidateAdd(Array.Empty<Guid>()).Error);
    }

    [Fact]
    public void ValidateAdd_collapses_duplicates_keeping_arrival_order()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var (ids, error) = TripOrderRequestRules.ValidateAdd(new[] { b, a, b, a, b });
        Assert.Null(error);
        Assert.Equal(new[] { b, a }, ids);
    }

    [Fact]
    public void ValidateAdd_accepts_200_distinct_and_rejects_201()
    {
        var ok = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
        Assert.Null(TripOrderRequestRules.ValidateAdd(ok).Error);

        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();
        Assert.Equal("Agregue como máximo 200 órdenes por solicitud.", TripOrderRequestRules.ValidateAdd(tooMany).Error);
    }

    [Fact]
    public void ValidateAdd_counts_distinct_ids_against_the_cap()
    {
        var one = Guid.NewGuid();
        var ids = Enumerable.Repeat(one, 500).Concat(Enumerable.Range(0, 199).Select(_ => Guid.NewGuid()));
        var (distinct, error) = TripOrderRequestRules.ValidateAdd(ids);
        Assert.Null(error);
        Assert.Equal(200, distinct.Count);
    }

    // ---------------------------------------------------------------- NormalizeUnassignedQuery

    [Theory]
    [InlineData(0, 0, 0, 100)]
    [InlineData(-5, -1, 0, 100)]
    [InlineData(10, 1, 10, 1)]
    [InlineData(0, 500, 0, 500)]
    [InlineData(0, 501, 0, 500)]
    [InlineData(20, 50, 20, 50)]
    public void NormalizeUnassignedQuery_normalizes_paging(int skip, int take, int expectedSkip, int expectedTake)
    {
        var r = TripOrderRequestRules.NormalizeUnassignedQuery(null, false, null, null, skip, take);
        Assert.True(r.IsValid);
        Assert.Equal(expectedSkip, r.Skip);
        Assert.Equal(expectedTake, r.Take);
    }

    [Fact]
    public void NormalizeUnassignedQuery_rejects_zone_and_noZone_together()
    {
        var r = TripOrderRequestRules.NormalizeUnassignedQuery(7, true, null, null, 0, 100);
        Assert.False(r.IsValid);
        Assert.Equal("Use dispatchZoneId o noZone, no ambos.", r.Error);
        Assert.True(TripOrderRequestRules.NormalizeUnassignedQuery(7, false, null, null, 0, 100).IsValid);
        Assert.True(TripOrderRequestRules.NormalizeUnassignedQuery(null, true, null, null, 0, 100).IsValid);
    }

    [Fact]
    public void NormalizeUnassignedQuery_rejects_inverted_date_range()
    {
        var r = TripOrderRequestRules.NormalizeUnassignedQuery(null, false, new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 26), 0, 100);
        Assert.Equal("El rango de fechas es inválido.", r.Error);
        Assert.True(TripOrderRequestRules.NormalizeUnassignedQuery(null, false, new DateOnly(2026, 9, 26), new DateOnly(2026, 9, 26), 0, 100).IsValid);
        Assert.True(TripOrderRequestRules.NormalizeUnassignedQuery(null, false, new DateOnly(2026, 9, 26), null, 0, 100).IsValid);
    }

    // ---------------------------------------------------------------- filtros en memoria

    [Theory]
    [InlineData("00949", "009", true)]
    [InlineData("00949-1234", "00949", true)]
    [InlineData(" 00949 ", "00949", true)]
    [InlineData("00961", "00949", false)]
    [InlineData(null, "009", false)]
    [InlineData(null, null, true)]
    [InlineData("00949", "  ", true)]
    public void MatchesPostalPrefix(string? postalCode, string? prefix, bool expected)
        => Assert.Equal(expected, TripOrderRequestRules.MatchesPostalPrefix(postalCode, prefix));

    [Theory]
    [InlineData("Bayamón", "bayamon", true)]
    [InlineData("SAN  JUAN", "san juan", true)]
    [InlineData("Río Piedras", "RIO PIEDRAS ", true)]
    [InlineData("Bayamón", "Baya", false)]
    [InlineData(null, "Bayamón", false)]
    [InlineData("Bayamón", null, true)]
    public void MatchesCity_ignores_accents_case_and_spacing(string? city, string? filter, bool expected)
        => Assert.Equal(expected, TripOrderRequestRules.MatchesCity(city, filter));

    [Fact]
    public void InRequestedRange_is_inclusive_by_day()
    {
        var from = new DateOnly(2026, 9, 26);
        var to = new DateOnly(2026, 9, 27);
        Assert.True(TripOrderRequestRules.InRequestedRange(new DateTime(2026, 9, 26, 0, 0, 0), from, to));
        Assert.True(TripOrderRequestRules.InRequestedRange(new DateTime(2026, 9, 27, 23, 59, 59), from, to));
        Assert.False(TripOrderRequestRules.InRequestedRange(new DateTime(2026, 9, 28, 0, 0, 0), from, to));
        Assert.False(TripOrderRequestRules.InRequestedRange(new DateTime(2026, 9, 25, 23, 59, 59), from, to));
        Assert.False(TripOrderRequestRules.InRequestedRange(null, from, to));
        Assert.True(TripOrderRequestRules.InRequestedRange(null, null, null));
        Assert.True(TripOrderRequestRules.InRequestedRange(new DateTime(2020, 1, 1), null, to));
    }
}
