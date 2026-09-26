using Teikem.Domain.Constants;

namespace Teikem.Domain.Fleet;

/// <summary>Resultado de evaluar un programa preventivo sobre un vehículo (panel Mantenimiento preventivo).</summary>
public sealed record MaintenanceDueResult(decimal? NextDueKm, DateOnly? NextDueDate, decimal? KmRemaining, int? DaysRemaining, string State);

/// <summary>
/// Lote 4 (P4): reglas puras del mantenimiento preventivo y del cierre de una orden de trabajo. Sin BD.
/// - Evaluate: MILEAGE usa km, TIME usa días y BOTH toma el peor. OVERDUE si lo restante ≤ 0; DUE_SOON si queda el 10 %
///   o menos del intervalo; OK en otro caso. NO_BASELINE si falta el último servicio de la dimensión requerida (o el
///   odómetro actual en una dimensión por km).
/// - ValidateClose: cerrar exige tareas activas completas (422) y odómetro si el programa es por kilometraje (400).
/// - ValidateSchedule: coherencia del programa (nombre, intervalo por disparador, último servicio).
/// </summary>
public static class MaintenanceDue
{
    /// <summary>Umbral de "Por vencer": fracción del intervalo que queda (10 %).</summary>
    public const decimal DueSoonFraction = 0.10m;

    public const int NameMaxLength = 150;
    public const int MaxIntervalDays = 36500;
    public const int TaskDescriptionMaxLength = 250;

    public const string NameRequiredMessage = "El nombre del programa es obligatorio.";
    public const string NameTooLongMessage = "El nombre del programa admite como máximo 150 caracteres.";
    public const string MileageIntervalMessage = "Un programa por kilometraje exige un intervalo en km mayor que 0.";
    public const string TimeIntervalMessage = "Un programa por tiempo exige un intervalo en días mayor que 0.";
    public const string IntervalKmPositiveMessage = "El intervalo en km debe ser mayor que 0.";
    public const string IntervalDaysPositiveMessage = "El intervalo en días debe ser mayor que 0.";
    public const string IntervalDaysTooLargeMessage = "El intervalo en días admite como máximo 36500.";
    public const string LastServiceKmNegativeMessage = "La lectura del último servicio no puede ser negativa.";
    public const string LastServiceDateFutureMessage = "La fecha del último servicio no puede ser futura.";
    public const string IncompleteTasksMessageFormat = "La orden tiene {0} tarea(s) sin completar; márquelas como completadas o quítelas antes de cerrarla.";
    public const string CloseOdometerRequiredMessage = "Indique la lectura de odómetro para cerrar una orden de un programa por kilometraje.";

    /// <summary>¿El disparador exige kilometraje (MILEAGE o BOTH)?</summary>
    public static bool UsesKm(string? triggerCode)
        => string.Equals(triggerCode, MaintenanceTriggers.Mileage, StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggerCode, MaintenanceTriggers.Both, StringComparison.OrdinalIgnoreCase);

