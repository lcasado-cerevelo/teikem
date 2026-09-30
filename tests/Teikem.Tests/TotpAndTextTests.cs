using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
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

/// <summary>
/// Lote 15 ("hoy" en hora de Puerto Rico en todo lo que dependa del día): los rangos de indicadores, gráficos y vistas son días
/// LOCALES convertidos a instantes UTC (00:00 local = 04:00Z; hasta exclusivo al día siguiente). Bordes 03:59Z / 04:00Z.
/// </summary>
public class DateRangeResolverTests
{
    private static readonly TimeZoneInfo Pr = LocalDay.DefaultZone;

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Last7_default_covers_today_and_six_previous_local_days()
    {
        var (from, to) = DateRangeResolver.Resolve(null, null, null, Utc(2026, 9, 24, 15), Pr);
        Assert.Equal(Utc(2026, 9, 18, 4), from);
        Assert.Equal(Utc(2026, 9, 25, 4), to);
        Assert.Equal((from, to), DateRangeResolver.Resolve(DateRangeModes.Last7, null, null, Utc(2026, 9, 24, 15), Pr));
    }

    [Fact]
    public void Today_changes_at_local_midnight_not_at_utc_midnight()
    {
        // 03:59Z del 25 = 23:59 del 24 en Puerto Rico: "hoy" sigue siendo el 24 (con días UTC ya sería el 25).
        var before = DateRangeResolver.Resolve(DateRangeModes.Last7, null, null, Utc(2026, 9, 25, 3, 59), Pr);
        Assert.Equal((Utc(2026, 9, 18, 4), Utc(2026, 9, 25, 4)), before);
        // 04:00Z del 25 = 00:00 del 25 local: ya es el 25.
        var after = DateRangeResolver.Resolve(DateRangeModes.Last7, null, null, Utc(2026, 9, 25, 4), Pr);
        Assert.Equal((Utc(2026, 9, 19, 4), Utc(2026, 9, 26, 4)), after);
        // Un movimiento a las 23:59 locales del 24 (03:59Z del 25) entra en el rango del 24, no en el del 25.
        var movement = Utc(2026, 9, 25, 3, 59);
        Assert.True(movement >= before.FromUtc && movement < before.ToUtc);
        var (d25f, d25t) = DateRangeResolver.Resolve(DateRangeModes.Custom, new DateOnly(2026, 9, 25), new DateOnly(2026, 9, 25), Utc(2026, 9, 26, 12), Pr);
        Assert.False(movement >= d25f && movement < d25t);
    }

    [Fact]
    public void Last30_this_month_custom_and_all_in_local_days()
    {
        var now = Utc(2026, 10, 1, 3);   // 23:00 del 30/09 local: el mes local todavía es septiembre
        Assert.Equal((Utc(2026, 9, 1, 4), Utc(2026, 10, 1, 4)), DateRangeResolver.Resolve(DateRangeModes.Last30, null, null, now, Pr));
        Assert.Equal((Utc(2026, 9, 1, 4), Utc(2026, 10, 1, 4)), DateRangeResolver.Resolve(DateRangeModes.ThisMonth, null, null, now, Pr));
        Assert.Equal((Utc(2026, 10, 1, 4), Utc(2026, 10, 2, 4)), DateRangeResolver.Resolve(DateRangeModes.ThisMonth, null, null, Utc(2026, 10, 1, 4), Pr));
        var (f, t) = DateRangeResolver.Resolve(DateRangeModes.Custom, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 1), now, Pr);
        Assert.Equal(Utc(2026, 3, 30, 4), f);
        Assert.Equal(Utc(2026, 4, 2, 4), t);
        Assert.Equal((Utc(2026, 3, 30, 4), (DateTime?)null), DateRangeResolver.Resolve(DateRangeModes.Custom, new DateOnly(2026, 3, 30), null, now, Pr));
        Assert.Equal(((DateTime?)null, (DateTime?)null), DateRangeResolver.Resolve(DateRangeModes.All, null, null, now, Pr));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), DateRangeResolver.LocalDays("this_month", null, null, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void With_the_tenant_clock_it_is_the_same_rule()
    {
        var clock = new TenantClock(Pr, () => Utc(2026, 9, 25, 3, 59));
        Assert.Equal(DateRangeResolver.Resolve(null, null, null, Utc(2026, 9, 25, 3, 59), Pr), DateRangeResolver.Resolve(null, null, null, clock));
        Assert.Equal((Utc(2026, 9, 18, 4), Utc(2026, 9, 25, 4)), DateRangeResolver.Resolve(null, null, null, clock));
    }

    [Fact]
    public void Charts_group_an_instant_by_its_local_day_and_a_calendar_day_as_is()
    {
        Assert.Equal("2026-09-24", AnalyticsEngine.GroupKey(Utc(2026, 9, 25, 3, 59), true, Pr).Label);   // 23:59 del 24 local
        Assert.Equal("2026-09-25", AnalyticsEngine.GroupKey(Utc(2026, 9, 25, 4), true, Pr).Label);       // 00:00 del 25 local
        Assert.Equal("2026-09-24", AnalyticsEngine.GroupKey(DateTime.SpecifyKind(new DateTime(2026, 9, 25, 3, 59, 0), DateTimeKind.Unspecified), true, Pr).Label);   // de la base = UTC
        Assert.Equal("2026-09-25", AnalyticsEngine.GroupKey(new DateOnly(2026, 9, 25), true, Pr).Label); // día de calendario: sin mover
        Assert.Equal(new DateTime(2026, 9, 25), AnalyticsEngine.GroupKey(new DateOnly(2026, 9, 25), true, Pr).Key);
        Assert.Equal(("—", (object?)null), AnalyticsEngine.GroupKey(null, true, Pr));
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
