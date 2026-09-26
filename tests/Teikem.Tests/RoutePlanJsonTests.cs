using Xunit;
using RoutePlanJson = Teikem.Domain.Trips.RoutePlanJson;
using RoutePlanResponse = Teikem.Domain.Trips.RoutePlanResponse;
using RoutePlanUnassigned = Teikem.Domain.Trips.RoutePlanUnassigned;

namespace Teikem.Tests;

/// <summary>Lote 5 / P0: el plan de una corrida se guarda en camelCase y se lee de forma tolerante (nunca lanza).</summary>
public class RoutePlanJsonTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Serialize_and_parse_round_trip()
    {
        var plan = new RoutePlanResponse(new[] { 5, 3 }, new[] { new RoutePlanUnassigned(A, "2026-000001", "CAPACITY_STOPS"), new RoutePlanUnassigned(B, "2026-000002", "CAPACITY_WEIGHT") }, null);
        var json = RoutePlanJson.Serialize(plan);
        Assert.Contains("\"orderedStopIds\":[5,3]", json);
        Assert.Contains("\"unassigned\":", json);
        Assert.Contains("\"orderPublicId\":", json);
        Assert.Contains("\"reasonCode\":\"CAPACITY_STOPS\"", json);

        var parsed = RoutePlanJson.ParseUnassigned(json);
        Assert.Equal(plan.Unassigned, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("basura")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"orderedStopIds\":[1,2]}")]
    [InlineData("{\"unassigned\":\"no es lista\"}")]
    [InlineData("{\"unassigned\":[1,2,3]}")]
    [InlineData("{\"unassigned\":[{\"orderPublicId\":")]
    [InlineData("42")]
    public void Unexpected_input_gives_an_empty_list_without_throwing(string? json)
        => Assert.Empty(RoutePlanJson.ParseUnassigned(json));

    [Fact]
    public void Elements_with_missing_or_invalid_fields_are_skipped()
    {
        var json = "{\"unassigned\":[" +
                   "{\"orderPublicId\":\"" + A + "\",\"orderNumber\":\"N1\",\"reasonCode\":\"CAPACITY_STOPS\"}," +
                   "{\"orderPublicId\":\"no-es-guid\",\"orderNumber\":\"N2\",\"reasonCode\":\"CAPACITY_STOPS\"}," +
                   "{\"orderPublicId\":\"" + B + "\",\"reasonCode\":\"CAPACITY_STOPS\"}," +
                   "{\"orderPublicId\":\"" + B + "\",\"orderNumber\":\"N4\"}," +
                   "{\"orderPublicId\":\"" + B + "\",\"orderNumber\":5,\"reasonCode\":\"X\"}" +
                   "]}";
        var parsed = RoutePlanJson.ParseUnassigned(json);
        var only = Assert.Single(parsed);
        Assert.Equal(new RoutePlanUnassigned(A, "N1", "CAPACITY_STOPS"), only);
    }

    [Fact]
    public void Property_names_are_case_insensitive()
    {
        var json = "{\"Unassigned\":[{\"OrderPublicId\":\"" + A + "\",\"OrderNumber\":\"N1\",\"ReasonCode\":\"CAPACITY_VOLUME\"}]}";
        Assert.Single(RoutePlanJson.ParseUnassigned(json));
    }
}