    /// <summary>¿El disparador exige tiempo (TIME o BOTH)?</summary>
    public static bool UsesDays(string? triggerCode)
        => string.Equals(triggerCode, MaintenanceTriggers.Time, StringComparison.OrdinalIgnoreCase)
           || string.Equals(triggerCode, MaintenanceTriggers.Both, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnownTrigger(string? triggerCode) => UsesKm(triggerCode) || UsesDays(triggerCode);

    /// <summary>
    /// Evalúa un programa sobre un vehículo. Con BOTH se evalúan las dos dimensiones y gana la peor
    /// (OVERDUE &gt; DUE_SOON &gt; NO_BASELINE &gt; OK): un kilometraje vencido no se esconde porque falte la fecha.
    /// Un disparador desconocido o un intervalo no positivo se tratan como NO_BASELINE (no hay con qué calcular).
    /// </summary>
    public static MaintenanceDueResult Evaluate(string triggerCode, decimal? intervalKm, int? intervalDays,
        decimal? lastServiceKm, DateOnly? lastServiceDate, decimal? currentOdometerKm, DateOnly today)
    {
        var useKm = UsesKm(triggerCode);
        var useDays = UsesDays(triggerCode);
        if (!useKm && !useDays) return new MaintenanceDueResult(null, null, null, null, MaintenanceDueStates.NoBaseline);

        decimal? nextKm = null, kmRemaining = null;
        DateOnly? nextDate = null;
        int? daysRemaining = null;
        var states = new List<string>(2);

        if (useKm)
        {
            if (intervalKm is decimal ik && ik > 0 && lastServiceKm is decimal lk)
            {
                nextKm = lk + ik;
                if (currentOdometerKm is decimal current)
                {
                    kmRemaining = nextKm.Value - current;
                    states.Add(StateFor(kmRemaining.Value, ik));
                }
                else states.Add(MaintenanceDueStates.NoBaseline);
            }
            else states.Add(MaintenanceDueStates.NoBaseline);
        }

        if (useDays)
        {
            if (intervalDays is int id && id > 0 && lastServiceDate is DateOnly ld
                && (long)ld.DayNumber + id <= DateOnly.MaxValue.DayNumber)
            {
                nextDate = ld.AddDays(id);
                daysRemaining = nextDate.Value.DayNumber - today.DayNumber;
                states.Add(StateFor(daysRemaining.Value, id));
            }
            else states.Add(MaintenanceDueStates.NoBaseline);
        }

        var worst = states.OrderByDescending(Severity).First();
        return new MaintenanceDueResult(nextKm, nextDate, kmRemaining, daysRemaining, worst);
    }

    /// <summary>Gravedad para ordenar el panel y combinar dimensiones: OVERDUE 3, DUE_SOON 2, NO_BASELINE 1, OK 0.</summary>
    public static int Severity(string state) => state switch
    {
        MaintenanceDueStates.Overdue => 3,
        MaintenanceDueStates.DueSoon => 2,
        MaintenanceDueStates.NoBaseline => 1,
        _ => 0,
    };

    public static bool IsKnownState(string? state)
        => state is MaintenanceDueStates.Ok or MaintenanceDueStates.DueSoon or MaintenanceDueStates.Overdue or MaintenanceDueStates.NoBaseline;

    private static string StateFor(decimal remaining, decimal interval)
    {
        if (remaining <= 0) return MaintenanceDueStates.Overdue;
        if (remaining <= interval * DueSoonFraction) return MaintenanceDueStates.DueSoon;
        return MaintenanceDueStates.Ok;
    }

    /// <summary>
    /// Regla de cierre de una OT: tareas activas sin completar → (422, mensaje); programa MILEAGE/BOTH sin lectura de
    /// odómetro → (400, mensaje); null si se puede cerrar. Una OT sin programa o de un programa por tiempo cierra sin odómetro.
    /// </summary>
    public static (int Status, string Message)? ValidateClose(int incompleteTasks, string? scheduleTriggerCode, decimal? odometerKm)
    {
        if (incompleteTasks > 0) return (422, string.Format(IncompleteTasksMessageFormat, incompleteTasks));
        if (UsesKm(scheduleTriggerCode) && odometerKm is null) return (400, CloseOdometerRequiredMessage);
        return null;
    }

    /// <summary>
    /// Coherencia del intervalo según el disparador (MILEAGE exige km &gt; 0, TIME exige días &gt; 0, BOTH ambos) y de los
    /// valores presentes (un intervalo capturado siempre es &gt; 0, como exige CK_MaintSchedule_Interval), más el último
    /// servicio (lectura ≥ 0, fecha no futura). Devuelve (campo, mensaje) por cada error. El disparador ya viene validado.
    /// </summary>
    public static IReadOnlyList<(string Field, string Message)> ValidateSchedule(string triggerCode, decimal? intervalKm, int? intervalDays,
        decimal? lastServiceKm, DateOnly? lastServiceDate, DateOnly today)
    {
        var errors = new List<(string, string)>();
        if (UsesKm(triggerCode) && (intervalKm is null || intervalKm <= 0)) errors.Add(("intervalKm", MileageIntervalMessage));
        else if (intervalKm is decimal km && km <= 0) errors.Add(("intervalKm", IntervalKmPositiveMessage));

        if (UsesDays(triggerCode) && (intervalDays is null || intervalDays <= 0)) errors.Add(("intervalDays", TimeIntervalMessage));
        else if (intervalDays is int d && d <= 0) errors.Add(("intervalDays", IntervalDaysPositiveMessage));
        else if (intervalDays is int big && big > MaxIntervalDays) errors.Add(("intervalDays", IntervalDaysTooLargeMessage));

        if (lastServiceKm is decimal lk && lk < 0) errors.Add(("lastServiceKm", LastServiceKmNegativeMessage));
        if (lastServiceDate is DateOnly ld && ld > today) errors.Add(("lastServiceDate", LastServiceDateFutureMessage));
        return errors;
    }
}
