using Teikem.Domain.Common;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

public class TotpServiceTests
{
    [Fact]
    public void Rfc6238_reference_vector_sha1()
    {
        // RFC 6238 Apéndice B: secreto "12345678901234567890", T=59 → 94287082 (8 dígitos) → últimos 6 = 287082
        var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("287082", TotpService.ComputeCode(secret, 59));
        Assert.Equal("081804", TotpService.ComputeCode(secret, 1111111109));
    }

    [Fact]
    public void Verify_accepts_current_and_adjacent_steps_only()
    {
        var secret = TotpService.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var code = TotpService.ComputeCode(secret, now.ToUnixTimeSeconds());
        Assert.True(TotpService.Verify(secret, code, 1, now));
        Assert.True(TotpService.Verify(secret, code, 1, now.AddSeconds(30)));
        Assert.False(TotpService.Verify(secret, code, 1, now.AddSeconds(120)));
        Assert.False(TotpService.Verify(secret, "000000", 1, now));
    }

    [Fact]
    public void Base32_roundtrip()
    {
        var bytes = TotpService.GenerateSecret(20);
        Assert.Equal(bytes, TotpService.Base32Decode(TotpService.Base32Encode(bytes)));
    }

    [Fact]
    public void Recovery_codes_are_unique_and_formatted()
    {
        var codes = TotpService.GenerateRecoveryCodes(10);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-z2-7]{4}-[a-z2-7]{4}-[a-z2-7]{4}$", c));
    }
}

public class MultilingualTextTests
{
    [Fact]
    public void Resolves_with_fallback_chain()
    {
        var json = MultilingualText.Build("Entregada", "Delivered");
        Assert.Equal("Entregada", MultilingualText.Resolve(json, "es"));
        Assert.Equal("Delivered", MultilingualText.Resolve(json, "en-US"));
        Assert.Equal("Entregada", MultilingualText.Resolve(json, "fr"));
        Assert.Equal("", MultilingualText.Resolve(null, "es"));
    }

    [Fact]
    public void Merge_override_wins_per_key()
    {
        var merged = MultilingualText.Merge("""{"es":"Recogido","en":"Pickup"}""", """{"es":"Recolectado"}""");
        Assert.Equal("Recolectado", MultilingualText.Resolve(merged, "es"));
        Assert.Equal("Pickup", MultilingualText.Resolve(merged, "en"));
    }
}

public class DateRangeResolverTests
{
    [Fact]
    public void Last7_default_covers_today_and_six_previous_days()
    {
        var now = new DateTime(2026, 9, 24, 15, 0, 0, DateTimeKind.Utc);
        var (from, to) = DateRangeResolver.Resolve(null, null, null, now);
        Assert.Equal(new DateTime(2026, 9, 18), from);
        Assert.Equal(new DateTime(2026, 9, 25), to);
    }

    [Fact]
    public void Custom_and_all()
    {
        var (f, t) = DateRangeResolver.Resolve("CUSTOM", new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 1));
        Assert.Equal(new DateTime(2026, 3, 30), f);
        Assert.Equal(new DateTime(2026, 4, 2), t);
        Assert.Equal(((DateTime?)null, (DateTime?)null), DateRangeResolver.Resolve("ALL", null, null));
    }
}

public class AggregateTests
{
    [Fact]
    public void Count_sum_avg_min_max()
    {
        var rows = new List<DataRow>
        {
            new() { ["v"] = 10m }, new() { ["v"] = 5m }, new() { ["v"] = null },
        };
        Assert.Equal(3, AnalyticsEngine.Aggregate(rows, new AggregateSpec("COUNT", null)));
        Assert.Equal(2, AnalyticsEngine.Aggregate(rows, new AggregateSpec("COUNT", "v")));
        Assert.Equal(15m, AnalyticsEngine.Aggregate(rows, new AggregateSpec("SUM", "v")));
        Assert.Equal(7.5m, AnalyticsEngine.Aggregate(rows, new AggregateSpec("AVG", "v")));
        Assert.Equal(5m, AnalyticsEngine.Aggregate(rows, new AggregateSpec("MIN", "v")));
        Assert.Equal(10m, AnalyticsEngine.Aggregate(rows, new AggregateSpec("MAX", "v")));
        Assert.Equal(0m, AnalyticsEngine.Aggregate(new List<DataRow>(), new AggregateSpec("SUM", "v")));
    }
}
