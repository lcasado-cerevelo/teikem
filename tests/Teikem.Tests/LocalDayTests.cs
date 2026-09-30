using Teikem.Domain.Common;
using Teikem.Infrastructure.Abstractions;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 14 — "hoy" en hora de Puerto Rico (decisión del dueño 2026-09-30). LocalDay (reglas puras) y TenantClock (el punto
/// único que usan los filtros por día): zona por defecto UTC−4 sin horario de verano, borde de medianoche local contra UTC,
/// rango de días [desde, hasta] con 'hasta' exclusivo al día siguiente, y resolución de zona por IANA, por id de Windows o por
/// respaldo.
/// </summary>
public class LocalDayTests
{
    private static readonly TimeZoneInfo Pr = LocalDay.DefaultZone;

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0, int s = 0) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    [Fact]
    public void Default_zone_is_puerto_rico_utc_minus_four_all_year()
    {
        Assert.Equal(TimeSpan.FromHours(-4), Pr.GetUtcOffset(Utc(2026, 1, 15, 12)));
        Assert.Equal(TimeSpan.FromHours(-4), Pr.GetUtcOffset(Utc(2026, 7, 15, 12)));
        Assert.False(Pr.SupportsDaylightSavingTime && Pr.IsDaylightSavingTime(Utc(2026, 7, 15, 12)));
    }

    [Theory]
    [InlineData(2026, 10, 1, 2, 30, 0, 2026, 9, 30)]   // 22:30 del 30 en Puerto Rico: en UTC ya es el 1
    [InlineData(2026, 10, 1, 3, 59, 59, 2026, 9, 30)]  // 23:59:59 local: sigue siendo el 30
    [InlineData(2026, 10, 1, 4, 0, 0, 2026, 10, 1)]    // medianoche local = 04:00 UTC
    [InlineData(2026, 9, 30, 12, 0, 0, 2026, 9, 30)]
    [InlineData(2027, 1, 1, 3, 0, 0, 2026, 12, 31)]    // fin de año: el 1/1 UTC todavía es 31/12 local
    public void Today_follows_the_local_midnight_not_utc(int y, int m, int d, int h, int min, int s, int ey, int em, int ed)
    {
        var now = Utc(y, m, d, h, min, s);
        Assert.Equal(new DateOnly(ey, em, ed), LocalDay.Today(now, Pr));
        Assert.Equal(new DateOnly(ey, em, ed), new TenantClock(Pr, () => now).Today);
    }

    [Fact]
    public void Start_of_day_and_range_are_local_midnights_in_utc()
    {
        Assert.Equal(Utc(2026, 9, 30, 4), LocalDay.StartOfDayUtc(new DateOnly(2026, 9, 30), Pr));
        var (from, to) = LocalDay.UtcRange(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), Pr);
        Assert.Equal(Utc(2026, 9, 30, 4), from);
        Assert.Equal(Utc(2026, 10, 1, 4), to);   // 'hasta' inclusivo por día = exclusivo a medianoche local del día siguiente
        Assert.Equal(DateTimeKind.Utc, from!.Value.Kind);
        Assert.Equal(((DateTime?)null, (DateTime?)null), LocalDay.UtcRange(null, null, Pr));
        var (onlyFrom, noTo) = LocalDay.UtcRange(new DateOnly(2026, 9, 1), null, Pr);
        Assert.Equal(Utc(2026, 9, 1, 4), onlyFrom);
        Assert.Null(noTo);
    }

    [Fact]
    public void A_movement_at_utc_midnight_belongs_to_the_previous_local_day()
    {
        var clock = new TenantClock(Pr, () => Utc(2026, 10, 1, 1));
        Assert.Equal(new DateOnly(2026, 9, 30), clock.DayOf(Utc(2026, 10, 1, 0, 0, 0)));
        Assert.Equal(new DateOnly(2026, 9, 30), clock.DayOf(DateTime.SpecifyKind(new DateTime(2026, 10, 1, 3, 0, 0), DateTimeKind.Unspecified)));   // de la base: Unspecified = UTC
        var (from, to) = clock.UtcRange(clock.Today, clock.Today);
        Assert.True(Utc(2026, 10, 1, 0) >= from && Utc(2026, 10, 1, 0) < to);
        Assert.False(Utc(2026, 10, 1, 4) < to);
    }

    [Fact]
    public void Zone_resolution_accepts_iana_windows_or_falls_back()
    {
        foreach (var id in new[] { "America/Puerto_Rico", "SA Western Standard Time", null, "", "Zona/Inexistente" })
        {
            var tz = LocalDay.ResolveZone(id);
            Assert.NotNull(tz);
            Assert.Equal(TimeSpan.FromHours(-4), tz.GetUtcOffset(Utc(2026, 9, 30, 12)));
        }
        // Una zona con horario de verano también funciona (preparado para configurarlo por compañía).
        var ny = TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out var iana) ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Equal(ny.Id, LocalDay.ResolveZone(ny.Id).Id);
        Assert.Equal(Utc(2026, 7, 1, 4), LocalDay.StartOfDayUtc(new DateOnly(2026, 7, 1), ny));
        Assert.Equal(Utc(2026, 1, 15, 5), LocalDay.StartOfDayUtc(new DateOnly(2026, 1, 15), ny));
    }

    [Fact]
    public void Default_clock_uses_the_default_zone_and_the_real_time()
    {
        Assert.Same(LocalDay.DefaultZone, TenantClock.Default.Zone);
        var before = DateTime.UtcNow.AddSeconds(-5);
        Assert.InRange(TenantClock.Default.UtcNow, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(LocalDay.Today(TenantClock.Default.UtcNow, LocalDay.DefaultZone), TenantClock.Default.Today);
    }
}
