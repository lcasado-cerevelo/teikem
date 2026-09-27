using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 6 (P9) — reglas puras de las citas de muelle (R19b, D30), con los mensajes exactos del manual.</summary>
public class DockScheduleRulesTests
{
    private static readonly DateTime Nine = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Messages_are_exact()
    {
        Assert.Equal("El muelle ya tiene una cita que se solapa con ese horario.", DockScheduleRules.Overlap);
        Assert.Equal("El fin de la cita debe ser posterior a su inicio.", DockScheduleRules.Window);
        Assert.Equal("La cita debe agendarse entre ayer y los próximos 90 días.", DockScheduleRules.Horizon);
        Assert.Equal("Una cita se enlaza a un aviso de llegada o a un viaje, no a ambos.", DockScheduleRules.AsnOrTrip);
        Assert.Equal("El muelle es de tipo INBOUND; no admite citas de OUTBOUND.", DockScheduleRules.Incompatible("INBOUND", "OUTBOUND"));
    }

    [Fact]
    public void Overlap_is_half_open()
    {
        // 09:00-10:00 contra 10:00-11:00: se tocan pero no se solapan.
        Assert.False(DockScheduleRules.Overlaps(Nine, Nine.AddHours(1), Nine.AddHours(1), Nine.AddHours(2)));
        Assert.False(DockScheduleRules.Overlaps(Nine.AddHours(1), Nine.AddHours(2), Nine, Nine.AddHours(1)));
        // 09:00-10:00 contra 09:30-10:30: sí.
        Assert.True(DockScheduleRules.Overlaps(Nine, Nine.AddHours(1), Nine.AddMinutes(30), Nine.AddMinutes(90)));
        // Contenida.
        Assert.True(DockScheduleRules.Overlaps(Nine, Nine.AddHours(3), Nine.AddHours(1), Nine.AddHours(2)));
        // Idéntica.
        Assert.True(DockScheduleRules.Overlaps(Nine, Nine.AddHours(1), Nine, Nine.AddHours(1)));
    }

    [Fact]
    public void Appointment_without_end_lasts_sixty_minutes()
    {
        Assert.Equal(Nine.AddMinutes(60), DockScheduleRules.EffectiveEnd(Nine, null));
        Assert.Equal(Nine.AddMinutes(15), DockScheduleRules.EffectiveEnd(Nine, Nine.AddMinutes(15)));
        // Sin fin (09:00-10:00) contra 09:59: se solapa; contra 10:00: no.
        Assert.True(DockScheduleRules.Overlaps(Nine, null, Nine.AddMinutes(59), null));
        Assert.False(DockScheduleRules.Overlaps(Nine, null, Nine.AddMinutes(60), null));
        Assert.True(DockScheduleRules.OverlapsAny(Nine.AddMinutes(30), null, new[] { (Nine, (DateTime?)Nine.AddHours(1)) }));
        Assert.False(DockScheduleRules.OverlapsAny(Nine.AddHours(2), null, new[] { (Nine, (DateTime?)Nine.AddHours(1)) }));
    }

    [Theory]
    [InlineData("INBOUND", "INBOUND", true)]
    [InlineData("INBOUND", "OUTBOUND", false)]
    [InlineData("OUTBOUND", "OUTBOUND", true)]
    [InlineData("OUTBOUND", "INBOUND", false)]
    [InlineData("BOTH", "INBOUND", true)]
    [InlineData("BOTH", "OUTBOUND", true)]
    [InlineData("both", "outbound", true)]
    [InlineData("BOTH", "SIDEWAYS", false)]
    [InlineData("OTHER", "INBOUND", false)]
    public void Compatibility_by_dock_type(string dockType, string direction, bool expected)
        => Assert.Equal(expected, DockScheduleRules.Compatible(dockType, direction));

    [Fact]
    public void Window_requires_end_after_start()
    {
        var now = Nine;
        Assert.Null(DockScheduleRules.ValidateWindow(Nine.AddHours(1), Nine.AddHours(2), now));
        Assert.Null(DockScheduleRules.ValidateWindow(Nine.AddHours(1), null, now));
        Assert.Equal(DockScheduleRules.Window, DockScheduleRules.ValidateWindow(Nine.AddHours(2), Nine.AddHours(2), now));
        Assert.Equal(DockScheduleRules.Window, DockScheduleRules.ValidateWindow(Nine.AddHours(2), Nine.AddHours(1), now));
    }

    [Fact]
    public void Horizon_goes_from_yesterday_to_ninety_days()
    {
        var now = Nine;
        var yesterdayMidnight = now.Date.AddDays(-1);
        Assert.Null(DockScheduleRules.ValidateWindow(yesterdayMidnight, null, now));
        Assert.Equal(DockScheduleRules.Horizon, DockScheduleRules.ValidateWindow(yesterdayMidnight.AddMinutes(-1), null, now));
        Assert.Null(DockScheduleRules.ValidateWindow(now.AddDays(90), null, now));
        Assert.Equal(DockScheduleRules.Horizon, DockScheduleRules.ValidateWindow(now.AddDays(90).AddMinutes(1), null, now));
    }

    [Fact]
    public void Asn_or_trip_never_both_and_asn_is_inbound()
    {
        Assert.Null(DockScheduleRules.ValidateReferences(false, false, DockDirections.Outbound));
        Assert.Null(DockScheduleRules.ValidateReferences(true, false, DockDirections.Inbound));
        Assert.Null(DockScheduleRules.ValidateReferences(false, true, DockDirections.Outbound));
        Assert.Equal(DockScheduleRules.AsnOrTrip, DockScheduleRules.ValidateReferences(true, true, DockDirections.Inbound));
        Assert.Equal(DockScheduleRules.AsnInboundOnly, DockScheduleRules.ValidateReferences(true, false, DockDirections.Outbound));
    }

    [Fact]
    public void Normalization_and_dock_occupancy()
    {
        Assert.Equal(DockDirections.Inbound, DockScheduleRules.NormalizeDirection(" inbound "));
        Assert.Null(DockScheduleRules.NormalizeDirection("BOTH"));
        Assert.Equal(AppointmentStatuses.NoShow, DockScheduleRules.NormalizeStatus("no_show"));
        Assert.Null(DockScheduleRules.NormalizeStatus("LOST"));

        Assert.True(DockScheduleRules.OccupiesDock(AppointmentStatuses.Arrived));
        Assert.False(DockScheduleRules.OccupiesDock(AppointmentStatuses.Scheduled));
        Assert.True(DockScheduleRules.ReleasesDock(AppointmentStatuses.Completed));
        Assert.True(DockScheduleRules.ReleasesDock(AppointmentStatuses.NoShow));
        Assert.True(DockScheduleRules.ReleasesDock(AppointmentStatuses.Cancelled));
        Assert.False(DockScheduleRules.ReleasesDock(AppointmentStatuses.Arrived));
        Assert.True(DockScheduleRules.IsActive(AppointmentStatuses.Scheduled));
        Assert.True(DockScheduleRules.IsActive(AppointmentStatuses.Arrived));
        Assert.False(DockScheduleRules.IsActive(AppointmentStatuses.Completed));
    }

    [Fact]
    public void Unspecified_dates_are_taken_as_utc()
    {
        var unspecified = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var utc = DockScheduleRules.AsUtc(unspecified);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(unspecified.Ticks, utc.Ticks);
        Assert.Equal(Nine, DockScheduleRules.AsUtc(Nine));
    }
}
