using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 4 (P4): evaluación del preventivo (Al día / Por vencer / Vencido / Sin historial) y regla de cierre de una OT.</summary>
public class MaintenanceDueTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    // ---------------------------------------------------------------- kilometraje

    [Theory]
    [InlineData(1000, "OK", 4000)]
    [InlineData(4600, "DUE_SOON", 400)]
    [InlineData(4500, "DUE_SOON", 500)]
    [InlineData(4499, "OK", 501)]
    [InlineData(5000, "OVERDUE", 0)]
    [InlineData(5200, "OVERDUE", -200)]
    public void Mileage_5000_from_zero(int odometer, string expected, int remaining)
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Mileage, 5000m, null, 0m, null, odometer, Today);
        Assert.Equal(expected, r.State);
        Assert.Equal(5000m, r.NextDueKm);
        Assert.Equal((decimal)remaining, r.KmRemaining);
        Assert.Null(r.NextDueDate);
        Assert.Null(r.DaysRemaining);
    }

    [Fact]
    public void Mileage_next_due_is_last_service_plus_interval()
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Mileage, 5000m, null, 5200m, null, 6000m, Today);
        Assert.Equal(10200m, r.NextDueKm);
        Assert.Equal(4200m, r.KmRemaining);
        Assert.Equal(MaintenanceDueStates.Ok, r.State);
    }

    [Fact]
    public void Mileage_without_last_service_is_no_baseline()
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Mileage, 5000m, null, null, Today.AddDays(-5), 1000m, Today);
        Assert.Equal(MaintenanceDueStates.NoBaseline, r.State);
        Assert.Null(r.NextDueKm);
    }

    [Fact]
    public void Mileage_without_current_odometer_is_no_baseline()
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Mileage, 5000m, null, 0m, null, null, Today);
        Assert.Equal(MaintenanceDueStates.NoBaseline, r.State);
        Assert.Equal(5000m, r.NextDueKm);
        Assert.Null(r.KmRemaining);
    }

    // ---------------------------------------------------------------- tiempo

    [Theory]
    [InlineData(10, "OK", 20)]
    [InlineData(27, "DUE_SOON", 3)]
    [InlineData(28, "DUE_SOON", 2)]
    [InlineData(30, "OVERDUE", 0)]
    [InlineData(40, "OVERDUE", -10)]
    public void Time_30_days(int daysAgo, string expected, int remaining)
    {
        var last = Today.AddDays(-daysAgo);
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Time, null, 30, null, last, null, Today);
        Assert.Equal(expected, r.State);
        Assert.Equal(last.AddDays(30), r.NextDueDate);
        Assert.Equal(remaining, r.DaysRemaining);
        Assert.Null(r.NextDueKm);
    }

    [Fact]
    public void Time_without_last_service_is_no_baseline_even_with_odometer()
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Time, null, 30, 1000m, null, 2000m, Today);
        Assert.Equal(MaintenanceDueStates.NoBaseline, r.State);
    }

    [Fact]
    public void Time_does_not_need_odometer()
    {
        var r = MaintenanceDue.Evaluate(MaintenanceTriggers.Time, null, 30, null, Today.AddDays(-1), null, Today);
        Assert.Equal(MaintenanceDueStates.Ok, r.State);
    }

    // ---------------------------------------------------------------- ambos

    [Fact]
    public void Both_takes_the_worst_dimension()
    {
        // km al día (4000 restantes) y tiempo vencido (hace 40 días de 30) → OVERDUE
        var overdue = MaintenanceDue.Evaluate(MaintenanceTriggers.Both, 5000m, 30, 0m, Today.AddDays(-40), 1000m, Today);
        Assert.Equal(MaintenanceDueStates.Overdue, overdue.State);
        Assert.Equal(4000m, overdue.KmRemaining);
        Assert.Equal(-10, overdue.DaysRemaining);

        // km por vencer (400 restantes) y tiempo al día → DUE_SOON
        var soon = MaintenanceDue.Evaluate(MaintenanceTriggers.Both, 5000m, 30, 0m, Today.AddDays(-5), 4600m, Today);
        Assert.Equal(MaintenanceDueStates.DueSoon, soon.State);

        // ambos al día → OK
        var ok = MaintenanceDue.Evaluate(MaintenanceTriggers.Both, 5000m, 30, 0m, Today.AddDays(-5), 1000m, Today);
        Assert.Equal(MaintenanceDueStates.Ok, ok.State);
    }

    [Fact]
    public void Both_with_one_dimension_missing_is_no_baseline_unless_the_other_is_worse()
    {
        var noDate = MaintenanceDue.Evaluate(MaintenanceTriggers.Both, 5000m, 30, 0m, null, 1000m, Today);
        Assert.Equal(MaintenanceDueStates.NoBaseline, noDate.State);

        var overdueKm = MaintenanceDue.Evaluate(MaintenanceTriggers.Both, 5000m, 30, 0m, null, 5200m, Today);
        Assert.Equal(MaintenanceDueStates.Overdue, overdueKm.State);
    }

    [Fact]
    public void Unknown_trigger_is_no_baseline()
    {
        var r = MaintenanceDue.Evaluate("FOO", 5000m, 30, 0m, Today, 1000m, Today);
        Assert.Equal(MaintenanceDueStates.NoBaseline, r.State);
    }

    [Fact]
    public void Severity_orders_overdue_first()
    {
        Assert.True(MaintenanceDue.Severity(MaintenanceDueStates.Overdue) > MaintenanceDue.Severity(MaintenanceDueStates.DueSoon));
        Assert.True(MaintenanceDue.Severity(MaintenanceDueStates.DueSoon) > MaintenanceDue.Severity(MaintenanceDueStates.NoBaseline));
        Assert.True(MaintenanceDue.Severity(MaintenanceDueStates.NoBaseline) > MaintenanceDue.Severity(MaintenanceDueStates.Ok));
    }

    // ---------------------------------------------------------------- cierre de OT

    [Fact]
    public void ValidateClose_with_incomplete_tasks_is_422_with_exact_message()
    {
        var r = MaintenanceDue.ValidateClose(2, MaintenanceTriggers.Mileage, 5200m);
        Assert.NotNull(r);
        Assert.Equal(422, r!.Value.Status);
        Assert.Equal("La orden tiene 2 tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.", r.Value.Message);
    }

    [Theory]
    [InlineData("MILEAGE")]
    [InlineData("BOTH")]
    public void ValidateClose_mileage_schedule_without_odometer_is_400(string trigger)
    {
        var r = MaintenanceDue.ValidateClose(0, trigger, null);
        Assert.NotNull(r);
        Assert.Equal(400, r!.Value.Status);
        Assert.Equal("Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.", r.Value.Message);
    }

    [Fact]
    public void ValidateClose_time_schedule_or_no_schedule_closes_without_odometer()
    {
        Assert.Null(MaintenanceDue.ValidateClose(0, MaintenanceTriggers.Time, null));
        Assert.Null(MaintenanceDue.ValidateClose(0, null, null));
        Assert.Null(MaintenanceDue.ValidateClose(0, MaintenanceTriggers.Mileage, 5200m));
    }

    // ---------------------------------------------------------------- coherencia del programa

    [Fact]
    public void ValidateSchedule_requires_interval_for_the_trigger()
    {
        var mileage = MaintenanceDue.ValidateSchedule(MaintenanceTriggers.Mileage, null, 30, null, null, Today);
        Assert.Contains(("intervalKm", "Un programa por kilometraje exige un intervalo en km mayor que 0."), mileage);

        var time = MaintenanceDue.ValidateSchedule(MaintenanceTriggers.Time, 5000m, 0, null, null, Today);
        Assert.Contains(("intervalDays", "Un programa por tiempo exige un intervalo en días mayor que 0."), time);

        var both = MaintenanceDue.ValidateSchedule(MaintenanceTriggers.Both, null, null, null, null, Today);
        Assert.Equal(2, both.Count);

        Assert.Empty(MaintenanceDue.ValidateSchedule(MaintenanceTriggers.Both, 5000m, 30, 0m, Today, Today));
    }

    [Fact]
    public void ValidateSchedule_rejects_non_positive_extra_interval_negative_km_and_future_date()
    {
        var errors = MaintenanceDue.ValidateSchedule(MaintenanceTriggers.Mileage, 5000m, 0, -1m, Today.AddDays(1), Today);
        Assert.Contains(("intervalDays", "El intervalo en días debe ser mayor que 0."), errors);
        Assert.Contains(("lastServiceKm", "La lectura del último servicio no puede ser negativa."), errors);
        Assert.Contains(("lastServiceDate", "La fecha del último servicio no puede ser futura."), errors);
    }
}
